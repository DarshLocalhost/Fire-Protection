using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SPA.SynthFire.Core.Settings;
using SPA.SynthFire.Core.Standards;
using SPA.SynthFire.Placement;
using SPA.SynthFire.Placement.Preview;
using SPA.SynthFire.UI;
using SPA.SynthFire.UI.Controls;
using SPA.SynthFire.UI.Views;

namespace SPA.SynthFire.Revit;

/// <summary>
/// Runs sprinkler placement from the modeless design window without blocking Revit.
/// The window calls <see cref="RequestPlace"/>, which raises an ExternalEvent; the
/// handler then does the transaction inside a valid Revit API context and reports
/// status back to the window. The coverage plan is opened MODELESS so its edit clicks
/// (add / remove / undo / redo) can raise this same event again (a modal dialog would
/// block Revit). Manual placements are the source of truth: each edit mutates an ordered
/// list of manual heads and rebuilds the affected room from its base snapshot.
/// </summary>
public sealed class SprinklerPlacementRunner : IPlacementRunner, ICoveragePlanInteraction
{
    private enum Mode { Place, Plan, PlaceOne, RemoveOne, Undo, Redo }

    private readonly ExternalEvent _event;
    private readonly PlacementHandler _handler;

    public SprinklerPlacementRunner(SprinklerDesignWindow window)
    {
        _handler = new PlacementHandler(window, this);
        _event = ExternalEvent.Create(_handler);
    }

    public void RequestPlace(PlacementOptions options)
    {
        _handler.Pending = options;
        _handler.Op = Mode.Place;
        _event.Raise();
    }

    public void RequestPlan(PlacementOptions options)
    {
        _handler.Pending = options;
        _handler.Op = Mode.Plan;
        _event.Raise();
    }

    // --- ICoveragePlanInteraction (called on the UI thread from the coverage plan) ---

    public void PlaceAt(string roomId, double x, double y)
    {
        _handler.EditRoomId = roomId; _handler.EditX = x; _handler.EditY = y;
        _handler.Op = Mode.PlaceOne; _event.Raise();
    }

    public void RemoveNear(string roomId, double x, double y)
    {
        _handler.EditRoomId = roomId; _handler.EditX = x; _handler.EditY = y;
        _handler.Op = Mode.RemoveOne; _event.Raise();
    }

    public void Undo() { _handler.Op = Mode.Undo; _event.Raise(); }
    public void Redo() { _handler.Op = Mode.Redo; _event.Raise(); }

    private sealed class PlacementHandler : IExternalEventHandler
    {
        private readonly SprinklerDesignWindow _window;
        private readonly ICoveragePlanInteraction _interaction;
        private CoveragePlanWindow? _planWindow;

        // Manual-placement history for the open plan (source of truth for add/undo/redo/delete).
        private readonly List<ManualHead> _active = new();
        private readonly Stack<ManualHead> _redo = new();
        private readonly Dictionary<string, RoomPreview> _baseRooms = new();

        public PlacementOptions? Pending;
        public Mode Op;
        public string? EditRoomId;
        public double EditX, EditY;

        public PlacementHandler(SprinklerDesignWindow window, ICoveragePlanInteraction interaction)
        {
            _window = window;
            _interaction = interaction;
        }

        public string GetName() => "SPA SynthFire — Place Sprinklers";

