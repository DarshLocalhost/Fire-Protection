using System.Collections.Generic;

namespace SPA.SynthFire.Core.Catalog;

/// <summary>Sprinkler orientation as installed.</summary>
public enum SprinklerOrientation
{
    Pendent,
    Upright,
    Sidewall
}

/// <summary>
/// One selectable sprinkler product (a Revit family + type) with the design
/// attributes needed to pick it and cross-check it against a rule set.
/// </summary>
public sealed record SprinklerCatalogRow
{
    public required string Family { get; init; }
    public required string Type { get; init; }
    public required double KFactor { get; init; }
    public required SprinklerOrientation Orientation { get; init; }

    /// <summary>Hazard class id this product is listed for (e.g. "LH", "OH1").</summary>
    public required string HazardClassId { get; init; }

    /// <summary>Standard id this listing targets (e.g. "NFPA13", "AS2118").</summary>
    public required string StandardId { get; init; }

    /// <summary>Temperature rating label (e.g. "68C", "155F"). Optional.</summary>
    public string TemperatureRating { get; init; } = string.Empty;
}

/// <summary>Detector kind for the detection ("dry fire") catalog.</summary>
public enum DetectorKind
{
    Smoke,
    Heat
}

/// <summary>One selectable detector product.</summary>
public sealed record SmokeDetectorCatalogRow
{
    public required string Family { get; init; }
    public required string Type { get; init; }
    public required DetectorKind Kind { get; init; }
    public required string StandardId { get; init; }
}

/// <summary>A loaded catalog: version stamp plus the product rows by category.</summary>
public sealed class Catalog
{
    public required string Version { get; init; }
    public required IReadOnlyList<SprinklerCatalogRow> Sprinklers { get; init; }
    public required IReadOnlyList<SmokeDetectorCatalogRow> SmokeDetectors { get; init; }
}
