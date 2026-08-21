using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace FireProtection.Backend.Models.DTOs
{
    public class ProjectInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("standard")]
        public string Standard { get; set; } = "NFPA13-2022";

        [JsonProperty("timestampUtc")]
        public string TimestampUtc { get; set; } = DateTime.UtcNow.ToString("o");
    }

    public class UnitsInfo
    {
        [JsonProperty("length")]
        public string Length { get; set; } = "ft";

        [JsonProperty("area")]
        public string Area { get; set; } = "sq_ft";

        [JsonProperty("volume")]
        public string Volume { get; set; } = "cu_ft";

        [JsonProperty("angle")]
        public string Angle { get; set; } = "degrees";
    }

    /// <summary>
    /// Root normalized model snapshot serialized to versioned JSON.
    /// Fully decoupled from Revit API types.
    /// </summary>
    public class ModelSnapshot
    {
        [JsonProperty("schemaVersion")]
        public string SchemaVersion { get; set; } = "1.0";

        [JsonProperty("units")]
        public UnitsInfo Units { get; set; }

        [JsonProperty("coordinateSystem")]
        public CoordinateSystemInfo CoordinateSystem { get; set; }

        [JsonProperty("project")]
        public ProjectInfo Project { get; set; }

        [JsonProperty("model")]
        public ModelStructureInfo Model { get; set; }

        [JsonProperty("levels")]
        public List<LevelData> Levels { get; set; }

        [JsonProperty("rooms")]
        public List<RoomData> Rooms { get; set; }

        [JsonProperty("existingSprinklers")]
        public List<ExistingSprinklerData> ExistingSprinklers { get; set; }

        [JsonProperty("obstacles")]
        public List<ObstacleData> Obstacles { get; set; }

        [JsonProperty("summary")]
        public ValidationSummary Summary { get; set; }

        [JsonProperty("warnings")]
        public List<ExtractionIssue> Warnings { get; set; }

        [JsonProperty("errors")]
        public List<ExtractionIssue> Errors { get; set; }

        public ModelSnapshot()
        {
            Units = new UnitsInfo();
            CoordinateSystem = new CoordinateSystemInfo();
            Project = new ProjectInfo();
            Model = new ModelStructureInfo();
            Levels = new List<LevelData>();
            Rooms = new List<RoomData>();
            ExistingSprinklers = new List<ExistingSprinklerData>();
            Obstacles = new List<ObstacleData>();
            Summary = new ValidationSummary();
            Warnings = new List<ExtractionIssue>();
            Errors = new List<ExtractionIssue>();
        }
    }
}
