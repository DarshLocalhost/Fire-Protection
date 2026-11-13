using System.Reflection;
using Autodesk.Revit.UI;

namespace SPA.SynthFire.Revit;

/// <summary>
/// SPA SynthFire add-in entry point. Builds the ribbon tab and panels when Revit
/// starts, organised by workflow: Setup → Sprinkler Design → Fire Detection →
/// Quantity Estimation → Support.
/// </summary>
public sealed class SynthFireApplication : IExternalApplication
{
    private const string TabName = "SPA SynthFire";

    // Kept alive for the session: the dockable pane's content is created once at registration.
    private static SynthFireDockablePane? _pane;

    public Result OnStartup(UIControlledApplication application)
    {
        try { application.CreateRibbonTab(TabName); }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { /* tab exists */ }

        string asm = Assembly.GetExecutingAssembly().Location;

        // Register the dockable panel once at startup (its WPF content lives for the session).
        _pane = new SynthFireDockablePane();
        try { application.RegisterDockablePane(SynthFireDockablePane.PaneId, "SPA SynthFire", _pane); }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { /* already registered */ }

        RibbonPanel setup = application.CreateRibbonPanel(TabName, "Setup");
        setup.AddItem(Button("SynthFirePanel", "Panel",
            typeof(SynthFirePanelCommand), asm,
            "Show the dockable SPA SynthFire panel (quick-launch home for every tool)."));
        setup.AddItem(Button("SynthFireSettings", "Settings",
            typeof(SettingsCommand), asm,
            "Configure the design standard, units, design-standard tables and placement offsets."));
        setup.AddItem(Button("SynthFireRoomCreation", "Room\nCreation",
            typeof(RoomCreationCommand), asm,
            "Create rooms in every enclosed region on the selected levels."));
        setup.AddItem(Button("SynthFireHazard", "Hazard\nClassification",
            typeof(HazardClassificationCommand), asm,
            "Tag rooms with a hazard class (selected rooms or all rooms)."));

        RibbonPanel design = application.CreateRibbonPanel(TabName, "Sprinkler Design");
        design.AddItem(Button("SynthFirePlaceSprinklers", "Place\nSprinklers",
            typeof(PlaceSprinklersCommand), asm,
            "Auto-place code-compliant sprinklers in the model's rooms (pendent, upright and sidewall by family)."));

        RibbonPanel detection = application.CreateRibbonPanel(TabName, "Fire Detection");
        detection.AddItem(Button("SynthFirePlaceDetectors", "Place\nDetectors",
            typeof(PlaceDetectorsCommand), asm,
            "Auto-place smoke/heat detectors on ceilings and notification appliances on walls (NFPA 72 nominal)."));

        RibbonPanel support = application.CreateRibbonPanel(TabName, "Support");
        support.AddItem(Button("SynthFireClearDevices", "Clear\nDevices",
            typeof(ClearDevicesCommand), asm,
            "Delete every sprinkler, detector and notification appliance placed by SPA SynthFire."));
        support.AddItem(Button("SynthFireAbout", "About",
            typeof(SynthFirePlaceholderCommand), asm, "About SPA SynthFire."));

        return Result.Succeeded;
    }

    private static PushButtonData Button(string name, string text, System.Type command, string asm, string tooltip)
    {
        var data = new PushButtonData(name, text, asm, command.FullName) { ToolTip = tooltip };
        return data;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}
