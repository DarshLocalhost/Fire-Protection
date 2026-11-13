namespace SPA.SynthFire.Core.Standards;

/// <summary>
/// The spacing/coverage envelope a standard imposes on standard-spray sprinklers
/// for one hazard classification. All lengths are in <see cref="Units"/> of the
/// declared <see cref="System"/>. These are hard maxima the solver must not exceed
/// (and a minimum spacing it must not violate).
/// </summary>
public sealed record SprinklerRuleSet
{
    public required string StandardId { get; init; }
    public required string HazardClassId { get; init; }
    public required UnitSystem System { get; init; }

    /// <summary>Maximum floor area a single sprinkler may protect.</summary>
    public required double MaxCoverageAreaPerSprinkler { get; init; }

    /// <summary>Maximum centre-to-centre distance between adjacent sprinklers.</summary>
    public required double MaxSpacing { get; init; }

    /// <summary>Maximum distance from a sprinkler to a wall (typically ½ max spacing).</summary>
    public required double MaxDistanceToWall { get; init; }

    /// <summary>Minimum centre-to-centre spacing (prevents cold-soldering / skipping).</summary>
    public required double MinSpacing { get; init; }

    /// <summary>
    /// False until an FPE has reviewed and signed off these encoded values.
    /// While false, every layout built from this rule set is review-required.
    /// </summary>
    public required bool IsProvisional { get; init; }

    /// <summary>Provenance / caveats (source table, unverified status, conversions).</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>Max coverage area converted to square feet (Revit internal unit).</summary>
    public double MaxCoverageAreaSquareFeet => Units.AreaToSquareFeet(MaxCoverageAreaPerSprinkler, System);

    /// <summary>Max spacing converted to feet (Revit internal unit).</summary>
    public double MaxSpacingFeet => Units.LengthToFeet(MaxSpacing, System);

    /// <summary>Max distance-to-wall converted to feet (Revit internal unit).</summary>
    public double MaxDistanceToWallFeet => Units.LengthToFeet(MaxDistanceToWall, System);

    /// <summary>Min spacing converted to feet (Revit internal unit).</summary>
    public double MinSpacingFeet => Units.LengthToFeet(MinSpacing, System);
}
