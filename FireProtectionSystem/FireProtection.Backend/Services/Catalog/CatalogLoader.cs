using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;

namespace FireProtection.Backend.Services.Catalog
{
    /// <summary>
    /// Fail-fast loader for the Excel catalog workbook. Returns a populated
    /// <see cref="Catalog"/> on success, or throws <see cref="CatalogLoadException"/>
    /// on any IO or schema problem. The loader is intentionally strict so that
    /// engineering risk is surfaced at load time rather than deep inside the
    /// eligibility / placement pipeline.
    /// </summary>
    public static class CatalogLoader
    {
        public static Catalog Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new CatalogLoadException("Catalog path is empty.");

            if (!File.Exists(path))
                throw new CatalogLoadException("Catalog file does not exist: " + path);

            Catalog catalog;
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (XLWorkbook wb = new XLWorkbook(fs))
                {
                    catalog = ReadWorkbook(wb);
                }
            }
            catch (CatalogLoadException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CatalogLoadException("Failed to read catalog workbook: " + ex.Message, ex);
            }

            catalog.SourcePath = path;

            CatalogValidationResult validation = CatalogValidator.Validate(catalog);
            if (validation.HasErrors)
            {
                throw new CatalogLoadException(
                    "Catalog failed validation (" + validation.Errors.Count + " error(s)).",
                    validation.Errors);
            }

