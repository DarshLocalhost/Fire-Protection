using System;
using System.Collections.Generic;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Placement;

namespace FireProtection.Backend.Services.Placement
{
    /// <summary>
    /// Orchestrates sprinkler placement calculation.
    ///
    /// Phase 2 scope (this file):
    ///   * Validate room / polygon.
    ///   * Resolve generic placement rules.
    ///   * Check room eligibility.
    ///   * Generate a deterministic sprinkler-point grid inside the polygon.
    ///   * Respect the existing generic wall-clearance and min-spacing rules.
    ///
    /// This service does NOT create Revit FamilyInstances, does NOT modify
    /// the Revit document, and does NOT implement NFPA-specific coverage,
    /// obstruction, or ceiling logic. Those are reserved for the senior's
    /// final rule implementation.
    /// </summary>
    public class SprinklerPlacementService
    {
        private readonly ISprinklerRuleProvider _ruleProvider;

        // Numerical tolerance in feet used for boundary comparisons.
        // Deliberately small so it doesn't materially affect placement.
        private const double GeometryToleranceFt = 1e-6;

        public SprinklerPlacementService(ISprinklerRuleProvider ruleProvider)
        {
            if (ruleProvider == null) throw new ArgumentNullException(nameof(ruleProvider));
            _ruleProvider = ruleProvider;
        }

        public List<SprinklerPlacementResult> PlaceForRooms(
            IEnumerable<SprinklerPlacementRequest> requests)
        {
            List<SprinklerPlacementResult> results = new List<SprinklerPlacementResult>();
            if (requests == null) return results;

            foreach (SprinklerPlacementRequest request in requests)
            {
                results.Add(PlaceForRoom(request));
            }

            return results;
        }

        public SprinklerPlacementResult PlaceForRoom(SprinklerPlacementRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            RoomData room = request.Room;
            LevelData level = request.Level;

            SprinklerPlacementResult result = new SprinklerPlacementResult
            {
                RoomId = room?.RoomId,
                RoomName = room?.Name,
                LevelId = level?.LevelId,
                LevelName = level?.Name,
                EffectiveHazardClass = request.EffectiveHazardClass
            };

            // 1) Validate inputs.
            if (room == null)
            {
                result.Status = SprinklerPlacementStatus.Failed;
                result.StatusMessage = "Room data is null.";
                return result;
            }

            if (level == null)
            {
                result.Status = SprinklerPlacementStatus.Failed;
                result.StatusMessage = "Level data is null.";
                return result;
            }

            // 2) Build a clean, closed polygon from the room boundary.
            List<double[]> polygon = BuildCleanPolygon(room);
            if (polygon == null || polygon.Count < 3)
            {
                result.Status = SprinklerPlacementStatus.Failed;
                result.StatusMessage = "Room has no usable boundary polygon.";
                return result;
            }

            // 3) Resolve rules from provider.
            SprinklerPlacementRules rules =
                _ruleProvider.GetRulesFor(request.EffectiveHazardClass);

            if (rules == null)
            {
                result.Status = SprinklerPlacementStatus.Failed;
                result.StatusMessage =
                    "No placement rules available for hazard class '"
                    + request.EffectiveHazardClass + "'.";
                return result;
            }

            result.RulesUsed = rules;

            // 4) Eligibility check via rules.
            double? ceilingHeightFt = room.Geometry?.CeilingHeightFt;

            string reason;
            bool eligible = rules.IsRoomEligible(
                room.AreaSqFt,
                ceilingHeightFt,
                request.EffectiveHazardClass,
                out reason);

            if (!eligible)
            {
                result.Status = SprinklerPlacementStatus.Skipped;
                result.StatusMessage = reason ?? "Room is not eligible under current rules.";
                return result;
            }

            // 5) Rule sanity check.
            if (rules.MaxSprinklerSpacingFt <= 0.0)
            {
                result.Status = SprinklerPlacementStatus.Failed;
                result.StatusMessage =
                    "Invalid rule: MaxSprinklerSpacingFt must be greater than zero.";
                return result;
            }

            // 6) Generate deterministic candidate grid inside the polygon bounding box.
            List<SprinklerPlacementPoint> generated = GenerateGridPoints(
                polygon,
                rules,
                room,
                level);

            if (generated.Count == 0)
            {
                result.Status = SprinklerPlacementStatus.Failed;
                result.StatusMessage =
                    "No candidate point satisfied polygon containment and wall-clearance rules.";
                return result;
            }

            result.Points = generated;
            result.Status = SprinklerPlacementStatus.Success;
            result.StatusMessage =
                "Generated " + generated.Count + " generic sprinkler point(s). "
                + "Values are provisional and do not represent final NFPA design.";

            return result;
        }

        // -------------------------------------------------------------------
        // Grid generation
        // -------------------------------------------------------------------

