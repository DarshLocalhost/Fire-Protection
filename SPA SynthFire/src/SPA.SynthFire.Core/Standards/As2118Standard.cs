using System;
using System.Collections.Generic;

namespace SPA.SynthFire.Core.Standards;

/// <summary>
/// AS 2118.1 spacing/coverage limits for standard-spray sprinklers (metric).
/// AS 2118 shares its lineage with EN 12845; the maximum-area-per-sprinkler and
/// maximum-spacing figures per hazard class follow that family.
///
/// PROVISIONAL: encoded from published figures but NOT FPE-verified, and AS 2118
/// exact tabulated values should be confirmed against the current edition, so
/// <see cref="HasApprovedRules"/> is false and every rule set is provisional.
/// </summary>
public sealed class As2118Standard : IDesignStandard
{
    public string Id => StandardIds.As2118;
    public string DisplayName => "AS 2118.1 (Automatic fire sprinkler systems)";
    public override string ToString() => DisplayName;
    public UnitSystem Units => UnitSystem.Metric;
    public bool HasApprovedRules => false;

    // Minimum spacing between sprinklers to avoid cold-soldering (EN 12845 / AS 2118 family): 2.0 m.
    private const double MinSpacingM = 2.0;

    private static readonly Dictionary<string, SprinklerRuleSet> Rules = Build();

    public IReadOnlyList<HazardClassification> SupportedHazardClasses { get; } = new[]
    {
        new HazardClassification("LH",  "Light Hazard",          "Non-industrial low fire-load occupancies."),
        new HazardClassification("OH1", "Ordinary Hazard Gp 1",  "Cement, ceramics, metalworking."),
        new HazardClassification("OH2", "Ordinary Hazard Gp 2",  "Bakeries, breweries, laundries, restaurants."),
        new HazardClassification("OH3", "Ordinary Hazard Gp 3",  "Department stores, paper mills, tyre manufacture."),
        new HazardClassification("HHP", "High Hazard Process",   "Coating, high fire-load process occupancies.")
    };

    public SprinklerRuleSet GetSprinklerRules(string hazardClassId)
    {
        if (hazardClassId is null) throw new ArgumentNullException(nameof(hazardClassId));
        if (!Rules.TryGetValue(hazardClassId, out var rules))
        {
            throw new ArgumentException(
                $"AS 2118 has no hazard class '{hazardClassId}'. Known: {string.Join(", ", Rules.Keys)}.",
                nameof(hazardClassId));
        }
        return rules;
    }

    private static Dictionary<string, SprinklerRuleSet> Build()
    {
        // maxArea (m²), maxSpacing (m), maxToWall (m) = ½ max spacing.
        SprinklerRuleSet Make(string id, double maxAreaM2, double maxSpacingM) => new()
        {
            StandardId = StandardIds.As2118,
            HazardClassId = id,
            System = UnitSystem.Metric,
            MaxCoverageAreaPerSprinkler = maxAreaM2,
            MaxSpacing = maxSpacingM,
            MaxDistanceToWall = maxSpacingM / 2.0,
            MinSpacing = MinSpacingM,
            IsProvisional = true,
            Notes = "AS 2118.1 / EN 12845 family, standard-spray. PROVISIONAL — confirm against current edition; not FPE-verified."
        };

        return new Dictionary<string, SprinklerRuleSet>(StringComparer.OrdinalIgnoreCase)
        {
            ["LH"]  = Make("LH",  21.0, 4.6),
            ["OH1"] = Make("OH1", 12.0, 4.0),
            ["OH2"] = Make("OH2", 12.0, 4.0),
            ["OH3"] = Make("OH3", 12.0, 4.0),
            ["HHP"] = Make("HHP",  9.0, 3.7),
        };
    }
}
