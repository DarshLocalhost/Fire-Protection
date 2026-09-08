namespace FireProtection.UI.Models.Sprinklers.BruteForce
{
    /// <summary>
    /// A finally selected sprinkler placement point, in host MEP model coordinates (feet).
    /// </summary>
    public class CalculatedSprinklerPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public string RoomId { get; set; }
        public string LevelId { get; set; }

        /// <summary>
        /// Level name as known at calculation time. Used by the placement layer to map a (possibly
        /// linked-model) level ElementId to the correct host-document Level when the ElementId itself
        /// belongs to a linked document.
        /// </summary>
        public string LevelName { get; set; }

        /// <summary>
        /// Index of the polygon edge this point was generated from (sidewall only).
        /// The placement layer uses this to find the wall face to host on.
        /// Null for ceiling-grid candidates.
        /// </summary>
        public int? WallEdgeIndex { get; set; }

        public override string ToString()
        {
            return $"({X:F3}, {Y:F3}, {Z:F3})";
        }
    }
}
