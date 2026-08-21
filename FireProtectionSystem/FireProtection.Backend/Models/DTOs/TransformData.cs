using Newtonsoft.Json;

namespace FireProtection.Backend.Models.DTOs
{
    public class TransformData
    {
        [JsonProperty("isIdentity")]
        public bool IsIdentity { get; set; }

        [JsonProperty("origin")]
        public Point3DData Origin { get; set; }

        [JsonProperty("basisX")]
        public Point3DData BasisX { get; set; }

        [JsonProperty("basisY")]
        public Point3DData BasisY { get; set; }

        [JsonProperty("basisZ")]
        public Point3DData BasisZ { get; set; }

        [JsonProperty("scale")]
        public double Scale { get; set; } = 1.0;

        public TransformData()
        {
            Origin = new Point3DData(0, 0, 0);
            BasisX = new Point3DData(1, 0, 0);
            BasisY = new Point3DData(0, 1, 0);
            BasisZ = new Point3DData(0, 0, 1);
            IsIdentity = true;
        }
    }
}
