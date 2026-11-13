namespace SPA.SynthFire.Core.Geometry;

/// <summary>
/// Resolves a believable finished-ceiling height (feet) for a room from the
/// candidate heights Revit exposes, so an unhosted device can sit at the room's
/// real upper bound instead of a flat guess. Revit-independent (pure arithmetic)
/// so it is unit-testable without the Revit API.
/// </summary>
public static class CeilingHeight
{
    /// <summary>Flat fallback when no candidate height is believable (feet).</summary>
    public const double DefaultFt = 9.0;

    /// <summary>Lower bound of a believable finished-ceiling band (feet).</summary>
    public const double MinFt = 6.0;

    /// <summary>Upper bound of a believable finished-ceiling band (feet).</summary>
    public const double MaxFt = 30.0;

    /// <summary>
    /// Prefer the room's computed (unbounded) height, else its bounding-box height,
    /// else the flat default — each accepted only inside [<see cref="MinFt"/>, <see cref="MaxFt"/>].
    /// </summary>
    public static double Resolve(double unboundedHeight, double bboxHeight)
    {
        if (IsBelievable(unboundedHeight)) return unboundedHeight;
        if (IsBelievable(bboxHeight)) return bboxHeight;
        return DefaultFt;
    }

    private static bool IsBelievable(double height) => height >= MinFt && height <= MaxFt;
}
