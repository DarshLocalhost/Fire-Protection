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

        /// <summary>Effective per-room device attributes (row override else level default), resolved by the UI
        /// via <c>GetEffective(...)</c>. Null when the device kind does not expose that attribute — smoke
        /// detectors use <see cref="DetectorType"/>/<see cref="Mount"/>/<see cref="CeilingSlope"/>, notification
        /// appliances use <see cref="ApplianceType"/>/<see cref="CandelaDba"/>. The Backend calc engines read
        /// these to select NFPA rules; a null attribute falls back to the engine default.</summary>
        public string DetectorType { get; set; }
        public string Mount { get; set; }
        public string CeilingSlope { get; set; }
        public string ApplianceType { get; set; }
        public string CandelaDba { get; set; }

        public DeviceKind DeviceKind { get; set; } = DeviceKind.SmokeDetector;

        public DeviceRoomInputItem()
        {
            Polygon = new List<double[]>();
        }
    }

    public enum DeviceKind
    {
        Sprinkler,
        SmokeDetector,
        NotificationAppliance
    }
}
