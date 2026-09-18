using System;
using System.Collections.Generic;
using ClosedXML.Excel;
using FireProtection.Backend.Services.Catalog;

namespace FireProtection.Tests
{
    internal static class CatalogTemplateGenerator
    {
        // Bumped whenever the sample rows change so a regenerated workbook is distinguishable from an
        // earlier one authored the same day. Free-form string; the loader only requires it to be non-blank.
        internal const string CatalogVersion = "2026-09-02.2";

        // Type lists for the sprinkler families loaded in the host Revit model
        // (Project Browser > Families > Sprinklers). These strings must match the Revit
        // FamilySymbol.Name verbatim - placement resolves the symbol by (Family.Name, Symbol.Name).
        private static readonly string[] DryHorizontalSidewallTypes =
        {
            "1/2\" Dry Horizontal Sidewall"
        };

        private static readonly string[] DryPendentTypes =
        {
            "1/2\" Dry Pendent",
            "1/2\" Dry Pendent on Drop",
            "1/2\" Dry Pendent on Drop with Guard",
            "1/2\" Dry Pendent with Guard"
        };

        private static readonly string[] PendentTypes =
        {
            "1/2\" Pendent",
            "1/2\" Pendent on Drop",
            "1/2\" Pendent on Drop with Guard",
            "1/2\" Pendent with Guard",
            "3/4\" Pendent",
            "3/4\" Pendent on Drop",
            "3/4\" Pendent on Drop with Guard",
            "3/4\" Pendent with Guard"
        };

        public static void Generate(string path)
        {
            using (XLWorkbook wb = new XLWorkbook())
            {
                IXLWorksheet sprinkler = wb.Worksheets.Add(CatalogValidator.SheetSprinklers);
                BuildSprinklersSheet(sprinkler);

                IXLWorksheet smoke = wb.Worksheets.Add(CatalogValidator.SheetSmokeDetectors);
                BuildSmokeDetectorsSheet(smoke);

                IXLWorksheet na = wb.Worksheets.Add(CatalogValidator.SheetNotificationAppliances);
                BuildNotificationAppliancesSheet(na);

                wb.SaveAs(path);
            }
        }

        private static void BuildSprinklersSheet(IXLWorksheet sheet)
        {
            sheet.Cell(1, 1).Value = "CatalogVersion";
            sheet.Cell(1, 2).Value = CatalogVersion;

            sheet.Cell(2, 1).Value = "Category";
            sheet.Cell(2, 2).Value = "FamilyName";
            sheet.Cell(2, 3).Value = "TypeName";
            sheet.Cell(2, 4).Value = "HazardClass";
            sheet.Cell(2, 5).Value = "Mount";
            sheet.Cell(2, 6).Value = "Notes";

            // HazardClass is deliberately left blank on every row. The loader rejects a duplicate
            // (FamilyName, TypeName) as a hard error, so one type cannot be repeated once per hazard
            // class; and the room's hazard class is picked per room in the UI from
            // HazardClassOptions.All, not read from this sheet. Mount records the orientation
            // (Pendent / Sidewall) - recessed vs semi-recessed vs exposed is already in FamilyName.
            int r = 3;

            AddFamily(sheet, ref r, "Sprinkler - Dry - Horizontal Sidewall - Fully Recessed - Hosted",
                DryHorizontalSidewallTypes, "Sidewall", "Dry pipe, fully recessed trim, hosted");
            AddFamily(sheet, ref r, "Sprinkler - Dry - Horizontal Sidewall - Hosted",
                DryHorizontalSidewallTypes, "Sidewall", "Dry pipe, exposed, hosted");
            AddFamily(sheet, ref r, "Sprinkler - Dry - Horizontal Sidewall - Semi-Recessed - Hosted",
                DryHorizontalSidewallTypes, "Sidewall", "Dry pipe, semi-recessed trim, hosted");

            AddFamily(sheet, ref r, "Sprinkler - Dry - Pendent - Fully Recessed - Hosted",
                DryPendentTypes, "Pendent", "Dry pipe, fully recessed trim, hosted");
            AddFamily(sheet, ref r, "Sprinkler - Dry - Pendent - Hosted",
                DryPendentTypes, "Pendent", "Dry pipe, exposed, hosted");
            AddFamily(sheet, ref r, "Sprinkler - Dry - Pendent - Semi-Recessed - Hosted",
                DryPendentTypes, "Pendent", "Dry pipe, semi-recessed trim, hosted");

            AddFamily(sheet, ref r, "Sprinkler - Pendent - Fully Recessed - Hosted",
                PendentTypes, "Pendent", "Wet pipe, fully recessed trim, hosted");
            AddFamily(sheet, ref r, "Sprinkler - Pendent - Hosted",
                PendentTypes, "Pendent", "Wet pipe, exposed, hosted");
            AddFamily(sheet, ref r, "Sprinkler - Pendent - Semi-Recessed - Hosted",
                PendentTypes, "Pendent", "Wet pipe, semi-recessed trim, hosted");

            sheet.Columns().AdjustToContents();
        }

