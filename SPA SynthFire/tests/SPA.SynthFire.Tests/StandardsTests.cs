using System;
using SPA.SynthFire.Core.Standards;
using Xunit;

namespace SPA.SynthFire.Tests;

public class StandardsTests
{
    [Fact]
    public void Nfpa13_LightHazard_MatchesPublishedLimits()
    {
        var rules = new Nfpa13Standard().GetSprinklerRules("LH");

        Assert.Equal(UnitSystem.Imperial, rules.System);
        Assert.Equal(225, rules.MaxCoverageAreaPerSprinkler, precision: 6);
        Assert.Equal(15, rules.MaxSpacing, precision: 6);
        Assert.Equal(7.5, rules.MaxDistanceToWall, precision: 6);
        Assert.Equal(6, rules.MinSpacing, precision: 6);
        Assert.True(rules.IsProvisional);
    }

    [Fact]
    public void Nfpa13_ExtraHazard_UsesTighterSpacing()
    {
        var rules = new Nfpa13Standard().GetSprinklerRules("EH1");
        Assert.Equal(100, rules.MaxCoverageAreaPerSprinkler, precision: 6);
        Assert.Equal(12, rules.MaxSpacing, precision: 6);
    }

    [Fact]
    public void Nfpa13_UnknownHazard_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Nfpa13Standard().GetSprinklerRules("ZZ"));
    }

    [Fact]
    public void As2118_IsMetric_AndConvertsToFeet()
    {
        var rules = new As2118Standard().GetSprinklerRules("OH1");

        Assert.Equal(UnitSystem.Metric, rules.System);
        Assert.Equal(4.0, rules.MaxSpacing, precision: 6);

        // 4.0 m -> ~13.12 ft
        Assert.Equal(13.1234, rules.MaxSpacingFeet, precision: 3);
        // 12 m² -> ~129.17 sq ft
        Assert.Equal(129.167, rules.MaxCoverageAreaSquareFeet, precision: 2);
    }

    [Fact]
    public void Registry_Default_ContainsBothStandards_AllProvisional()
    {
        var registry = StandardRegistry.CreateDefault();

        Assert.True(registry.TryGet(StandardIds.Nfpa13, out var nfpa));
        Assert.True(registry.TryGet(StandardIds.As2118, out var as2118));
        Assert.False(nfpa.HasApprovedRules);
        Assert.False(as2118.HasApprovedRules);
    }
}
