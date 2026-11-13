using System;
using System.Linq;
using SPA.SynthFire.Placement;
using Xunit;

namespace SPA.SynthFire.Tests;

public class PlacementScheduleExporterTests
{
    private static RoomPlacementResult Room(
        string level, string name, string hazard, PlacementStatus status,
        int count, double maxSpacing = 0, double coverage = 0, params string[] notes) =>
        new()
        {
            RoomId = name,
            RoomName = name,
            LevelName = level,
            HazardClassId = hazard,
            Status = status,
            Sprinklers = Enumerable.Range(0, count)
                .Select(i => new SprinklerPoint(i, 0, 0)).ToList(),
            ActualMaxSpacing = maxSpacing,
            ActualCoveragePerSprinkler = coverage,
            Notes = notes
        };

    private static PlacementResult Result(params RoomPlacementResult[] rooms) => new()
    {
        Rooms = rooms,
        StandardId = "NFPA13",
        HazardClassId = "LH",
        ReviewRequired = rooms.Any(r => r.ReviewRequired)
    };

    [Fact]
    public void ToCsv_HasHeaderAndOneRowPerRoom()
    {
        string csv = PlacementScheduleExporter.ToCsv(Result(
            Room("L1", "Office", "LH", PlacementStatus.Placed, 4, 12.0, 130.0),
            Room("L1", "Store", "OH2", PlacementStatus.PlacedReviewRequired, 6, 10.0, 100.0)));

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length); // header + 2 rooms
        Assert.StartsWith("Level,Room,Hazard,Status,Sprinklers,", lines[0]);
    }

    [Fact]
    public void ToCsv_OrdersByLevelThenRoom()
    {
        string csv = PlacementScheduleExporter.ToCsv(Result(
            Room("L2", "Zeta", "LH", PlacementStatus.Placed, 1),
            Room("L1", "Beta", "LH", PlacementStatus.Placed, 1),
            Room("L1", "Alpha", "LH", PlacementStatus.Placed, 1)));

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("L1,Alpha,", lines[1]);
        Assert.Contains("L1,Beta,", lines[2]);
        Assert.Contains("L2,Zeta,", lines[3]);
    }

    [Fact]
    public void ToCsv_QuotesFieldsWithCommas()
    {
        string csv = PlacementScheduleExporter.ToCsv(Result(
            Room("L1", "Lobby, Main", "LH", PlacementStatus.Placed, 2, 12, 130,
                "spacing tight, review")));

        Assert.Contains("\"Lobby, Main\"", csv);
        Assert.Contains("\"spacing tight, review\"", csv);
    }

    [Fact]
    public void ToCsv_EscapesEmbeddedQuotes()
    {
        string csv = PlacementScheduleExporter.ToCsv(Result(
            Room("L1", "Room \"A\"", "LH", PlacementStatus.Placed, 1)));

        Assert.Contains("\"Room \"\"A\"\"\"", csv);
    }

    [Fact]
    public void ToCsv_ReviewColumnReflectsStatus()
    {
        string csv = PlacementScheduleExporter.ToCsv(Result(
            Room("L1", "NeedsReview", "EH2", PlacementStatus.PlacedReviewRequired, 8)));

        var row = csv.Split("\r\n")[1];
        Assert.Contains(",Yes,", row); // review-required → Yes
        Assert.Contains("Review required", row);
    }

    [Fact]
    public void ToCsv_UsesDeviceNounInHeader()
    {
        string csv = PlacementScheduleExporter.ToCsv(
            Result(Room("L1", "Hall", "LH", PlacementStatus.Placed, 3)), "detectors");

        Assert.StartsWith("Level,Room,Hazard,Status,Detectors,", csv);
    }
}
