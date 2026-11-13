namespace SPA.SynthFire.Core.Settings;

/// <summary>
/// A user edit to a standard's spacing/coverage values for one hazard class
/// (the editable Design Standards table). Values are in the standard's own units.
/// Null means "leave the published value". These are the engineer's own design
/// criteria — applied verbatim (not clamped), and the resulting layout stays
/// review-required.
/// </summary>
public sealed class HazardRuleOverride
{
    public double? MaxSpacing { get; set; }
    public double? MinSpacing { get; set; }
    public double? MaxDistanceToWall { get; set; }
    public double? MaxCoverageAreaPerSprinkler { get; set; }

    public bool IsEmpty =>
        MaxSpacing is null && MinSpacing is null &&
        MaxDistanceToWall is null && MaxCoverageAreaPerSprinkler is null;

    /// <summary>Settings key for a standard + hazard class.</summary>
    public static string Key(string standardId, string hazardId) => $"{standardId}|{hazardId}";
}
