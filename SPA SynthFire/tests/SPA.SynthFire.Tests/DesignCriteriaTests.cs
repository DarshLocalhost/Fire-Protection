using SPA.SynthFire.Core.Standards;
using Xunit;

namespace SPA.SynthFire.Tests;

public class DesignCriteriaTests
{
    private static SprinklerRuleSet Nfpa13Lh() => new Nfpa13Standard().GetSprinklerRules("LH");

    [Fact]
    public void Apply_None_ReturnsSameRules()
    {
        var baseRules = Nfpa13Lh();
        var result = DesignCriteriaResolver.Apply(baseRules, UserDesignCriteria.None);
        Assert.Same(baseRules, result);
    }

    [Fact]
    public void Apply_TighterMaxSpacing_IsHonoured()
    {
        var result = DesignCriteriaResolver.Apply(Nfpa13Lh(),
            new UserDesignCriteria { MaxSpacing = 12 });

        Assert.Equal(12, result.MaxSpacing, precision: 6);
        Assert.Contains("Tightened", result.Notes);
    }

    [Fact]
    public void Apply_LooserMaxSpacing_IsClampedToStandard()
    {
        // User asks for 20 ft but the code caps LH at 15 ft — must clamp down.
        var result = DesignCriteriaResolver.Apply(Nfpa13Lh(),
            new UserDesignCriteria { MaxSpacing = 20 });

        Assert.Equal(15, result.MaxSpacing, precision: 6);
    }

    [Fact]
    public void Apply_SmallerMinSpacing_IsClampedUpToStandard()
    {
        // User asks for 3 ft min but the code floor is 6 ft — must clamp up.
        var result = DesignCriteriaResolver.Apply(Nfpa13Lh(),
            new UserDesignCriteria { MinSpacing = 3 });

        Assert.Equal(6, result.MinSpacing, precision: 6);
    }

    [Fact]
    public void Apply_KeepsProvisionalFlag()
    {
        var result = DesignCriteriaResolver.Apply(Nfpa13Lh(),
            new UserDesignCriteria { MaxSpacing = 10 });

        Assert.True(result.IsProvisional);
    }
}
