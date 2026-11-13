using ClosedXML.Excel;

namespace SPA.SynthFire.Catalog;

/// <summary>
/// Writes a starter catalog workbook matching <see cref="CatalogSchema"/>, with
/// a couple of example rows per sheet. Used to hand users a correct template and
/// to seed loader round-trip tests. The add-in never writes catalogs at runtime.
/// </summary>
public static class CatalogTemplateWriter
{
    public static void Write(string path)
    {
        using var workbook = new XLWorkbook();

        var sprinklers = workbook.Worksheets.Add(CatalogSchema.SprinklersSheet);
        WriteHeader(sprinklers, CatalogSchema.SprinklerHeaders);
        WriteSprinklerRow(sprinklers, CatalogSchema.FirstDataRow,
            "Std Pendent", "SP-1", 5.6, "Pendent", "LH", "NFPA13", "155F");
        WriteSprinklerRow(sprinklers, CatalogSchema.FirstDataRow + 1,
            "Std Pendent", "SP-2", 8.0, "Pendent", "OH1", "NFPA13", "155F");
        WriteSprinklerRow(sprinklers, CatalogSchema.FirstDataRow + 2,
            "Conventional", "CU-1", 80.0, "Upright", "OH1", "AS2118", "68C");

        var detectors = workbook.Worksheets.Add(CatalogSchema.SmokeDetectorsSheet);
        WriteHeader(detectors, CatalogSchema.SmokeDetectorHeaders);
        WriteDetectorRow(detectors, CatalogSchema.FirstDataRow, "Photoelectric", "PD-1", "Smoke", "NFPA13");
        WriteDetectorRow(detectors, CatalogSchema.FirstDataRow + 1, "Fixed Temp", "HD-1", "Heat", "NFPA13");

        foreach (var ws in workbook.Worksheets) ws.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }

    private static void WriteHeader(IXLWorksheet sheet, string[] headers)
    {
        sheet.Cell(CatalogSchema.VersionRow, 1).Value = "CatalogVersion";
        sheet.Cell(CatalogSchema.VersionRow, 2).Value = CatalogSchema.CurrentVersion;
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(CatalogSchema.HeaderRow, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
        }
    }

    private static void WriteSprinklerRow(IXLWorksheet sheet, int row,
        string family, string type, double kFactor, string orientation,
        string hazard, string standard, string temp)
    {
        sheet.Cell(row, 1).Value = family;
        sheet.Cell(row, 2).Value = type;
        sheet.Cell(row, 3).Value = kFactor;
        sheet.Cell(row, 4).Value = orientation;
        sheet.Cell(row, 5).Value = hazard;
        sheet.Cell(row, 6).Value = standard;
        sheet.Cell(row, 7).Value = temp;
    }

    private static void WriteDetectorRow(IXLWorksheet sheet, int row,
        string family, string type, string kind, string standard)
    {
        sheet.Cell(row, 1).Value = family;
        sheet.Cell(row, 2).Value = type;
        sheet.Cell(row, 3).Value = kind;
        sheet.Cell(row, 4).Value = standard;
    }
}
