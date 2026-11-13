using SPA.SynthFire.Core.Catalog;
using SPA.SynthFire.Core.Devices;

namespace SPA.SynthFire.UI.Views;

/// <summary>Bridge for running detector placement off the UI thread (ExternalEvent on the Revit side).</summary>
public interface IDetectorRunner
{
    /// <summary><paramref name="previewOnly"/> solves + reports without writing to the model.
    /// <paramref name="mount"/> is the selected family's mounting type, so a wall notification
    /// appliance lays out and previews directionally instead of on a ceiling grid.</summary>
    void RequestPlace(string familySymbolId, DetectorKind kind, bool clearPrevious, bool previewOnly,
        DeviceMount mount = DeviceMount.Ceiling);

    /// <summary>
    /// Queue a dry-run solve for the given detector kind and open the 2D coverage plan
    /// (non-blocking, no model writes). Rooms render as true polygons with the coverage heatmap.
    /// <paramref name="familySymbolId"/> is optional: when supplied, right-clicking a gap on the
    /// plan writes ONE device of that family at the clicked point (click-to-place); when null the
    /// plan is a read-only viewer.
    /// </summary>
    void RequestPlan(DetectorKind kind, DeviceMount mount = DeviceMount.Ceiling, string? familySymbolId = null);
}
