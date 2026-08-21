using Newtonsoft.Json;

namespace FireProtection.Backend.Models.DTOs
{
    public class ExistingSprinklerData
    {
        [JsonProperty("elementId")]
        public string ElementId { get; set; }

        [JsonProperty("familyName")]
        public string FamilyName { get; set; }

        [JsonProperty("typeName")]
        public string TypeName { get; set; }

        [JsonProperty("location")]
        public Point3DData Location { get; set; }

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

        [JsonProperty("hostElementId")]
        public string HostElementId { get; set; }

        [JsonProperty("hostName")]
        public string HostName { get; set; }

        [JsonProperty("source")]
        public SourceReferenceData Source { get; set; }

        [JsonProperty("orientation")]
        public Point3DData Orientation { get; set; }

        [JsonProperty("mountingType")]
        public string MountingType { get; set; }

        [JsonProperty("boundingBox")]
        public BoundingBox3DData BoundingBox { get; set; }

        public ExistingSprinklerData()
        {
            Location = new Point3DData();
            Source = new SourceReferenceData();
            Orientation = new Point3DData(0, 0, -1);
            MountingType = "Pendent";
        }
    }
}
