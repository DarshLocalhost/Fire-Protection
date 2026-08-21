using FireProtection.Backend.Models.DTOs;

namespace FireProtection.Backend.Services.Placement
{
    /// <summary>
    /// Input required to run placement for a single room.
    /// Assembled by the caller from the user's selection (rooms + effective hazard).
    /// </summary>
    public class SprinklerPlacementRequest
    {
        public LevelData Level { get; set; }
        public RoomData Room { get; set; }

        /// <summary>
        /// Effective (possibly user-overridden) hazard class string,
        /// e.g. "LIGHT", "OH1", "OH2", "EH1", "EH2".
        /// </summary>
        public string EffectiveHazardClass { get; set; }
    }
}