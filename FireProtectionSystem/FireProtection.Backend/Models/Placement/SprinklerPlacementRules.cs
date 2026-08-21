namespace FireProtection.Backend.Models.Placement
{
    /// <summary>
    /// Abstract, configurable placement rule set consumed by the placement service.
    ///
    /// The concrete values are supplied by an ISprinklerRuleProvider. The placement
    /// algorithm MUST read all thresholds from an instance of this class and must
    /// NOT hard-code any of them.
    ///
    /// NOTE: This rule set intentionally does NOT carry any sprinkler Family or
    /// Type identifier. The actual Revit Family/Type used for placement is
    /// determined exclusively by the user selection carried on
    /// PlacementRequestItem.SelectedSprinklerFamilyName and
    /// PlacementRequestItem.SelectedSprinklerTypeName, and is resolved to a
    /// FamilySymbol by SprinklerFamilyResolver in the executor.
    ///
    /// Rule providers MUST NOT select, override, or fall back to a sprinkler
    /// Family/Type.
    /// </summary>
    public abstract class SprinklerPlacementRules
    {
        public double MaxCoveragePerHeadSqFt { get; set; }

        public double MaxSprinklerSpacingFt { get; set; }
        public double MinSprinklerSpacingFt { get; set; }

        public double MaxWallDistanceFt { get; set; }
        public double MinWallDistanceFt { get; set; }

        /// <summary>
        /// Provisional mounting offset from the room ceiling, measured downward
        /// (i.e. sprinkler Z = ceiling Z minus this value).
        /// </summary>
        public double MountingOffsetFromCeilingFt { get; set; }

        /// <summary>
        /// Returns whether the given room is eligible for placement under these rules.
        /// Implementations may inspect area, hazard class, ceiling height, etc.
        /// </summary>
        public abstract bool IsRoomEligible(
            double areaSqFt,
            double? ceilingHeightFt,
            string effectiveHazardClass,
            out string reason);
    }
}