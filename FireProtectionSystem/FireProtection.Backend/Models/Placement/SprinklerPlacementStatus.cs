namespace FireProtection.Backend.Models.Placement
{
    public enum SprinklerPlacementStatus
    {
        /// <summary>Pipeline ran but final placement logic is not implemented yet (Phase 1).</summary>
        Pending = 0,

        /// <summary>Placement points were successfully produced.</summary>
        Success = 1,

        /// <summary>Room was skipped for a non-error reason (e.g. not eligible).</summary>
        Skipped = 2,

        /// <summary>Placement could not be computed due to a validation failure.</summary>
        Failed = 3
    }
}