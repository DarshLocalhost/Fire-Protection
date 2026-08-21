using System.Collections.Generic;

namespace FireProtection.Backend.Models.Placement
{
    /// <summary>
    /// Result of running sprinkler placement calculation for a single room.
    /// </summary>
    public class SprinklerPlacementResult
    {
        public string RoomId { get; set; }
        public string RoomName { get; set; }
        public string LevelId { get; set; }
        public string LevelName { get; set; }

        public SprinklerPlacementStatus Status { get; set; }
        public string StatusMessage { get; set; }

        public List<SprinklerPlacementPoint> Points { get; set; }

        /// <summary>Snapshot of the rules actually used for this room.</summary>
        public SprinklerPlacementRules RulesUsed { get; set; }

        /// <summary>Effective hazard class string used (e.g. "OH1").</summary>
        public string EffectiveHazardClass { get; set; }

        public int SprinklerCount
        {
            get { return Points == null ? 0 : Points.Count; }
        }

        public SprinklerPlacementResult()
        {
            Points = new List<SprinklerPlacementPoint>();
            Status = SprinklerPlacementStatus.Pending;
        }
    }
}