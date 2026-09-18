using FireProtection.Backend.Models.Hazard;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce
{
    /// <summary>
    /// Pluggable abstraction that maps a hazard class to its spacing/coverage rule set.
    /// Production code must consume the rules via this interface so the calculation engine is
    /// independent from any specific NFPA edition or project design basis.
    /// </summary>
    public interface IHazardPlacementRules
    {
        /// <summary>
        /// True when the values are engineering-approved and not provisional placeholders.
        /// This is the contract the production UI/reporting layer uses to decide whether review is required.
        /// </summary>
        bool HasApprovedRules { get; }

        HazardPlacementRuleSet GetRules(HazardClass hazardClass);
    }
}
