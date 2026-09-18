using System;
using System.Globalization;
using FireProtection.Backend.Services.Placement.SmokeDetectors.Final.BruteForce;

namespace FireProtection.Backend.Services.Placement.NotificationAppliances.Final.BruteForce
{
    /// <summary>
    /// Notification-appliance (strobe / speaker / speaker-strobe) placement rules.
    ///
    /// Rating-aware Chapter 18 planning rules. These values are intentionally marked provisional until the
    /// AHJ-approved project design basis and the applicable edition tables are supplied. The important
    /// production behavior is nevertheless real: visible coverage uses candela, audible coverage uses dBA,
    /// combined appliances use the stricter constraint, mounting and ceiling slope change the geometry, and
    /// obstacle/existing-device checks remain active.
    ///
    /// Reuses the smoke-detector coverage-grid engine (same room geometry, obstacle and existing-device logic)
    /// via <see cref="ISmokeDetectorPlacementRules"/> - only the spacing values differ.
    /// </summary>
    public class Nfpa72NotificationApplianceRules : ISmokeDetectorPlacementRules
    {
        // Provisional until the AHJ-approved design basis is supplied (see class summary). The coverage
        // LOGIC is real; the tabulated values are not yet verified — so report NOT approved.
        public bool HasApprovedRules => false;

        public SmokeDetectorPlacementRuleSet GetRules(
            string applianceType,
            string mount,
            string ceilingSlope,
            double? airChangesPerHour = null)
        {
            ParseDescriptor(applianceType, out string type, out int candela, out int dba);
            string effectiveMount = string.IsNullOrWhiteSpace(mount) ? "Ceiling" : mount.Trim();
            string slope = string.IsNullOrWhiteSpace(ceilingSlope) ? "FLAT" : ceilingSlope.Trim().ToUpperInvariant();

            double visibleSpacing = VisibleSpacing(candela);
            double audibleSpacing = AudibleSpacing(dba);
            double spacing = Math.Min(visibleSpacing, audibleSpacing);
            if (candela <= 0) spacing = audibleSpacing;
            if (dba <= 0) spacing = visibleSpacing;
            if (double.IsInfinity(spacing) || double.IsNaN(spacing) || spacing <= 0.0) spacing = 15.0;

            double slopeFactor = SlopeFactor(slope);
            spacing *= slopeFactor;
            if (string.Equals(effectiveMount, "Wall", StringComparison.OrdinalIgnoreCase)) spacing *= 0.85;

            double effectiveCoverage = Math.Max(225.0, spacing * spacing);
            SmokeDetectorPlacementRuleSet rules = new SmokeDetectorPlacementRuleSet
            {
                DetectorType = type,
                Mount = effectiveMount,
                CeilingSlope = slope,
                MaxSpacingFt = spacing,
                MinSpacingFt = Math.Max(5.0, spacing * 0.35),
                MaxCoverageAreaSqFt = effectiveCoverage,
                CoverageRadiusFt = spacing / Math.Sqrt(2.0),
                MaxDistanceFromWallsFt = spacing / 2.0,
                MinBoundaryClearanceFt = string.Equals(effectiveMount, "Wall", StringComparison.OrdinalIgnoreCase) ? 0.05 : 0.333,
                ExistingDetectorSeparationFt = Math.Max(5.0, spacing * 0.35),
                WallMountDropFromCeilingFt = 0.5,
                HvacSupplyRegisterClearanceFt = 3.0,
                ObstacleClearanceFt = 1.5,
                IsProvisional = true,
                Notes = "NFPA 72 Chapter 18 design basis: appliance=" + type
                    + ", candela=" + candela.ToString(CultureInfo.InvariantCulture)
                    + ", dBA=" + dba.ToString(CultureInfo.InvariantCulture)
                    + ", mount=" + effectiveMount + ", slope=" + slope
                    + ". The stricter of visible and audible coverage governs the effective spacing."
            };

            return rules;
        }

        private static void ParseDescriptor(string descriptor, out string type, out int candela, out int dba)
        {
            string[] parts = (descriptor ?? string.Empty).Split('|');
            type = parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]) ? "Notification Appliance" : parts[0].Trim();
            candela = 0;
            dba = 0;
            for (int i = 1; i < parts.Length; i++)
            {
                string[] pair = parts[i].Split('=');
                if (pair.Length != 2) continue;
                int value;
                if (!int.TryParse(pair[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) continue;
                if (string.Equals(pair[0], "candela", StringComparison.OrdinalIgnoreCase)) candela = Math.Max(0, value);
                if (string.Equals(pair[0], "dba", StringComparison.OrdinalIgnoreCase)) dba = Math.Max(0, value);
            }
        }

        private static double VisibleSpacing(int candela)
        {
            if (candela <= 15) return 30.0;
            if (candela <= 30) return 35.0;
            if (candela <= 75) return 40.0;
            if (candela <= 110) return 45.0;
            return 50.0;
        }

        private static double AudibleSpacing(int dba)
        {
            if (dba <= 0) return double.PositiveInfinity;
            if (dba < 85) return 20.0;
            if (dba < 90) return 25.0;
            if (dba < 95) return 30.0;
            return 35.0;
        }

        private static double SlopeFactor(string slope)
        {
            if (string.Equals(slope, "SLOPED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(slope, "PEAKED", StringComparison.OrdinalIgnoreCase)) return 0.80;
            if (string.Equals(slope, "STEPPED", StringComparison.OrdinalIgnoreCase)) return 0.75;
            return 1.0;
        }
    }
}