        private List<SprinklerPlacementPoint> GenerateGridPoints(
            List<double[]> polygon,
            SprinklerPlacementRules rules,
            RoomData room,
            LevelData level)
        {
            List<SprinklerPlacementPoint> accepted = new List<SprinklerPlacementPoint>();

            // Bounding rectangle of the polygon.
            double minX, minY, maxX, maxY;
            ComputeBoundingBox(polygon, out minX, out minY, out maxX, out maxY);

            double width = maxX - minX;
            double height = maxY - minY;

            if (width <= GeometryToleranceFt || height <= GeometryToleranceFt)
            {
                return accepted; // Degenerate polygon.
            }

            double spacing = rules.MaxSprinklerSpacingFt;

            // First-row inset: use MaxWallDistanceFt so the first row sits within
            // reach of the walls. Clamp to half the spacing to keep the grid
            // symmetric and centered when the room is small.
            double halfSpacing = spacing * 0.5;
            double inset = rules.MaxWallDistanceFt > 0.0
                ? Math.Min(rules.MaxWallDistanceFt, halfSpacing)
                : halfSpacing;

            // Compute how many grid steps fit, centering the grid inside the box.
            int cols = ComputeCount(width, inset, spacing);
            int rows = ComputeCount(height, inset, spacing);

            double xStart = ComputeStart(minX, width, cols, inset, spacing);
            double yStart = ComputeStart(minY, height, rows, inset, spacing);

            // Precompute Z once per room.
            double z = ComputeSprinklerZ(level, room, rules);

            double minWall = rules.MinWallDistanceFt > 0.0
                ? rules.MinWallDistanceFt
                : 0.0;

            double minSpacingSq = rules.MinSprinklerSpacingFt > 0.0
                ? rules.MinSprinklerSpacingFt * rules.MinSprinklerSpacingFt
                : 0.0;

            // Deterministic traversal: rows outer (Y ascending), cols inner (X ascending).
            for (int j = 0; j < rows; j++)
            {
                double y = yStart + j * spacing;

                for (int i = 0; i < cols; i++)
                {
                    double x = xStart + i * spacing;

                    // Must be strictly inside the polygon.
                    if (!IsPointInsidePolygon(polygon, x, y))
                        continue;

                    // Must not be too close to any wall (generic min-wall rule).
                    if (minWall > 0.0)
                    {
                        double distToBoundary = DistanceToPolygonBoundary(polygon, x, y);
                        if (distToBoundary < minWall) continue;
                    }

                    // Must not be too close to an already accepted point (generic min-spacing).
                    if (minSpacingSq > 0.0 && IsTooCloseToExisting(accepted, x, y, minSpacingSq))
                        continue;

                    accepted.Add(new SprinklerPlacementPoint(
                        x, y, z,
                        room.RoomId,
                        level.LevelId));
                }
            }

            // Fallback: if the grid produced nothing but the polygon is otherwise valid,
            // try the polygon centroid as a single interior point (still validated).
            if (accepted.Count == 0)
            {
                double cx, cy;
                ComputeCentroid(polygon, out cx, out cy);

                if (IsPointInsidePolygon(polygon, cx, cy))
                {
                    bool centroidOk = true;

                    if (minWall > 0.0)
                    {
                        double d = DistanceToPolygonBoundary(polygon, cx, cy);
                        if (d < minWall) centroidOk = false;
                    }

                    if (centroidOk)
                    {
                        accepted.Add(new SprinklerPlacementPoint(
                            cx, cy, z,
                            room.RoomId,
                            level.LevelId));
                    }
                }
            }

            return accepted;
        }

        private static int ComputeCount(double length, double inset, double spacing)
        {
            // Usable span after taking off the inset on each side.
            double usable = length - 2.0 * inset;
            if (usable < 0.0) return 1; // Very small room: try one point at the center.
            int steps = (int)Math.Floor(usable / spacing + GeometryToleranceFt);
            return steps + 1; // number of grid nodes along this axis
        }

        private static double ComputeStart(
            double minCoord,
            double length,
            int count,
            double inset,
            double spacing)
        {
            if (count <= 1)
            {
                // Center a single point.
                return minCoord + length * 0.5;
            }

            double totalSpan = (count - 1) * spacing;
            // Center the grid within the usable region defined by the inset.
            double leftover = length - totalSpan;
            double start = minCoord + leftover * 0.5;

            // Never start closer to the edge than the inset.
            if (start < minCoord + inset) start = minCoord + inset;

            return start;
        }

        private static double ComputeSprinklerZ(
            LevelData level,
            RoomData room,
            SprinklerPlacementRules rules)
        {
            double baseZ = level.ElevationFt;

            double? ceiling = room.Geometry?.CeilingHeightFt;
            if (ceiling.HasValue && ceiling.Value > 0.0)
            {
                double offset = rules.MountingOffsetFromCeilingFt;
                if (offset < 0.0) offset = 0.0;

                double z = baseZ + ceiling.Value - offset;
                return z;
            }

            return baseZ;
        }

        private static bool IsTooCloseToExisting(
            List<SprinklerPlacementPoint> accepted,
            double x,
            double y,
            double minSpacingSq)
        {
            for (int i = 0; i < accepted.Count; i++)
            {
                double dx = accepted[i].X - x;
                double dy = accepted[i].Y - y;
                if (dx * dx + dy * dy < minSpacingSq) return true;
            }
            return false;
        }

