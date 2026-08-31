using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Device-agnostic placement input for a single selected room. Carries the minimal data the device
    /// backend needs (level/room identity, geometry, selected family/type). Counterpart of
    /// <c>PlacementRoomInputItem</c>; the sprinkler-specific <c>EffectiveHazardClass</c> is intentionally
    /// absent because hazard class does not drive device (NFPA-72) placement.
    /// </summary>
    public class DeviceRoomInputItem
    {
        public string LevelId { get; set; }
        public string LevelName { get; set; }
        public double LevelElevationFt { get; set; }

        public string RoomId { get; set; }
        public string RoomName { get; set; }
        public string RoomNumber { get; set; }

        public double AreaSqFt { get; set; }
        public double? CeilingHeightFt { get; set; }
        public string CeilingType { get; set; }
        public List<double[]> Polygon { get; set; }

        /// <summary>Full room payload (ceilings, obstacles, existing devices, source, etc.) captured from the
        /// ModelSnapshot JSON and forwarded to the exporter/backend. The Backend rehydrates its own DTOs from
        /// this, avoiding any circular project dependency.</summary>
        public JObject FullRoomJson { get; set; }

        /// <summary>Selected device family/type for this request (populated by the UI).</summary>
        public string SelectedFamilyName { get; set; }
        public string SelectedTypeName { get; set; }

        public DeviceRoomInputItem()
        {
            Polygon = new List<double[]>();
        }
    }
}
