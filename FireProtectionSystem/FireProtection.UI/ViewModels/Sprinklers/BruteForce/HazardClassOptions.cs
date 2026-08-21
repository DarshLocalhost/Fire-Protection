using System.Collections.Generic;

namespace FireProtection.UI.ViewModels.Sprinklers.BruteForce
{
    public static class HazardClassOptions
    {
        public const string Light = "LIGHT";
        public const string OH1 = "OH1";
        public const string OH2 = "OH2";
        public const string EH1 = "EH1";
        public const string EH2 = "EH2";

        public static IReadOnlyList<string> All { get; } =
            new List<string>
            {
                Light,
                OH1,
                OH2,
                EH1,
                EH2
            };
    }
}