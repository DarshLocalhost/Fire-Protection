using System.Collections.Generic;
using Newtonsoft.Json;

namespace FireProtection.Backend.Models.DTOs
{
    public class HazardData
    {
        [JsonProperty("hazardClass")]
        public string HazardClass { get; set; }

        [JsonProperty("matchedKeyword")]
        public string MatchedKeyword { get; set; }

        [JsonProperty("matchedTerms")]
        public List<string> MatchedTerms { get; set; }

        [JsonProperty("requiresReview")]
        public bool RequiresReview { get; set; }

        [JsonProperty("overridden")]
        public bool Overridden { get; set; }

        [JsonProperty("confirmedBy")]
        public string ConfirmedBy { get; set; }

        public HazardData()
        {
            MatchedTerms = new List<string>();
            HazardClass = "Light";
            MatchedKeyword = string.Empty;
            RequiresReview = false;
        }
    }
}
