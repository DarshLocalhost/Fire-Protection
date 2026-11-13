using System;
using System.Collections.Generic;
using System.Linq;

namespace SPA.SynthFire.Core.Standards;

/// <summary>
/// NFPA 13 spacing/coverage limits for standard-spray sprinklers under
/// unobstructed construction (the common case). Values are the published
/// maximum-protection-area / maximum-spacing figures per hazard class.
///
/// PROVISIONAL: encoded from published NFPA 13 tables but NOT yet FPE-verified,
/// so <see cref="HasApprovedRules"/> is false and every rule set is marked
/// provisional. Do not rely on these for issued designs until reviewed.
/// </summary>
public sealed class Nfpa13Standard : IDesignStandard
{
    public string Id => StandardIds.Nfpa13;
    public string DisplayName => "NFPA 13 (Standard for the Installation of Sprinkler Systems)";
    public override string ToString() => DisplayName;
    public UnitSystem Units => UnitSystem.Imperial;
    public bool HasApprovedRules => false;

    // NFPA 13 minimum spacing between standard-spray sprinklers: 6 ft.
    private const double MinSpacingFt = 6.0;

    private static readonly Dictionary<string, SprinklerRuleSet> Rules = Build();

    public IReadOnlyList<HazardClassification> SupportedHazardClasses { get; } = new[]
    {
        new HazardClassification("LH",  "Light Hazard",       "Offices, schools, churches, residential-type occupancies."),
        new HazardClassification("OH1", "Ordinary Hazard 1",  "Automobile parking/showrooms, bakeries, canneries."),
        new HazardClassification("OH2", "Ordinary Hazard 2",  "Chemical plants, mercantile, machine shops, repair garages."),
        new HazardClassification("EH1", "Extra Hazard 1",     "Aircraft hangars, die casting, plywood/particleboard mfg."),
        new HazardClassification("EH2", "Extra Hazard 2",     "Flammable-liquid spraying, plastics processing, saturating.")
    };

    public SprinklerRuleSet GetSprinklerRules(string hazardClassId)
    {
        if (hazardClassId is null) throw new ArgumentNullException(nameof(hazardClassId));
        if (!Rules.TryGetValue(hazardClassId, out var rules))
        {
            throw new ArgumentException(
                $"NFPA 13 has no hazard class '{hazardClassId}'. Known: {string.Join(", ", Rules.Keys)}.",
                nameof(hazardClassId));
        }
        return rules;
    }

    private static Dictionary<string, SprinklerRuleSet> Build()
    {
        // maxArea (sq ft), maxSpacing (ft), maxToWall (ft) = ½ max spacing.
        SprinklerRuleSet Make(string id, double maxArea, double maxSpacing) => new()
        {
            StandardId = StandardIds.Nfpa13,
            HazardClassId = id,
            System = UnitSystem.Imperial,
            MaxCoverageAreaPerSprinkler = maxArea,
            MaxSpacing = maxSpacing,
            MaxDistanceToWall = maxSpacing / 2.0,
            MinSpacing = MinSpacingFt,
            IsProvisional = true,
            Notes = "NFPA 13 standard-spray, unobstructed construction. PROVISIONAL — not FPE-verified."
        };

        return new Dictionary<string, SprinklerRuleSet>(StringComparer.OrdinalIgnoreCase)
        {
            ["LH"]  = Make("LH",  225, 15),
            ["OH1"] = Make("OH1", 130, 15),
            ["OH2"] = Make("OH2", 130, 15),
            ["EH1"] = Make("EH1", 100, 12),
            ["EH2"] = Make("EH2", 100, 12),
        };
    }
}
