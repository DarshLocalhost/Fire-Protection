using System.Collections.Generic;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce
{
    /// <summary>
    /// Pure 2D geometry helpers used by the BruteForce engine. No Revit API dependencies.
    /// All coordinates are host MEP model coordinates (feet). Comparisons use configurable
    /// tolerances; exact floating point equality is never used.
    /// </summary>
    internal static class GeometryMath
    {
        /// <summary>
        /// Even-odd ray casting point-in-polygon test. Polygon is an open or closed list of
        /// [x, y] vertices. Tolerant: points within <paramref name="tolerance"/> of an edge are
        /// treated as inside.
        /// </summary>
        public static bool PointInPolygon(double x, double y, List<double[]> polygon, double tolerance)
        {
            if (polygon == null || polygon.Count < 3) return false;

            int n = polygon.Count;
            bool inside = false;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double[] pi = polygon[i];
                double[] pj = polygon[j];
                if (pi == null || pj == null || pi.Length < 2 || pj.Length < 2) continue;

                double xi = pi[0], yi = pi[1];
                double xj = pj[0], yj = pj[1];

                // Tolerant edge test: distance from (x,y) to segment (pi,pj).
                if (DistancePointToSegmentSquared(x, y, xi, yi, xj, yj) <= tolerance * tolerance)
                {
                    return true;
                }

                bool intersects = ((yi > y) != (yj > y)) &&
                    (x < (xj - xi) * (y - yi) / (yj - yi) + xi);

                if (intersects) inside = !inside;
            }

            return inside;
        }

        /// <summary>
        /// Squared distance from point (px,py) to segment (ax,ay)-(bx,by).
        /// </summary>
        public static double DistancePointToSegmentSquared(
            double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax;
            double dy = by - ay;

            double lenSq = dx * dx + dy * dy;
            if (lenSq <= double.Epsilon)
            {
                double ddx = px - ax;
                double ddy = py - ay;
                return ddx * ddx + ddy * ddy;
            }

            double t = ((px - ax) * dx + (py - ay) * dy) / lenSq;
            if (t < 0.0) t = 0.0;
            else if (t > 1.0) t = 1.0;

            double cx = ax + t * dx;
            double cy = ay + t * dy;

            double ex = px - cx;
            double ey = py - cy;
            return ex * ex + ey * ey;
        }

        public static double Distance(double ax, double ay, double bx, double by)
        {
            double dx = ax - bx;
            double dy = ay - by;
            return System.Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Returns true when (x,y) lies inside the axis-aligned box expanded by <paramref name="clearance"/>.
        /// </summary>
        public static bool InsideExpandedBox(
            double x, double y, double minX, double minY, double maxX, double maxY, double clearance)
        {
            return x >= minX - clearance && x <= maxX + clearance &&
                   y >= minY - clearance && y <= maxY + clearance;
        }
    }
}
