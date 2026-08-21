using System.Collections.Generic;
using Newtonsoft.Json;

namespace FireProtection.UI.Models
{
    public class FireProtectionUiData
    {
        [JsonProperty("schemaVersion")]
        public string SchemaVersion { get; set; }

        [JsonProperty("project")]
        public ProjectUiData Project { get; set; }

        [JsonProperty("units")]
        public UnitsUiData Units { get; set; }

        [JsonProperty("levels")]
        public List<LevelUiData> Levels { get; set; }

        public FireProtectionUiData()
        {
            Levels = new List<LevelUiData>();
        }
    }

    public class ProjectUiData
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("standard")]
        public string Standard { get; set; }
    }

    public class UnitsUiData
    {
        [JsonProperty("length")]
        public string Length { get; set; }
    }

    public class LevelUiData
    {
        [JsonProperty("levelId")]
        public string LevelId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("origin")]
        public string Origin { get; set; }

        [JsonProperty("elevationFt")]
        public double ElevationFt { get; set; }

        [JsonProperty("rooms")]
        public List<RoomUiData> Rooms { get; set; }

        public LevelUiData()
        {
            Rooms = new List<RoomUiData>();
        }
    }

    public class RoomUiData
    {
        [JsonProperty("roomId")]
        public string RoomId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("number")]
        public string Number { get; set; }

        [JsonProperty("levelName")]
        public string LevelName { get; set; }

        [JsonProperty("areaSqFt")]
        public double AreaSqFt { get; set; }

        [JsonProperty("classification")]
        public ClassificationUiData Classification { get; set; }

        [JsonProperty("geometry")]
        public GeometryUiData Geometry { get; set; }

        [JsonProperty("requiresHumanReview")]
        public bool RequiresHumanReview { get; set; }

        public RoomUiData()
        {
            Classification = new ClassificationUiData();
            Geometry = new GeometryUiData();
        }
    }

    public class ClassificationUiData
    {
        [JsonProperty("hazardClass")]
        public string HazardClass { get; set; }

        [JsonProperty("suggestedByClassifier")]
        public string SuggestedByClassifier { get; set; }

        [JsonProperty("overridden")]
        public bool Overridden { get; set; }

        [JsonProperty("confirmedBy")]
        public string ConfirmedBy { get; set; }
    }

    public class GeometryUiData
    {
        [JsonProperty("polygon")]
        public List<double[]> Polygon { get; set; }

        [JsonProperty("ceilingHeightFt")]
        public double? CeilingHeightFt { get; set; }

        [JsonProperty("ceilingType")]
        public string CeilingType { get; set; }

        public GeometryUiData()
        {
            Polygon = new List<double[]>();
        }
    }
}