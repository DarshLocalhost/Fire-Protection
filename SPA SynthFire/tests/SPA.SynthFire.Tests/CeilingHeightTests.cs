using SPA.SynthFire.Core.Geometry;
using Xunit;

namespace SPA.SynthFire.Tests;

/// <summary>
/// Covers the ceiling-height fallback ladder that feeds an unhosted device's Z
/// (the fix for devices placed floating at a fabricated 9 ft offset). Revit-free:
/// exercises the pure arithmetic extracted out of RoomReader.
/// </summary>
public class CeilingHeightTests
{
    [Fact]
    public void PrefersUnboundedHeight_WhenBelievable()
    {
        // 10 ft room height in band → used verbatim, bbox ignored.
        Assert.Equal(10.0, CeilingHeight.Resolve(unboundedHeight: 10.0, bboxHeight: 25.0));
    }

    [Fact]
    public void FallsBackToBbox_WhenUnboundedOutOfBand()
    {
        // Unbounded room reports 0 (no upper bound) → drop to bbox height.
        Assert.Equal(9.5, CeilingHeight.Resolve(unboundedHeight: 0.0, bboxHeight: 9.5));
    }

    [Fact]
    public void FallsBackToDefault_WhenBothOutOfBand()
    {
        // Shaft/atrium: both candidates are wild → flat default, never a wild Z.
        Assert.Equal(CeilingHeight.DefaultFt, CeilingHeight.Resolve(unboundedHeight: 0.0, bboxHeight: 120.0));
    }

    [Theory]
    [InlineData(CeilingHeight.MinFt)]
    [InlineData(CeilingHeight.MaxFt)]
    public void BandBoundsAreInclusive(double height)
    {
        Assert.Equal(height, CeilingHeight.Resolve(unboundedHeight: height, bboxHeight: 0.0));
    }

    [Fact]
    public void RejectsHeightJustBelowMin()
    {
        Assert.Equal(CeilingHeight.DefaultFt, CeilingHeight.Resolve(unboundedHeight: CeilingHeight.MinFt - 0.01, bboxHeight: 200.0));
    }
}
