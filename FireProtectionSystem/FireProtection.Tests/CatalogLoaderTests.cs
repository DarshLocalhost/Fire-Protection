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
            TestPerTypeNumericColumnsLoad();
            TestBlankPerTypeColumnsFallBackToNull();
            TestOutOfRangePerTypeValueWarnsNotFails();
            TestSprinklerEntryLookup();
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

        /// <summary>
        /// Writes a Sprinklers sheet carrying the full header (cols 1-15) plus a single data row,
        /// letting the caller fill the optional per-type cells (7-15). Column indices mirror
        /// <see cref="CatalogLoader.ReadSprinklers"/> exactly.
        /// </summary>
        private static string WriteSprinklerWorkbook(Action<IXLWorksheet> fillRow3)
        {
            return WriteTempWorkbook(wb =>
            {
                IXLWorksheet ws = wb.Worksheets.Add("Sprinklers");
                ws.Cell(1, 1).Value = "CatalogVersion";
                ws.Cell(1, 2).Value = "2026-09-01";
                ws.Cell(2, 1).Value = "Category";
                ws.Cell(2, 2).Value = "FamilyName";
                ws.Cell(2, 3).Value = "TypeName";
                ws.Cell(2, 4).Value = "HazardClass";
                ws.Cell(2, 5).Value = "Mount";
                ws.Cell(2, 6).Value = "Notes";
                ws.Cell(2, 7).Value = "SprinklerClass";
                ws.Cell(2, 8).Value = "MaxCoverageAreaSqFt";
                ws.Cell(2, 9).Value = "MaxSpacingFt";
                ws.Cell(2, 10).Value = "MinSpacingFt";
                ws.Cell(2, 11).Value = "CoverageRadiusFt";
                ws.Cell(2, 12).Value = "KFactor";
                ws.Cell(2, 13).Value = "ResponseType";
                ws.Cell(2, 14).Value = "TempRatingF";
                ws.Cell(2, 15).Value = "DeflectorToCeilingIn";
                ws.Cell(3, 1).Value = "Sprinkler";
                ws.Cell(3, 2).Value = "F1";
                ws.Cell(3, 3).Value = "T1";
                fillRow3(ws);
            });
        }

        private static void TestPerTypeNumericColumnsLoad()
        {
            Console.WriteLine("Test: per-type numeric columns (7-15) load into the row");
            string path = WriteSprinklerWorkbook(ws =>
            {
                ws.Cell(3, 7).Value = "ExtendedCoverage";
                ws.Cell(3, 8).Value = 400;
                ws.Cell(3, 9).Value = 20;
                ws.Cell(3, 10).Value = 8;
                ws.Cell(3, 11).Value = 10;
                ws.Cell(3, 12).Value = 11.2;
                ws.Cell(3, 13).Value = "QR";
                ws.Cell(3, 14).Value = 200;
                ws.Cell(3, 15).Value = 4;
            });
            try
            {
                Catalog c = CatalogLoader.Load(path);
                SprinklerCatalogRow row = c.Sprinklers[0];
                Check(row.SprinklerClass == "ExtendedCoverage", "SprinklerClass parsed (col 7)");
                Check(row.MaxCoverageAreaSqFt == 400, "MaxCoverageAreaSqFt parsed (col 8)");
                Check(row.MaxSpacingFt == 20, "MaxSpacingFt parsed (col 9)");
                Check(row.MinSpacingFt == 8, "MinSpacingFt parsed (col 10)");
                Check(row.CoverageRadiusFt == 10, "CoverageRadiusFt parsed (col 11)");
                Check(row.KFactor == 11.2, "KFactor parsed (col 12)");
                Check(row.ResponseType == "QR", "ResponseType parsed (col 13)");
                Check(row.TempRatingF == 200, "TempRatingF parsed (col 14)");
                Check(row.DeflectorToCeilingIn == 4, "DeflectorToCeilingIn parsed (col 15)");
            }
            catch (Exception ex)
            {
                Check(false, "per-type columns threw: " + ex.Message);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void TestBlankPerTypeColumnsFallBackToNull()
        {
            Console.WriteLine("Test: blank per-type cells load as null (engine falls back to hazard default)");
            // Row 3 carries only the required identity cells; every optional column is left blank.
            string path = WriteSprinklerWorkbook(ws => { });
            try
            {
                Catalog c = CatalogLoader.Load(path);
                SprinklerCatalogRow row = c.Sprinklers[0];
                Check(string.IsNullOrEmpty(row.SprinklerClass), "blank SprinklerClass -> empty");
                Check(!row.MaxCoverageAreaSqFt.HasValue, "blank MaxCoverageAreaSqFt -> null (not 0)");
                Check(!row.MaxSpacingFt.HasValue, "blank MaxSpacingFt -> null (not 0)");
                Check(!row.MinSpacingFt.HasValue, "blank MinSpacingFt -> null");
                Check(!row.CoverageRadiusFt.HasValue, "blank CoverageRadiusFt -> null");
                Check(!row.KFactor.HasValue, "blank KFactor -> null");
                Check(!row.TempRatingF.HasValue, "blank TempRatingF -> null");
                Check(!row.DeflectorToCeilingIn.HasValue, "blank DeflectorToCeilingIn -> null");
            }
            catch (Exception ex)
            {
                Check(false, "blank per-type columns threw: " + ex.Message);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void TestOutOfRangePerTypeValueWarnsNotFails()
        {
            Console.WriteLine("Test: out-of-range / unknown per-type values WARN, do not fail the load");
            string path = WriteSprinklerWorkbook(ws =>
            {
                ws.Cell(3, 7).Value = "Bogus";   // unknown SprinklerClass -> warning
                ws.Cell(3, 8).Value = -5;         // non-positive coverage -> warning
                ws.Cell(3, 9).Value = 10;         // MaxSpacing 10
                ws.Cell(3, 10).Value = 14;        // MinSpacing 14 > MaxSpacing -> warning
                ws.Cell(3, 13).Value = "XX";      // unknown ResponseType -> warning
            });
            try
            {
                // A load that only produces WARNINGs must succeed (no throw) and keep the row.
                Catalog c = CatalogLoader.Load(path);
                Check(c.Sprinklers.Count == 1, "row with out-of-range values still loads (warnings only)");

                // The validator must surface these as WARNINGs, never ERRORs.
                CatalogValidationResult v = CatalogValidator.Validate(c);
                Check(!v.HasErrors, "out-of-range per-type values produce no ERRORs");
                int warnings = 0;
                foreach (CatalogIssue i in v.Issues)
                    if (i != null && i.Code == CatalogValidator.SeverityWarning) warnings++;
                Check(warnings >= 3, "unknown class + negative coverage + min>max each warn (got " + warnings + ")");
            }
            catch (Exception ex)
            {
                Check(false, "out-of-range per-type values should not throw: " + ex.Message);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void TestSprinklerEntryLookup()
        {
            Console.WriteLine("Test: CatalogService.GetSprinklerEntry surfaces per-type values");
            string path = WriteSprinklerWorkbook(ws =>
            {
                ws.Cell(3, 5).Value = "Sidewall";
                ws.Cell(3, 7).Value = "Sidewall";
                ws.Cell(3, 8).Value = 196;
                ws.Cell(3, 9).Value = 14;
                ws.Cell(3, 12).Value = 5.6;
            });
            try
            {
                Catalog c = CatalogLoader.Load(path);
                FireProtection.Backend.Services.Catalog.CatalogService svc =
                    FireProtection.Backend.Services.Catalog.CatalogService.FromCatalog(c);

                FireProtection.UI.Services.SprinklerCatalogEntry entry = svc.GetSprinklerEntry("F1", "T1");
                Check(entry != null, "entry found for (F1, T1)");
                Check(entry.SprinklerClass == "Sidewall", "entry SprinklerClass = Sidewall");
                Check(entry.MaxCoverageAreaSqFt == 196, "entry MaxCoverageAreaSqFt = 196");
                Check(entry.MaxSpacingFt == 14, "entry MaxSpacingFt = 14");
                Check(entry.KFactor == 5.6, "entry KFactor = 5.6");

                // A type the workbook does not list returns null (no fabricated data).
                Check(svc.GetSprinklerEntry("F1", "NoSuchType") == null, "missing type -> null entry");

                IReadOnlyList<FireProtection.UI.Services.SprinklerCatalogEntry> forFamily =
                    svc.GetSprinklerEntriesForFamily("F1");
                Check(forFamily.Count == 1, "GetSprinklerEntriesForFamily returns the family's one type");
            }
            catch (Exception ex)
            {
                Check(false, "GetSprinklerEntry lookup threw: " + ex.Message);
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
