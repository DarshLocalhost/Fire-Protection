using System;
using SPA.SynthFire.Core.Geometry;
using Xunit;

namespace SPA.SynthFire.Tests;

public class Polygon2DTests
{
    // An L-shape: 30x30 block with the top-right 15x15 quadrant removed. Area = 900 - 225 = 675.
    private static Polygon2D LShape() => new(new[]
    {
        new Point2D(0,  0), new Point2D(30, 0), new Point2D(30, 15),
        new Point2D(15, 15), new Point2D(15, 30), new Point2D(0, 30)
    });

    [Fact]
    public void Area_MatchesShoelace_ForLShape()
    {
        Assert.Equal(675.0, LShape().Area, precision: 6);
    }

    [Fact]
    public void Bounds_IsAxisAlignedHull()
    {
        var b = LShape().Bounds;
        Assert.Equal(0, b.MinX);
        Assert.Equal(0, b.MinY);
        Assert.Equal(30, b.MaxX);
        Assert.Equal(30, b.MaxY);
    }

    [Fact]
    public void Contains_TrueInsideSolidQuadrant()
    {
        Assert.True(LShape().Contains(5, 25));   // left arm, above the notch
        Assert.True(LShape().Contains(25, 5));   // bottom arm
    }

    [Fact]
    public void Contains_FalseInRemovedQuadrant()
    {
        Assert.False(LShape().Contains(25, 25)); // the cut-out corner
    }

    [Fact]
    public void Contains_FalseOutsideBounds()
    {
        Assert.False(LShape().Contains(-1, 5));
        Assert.False(LShape().Contains(40, 40));
    }

    [Fact]
    public void Ctor_Throws_WhenTooFewVertices()
    {
        Assert.Throws<ArgumentException>(() => new Polygon2D(new[] { new Point2D(0, 0), new Point2D(1, 1) }));
    }
}
