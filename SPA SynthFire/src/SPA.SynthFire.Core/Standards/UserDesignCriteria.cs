namespace SPA.SynthFire.Core.Standards;

/// <summary>
/// User-supplied overrides layered on top of a base standard's rule set
/// (Tandm's "input your own design criteria"). Any value left null falls
/// through to the standard. Overrides are interpreted in the base rule set's
/// unit system.
///
/// Overrides may only make a layout MORE conservative than the standard: the
/// engine clamps user maxima to the standard's maxima and user minima to be at
/// least the standard's minimum, so a user cannot silently relax code limits.
/// </summary>
public sealed record UserDesignCriteria
{
    public double? MaxCoverageAreaPerSprinkler { get; init; }
    public double? MaxSpacing { get; init; }
    public double? MaxDistanceToWall { get; init; }
    public double? MinSpacing { get; init; }

    public static readonly UserDesignCriteria None = new();

    public bool IsEmpty =>
        MaxCoverageAreaPerSprinkler is null &&
        MaxSpacing is null &&
        MaxDistanceToWall is null &&
        MinSpacing is null;
}
