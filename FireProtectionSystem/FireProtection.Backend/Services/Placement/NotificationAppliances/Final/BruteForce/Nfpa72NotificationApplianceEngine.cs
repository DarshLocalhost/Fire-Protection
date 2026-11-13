using System;
using System.Collections.Generic;
using System.Linq;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Placement.SmokeDetectors.Final;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.Backend.Services.Placement.LocationPoints;
using FireProtection.Backend.Services.Placement.SmokeDetectors.Final.BruteForce;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce;
using FireProtection.UI.Models.Sprinklers.BruteForce;

namespace FireProtection.Backend.Services.Placement.NotificationAppliances.Final.BruteForce
{
    /// <summary>
    /// Pure, Revit-free, production-grade NFPA 72 (Chapter 18) Notification Appliance Calculation Engine.
    ///
    /// Key Principles:
    /// 1. NFPA 72 Chapter 18 Compartment Rules:
    ///    - In standard rooms (<= 30 ft x 30 ft or <= 900 sq ft), a single notification appliance
    ///      provides full coverage per Table 18.5.5.4.1(a) (visible) and §18.4 (audible).
    ///    - In corridors (width <= 20 ft, aspect ratio >= 2.5:1), appliances are spaced along the
    ///      corridor at <= 100 ft apart with end offsets <= 15 ft per NFPA 72 §18.5.5.5.
    ///    - In large rooms (> 900 sq ft), appliances are distributed across 30 ft x 30 ft bays.
    /// 2. Mounting Heights & Wall Selection:
    ///    - Wall-mounted appliances are placed at 90 inches AFF (7.5 ft above finished floor),
    ///      within NFPA 72 §18.5.5.1's required 80 in to 96 in envelope.
    ///    - Wall placement targets the midpoint of the primary (longest) physical wall facing inward.
    ///    - Ceiling-mounted appliances attach at ceiling elevation near the room centroid.
    /// 3. Non-destructive Obstacle Handling:
    ///    - Obstacles are evaluated at placement elevation. Clashing heads receive HasObstacleConflict = true.
    /// </summary>
    public static class Nfpa72NotificationApplianceEngine
    {
        public const double WallMountAffFt = 7.5; // 90 inches AFF (NFPA 72 §18.5.5.1: 80-96 in)
        public const double DefaultCeilingHeightFt = 9.0;
        public const double CorridorMaxSpacingFt = 100.0; // NFPA 72 §18.5.5.5
        public const double CorridorMaxEndOffsetFt = 15.0; // NFPA 72 §18.5.5.5
        public const double StandardRoomMaxAreaSqFt = 900.0; // 30 ft x 30 ft
        public const double StandardRoomMaxDimFt = 30.0;
        public const double WallStandoffFt = 0.5;

        public static SmokeDetectorCalculationResult Calculate(
            SmokeDetectorPlacementInputSnapshot snapshot,
            ISmokeDetectorPlacementRules rules,
            BruteForceCalculationConfig config)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (config == null) throw new ArgumentNullException(nameof(config));

            var result = new SmokeDetectorCalculationResult
            {
                IsProvisional = false,
                AppliedRulesSummary = "NFPA 72 Chapter 18 standard notification appliance placement rules (§18.4 Audible & §18.5 Visible)."
            };

            if (snapshot.Rooms == null || snapshot.Rooms.Count == 0)
            {
                result.Success = false;
                result.Errors.Add("No rooms supplied to notification appliance calculation.");
                return result;
            }

            foreach (SmokeDetectorRoomInput room in snapshot.Rooms)
            {
                SmokeDetectorRoomCalculationResult roomResult;
                try
                {
                    roomResult = CalculateRoom(room, rules, config);
                }
                catch (Exception ex)
                {
                    roomResult = new SmokeDetectorRoomCalculationResult
                    {
                        RoomId = room?.RoomId,
                        RoomName = room?.RoomName,
                        RoomNumber = room?.RoomNumber,
                        FamilyName = room?.SelectedFamilyName,
                        TypeName = room?.SelectedTypeName,
                        Status = CalculationStatus.Failed
                    };
                    roomResult.Errors.Add("Calculation error: " + ex.Message);
                }

                result.Rooms.Add(roomResult);
                result.TotalCalculatedDetectors += roomResult.CalculatedCount;
                result.Warnings.AddRange(roomResult.Warnings);
                result.Errors.AddRange(roomResult.Errors);
            }

