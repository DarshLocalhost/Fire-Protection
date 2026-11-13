using System;
using Autodesk.Revit.DB;
using SPA.SynthFire.Core.Devices;

namespace SPA.SynthFire.Revit;

/// <summary>How a sprinkler family mounts, which decides its placement strategy.</summary>
public enum SprinklerMount
{
    /// <summary>Pendent — hangs below and hosts to the ceiling face above the point.</summary>
    Ceiling,

    /// <summary>Upright — sits just below the ceiling on a sprig, pointing up; placed
    /// free at the ceiling elevation (NOT face-hosted, which would point it down).</summary>
    Upright,

    /// <summary>Sidewall — hosts to a room wall and throws across the room.</summary>
    Sidewall,

    /// <summary>Unhosted / level-based — placed as a free point at the computed height.</summary>
    Free
}

/// <summary>
/// Resolves a family's mounting behaviour from its Revit placement type and name.
/// Sidewall families are detected by name ("sidewall"); ceiling-hostable families
/// (face/work-plane/hosted) get ceiling hosting; everything else is placed free.
/// </summary>
public static class SprinklerMountResolver
{
    public static SprinklerMount Resolve(FamilySymbol symbol)
    {
        string name = ($"{symbol.FamilyName} {symbol.Name}").ToLowerInvariant();
        if (name.Contains("sidewall") || name.Contains("side wall") || name.Contains("horizontal"))
            return SprinklerMount.Sidewall;

        if (name.Contains("upright"))
            return SprinklerMount.Upright;

        bool hostable =
            symbol.Family.FamilyPlacementType is FamilyPlacementType.WorkPlaneBased
                                               or FamilyPlacementType.OneLevelBasedHosted;
        return hostable ? SprinklerMount.Ceiling : SprinklerMount.Free;
    }
}

/// <summary>Maps the Revit-writer mount enum to the Revit-free <see cref="DeviceMount"/>
/// carried through the solver and coverage preview (1:1, so the two stay in step).</summary>
public static class SprinklerMountExtensions
{
    public static DeviceMount ToDeviceMount(this SprinklerMount mount) => mount switch
    {
        SprinklerMount.Sidewall => DeviceMount.Sidewall,
        SprinklerMount.Upright => DeviceMount.Upright,
        SprinklerMount.Free => DeviceMount.Free,
        _ => DeviceMount.Ceiling
    };
}
