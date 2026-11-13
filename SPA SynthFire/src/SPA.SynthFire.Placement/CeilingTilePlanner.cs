namespace SPA.SynthFire.Placement;

/// <summary>
/// Builds a <see cref="CeilingTile"/> snap grid from the user's settings (all in inches),
/// converting to feet (Revit internal units). Kept Revit-free so the inches→feet conversion
/// and the enable/validity gating are unit-tested without a live model.
/// </summary>
public static class CeilingTilePlanner
{
    private const double InchesPerFoot = 12.0;

    /// <summary>
    /// Returns the tile grid to snap ceiling devices to, or null when snapping is off or the
    /// module is not a positive size (in which case placement stays on the even coverage grid).
    /// </summary>
    public static CeilingTile? FromInches(
        bool enabled, double moduleXInches, double moduleYInches,
        double originXInches = 0.0, double originYInches = 0.0)
    {
        if (!enabled || moduleXInches <= 0 || moduleYInches <= 0) return null;
        return new CeilingTile(
            moduleXInches / InchesPerFoot,
            moduleYInches / InchesPerFoot,
            originXInches / InchesPerFoot,
            originYInches / InchesPerFoot);
    }
}
