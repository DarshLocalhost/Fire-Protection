using System;
using System.Collections.Generic;
using System.Linq;
using SPA.SynthFire.Core.Catalog;
using SPA.SynthFire.Core.Geometry;
using SPA.SynthFire.Core.Standards;
using SPA.SynthFire.Placement;
using Xunit;

namespace SPA.SynthFire.Tests;

public class CoverageValidatorTests
{
    private static readonly SprinklerRuleSet Smoke = Nfpa72DetectionRules.For(DetectorKind.Smoke); // 30 ft / 900 ft²

    private static RoomPlacementResult Room(
        PlacementStatus status, int heads, double spacing, double coverage, SprinklerRuleSet? rules) =>
        new()
        {
            RoomId = "R1",
            RoomName = "Hall",
            Status = status,
            Sprinklers = Enumerable.Range(0, heads).Select(i => new SprinklerPoint(i, 0, 10)).ToList(),
            ActualMaxSpacing = spacing,
            ActualCoveragePerSprinkler = coverage,
            Rules = rules
        };

    [Fact]
    public void WithinLimits_IsOk()
    {
        var finding = CoverageValidator.Validate(Room(PlacementStatus.Placed, 4, 28, 850, Smoke));
        Assert.Equal(CoverageSeverity.Ok, finding.Severity);
        Assert.Equal("OK", finding.Summary);
    }

    [Fact]
    public void SpacingOverLimit_IsGap()
    {
        var finding = CoverageValidator.Validate(Room(PlacementStatus.PlacedReviewRequired, 2, 40, 850, Smoke));
        Assert.True(finding.IsGap);
        Assert.Contains("spacing", finding.Summary);
    }

    [Fact]
    public void CoverageOverLimit_IsGap()
    {
        var finding = CoverageValidator.Validate(Room(PlacementStatus.PlacedReviewRequired, 2, 28, 1200, Smoke));
        Assert.True(finding.IsGap);
        Assert.Contains("coverage", finding.Summary);
    }

    [Fact]
    public void SkippedRoom_IsGap_EvenWithoutRules()
    {
        var finding = CoverageValidator.Validate(Room(PlacementStatus.Skipped, 0, 0, 0, rules: null));
        Assert.True(finding.IsGap);
        Assert.Contains("skipped", finding.Summary);
    }

    [Fact]
    public void ExactlyOnLimit_IsOk_WithinSlack()
    {
        var finding = CoverageValidator.Validate(Room(PlacementStatus.Placed, 2, 30, 900, Smoke));
        Assert.Equal(CoverageSeverity.Ok, finding.Severity);
    }

    [Fact]
    public void SolvedRoom_PassesCoverageCheck()
    {
        var room = new RoomInput
        {
            RoomId = "R1",
            Name = "Hall",
            Footprint = Rectangle2D.FromSize(0, 0, 60, 30),
            CeilingElevation = 10
        };
        var result = new SprinklerPlacementSolver().Solve(new[] { room }, Smoke);
        Assert.Equal(0, CoverageValidator.GapCount(result));
        Assert.All(CoverageValidator.Validate(result), f => Assert.Equal(CoverageSeverity.Ok, f.Severity));
    }
}
