using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using FireProtection.Backend.Services.Catalog;

namespace FireProtection.Tests
{
    internal static class CatalogLoaderTests
    {
        private static int _failures;

        public static void RunAll()
        {
            _failures = 0;
            TestValidWorkbookLoads();
            TestMissingCatalogVersionFails();
            TestDuplicateFamilyTypeFails();
            TestEmptyPathFails();
            TestMissingFileFails();
            TestUnknownHazardClassFallsBack();
            TestCatalogServiceLookups();
            TestCandelaRequiredForNotificationAppliance();
            TestAudibleOnlyNotificationApplianceLoads();
            TestNotificationApplianceWithNoRatingFails();
            TestTemplateGenerator();

            if (_failures == 0)
            {
                Console.WriteLine("CatalogLoaderTests: PASS");
            }
            else
            {
                Console.WriteLine("CatalogLoaderTests: " + _failures + " FAIL(s)");
                throw new Exception("CatalogLoaderTests failed");
            }
        }

        private static void Check(bool condition, string message)
        {
            if (condition) Console.WriteLine("  PASS: " + message);
            else
            {
                Console.WriteLine("  FAIL: " + message);
                _failures++;
            }
        }

        private static string WriteTempWorkbook(Action<XLWorkbook> populate)
        {
            string path = Path.Combine(Path.GetTempPath(), "fps_catalog_test_" + Guid.NewGuid().ToString("N") + ".xlsx");
            using (XLWorkbook wb = new XLWorkbook())
            {
                populate(wb);
                wb.SaveAs(path);
            }
            return path;
        }

