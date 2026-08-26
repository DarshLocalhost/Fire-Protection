using FireProtection.Backend.Models.Hazard;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce
{
    /// <summary>
    /// Pluggable abstraction that maps a hazard class to its spacing/coverage rule set.
    /// The engine consumes rules exclusively through this interface so the exact
    /// project-approved NFPA13-2022 values can be dropped in later without touching the algorithm.
    /// </summary>
    public interface IHazardPlacementRules
    {
        /// <summary>True when the values are project-approved (not provisional placeholders).</summary>
        bool HasApprovedRules { get; }

        HazardPlacementRuleSet GetRules(HazardClass hazardClass);
    }
}
