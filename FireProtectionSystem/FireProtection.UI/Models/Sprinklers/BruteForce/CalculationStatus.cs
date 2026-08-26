namespace FireProtection.UI.Models.Sprinklers.BruteForce
{
    /// <summary>
    /// Explicit, non-exception status for a room or overall BruteForce calculation.
    /// </summary>
    public enum CalculationStatus
    {
        Success = 0,
        ReviewRequired = 1,
        InvalidInput = 2,
        InvalidRoomGeometry = 3,
        MissingCeiling = 4,
        UnsupportedCeiling = 5,
        NoValidCandidates = 6,
        InsufficientCoverage = 7,
        Failed = 8
    }
}
