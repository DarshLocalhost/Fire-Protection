using System.Collections.Generic;
using Newtonsoft.Json;

namespace FireProtection.Backend.Models.DTOs
{
    public class RoomData
    {
        [JsonProperty("roomId")]
        public string RoomId { get; set; }

        [JsonProperty("elementId")]
        public string ElementId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("number")]
        public string Number { get; set; }

        [JsonProperty("levelId")]
        public string LevelId { get; set; }

        [JsonProperty("levelName")]
        public string LevelName { get; set; }

        [JsonProperty("levelElevationFt")]
        public double LevelElevationFt { get; set; }

        [JsonProperty("areaSqFt")]
        public double AreaSqFt { get; set; }

        [JsonProperty("volumeCuFt")]
        public double? VolumeCuFt { get; set; }

        [JsonProperty("phase")]
        public string Phase { get; set; }

        [JsonProperty("source")]
        public SourceReferenceData Source { get; set; }

        [JsonProperty("locationPoint")]
        public Point3DData LocationPoint { get; set; }

        [JsonProperty("boundingBox")]
        public BoundingBox3DData BoundingBox { get; set; }

        [JsonProperty("boundary")]
        public BoundaryData Boundary { get; set; }

        [JsonProperty("hazard")]
        public HazardData Hazard { get; set; }

        [JsonProperty("classification")]
        public ClassificationData Classification { get; set; }

        [JsonProperty("geometry")]
        public GeometryData Geometry { get; set; }

        [JsonProperty("ceilings")]
        public List<CeilingData> Ceilings { get; set; }

        [JsonProperty("associatedObstacleIds")]
        public List<string> AssociatedObstacleIds { get; set; }

        [JsonProperty("requiresHumanReview")]
        public bool RequiresHumanReview { get; set; }

        [JsonProperty("warnings")]
        public List<string> Warnings { get; set; }

        public RoomData()
        {
            Source = new SourceReferenceData();
            Boundary = new BoundaryData();
            Hazard = new HazardData();
            Classification = new ClassificationData();
            Geometry = new GeometryData();
            Ceilings = new List<CeilingData>();
            AssociatedObstacleIds = new List<string>();
            Warnings = new List<string>();
        }
    }

    public class ClassificationData
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

    public class GeometryData
    {
        [JsonProperty("polygon")]
        public List<double[]> Polygon { get; set; }

        [JsonProperty("ceilingHeightFt")]
        public double? CeilingHeightFt { get; set; }

        [JsonProperty("ceilingType")]
        public string CeilingType { get; set; }

        public GeometryData()
        {
            Polygon = new List<double[]>();
        }
    }
}
