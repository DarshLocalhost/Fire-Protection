using System.Collections.Generic;
using Newtonsoft.Json;

namespace FireProtection.Backend.Models.DTOs
{
    public class DocumentInfo
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("isWorkshared")]
        public bool IsWorkshared { get; set; }

        [JsonProperty("revitVersion")]
        public string RevitVersion { get; set; }

        [JsonProperty("isHostMep")]
        public bool IsHostMep { get; set; }
    }

    public class LinkInfo
    {
        [JsonProperty("instanceId")]
        public string InstanceId { get; set; }

        [JsonProperty("linkName")]
        public string LinkName { get; set; }

        [JsonProperty("documentTitle")]
        public string DocumentTitle { get; set; }

        [JsonProperty("documentPath")]
        public string DocumentPath { get; set; }

        [JsonProperty("isLoaded")]
        public bool IsLoaded { get; set; }

        [JsonProperty("isNested")]
        public bool IsNested { get; set; }

        [JsonProperty("roomCount")]
        public int RoomCount { get; set; }

        [JsonProperty("transform")]
        public TransformData Transform { get; set; }

        public LinkInfo()
        {
            Transform = new TransformData();
        }
    }

    public class ModelStructureInfo
    {
        [JsonProperty("host")]
        public DocumentInfo Host { get; set; }

        [JsonProperty("links")]
        public List<LinkInfo> Links { get; set; }

        public ModelStructureInfo()
        {
            Host = new DocumentInfo();
            Links = new List<LinkInfo>();
        }
    }
}
