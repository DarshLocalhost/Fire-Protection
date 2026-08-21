using System.Collections.Generic;
using Newtonsoft.Json;

namespace FireProtection.Backend.Models.DTOs
{
    public class SourceReferenceData
    {
        [JsonProperty("documentTitle")]
        public string DocumentTitle { get; set; }

        [JsonProperty("documentPath")]
        public string DocumentPath { get; set; }

        [JsonProperty("linkInstanceId")]
        public string LinkInstanceId { get; set; }

        [JsonProperty("linkName")]
        public string LinkName { get; set; }

        [JsonProperty("isFromLink")]
        public bool IsFromLink { get; set; }
    }

    public class CeilingData
    {
        [JsonProperty("elementId")]
        public string ElementId { get; set; }

        [JsonProperty("ceilingName")]
        public string CeilingName { get; set; }

        [JsonProperty("familyName")]
        public string FamilyName { get; set; }

        [JsonProperty("typeName")]
        public string TypeName { get; set; }

        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("source")]
        public SourceReferenceData Source { get; set; }

        [JsonProperty("boundingBox")]
        public BoundingBox3DData BoundingBox { get; set; }

        [JsonProperty("bottomElevationFt")]
        public double? BottomElevationFt { get; set; }

        [JsonProperty("topElevationFt")]
        public double? TopElevationFt { get; set; }

        [JsonProperty("heightAboveLevelFt")]
        public double? HeightAboveLevelFt { get; set; }

        [JsonProperty("slopeType")]
        public string SlopeType { get; set; } = "FLAT";

        [JsonProperty("slopeDegrees")]
        public double? SlopeDegrees { get; set; }

        [JsonProperty("boundaryPolygon")]
        public List<double[]> BoundaryPolygon { get; set; }

        [JsonProperty("thicknessFt")]
        public double? ThicknessFt { get; set; }

        [JsonProperty("isRoomDirectCeiling")]
        public bool IsRoomDirectCeiling { get; set; }

        [JsonProperty("notes")]
        public string Notes { get; set; }

        public CeilingData()
        {
            Source = new SourceReferenceData();
            BoundaryPolygon = new List<double[]>();
        }
    }
}
