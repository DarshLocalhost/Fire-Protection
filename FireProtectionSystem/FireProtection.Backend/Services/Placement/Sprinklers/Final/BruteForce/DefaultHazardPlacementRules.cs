using FireProtection.Backend.Models.Hazard;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce
{
    /// <summary>
    /// Default rule provider. IMPORTANT: the spacing values below are PROVISIONAL placeholders only.
    /// They are NOT NFPA13-2022 compliant values. They exist so the deterministic calculation engine
    /// has a runnable configuration; every room using them is flagged <c>ReviewRequired</c> and
    /// <see cref="HasApprovedRules"/> is false. Replace this class (or inject a different
    /// <see cref="IHazardPlacementRules"/>) when the senior/project supplies approved values.
    /// </summary>
    public class DefaultHazardPlacementRules : IHazardPlacementRules
    {
        public bool HasApprovedRules => false;

        public HazardPlacementRuleSet GetRules(HazardClass hazardClass)
        {
            // Provisional, uniform placeholder. The same placeholder is intentionally used across
            // hazard classes to avoid inventing hazard-specific engineering factors. All rooms are
            // flagged for human review via IsProvisional.
            const double placeholderSpacingFt = 15.0;

            return new HazardPlacementRuleSet
            {
                HazardClass = hazardClass,
                MaxSpacingFt = placeholderSpacingFt,
                CoverageRadiusFt = placeholderSpacingFt / 2.0,
                ObstacleClearanceFt = 1.0,
                BoundaryClearanceFt = 1.0,
                ExistingSprinklerSeparationFt = placeholderSpacingFt / 2.0,
                IsProvisional = true,
                Notes = "Provisional placeholder spacing (15 ft). NFPA13-2022 project-approved " +
                        "values not yet supplied; must be confirmed by senior engineer."
            };
        }
    }
}
