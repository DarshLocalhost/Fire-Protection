using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SPA.SynthFire.Core.Standards;

namespace SPA.SynthFire.Placement;

/// <summary>How serious a coverage finding is.</summary>
public enum CoverageSeverity
{
    /// <summary>Room meets its rule set's spacing and coverage limits.</summary>
    Ok,

    /// <summary>Laid out but a limit is exceeded — a real coverage gap needing attention.</summary>
    Gap
}

/// <summary>One room's coverage-validation outcome against the rule set it was laid out under.</summary>
public sealed record CoverageFinding(
    string RoomId,
    string RoomName,
    string LevelName,
    CoverageSeverity Severity,
    IReadOnlyList<string> Issues)
{
    public bool IsGap => Severity == CoverageSeverity.Gap;

    /// <summary>Compact cell text for the report/CSV coverage column.</summary>
    public string Summary => Severity == CoverageSeverity.Ok
        ? "OK"
        : "Gap: " + string.Join("; ", Issues);
}

/// <summary>
/// Revit-free post-placement check that compares each room's ACHIEVED layout
/// (device count, largest spacing, coverage area per device) against the LIMITS in
/// the rule set that room was actually laid out under, and flags coverage gaps the
/// solver may have accepted (e.g. a room skipped as degenerate, or an oddly-shaped
/// room where the grid could not stay under the ceiling). Purely a validation lens —
/// it never re-lays anything — so it is unit-tested in isolation and surfaced in the
/// placement report and CSV. Provisional rule values still apply: a room can pass
/// this check and still be review-required because the underlying numbers are not
/// FPE-verified.
/// </summary>
public static class CoverageValidator
{
    // Compare in feet / sq ft with a small absolute slack so floating-point grid
    // arithmetic that lands exactly on a limit does not read as a violation.
    private const double SpacingSlackFt = 0.01;
    private const double AreaSlackSqFt = 0.5;

    public static IReadOnlyList<CoverageFinding> Validate(PlacementResult result)
    {
        System.ArgumentNullException.ThrowIfNull(result);
        return result.Rooms.Select(Validate).ToList();
    }

    public static CoverageFinding Validate(RoomPlacementResult room)
    {
        var issues = new List<string>();

        if (room.Status == PlacementStatus.Skipped)
            issues.Add("room skipped — no devices placed");
        else if (room.Count == 0)
            issues.Add("no devices placed");

        SprinklerRuleSet? rules = room.Rules;
        if (room.Status != PlacementStatus.Skipped && rules is not null)
        {
            if (rules.MaxSpacingFeet > 0 && room.ActualMaxSpacing > rules.MaxSpacingFeet + SpacingSlackFt)
                issues.Add(Fmt("spacing {0} ft exceeds max {1} ft",
                    room.ActualMaxSpacing, rules.MaxSpacingFeet));

            if (rules.MaxCoverageAreaSquareFeet > 0 &&
                room.ActualCoveragePerSprinkler > rules.MaxCoverageAreaSquareFeet + AreaSlackSqFt)
                issues.Add(Fmt("coverage {0} ft² exceeds max {1} ft² per device",
                    room.ActualCoveragePerSprinkler, rules.MaxCoverageAreaSquareFeet));
        }

        return new CoverageFinding(
            room.RoomId, room.RoomName, room.LevelName,
            issues.Count == 0 ? CoverageSeverity.Ok : CoverageSeverity.Gap,
            issues);
    }

    /// <summary>Count of rooms with a coverage gap in this run.</summary>
    public static int GapCount(PlacementResult result) => Validate(result).Count(f => f.IsGap);

    private static string Fmt(string format, double a, double b) =>
        string.Format(CultureInfo.InvariantCulture, format,
            a.ToString("0.##", CultureInfo.InvariantCulture),
            b.ToString("0.##", CultureInfo.InvariantCulture));
}