            return catalog;
        }

        private static Catalog ReadWorkbook(XLWorkbook wb)
        {
            Catalog catalog = new Catalog();

            IXLWorksheet sprinklerSheet = GetSheetOrNull(wb, CatalogValidator.SheetSprinklers);
            IXLWorksheet smokeSheet = GetSheetOrNull(wb, CatalogValidator.SheetSmokeDetectors);
            IXLWorksheet naSheet = GetSheetOrNull(wb, CatalogValidator.SheetNotificationAppliances);

            ReadCatalogVersionHeader(sprinklerSheet ?? smokeSheet ?? naSheet, catalog);

            if (sprinklerSheet != null) catalog.Sprinklers = ReadSprinklers(sprinklerSheet);
            if (smokeSheet != null) catalog.SmokeDetectors = ReadSmokeDetectors(smokeSheet);
            if (naSheet != null) catalog.NotificationAppliances = ReadNotificationAppliances(naSheet);

            return catalog;
        }

        private static IXLWorksheet GetSheetOrNull(XLWorkbook wb, string name)
        {
            try
            {
                if (wb.Worksheets.TryGetWorksheet(name, out IXLWorksheet ws)) return ws;
            }
            catch
            {
            }
            return null;
        }

        private static void ReadCatalogVersionHeader(IXLWorksheet sheet, Catalog catalog)
        {
            if (sheet == null) return;
            IXLCell firstCell = sheet.FirstCellUsed();
            if (firstCell == null) return;
            IXLCell versionCell = firstCell.WorksheetRow().Cell(2);
            string first = firstCell.GetString();
            string second = versionCell.GetString();
            if (string.Equals(first, "CatalogVersion", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(second))
            {
                catalog.CatalogVersion = second.Trim();
            }
        }

        private static int ResolveDataStartRow(IXLWorksheet sheet)
        {
            // Row 1 is reserved for the CatalogVersion header ("CatalogVersion", "<value>") in column A.
            // The data sheet column header (with "FamilyName" in col 2) sits on the next row.
            // The first DATA row is the row after that.
            if (sheet == null) return 1;
            IXLCell first = sheet.FirstCellUsed();
            if (first == null) return 1;
            IXLRow row1 = first.WorksheetRow();
            string a1 = (row1.Cell(1).GetString() ?? string.Empty).Trim();
            if (!string.Equals(a1, "CatalogVersion", StringComparison.OrdinalIgnoreCase))
            {
                return row1.RowNumber();
            }
            int headerRow = row1.RowNumber() + 1;
            if (headerRow > sheet.LastRowUsed().RowNumber()) return headerRow;
            IXLRow headerCheck = sheet.Row(headerRow);
            string headerCol2 = (headerCheck.Cell(2).GetString() ?? string.Empty).Trim();
            if (string.Equals(headerCol2, "FamilyName", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerCol2, "DetectorType", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerCol2, "ApplianceType", StringComparison.OrdinalIgnoreCase))
            {
                return headerRow + 1;
            }
            return headerRow;
        }

        private static List<SprinklerCatalogRow> ReadSprinklers(IXLWorksheet sheet)
        {
            List<SprinklerCatalogRow> rows = new List<SprinklerCatalogRow>();
            if (sheet.LastRowUsed() == null) return rows;
            int firstDataRow = ResolveDataStartRow(sheet);
            int lastUsed = sheet.LastRowUsed().RowNumber();
            for (int r = firstDataRow; r <= lastUsed; r++)
            {
                IXLRow row = sheet.Row(r);
                if (IsRowEmpty(row)) continue;
                SprinklerCatalogRow entry = new SprinklerCatalogRow
                {
                    FamilyName = GetCellString(row, 2),
                    TypeName = GetCellString(row, 3),
                    HazardClass = GetCellString(row, 4),
                    Mount = GetCellString(row, 5),
                    Notes = GetCellString(row, 6)
                };
                if (string.IsNullOrWhiteSpace(entry.FamilyName) && string.IsNullOrWhiteSpace(entry.TypeName)
                    && string.IsNullOrWhiteSpace(entry.HazardClass) && string.IsNullOrWhiteSpace(entry.Mount)
                    && string.IsNullOrWhiteSpace(entry.Notes))
                {
                    continue;
                }
                rows.Add(entry);
            }
            return rows;
        }

        private static List<SmokeDetectorCatalogRow> ReadSmokeDetectors(IXLWorksheet sheet)
        {
            List<SmokeDetectorCatalogRow> rows = new List<SmokeDetectorCatalogRow>();
            if (sheet.LastRowUsed() == null) return rows;
            int firstDataRow = ResolveDataStartRow(sheet);
            int lastUsed = sheet.LastRowUsed().RowNumber();
            for (int r = firstDataRow; r <= lastUsed; r++)
            {
                IXLRow row = sheet.Row(r);
                if (IsRowEmpty(row)) continue;
                SmokeDetectorCatalogRow entry = new SmokeDetectorCatalogRow
                {
                    FamilyName = GetCellString(row, 2),
                    TypeName = GetCellString(row, 3),
                    DetectorType = GetCellString(row, 4),
                    Mount = GetCellString(row, 5),
                    CeilingSlope = GetCellString(row, 6),
                    Notes = GetCellString(row, 7)
                };
                rows.Add(entry);
            }
            return rows;
        }

        private static List<NotificationApplianceCatalogRow> ReadNotificationAppliances(IXLWorksheet sheet)
        {
            List<NotificationApplianceCatalogRow> rows = new List<NotificationApplianceCatalogRow>();
            if (sheet.LastRowUsed() == null) return rows;
            int firstDataRow = ResolveDataStartRow(sheet);
            int lastUsed = sheet.LastRowUsed().RowNumber();
            for (int r = firstDataRow; r <= lastUsed; r++)
            {
                IXLRow row = sheet.Row(r);
                if (IsRowEmpty(row)) continue;
                NotificationApplianceCatalogRow entry = new NotificationApplianceCatalogRow
                {
                    FamilyName = GetCellString(row, 2),
                    TypeName = GetCellString(row, 3),
                    ApplianceType = GetCellString(row, 4),
                    Candela = GetCellInt(row, 5),
                    NotificationDba = GetCellInt(row, 6),
                    Notes = GetCellString(row, 7)
                };
                rows.Add(entry);
            }
            return rows;
        }

        private static bool IsRowEmpty(IXLRow row)
        {
            IXLCells cells = row.CellsUsed();
            int n = 0;
            foreach (IXLCell c in cells)
            {
                n++;
                if (n > 50) break;
                if (!string.IsNullOrWhiteSpace(c.GetString())) return false;
            }
            return true;
        }

        private static string GetCellString(IXLRow row, int column)
        {
            try
            {
                IXLCell cell = row.Cell(column);
                if (cell == null) return string.Empty;
                return (cell.GetString() ?? string.Empty).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int GetCellInt(IXLRow row, int column)
        {
            try
            {
                IXLCell cell = row.Cell(column);
                if (cell == null) return 0;
                if (cell.TryGetValue<double>(out double d))
                {
                    return (int)Math.Round(d, MidpointRounding.AwayFromZero);
                }
                string s = (cell.GetString() ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(s)) return 0;
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return i;
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double dd))
                    return (int)Math.Round(dd, MidpointRounding.AwayFromZero);
            }
            catch
            {
            }
            return 0;
        }
    }
}
