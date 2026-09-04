using System;
using System.Collections.Generic;
using System.IO;

namespace FireProtection.CatalogStandalone
{
    internal static class Program
    {
        private static int _failures;

        private static int Main(string[] args)
        {
            if (args != null && args.Length >= 1)
            {
                if (string.Equals(args[0], "generate-template", StringComparison.OrdinalIgnoreCase))
                {
                    string path = args.Length >= 2 ? args[1] : "CatalogTemplate.xlsx";
                    FireProtection.Tests.CatalogTemplateGenerator.Generate(path);
                    Console.WriteLine("Generated catalog template at: " + Path.GetFullPath(path));
                    return 0;
                }
                if (string.Equals(args[0], "validate-catalog", StringComparison.OrdinalIgnoreCase))
                {
                    string path = args.Length >= 2 ? args[1] : "CatalogTemplate.xlsx";
                    return FireProtection.Tests.CatalogLoaderRunner.Run(path);
                }
            }

            _failures = 0;
            Console.WriteLine("Test: CatalogLoaderTests");
            FireProtection.Tests.CatalogLoaderTests.RunAll();
            Console.WriteLine();
            Console.WriteLine("Test: BruteForceOverrideTests");
            FireProtection.Tests.BruteForceOverrideTests.RunAll();
            Console.WriteLine();
            if (_failures == 0)
            {
                Console.WriteLine("ALL CATALOG + OVERRIDE TESTS PASSED");
                return 0;
            }
            Console.WriteLine(_failures + " TEST(S) FAILED");
            return 1;
        }
    }
}
