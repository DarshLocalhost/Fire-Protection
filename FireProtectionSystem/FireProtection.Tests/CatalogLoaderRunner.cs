using System;
using FireProtection.Backend.Services.Catalog;

namespace FireProtection.Tests
{
    internal static class CatalogLoaderRunner
    {
        public static int Run(string path)
        {
            try
            {
                Catalog catalog = CatalogLoader.Load(path);
                Console.WriteLine("Catalog OK: version=" + (catalog.CatalogVersion ?? "<none>")
                    + " sprinklers=" + (catalog.Sprinklers == null ? 0 : catalog.Sprinklers.Count)
                    + " smokeDetectors=" + (catalog.SmokeDetectors == null ? 0 : catalog.SmokeDetectors.Count)
                    + " notificationAppliances=" + (catalog.NotificationAppliances == null ? 0 : catalog.NotificationAppliances.Count));
                return 0;
            }
            catch (CatalogLoadException ex)
            {
                Console.WriteLine("Catalog LOAD FAILED:");
                Console.WriteLine(ex.Message);
                return 2;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Catalog LOAD FAILED (unexpected): " + ex.Message);
                return 3;
            }
        }
    }
}
