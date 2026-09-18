using System;
using FireProtection.Backend.Services.Placement.NotificationAppliances.Final.BruteForce;

namespace FireProtection.Tests
{
    internal static class NotificationApplianceRulesTests
    {
        public static void RunAll()
        {
            Nfpa72NotificationApplianceRules rules = new Nfpa72NotificationApplianceRules();

            var lowCandela = rules.GetRules("Strobe|candela=15|dba=0", "Ceiling", "FLAT");
            Check(Math.Abs(lowCandela.MaxSpacingFt - 30.0) < 0.001, "15 cd strobe uses the low-candela visible envelope");

            var highCandela = rules.GetRules("Strobe|candela=110|dba=0", "Ceiling", "FLAT");
            Check(highCandela.MaxSpacingFt > lowCandela.MaxSpacingFt, "higher candela changes visible spacing");

            var combined = rules.GetRules("HornStrobe|candela=15|dba=87", "Ceiling", "FLAT");
            Check(Math.Abs(combined.MaxSpacingFt - 25.0) < 0.001, "combined appliance uses the stricter audible limit");

            var wallSloped = rules.GetRules("HornStrobe|candela=15|dba=87", "Wall", "SLOPED");
            Check(wallSloped.Mount == "Wall", "wall appliance keeps wall mount");
            Check(wallSloped.MaxSpacingFt < combined.MaxSpacingFt, "wall and sloped constraints reduce spacing");
            Check(wallSloped.IsProvisional, "unapproved project design basis remains visibly provisional");

            Console.WriteLine("NotificationApplianceRulesTests: PASS");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + message);
            Console.WriteLine("  PASS: " + message);
        }
    }
}