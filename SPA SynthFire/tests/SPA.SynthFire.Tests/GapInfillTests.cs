using System.Collections.Generic;
using System.Linq;
using SPA.SynthFire.Core.Geometry;
using SPA.SynthFire.Placement;
using Xunit;

namespace SPA.SynthFire.Tests;

public class GapInfillTests
{
    private static readonly Rectangle2D Box20 = new(0, 0, 20, 20);
    private static double Radius(double spacing) => CoverageField.DefaultRadiusForSpacing(spacing);

    private static double CoveragePercent(
        Rectangle2D f, IReadOnlyList<(double X, double Y)> pts, double radius,
        IReadOnlyList<ObstacleBox>? obs = null, Polygon2D? boundary = null) =>
        CoverageField.Build(f, pts, radius, 1.0, obs, boundary).CoveragePercent;

    [Fact]
    public void FullyGriddedRoom_AddsNothing()
    {
        var pts = new List<(double, double)> { (5, 5), (15, 5), (5, 15), (15, 15) };
        var result = GapInfill.FillCircular(
            Box20, boundary: null, obstacles: System.Array.Empty<ObstacleBox>(),
            existing: pts, radiusFt: Radius(10), cellSizeFt: 1.0, minSpacingFt: 6.0);

        Assert.Empty(result.Added);
        Assert.Equal(0, result.RemainingUncoveredCells);
    }

    [Fact]
    public void SingleHeadRoom_FillsGaps_AndRaisesCoverage()
    {
        var existing = new List<(double X, double Y)> { (5, 5) };
        double radius = Radius(10);
        double before = CoveragePercent(Box20, existing, radius);

        var result = GapInfill.FillCircular(
            Box20, boundary: null, obstacles: System.Array.Empty<ObstacleBox>(),
            existing, radius, cellSizeFt: 1.0, minSpacingFt: 6.0);

        Assert.NotEmpty(result.Added);
        double after = CoveragePercent(Box20, existing.Concat(result.Added).ToList(), radius);
        Assert.True(after > before);
    }

    [Fact]
    public void EmptyRoom_FillsFromScratch()
    {
        var result = GapInfill.FillCircular(
            Box20, boundary: null, obstacles: System.Array.Empty<ObstacleBox>(),
            existing: System.Array.Empty<(double, double)>(),
            radiusFt: Radius(10), cellSizeFt: 1.0, minSpacingFt: 6.0);

        Assert.NotEmpty(result.Added);
        double after = CoveragePercent(Box20, result.Added, Radius(10));
        Assert.True(after > 50.0); // a bare room gets meaningfully covered
    }

    [Fact]
    public void AddedHeads_HonourMinSpacing()
    {
        const double minSpacing = 6.0;
        var existing = new List<(double X, double Y)> { (5, 5) };
        var result = GapInfill.FillCircular(
            Box20, boundary: null, obstacles: System.Array.Empty<ObstacleBox>(),
            existing, radiusFt: Radius(10), cellSizeFt: 1.0, minSpacingFt: minSpacing);

        var heads = existing.Concat(result.Added).ToList();
        for (int i = 0; i < heads.Count; i++)
            for (int j = i + 1; j < heads.Count; j++)
            {
                double dx = heads[i].X - heads[j].X, dy = heads[i].Y - heads[j].Y;
                Assert.True(dx * dx + dy * dy >= minSpacing * minSpacing - 1e-6);
            }
    }

    [Fact]
    public void AddedHeads_StayInsideBoundary()
    {
        // L-shape: top-right 10x10 removed.
        var boundary = new Polygon2D(new[]
        {
            new Point2D(0, 0), new Point2D(20, 0), new Point2D(20, 10),
            new Point2D(10, 10), new Point2D(10, 20), new Point2D(0, 20)
        });
        var result = GapInfill.FillCircular(
            Box20, boundary, obstacles: System.Array.Empty<ObstacleBox>(),
            existing: System.Array.Empty<(double, double)>(),
            radiusFt: Radius(10), cellSizeFt: 1.0, minSpacingFt: 6.0);

        Assert.NotEmpty(result.Added);
        Assert.All(result.Added, p => Assert.True(boundary.Contains(p.X, p.Y)));
    }

    [Fact]
    public void AddedHeads_AvoidObstructions()
    {
        var obstacle = new ObstacleBox(new Rectangle2D(8, 8, 12, 12), "duct");
        var result = GapInfill.FillCircular(
            Box20, boundary: null, obstacles: new[] { obstacle },
            existing: System.Array.Empty<(double, double)>(),
            radiusFt: Radius(10), cellSizeFt: 1.0, minSpacingFt: 6.0);

        Assert.NotEmpty(result.Added);
        Assert.All(result.Added, p => Assert.False(obstacle.Bounds.Contains(p.X, p.Y)));
    }
}
