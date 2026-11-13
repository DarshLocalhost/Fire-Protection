using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SPA.SynthFire.Core.Settings;
using SPA.SynthFire.Core.Standards;
using SPA.SynthFire.UI;
using SPA.SynthFire.UI.Views;

namespace SPA.SynthFire.Revit;

/// <summary>
/// Single home for opening each SPA SynthFire tool, so the ribbon buttons and the
/// dockable panel share ONE launch path (and ONE modeless-window instance per tool).
/// Every method must run inside a valid Revit API context (a command's Execute or an
/// ExternalEvent handler) because it reads the model before showing the window.
/// </summary>
public static class SynthFireLauncher
{
    // One live instance per modeless tool window, shared by the ribbon and the panel.
    private static SprinklerDesignWindow? _sprinklers;
    private static SprinklerPlacementRunner? _sprinklerRunner;
    private static DetectorDesignWindow? _detectors;
    private static DetectorPlacementRunner? _detectorRunner;
    private static RoomCreationWindow? _rooms;
    private static RoomCreationRunner? _roomRunner;
    private static HazardClassificationWindow? _hazard;
    private static HazardClassificationRunner? _hazardRunner;
    private static SettingsWindow? _settings;

    /// <summary>Runs the tool by kind, reporting any user-facing message via TaskDialog.</summary>
    public static Result Launch(SynthFireTool tool, UIApplication app, ref string message) => tool switch
    {
        SynthFireTool.Settings => LaunchSettings(app),
        SynthFireTool.RoomCreation => LaunchRoomCreation(app, ref message),
        SynthFireTool.Hazard => LaunchHazard(app, ref message),
        SynthFireTool.PlaceSprinklers => LaunchSprinklers(app, ref message),
        SynthFireTool.PlaceDetectors => LaunchDetectors(app, ref message),
        SynthFireTool.ClearDevices => ClearDevices(app, ref message),
        _ => Result.Cancelled
    };

    public static Result LaunchSprinklers(UIApplication app, ref string message)
    {
        UIDocument uiDoc = app.ActiveUIDocument;
        if (uiDoc is null) { message = "Open a project first."; return Result.Cancelled; }

        var families = SprinklerFamilyReader.ReadSprinklerTypes(uiDoc.Document);
        if (families.Count == 0)
        {
            TaskDialog.Show("SPA SynthFire",
                "No sprinkler family is loaded. Load a Sprinklers-category family and try again.");
            return Result.Cancelled;
        }
        if (_sprinklers is { IsLoaded: true }) { _sprinklers.Activate(); return Result.Succeeded; }

        var settings = SettingsStore.Load();
        var registry = StandardRegistry.CreateDefault();
        _sprinklers = new SprinklerDesignWindow(registry, families, settings.StandardId, settings.FamilyMap);
        _sprinklerRunner = new SprinklerPlacementRunner(_sprinklers);
        _sprinklers.Runner = _sprinklerRunner;
        _sprinklers.SetOwner(app.MainWindowHandle);
        _sprinklers.Closed += (_, _) => { _sprinklers = null; _sprinklerRunner = null; };
        _sprinklers.Show();
        return Result.Succeeded;
    }

    public static Result LaunchDetectors(UIApplication app, ref string message)
    {
        UIDocument uiDoc = app.ActiveUIDocument;
        if (uiDoc is null) { message = "Open a project first."; return Result.Cancelled; }

        var families = DetectorFamilyReader.ReadDetectorTypes(uiDoc.Document);
        if (families.Count == 0)
        {
            TaskDialog.Show("SPA SynthFire", "No fire-alarm/detector family is loaded. Load one and try again.");
            return Result.Cancelled;
        }
        if (_detectors is { IsLoaded: true }) { _detectors.Activate(); return Result.Succeeded; }

        _detectors = new DetectorDesignWindow(families);
        _detectorRunner = new DetectorPlacementRunner(_detectors);
        _detectors.Runner = _detectorRunner;
        _detectors.SetOwner(app.MainWindowHandle);
        _detectors.Closed += (_, _) => { _detectors = null; _detectorRunner = null; };
        _detectors.Show();
        return Result.Succeeded;
    }

