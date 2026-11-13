using System;
using System.Collections.Generic;
using System.Linq;
using SPA.SynthFire.Core.Geometry;
using SPA.SynthFire.Placement;
using Xunit;

namespace SPA.SynthFire.Tests;

public class CoverageFieldTests
{
    private static readonly Rectangle2D Room = new(0, 0, 20, 20);

    [Fact]
    public void SingleCenterDevice_LeavesCornersUncovered()
    {
        // One device with a small radius cannot reach a 20x20 room's corners.
        var field = CoverageField.Build(Room, new[] { (10.0, 10.0) }, radiusFt: 5.0, cellSizeFt: 1.0);

        Assert.True(field.TotalCells > 0);
        Assert.True(field.UncoveredCells > 0);
        Assert.True(field.CoverageFraction is > 0 and < 1);
        Assert.DoesNotContain(field.Cells, c => c.Kind == CoverageCellKind.Overlap);
    }

    [Fact]
    public void DenseGrid_WithHalfDiagonalRadius_CoversWholeRoom()
    {
        double spacing = 10.0;
        var points = new List<(double, double)>();
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
                points.Add((5 + i * spacing, 5 + j * spacing)); // centres of four 10ft cells

        double r = CoverageField.DefaultRadiusForSpacing(spacing); // 10/sqrt2 ~= 7.07
        var field = CoverageField.Build(Room, points, r, cellSizeFt: 1.0);

        Assert.Equal(0, field.UncoveredCells);
        Assert.Equal(1.0, field.CoverageFraction, 3);
        Assert.True(field.OverlapCells > 0); // seams between heads are within reach of two
    }

    [Fact]
    public void ObstructedCells_AreExcludedFromCoverageDenominator()
    {
        var obstacles = new[] { new ObstacleBox(new Rectangle2D(0, 0, 20, 10)) }; // lower half blocked
        // Device only in the upper half.
        var field = CoverageField.Build(
            Room, new[] { (10.0, 15.0) }, radiusFt: 8.0, cellSizeFt: 1.0, obstacles: obstacles);

        Assert.True(field.ObstructedCells > 0);
        Assert.All(field.Cells.Where(c => c.CenterY < 10),
            c => Assert.Equal(CoverageCellKind.Obstructed, c.Kind));
        // Coverage fraction is measured over protectable (non-obstructed) cells only.
        Assert.True(field.CoverageFraction > 0);
    }

    [Fact]
    public void NoDevices_IsAllUncovered_ZeroCoverage()
    {
        var field = CoverageField.Build(Room, Array.Empty<(double, double)>(), radiusFt: 6.0, cellSizeFt: 2.0);
        Assert.Equal(field.TotalCells, field.UncoveredCells);
        Assert.Equal(0.0, field.CoverageFraction);
    }

    [Fact]
    public void DegenerateOrZeroRadius_ProducesNoCells()
    {
        Assert.Empty(CoverageField.Build(new Rectangle2D(0, 0, 0, 10), new[] { (0.0, 0.0) }, 5.0).Cells);
        Assert.Empty(CoverageField.Build(Room, new[] { (0.0, 0.0) }, radiusFt: 0.0).Cells);
    }

    [Fact]
    public void OverlapCells_CountedWhenTwoDevicesReachSamePoint()
    {
        // Two devices 4 ft apart, radius 5 ft: the midpoint is within both.
        var field = CoverageField.Build(Room, new[] { (8.0, 10.0), (12.0, 10.0) }, radiusFt: 5.0, cellSizeFt: 1.0);
        Assert.Contains(field.Cells, c => c.Kind == CoverageCellKind.Overlap && c.DeviceCount >= 2);
    }

    [Fact]
    public void Boundary_ClipsCellsToTheRoomPolygon()
    {
        // L-shape inside the 20x20 bounding box: top-right 10x10 quadrant removed.
        var boundary = new Polygon2D(new[]
        {
            new Point2D(0, 0), new Point2D(20, 0), new Point2D(20, 10),
            new Point2D(10, 10), new Point2D(10, 20), new Point2D(0, 20)
        });
        var full = CoverageField.Build(Room, new[] { (10.0, 10.0) }, radiusFt: 8.0, cellSizeFt: 1.0);
        var clipped = CoverageField.Build(Room, new[] { (10.0, 10.0) }, radiusFt: 8.0, cellSizeFt: 1.0, boundary: boundary);

        // Clipping drops the removed quadrant, so fewer cells and none in the cut-out corner.
        Assert.True(clipped.TotalCells < full.TotalCells);
        Assert.DoesNotContain(clipped.Cells, c => c.CenterX > 10 && c.CenterY > 10);
    }
}