        private static void AddFamily(IXLWorksheet sheet, ref int r, string family, string[] types, string mount, string notes)
        {
            for (int i = 0; i < types.Length; i++)
            {
                AddSprinkler(sheet, ref r, family, types[i], null, mount, notes);
            }
        }

        private static void AddSprinkler(IXLWorksheet sheet, ref int r, string family, string type, string hazard, string mount, string notes)
        {
            sheet.Cell(r, 1).Value = "Sprinkler";
            sheet.Cell(r, 2).Value = family;
            sheet.Cell(r, 3).Value = type;
            if (!string.IsNullOrEmpty(hazard)) sheet.Cell(r, 4).Value = hazard;
            if (!string.IsNullOrEmpty(mount)) sheet.Cell(r, 5).Value = mount;
            if (!string.IsNullOrEmpty(notes)) sheet.Cell(r, 6).Value = notes;
            r++;
        }

        private static void BuildSmokeDetectorsSheet(IXLWorksheet sheet)
        {
            sheet.Cell(1, 1).Value = "CatalogVersion";
            sheet.Cell(1, 2).Value = CatalogVersion;

            sheet.Cell(2, 1).Value = "Category";
            sheet.Cell(2, 2).Value = "FamilyName";
            sheet.Cell(2, 3).Value = "TypeName";
            sheet.Cell(2, 4).Value = "DetectorType";
            sheet.Cell(2, 5).Value = "Mount";
            sheet.Cell(2, 6).Value = "CeilingSlope";
            sheet.Cell(2, 7).Value = "Notes";

            // The Smoke Detectors tab reads its Detector Type / Mount / Ceiling Slope dropdowns from the
            // distinct values in this sheet, so the sample rows have to span the ranges the UI must offer:
            // every DetectorType the validator knows, both Ceiling and Wall mounts, and all three slopes.
            int r = 3;
            AddSmoke(sheet, ref r, "Smoke Detector - Beam Receiver", "Standard", "Photoelectric", "Ceiling", "Flat", "Beam receiver");
            AddSmoke(sheet, ref r, "Smoke Detector - Beam Transmitter", "Standard", "Photoelectric", "Ceiling", "Flat", "Beam transmitter");
            AddSmoke(sheet, ref r, "Duct-smoke-detector", "Duct-smoke-detector", "Photoelectric", "Ceiling", "Flat", "Duct smoke detector");
            AddSmoke(sheet, ref r, "Smoke-detector", "Smoke-detector", "Photoelectric", "Ceiling", "Flat", "Generic smoke detector");
            AddSmoke(sheet, ref r, "Smoke Detector", "Air Sampling", "Aspirating", "Ceiling", "Flat", "Air sampling");
            AddSmoke(sheet, ref r, "Smoke Detector", "Ionization", "Ionization", "Ceiling", "Flat", null);
            AddSmoke(sheet, ref r, "Smoke Detector", "Photoelectric", "Photoelectric", "Ceiling", "Flat", null);
            AddSmoke(sheet, ref r, "Smoke Detector", "Plain", "Photoelectric", "Ceiling", "Flat", null);
            AddSmoke(sheet, ref r, "Smoke Detector", "Smoke Detector", "Photoelectric", "Ceiling", "Flat", null);

            sheet.Columns().AdjustToContents();
        }