        // -------------------------------------------------------------------
        // Polygon helpers
        // -------------------------------------------------------------------

        private static List<double[]> BuildCleanPolygon(RoomData room)
        {
            if (room.Geometry == null) return null;
            if (room.Geometry.Polygon == null) return null;

            List<double[]> cleaned = new List<double[]>();

            foreach (double[] v in room.Geometry.Polygon)
            {
                if (v == null || v.Length < 2) continue;

                double x = v[0];
                double y = v[1];

                // Drop consecutive duplicates.
                if (cleaned.Count > 0)
                {
                    double[] last = cleaned[cleaned.Count - 1];
                    if (Math.Abs(last[0] - x) < GeometryToleranceFt &&
                        Math.Abs(last[1] - y) < GeometryToleranceFt)
                    {
                        continue;
                    }
                }

                cleaned.Add(new double[] { x, y });
            }

            // Drop closing duplicate if the polygon is explicitly closed.
            if (cleaned.Count >= 2)
            {
                double[] first = cleaned[0];
                double[] last = cleaned[cleaned.Count - 1];
                if (Math.Abs(first[0] - last[0]) < GeometryToleranceFt &&
                    Math.Abs(first[1] - last[1]) < GeometryToleranceFt)
                {
                    cleaned.RemoveAt(cleaned.Count - 1);
                }
            }

            return cleaned.Count >= 3 ? cleaned : null;
        }

        private static void ComputeBoundingBox(
            List<double[]> polygon,
            out double minX, out double minY,
            out double maxX, out double maxY)
        {
            minX = double.MaxValue;
            minY = double.MaxValue;
            maxX = double.MinValue;
            maxY = double.MinValue;

            for (int i = 0; i < polygon.Count; i++)
            {
                double x = polygon[i][0];
                double y = polygon[i][1];

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        /// <summary>
        /// Standard ray-casting point-in-polygon test.
        /// Points exactly on the boundary are treated as OUTSIDE, which
        /// matches the requirement that boundary points are not preferred.
        /// </summary>
        private static bool IsPointInsidePolygon(List<double[]> polygon, double x, double y)
        {
            bool inside = false;
            int n = polygon.Count;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = polygon[i][0];
                double yi = polygon[i][1];
                double xj = polygon[j][0];
                double yj = polygon[j][1];

                bool intersect =
                    ((yi > y) != (yj > y)) &&
                    (x < (xj - xi) * (y - yi) / ((yj - yi) == 0 ? GeometryToleranceFt : (yj - yi)) + xi);

                if (intersect) inside = !inside;
            }

            return inside;
        }

        /// <summary>
        /// Minimum Euclidean distance from a point to the polygon boundary
        /// (treating the polygon as a closed set of edges).
        /// </summary>
        private static double DistanceToPolygonBoundary(
            List<double[]> polygon,
            double x,
            double y)
        {
            double best = double.MaxValue;
            int n = polygon.Count;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double d = DistancePointToSegment(
                    x, y,
                    polygon[j][0], polygon[j][1],
                    polygon[i][0], polygon[i][1]);

                if (d < best) best = d;
            }

            return best;
        }

        private static double DistancePointToSegment(
            double px, double py,
            double ax, double ay,
            double bx, double by)
        {
            double dx = bx - ax;
            double dy = by - ay;

            double lengthSq = dx * dx + dy * dy;
            if (lengthSq <= GeometryToleranceFt)
            {
                // Degenerate segment: distance to point A.
                double ex = px - ax;
                double ey = py - ay;
                return Math.Sqrt(ex * ex + ey * ey);
            }

            double t = ((px - ax) * dx + (py - ay) * dy) / lengthSq;
            if (t < 0.0) t = 0.0;
            else if (t > 1.0) t = 1.0;

            double projX = ax + t * dx;
            double projY = ay + t * dy;

            double rx = px - projX;
            double ry = py - projY;
            return Math.Sqrt(rx * rx + ry * ry);
        }

        private static void ComputeCentroid(
            List<double[]> polygon,
            out double cx,
            out double cy)
        {
            // Area-weighted centroid using the shoelace formula.
            double area2 = 0.0;
            double xSum = 0.0;
            double ySum = 0.0;

            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = polygon[i][0];
                double yi = polygon[i][1];
                double xj = polygon[j][0];
                double yj = polygon[j][1];

                double cross = xj * yi - xi * yj;
                area2 += cross;
                xSum += (xj + xi) * cross;
                ySum += (yj + yi) * cross;
            }

            if (Math.Abs(area2) < GeometryToleranceFt)
            {
                // Degenerate: fall back to arithmetic mean.
                double sx = 0.0, sy = 0.0;
                for (int i = 0; i < n; i++)
                {
                    sx += polygon[i][0];
                    sy += polygon[i][1];
                }
                cx = sx / n;
                cy = sy / n;
                return;
            }

            double factor = 1.0 / (3.0 * area2);
            cx = xSum * factor;
            cy = ySum * factor;
        }
    }
}