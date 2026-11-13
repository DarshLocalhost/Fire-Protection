using SPA.SynthFire.Core.Standards;

namespace SPA.SynthFire.Core.Settings;

/// <summary>
/// Builds the effective sprinkler rule set for a standard + hazard class, applying
/// any user edit from the Design Standards table on top of the published values.
/// Edited values replace the published ones verbatim (the engineer's design
/// criteria); the result stays provisional.
/// </summary>
public static class EffectiveRules
{
    public static SprinklerRuleSet For(
        StandardRegistry registry, SynthFireSettings settings, string standardId, string hazardId)
    {
        SprinklerRuleSet baseRules = registry.Get(standardId).GetSprinklerRules(hazardId);

        if (settings.Overrides is null ||
            !settings.Overrides.TryGetValue(HazardRuleOverride.Key(standardId, hazardId), out var o) ||
            o is null || o.IsEmpty)
        {
            return baseRules;
        }

        return baseRules with
        {
            MaxSpacing = o.MaxSpacing ?? baseRules.MaxSpacing,
            MinSpacing = o.MinSpacing ?? baseRules.MinSpacing,
            MaxDistanceToWall = o.MaxDistanceToWall ?? baseRules.MaxDistanceToWall,
            MaxCoverageAreaPerSprinkler = o.MaxCoverageAreaPerSprinkler ?? baseRules.MaxCoverageAreaPerSprinkler,
            Notes = string.IsNullOrEmpty(baseRules.Notes)
                ? "Edited via Design Standards settings."
                : baseRules.Notes + " | Edited via Design Standards settings."
        };
    }
}
