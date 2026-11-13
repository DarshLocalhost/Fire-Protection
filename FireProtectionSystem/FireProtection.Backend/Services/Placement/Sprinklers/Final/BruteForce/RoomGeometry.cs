using System.Collections.Generic;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce
{
    /// <summary>
    /// Room boundary abstraction supporting outer loops and inner loops (openings),
    /// independent of room shape (rectangle, L-shape, concave, irregular).
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
            _outer = CleanPolygon(outerPolygon);
            _innerLoops = new List<List<double[]>>();

            if (innerLoops != null)
            {
                foreach (List<double[]> inner in innerLoops)
                {
                    List<double[]> cleaned = CleanPolygon(inner);
                    if (cleaned.Count >= 3)
                        _innerLoops.Add(cleaned);
                }
            }

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

        /// <summary>
        /// Removes duplicate consecutive vertices and closes loop endpoints.
        /// </summary>
        private static List<double[]> CleanPolygon(List<double[]> raw)
        {
            List<double[]> cleaned = new List<double[]>();
            if (raw == null || raw.Count == 0) return cleaned;

            foreach (double[] pt in raw)
            {
                if (pt == null || pt.Length < 2) continue;
                if (cleaned.Count > 0)
                {
                    double[] prev = cleaned[cleaned.Count - 1];
                    if (System.Math.Abs(pt[0] - prev[0]) < 1e-6 && System.Math.Abs(pt[1] - prev[1]) < 1e-6)
                        continue;
                }
                cleaned.Add(new double[] { pt[0], pt[1] });
            }

            if (cleaned.Count >= 3)
            {
                double[] first = cleaned[0];
                double[] last = cleaned[cleaned.Count - 1];
                if (System.Math.Abs(first[0] - last[0]) < 1e-6 && System.Math.Abs(first[1] - last[1]) < 1e-6)
                {
                    cleaned.RemoveAt(cleaned.Count - 1);
                }
            }

            return cleaned;
        }
    }
}