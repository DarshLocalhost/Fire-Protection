using System.Collections.Generic;
using System.Linq;
using SPA.SynthFire.Core.Geometry;
using SPA.SynthFire.Core.Standards;
using SPA.SynthFire.Placement;
using Xunit;

namespace SPA.SynthFire.Tests;

public class SprinklerPlacementSolverTests
{
    private static readonly SprinklerPlacementSolver Solver = new();

    private static SprinklerRuleSet Lh() => new Nfpa13Standard().GetSprinklerRules("LH");   // 15ft / 225sqft
    private static SprinklerRuleSet Eh1() => new Nfpa13Standard().GetSprinklerRules("EH1"); // 12ft / 100sqft

    private static RoomInput Room(double w, double h, double originX = 0, double originY = 0) => new()
    {
        RoomId = "R1",
        Name = "Test",
        Footprint = Rectangle2D.FromSize(originX, originY, w, h),
        CeilingElevation = 9.0
    };

    [Fact]
    public void SmallRoom_GetsSingleCentredSprinkler()
    {
        var result = Solver.Solve(new[] { Room(10, 10) }, Lh()).Rooms[0];

        Assert.Single(result.Sprinklers);
        Assert.Equal(5.0, result.Sprinklers[0].X, precision: 6);
        Assert.Equal(5.0, result.Sprinklers[0].Y, precision: 6);
        Assert.Equal(9.0, result.Sprinklers[0].Z, precision: 6);
    }

    [Fact]
    public void ThirtyFootRoom_LightHazard_MakesFourHeadsAtMaxSpacing()
    {
        var result = Solver.Solve(new[] { Room(30, 30) }, Lh()).Rooms[0];

        Assert.Equal(4, result.Count);
        Assert.Equal(15.0, result.ActualMaxSpacing, precision: 6);
        Assert.True(result.ActualCoveragePerSprinkler <= 225.0 + 1e-9);
    }

    [Fact]
    public void NeverExceedsMaxSpacingOrCoverage()
    {
        var rules = Lh();
        foreach (var (w, h) in new[] { (12.0, 40.0), (40.0, 40.0), (55.0, 33.0), (17.0, 17.0) })
        {
            var r = Solver.Solve(new[] { Room(w, h) }, rules).Rooms[0];
            Assert.True(r.ActualMaxSpacing <= rules.MaxSpacingFeet + 1e-9, $"{w}x{h} spacing");
            Assert.True(r.ActualCoveragePerSprinkler <= rules.MaxCoverageAreaSquareFeet + 1e-9, $"{w}x{h} area");
        }
    }

    [Fact]
    public void CoverageLimit_ForcesExtraHeads_WhenTighterThanSpacing()
    {
        // 24x24 under EH1 (12ft/100sqft): a 2x2 grid gives 12ft spacing but 144 sqft/head,
        // so the solver must add heads to bring coverage under 100 sqft.
        var r = Solver.Solve(new[] { Room(24, 24) }, Eh1()).Rooms[0];

        Assert.True(r.ActualCoveragePerSprinkler <= 100.0 + 1e-9);
        Assert.True(r.Count > 4);
    }

    [Fact]
    public void EveryHead_IsInsideFootprint_AndOutermostHeadsRespectWallLimit()
    {
        var rules = Lh();
        var room = Room(40, 25, originX: 100, originY: 200);
        var r = Solver.Solve(new[] { room }, rules).Rooms[0];

        foreach (var p in r.Sprinklers)
            Assert.True(room.Footprint.Contains(p.X, p.Y));

        // Only the nearest wall matters: the outermost heads must sit within the
        // max distance-to-wall of each boundary.
        double maxToWall = rules.MaxDistanceToWallFeet;
        double minX = r.Sprinklers.Min(p => p.X);
        double maxX = r.Sprinklers.Max(p => p.X);
        double minY = r.Sprinklers.Min(p => p.Y);
        double maxY = r.Sprinklers.Max(p => p.Y);

        Assert.True(minX - room.Footprint.MinX <= maxToWall + 1e-9);
        Assert.True(room.Footprint.MaxX - maxX <= maxToWall + 1e-9);
        Assert.True(minY - room.Footprint.MinY <= maxToWall + 1e-9);
        Assert.True(room.Footprint.MaxY - maxY <= maxToWall + 1e-9);
    }

