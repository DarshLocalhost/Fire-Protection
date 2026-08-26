using FireProtection.Backend.Models.DTOs;
using System.Collections.Generic;

namespace FireProtection.Backend.Services.Model
{
    /// <summary>
    /// Lightweight geometry predicates used to associate extracted elements
    /// (ceilings, obstacles, sprinklers) with rooms in the canonical host coordinate space.
    /// All inputs are expected to already be expressed in host MEP coordinates (feet).
    /// </summary>
    public static class SpatialHelpers
    {
        public const double DefaultZOverlapToleranceFt = 1.0;

        public static bool XYOverlap(BoundingBox3DData a, BoundingBox3DData b)
        {
            if (a == null || b == null) return false;
            return !(a.Max.X < b.Min.X || a.Min.X > b.Max.X ||
                     a.Max.Y < b.Min.Y || a.Min.Y > b.Max.Y);
        }

        public static bool ZOverlap(BoundingBox3DData a, BoundingBox3DData b, double toleranceFt = DefaultZOverlapToleranceFt)
        {
            if (a == null || b == null) return false;
            return a.Max.Z >= b.Min.Z - toleranceFt && a.Min.Z <= b.Max.Z + toleranceFt;
        }

        public static bool XYContains(BoundingBox3DData box, Point3DData point)
        {
            if (box == null || point == null) return false;
            return point.X >= box.Min.X && point.X <= box.Max.X &&
                   point.Y >= box.Min.Y && point.Y <= box.Max.Y;
        }

        /// <summary>
        /// Even-odd ray casting point-in-polygon test. Polygon is a list of [x, y] pairs
        /// in host coordinates.
        /// </summary>
        public static bool PointInPolygonXY(double x, double y, List<double[]> polygon)
        {
            if (polygon == null || polygon.Count < 3) return false;

            bool inside = false;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = polygon[i][0], yi = polygon[i][1];
                double xj = polygon[j][0], yj = polygon[j][1];

                bool intersects = ((yi > y) != (yj > y)) &&
                                  (x < (xj - xi) * (y - yi) / (yj - yi) + xi);
                if (intersects) inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// Determines whether a point lies within the room's horizontal extent.
        /// Prefers an accurate polygon test; falls back to the axis-aligned bounding box.
        /// </summary>
        public static bool PointInRoomXY(Point3DData point, RoomData room)
        {
            if (point == null || room == null) return false;

            if (room.Boundary != null && room.Boundary.Polygon != null && room.Boundary.Polygon.Count >= 3)
            {
                if (PointInPolygonXY(point.X, point.Y, room.Boundary.Polygon))
                    return true;
                // Polygon test failed but the point may be in a circular/rounded room where
                // tessellation was coarse; fall through to bounding box as a safety net.
            }

            return XYContains(room.BoundingBox, point);
        }
    }
}
