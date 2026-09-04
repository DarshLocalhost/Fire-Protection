using System.Collections.Generic;

namespace FireProtection.Backend.Models.Placement.Sprinklers.Final
{
    public class PlacementInputSnapshot
    {
        public List<PlacementRoomInput> Rooms { get; set; }

        public PlacementInputSnapshot()
        {
            Rooms = new List<PlacementRoomInput>();
        }
    }
}
