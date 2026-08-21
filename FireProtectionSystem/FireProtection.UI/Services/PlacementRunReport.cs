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

        public int SprinklersRequested { get; set; }
        public int SprinklersPlaced { get; set; }

        public List<PlacementRoomReport> RoomReports { get; set; }

        public PlacementRunReport()
        {
            RoomReports = new List<PlacementRoomReport>();
        }
    }

    public class PlacementRoomReport
    {
        public string RoomId { get; set; }
        public string RoomName { get; set; }
        public string LevelName { get; set; }
        public string Status { get; set; }      // "Success", "Failed", "Skipped"
        public string Message { get; set; }
        public int PointsRequested { get; set; }
        public int PointsPlaced { get; set; }
    }
}