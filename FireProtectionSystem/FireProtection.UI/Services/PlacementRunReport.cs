using System.Collections.Generic;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Summary of an end-to-end placement run, returned to the UI.
    /// UI-friendly: no Revit or backend types.
    /// </summary>
    public class PlacementRunReport
    {
        public int RoomsProcessed { get; set; }
        public int RoomsSucceeded { get; set; }
        public int RoomsFailed { get; set; }
        public int RoomsSkipped { get; set; }
        public int ReviewRequiredCount { get; set; }

        public int SprinklersRequested { get; set; }
        public int SprinklersPlaced { get; set; }

        // Generic aliases for device runs so the UI/reporting layer does not need to distinguish the
        // device category to describe a production run accurately.
        public int TotalRequested { get => SprinklersRequested; set => SprinklersRequested = value; }
        public int TotalPlaced { get => SprinklersPlaced; set => SprinklersPlaced = value; }
        public int TotalFailed { get => RoomsFailed; set => RoomsFailed = value; }
        public int TotalSkipped { get => RoomsSkipped; set => RoomsSkipped = value; }

        public bool IsProvisional { get; set; }
        public string AppliedRulesSummary { get; set; }
        public string OverallStatus { get; set; } = "Success";
        public string Summary { get; set; }
        public List<string> Warnings { get; set; }
        public List<string> FailedReasons { get; set; }
        public List<string> ReviewRequiredReasons { get; set; }

        public List<PlacementRoomReport> RoomReports { get; set; }

        public PlacementRunReport()
        {
            RoomReports = new List<PlacementRoomReport>();
            Warnings = new List<string>();
            FailedReasons = new List<string>();
            ReviewRequiredReasons = new List<string>();
        }
    }

    public class PlacementRoomReport
    {
        public string RoomId { get; set; }
        public string RoomName { get; set; }
        public string LevelName { get; set; }
        public string Status { get; set; }      // "Success", "Failed", "Skipped", "ReviewRequired"
        public string Message { get; set; }
        public string FailureReason { get; set; }
        public bool ReviewRequired { get; set; }
        public int PointsRequested { get; set; }
        public int PointsPlaced { get; set; }
    }
}