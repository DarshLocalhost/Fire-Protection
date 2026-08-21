using Newtonsoft.Json;

namespace FireProtection.Backend.Models.DTOs
{
    /// <summary>
    /// Represents a 3D coordinate point (X, Y, Z) in feet normalized to host MEP coordinates.
    /// </summary>
    public class Point3DData
    {
        [JsonProperty("x")]
        public double X { get; set; }

        [JsonProperty("y")]
        public double Y { get; set; }

        [JsonProperty("z")]
        public double Z { get; set; }

        public Point3DData() { }

        public Point3DData(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public override string ToString()
        {
            return $"({X:F3}, {Y:F3}, {Z:F3})";
        }
    }

    /// <summary>
    /// Represents an axis-aligned 3D bounding box in host MEP coordinates.
    /// </summary>
    public class BoundingBox3DData
    {
        [JsonProperty("min")]
        public Point3DData Min { get; set; }

        [JsonProperty("max")]
        public Point3DData Max { get; set; }

        public BoundingBox3DData()
        {
            Min = new Point3DData();
            Max = new Point3DData();
        }

        public BoundingBox3DData(Point3DData min, Point3DData max)
        {
            Min = min;
            Max = max;
        }
    }
}
