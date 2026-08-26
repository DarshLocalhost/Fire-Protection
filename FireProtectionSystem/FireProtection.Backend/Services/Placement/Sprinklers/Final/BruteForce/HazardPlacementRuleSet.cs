using FireProtection.Backend.Models.Hazard;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce
{
    /// <summary>
    /// Concrete spacing/coverage values for a hazard class. Values are supplied by an
    /// <see cref="IHazardPlacementRules"/> implementation and must be replaceable later when the
    /// senior/project provides exact NFPA13-2022 values.
    /// </summary>
    public class HazardPlacementRuleSet
    {
        public HazardClass HazardClass { get; set; }

        /// <summary>Maximum permitted spacing between sprinklers (feet).</summary>
        public double MaxSpacingFt { get; set; }

        /// <summary>Maximum coverage radius attributed to a single sprinkler (feet).</summary>
        public double CoverageRadiusFt { get; set; }

        /// <summary>Required clearance from obstacles (feet).</summary>
        public double ObstacleClearanceFt { get; set; }

        /// <summary>Required clearance from the room boundary (feet).</summary>
        public double BoundaryClearanceFt { get; set; }

        /// <summary>Minimum separation from an existing sprinkler (feet).</summary>
        public double ExistingSprinklerSeparationFt { get; set; }

        /// <summary>
        /// True when these values are provisional placeholders that must NOT be treated as
        /// approved engineering compliance. The room result is flagged for human review.
        /// </summary>
        public bool IsProvisional { get; set; }

        public string Notes { get; set; }
    }
}
