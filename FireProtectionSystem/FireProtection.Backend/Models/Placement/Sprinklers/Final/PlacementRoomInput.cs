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
