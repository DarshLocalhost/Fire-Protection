namespace FireProtection.Backend.Services.Placement.SmokeDetectors.Final.BruteForce
{
    /// <summary>
    /// Pluggable mapping from detector configuration to an NFPA 72 spacing/coverage rule set.
    /// The calculation engine consumes rules only through this interface so device-specific spacing,
    /// slope, airflow, and wall adjustments remain independent from the sprinkler logic.
    /// </summary>
    public interface ISmokeDetectorPlacementRules
    {
        /// <summary>
        /// True when values are engineering-approved and not placeholder values.
        /// The production reporting layer uses this to distinguish standard-compliant rules from review-required ones.
        /// </summary>
        bool HasApprovedRules { get; }

        /// <summary>
        /// Returns the rule set for the given detector attributes.
        /// Callers must <see cref="SmokeDetectorPlacementRuleSet.Clone"/> before mutating
        /// (per-room overrides, ACH tweaks, etc.).
        /// </summary>
        /// <param name="detectorType">Catalog detector type (e.g. Photoelectric), or null.</param>
        /// <param name="mount">Catalog mount (e.g. Ceiling / Wall), or null.</param>
        /// <param name="ceilingSlope">FLAT / SLOPED / STEPPED / PEAKED, or null.</param>
        /// <param name="airChangesPerHour">
        /// Optional ACH for NFPA 72 Table 17.7.6.3.3.2 high-air-movement spacing.
        /// Null or ≤ 7.5 means no airflow reduction.
        /// </param>
        SmokeDetectorPlacementRuleSet GetRules(
            string detectorType,
            string mount,
            string ceilingSlope,
            double? airChangesPerHour = null);
    }
}