        public void Execute(UIApplication app)
        {
            try
            {
                UIDocument uiDoc = app.ActiveUIDocument;
                if (uiDoc is null) { _window.SetStatus("Open a project first."); return; }
                Document doc = uiDoc.Document;

                switch (Op)
                {
                    case Mode.PlaceOne: ExecutePlaceOne(doc); return;
                    case Mode.RemoveOne: ExecuteRemoveOne(doc); return;
                    case Mode.Undo: ExecuteUndo(doc); return;
                    case Mode.Redo: ExecuteRedo(doc); return;
                }

                PlacementOptions? options = Pending;
                if (options is null) return;

                var settings = SettingsStore.Load();
                CeilingTile? tile = CeilingTilePlanner.FromInches(
                    settings.SnapToCeilingTile,
                    settings.CeilingTileModuleXInches, settings.CeilingTileModuleYInches,
                    settings.CeilingTileOriginXInches, settings.CeilingTileOriginYInches);

                var read = RoomReader.ReadRooms(doc, tile);
                if (read.Rooms.Count == 0) { _window.SetStatus("No placed rooms were found."); return; }

                var registry = StandardRegistry.CreateDefault();

                SprinklerRuleSet defaultRules =
                    EffectiveRules.For(registry, settings, options.StandardId, options.HazardClassId);

                SprinklerRuleSet ResolveRules(RoomInput room)
                {
                    string? hazard = room.HazardClassId;
                    if (string.IsNullOrWhiteSpace(hazard) || hazard == options.HazardClassId)
                        return defaultRules;
                    try { return EffectiveRules.For(registry, settings, options.StandardId, hazard); }
                    catch { return defaultRules; }
                }

                PlacementResult result = new SprinklerPlacementSolver()
                    .Solve(read.Rooms, ResolveRules, options.StandardId, options.HazardClassId, options.Mount);

                if (Op == Mode.Plan)
                {
                    _planWindow = CoveragePlanPresenter.ShowInteractive(
                        result, app.MainWindowHandle, "sprinkler", _window.SetStatus, _interaction);

                    _active.Clear();
                    _redo.Clear();
                    _baseRooms.Clear();
                    if (_planWindow is not null)
                    {
                        foreach (var r in _planWindow.AllRooms) _baseRooms[r.RoomId] = r;
                        _planWindow.SetHistoryState(false, false);
                    }
                    return;
                }

                if (options.PreviewOnly)
                {
                    _window.SetStatus(
                        $"Preview — {result.TotalSprinklers} sprinklers across {read.Rooms.Count} room(s), " +
                        $"{CoverageValidator.GapCount(result)} coverage gap(s). Nothing was written. " +
                        (result.ReviewRequired ? "Review required." : ""));
                    PlacementReportWindow.ShowReport(result, app.MainWindowHandle, "sprinklers (preview)");
                    return;
                }

                SprinklerPlacementOutcome outcome = SprinklerPlacer.Place(
                    doc, result, read.RoomLevels, options.FamilySymbolId, defaultRules, _window.ClearPrevious);

                if (!outcome.Succeeded) { _window.SetStatus(outcome.Error ?? "Placement failed."); return; }

                settings.FamilyMap[$"{options.StandardId}|{options.HazardClassId}"] = options.FamilyLabel;
                SettingsStore.Save(settings);

                _window.SetStatus(
                    $"Done — {outcome.Placed} placed ({outcome.Mount}), " +
                    $"{outcome.HostedToCeiling} ceiling, {outcome.HostedToWall} wall, " +
                    (outcome.Cleared > 0 ? $"{outcome.Cleared} cleared, " : "") +
                    $"{outcome.Failed} failed. {(result.ReviewRequired ? "Review required." : "")}" +
                    (outcome.Note is { } note ? " " + note : ""));

                if (outcome.Mount == SprinklerMount.Sidewall && (outcome.Placed == 0 || outcome.Failed > 0))
                {
                    string diag =
                        $"Family: {options.FamilyLabel}\n" +
                        $"Revit placement type: {outcome.PlacementType}\n\n" +
                        $"Solver produced (attempted): {outcome.Attempted} head(s)\n" +
                        $"Placed: {outcome.Placed}  (wall-hosted {outcome.HostedToWall})\n" +
                        $"Failed: {outcome.Failed}  (host-required {outcome.HostRequiredFails})\n\n" +
                        (outcome.Attempted == 0
                            ? "The solver laid out NO sidewall points for these rooms."
                            : outcome.HostRequiredFails > 0
                                ? "This family MUST host to a real wall, but the walls here are in a linked model."
                                : outcome.Placed == 0
                                    ? "Heads were attempted but none landed."
                                    : "Some heads placed; some failed.");

                    if (outcome.SidewallDiag.Count > 0)
                        diag += "\n\nPer-head trace:\n• " + string.Join("\n• ", outcome.SidewallDiag);

                    TaskDialog.Show("SPA SynthFire — Sidewall placement", diag);
                }

                PlacementReportWindow.ShowReport(result, app.MainWindowHandle, "sprinklers");
            }
            catch (Exception ex)
            {
                _window.SetStatus("Error: " + ex.Message);
            }
        }

