using System;
using FireProtection.Backend.Models.Placement;

namespace FireProtection.Backend.Services.Placement
{
    /// <summary>
    /// PROVISIONAL provider that returns generic placement rules keyed by hazard class.
    ///
    /// The numeric values below are PLACEHOLDERS for architectural bring-up only.
    /// They are NOT NFPA-compliant and MUST be replaced with the senior-provided
    /// values in a later phase.
    ///
    /// This provider MUST NOT select, override, or fall back to a sprinkler
    /// Family/Type. The actual Revit Family/Type used for placement is chosen
    /// by the user in the UI and carried on PlacementRequestItem
    /// (SelectedSprinklerFamilyName / SelectedSprinklerTypeName).
    /// </summary>
    public class GenericSprinklerRuleProvider : ISprinklerRuleProvider
    {
        public SprinklerPlacementRules GetRulesFor(string effectiveHazardClass)
        {
            string hazard = (effectiveHazardClass ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

            switch (hazard)
            {
                case "LIGHT":
                    return BuildRules(
                        maxCoverage: 225.0,
                        maxSpacing: 15.0,
                        minSpacing: 6.0,
                        maxWall: 7.5,
                        minWall: 0.33,
                        mountOffset: 0.5);

                case "OH1":
                    return BuildRules(
                        maxCoverage: 130.0,
                        maxSpacing: 15.0,
                        minSpacing: 6.0,
                        maxWall: 7.5,
                        minWall: 0.33,
                        mountOffset: 0.5);

                case "OH2":
                    return BuildRules(
                        maxCoverage: 130.0,
                        maxSpacing: 15.0,
                        minSpacing: 6.0,
                        maxWall: 7.5,
                        minWall: 0.33,
                        mountOffset: 0.5);

                case "EH1":
                    return BuildRules(
                        maxCoverage: 100.0,
                        maxSpacing: 12.0,
                        minSpacing: 6.0,
                        maxWall: 6.0,
                        minWall: 0.33,
                        mountOffset: 0.5);

                case "EH2":
                    return BuildRules(
                        maxCoverage: 100.0,
                        maxSpacing: 12.0,
                        minSpacing: 6.0,
                        maxWall: 6.0,
                        minWall: 0.33,
                        mountOffset: 0.5);

                default:
                    // Unknown hazard: fall back to LIGHT-like generic values.
                    return BuildRules(
                        maxCoverage: 225.0,
                        maxSpacing: 15.0,
                        minSpacing: 6.0,
                        maxWall: 7.5,
                        minWall: 0.33,
                        mountOffset: 0.5);
            }
        }

        private static GenericSprinklerPlacementRules BuildRules(
            double maxCoverage,
            double maxSpacing,
            double minSpacing,
            double maxWall,
            double minWall,
            double mountOffset)
        {
            return new GenericSprinklerPlacementRules
            {
                MaxCoveragePerHeadSqFt = maxCoverage,
                MaxSprinklerSpacingFt = maxSpacing,
                MinSprinklerSpacingFt = minSpacing,
                MaxWallDistanceFt = maxWall,
                MinWallDistanceFt = minWall,
                MountingOffsetFromCeilingFt = mountOffset,

                // Generic eligibility thresholds — PROVISIONAL.
                MinRoomAreaSqFt = 1.0,
                MinCeilingHeightFt = 4.0
            };
        }
    }
}