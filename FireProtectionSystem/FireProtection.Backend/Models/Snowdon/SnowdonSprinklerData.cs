using System.Collections.Generic;
using Newtonsoft.Json;

namespace FireProtection.Backend.Models.Snowdon
{
    /// <summary>
    /// Root object of the Snowdon sprinkler-coordinates JSON.
    /// Schema: { "units": "ft", "draft": false, "roomsRequiringReview": [...], "sprinklers": [...] }
    /// This is the TEST INPUT schema. It is NOT the same as extractTest.json.
    /// </summary>
    public class SnowdonSprinklerData
    {
        [JsonProperty("units")]
        public string Units { get; set; }

        [JsonProperty("draft")]
        public bool Draft { get; set; }

        [JsonProperty("roomsRequiringReview")]
        public List<string> RoomsRequiringReview { get; set; }

        [JsonProperty("sprinklers")]
        public List<SnowdonSprinklerEntry> Sprinklers { get; set; }

        public SnowdonSprinklerData()
        {
            RoomsRequiringReview = new List<string>();
            Sprinklers = new List<SnowdonSprinklerEntry>();
        }
    }

    /// <summary>
    /// A single sprinkler entry in the Snowdon JSON.
    /// x and y are in host-model coordinates (feet).
    /// offsetFromLevelFt is the height above the level elevation (feet).
    /// Z = level.ElevationFt + offsetFromLevelFt.
    /// </summary>
    public class SnowdonSprinklerEntry
    {
        [JsonProperty("tag")]
        public string Tag { get; set; }

        [JsonProperty("levelId")]
        public string LevelId { get; set; }

        [JsonProperty("roomId")]
        public string RoomId { get; set; }

        [JsonProperty("x")]
        public double X { get; set; }

        [JsonProperty("y")]
        public double Y { get; set; }

        [JsonProperty("offsetFromLevelFt")]
        public double OffsetFromLevelFt { get; set; }
    }
}