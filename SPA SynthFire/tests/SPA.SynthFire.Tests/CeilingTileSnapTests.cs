using System.Linq;
using SPA.SynthFire.Core.Geometry;
using SPA.SynthFire.Placement;
using Xunit;

namespace SPA.SynthFire.Tests;

/// <summary>
/// Track 1 — ceiling-tile snapping feed. Covers the settings→tile conversion
/// (<see cref="CeilingTilePlanner"/>) and that <see cref="CeilingGridLayout"/> snaps the
/// even coverage grid to tile centres when a <see cref="CeilingTile"/> is supplied.
/// </summary>
public class CeilingTileSnapTests
{
    private static RoomInput Room(double w, double h, CeilingTile? tile) => new()
    {
        RoomId = "R1",
        Name = "Test",
        Footprint = Rectangle2D.FromSize(0, 0, w, h),
        CeilingElevation = 9.0,
        CeilingTile = tile
    };

    [Fact]
    public void Planner_Disabled_ReturnsNull()
    {
        Assert.Null(CeilingTilePlanner.FromInches(false, 24, 24));
    }

    [Fact]
    public void Planner_NonPositiveModule_ReturnsNull()
    {
        Assert.Null(CeilingTilePlanner.FromInches(true, 0, 24));
        Assert.Null(CeilingTilePlanner.FromInches(true, 24, -1));
    }

    [Fact]
    public void Planner_ConvertsInchesToFeet()
    {
        var tile = CeilingTilePlanner.FromInches(true, 24, 48, 6, 12);
        Assert.NotNull(tile);
        Assert.Equal(2.0, tile!.ModuleXFt, 6);
        Assert.Equal(4.0, tile.ModuleYFt, 6);
        Assert.Equal(0.5, tile.OriginXFt, 6);
        Assert.Equal(1.0, tile.OriginYFt, 6);
    }

    [Fact]
    public void Compute_WithTile_SnapsEveryHeadToATileCentre()
    {
        // 24x24 room, 2 ft tiles from origin 0 → tile centres at (k+0.5)*2 = 1,3,5,...
        var tile = new CeilingTile(2.0, 2.0);
        var result = CeilingGridLayout.Compute(Room(24, 24, tile), maxSpacingFt: 15, maxCoverageSqFt: 225, minSpacingFt: 6);

        Assert.NotEmpty(result.Points);
        foreach (var (x, y) in result.Points)
        {
            Assert.True(IsTileCentre(x, 0, 2), $"X {x} is not a tile centre");
            Assert.True(IsTileCentre(y, 0, 2), $"Y {y} is not a tile centre");
        }
        Assert.Contains(result.Notes, n => n.Contains("snapped to the ceiling tile grid"));
    }

    [Fact]
    public void Compute_WithoutTile_KeepsEvenGrid()
    {
        var result = CeilingGridLayout.Compute(Room(24, 24, null), maxSpacingFt: 15, maxCoverageSqFt: 225, minSpacingFt: 6);
        // Even grid on a 24 ft room with 2 columns lands heads at 6 and 18 (not tile centres of a 2 ft grid).
        Assert.Contains(result.Points, p => p.X == 6.0 && p.Y == 6.0);
        Assert.DoesNotContain(result.Notes, n => n.Contains("snapped to the ceiling tile grid"));
    }

    // A value sits on a tile centre when (v - origin)/module - 0.5 is (near) an integer.
    private static bool IsTileCentre(double v, double origin, double module)
    {
        double k = (v - origin) / module - 0.5;
        return System.Math.Abs(k - System.Math.Round(k)) < 1e-6;
    }
}
