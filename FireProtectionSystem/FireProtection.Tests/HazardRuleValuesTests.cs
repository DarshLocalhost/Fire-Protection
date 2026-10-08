using System;
using FireProtection.Backend.Models.Hazard;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce;

namespace FireProtection.Tests
{
    /// <summary>
    /// Locks the per-hazard-class sprinkler rule values against the client source
    /// (NFPA 13 (2002) via the NFSA textbook "Layout, Detail and Calculation of Fire Sprinkler
    /// Systems"): max spacing 15 ft light/ordinary &amp; 12 ft extra (p.218), min spacing 6 ft
    /// cold-soldering (p.218), max wall distance = ½ max spacing (p.219), coverage areas Table 19-1
    /// (p.221). These are still provisional — the suite also asserts HasApprovedRules stays false and
    /// every set is flagged IsProvisional. If someone edits a number, this test says so.
    /// </summary>
    public static class HazardRuleValuesTests
    {
        private static int _failures;

        public static void RunAll()
        {
            _failures = 0;
            var rules = new DefaultHazardPlacementRules();

            Check(!rules.HasApprovedRules, "HasApprovedRules stays false (provisional, review-required)");

            // hazard, maxSpacing, minSpacing, maxCoverageArea, maxWallDistance (= ½ spacing)
            Expect(rules, HazardClass.Light, 15.0, 6.0, 225.0, 7.5);
            Expect(rules, HazardClass.OH1, 15.0, 6.0, 130.0, 7.5);
            Expect(rules, HazardClass.OH2, 15.0, 6.0, 130.0, 7.5);
            Expect(rules, HazardClass.EH1, 12.0, 6.0, 100.0, 6.0);
            Expect(rules, HazardClass.EH2, 12.0, 6.0, 100.0, 6.0);

            if (_failures > 0)
            {
                Console.WriteLine("  " + _failures + " HazardRuleValuesTests check(s) failed");
                throw new Exception("HazardRuleValuesTests failed");
            }
        }

        private static void Expect(
            DefaultHazardPlacementRules rules,
            HazardClass hazard,
            double maxSpacing,
            double minSpacing,
            double maxCoverageArea,
            double maxWallDistance)
        {
            HazardPlacementRuleSet r = rules.GetRules(hazard);
            Check(r.IsProvisional, hazard + ": flagged IsProvisional");
            Check(Near(r.MaxSpacingFt, maxSpacing), hazard + ": MaxSpacingFt=" + maxSpacing + " (got " + r.MaxSpacingFt + ")");
            Check(Near(r.MinSpacingFt, minSpacing), hazard + ": MinSpacingFt=" + minSpacing + " (got " + r.MinSpacingFt + ")");
            Check(Near(r.MaxCoverageAreaSqFt, maxCoverageArea), hazard + ": MaxCoverageAreaSqFt=" + maxCoverageArea + " (got " + r.MaxCoverageAreaSqFt + ")");
            Check(Near(r.MaxDistanceFromWallsFt, maxWallDistance), hazard + ": MaxDistanceFromWallsFt=" + maxWallDistance + " = ½ spacing (got " + r.MaxDistanceFromWallsFt + ")");
            // Cold-soldering minimum is universal 6 ft — the existing-sprinkler separation matches it.
            Check(Near(r.ExistingSprinklerSeparationFt, 6.0), hazard + ": ExistingSprinklerSeparationFt=6 (cold-soldering, got " + r.ExistingSprinklerSeparationFt + ")");
        }

        private static bool Near(double a, double b) { return Math.Abs(a - b) < 1e-6; }

        private static void Check(bool condition, string message)
        {
            if (condition) Console.WriteLine("  PASS: " + message);
            else { Console.WriteLine("  FAIL: " + message); _failures++; }
        }
    }
}
