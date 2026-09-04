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

        /// <summary>Per-room sprinkler family override (Decision 017). Carried on the result so
        /// the placement layer and the missing-family probe can resolve the same symbol
        /// the user picked for this room in the UI.</summary>
        public string SprinklerFamilyName { get; set; }

        /// <summary>Per-room sprinkler type override (Decision 017).</summary>
        public string SprinklerTypeName { get; set; }

        public CalculationStatus Status { get; set; }

        public int RequiredCount { get; set; }
        public int CalculatedCount { get; set; }

        public List<CalculatedSprinklerPoint> Points { get; set; }

        /// <summary>
        /// Room boundary in HOST coordinates, [x,y] pairs in feet — the same polygon the calculation used.
        /// Carried onto the result so the placement layer can (a) prove every point it is about to create
        /// actually falls inside its room (the guard against linked-model coordinate-space mistakes, where
        /// an untransformed point lands hundreds of feet away) and (b) find existing devices in the room.
        /// </summary>
        public List<double[]> Polygon { get; set; }

        /// <summary>Spacing actually used for this room (after per-room override and clamping), in feet.</summary>
        public double? AppliedMaxSpacingFt { get; set; }

        /// <summary>Boundary clearance actually used for this room, in feet.</summary>
        public double? AppliedBoundaryClearanceFt { get; set; }

        /// <summary>
        /// False while spacing comes from the provisional placeholder rather than an approved NFPA-13 rule set.
        /// Stamped onto every placed element so a reviewer can tell provisional geometry from approved geometry.
        /// </summary>
        public bool RulesApproved { get; set; }

        public List<string> Warnings { get; set; }
        public List<string> Errors { get; set; }
        public List<string> Diagnostics { get; set; }

        public RoomCalculationResult()
        {
            Points = new List<CalculatedSprinklerPoint>();
            Polygon = new List<double[]>();
            Warnings = new List<string>();
            Errors = new List<string>();
            Diagnostics = new List<string>();
        }

        public bool IsSuccessful =>
            Status == CalculationStatus.Success || Status == CalculationStatus.ReviewRequired;
    }
}
