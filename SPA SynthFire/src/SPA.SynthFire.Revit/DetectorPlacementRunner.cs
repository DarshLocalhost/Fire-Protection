using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SPA.SynthFire.Core.Catalog;
using SPA.SynthFire.Core.Devices;
using SPA.SynthFire.Core.Settings;
using SPA.SynthFire.Core.Standards;
using SPA.SynthFire.Placement;
using SPA.SynthFire.Placement.Preview;
using SPA.SynthFire.UI.Controls;
using SPA.SynthFire.UI.Views;

namespace SPA.SynthFire.Revit;

/// <summary>
/// Runs detector placement from the modeless detection window via an ExternalEvent,
/// so Revit stays interactive while the window is open. The coverage plan is opened
/// MODELESS so its edit clicks (add / remove / undo / redo) can raise this same event
/// again (a modal dialog would block Revit's message loop). Manual placements are the
/// source of truth: each edit mutates an ordered list of manual heads and rebuilds the
/// affected room from its base snapshot.
/// </summary>
public sealed class DetectorPlacementRunner : IDetectorRunner, ICoveragePlanInteraction
{
    private enum Mode { Place, Plan, PlaceOne, RemoveOne, Undo, Redo }

    private readonly ExternalEvent _event;
    private readonly Handler _handler;

    public DetectorPlacementRunner(DetectorDesignWindow window)
    {
        _handler = new Handler(window, this);
        _event = ExternalEvent.Create(_handler);
    }

    public void RequestPlace(string familySymbolId, DetectorKind kind, bool clearPrevious, bool previewOnly,
        DeviceMount mount = DeviceMount.Ceiling)
    {
        _handler.FamilyId = familySymbolId;
        _handler.Kind = kind;
        _handler.ClearPrevious = clearPrevious;
        _handler.PreviewOnly = previewOnly;
        _handler.Mount = mount;
        _handler.Op = Mode.Place;
        _event.Raise();
    }

    public void RequestPlan(DetectorKind kind, DeviceMount mount = DeviceMount.Ceiling, string? familySymbolId = null)
    {
        _handler.FamilyId = familySymbolId;
        _handler.Kind = kind;
        _handler.Mount = mount;
        _handler.Op = Mode.Plan;
        _event.Raise();
    }

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

    private sealed class Handler : IExternalEventHandler
    {
        private readonly DetectorDesignWindow _window;
        private readonly ICoveragePlanInteraction _interaction;
        private CoveragePlanWindow? _planWindow;

        private readonly List<ManualHead> _active = new();
        private readonly Stack<ManualHead> _redo = new();
        private readonly Dictionary<string, RoomPreview> _baseRooms = new();

        public string? FamilyId;
        public DetectorKind Kind;
        public bool ClearPrevious;
        public bool PreviewOnly;
        public DeviceMount Mount;
        public Mode Op;
        public string? EditRoomId;
        public double EditX, EditY;

        public Handler(DetectorDesignWindow window, ICoveragePlanInteraction interaction)
        {
            _window = window;
            _interaction = interaction;
        }

        public string GetName() => "SPA SynthFire — Place Detectors";

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

                var settings = SettingsStore.Load();
                double mountingHeightFt = settings.NotificationMountingHeightInches / 12.0;
                CeilingTile? tile = CeilingTilePlanner.FromInches(
                    settings.SnapToCeilingTile,
                    settings.CeilingTileModuleXInches, settings.CeilingTileModuleYInches,
                    settings.CeilingTileOriginXInches, settings.CeilingTileOriginYInches);

                var read = RoomReader.ReadRooms(doc, tile);
                if (read.Rooms.Count == 0) { _window.SetStatus("No placed rooms were found."); return; }

                SprinklerRuleSet rules = Nfpa72DetectionRules.For(Kind);
                PlacementResult result = new SprinklerPlacementSolver().Solve(read.Rooms, rules, Mount);

