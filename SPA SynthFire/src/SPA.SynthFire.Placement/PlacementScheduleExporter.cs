using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SPA.SynthFire.Placement;

/// <summary>
/// Turns a completed <see cref="PlacementResult"/> into a device schedule as
/// RFC 4180 CSV — the "quantity estimation" export. Revit-free and pure, so it is
/// unit-tested in isolation and reused by the report window's "Export CSV" button.
/// </summary>
public static class PlacementScheduleExporter
{
    /// <summary>
    /// One CSV row per room (ordered by level, then room), with a header line.
    /// <paramref name="deviceNoun"/> only labels the count column header
    /// (e.g. "Sprinklers", "Detectors").
    /// </summary>
    public static string ToCsv(PlacementResult result, string deviceNoun = "Sprinklers")
    {
        System.ArgumentNullException.ThrowIfNull(result);

        var sb = new StringBuilder();
        sb.Append("Level,Room,Hazard,Status,").Append(Capitalize(deviceNoun))
          .Append(",Max spacing (ft),Coverage (ft2/head),Coverage check,Review required,Notes\r\n");

        foreach (var room in result.Rooms
                     .OrderBy(r => r.LevelName, System.StringComparer.OrdinalIgnoreCase)
                     .ThenBy(r => r.RoomName, System.StringComparer.OrdinalIgnoreCase))
        {
            AppendRow(sb,
                room.LevelName,
                room.RoomName,
                room.HazardClassId,
                Describe(room.Status),
                room.Count.ToString(CultureInfo.InvariantCulture),
                room.ActualMaxSpacing.ToString("0.##", CultureInfo.InvariantCulture),
                room.ActualCoveragePerSprinkler.ToString("0.#", CultureInfo.InvariantCulture),
                CoverageValidator.Validate(room).Summary,
                room.ReviewRequired ? "Yes" : "No",
                string.Join(" | ", room.Notes));
        }

        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, params string[] fields)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Escape(fields[i]));
        }
        sb.Append("\r\n");
    }

    /// <summary>RFC 4180 quoting: wrap in quotes and double any embedded quote when needed.</summary>
    private static string Escape(string? field)
    {
        field ??= string.Empty;
        bool mustQuote = field.Contains(',') || field.Contains('"')
            || field.Contains('\n') || field.Contains('\r');
        if (!mustQuote) return field;
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }

    private static string Describe(PlacementStatus status) => status switch
    {
        PlacementStatus.Placed => "Placed",
        PlacementStatus.PlacedReviewRequired => "Review required",
        PlacementStatus.Skipped => "Skipped",
        _ => status.ToString()
    };

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
