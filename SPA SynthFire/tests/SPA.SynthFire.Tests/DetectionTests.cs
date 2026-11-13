using System.Linq;
using SPA.SynthFire.Core.Catalog;
using SPA.SynthFire.Core.Geometry;
using SPA.SynthFire.Core.Standards;
using SPA.SynthFire.Placement;
using Xunit;

namespace SPA.SynthFire.Tests;

public class DetectionTests
{
    [Fact]
    public void SmokeRules_UseNfpa72NominalSpacing()
    {
        var rules = Nfpa72DetectionRules.For(DetectorKind.Smoke);
        Assert.Equal("NFPA72", rules.StandardId);
        Assert.Equal(30.0, rules.MaxSpacingFeet, precision: 6);
        Assert.Equal(900.0, rules.MaxCoverageAreaSquareFeet, precision: 6);
        Assert.True(rules.IsProvisional);
    }

    [Fact]
    public void HeatRules_AreCoarserThanSmoke()
    {
        var smoke = Nfpa72DetectionRules.For(DetectorKind.Smoke);
        var heat = Nfpa72DetectionRules.For(DetectorKind.Heat);
        Assert.True(heat.MaxSpacingFeet > smoke.MaxSpacingFeet);
    }

    [Fact]
    public void Solver_LaysOutDetectorsOnSharedGrid()
    {
        var room = new RoomInput
        {
            RoomId = "R1",
            Name = "Hall",
            Footprint = Rectangle2D.FromSize(0, 0, 60, 30),
            CeilingElevation = 10
        };
        var result = new SprinklerPlacementSolver().Solve(new[] { room }, Nfpa72DetectionRules.For(DetectorKind.Smoke));
        var r = result.Rooms[0];

        // 60x30 at 30ft spacing / 900 sqft -> 2x1 grid.
        Assert.Equal(2, r.Count);
        Assert.True(r.ActualCoveragePerSprinkler <= 900.0 + 1e-9);
        Assert.True(result.ReviewRequired); // provisional
    }
}
