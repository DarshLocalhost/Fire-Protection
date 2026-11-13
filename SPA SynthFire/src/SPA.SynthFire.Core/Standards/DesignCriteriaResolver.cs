using System;

namespace SPA.SynthFire.Core.Standards;

/// <summary>
/// Applies user-supplied design criteria on top of a standard's rule set.
///
/// Safety rule: user overrides may only tighten the design, never relax the
/// code. Maxima are clamped down to the standard's maxima; the minimum spacing
/// is clamped up to at least the standard's minimum. A tightened result stays
/// provisional if the standard is provisional (overriding does not "approve" it).
/// </summary>
public static class DesignCriteriaResolver
{
    public static SprinklerRuleSet Apply(SprinklerRuleSet baseRules, UserDesignCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(baseRules);
        ArgumentNullException.ThrowIfNull(criteria);

        if (criteria.IsEmpty)
        {
            return baseRules;
        }

        double maxArea = ClampMax(criteria.MaxCoverageAreaPerSprinkler, baseRules.MaxCoverageAreaPerSprinkler);
        double maxSpacing = ClampMax(criteria.MaxSpacing, baseRules.MaxSpacing);
        double maxToWall = ClampMax(criteria.MaxDistanceToWall, baseRules.MaxDistanceToWall);
        double minSpacing = ClampMin(criteria.MinSpacing, baseRules.MinSpacing);

        bool tightened =
            maxArea < baseRules.MaxCoverageAreaPerSprinkler ||
            maxSpacing < baseRules.MaxSpacing ||
            maxToWall < baseRules.MaxDistanceToWall ||
            minSpacing > baseRules.MinSpacing;

        string notes = baseRules.Notes;
        if (tightened)
        {
            notes = string.IsNullOrEmpty(notes)
                ? "Tightened by user design criteria."
                : notes + " | Tightened by user design criteria.";
        }

        return baseRules with
        {
            MaxCoverageAreaPerSprinkler = maxArea,
            MaxSpacing = maxSpacing,
            MaxDistanceToWall = maxToWall,
            MinSpacing = minSpacing,
            Notes = notes
        };
    }

    // A user max tighter than the standard is honoured; a looser one is clamped to the standard.
    private static double ClampMax(double? userValue, double standardMax)
        => userValue is null ? standardMax : Math.Min(userValue.Value, standardMax);

    // A user min stricter (larger) than the standard is honoured; a looser one is clamped up.
    private static double ClampMin(double? userValue, double standardMin)
        => userValue is null ? standardMin : Math.Max(userValue.Value, standardMin);
}
