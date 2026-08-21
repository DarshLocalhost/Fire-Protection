using Newtonsoft.Json;

namespace FireProtection.Backend.Models.DTOs
{
    public class CoordinateSystemInfo
    {
        [JsonProperty("canonical")]
        public string Canonical { get; set; } = "host_mep_model";

        [JsonProperty("lengthUnit")]
        public string LengthUnit { get; set; } = "feet";

        [JsonProperty("description")]
        public string Description { get; set; } = "All coordinates, elevations, and geometry are normalized to the active MEP host model internal coordinate system in decimal feet.";
    }
}
