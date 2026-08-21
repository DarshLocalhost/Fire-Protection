using FireProtection.Backend.Models.Placement;

namespace FireProtection.Backend.Services.Placement
{
    /// <summary>
    /// Provides sprinkler placement rules for a given effective hazard class.
    /// Implementations decouple the placement algorithm from concrete rule values.
    /// </summary>
    public interface ISprinklerRuleProvider
    {
        SprinklerPlacementRules GetRulesFor(string effectiveHazardClass);
    }
}



