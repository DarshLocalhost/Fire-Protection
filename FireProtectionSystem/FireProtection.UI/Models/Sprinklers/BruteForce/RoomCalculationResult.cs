using System.Collections.Generic;

namespace FireProtection.UI.Models.Sprinklers.BruteForce
{
    /// <summary>
    /// Per-room BruteForce calculation result. A normal room-level failure is recorded here
    /// and must not abort calculation of other rooms.
    /// </summary>
    public class RoomCalculationResult
    {
        public string RoomId { get; set; }
        public string RoomName { get; set; }
        public string RoomNumber { get; set; }

        public CalculationStatus Status { get; set; }

        public int RequiredCount { get; set; }
        public int CalculatedCount { get; set; }

        public List<CalculatedSprinklerPoint> Points { get; set; }

        public List<string> Warnings { get; set; }
        public List<string> Errors { get; set; }
        public List<string> Diagnostics { get; set; }

        public RoomCalculationResult()
        {
            Points = new List<CalculatedSprinklerPoint>();
            Warnings = new List<string>();
            Errors = new List<string>();
            Diagnostics = new List<string>();
        }

        public bool IsSuccessful =>
            Status == CalculationStatus.Success || Status == CalculationStatus.ReviewRequired;
    }
}