        private static void TestValidWorkbookLoads()
        {
            Console.WriteLine("Test: valid catalog workbook loads");
            string path = WriteTempWorkbook(wb =>
            {
                IXLWorksheet ws = wb.Worksheets.Add("Sprinklers");
                ws.Cell(1, 1).Value = "CatalogVersion";
                ws.Cell(1, 2).Value = "2026-09-01";
                ws.Cell(2, 1).Value = "Category";
                ws.Cell(2, 2).Value = "FamilyName";
                ws.Cell(2, 3).Value = "TypeName";
                ws.Cell(2, 4).Value = "HazardClass";
                ws.Cell(3, 1).Value = "Sprinkler";
                ws.Cell(3, 2).Value = "F1";
                ws.Cell(3, 3).Value = "T1";
                ws.Cell(3, 4).Value = "Light";
            });
            try
            {
                Catalog c = CatalogLoader.Load(path);
                Check(c.CatalogVersion == "2026-09-01", "CatalogVersion parsed");
                Check(c.Sprinklers.Count == 1, "one sprinkler row read");
                Check(c.Sprinklers[0].FamilyName == "F1", "FamilyName parsed");
                Check(c.Sprinklers[0].TypeName == "T1", "TypeName parsed");
                Check(c.Sprinklers[0].HazardClass == "Light", "HazardClass parsed");
            }
            catch (Exception ex)
            {
                Check(false, "valid workbook threw: " + ex.Message);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void TestMissingCatalogVersionFails()
        {
            Console.WriteLine("Test: missing CatalogVersion header fails");
            string path = WriteTempWorkbook(wb =>
            {
                IXLWorksheet ws = wb.Worksheets.Add("Sprinklers");
                ws.Cell(1, 1).Value = "FamilyName";
                ws.Cell(1, 2).Value = "F1";
                ws.Cell(1, 3).Value = "T1";
            });
            try
            {
                try
                {
                    CatalogLoader.Load(path);
                    Check(false, "expected CatalogLoadException");
                }
                catch (CatalogLoadException ex)
                {
                    Check(ex.Issues.Count > 0, "CatalogLoadException carries at least one issue");
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void TestDuplicateFamilyTypeFails()
        {
            Console.WriteLine("Test: duplicate (FamilyName, TypeName) fails");
            string path = WriteTempWorkbook(wb =>
            {
                IXLWorksheet ws = wb.Worksheets.Add("Sprinklers");
                ws.Cell(1, 1).Value = "CatalogVersion";
                ws.Cell(1, 2).Value = "2026-09-01";
                ws.Cell(2, 1).Value = "Category";
                ws.Cell(2, 2).Value = "FamilyName";
                ws.Cell(2, 3).Value = "TypeName";
                ws.Cell(2, 4).Value = "HazardClass";
                ws.Cell(3, 1).Value = "Sprinkler";
                ws.Cell(3, 2).Value = "F1";
                ws.Cell(3, 3).Value = "T1";
                ws.Cell(3, 4).Value = "Light";
                ws.Cell(4, 1).Value = "Sprinkler";
                ws.Cell(4, 2).Value = "F1";
                ws.Cell(4, 3).Value = "T1";
                ws.Cell(4, 4).Value = "Light";
            });
            try
            {
                try
                {
                    CatalogLoader.Load(path);
                    Check(false, "expected CatalogLoadException for duplicate");
                }
                catch (CatalogLoadException ex)
                {
                    bool foundDup = false;
                    foreach (CatalogIssue issue in ex.Issues)
                    {
                        if (issue.Message != null && issue.Message.ToLowerInvariant().Contains("duplicate")) { foundDup = true; break; }
                    }
                    Check(foundDup, "duplicate (Family, Type) reported");
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void TestEmptyPathFails()
        {
            Console.WriteLine("Test: empty path fails");
            try
            {
                CatalogLoader.Load("");
                Check(false, "empty path should have thrown");
            }
            catch (CatalogLoadException)
            {
                Check(true, "empty path threw CatalogLoadException");
            }
        }

        private static void TestMissingFileFails()
        {
            Console.WriteLine("Test: missing file fails");
            try
            {
                CatalogLoader.Load(@"C:\nonexistent\no-such-folder\catalog.xlsx");
                Check(false, "missing file should have thrown");
            }
            catch (CatalogLoadException)
            {
                Check(true, "missing file threw CatalogLoadException");
            }
        }

        private static void TestEmptyWorkbookFails()
        {
            // (intentionally removed — covered by TestMissingCatalogVersionFails; ClosedXML
            // refuses to save a workbook with zero sheets so the empty-workbook case is not
            // a meaningful path through the loader.)
        }

        private static void TestUnknownHazardClassFallsBack()
        {
            Console.WriteLine("Test: unknown HazardClass emits WARNING and does not fail load");
            string path = WriteTempWorkbook(wb =>
            {
                IXLWorksheet ws = wb.Worksheets.Add("Sprinklers");
                ws.Cell(1, 1).Value = "CatalogVersion";
                ws.Cell(1, 2).Value = "2026-09-01";
                ws.Cell(2, 1).Value = "Category";
                ws.Cell(2, 2).Value = "FamilyName";
                ws.Cell(2, 3).Value = "TypeName";
                ws.Cell(2, 4).Value = "HazardClass";
                ws.Cell(3, 1).Value = "Sprinkler";
                ws.Cell(3, 2).Value = "F1";
                ws.Cell(3, 3).Value = "T1";
                ws.Cell(3, 4).Value = "EXOTIC";
            });
            try
            {
                Catalog c = CatalogLoader.Load(path);
                Check(c.Sprinklers[0].HazardClass == "EXOTIC", "HazardClass preserved as-is");
                Check(c.TotalRowCount == 1, "row count = 1");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void TestCatalogServiceLookups()
        {
            Console.WriteLine("Test: CatalogService lookups (families, types, hazard)");
            string path = WriteTempWorkbook(wb =>
            {
                IXLWorksheet ws = wb.Worksheets.Add("Sprinklers");
                ws.Cell(1, 1).Value = "CatalogVersion";
                ws.Cell(1, 2).Value = "2026-09-01";
                ws.Cell(2, 1).Value = "Category";
                ws.Cell(2, 2).Value = "FamilyName";
                ws.Cell(2, 3).Value = "TypeName";
                ws.Cell(2, 4).Value = "HazardClass";
                ws.Cell(3, 1).Value = "Sprinkler"; ws.Cell(3, 2).Value = "F1"; ws.Cell(3, 3).Value = "T1"; ws.Cell(3, 4).Value = "Light";
                ws.Cell(4, 1).Value = "Sprinkler"; ws.Cell(4, 2).Value = "F1"; ws.Cell(4, 3).Value = "T2"; ws.Cell(4, 4).Value = "OH1";
                ws.Cell(5, 1).Value = "Sprinkler"; ws.Cell(5, 2).Value = "F2"; ws.Cell(5, 3).Value = "T1"; ws.Cell(5, 4).Value = "OH2";
            });
            try
            {
                Catalog c = CatalogLoader.Load(path);
                CatalogService svc = CatalogService.FromCatalog(c);
                IReadOnlyList<string> fams = svc.GetSprinklerFamilies();
                Check(fams.Count == 2, "two families (F1, F2)");
                IReadOnlyList<string> f1Types = svc.GetSprinklerTypesForFamily("F1");
                Check(f1Types.Count == 2, "F1 has two types");
                string hazard = svc.GetHazardClassForSprinkler("F1", "T1");
                Check(hazard == "Light", "F1/T1 hazard = Light");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void TestTemplateGenerator()
        {
            Console.WriteLine("Test: template generator produces a valid catalog (round-trip)");
            string path = Path.Combine(Path.GetTempPath(), "fps_template_" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                CatalogTemplateGenerator.Generate(path);
                Catalog c = CatalogLoader.Load(path);
                Check(c.CatalogVersion == CatalogTemplateGenerator.CatalogVersion,
                    "template CatalogVersion = " + CatalogTemplateGenerator.CatalogVersion);
                Check(c.Sprinklers.Count >= 5, "template has >= 5 sprinkler rows (got " + c.Sprinklers.Count + ")");
                Check(c.SmokeDetectors.Count >= 3, "template has >= 3 smoke detector rows (got " + c.SmokeDetectors.Count + ")");
                Check(c.NotificationAppliances.Count >= 3, "template has >= 3 notification appliance rows (got " + c.NotificationAppliances.Count + ")");

                CatalogService svc = CatalogService.FromCatalog(c);
                Check(svc.GetSprinklerFamilies().Count >= 3, "template exposes >= 3 sprinkler families");
                Check(svc.GetSprinklerTypesForFamily("Sprinkler - Pendent - Hosted").Count >= 3,
                    "Pendent family has >= 3 types");
            }
            catch (Exception ex)
            {
                Check(false, "template round-trip failed: " + ex.Message);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void TestCandelaRequiredForNotificationAppliance()
        {
            Console.WriteLine("Test: NotificationAppliance without Candela fails");
            string path = WriteTempWorkbook(wb =>
            {
                IXLWorksheet ws = wb.Worksheets.Add("NotificationAppliances");
                ws.Cell(1, 1).Value = "CatalogVersion";
                ws.Cell(1, 2).Value = "2026-09-01";
                ws.Cell(2, 1).Value = "Category";
                ws.Cell(2, 2).Value = "FamilyName";
                ws.Cell(2, 3).Value = "TypeName";
                ws.Cell(2, 4).Value = "ApplianceType";
                ws.Cell(2, 5).Value = "Candela";
                ws.Cell(2, 6).Value = "NotificationDba";
                ws.Cell(3, 1).Value = "NotificationAppliance";
                ws.Cell(3, 2).Value = "F1";
                ws.Cell(3, 3).Value = "T1";
                ws.Cell(3, 4).Value = "Strobe";
                ws.Cell(3, 5).Value = ""; // missing Candela
                ws.Cell(3, 6).Value = 0;  // missing dBA
            });
            try
            {
                try
                {
                    CatalogLoader.Load(path);
                    Check(false, "expected CatalogLoadException for missing Candela");
                }
                catch (CatalogLoadException ex)
                {
                    bool found = false;
                    foreach (CatalogIssue issue in ex.Issues)
                    {
                        if (issue.Code == "ERROR"
                            && issue.Column == "Candela") { found = true; break; }
                    }
                    Check(found, "missing Candela reported");
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>
        /// A Horn / Speaker / Chime has no visible (candela) rating, so a blank Candela on an
        /// audible-only row must load cleanly. Only a Strobe-bearing type requires one, and only a
        /// row with neither rating is an error - see the paired
        /// <see cref="TestCandelaRequiredForNotificationAppliance"/> and
        /// <see cref="TestNotificationApplianceWithNoRatingFails"/>.
        /// </summary>
        private static void TestAudibleOnlyNotificationApplianceLoads()
        {
            Console.WriteLine("Test: audible-only NotificationAppliance (no Candela) loads");
            string path = WriteTempWorkbook(wb =>
            {
                IXLWorksheet ws = wb.Worksheets.Add("NotificationAppliances");
                ws.Cell(1, 1).Value = "CatalogVersion";
                ws.Cell(1, 2).Value = "2026-09-01";
                ws.Cell(2, 1).Value = "Category";
                ws.Cell(2, 2).Value = "FamilyName";
                ws.Cell(2, 3).Value = "TypeName";
                ws.Cell(2, 4).Value = "ApplianceType";
                ws.Cell(2, 5).Value = "Candela";
                ws.Cell(2, 6).Value = "NotificationDba";
                ws.Cell(3, 1).Value = "NotificationAppliance";
                ws.Cell(3, 2).Value = "F1";
                ws.Cell(3, 3).Value = "Wall Horn 87dBA";
                ws.Cell(3, 4).Value = "Horn";
                ws.Cell(3, 5).Value = "";  // no candela - a horn has no visible rating
                ws.Cell(3, 6).Value = 87;
            });
            try
            {
                Catalog catalog = CatalogLoader.Load(path);
                Check(catalog.NotificationAppliances.Count == 1, "audible-only row loaded");
                Check(catalog.NotificationAppliances[0].Candela == 0, "Candela stays 0 for an audible-only row");
                Check(catalog.NotificationAppliances[0].NotificationDba == 87, "dBA preserved (87)");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>A row with neither a candela nor a dBA rating carries no placeable device data.</summary>
        private static void TestNotificationApplianceWithNoRatingFails()
        {
            Console.WriteLine("Test: NotificationAppliance with neither Candela nor dBA fails");
            string path = WriteTempWorkbook(wb =>
            {
                IXLWorksheet ws = wb.Worksheets.Add("NotificationAppliances");
                ws.Cell(1, 1).Value = "CatalogVersion";
                ws.Cell(1, 2).Value = "2026-09-01";
                ws.Cell(2, 1).Value = "Category";
                ws.Cell(2, 2).Value = "FamilyName";
                ws.Cell(2, 3).Value = "TypeName";
                ws.Cell(2, 4).Value = "ApplianceType";
                ws.Cell(2, 5).Value = "Candela";
                ws.Cell(2, 6).Value = "NotificationDba";
                ws.Cell(3, 1).Value = "NotificationAppliance";
                ws.Cell(3, 2).Value = "F1";
                ws.Cell(3, 3).Value = "T1";
                ws.Cell(3, 4).Value = "Chime"; // audible type, so the dBA rule fires too
                ws.Cell(3, 5).Value = "";
                ws.Cell(3, 6).Value = "";
            });
            try
            {
                try
                {
                    CatalogLoader.Load(path);
                    Check(false, "expected CatalogLoadException for a row with no rating");
                }
                catch (CatalogLoadException ex)
                {
                    bool found = false;
                    foreach (CatalogIssue issue in ex.Issues)
                    {
                        if (issue.Code == "ERROR"
                            && issue.Column == "Candela/NotificationDba") { found = true; break; }
                    }
                    Check(found, "row with neither rating reported");
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
