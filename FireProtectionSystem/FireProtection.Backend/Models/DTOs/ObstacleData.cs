using System.Collections.Generic;
using Newtonsoft.Json;

namespace FireProtection.Backend.Models.DTOs
{
    public class ObstacleData
    {
        [JsonProperty("elementId")]
        public string ElementId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("structuralType")]
        public string StructuralType { get; set; }

        [JsonProperty("levelId")]
        public string LevelId { get; set; }

        [JsonProperty("source")]
        public SourceReferenceData Source { get; set; }

        [JsonProperty("boundingBox")]
        public BoundingBox3DData BoundingBox { get; set; }

        [JsonProperty("centerPoint")]
        public Point3DData CenterPoint { get; set; }

        [JsonProperty("dimensionsFt")]
        public Point3DData DimensionsFt { get; set; }

        [JsonProperty("associatedRoomIds")]
        public List<string> AssociatedRoomIds { get; set; }

        public ObstacleData()
        {
            Source = new SourceReferenceData();
            AssociatedRoomIds = new List<string>();
        }
    }
}