        private void ExecutePlaceOne(Document doc)
        {
            PlacementOptions? options = Pending;
            if (options?.FamilySymbolId is null)
            { _window.SetStatus("Select a sprinkler family/type before placing on the plan."); return; }
            if (EditRoomId is null) return;

            var read = RoomReader.ReadRooms(doc);
            bool ok = SprinklerPlacer.PlaceOne(
                doc, options.FamilySymbolId, read.RoomLevels, EditRoomId, EditX, EditY,
                options.Mount, out long placedId, out string msg);
            if (!ok) { _window.SetStatus(msg); return; }

            _active.Add(new ManualHead(EditRoomId, EditX, EditY, options.Mount, "sprinkler", placedId,
                _planWindow?.FindRoom(EditRoomId)?.CeilingElevation ?? 0));
            _redo.Clear();
            RebuildAndRefresh(EditRoomId);
            _window.SetStatus("Added 1 sprinkler at the clicked point. Review required.");
        }

        private void ExecuteRemoveOne(Document doc)
        {
            if (EditRoomId is null) return;
            ManualHead? head = NearestActive(EditRoomId, EditX, EditY);
            if (head is null) { _window.SetStatus("Only manually placed heads can be removed."); return; }

            if (!SprinklerPlacer.RemoveOne(doc, head.ElementId, out string msg))
            { _window.SetStatus(msg); return; }

            _active.Remove(head);
            _redo.Clear();
            RebuildAndRefresh(head.RoomId);
            _window.SetStatus("Removed 1 manual sprinkler. Review required.");
        }

        private void ExecuteUndo(Document doc)
        {
            if (_active.Count == 0) { _window.SetStatus("Nothing to undo."); return; }
            ManualHead head = _active[^1];
            if (!SprinklerPlacer.RemoveOne(doc, head.ElementId, out string msg))
            { _window.SetStatus(msg); return; }

            _active.RemoveAt(_active.Count - 1);
            _redo.Push(head);
            RebuildAndRefresh(head.RoomId);
            _window.SetStatus("Undid last placement.");
        }

        private void ExecuteRedo(Document doc)
        {
            if (_redo.Count == 0) { _window.SetStatus("Nothing to redo."); return; }
            PlacementOptions? options = Pending;
            if (options?.FamilySymbolId is null)
            { _window.SetStatus("Select a sprinkler family/type before redo."); return; }

            ManualHead head = _redo.Peek();
            var read = RoomReader.ReadRooms(doc);
            bool ok = SprinklerPlacer.PlaceOne(
                doc, options.FamilySymbolId, read.RoomLevels, head.RoomId, head.X, head.Y,
                head.Mount, out long placedId, out string msg);
            if (!ok) { _window.SetStatus(msg); return; }

            _redo.Pop();
            _active.Add(head with { ElementId = placedId });
            RebuildAndRefresh(head.RoomId);
            _window.SetStatus("Redid placement.");
        }

        private void RebuildAndRefresh(string roomId)
        {
            if (_planWindow is null) return;
            RoomPreview? baseRoom = _baseRooms.TryGetValue(roomId, out var b) ? b : _planWindow.FindRoom(roomId);
            if (baseRoom is not null)
            {
                RoomPreview rebuilt = baseRoom;
                foreach (var h in _active.Where(h => h.RoomId == roomId))
                    rebuilt = LevelPreviewBuilder.AddManualDevice(rebuilt, h.X, h.Y, h.Mount, h.Kind);
                _planWindow.RefreshRoom(rebuilt);
            }
            _planWindow.SetHistoryState(_active.Count > 0, _redo.Count > 0);
        }

        private ManualHead? NearestActive(string roomId, double x, double y)
        {
            ManualHead? best = null;
            double bestD2 = double.MaxValue;
            foreach (var h in _active.Where(h => h.RoomId == roomId))
            {
                double dx = h.X - x, dy = h.Y - y;
                double d2 = dx * dx + dy * dy;
                if (d2 < bestD2) { bestD2 = d2; best = h; }
            }
            return bestD2 <= 1.0 ? best : null;
        }
    }
}