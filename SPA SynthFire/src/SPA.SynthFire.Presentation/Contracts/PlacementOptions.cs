using SPA.SynthFire.Core.Devices;

namespace SPA.SynthFire.UI;

/// <summary>A selectable Revit family type, kept Revit-free for the dialog.</summary>
public sealed record FamilyTypeOption(string Id, string Label)
{
    /// <summary>
    /// How this family mounts (pendent/upright/sidewall/free), resolved from the Revit
    /// family when the picker is populated. Drives both the layout strategy and how the
    /// coverage plan draws the device, so the preview matches what actually gets placed.
    /// Defaults to <see cref="DeviceMount.Ceiling"/> for callers/tests that don't set it.
    /// </summary>
    public DeviceMount Mount { get; init; } = DeviceMount.Ceiling;

    public override string ToString() => Label;
}

/// <summary>
/// User selections gathered before a placement run: the sprinkler family/type to
/// place, and which standard and hazard class to design to. Revit-free so the
/// dialog can be unit-tested / reused. <paramref name="PreviewOnly"/> runs the
/// solver and shows the report WITHOUT writing anything into the model.
/// </summary>
public sealed record PlacementOptions(
    string StandardId, string HazardClassId, string FamilySymbolId, string FamilyLabel,
    bool PreviewOnly = false, DeviceMount Mount = DeviceMount.Ceiling);
