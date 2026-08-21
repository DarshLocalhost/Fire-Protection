using System.Collections.Generic;
using Newtonsoft.Json;

namespace FireProtection.Backend.Models.DTOs
{
    public class LevelData
    {
        [JsonProperty("levelId")]
        public string LevelId { get; set; }

        [JsonProperty("elementId")]
        public string ElementId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("elevationFt")]
        public double ElevationFt { get; set; }

        [JsonProperty("origin")]
        public string Origin { get; set; } = string.Empty;

        [JsonProperty("source")]
        public SourceReferenceData Source { get; set; }

        [JsonProperty("roomCount")]
        public int RoomCount => Rooms != null ? Rooms.Count : 0;

        [JsonProperty("rooms")]
        public List<RoomData> Rooms { get; set; }

        public LevelData()
        {
            Source = new SourceReferenceData();
            Rooms = new List<RoomData>();
        }
    }
}
