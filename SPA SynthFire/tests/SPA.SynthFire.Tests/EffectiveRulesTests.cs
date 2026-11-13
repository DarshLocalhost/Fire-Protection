using SPA.SynthFire.Core.Settings;
using SPA.SynthFire.Core.Standards;
using Xunit;

namespace SPA.SynthFire.Tests;

public class EffectiveRulesTests
{
    private static readonly StandardRegistry Registry = StandardRegistry.CreateDefault();

    [Fact]
    public void NoOverride_ReturnsPublishedValues()
    {
        var settings = new SynthFireSettings();
        var rules = EffectiveRules.For(Registry, settings, StandardIds.Nfpa13, "LH");
        Assert.Equal(15.0, rules.MaxSpacing, precision: 6);   // published NFPA 13 LH
        Assert.Equal(225.0, rules.MaxCoverageAreaPerSprinkler, precision: 6);
    }

    [Fact]
    public void Override_ReplacesPublishedValues()
    {
        var settings = new SynthFireSettings();
        settings.Overrides[HazardRuleOverride.Key(StandardIds.Nfpa13, "LH")] = new HazardRuleOverride
        {
            MaxSpacing = 12.0,
            MaxCoverageAreaPerSprinkler = 200.0
        };

        var rules = EffectiveRules.For(Registry, settings, StandardIds.Nfpa13, "LH");
        Assert.Equal(12.0, rules.MaxSpacing, precision: 6);            // overridden
        Assert.Equal(200.0, rules.MaxCoverageAreaPerSprinkler, precision: 6); // overridden
        Assert.Equal(6.0, rules.MinSpacing, precision: 6);            // untouched
        Assert.Contains("Edited", rules.Notes);
    }
}
