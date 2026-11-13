namespace SPA.SynthFire.Core.Devices;

/// <summary>
/// How a device mounts, which decides both its placement strategy in Revit AND how
/// its coverage is drawn in the plan. Revit-free (lives in Core) so the selected
/// family's mount can be carried from the family picker all the way through the
/// solver and into the coverage preview — the whole pipeline lays out and draws a
/// sidewall differently from a pendent instead of treating them identically.
/// </summary>
public enum DeviceMount
{
    /// <summary>Pendent / ceiling-hosted: hangs below the ceiling, throws a symmetric
    /// circle centred on the point. The default for anything not otherwise classified.</summary>
    Ceiling = 0,

    /// <summary>Upright: sits just below the ceiling pointing up; still a symmetric
    /// circular coverage, but placed free at the ceiling plane (not face-hosted down).</summary>
    Upright = 1,

    /// <summary>Sidewall / wall-mounted: hosts to a room wall and throws ACROSS the room
    /// in one direction — a directional (roughly rectangular) footprint, not a circle.</summary>
    Sidewall = 2,

    /// <summary>Unhosted / level-based: placed as a free point; symmetric circular coverage.</summary>
    Free = 3
}
