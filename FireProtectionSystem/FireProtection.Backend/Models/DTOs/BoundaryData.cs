using System.Collections.Generic;
using Newtonsoft.Json;

namespace FireProtection.Backend.Models.DTOs
{
    public enum SegmentType
    {
        Line = 0,
        Arc = 1,
        Other = 2
    }

    public class BoundarySegmentData
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "Line";

        [JsonProperty("start")]
        public Point3DData Start { get; set; }

        [JsonProperty("end")]
        public Point3DData End { get; set; }

        [JsonProperty("mid")]
        public Point3DData Mid { get; set; }

        [JsonProperty("lengthFt")]
        public double LengthFt { get; set; }

        [JsonProperty("radiusFt")]
        public double? RadiusFt { get; set; }
    }

    public class BoundaryLoopData
    {
        [JsonProperty("isOuter")]
        public bool IsOuter { get; set; } = true;

        [JsonProperty("polygon")]
        public List<double[]> Polygon { get; set; }

        [JsonProperty("segments")]
        public List<BoundarySegmentData> Segments { get; set; }

        public BoundaryLoopData()
        {
            Polygon = new List<double[]>();
            Segments = new List<BoundarySegmentData>();
        }
    }

    public class BoundaryData
    {
        [JsonProperty("outerLoop")]
        public BoundaryLoopData OuterLoop { get; set; }

        [JsonProperty("innerLoops")]
        public List<BoundaryLoopData> InnerLoops { get; set; }

        [JsonProperty("polygon")]
        public List<double[]> Polygon { get; set; }

        public BoundaryData()
        {
            OuterLoop = new BoundaryLoopData { IsOuter = true };
            InnerLoops = new List<BoundaryLoopData>();
            Polygon = new List<double[]>();
        }
    }
}
