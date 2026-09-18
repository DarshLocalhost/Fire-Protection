using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Minimum permitted center-to-center spacing between two sprinklers (feet).
        /// 0 means "use the conservative default of CoverageRadiusFt" in the engine.
        /// Prevents bunching; defaults to ~6 ft per NFPA 13 typical values when populated.
        /// </summary>
        public double MinSpacingFt { get; set; }

        /// <summary>Maximum coverage area per sprinkler (square feet).</summary>
        public double MaxCoverageAreaSqFt { get; set; }

        /// <summary>Maximum coverage radius attributed to a single sprinkler (feet).</summary>
        public double CoverageRadiusFt { get; set; }

        /// <summary>Required clearance from obstacles (feet).</summary>
        public double ObstacleClearanceFt { get; set; }

        /// <summary>Required clearance from the room boundary (feet).</summary>
        public double BoundaryClearanceFt { get; set; }

        /// <summary>Minimum separation from an existing sprinkler (feet).</summary>
        public double ExistingSprinklerSeparationFt { get; set; }

        /// <summary>Maximum distance from walls (feet) - NFPA13-2022 requirement.</summary>
        public double MaxDistanceFromWallsFt { get; set; }

        /// <summary>Minimum K-factor requirement for this hazard class.</summary>
        public double MinKFactor { get; set; }

        /// <summary>Ceiling height adjustment factor (multiplier for spacing).</summary>
        public double CeilingHeightAdjustmentFactor { get; set; }

        /// <summary>Ceiling slope adjustment factors (key: slope type, value: spacing multiplier).</summary>
        public Dictionary<string, double> CeilingSlopeAdjustments { get; set; }

        /// <summary>Obstacle-specific clearances (key: obstacle type, value: clearance in feet).</summary>
        public Dictionary<string, double> ObstacleSpecificClearances { get; set; }

        /// <summary>Orientation-specific spacing adjustments (key: orientation, value: spacing multiplier).</summary>
        public Dictionary<string, double> OrientationSpacingAdjustments { get; set; }

        /// <summary>Coverage pattern adjustment factors (key: pattern type, value: spacing multiplier).</summary>
        public Dictionary<string, double> CoveragePatternAdjustments { get; set; }

        /// <summary>
        /// True when these values are provisional placeholders that must NOT be treated as
        /// approved engineering compliance. The room result is flagged for human review.
        /// </summary>
        public bool IsProvisional { get; set; }

        public string Notes { get; set; }

        public HazardPlacementRuleSet()
        {
            ObstacleSpecificClearances = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            OrientationSpacingAdjustments = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "pendent", 1.0 },
                { "upright", 1.0 },
                { "sidewall", 0.85 }
            };
            CeilingSlopeAdjustments = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "FLAT", 1.0 },
                { "SLOPED", 0.9 },
                { "STEPPED", 0.85 }
            };
            CoveragePatternAdjustments = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "circular", 1.0 },
                { "rectangular", 0.9 },
                { "square", 0.95 }
            };
        }

        public double GetObstacleClearance(string obstacleType)
        {
            if (string.IsNullOrWhiteSpace(obstacleType)) return ObstacleClearanceFt;
            return ObstacleSpecificClearances.TryGetValue(obstacleType, out double clearance) ? clearance : ObstacleClearanceFt;
        }

        public double GetOrientationAdjustment(string orientation)
        {
            if (string.IsNullOrWhiteSpace(orientation)) return 1.0;
            return OrientationSpacingAdjustments.TryGetValue(orientation, out double adjustment) ? adjustment : 1.0;
        }

        public double GetCeilingSlopeAdjustment(string slopeType)
        {
            if (string.IsNullOrWhiteSpace(slopeType)) return 1.0;
            return CeilingSlopeAdjustments.TryGetValue(slopeType, out double adjustment) ? adjustment : 1.0;
        }

        public double GetCoveragePatternAdjustment(string patternType)
        {
            if (string.IsNullOrWhiteSpace(patternType)) return 1.0;
            return CoveragePatternAdjustments.TryGetValue(patternType, out double adjustment) ? adjustment : 1.0;
        }

        /// <summary>
        /// Returns a deep copy of this rule set so per-room overrides, ceiling-height
        /// adjustments, and other in-place mutations cannot leak back into the base
        /// rule set returned by <see cref="IHazardPlacementRules.GetRules"/>.
        /// </summary>
        public HazardPlacementRuleSet Clone()
        {
            HazardPlacementRuleSet copy = new HazardPlacementRuleSet
            {
                HazardClass = this.HazardClass,
                MaxSpacingFt = this.MaxSpacingFt,
                MinSpacingFt = this.MinSpacingFt,
                MaxCoverageAreaSqFt = this.MaxCoverageAreaSqFt,
                CoverageRadiusFt = this.CoverageRadiusFt,
                ObstacleClearanceFt = this.ObstacleClearanceFt,
                BoundaryClearanceFt = this.BoundaryClearanceFt,
                ExistingSprinklerSeparationFt = this.ExistingSprinklerSeparationFt,
                MaxDistanceFromWallsFt = this.MaxDistanceFromWallsFt,
                MinKFactor = this.MinKFactor,
                CeilingHeightAdjustmentFactor = this.CeilingHeightAdjustmentFactor,
                IsProvisional = this.IsProvisional,
                Notes = this.Notes
            };
            if (this.CeilingSlopeAdjustments != null)
            {
                copy.CeilingSlopeAdjustments = new Dictionary<string, double>(this.CeilingSlopeAdjustments, StringComparer.OrdinalIgnoreCase);
            }
            if (this.ObstacleSpecificClearances != null)
            {
                copy.ObstacleSpecificClearances = new Dictionary<string, double>(this.ObstacleSpecificClearances, StringComparer.OrdinalIgnoreCase);
            }
            if (this.OrientationSpacingAdjustments != null)
            {
                copy.OrientationSpacingAdjustments = new Dictionary<string, double>(this.OrientationSpacingAdjustments, StringComparer.OrdinalIgnoreCase);
            }
            if (this.CoveragePatternAdjustments != null)
            {
                copy.CoveragePatternAdjustments = new Dictionary<string, double>(this.CoveragePatternAdjustments, StringComparer.OrdinalIgnoreCase);
            }
            return copy;
        }
    }
}