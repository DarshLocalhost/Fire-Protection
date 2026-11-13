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

        /// <summary>
        /// Maximum coverage radius attributed to a single sprinkler (feet).
        /// NOTE: this is the head's LISTED coverage radius (from its approval / NFPA table),
        /// NOT the geometric distance-to-nearest-head of the layout the engine generates.
        /// Use <see cref="EffectiveCoverageRadiusFt"/> for the layout-geometry check.</summary>
        public double CoverageRadiusFt { get; set; }

        /// <summary>
        /// The geometric coverage radius the engine should test a generated layout against.
        ///
        /// For a rectangular array on a square-ish grid of centre-to-centre spacing S, the
        /// worst-case distance from ANY point on the floor to the nearest head is:
        ///
        ///     array corner : sqrt((S/2)^2 + (S/2)^2) = S / sqrt(2)  ~ 0.707 * S   (worst)
        ///     array edge   : S / 2                                 ~ 0.500 * S
        ///     array centre : S / sqrt(2)                           ~ 0.707 * S
        ///
        /// The stored <see cref="CoverageRadiusFt"/> was set to S/2 for every hazard class,
        /// which is only valid along the array EDGES. Testing a correct centred array against
        /// S/2 therefore reports a coverage gap at every corner and array centre — a false
        /// positive on every single room.
        ///
        /// S/2 and S/sqrt(2) happened to partly cancel out against a 5% tolerance, which is
        /// why the error was invisible. Both halves are corrected together in
        /// FinalizeSelection; see RemoteDistanceLimitFt for the reporting side.
        /// </summary>
        public double EffectiveCoverageRadiusFt =>
            MaxSpacingFt > 0
                ? MaxSpacingFt / Math.Sqrt(2.0)
                : CoverageRadiusFt;

        /// <summary>
        /// The NFPA remote-distance limit used for the ADVISORY coverage report: the furthest
        /// floor point from any head must not exceed 0.7 * nominal spacing.
        /// </summary>
        public double RemoteDistanceLimitFt => MaxSpacingFt * 0.7;

        /// <summary>Required clearance from obstacles (feet).</summary>
        public double ObstacleClearanceFt { get; set; }

        /// <summary>Required clearance from the room boundary (feet).</summary>
        public double BoundaryClearanceFt { get; set; }

        /// <summary>Minimum separation from an existing sprinkler (feet).</summary>
        public double ExistingSprinklerSeparationFt { get; set; }

        /// <summary>Maximum distance from walls (feet) - NFPA13-2022 requirement.</summary>
        public double MaxDistanceFromWallsFt { get; set; }

        /// <summary>
        /// Minimum K-factor requirement for this hazard class.
        ///
        /// NOT ENFORCED. This value is populated by every rule set and carried through
        /// <see cref="Clone"/>, but nothing in the calculation engine reads it, because K-factor
        /// is a property of the selected sprinkler TYPE and is validated at the hydraulic stage,
        /// which this tool does not perform. It is retained so the approved rule set has a home
        /// for the value; <see cref="IsProvisional"/> stays true until an engineer signs off.
        /// Do not read this as "the layout satisfies the K-factor requirement".
        /// </summary>
        public double MinKFactor { get; set; }

        /// <summary>Ceiling height adjustment factor (multiplier for spacing).</summary>
        public double CeilingHeightAdjustmentFactor { get; set; }

        /// <summary>Ceiling slope adjustment factors (key: slope type, value: spacing multiplier).</summary>
        public Dictionary<string, double> CeilingSlopeAdjustments { get; set; }

        /// <summary>Obstacle-specific clearances (key: obstacle type, value: clearance in feet).</summary>
        public Dictionary<string, double> ObstacleSpecificClearances { get; set; }

        /// <summary>Orientation-specific spacing adjustments (key: orientation, value: spacing multiplier).</summary>
        public Dictionary<string, double> OrientationSpacingAdjustments { get; set; }

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
            // CoveragePatternAdjustments and GetCoveragePatternAdjustment were removed.
            // They had ZERO callers anywhere in the solution, so the circular/rectangular/square
            // factors were never applied to any layout — an unvalidated-looking table that a
            // future reader could easily mistake for an implemented rule. Nothing selects a
            // coverage pattern today, so there is no pattern to adjust.
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
            return copy;
        }
    }
}