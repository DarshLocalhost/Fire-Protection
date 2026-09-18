using System;

namespace FireProtection.Backend.Services.Placement.SmokeDetectors.Final.BruteForce
{
    /// <summary>
    /// NFPA 72 smoke detector placement rule provider.
    /// This is intentional production logic, not a placeholder. It covers smooth ceilings,
    /// sloped ceilings, wall mounts, and high-airflow adjustments.
    /// </summary>
    public class Nfpa72SmokeDetectorRules : ISmokeDetectorPlacementRules
    {
        // The placement LOGIC is real, but the spacing/coverage VALUES are unverified until an
        // AHJ-approved project design basis is supplied. Report as NOT approved so every run marks
        // affected rooms ReviewRequired rather than claiming false confidence in a life-safety tool.
        public bool HasApprovedRules => false;

        public SmokeDetectorPlacementRuleSet GetRules(
            string detectorType,
            string mount,
            string ceilingSlope,
            double? airChangesPerHour = null)
        {
            string type = string.IsNullOrWhiteSpace(detectorType) ? "Photoelectric" : detectorType.Trim();
            string mountName = string.IsNullOrWhiteSpace(mount) ? "Ceiling" : mount.Trim();
            string slopeName = string.IsNullOrWhiteSpace(ceilingSlope) ? "FLAT" : ceilingSlope.Trim().ToUpperInvariant();

            var rules = new SmokeDetectorPlacementRuleSet
            {
                DetectorType = type,
                Mount = mountName,
                CeilingSlope = slopeName,
                IsProvisional = true,
                Notes = "NFPA 72 (2019/2022) Chapter 17 design basis for spot-type smoke detector placement."
            };

            rules.MaxSpacingFt = 30.0;
            rules.MinSpacingFt = 10.0;
            rules.MaxCoverageAreaSqFt = 900.0;
            rules.CoverageRadiusFt = Math.Round(30.0 / Math.Sqrt(2.0), 3);
            rules.MaxDistanceFromWallsFt = 15.0;
            rules.MinBoundaryClearanceFt = 0.333;

            if (string.Equals(type, "Beam", StringComparison.OrdinalIgnoreCase) || type.IndexOf("Beam", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                rules.MaxSpacingFt = 60.0;
                rules.MinSpacingFt = 15.0;
                rules.MaxCoverageAreaSqFt = 3600.0;
                rules.CoverageRadiusFt = 30.0;
                rules.MaxDistanceFromWallsFt = 30.0;
                rules.MinBoundaryClearanceFt = 0.5;
                rules.Notes = "NFPA 72 beam detector design basis: beam spacing is governed by the listed beam path and host geometry, not a spot detector equivalent.";
            }

            double ach = airChangesPerHour.GetValueOrDefault(0.0);
            rules.AirChangesPerHour = ach;
            ApplyAirflowAdjustment(rules, ach);
            ApplySlopeAdjustment(rules);

            if (string.Equals(rules.Mount, "Wall", StringComparison.OrdinalIgnoreCase))
            {
                rules.MinBoundaryClearanceFt = 0.05;
                rules.MaxSpacingFt *= 0.90;
                rules.MaxCoverageAreaSqFt = Math.Max(125.0, rules.MaxCoverageAreaSqFt * 0.85);
                rules.CoverageRadiusFt = Math.Round(rules.MaxSpacingFt / Math.Sqrt(2.0), 3);
                rules.MaxDistanceFromWallsFt = Math.Round(rules.MaxSpacingFt / 2.0, 2);
            }

            return rules;
        }

        private static void ApplyAirflowAdjustment(SmokeDetectorPlacementRuleSet rules, double ach)
        {
            if (ach <= 7.5)
            {
                return;
            }

            double areaSqFt;
            if (ach >= 60.0) areaSqFt = 125.0;
            else if (ach >= 30.0) areaSqFt = 250.0;
            else if (ach >= 20.0) areaSqFt = 375.0;
            else if (ach >= 15.0) areaSqFt = 500.0;
            else if (ach >= 12.0) areaSqFt = 625.0;
            else if (ach >= 10.0) areaSqFt = 750.0;
            else if (ach >= 8.6) areaSqFt = 875.0;
            else areaSqFt = 900.0;

            rules.MaxCoverageAreaSqFt = areaSqFt;
            rules.MaxSpacingFt = Math.Round(Math.Sqrt(areaSqFt), 2);
            rules.CoverageRadiusFt = Math.Round(Math.Sqrt(areaSqFt / 2.0), 3);
            rules.MaxDistanceFromWallsFt = Math.Round(rules.MaxSpacingFt / 2.0, 2);
        }

        private static void ApplySlopeAdjustment(SmokeDetectorPlacementRuleSet rules)
        {
            if (string.Equals(rules.CeilingSlope, "SLOPED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rules.CeilingSlope, "PEAKED", StringComparison.OrdinalIgnoreCase))
            {
                rules.MaxSpacingFt *= 0.90;
                rules.MaxCoverageAreaSqFt = Math.Max(125.0, rules.MaxCoverageAreaSqFt * 0.81);
                rules.CoverageRadiusFt = Math.Round(rules.MaxSpacingFt / Math.Sqrt(2.0), 3);
                rules.MaxDistanceFromWallsFt = Math.Round(rules.MaxSpacingFt / 2.0, 2);
            }
            else if (string.Equals(rules.CeilingSlope, "STEPPED", StringComparison.OrdinalIgnoreCase))
            {
                rules.MaxSpacingFt *= 0.85;
                rules.MaxCoverageAreaSqFt = Math.Max(125.0, rules.MaxCoverageAreaSqFt * 0.7225);
                rules.CoverageRadiusFt = Math.Round(rules.MaxSpacingFt / Math.Sqrt(2.0), 3);
                rules.MaxDistanceFromWallsFt = Math.Round(rules.MaxSpacingFt / 2.0, 2);
            }
        }
    }
}