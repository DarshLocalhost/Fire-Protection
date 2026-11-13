using SPA.SynthFire.Core.Geometry;
using Xunit;

namespace SPA.SynthFire.Tests;

public class GeometryTests
{
    [Fact]
    public void DistanceTo_ComputesEuclideanDistance()
    {
        var a = new Point3D(0, 0, 0);
        var b = new Point3D(3, 4, 0);

        Assert.Equal(5.0, a.DistanceTo(b), precision: 9);
    }

    [Fact]
    public void DistanceTo_IsSymmetric()
    {
        var a = new Point3D(1, 2, 3);
        var b = new Point3D(-4, 5, 6);

        Assert.Equal(a.DistanceTo(b), b.DistanceTo(a), precision: 9);
    }
}
