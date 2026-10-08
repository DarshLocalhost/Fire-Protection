using System.Collections.Generic;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.UI.Services;
using Newtonsoft.Json;

namespace FireProtection.Backend.Models.Placement.SmokeDetectors.Final
{
    /// <summary>
    /// Per-room input for the NFPA 72 smoke detector calculation engine.
    /// Mirrors <see cref="PlacementRoomInput"/> but without sprinkler hazard/orientation fields.
    /// </summary>
    public class SmokeDetectorRoomInput
    {
        [JsonProperty("levelId")]
        public string LevelId { get; set; }

        [JsonProperty("levelName")]
        public string LevelName { get; set; }

        [JsonProperty("levelElevationFt")]
        public double LevelElevationFt { get; set; }

        [JsonProperty("roomId")]
        public string RoomId { get; set; }

        [JsonProperty("roomName")]
        public string RoomName { get; set; }

        [JsonProperty("roomNumber")]
        public string RoomNumber { get; set; }

        [JsonProperty("areaSqFt")]
        public double AreaSqFt { get; set; }

        [JsonProperty("volumeCuFt")]
        public double? VolumeCuFt { get; set; }

        [JsonProperty("ceilingHeightFt")]
        public double? CeilingHeightFt { get; set; }

        [JsonProperty("ceilingType")]
        public string CeilingType { get; set; }

        [JsonProperty("detectorType")]
        public string DetectorType { get; set; }

        [JsonProperty("mount")]
        public string Mount { get; set; }

        [JsonProperty("ceilingSlope")]
        public string CeilingSlope { get; set; }

        [JsonProperty("selectedFamilyName")]
        public string SelectedFamilyName { get; set; }

        [JsonProperty("selectedTypeName")]
        public string SelectedTypeName { get; set; }

        [JsonProperty("selectedPlacementBehavior")]
        public DevicePlacementBehavior SelectedPlacementBehavior { get; set; }

        [JsonProperty("ruleDescriptor")]
        public string RuleDescriptor { get; set; }

        [JsonProperty("airChangesPerHour")]
        public double? AirChangesPerHour { get; set; }

        [JsonProperty("overrideMaxSpacingFt")]
        public double? OverrideMaxSpacingFt { get; set; }

        [JsonProperty("overrideBoundaryClearanceFt")]
        public double? OverrideBoundaryClearanceFt { get; set; }

        /// <summary>
        /// Per-room MAXIMUM distance-to-wall (device→wall) override in feet, enforced
        /// post-selection independently of the MINIMUM <see cref="OverrideBoundaryClearanceFt"/>.
        /// null = use the engine-derived value (½·spacing). NOT clamped — flagged provisional.
        /// </summary>
        [JsonProperty("overrideMaxDistanceToWallFt")]
        public double? OverrideMaxDistanceToWallFt { get; set; }

        /// <summary>Per-room fallback ceiling-tile pitch (feet), primary axis, used only when the
        /// ceiling has no readable grid pattern. Enables tile-center snapping from a user-entered
        /// tile size. null = free lattice (no synthetic grid).</summary>
        [JsonProperty("overrideCeilingTileUFt")]
        public double? OverrideCeilingTileUFt { get; set; }

        /// <summary>Per-room fallback ceiling-tile pitch (feet), secondary axis. null = square tile.</summary>
        [JsonProperty("overrideCeilingTileVFt")]
        public double? OverrideCeilingTileVFt { get; set; }

        [JsonProperty("boundaryPolygon")]
        public List<double[]> BoundaryPolygon { get; set; }

        [JsonProperty("boundary")]
        public BoundaryData Boundary { get; set; }

        [JsonProperty("ceilings")]
        public List<CeilingData> Ceilings { get; set; }

        [JsonProperty("obstacles")]
        public List<ObstacleData> Obstacles { get; set; }

        [JsonProperty("existingDetectors")]
        public List<Point3DData> ExistingDetectors { get; set; }

        [JsonProperty("source")]
        public SourceReferenceData Source { get; set; }

        [JsonProperty("deviceKind")]
        public DeviceKind DeviceKind { get; set; } = DeviceKind.SmokeDetector;

        public SmokeDetectorRoomInput()
        {
            SelectedPlacementBehavior = DevicePlacementBehavior.Unknown;
            BoundaryPolygon = new List<double[]>();
            Boundary = new BoundaryData();
            Ceilings = new List<CeilingData>();
            Obstacles = new List<ObstacleData>();
            ExistingDetectors = new List<Point3DData>();
            Source = new SourceReferenceData();
        }
    }
}