using System.Collections.Generic;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce
{
    /// <summary>
    /// Room boundary abstraction supporting outer loops and inner loops (openings),
    /// independent of room shape (rectangle, L-shape, concave, irregular).
    /// A point is "inside the room" when it is inside the outer loop and outside every inner loop.
    /// </summary>
    internal sealed class RoomGeometry
    {
        private readonly List<double[]> _outer;
        private readonly List<List<double[]>> _innerLoops;

        private double _minX = double.MaxValue;
        private double _maxX = double.MinValue;
        private double _minY = double.MaxValue;
        private double _maxY = double.MinValue;

        public RoomGeometry(List<double[]> outerPolygon, List<List<double[]>> innerLoops)
        {
            _outer = outerPolygon ?? new List<double[]>();
            _innerLoops = innerLoops ?? new List<List<double[]>>();

            foreach (double[] p in _outer)
            {
                if (p == null || p.Length < 2) continue;
                if (p[0] < _minX) _minX = p[0];
                if (p[0] > _maxX) _maxX = p[0];
                if (p[1] < _minY) _minY = p[1];
                if (p[1] > _maxY) _maxY = p[1];
            }
        }

        public bool IsDegenerate => _outer == null || _outer.Count < 3;

        public double MinX => _minX;
        public double MaxX => _maxX;
        public double MinY => _minY;
        public double MaxY => _maxY;

        /// <summary>
        /// Read-only view of the outer boundary vertices ([x,y] pairs, feet). Exposed so the
        /// notification-appliance audible-coverage audit can count wall crossings along a
        /// sample→appliance line. Pure projection of existing data — no behaviour change.
        /// </summary>
        public IReadOnlyList<double[]> OuterPolygon => _outer;

        public bool IsPointInsideRoom(double x, double y, double tolerance)
        {
            if (IsDegenerate) return false;

            if (!GeometryMath.PointInPolygon(x, y, _outer, tolerance))
            {
                return false;
            }

            foreach (List<double[]> inner in _innerLoops)
            {
                if (inner == null || inner.Count < 3) continue;
                if (GeometryMath.PointInPolygon(x, y, inner, tolerance))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Minimum distance from the point to the outer boundary segments. Used for boundary-clearance checks.
        /// </summary>
        public double DistanceToOuterBoundary(double x, double y)
        {
            if (IsDegenerate || _outer.Count < 2) return double.MaxValue;

            double best = double.MaxValue;
            int n = _outer.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double[] pi = _outer[i];
                double[] pj = _outer[j];
                if (pi == null || pj == null || pi.Length < 2 || pj.Length < 2) continue;

                double d2 = GeometryMath.DistancePointToSegmentSquared(x, y, pi[0], pi[1], pj[0], pj[1]);
                if (d2 < best) best = d2;
            }

            return System.Math.Sqrt(best);
        }
    }
}
