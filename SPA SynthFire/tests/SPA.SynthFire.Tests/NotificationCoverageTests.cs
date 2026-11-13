using SPA.SynthFire.Core.Standards;
using Xunit;

namespace SPA.SynthFire.Tests;

/// <summary>
/// Track 2 — NFPA 72 notification-appliance coverage math (<see cref="NotificationCoverage"/>):
/// the wall-mounted visual candela table (18.5.5.5.1(a)), room-appliance counts, and the
/// audible inverse-square / public-mode dBA relationships.
/// </summary>
public class NotificationCoverageTests
{
    [Theory]
    [InlineData(75, 45)]    // 75 cd row → 45 ft square
    [InlineData(15, 20)]    // smallest listed rating
    [InlineData(135, 60)]   // 135 cd → 60 ft
    [InlineData(635, 130)]  // top of the table
    public void MaxRoomSide_MatchesTableRow(double candela, double expectedSide)
        => Assert.Equal(expectedSide, NotificationCoverage.MaxRoomSideForCandela(candela), 6);

    [Fact]
    public void MaxRoomSide_UsesHighestRowAtOrBelowRating()
    {
        // 100 cd is between the 94 cd (50 ft) and 110 cd (54 ft) rows → 94 cd row.
        Assert.Equal(50.0, NotificationCoverage.MaxRoomSideForCandela(100), 6);
    }

    [Fact]
    public void MaxRoomSide_BelowSmallestRating_IsZero()
        => Assert.Equal(0.0, NotificationCoverage.MaxRoomSideForCandela(10), 6);

    [Fact]
    public void MinCandelaForRoom_PicksSmallestSufficientRating()
    {
        // 42 ft longest dimension needs the 45 ft (75 cd) row, not the 40 ft (60 cd) one.
        Assert.Equal(75.0, NotificationCoverage.MinCandelaForRoom(42, 30));
    }

    [Fact]
    public void MinCandelaForRoom_TooLargeForOneAppliance_IsNull()
        => Assert.Null(NotificationCoverage.MinCandelaForRoom(200, 200));

    [Fact]
    public void WallAppliances_SmallRoom_IsOne()
        => Assert.Equal(1, NotificationCoverage.WallAppliancesForRoom(40, 30, 75)); // both ≤ 45 ft

    [Fact]
    public void WallAppliances_SubdividesLargeRoom()
    {
        // 75 cd covers 45 ft; a 90×45 room needs 2 columns × 1 row = 2 appliances.
        Assert.Equal(2, NotificationCoverage.WallAppliancesForRoom(90, 45, 75));
    }

    [Fact]
    public void WallAppliances_BelowTable_IsZero()
        => Assert.Equal(0, NotificationCoverage.WallAppliancesForRoom(20, 20, 10));

    [Fact]
    public void RequiredPublicModeDba_TakesGreaterLeg()
    {
        Assert.Equal(75.0, NotificationCoverage.RequiredPublicModeDba(60, 55), 6); // 60+15 vs 55+5
        Assert.Equal(85.0, NotificationCoverage.RequiredPublicModeDba(60, 80), 6); // 60+15 vs 80+5
    }

    [Fact]
    public void AudibleRadius_HalvesPerSixDb()
    {
        // A source that is 6 dB hotter than required reaches ~2× the 10 ft reference distance.
        double r = NotificationCoverage.AudibleRadiusFt(ratedDbaAt10Ft: 81, requiredDba: 75);
        Assert.Equal(20.0, r, 1);
    }

    [Fact]
    public void AudibleRadius_CannotMeetRequirement_IsZero()
        => Assert.Equal(0.0, NotificationCoverage.AudibleRadiusFt(70, 75), 6);
}