            result.Success = result.Rooms.All(r => r.IsSuccessful);
            return result;
        }

        public static SmokeDetectorRoomCalculationResult CalculateRoom(
            SmokeDetectorRoomInput room,
            ISmokeDetectorPlacementRules rules,
            BruteForceCalculationConfig config)
        {
            var result = new SmokeDetectorRoomCalculationResult
            {
                RoomId = room.RoomId,
                RoomName = room.RoomName,
                RoomNumber = room.RoomNumber,
                FamilyName = room.SelectedFamilyName,
                TypeName = room.SelectedTypeName,
                Status = CalculationStatus.Success
            };

            List<double[]> polygon = ExtractOuterPolygon(room);
            polygon = CleanPolygonLoop(polygon);
            polygon = BridgeObstacleNotches(polygon);

            if (polygon == null || polygon.Count < 3)
            {
                result.Status = CalculationStatus.InvalidRoomGeometry;
                result.Errors.Add("Room boundary is missing or degenerate (need at least 3 vertices).");
                return result;
            }

            result.Polygon = polygon;

            // Determine if device is wall-mounted or ceiling-mounted
            bool isWallMounted = IsWallMounted(room);
            double placementZ = ResolvePlacementZ(room, isWallMounted);

            // Compute room bounding envelope
            double minX = polygon.Min(p => p[0]);
            double maxX = polygon.Max(p => p[0]);
            double minY = polygon.Min(p => p[1]);
            double maxY = polygon.Max(p => p[1]);
            double widthFt = maxX - minX;
            double lengthFt = maxY - minY;
            double roomDimMin = Math.Min(widthFt, lengthFt);
            double roomDimMax = Math.Max(widthFt, lengthFt);

            bool isCorridor = roomDimMin <= 20.0 && (roomDimMax / Math.Max(roomDimMin, 1e-3)) >= 2.5;

            List<CalculatedSmokeDetectorPoint> candidatePoints = new List<CalculatedSmokeDetectorPoint>();

            if (isCorridor)
            {
                // Corridor placement per NFPA 72 §18.5.5.5
                candidatePoints = GenerateCorridorLayout(
                    room, polygon, isWallMounted, placementZ, minX, maxX, minY, maxY, widthFt, lengthFt, result);
            }
            else if (room.AreaSqFt <= StandardRoomMaxAreaSqFt && roomDimMax <= StandardRoomMaxDimFt + 2.0)
            {
                // Standard room compartment rule: 1 appliance per room per NFPA 72 Table 18.5.5.4.1(a)
                candidatePoints = GenerateSingleRoomAppliance(
                    room, polygon, isWallMounted, placementZ, minX, maxX, minY, maxY, result);
            }
            else
            {
                // Large room: distributed bay layout
                candidatePoints = GenerateLargeRoomLayout(
                    room, polygon, isWallMounted, placementZ, minX, maxX, minY, maxY, widthFt, lengthFt, result);
            }

            // Evaluate obstacle conflicts
            List<ObstacleBox> obstacleBoxes = BuildObstacleBoxes(room.Obstacles);
            foreach (CalculatedSmokeDetectorPoint pt in candidatePoints)
            {
                CheckObstacleConflict(pt, obstacleBoxes, config);
            }

            result.Points = candidatePoints;
            result.CalculatedCount = candidatePoints.Count;
            result.Status = candidatePoints.Count > 0 ? CalculationStatus.Success : CalculationStatus.NoValidCandidates;

            return result;
        }

