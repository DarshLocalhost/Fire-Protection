using System.Collections.Generic;
using FireProtection.Backend.Models.DTOs;
using Newtonsoft.Json;

namespace FireProtection.Backend.Models.Placement.Sprinklers.Final
{
    /// <summary>
    /// Represents all input data for a single room prepared for the future sprinkler calculation engine.
    /// </summary>
    public class PlacementRoomInput
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

        [JsonProperty("effectiveHazardClass")]
        public string EffectiveHazardClass { get; set; }

        [JsonProperty("ceilingHeightFt")]
        public double? CeilingHeightFt { get; set; }

        [JsonProperty("ceilingType")]
        public string CeilingType { get; set; }

        [JsonProperty("boundaryPolygon")]
        public List<double[]> BoundaryPolygon { get; set; }

        [JsonProperty("boundary")]
        public BoundaryData Boundary { get; set; }

        [JsonProperty("ceilings")]
        public List<CeilingData> Ceilings { get; set; }

        [JsonProperty("obstacles")]
        public List<ObstacleData> Obstacles { get; set; }

        [JsonProperty("existingSprinklers")]
        public List<ExistingSprinklerData> ExistingSprinklers { get; set; }

        /// <summary>
        /// Per-room sprinkler family override (Decision 017). When non-null, this drives
        /// the catalog/placement pipeline for this room (overrides the universal selection
        /// in the <c>CalculateBruteForce</c> entry point).
        /// </summary>
        [JsonProperty("selectedSprinklerFamilyName")]
        public string SelectedSprinklerFamilyName { get; set; }

        /// <summary>Per-room sprinkler type override (Decision 017).</summary>
        [JsonProperty("selectedSprinklerTypeName")]
        public string SelectedSprinklerTypeName { get; set; }

        /// <summary>
        /// Per-room PROVEN Revit's <c>FamilyPlacementType</c> string (e.g.
        /// <c>"FaceBased"</c> / <c>"WorkPlaneBased"</c> / <c>"OneLevelBased"</c>).
        /// Resolved at the Revit-aware boundary by
        /// <see cref="FireProtection.Backend.Services.Placement.RevitSprinklerFamilySource"/>
        /// from the per-row <see cref="SelectedSprinklerFamilyName"/> /
        /// <see cref="SelectedSprinklerTypeName"/>. <c>null</c> when the family/type
        /// was not resolved.
        ///
        /// Step 2 carries this value into the calculation input WITHOUT changing
        /// the calculation algorithm. The future candidate generator will read it
        /// to switch between candidate modes.
        /// </summary>
        [JsonProperty("selectedSprinklerFamilyPlacementType")]
        public string SelectedSprinklerFamilyPlacementType { get; set; }

        /// <summary>
        /// Per-room plain, Revit-free classification of the placement behavior required
        /// by the per-row sprinkler family/type. Default
        /// <see cref="DevicePlacementBehavior.Unknown"/> when the per-row family/type
        /// was not resolved. Step 2 only carries this value through the pipeline;
        /// <see cref="FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce.BruteForceCalculationService"/>
        /// does not yet read it.
        /// </summary>
        [JsonProperty("selectedSprinklerPlacementBehavior")]
        public DevicePlacementBehavior SelectedSprinklerPlacementBehavior { get; set; } = DevicePlacementBehavior.Unknown;

        /// <summary>Per-room MaxSpacingFt override in feet (Decision 018). null = use rule-set default.</summary>
        [JsonProperty("overrideMaxSpacingFt")]
        public double? OverrideMaxSpacingFt { get; set; }

        /// <summary>Per-room BoundaryClearanceFt (wall) override in feet (Decision 018). null = use rule-set default.</summary>
        [JsonProperty("overrideBoundaryClearanceFt")]
        public double? OverrideBoundaryClearanceFt { get; set; }

        /// <summary>
        /// Per-row sprinkler orientation: <c>"pendent"</c>, <c>"upright"</c>, or
        /// <c>"sidewall"</c>. Drives <see cref="FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce.HazardPlacementRuleSet.GetOrientationAdjustment"/>
        /// which scales MaxSpacing / CoverageRadius per NFPA 13 §10.2 (sidewall gets a
        /// tighter spacing multiplier). <c>null</c> / empty / unknown = no adjustment (1.0).
        /// </summary>
        [JsonProperty("selectedSprinklerOrientation")]
        public string SelectedSprinklerOrientation { get; set; }

        [JsonProperty("source")]
        public SourceReferenceData Source { get; set; }

        public PlacementRoomInput()
        {
            BoundaryPolygon = new List<double[]>();
            Boundary = new BoundaryData();
            Ceilings = new List<CeilingData>();
            Obstacles = new List<ObstacleData>();
            ExistingSprinklers = new List<ExistingSprinklerData>();
            Source = new SourceReferenceData();
        }
    }
}
