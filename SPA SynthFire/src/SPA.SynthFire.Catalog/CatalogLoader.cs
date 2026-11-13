using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using SPA.SynthFire.Core.Catalog;

namespace SPA.SynthFire.Catalog;

/// <summary>
/// Fail-fast Excel catalog loader. Reads the workbook picked by the user each
/// session (never written back) and produces an in-memory <see cref="Core.Catalog.Catalog"/>.
/// Any IO, missing-sheet, header-mismatch or bad-cell problem throws
/// <see cref="CatalogLoadException"/> so a malformed catalog cannot silently
/// yield a partial device list.
/// </summary>
public static class CatalogLoader
{
    public static Core.Catalog.Catalog LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new CatalogLoadException("Catalog path was null or empty.");
        if (!File.Exists(path))
            throw new CatalogLoadException($"Catalog file not found: {path}");

        try
        {
            using var workbook = new XLWorkbook(path);

            var sprinklerSheet = GetSheet(workbook, CatalogSchema.SprinklersSheet);
            var detectorSheet = GetSheet(workbook, CatalogSchema.SmokeDetectorsSheet);

            string version = ReadVersion(sprinklerSheet);

            ValidateHeaders(sprinklerSheet, CatalogSchema.SprinklerHeaders);
            ValidateHeaders(detectorSheet, CatalogSchema.SmokeDetectorHeaders);

            var sprinklers = ReadSprinklers(sprinklerSheet);
            var detectors = ReadDetectors(detectorSheet);

            return new Core.Catalog.Catalog
            {
                Version = version,
                Sprinklers = sprinklers,
                SmokeDetectors = detectors
            };
        }
        catch (CatalogLoadException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CatalogLoadException($"Failed to read catalog '{path}': {ex.Message}", ex);
        }
    }

    private static IXLWorksheet GetSheet(XLWorkbook workbook, string name)
    {
        if (!workbook.Worksheets.TryGetWorksheet(name, out var sheet))
            throw new CatalogLoadException($"Catalog is missing required sheet '{name}'.");
        return sheet;
    }

    private static string ReadVersion(IXLWorksheet sheet)
    {
        string raw = sheet.Cell(CatalogSchema.VersionRow, 2).GetString().Trim();
        if (string.IsNullOrEmpty(raw))
            throw new CatalogLoadException(
                $"Catalog version missing (sheet '{sheet.Name}', row {CatalogSchema.VersionRow}, column 2).");
        return raw;
    }

    private static void ValidateHeaders(IXLWorksheet sheet, string[] expected)
    {
        for (int i = 0; i < expected.Length; i++)
        {
            string actual = sheet.Cell(CatalogSchema.HeaderRow, i + 1).GetString().Trim();
            if (!string.Equals(actual, expected[i], StringComparison.OrdinalIgnoreCase))
            {
                throw new CatalogLoadException(
                    $"Sheet '{sheet.Name}' column {i + 1}: expected header '{expected[i]}' but found '{actual}'.");
            }
        }
    }

    private static List<SprinklerCatalogRow> ReadSprinklers(IXLWorksheet sheet)
    {
        var rows = new List<SprinklerCatalogRow>();
        foreach (int r in DataRows(sheet))
        {
            string family = RequireString(sheet, r, 1, "Family");
            string type = RequireString(sheet, r, 2, "Type");
            double kFactor = RequirePositiveDouble(sheet, r, 3, "KFactor");
            var orientation = RequireEnum<SprinklerOrientation>(sheet, r, 4, "Orientation");
            string hazard = RequireString(sheet, r, 5, "HazardClassId");
            string standard = RequireString(sheet, r, 6, "StandardId");
            string temp = sheet.Cell(r, 7).GetString().Trim();

            rows.Add(new SprinklerCatalogRow
            {
                Family = family,
                Type = type,
                KFactor = kFactor,
                Orientation = orientation,
                HazardClassId = hazard,
                StandardId = standard,
                TemperatureRating = temp
            });
        }
        return rows;
    }

    private static List<SmokeDetectorCatalogRow> ReadDetectors(IXLWorksheet sheet)
    {
        var rows = new List<SmokeDetectorCatalogRow>();
        foreach (int r in DataRows(sheet))
        {
            rows.Add(new SmokeDetectorCatalogRow
            {
                Family = RequireString(sheet, r, 1, "Family"),
                Type = RequireString(sheet, r, 2, "Type"),
                Kind = RequireEnum<DetectorKind>(sheet, r, 3, "Kind"),
                StandardId = RequireString(sheet, r, 4, "StandardId")
            });
        }
        return rows;
    }

    private static IEnumerable<int> DataRows(IXLWorksheet sheet)
    {
        var used = sheet.RangeUsed();
        if (used is null) yield break;

        int lastRow = used.LastRow().RowNumber();
        for (int r = CatalogSchema.FirstDataRow; r <= lastRow; r++)
        {
            // Skip blank rows (blank first cell = gap/end).
            if (sheet.Cell(r, 1).GetString().Trim().Length == 0) continue;
            yield return r;
        }
    }

    private static string RequireString(IXLWorksheet sheet, int row, int col, string field)
    {
        string value = sheet.Cell(row, col).GetString().Trim();
        if (string.IsNullOrEmpty(value))
            throw new CatalogLoadException($"Sheet '{sheet.Name}' row {row}: '{field}' is required but empty.");
        return value;
    }

    private static double RequirePositiveDouble(IXLWorksheet sheet, int row, int col, string field)
    {
        string raw = sheet.Cell(row, col).GetString().Trim();
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            throw new CatalogLoadException($"Sheet '{sheet.Name}' row {row}: '{field}' = '{raw}' is not a number.");
        if (value <= 0)
            throw new CatalogLoadException($"Sheet '{sheet.Name}' row {row}: '{field}' must be > 0 (was {value}).");
        return value;
    }

    private static TEnum RequireEnum<TEnum>(IXLWorksheet sheet, int row, int col, string field)
        where TEnum : struct, Enum
    {
        string raw = sheet.Cell(row, col).GetString().Trim();
        if (!Enum.TryParse<TEnum>(raw, ignoreCase: true, out var value) || !Enum.IsDefined(value))
        {
            throw new CatalogLoadException(
                $"Sheet '{sheet.Name}' row {row}: '{field}' = '{raw}' is not one of {string.Join(", ", Enum.GetNames<TEnum>())}.");
        }
        return value;
    }
}
