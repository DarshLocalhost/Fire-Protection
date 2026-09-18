using System;
using System.Collections.Generic;

namespace FireProtection.Backend.Services.Placement.SmokeDetectors.Final.BruteForce
{
    /// <summary>
    /// Concrete spacing, clearance, and coverage values for spot-type smoke detectors
    /// (NFPA 72 Chapter 17). Supplied by an <see cref="ISmokeDetectorPlacementRules"/>
    /// implementation; clone before any per-room mutation.
    /// </summary>
    public class SmokeDetectorPlacementRuleSet
    {
        public string DetectorType { get; set; }

        public string Mount { get; set; }

        public string CeilingSlope { get; set; }

        /// <summary>NFPA 72 §17.7.3.2.3.1 nominal smooth-ceiling spacing (ft). Default 30.</summary>
        public double MaxSpacingFt { get; set; }

        /// <summary>Minimum center-to-center distance between two detectors (ft).</summary>
        public double MinSpacingFt { get; set; }

        /// <summary>Maximum listed coverage area per detector (sq ft). Default 900.</summary>
        public double MaxCoverageAreaSqFt { get; set; }

        /// <summary>
        /// Coverage radius (ft). Smooth ceiling: S / sqrt(2) ≈ 21.21 for S = 30.
        /// </summary>
        public double CoverageRadiusFt { get; set; }

        /// <summary>
        /// NFPA 72 §17.7.3.2.1 minimum distance from walls for ceiling-mounted detectors (ft).
        /// 4 in = 0.333 ft.
        /// </summary>
        public double MinBoundaryClearanceFt { get; set; }

        /// <summary>
        /// NFPA 72 §17.7.3.2.3.1 maximum distance from walls (ft), typically 0.5 * S.
        /// </summary>
        public double MaxDistanceFromWallsFt { get; set; }

        /// <summary>
        /// Wall-mounted detector drop below ceiling (ft), 4–12 in. Default 0.5 ft (6 in).
        /// </summary>
        public double WallMountDropFromCeilingFt { get; set; }

        /// <summary>
        /// Clearance from HVAC supply registers / terminals (ft). Industry practice 3.0.
        /// </summary>
        public double HvacSupplyRegisterClearanceFt { get; set; }

        /// <summary>Default obstacle clearance when category is unknown (ft).</summary>
        public double ObstacleClearanceFt { get; set; }

        /// <summary>Minimum separation from an existing smoke detector (ft).</summary>
        public double ExistingDetectorSeparationFt { get; set; }

        /// <summary>NFPA 72 peak/ridge zone width for sloped ceilings (ft).</summary>
        public double PeakZoneWidthFt { get; set; }

        /// <summary>Air changes per hour used when building this rule set (0 = not applied).</summary>
        public double AirChangesPerHour { get; set; }

        public Dictionary<string, double> ObstacleSpecificClearances { get; set; }

        public bool IsProvisional { get; set; }

        public string Notes { get; set; }

        public SmokeDetectorPlacementRuleSet()
        {
            DetectorType = "Photoelectric";
            Mount = "Ceiling";
            CeilingSlope = "FLAT";
            MaxSpacingFt = 30.0;
            MinSpacingFt = 10.0;
            MaxCoverageAreaSqFt = 900.0;
            CoverageRadiusFt = 21.213;
            MinBoundaryClearanceFt = 0.333;
            MaxDistanceFromWallsFt = 15.0;
            WallMountDropFromCeilingFt = 0.5;
            HvacSupplyRegisterClearanceFt = 3.0;
            ObstacleClearanceFt = 1.0;
            ExistingDetectorSeparationFt = 15.0;
            PeakZoneWidthFt = 3.0;
            AirChangesPerHour = 0.0;
            IsProvisional = false;
            Notes = null;

            ObstacleSpecificClearances = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "OST_DuctCurves", 1.5 },
                { "OST_DuctTerminal", 3.0 },
                { "OST_StructuralColumns", 1.0 },
                { "OST_StructuralFraming", 1.0 },
                { "OST_Walls", 0.5 },
                { "OST_PipeCurves", 0.5 },
                { "OST_CableTray", 1.0 }
            };
        }

        public double GetObstacleClearance(string obstacleCategory)
        {
            if (string.IsNullOrWhiteSpace(obstacleCategory)) return ObstacleClearanceFt;
            if (ObstacleSpecificClearances != null
                && ObstacleSpecificClearances.TryGetValue(obstacleCategory, out double clearance))
            {
                return clearance;
            }
            return ObstacleClearanceFt;
        }

        public SmokeDetectorPlacementRuleSet Clone()
        {
            SmokeDetectorPlacementRuleSet copy = new SmokeDetectorPlacementRuleSet
            {
                DetectorType = this.DetectorType,
                Mount = this.Mount,
                CeilingSlope = this.CeilingSlope,
                MaxSpacingFt = this.MaxSpacingFt,
                MinSpacingFt = this.MinSpacingFt,
                MaxCoverageAreaSqFt = this.MaxCoverageAreaSqFt,
                CoverageRadiusFt = this.CoverageRadiusFt,
                MinBoundaryClearanceFt = this.MinBoundaryClearanceFt,
                MaxDistanceFromWallsFt = this.MaxDistanceFromWallsFt,
                WallMountDropFromCeilingFt = this.WallMountDropFromCeilingFt,
                HvacSupplyRegisterClearanceFt = this.HvacSupplyRegisterClearanceFt,
                ObstacleClearanceFt = this.ObstacleClearanceFt,
                ExistingDetectorSeparationFt = this.ExistingDetectorSeparationFt,
                PeakZoneWidthFt = this.PeakZoneWidthFt,
                AirChangesPerHour = this.AirChangesPerHour,
                IsProvisional = this.IsProvisional,
                Notes = this.Notes
            };

            if (this.ObstacleSpecificClearances != null)
            {
                copy.ObstacleSpecificClearances = new Dictionary<string, double>(
                    this.ObstacleSpecificClearances, StringComparer.OrdinalIgnoreCase);
            }

            return copy;
        }
    }
}