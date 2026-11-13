using SPA.SynthFire.Core.Standards;
using Xunit;

namespace SPA.SynthFire.Tests;

public class RoomHazardClassifierTests
{
    [Theory]
    [InlineData("Office 101", HazardTier.Light)]
    [InlineData("Main Lobby", HazardTier.Light)]
    [InlineData("Car Parking", HazardTier.OH1)]
    [InlineData("Kitchen", HazardTier.OH1)]
    [InlineData("Retail Shop", HazardTier.OH2)]
    [InlineData("Bulk Storage", HazardTier.OH2)]
    [InlineData("Printing Press", HazardTier.EH1)]
    [InlineData("Paint Spray Booth", HazardTier.EH2)]
    public void Classify_MapsKnownKeywords(string name, HazardTier expected)
    {
        Assert.Equal(expected, RoomHazardClassifier.Classify(name).Tier);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Zone 7")]
    [InlineData("Random Space")]
    public void Classify_UnknownOrEmpty_DefaultsToLight_NoReview(string? name)
    {
        var result = RoomHazardClassifier.Classify(name);
        Assert.Equal(HazardTier.Light, result.Tier);
        Assert.False(result.Matched);
        Assert.False(result.ReviewRequired);
    }

    [Fact]
    public void Classify_MostSevereMatchWins()
    {
        // "office storage" hits both Light (office) and OH2 (storage) → OH2.
        var result = RoomHazardClassifier.Classify("Office Storage");
        Assert.Equal(HazardTier.OH2, result.Tier);
        Assert.True(result.ReviewRequired); // multiple hits AND "storage" is always-review
    }

    [Fact]
    public void Classify_StorageIsAlwaysReview_EvenOnSingleMatch()
    {
        var result = RoomHazardClassifier.Classify("Warehouse");
        Assert.Equal(HazardTier.OH2, result.Tier);
        Assert.True(result.ReviewRequired);
    }

    [Fact]
    public void Classify_SingleOrdinaryMatch_NoReview()
    {
        var result = RoomHazardClassifier.Classify("Kitchen");
        Assert.True(result.Matched);
        Assert.False(result.ReviewRequired);
    }

    [Fact]
    public void MapToStandardHazardId_Nfpa13_UsesSameIds()
    {
        var nfpa = new Nfpa13Standard();
        Assert.Equal("LH", RoomHazardClassifier.MapToStandardHazardId(nfpa, HazardTier.Light));
        Assert.Equal("OH2", RoomHazardClassifier.MapToStandardHazardId(nfpa, HazardTier.OH2));
        Assert.Equal("EH2", RoomHazardClassifier.MapToStandardHazardId(nfpa, HazardTier.EH2));
    }

    [Fact]
    public void MapToStandardHazardId_As2118_ClampsToItsLadder()
    {
        var as2118 = new As2118Standard();
        // AS 2118 has no EH2 id; the EH2 tier clamps onto its most-severe class.
        string mapped = RoomHazardClassifier.MapToStandardHazardId(as2118, HazardTier.EH2);
        Assert.Contains(mapped, new[] { "HHP", "OH3" });
        Assert.Equal("LH", RoomHazardClassifier.MapToStandardHazardId(as2118, HazardTier.Light));
    }
}
