using SPA.SynthFire.Core.Catalog;

namespace SPA.SynthFire.Core.Standards;

/// <summary>
/// NFPA 72 nominal ceiling-detector spacing, expressed as a <see cref="SprinklerRuleSet"/>
/// so the shared coverage-grid engine can lay detectors out exactly like sprinklers.
///
/// PROVISIONAL: smoke detectors use the 30 ft nominal spacing / ~900 ft² rule of
/// thumb for smooth ceilings; heat detectors use their listed spacing. Both must be
/// derated for ceiling height, beams, airflow and the specific listing, so these are
/// NOT FPE-verified — every layout is provisional.
/// </summary>
public static class Nfpa72DetectionRules
{
    public const string StandardId = "NFPA72";

    public static SprinklerRuleSet For(DetectorKind kind) => kind switch
    {
        DetectorKind.Smoke => Make("Smoke", maxSpacingFt: 30.0, maxAreaSqFt: 900.0),
        DetectorKind.Heat => Make("Heat", maxSpacingFt: 50.0, maxAreaSqFt: 2500.0),
        _ => Make("Smoke", 30.0, 900.0)
    };

    private static SprinklerRuleSet Make(string kindId, double maxSpacingFt, double maxAreaSqFt) => new()
    {
        StandardId = StandardId,
        HazardClassId = kindId,
        System = UnitSystem.Imperial,
        MaxCoverageAreaPerSprinkler = maxAreaSqFt,
        MaxSpacing = maxSpacingFt,
        MaxDistanceToWall = maxSpacingFt / 2.0,
        MinSpacing = 0.0,
        IsProvisional = true,
        Notes = $"NFPA 72 nominal {kindId.ToLowerInvariant()}-detector spacing, smooth ceiling. " +
                "PROVISIONAL — derate for height/beams/airflow; not FPE-verified."
    };
}
