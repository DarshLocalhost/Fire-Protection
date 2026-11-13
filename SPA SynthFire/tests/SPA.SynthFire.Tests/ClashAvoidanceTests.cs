using System.Collections.Generic;
using System.Linq;
using SPA.SynthFire.Core.Geometry;
using SPA.SynthFire.Core.Standards;
using SPA.SynthFire.Placement;
using Xunit;

namespace SPA.SynthFire.Tests;

public class ClashAvoidanceTests
{
    private static readonly SprinklerPlacementSolver Solver = new();
    private static SprinklerRuleSet Lh() => new Nfpa13Standard().GetSprinklerRules("LH");

    private static RoomInput Room(double w, double h, IReadOnlyList<ObstacleBox> obstacles) => new()
    {
        RoomId = "R1",
        Name = "Test",
        Footprint = Rectangle2D.FromSize(0, 0, w, h),
        CeilingElevation = 9.0,
        Obstacles = obstacles
    };

    [Fact]
    public void HeadInsideObstacle_IsNudgedOff()
    {
        // 30x30 LH puts a head at (7.5, 7.5). Put a small obstacle right on it.
        var obstacle = new ObstacleBox(new Rectangle2D(7.0, 7.0, 8.0, 8.0), "duct");
        var result = Solver.Solve(new[] { Room(30, 30, new[] { obstacle }) }, Lh()).Rooms[0];

        // No head should remain inside the obstacle box.
        Assert.DoesNotContain(result.Sprinklers, p => obstacle.Bounds.Contains(p.X, p.Y));
    }

    [Fact]
    public void NoObstacles_LeavesGridUnchanged()
    {
        var plain = Solver.Solve(new[] { Room(30, 30, System.Array.Empty<ObstacleBox>()) }, Lh()).Rooms[0];
        Assert.Equal(4, plain.Count);
        Assert.Contains(plain.Sprinklers, p => p.X == 7.5 && p.Y == 7.5);
    }

    [Fact]
    public void NudgedHead_KeepsClearanceFromObstacleEdge()
    {
        // Head at (7.5,7.5); obstacle 7..8. After nudging it must clear the box by
        // the clearance margin (0.5 ft), i.e. sit outside 6.5..8.5 on some axis.
        var obstacle = new ObstacleBox(new Rectangle2D(7.0, 7.0, 8.0, 8.0), "duct");
        var result = Solver.Solve(new[] { Room(30, 30, new[] { obstacle }) }, Lh()).Rooms[0];

        var near = result.Sprinklers.First(p => System.Math.Abs(p.Y - 7.5) < 3 || System.Math.Abs(p.X - 7.5) < 3);
        bool clearsX = near.X < 6.5 || near.X > 8.5;
        bool clearsY = near.Y < 6.5 || near.Y > 8.5;
        Assert.True(clearsX || clearsY);
    }

    [Fact]
    public void ObstacleCoveringWholeArea_FlagsReview()
    {
        // An obstacle spanning the entire room can't be nudged out of.
        var obstacle = new ObstacleBox(new Rectangle2D(-1, -1, 31, 31), "slab");
        var result = Solver.Solve(new[] { Room(30, 30, new[] { obstacle }) }, Lh()).Rooms[0];

        Assert.True(result.ReviewRequired);
        Assert.Contains(result.Notes, n => n.Contains("could not clear"));
    }
}
