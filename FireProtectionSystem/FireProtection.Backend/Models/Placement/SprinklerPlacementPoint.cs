namespace FireProtection.Backend.Models.Placement
{
    /// <summary>
    /// A single sprinkler location in HOST-model coordinates (feet).
    /// Produced by the placement calculation layer.
    /// This is NOT a Revit FamilyInstance; actual Revit placement is a later phase.
    /// </summary>
    public class SprinklerPlacementPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public string RoomId { get; set; }
        public string LevelId { get; set; }

        public SprinklerPlacementPoint() { }

        public SprinklerPlacementPoint(
            double x,
            double y,
            double z,
            string roomId,
            string levelId)
        {
            X = x;
            Y = y;
            Z = z;
            RoomId = roomId;
            LevelId = levelId;
        }
    }
}