    [Fact]
    public void ProvisionalRules_AlwaysFlagReviewRequired()
    {
        var result = Solver.Solve(new[] { Room(30, 30) }, Lh());
        Assert.True(result.ReviewRequired);
        Assert.Equal(PlacementStatus.PlacedReviewRequired, result.Rooms[0].Status);
    }

    [Fact]
    public void DegenerateRoom_IsSkipped()
    {
        var r = Solver.Solve(new[] { Room(0, 10) }, Lh()).Rooms[0];
        Assert.Equal(PlacementStatus.Skipped, r.Status);
        Assert.Empty(r.Sprinklers);
    }

    [Fact]
    public void LShapedRoom_DropsHeadsOutsideTheOutline_AndFlagsReview()
    {
        // 40x40 box with the top-right 20x20 quadrant removed. The grid is laid over the
        // bounding box, so heads land in the cut-out; the polygon filter must drop them.
        var boundary = new Polygon2D(new[]
        {
            new Point2D(0, 0), new Point2D(40, 0), new Point2D(40, 20),
            new Point2D(20, 20), new Point2D(20, 40), new Point2D(0, 40)
        });
        var lRoom = new RoomInput
        {
            RoomId = "L", Name = "L-Hall",
            Footprint = new Rectangle2D(0, 0, 40, 40),
            Boundary = boundary,
            CeilingElevation = 9.0
        };

        var l = Solver.Solve(new[] { lRoom }, Lh()).Rooms[0];

        // Every head sits on real floor, none in the removed quadrant (auto-infill also
        // respects the outline — it only fills uncovered cells inside the polygon).
        Assert.All(l.Sprinklers, p => Assert.True(boundary.Contains(p.X, p.Y)));
        Assert.DoesNotContain(l.Sprinklers, p => p.X > 20 && p.Y > 20); // the cut-out quadrant
        Assert.True(l.ReviewRequired);
        Assert.Contains(l.Notes, n => n.Contains("outside the room outline")); // grid points were dropped
    }

    [Fact]
    public void ConcaveRoom_AutoInfillsResidualGaps_AndStaysReviewRequired()
    {
        // Same L-shape: dropping the cut-out grid points can leave the arms under-covered.
        // Auto-infill must top the coverage back up so no red floor ships, and the layout
        // stays review-required (covers + flags; never auto-exempts).
        var boundary = new Polygon2D(new[]
        {
            new Point2D(0, 0), new Point2D(40, 0), new Point2D(40, 20),
            new Point2D(20, 20), new Point2D(20, 40), new Point2D(0, 40)
        });
        var lRoom = new RoomInput
        {
            RoomId = "L", Name = "L-Hall",
            Footprint = new Rectangle2D(0, 0, 40, 40),
            Boundary = boundary,
            CeilingElevation = 9.0
        };

        var l = Solver.Solve(new[] { lRoom }, Lh()).Rooms[0];

        double radius = CoverageField.DefaultRadiusForSpacing(l.ActualMaxSpacing);
        var field = CoverageField.Build(
            l.Footprint!.Value, l.Sprinklers.Select(p => (p.X, p.Y)).ToList(),
            radius, 1.0, l.Obstacles, l.Boundary);

        Assert.True(field.CoveragePercent > 95.0, $"coverage was {field.CoveragePercent:0.#}%");
        Assert.Equal(PlacementStatus.PlacedReviewRequired, l.Status);
    }

    [Fact]
    public void IsDeterministic()
    {
        var a = Solver.Solve(new[] { Room(37, 29) }, Lh()).Rooms[0];
        var b = Solver.Solve(new[] { Room(37, 29) }, Lh()).Rooms[0];
        Assert.Equal(a.Count, b.Count);
        Assert.Equal(a.Sprinklers.Select(p => p.X), b.Sprinklers.Select(p => p.X));
    }
}