        private static void AddSmoke(IXLWorksheet sheet, ref int r, string family, string type, string detector, string mount, string slope, string notes)
        {
            sheet.Cell(r, 1).Value = "SmokeDetector";
            sheet.Cell(r, 2).Value = family;
            sheet.Cell(r, 3).Value = type;
            sheet.Cell(r, 4).Value = detector;
            sheet.Cell(r, 5).Value = mount;
            sheet.Cell(r, 6).Value = slope;
            if (!string.IsNullOrEmpty(notes)) sheet.Cell(r, 7).Value = notes;
            r++;
        }

        private static void BuildNotificationAppliancesSheet(IXLWorksheet sheet)
        {
            sheet.Cell(1, 1).Value = "CatalogVersion";
            sheet.Cell(1, 2).Value = CatalogVersion;

            sheet.Cell(2, 1).Value = "Category";
            sheet.Cell(2, 2).Value = "FamilyName";
            sheet.Cell(2, 3).Value = "TypeName";
            sheet.Cell(2, 4).Value = "ApplianceType";
            sheet.Cell(2, 5).Value = "Candela";
            sheet.Cell(2, 6).Value = "NotificationDba";
            sheet.Cell(2, 7).Value = "Notes";

            // The Notification Appliances tab reads its Appliance Type / Candela / Notification dBA
            // dropdowns from the distinct values in this sheet, and the Candela / dBA level-default combo
            // from the (Candela, dBA) pairs that actually appear here. So the sample rows span every
            // ApplianceType the validator knows plus the standard UL-listed candela steps. A 0 in either
            // numeric column means "no such rating" (a strobe has no dBA, a horn has no candela) and is
            // filtered out of the dropdowns.
            int r = 3;
            AddNa(sheet, ref r, "Fire Alarm Horn - Wall Mounted", "Standard", "Horn", 0, 87, "Wall mounted horn");
            AddNa(sheet, ref r, "Fire Alarm Horn Strobe - Ceiling Mounted", "Standard", "HornStrobe", 15, 87, "Ceiling mounted horn strobe");
            AddNa(sheet, ref r, "Fire Alarm Horn Strobe - Wall Mounted", "Standard", "HornStrobe", 15, 87, "Wall mounted horn strobe");
            AddNa(sheet, ref r, "Fire Alarm Strobe Speaker - Ceiling Mounted", "Standard", "SpeakerStrobe", 15, 83, "Ceiling mounted speaker strobe");
            AddNa(sheet, ref r, "Fire Alarm Strobe Speaker - Wall Mounted", "Standard", "SpeakerStrobe", 15, 83, "Wall mounted speaker strobe");

            sheet.Columns().AdjustToContents();
        }

        private static void AddNa(IXLWorksheet sheet, ref int r, string family, string type, string appliance, int candela, int dba, string notes)
        {
            sheet.Cell(r, 1).Value = "NotificationAppliance";
            sheet.Cell(r, 2).Value = family;
            sheet.Cell(r, 3).Value = type;
            sheet.Cell(r, 4).Value = appliance;
            sheet.Cell(r, 5).Value = candela;
            sheet.Cell(r, 6).Value = dba;
            if (!string.IsNullOrEmpty(notes)) sheet.Cell(r, 7).Value = notes;
            r++;
        }
    }
}
