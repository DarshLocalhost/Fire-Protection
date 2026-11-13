using System.Collections.Generic;
using System.Linq;
using SPA.SynthFire.Core.Devices;
using SPA.SynthFire.Core.Geometry;
using SPA.SynthFire.Core.Standards;
using SPA.SynthFire.Placement;
using Xunit;

namespace SPA.SynthFire.Tests;

public class SidewallLayoutTests
{
    private static readonly SprinklerPlacementSolver Solver = new();
    private static SprinklerRuleSet Lh() => new Nfpa13Standard().GetSprinklerRules("LH"); // 15ft throw

    private static RoomInput Room(double w, double h) => new()
    {
        RoomId = "R1",
        Name = "Test",
        Footprint = Rectangle2D.FromSize(0, 0, w, h),
        CeilingElevation = 9.0
    };

    [Fact]
    public void ShallowRoom_LaysHeadsAlongOneWall_OrientedIntoRoom()
    {
        // 30 wide x 12 deep, throw 15 > depth 12 → a single wall covers it.
        var r = Solver.Solve(new[] { Room(30, 12) }, Lh(), DeviceMount.Sidewall).Rooms[0];

        Assert.NotEmpty(r.Sprinklers);
        Assert.All(r.Sprinklers, s => Assert.Equal(DeviceMount.Sidewall, s.Mount));
        Assert.All(r.Sprinklers, s => Assert.NotNull(s.OrientationRadians));
        // All heads sit against the same (bottom) wall and throw up into the room (+Y).
        Assert.All(r.Sprinklers, s => Assert.True(s.Y < 2.0));
        Assert.All(r.Sprinklers, s => Assert.True(System.Math.Sin(s.OrientationRadians!.Value) > 0.5));
    }

    [Fact]
    public void DeepRoom_AddsOpposingWall()
    {
        // 40 wide x 30 deep, depth 30 > throw 15 → heads on both long walls.
        var r = Solver.Solve(new[] { Room(40, 30) }, Lh(), DeviceMount.Sidewall).Rooms[0];

        Assert.Contains(r.Sprinklers, s => s.Y < 2.0);          // bottom wall
        Assert.Contains(r.Sprinklers, s => s.Y > 28.0);         // opposite (top) wall
    }

    [Fact]
    public void SidewallLayout_IsAlwaysReviewRequired()
    {
        var result = Solver.Solve(new[] { Room(30, 12) }, Lh(), DeviceMount.Sidewall);
        Assert.True(result.ReviewRequired);
        Assert.Equal(PlacementStatus.PlacedReviewRequired, result.Rooms[0].Status);
        Assert.Contains(result.Rooms[0].Notes, n => n.Contains("Sidewall"));
    }

    [Fact]
    public void CeilingMount_StaysOnTheGrid_NotAgainstAWall()
    {
        // Same room as a pendent: the centre head is well off the wall (a grid, not a sidewall row).
        var r = Solver.Solve(new[] { Room(30, 12) }, Lh(), DeviceMount.Ceiling).Rooms[0];
        Assert.All(r.Sprinklers, s => Assert.Equal(DeviceMount.Ceiling, s.Mount));
        Assert.Contains(r.Sprinklers, s => s.Y > 3.0);
    }

    [Fact]
    public void DirectionalCoverage_ReachesForward_NotBehind()
    {
        // A sidewall head at the origin throwing in +X: cells ahead are covered, cells behind are not.
        var dev = new CoverageDevice(0, 0, OrientationRadians: 0.0, ThrowFt: 10.0, HalfWidthFt: 5.0);
        Assert.True(dev.Covers(5, 0));    // straight ahead, in range
        Assert.True(dev.Covers(9, 4));    // ahead and within the lateral band
        Assert.False(dev.Covers(15, 0));  // beyond the throw
        Assert.False(dev.Covers(5, 8));   // outside the lateral band
        Assert.False(dev.Covers(-5, 0));  // behind the head
    }
}