        private static bool IsWallMounted(SmokeDetectorRoomInput room)
        {
            if (string.Equals(room.Mount, "Wall", StringComparison.OrdinalIgnoreCase)) return true;
            if (room.SelectedPlacementBehavior == DevicePlacementBehavior.WallSidewall) return true;
            string combined = (room.SelectedFamilyName ?? string.Empty) + " " + (room.SelectedTypeName ?? string.Empty);
            return combined.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static double ResolvePlacementZ(SmokeDetectorRoomInput room, bool isWallMounted)
        {
            if (isWallMounted)
            {
                // 90 inches AFF (7.5 ft)
                return room.LevelElevationFt + WallMountAffFt;
            }

            // Ceiling mount
            double ceilingH = room.CeilingHeightFt.HasValue && room.CeilingHeightFt.Value > 0
                ? room.CeilingHeightFt.Value
                : DefaultCeilingHeightFt;
            return room.LevelElevationFt + ceilingH;
        }

        private static List<CalculatedSmokeDetectorPoint> GenerateSingleRoomAppliance(
            SmokeDetectorRoomInput room,
            List<double[]> polygon,
            bool isWallMounted,
            double placementZ,
            double minX, double maxX, double minY, double maxY,
            SmokeDetectorRoomCalculationResult result)
        {
            List<CalculatedSmokeDetectorPoint> list = new List<CalculatedSmokeDetectorPoint>();

            if (isWallMounted)
            {
                // Find the primary (longest) physical wall edge
                int bestEdge = FindPrimaryWallEdge(polygon, room);
                if (bestEdge >= 0)
                {
                    double[] a = polygon[bestEdge];
                    double[] b = polygon[(bestEdge + 1) % polygon.Count];
                    double midX = (a[0] + b[0]) * 0.5;
                    double midY = (a[1] + b[1]) * 0.5;

                    GetEdgeInwardNormal(polygon, bestEdge, out double nx, out double ny);

                    double cx = midX + nx * WallStandoffFt;
                    double cy = midY + ny * WallStandoffFt;

                    list.Add(new CalculatedSmokeDetectorPoint
                    {
                        X = cx,
                        Y = cy,
                        Z = placementZ,
                        RoomId = room.RoomId,
                        LevelId = room.LevelId,
                        LevelName = room.LevelName,
                        Mount = "Wall",
                        WallEdgeIndex = bestEdge
                    });

                    result.Diagnostics.Add(
                        $"Single wall-mounted notification appliance placed on primary wall edge {bestEdge} at midpoint ({cx:F2}, {cy:F2}, Z={placementZ:F2} ft / 90 in AFF) per NFPA 72 Table 18.5.5.4.1(a).");
                    return list;
                }
            }

            // Ceiling mount or fallback: geometric centroid
            ComputeCentroid(polygon, out double centX, out double centY);
            list.Add(new CalculatedSmokeDetectorPoint
            {
                X = centX,
                Y = centY,
                Z = placementZ,
                RoomId = room.RoomId,
                LevelId = room.LevelId,
                LevelName = room.LevelName,
                Mount = "Ceiling",
                WallEdgeIndex = null
            });

            result.Diagnostics.Add(
                $"Single ceiling-mounted notification appliance placed at centroid ({centX:F2}, {centY:F2}, Z={placementZ:F2} ft) per NFPA 72 compartment rule.");
            return list;
        }

        private static List<CalculatedSmokeDetectorPoint> GenerateCorridorLayout(
            SmokeDetectorRoomInput room,
            List<double[]> polygon,
            bool isWallMounted,
            double placementZ,
            double minX, double maxX, double minY, double maxY,
            double widthFt, double lengthFt,
            SmokeDetectorRoomCalculationResult result)
        {
            List<CalculatedSmokeDetectorPoint> list = new List<CalculatedSmokeDetectorPoint>();

            bool runsEastWest = widthFt >= lengthFt;
            double corLength = runsEastWest ? widthFt : lengthFt;

            // Spacing along corridor <= 100 ft, end offset <= 15 ft per NFPA 72 §18.5.5.5
            int numAppliances = Math.Max(1, (int)Math.Ceiling(corLength / CorridorMaxSpacingFt));
            double step = corLength / numAppliances;
            double endOffset = Math.Min(CorridorMaxEndOffsetFt, step * 0.5);

            if (numAppliances > 1)
            {
                // Recompute step with fixed end offset
                step = (corLength - 2.0 * endOffset) / (numAppliances - 1);
            }

            int bestEdge = -1;
            if (isWallMounted)
            {
                bestEdge = FindLongestWallInDirection(polygon, runsEastWest);
            }

            for (int k = 0; k < numAppliances; k++)
            {
                double distAlong = (numAppliances == 1) ? corLength * 0.5 : endOffset + k * step;
                double px, py;

                if (runsEastWest)
                {
                    px = minX + distAlong;
                    if (isWallMounted && bestEdge >= 0)
                    {
                        double[] a = polygon[bestEdge];
                        GetEdgeInwardNormal(polygon, bestEdge, out double nx, out double ny);
                        py = a[1] + ny * WallStandoffFt;
                    }
                    else
                    {
                        py = (minY + maxY) * 0.5;
                    }
                }
                else
                {
                    py = minY + distAlong;
                    if (isWallMounted && bestEdge >= 0)
                    {
                        double[] a = polygon[bestEdge];
                        GetEdgeInwardNormal(polygon, bestEdge, out double nx, out double ny);
                        px = a[0] + nx * WallStandoffFt;
                    }
                    else
                    {
                        px = (minX + maxX) * 0.5;
                    }
                }

                list.Add(new CalculatedSmokeDetectorPoint
                {
                    X = px,
                    Y = py,
                    Z = placementZ,
                    RoomId = room.RoomId,
                    LevelId = room.LevelId,
                    LevelName = room.LevelName,
                    Mount = isWallMounted ? "Wall" : "Ceiling",
                    WallEdgeIndex = isWallMounted && bestEdge >= 0 ? bestEdge : (int?)null
                });
            }

            result.Diagnostics.Add(
                $"Corridor layout: {list.Count} appliance(s) spaced <= 100 ft with <= 15 ft end offsets per NFPA 72 §18.5.5.5.");
            return list;
        }

        private static List<CalculatedSmokeDetectorPoint> GenerateLargeRoomLayout(
            SmokeDetectorRoomInput room,
            List<double[]> polygon,
            bool isWallMounted,
            double placementZ,
            double minX, double maxX, double minY, double maxY,
            double widthFt, double lengthFt,
            SmokeDetectorRoomCalculationResult result)
        {
            List<CalculatedSmokeDetectorPoint> list = new List<CalculatedSmokeDetectorPoint>();

            int nx = Math.Max(1, (int)Math.Ceiling(widthFt / StandardRoomMaxDimFt));
            int ny = Math.Max(1, (int)Math.Ceiling(lengthFt / StandardRoomMaxDimFt));
            double stepX = widthFt / nx;
            double stepY = lengthFt / ny;

            if (isWallMounted)
            {
                // Perimeter wall distribution on primary opposing walls
                int bestEdge1 = FindPrimaryWallEdge(polygon, room);
                int bestEdge2 = FindOpposingWallEdge(polygon, bestEdge1);

                int headsPerWall = Math.Max(1, Math.Max(nx, ny));
                AddWallRow(list, polygon, bestEdge1, headsPerWall, placementZ, room);
                if (bestEdge2 >= 0)
                {
                    AddWallRow(list, polygon, bestEdge2, headsPerWall, placementZ, room);
                }

                result.Diagnostics.Add(
                    $"Large room wall layout: {list.Count} appliances distributed across opposing walls per NFPA 72 Table 18.5.5.4.1(a).");
                return list;
            }

            // Ceiling grid distribution
            for (int iy = 0; iy < ny; iy++)
            {
                double py = minY + (iy + 0.5) * stepY;
                for (int ix = 0; ix < nx; ix++)
                {
                    double px = minX + (ix + 0.5) * stepX;
                    if (GeometryMath.PointInPolygon(px, py, polygon, 0.1))
                    {
                        list.Add(new CalculatedSmokeDetectorPoint
                        {
                            X = px,
                            Y = py,
                            Z = placementZ,
                            RoomId = room.RoomId,
                            LevelId = room.LevelId,
                            LevelName = room.LevelName,
                            Mount = "Ceiling",
                            WallEdgeIndex = null
                        });
                    }
                }
            }

            result.Diagnostics.Add(
                $"Large room ceiling layout: {list.Count} appliances distributed on {StandardRoomMaxDimFt} ft grid per NFPA 72.");
            return list;
        }

        private static void AddWallRow(
            List<CalculatedSmokeDetectorPoint> list,
            List<double[]> polygon,
            int edgeIdx,
            int count,
            double placementZ,
            SmokeDetectorRoomInput room)
        {
            if (edgeIdx < 0 || edgeIdx >= polygon.Count) return;

            double[] a = polygon[edgeIdx];
            double[] b = polygon[(edgeIdx + 1) % polygon.Count];
            double ex = b[0] - a[0], ey = b[1] - a[1];
            double len = Math.Sqrt(ex * ex + ey * ey);
            if (len < 0.5) return;

            double ux = ex / len, uy = ey / len;
            GetEdgeInwardNormal(polygon, edgeIdx, out double nx, out double ny);

            double step = len / count;
            double endDist = step * 0.5;

            for (int k = 0; k < count; k++)
            {
                double d = endDist + k * step;
                double bx = a[0] + ux * d;
                double by = a[1] + uy * d;
                double cx = bx + nx * WallStandoffFt;
                double cy = by + ny * WallStandoffFt;

                list.Add(new CalculatedSmokeDetectorPoint
                {
                    X = cx,
                    Y = cy,
                    Z = placementZ,
                    RoomId = room.RoomId,
                    LevelId = room.LevelId,
                    LevelName = room.LevelName,
                    Mount = "Wall",
                    WallEdgeIndex = edgeIdx
                });
            }
        }

        private static int FindPrimaryWallEdge(List<double[]> polygon, SmokeDetectorRoomInput room)
        {
            if (polygon == null || polygon.Count < 3) return -1;

            int bestEdge = -1;
            double maxLen = -1.0;

            for (int i = 0; i < polygon.Count; i++)
            {
                double[] a = polygon[i];
                double[] b = polygon[(i + 1) % polygon.Count];
                double ex = b[0] - a[0], ey = b[1] - a[1];
                double len = Math.Sqrt(ex * ex + ey * ey);

                if (len > maxLen)
                {
                    maxLen = len;
                    bestEdge = i;
                }
            }

            return bestEdge;
        }

        private static int FindLongestWallInDirection(List<double[]> polygon, bool runsEastWest)
        {
            int bestEdge = -1;
            double maxLen = -1.0;

            for (int i = 0; i < polygon.Count; i++)
            {
                double[] a = polygon[i];
                double[] b = polygon[(i + 1) % polygon.Count];
                double ex = b[0] - a[0], ey = b[1] - a[1];
                double len = Math.Sqrt(ex * ex + ey * ey);
                if (len < 0.5) continue;

                double ux = Math.Abs(ex / len);
                double uy = Math.Abs(ey / len);

                // If corridor runs East-West, wall edges run mostly East-West (ux > 0.7)
                bool matchesDir = runsEastWest ? (ux >= 0.7) : (uy >= 0.7);
                if (matchesDir && len > maxLen)
                {
                    maxLen = len;
                    bestEdge = i;
                }
            }

            return bestEdge >= 0 ? bestEdge : FindPrimaryWallEdge(polygon, null);
        }

        private static int FindOpposingWallEdge(List<double[]> polygon, int primaryEdge)
        {
            if (primaryEdge < 0 || primaryEdge >= polygon.Count) return -1;

            double[] pA = polygon[primaryEdge];
            double[] pB = polygon[(primaryEdge + 1) % polygon.Count];
            double ex = pB[0] - pA[0], ey = pB[1] - pA[1];
            double len = Math.Sqrt(ex * ex + ey * ey);
            if (len < 1e-6) return -1;
            double u1x = ex / len, u1y = ey / len;

            GetEdgeInwardNormal(polygon, primaryEdge, out double inX, out double inY);

            int bestOpposite = -1;
            double bestScore = double.MinValue;

            for (int e = 0; e < polygon.Count; e++)
            {
                if (e == primaryEdge) continue;
                double[] cA = polygon[e];
                double[] cB = polygon[(e + 1) % polygon.Count];
                double cx = cB[0] - cA[0], cy = cB[1] - cA[1];
                double cLen = Math.Sqrt(cx * cx + cy * cy);
                if (cLen < 0.5) continue;

                double u2x = cx / cLen, u2y = cy / cLen;
                double dot = u1x * u2x + u1y * u2y;
                double antiParallelism = -dot;
                if (antiParallelism < 0.3) continue;

                double midCandX = (cA[0] + cB[0]) * 0.5;
                double midCandY = (cA[1] + cB[1]) * 0.5;
                double perpDist = (midCandX - pA[0]) * inX + (midCandY - pA[1]) * inY;

                double score = antiParallelism * 10.0 + perpDist;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestOpposite = e;
                }
            }

            return bestOpposite;
        }