    public static Result LaunchRoomCreation(UIApplication app, ref string message)
    {
        UIDocument uiDoc = app.ActiveUIDocument;
        if (uiDoc is null) { message = "Open a project first."; return Result.Cancelled; }

        var levels = new FilteredElementCollector(uiDoc.Document)
            .OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation)
            .Select(l => new NamedOption(l.Id.Value.ToString(), l.Name)).ToList();
        if (levels.Count == 0) { TaskDialog.Show("SPA SynthFire", "No levels found in the model."); return Result.Cancelled; }
        if (_rooms is { IsLoaded: true }) { _rooms.Activate(); return Result.Succeeded; }

        _rooms = new RoomCreationWindow(levels);
        _roomRunner = new RoomCreationRunner(_rooms);
        _rooms.Runner = _roomRunner;
        _rooms.SetOwner(app.MainWindowHandle);
        _rooms.Closed += (_, _) => { _rooms = null; _roomRunner = null; };
        _rooms.Show();
        return Result.Succeeded;
    }

    public static Result LaunchHazard(UIApplication app, ref string message)
    {
        UIDocument uiDoc = app.ActiveUIDocument;
        if (uiDoc is null) { message = "Open a project first."; return Result.Cancelled; }
        if (_hazard is { IsLoaded: true }) { _hazard.Activate(); return Result.Succeeded; }

        var settings = SettingsStore.Load();
        var registry = StandardRegistry.CreateDefault();
        var seeds = new List<HazardRoomSeed>();
        foreach (var room in HazardRoomReader.Read(uiDoc.Document))
        {
            var s = RoomHazardClassifier.Classify(room.RoomName);
            seeds.Add(new HazardRoomSeed(
                room.RoomId, room.RoomName, room.LevelName,
                s.Tier, s.MatchedKeywords, s.ReviewRequired, room.ExistingHazardId));
        }

        _hazard = new HazardClassificationWindow(registry, settings.StandardId, seeds);
        _hazardRunner = new HazardClassificationRunner(_hazard);
        _hazard.Runner = _hazardRunner;
        _hazard.SetOwner(app.MainWindowHandle);
        _hazard.Closed += (_, _) => { _hazard = null; _hazardRunner = null; };
        _hazard.Show();
        return Result.Succeeded;
    }

    public static Result LaunchSettings(UIApplication app)
    {
        if (_settings is { IsLoaded: true }) { _settings.Activate(); return Result.Succeeded; }

        var settings = SettingsStore.Load();
        var registry = StandardRegistry.CreateDefault();
        _settings = SettingsWindow.Create(registry, settings, app.MainWindowHandle);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
        return Result.Succeeded;
    }

    public static Result ClearDevices(UIApplication app, ref string message)
    {
        UIDocument uiDoc = app.ActiveUIDocument;
        if (uiDoc is null) { message = "Open a project first."; return Result.Cancelled; }
        Document doc = uiDoc.Document;

        const string deviceTag = "SPA SynthFire";
        var ids = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType().OfClass(typeof(FamilyInstance))
            .Where(e => e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString()?.StartsWith(deviceTag) == true)
            .Select(e => e.Id).ToList();

        if (ids.Count == 0)
        { TaskDialog.Show("SPA SynthFire", "No SPA SynthFire devices were found to clear."); return Result.Succeeded; }

        var confirm = new TaskDialog("SPA SynthFire — Clear Devices")
        {
            MainInstruction = $"Delete {ids.Count} SPA SynthFire device(s)?",
            MainContent = "This removes every sprinkler, detector and notification appliance " +
                          "placed by SPA SynthFire (tagged in Comments). You can still undo it in Revit.",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No
        };
        if (confirm.Show() != TaskDialogResult.Yes) return Result.Cancelled;

        using (var tx = new Transaction(doc, "SPA SynthFire — Clear Devices"))
        {
            tx.Start();
            doc.Delete(ids);
            tx.Commit();
        }
        TaskDialog.Show("SPA SynthFire", $"Cleared {ids.Count} device(s).");
        return Result.Succeeded;
    }
}