                if (Op == Mode.Plan)
                {
                    _planWindow = CoveragePlanPresenter.ShowInteractive(
                        result, app.MainWindowHandle, Kind.ToString().ToLowerInvariant(), _window.SetStatus, _interaction);

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

                if (FamilyId is null) { _window.SetStatus("Select a detector family/type."); return; }

                if (PreviewOnly)
                {
                    _window.SetStatus(
                        $"Preview — {Kind} detectors: {result.TotalSprinklers} across {read.Rooms.Count} room(s), " +
                        $"{CoverageValidator.GapCount(result)} coverage gap(s). Nothing was written.");
                    PlacementReportWindow.ShowReport(result, app.MainWindowHandle, $"{Kind} detectors (preview)");
                    return;
                }

                DetectorPlacementOutcome outcome = DetectorPlacer.Place(
                    doc, result, read.RoomLevels, FamilyId, ClearPrevious, mountingHeightFt, settings.NotificationCandela);

                if (!outcome.Succeeded) { _window.SetStatus(outcome.Error ?? "Placement failed."); return; }

                _window.SetStatus(
                    $"Done — {Kind} detectors: {outcome.Placed} placed ({outcome.Mount}), " +
                    $"{outcome.HostedToCeiling} ceiling, {outcome.HostedToWall} wall, " +
                    (outcome.Cleared > 0 ? $"{outcome.Cleared} cleared, " : "") +
                    $"{outcome.Failed} failed.");

                PlacementReportWindow.ShowReport(result, app.MainWindowHandle, $"{Kind} detectors");
            }
            catch (Exception ex)
            {
                _window.SetStatus("Error: " + ex.Message);
            }
        }

        private void ExecutePlaceOne(Document doc)
        {
            if (FamilyId is null)
            { _window.SetStatus("Select a detector family/type before placing on the plan."); return; }
            if (EditRoomId is null) return;

            var settings = SettingsStore.Load();
            double mountingHeightFt = settings.NotificationMountingHeightInches / 12.0;

            var read = RoomReader.ReadRooms(doc);
            bool ok = DetectorPlacer.PlaceOne(
                doc, FamilyId, read.RoomLevels, EditRoomId, EditX, EditY, Mount, mountingHeightFt,
                out long placedId, out string msg);
            if (!ok) { _window.SetStatus(msg); return; }

            _active.Add(new ManualHead(EditRoomId, EditX, EditY, Mount, Kind.ToString().ToLowerInvariant(), placedId,
                _planWindow?.FindRoom(EditRoomId)?.CeilingElevation ?? 0));
            _redo.Clear();
            RebuildAndRefresh(EditRoomId);
            _window.SetStatus($"Added 1 {Kind} detector at the clicked point. Review required.");
        }

        private void ExecuteRemoveOne(Document doc)
        {
            if (EditRoomId is null) return;
            ManualHead? head = NearestActive(EditRoomId, EditX, EditY);
            if (head is null) { _window.SetStatus("Only manually placed devices can be removed."); return; }

            if (!DetectorPlacer.RemoveOne(doc, head.ElementId, out string msg))
            { _window.SetStatus(msg); return; }

            _active.Remove(head);
            _redo.Clear();
            RebuildAndRefresh(head.RoomId);
            _window.SetStatus("Removed 1 manual detector. Review required.");
        }

        private void ExecuteUndo(Document doc)
        {
            if (_active.Count == 0) { _window.SetStatus("Nothing to undo."); return; }
            ManualHead head = _active[^1];
            if (!DetectorPlacer.RemoveOne(doc, head.ElementId, out string msg))
            { _window.SetStatus(msg); return; }

            _active.RemoveAt(_active.Count - 1);
            _redo.Push(head);
            RebuildAndRefresh(head.RoomId);
            _window.SetStatus("Undid last placement.");
        }

        private void ExecuteRedo(Document doc)
        {
            if (_redo.Count == 0) { _window.SetStatus("Nothing to redo."); return; }
            if (FamilyId is null)
            { _window.SetStatus("Select a detector family/type before redo."); return; }

            var settings = SettingsStore.Load();
            double mountingHeightFt = settings.NotificationMountingHeightInches / 12.0;

            ManualHead head = _redo.Peek();
            var read = RoomReader.ReadRooms(doc);
            bool ok = DetectorPlacer.PlaceOne(
                doc, FamilyId, read.RoomLevels, head.RoomId, head.X, head.Y, head.Mount, mountingHeightFt,
                out long placedId, out string msg);
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