        private static void GetEdgeInwardNormal(List<double[]> polygon, int edgeIdx, out double inX, out double inY)
        {
            double[] a = polygon[edgeIdx];
            double[] b = polygon[(edgeIdx + 1) % polygon.Count];
            double ex = b[0] - a[0], ey = b[1] - a[1];
            double len = Math.Sqrt(ex * ex + ey * ey);
            if (len < 1e-6) { inX = 0; inY = 1; return; }

            bool isCCW = ComputeSignedArea(polygon) > 0;
            double ux = ex / len, uy = ey / len;
            double nx = isCCW ? -uy : uy;
            double ny = isCCW ? ux : -ux;

            double midX = (a[0] + b[0]) * 0.5;
            double midY = (a[1] + b[1]) * 0.5;

            bool inward = false;
            foreach (double step in new[] { 0.1, 0.25, 0.5 })
            {
                if (GeometryMath.PointInPolygon(midX + nx * step, midY + ny * step, polygon, 0.05))
                {
                    inward = true;
                    break;
                }
            }
            if (!inward) { nx = -nx; ny = -ny; }

            inX = nx;
            inY = ny;
        }

        private static void ComputeCentroid(List<double[]> polygon, out double cx, out double cy)
        {
            double a = 0;
            double x = 0;
            double y = 0;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double f = polygon[j][0] * polygon[i][1] - polygon[i][0] * polygon[j][1];
                a += f;
                x += (polygon[j][0] + polygon[i][0]) * f;
                y += (polygon[j][1] + polygon[i][1]) * f;
            }
            if (Math.Abs(a) < 1e-9)
            {
                cx = polygon.Average(p => p[0]);
                cy = polygon.Average(p => p[1]);
                return;
            }
            a *= 0.5;
            cx = x / (6.0 * a);
            cy = y / (6.0 * a);
        }

        private static double ComputeSignedArea(List<double[]> polygon)
        {
            if (polygon == null || polygon.Count < 3) return 0;
            double area = 0;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                area += (polygon[j][0] * polygon[i][1]) - (polygon[i][0] * polygon[j][1]);
            }
            return area * 0.5;
        }

        private static List<double[]> CleanPolygonLoop(List<double[]> raw)
        {
            List<double[]> cleaned = new List<double[]>();
            if (raw == null) return cleaned;

            foreach (double[] pt in raw)
            {
                if (pt == null || pt.Length < 2) continue;
                if (cleaned.Count > 0)
                {
                    double[] prev = cleaned[cleaned.Count - 1];
                    if (Math.Abs(pt[0] - prev[0]) < 1e-6 && Math.Abs(pt[1] - prev[1]) < 1e-6)
                        continue;
                }
                cleaned.Add(new double[] { pt[0], pt[1] });
            }

            if (cleaned.Count >= 3)
            {
                double[] first = cleaned[0];
                double[] last = cleaned[cleaned.Count - 1];
                if (Math.Abs(first[0] - last[0]) < 1e-6 && Math.Abs(first[1] - last[1]) < 1e-6)
                    cleaned.RemoveAt(cleaned.Count - 1);
            }

            return MergeCollinearVertices(cleaned);
        }

        private static List<double[]> MergeCollinearVertices(List<double[]> polygon)
        {
            if (polygon == null || polygon.Count < 3) return polygon ?? new List<double[]>();

            List<double[]> result = new List<double[]>(polygon);
            bool modified = true;

            while (modified && result.Count >= 3)
            {
                modified = false;
                int n = result.Count;

                for (int i = 0; i < n; i++)
                {
                    double[] prev = result[(i + n - 1) % n];
                    double[] curr = result[i];
                    double[] next = result[(i + 1) % n];

                    double v1x = curr[0] - prev[0];
                    double v1y = curr[1] - prev[1];
                    double len1 = Math.Sqrt(v1x * v1x + v1y * v1y);

                    double v2x = next[0] - curr[0];
                    double v2y = next[1] - curr[1];
                    double len2 = Math.Sqrt(v2x * v2x + v2y * v2y);

                    if (len1 < 1e-6 || len2 < 1e-6) continue;

                    v1x /= len1; v1y /= len1;
                    v2x /= len2; v2y /= len2;

                    double cross = v1x * v2y - v1y * v2x;
                    double dot = v1x * v2x + v1y * v2y;

                    if (Math.Abs(cross) < 1e-3 && dot > 0.999)
                    {
                        result.RemoveAt(i);
                        modified = true;
                        break;
                    }
                }
            }

            return result;
        }

        private static double PerpDistanceToLine(double[] p, double[] a, double[] b)
        {
            double abx = b[0] - a[0];
            double aby = b[1] - a[1];
            double l = Math.Sqrt(abx * abx + aby * aby);
            if (l < 1e-9) return GeometryMath.Distance(p[0], p[1], a[0], a[1]);
            return Math.Abs(abx * (p[1] - a[1]) - aby * (p[0] - a[0])) / l;
        }

        private static List<double[]> CollapseCloseVertices(List<double[]> polygon, double minDist = 0.05)
        {
            if (polygon == null || polygon.Count < 3) return polygon ?? new List<double[]>();
            List<double[]> res = new List<double[]>();
            foreach (double[] p in polygon)
            {
                if (p == null || p.Length < 2) continue;
                if (res.Count == 0 || GeometryMath.Distance(res[res.Count - 1][0], res[res.Count - 1][1], p[0], p[1]) >= minDist)
                {
                    res.Add(p);
                }
            }
            if (res.Count > 1 && GeometryMath.Distance(res[0][0], res[0][1], res[res.Count - 1][0], res[res.Count - 1][1]) < minDist)
            {
                res.RemoveAt(res.Count - 1);
            }
            return res;
        }

        private static List<double[]> BridgeObstacleNotches(
            List<double[]> polygon,
            double maxDepth = 3.5,
            double maxSpan = 15.0,
            double tol = 0.1)
        {
            if (polygon == null || polygon.Count < 4) return polygon ?? new List<double[]>();

            List<double[]> current = CleanPolygonLoop(CollapseCloseVertices(polygon));
            bool changed = true;
            int iterations = 0;

            while (changed && iterations < 15)
            {
                iterations++;
                changed = false;
                int n = current.Count;
                if (n < 4) break;

                for (int i = 0; i < n; i++)
                {
                    double[] a = current[(i - 1 + n) % n];
                    double[] b = current[i];
                    double abLen = GeometryMath.Distance(a[0], a[1], b[0], b[1]);
                    if (abLen < 0.2) continue;

                    double ux = (b[0] - a[0]) / abLen;
                    double uy = (b[1] - a[1]) / abLen;

                    for (int step = 2; step < Math.Min(15, n); step++)
                    {
                        int cIdx = (i + step - 1) % n;
                        double[] c = current[cIdx];
                        if (PerpDistanceToLine(c, a, b) > tol) continue;

                        double span = (c[0] - b[0]) * ux + (c[1] - b[1]) * uy;
                        if (span <= 0.05 || span > maxSpan) continue;

                        bool valid = true;
                        bool hasJog = false;
                        for (int j = 1; j < step; j++)
                        {
                            double[] v = current[(i + j - 1) % n];
                            double dV = PerpDistanceToLine(v, a, b);
                            if (dV > maxDepth) { valid = false; break; }
                            if (dV > 0.05) hasJog = true;
                        }

                        if (valid && hasJog)
                        {
                            HashSet<int> dropIndices = new HashSet<int>();
                            for (int j = 1; j < step - 1; j++)
                            {
                                dropIndices.Add((i + j) % n);
                            }
                            List<double[]> newPoly = new List<double[]>();
                            for (int k = 0; k < n; k++)
                            {
                                if (!dropIndices.Contains(k)) newPoly.Add(current[k]);
                            }
                            current = CleanPolygonLoop(newPoly);
                            changed = true;
                            break;
                        }
                    }
                    if (changed) break;
                }
            }

            return CleanPolygonLoop(current);
        }

        private static List<double[]> ExtractOuterPolygon(SmokeDetectorRoomInput room)
        {
            if (room.BoundaryPolygon != null && room.BoundaryPolygon.Count >= 3)
            {
                return room.BoundaryPolygon;
            }
            return new List<double[]>();
        }

        private static List<ObstacleBox> BuildObstacleBoxes(IReadOnlyList<ObstacleData> obstacles)
        {
            var boxes = new List<ObstacleBox>();
            if (obstacles == null) return boxes;

            foreach (ObstacleData obs in obstacles)
            {
                if (obs?.BoundingBox?.Min == null || obs?.BoundingBox?.Max == null) continue;
                boxes.Add(new ObstacleBox
                {
                    MinX = Math.Min(obs.BoundingBox.Min.X, obs.BoundingBox.Max.X),
                    MinY = Math.Min(obs.BoundingBox.Min.Y, obs.BoundingBox.Max.Y),
                    MaxX = Math.Max(obs.BoundingBox.Min.X, obs.BoundingBox.Max.X),
                    MaxY = Math.Max(obs.BoundingBox.Min.Y, obs.BoundingBox.Max.Y),
                    MinZ = Math.Min(obs.BoundingBox.Min.Z, obs.BoundingBox.Max.Z),
                    MaxZ = Math.Max(obs.BoundingBox.Min.Z, obs.BoundingBox.Max.Z),
                    Category = obs.Category ?? "Obstacle"
                });
            }
            return boxes;
        }

        private static void CheckObstacleConflict(
            CalculatedSmokeDetectorPoint pt,
            List<ObstacleBox> obstacleBoxes,
            BruteForceCalculationConfig config)
        {
            if (pt == null || obstacleBoxes == null || obstacleBoxes.Count == 0) return;

            foreach (ObstacleBox box in obstacleBoxes)
            {
                if (pt.Z < box.MinZ - config.ToleranceFt || pt.Z > box.MaxZ + config.ToleranceFt)
                    continue;

                if (GeometryMath.InsideExpandedBox(pt.X, pt.Y, box.MinX, box.MinY, box.MaxX, box.MaxY, 0.0))
                {
                    pt.HasObstacleConflict = true;
                    pt.ObstacleConflictDescription = $"Direct collision with {box.Category}";
                    return;
                }
            }
        }
    }
}
