using System;
using System.Collections.Generic;
using System.Linq;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Placement.SmokeDetectors.Final;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.Backend.Services.Placement.LocationPoints;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce;
using FireProtection.UI.Models.Sprinklers.BruteForce;

namespace FireProtection.Backend.Services.Placement.SmokeDetectors.Final.BruteForce
{
    /// <summary>
    /// Pure, Revit-free, production-grade NFPA 72 (Chapter 17) Smoke Detector Calculation Engine.
    /// 
    /// Key Principles:
    /// 1. NFPA 72 Spacing Rules:
    ///    - Nominal maximum spacing S = 30 ft (smooth/level ceilings).
    ///    - Wall distance &lt;= S / 2 (15 ft) measured at right angles.
    ///    - Radial coverage to all points &lt;= 0.7 * S (21.213 ft).
    ///    - Minimum clearance from wall face: 4 inches (0.333 ft) for ceiling mount; wall mount 4 to 12 inches below ceiling.
    ///    - High airflow (ACH &gt; 7.5): Area reduction per Table 17.7.6.3.3.2.
    /// 2. Grid &amp; Tile-Centric Alignment:
    ///    - If the room ceiling has a readable acoustic tile grid (or user-specified U x V fallback),
    ///      candidates are placed at whole-tile centers using balanced X-2X-X layout solver.
    ///    - Otherwise, candidates follow centered orthogonal grid spacing S &lt;= 30 ft.
    /// 3. Obstacle Handling (Non-destructive):
    ///    - Obstacles (beams, ducts, columns) are NOT discarded inside the calculation engine.
    ///    - Instead, any candidate point clashing with an obstacle at placement elevation is flagged
    ///      with HasObstacleConflict = true and described for audit/user confirmation.
    /// 4. Zero impact on Sprinklers:
    ///    - Completely self-contained within the SmokeDetectors namespace.
    /// </summary>
    public static class Nfpa72SmokeDetectorEngine
    {
        public static SmokeDetectorCalculationResult Calculate(
            SmokeDetectorPlacementInputSnapshot snapshot,
            ISmokeDetectorPlacementRules rules,
            BruteForceCalculationConfig config)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (config == null) throw new ArgumentNullException(nameof(config));

            var result = new SmokeDetectorCalculationResult
            {
                IsProvisional = !rules.HasApprovedRules,
                AppliedRulesSummary = rules.HasApprovedRules
                    ? "NFPA 72 Chapter 17 standard smoke detector placement rules."
                    : "NFPA 72 Chapter 17 prescriptive design basis (spot-type smoke detectors)."
            };

            if (snapshot.Rooms == null || snapshot.Rooms.Count == 0)
            {
                result.Success = false;
                result.Errors.Add("No rooms supplied to smoke detector calculation.");
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

        private static SmokeDetectorRoomCalculationResult CalculateRoom(
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
                Polygon = room.BoundaryPolygon ?? new List<double[]>()
            };

            if (result.Polygon == null || result.Polygon.Count < 3)
            {
                result.Status = CalculationStatus.InvalidRoomGeometry;
                result.Errors.Add("Room boundary is missing or degenerate (at least 3 vertices required).");
                return result;
            }

            // Resolve placement rules
            SmokeDetectorPlacementRuleSet ruleSet = rules.GetRules(
                room.DetectorType,
                room.Mount,
                room.CeilingSlope,
                room.AirChangesPerHour);

            if (room.OverrideMaxSpacingFt.HasValue && room.OverrideMaxSpacingFt.Value > 0)
            {
                ruleSet.MaxSpacingFt = room.OverrideMaxSpacingFt.Value;
                ruleSet.CoverageRadiusFt = Math.Round(ruleSet.MaxSpacingFt / Math.Sqrt(2.0), 3);
            }
            if (room.OverrideMaxDistanceToWallFt.HasValue && room.OverrideMaxDistanceToWallFt.Value > 0)
            {
                ruleSet.MaxDistanceFromWallsFt = room.OverrideMaxDistanceToWallFt.Value;
            }
            if (room.OverrideBoundaryClearanceFt.HasValue && room.OverrideBoundaryClearanceFt.Value > 0)
            {
                ruleSet.MinBoundaryClearanceFt = room.OverrideBoundaryClearanceFt.Value;
            }

            // Room Geometry
            List<List<double[]>> innerLoops = new List<List<double[]>>();
            if (room.Boundary?.InnerLoops != null)
            {
                foreach (var loop in room.Boundary.InnerLoops)
                {
                    if (loop?.Polygon != null && loop.Polygon.Count >= 3)
                        innerLoops.Add(loop.Polygon);
                }
            }

            RoomGeometry geometry = new RoomGeometry(result.Polygon, innerLoops);

            double width = geometry.MaxX - geometry.MinX;
            double height = geometry.MaxY - geometry.MinY;

            if (geometry.IsDegenerate || width <= 0 || height <= 0)
            {
                result.Status = CalculationStatus.InvalidRoomGeometry;
                result.Errors.Add("Room bounding envelope is degenerate.");
                return result;
            }

            // Placement Elevation
            double placementZ = ResolvePlacementZ(room, ruleSet, result);

            // Obstacles
            List<ObstacleBox> obstacleBoxes = BuildObstacleBoxes(room.Obstacles);

            // Ceiling Grid resolution
            CeilingGridMath.CeilingGrid tileGrid = ResolveCeilingGrid(room);

            // Check if Wall Mount requested
            bool isWallMount = string.Equals(ruleSet.Mount, "Wall", StringComparison.OrdinalIgnoreCase)
                || room.SelectedPlacementBehavior == DevicePlacementBehavior.WallSidewall;

            List<CalculatedSmokeDetectorPoint> selectedPoints;

            if (isWallMount)
            {
                selectedPoints = LayoutWallMount(room, geometry, ruleSet, placementZ, obstacleBoxes, config, result);
            }
            else if (tileGrid.IsValid)
            {
                selectedPoints = LayoutTileCentric(room, geometry, ruleSet, tileGrid, placementZ, obstacleBoxes, config, result);
            }
            else
            {
                selectedPoints = LayoutCenteredGrid(room, geometry, ruleSet, placementZ, obstacleBoxes, config, result);
            }

            if (selectedPoints == null || selectedPoints.Count == 0)
            {
                result.Status = CalculationStatus.NoValidCandidates;
                result.Warnings.Add("No smoke detector locations could be identified for this room.");
                return result;
            }

            // Assign Level metadata
            foreach (var pt in selectedPoints)
            {
                pt.RoomId = room.RoomId;
                pt.LevelId = room.LevelId;
                pt.LevelName = room.LevelName;
                result.Points.Add(pt);
            }

            // Perform post-selection NFPA 72 compliance checks
            VerifyNfpa72Coverage(geometry, result.Points, ruleSet, config, result);

            result.CalculatedCount = result.Points.Count;
            result.AppliedMaxSpacingFt = ruleSet.MaxSpacingFt;
            result.AppliedBoundaryClearanceFt = ruleSet.MinBoundaryClearanceFt;
            result.Status = result.Warnings.Count > 0 ? CalculationStatus.ReviewRequired : CalculationStatus.Success;

            return result;
        }

        private static double ResolvePlacementZ(
            SmokeDetectorRoomInput room,
            SmokeDetectorPlacementRuleSet rules,
            SmokeDetectorRoomCalculationResult result)
        {
            double levelElev = room.LevelElevationFt;
            double ceilingH = room.CeilingHeightFt.GetValueOrDefault(9.0);

            if (room.Ceilings != null && room.Ceilings.Count > 0)
            {
                CeilingData c = room.Ceilings[0];
                if (c.BottomElevationFt.HasValue && c.BottomElevationFt.Value > 0)
                {
                    ceilingH = c.BottomElevationFt.Value - levelElev;
                }
            }

            double z = levelElev + ceilingH;

            // Spot-type ceiling detectors mount flush to ceiling plane
            // Wall-mounted detectors mount between 4 in and 12 in below ceiling
            if (string.Equals(rules.Mount, "Wall", StringComparison.OrdinalIgnoreCase))
            {
                z -= rules.WallMountDropFromCeilingFt; // typically 0.5 ft (6 in)
            }

            return Math.Round(z, 4);
        }

        private static List<ObstacleBox> BuildObstacleBoxes(List<ObstacleData> obstacles)
        {
            var list = new List<ObstacleBox>();
            if (obstacles == null) return list;

            foreach (var obs in obstacles)
            {
                if (obs == null || obs.BoundingBox == null || obs.BoundingBox.Min == null || obs.BoundingBox.Max == null)
                    continue;

                double cl = 0.5; // default keep-away clearance
                string cat = (obs.Category ?? string.Empty).ToLowerInvariant();
                if (cat.Contains("duct") || cat.Contains("terminal")) cl = 1.0;
                else if (cat.Contains("beam")) cl = 0.5;

                list.Add(new ObstacleBox
                {
                    Category = obs.Category,
                    MinX = obs.BoundingBox.Min.X,
                    MinY = obs.BoundingBox.Min.Y,
                    MaxX = obs.BoundingBox.Max.X,
                    MaxY = obs.BoundingBox.Max.Y,
                    MinZ = obs.BoundingBox.Min.Z,
                    MaxZ = obs.BoundingBox.Max.Z,
                    ClearanceFt = cl
                });
            }

            return list;
        }

        private static CeilingGridMath.CeilingGrid ResolveCeilingGrid(SmokeDetectorRoomInput room)
        {
            CeilingData gridCeiling = room.Ceilings?.FirstOrDefault(c => c != null && c.HasReadableGrid);
            if (gridCeiling != null)
            {
                return new CeilingGridMath.CeilingGrid
                {
                    IsValid = true,
                    OriginX = gridCeiling.GridOriginXFt,
                    OriginY = gridCeiling.GridOriginYFt,
                    UFt = gridCeiling.GridSpacingUFt,
                    VFt = gridCeiling.GridSpacingVFt,
                    AngleRad = gridCeiling.GridAngleRad
                };
            }

            // Fallback from room-level tile size inputs if provided
            if (room.OverrideCeilingTileUFt.HasValue && room.OverrideCeilingTileUFt.Value > 0 &&
                room.OverrideCeilingTileVFt.HasValue && room.OverrideCeilingTileVFt.Value > 0)
            {
                return new CeilingGridMath.CeilingGrid
                {
                    IsValid = true,
                    OriginX = 0,
                    OriginY = 0,
                    UFt = room.OverrideCeilingTileUFt.Value,
                    VFt = room.OverrideCeilingTileVFt.Value,
                    AngleRad = 0
                };
            }

            return default;
        }

        #region Layout Algorithms

        /// <summary>
        /// Smooth ceiling centered orthogonal grid solver.
        /// Spacing S &lt;= MaxSpacingFt (30 ft), distance to walls &lt;= S/2 (15 ft).
        /// </summary>
        private static List<CalculatedSmokeDetectorPoint> LayoutCenteredGrid(
            SmokeDetectorRoomInput room,
            RoomGeometry geometry,
            SmokeDetectorPlacementRuleSet ruleSet,
            double placementZ,
            List<ObstacleBox> obstacleBoxes,
            BruteForceCalculationConfig config,
            SmokeDetectorRoomCalculationResult result)
        {
            double w = geometry.MaxX - geometry.MinX;
            double h = geometry.MaxY - geometry.MinY;

            double maxS = ruleSet.MaxSpacingFt;
            double maxWall = Math.Min(ruleSet.MaxDistanceFromWallsFt, maxS / 2.0);

            // Compute number of rows and columns needed to satisfy maxS and maxWall
            int nx = Math.Max(1, (int)Math.Ceiling(w / maxS));
            if (w / (2.0 * nx) > maxWall) nx = Math.Max(nx, (int)Math.Ceiling(w / (2.0 * maxWall)));

            int ny = Math.Max(1, (int)Math.Ceiling(h / maxS));
            if (h / (2.0 * ny) > maxWall) ny = Math.Max(ny, (int)Math.Ceiling(h / (2.0 * maxWall)));

            double stepX = w / nx;
            double stepY = h / ny;
            double startX = geometry.MinX + stepX / 2.0;
            double startY = geometry.MinY + stepY / 2.0;

            var points = new List<CalculatedSmokeDetectorPoint>();

            for (int iy = 0; iy < ny; iy++)
            {
                double y = startY + iy * stepY;
                for (int ix = 0; ix < nx; ix++)
                {
                    double x = startX + ix * stepX;

                    // If point falls outside or too close to wall, snap to nearest valid interior position
                    if (!geometry.IsPointInsideRoom(x, y, config.ToleranceFt) ||
                        geometry.DistanceToOuterBoundary(x, y) < ruleSet.MinBoundaryClearanceFt - config.ToleranceFt)
                    {
                        if (!TrySnapToValidInterior(x, y, geometry, ruleSet.MinBoundaryClearanceFt, config, out double sx, out double sy))
                        {
                            continue;
                        }
                        x = sx;
                        y = sy;
                    }

                    var pt = new CalculatedSmokeDetectorPoint
                    {
                        X = Math.Round(x, 4),
                        Y = Math.Round(y, 4),
                        Z = placementZ,
                        Mount = "Ceiling"
                    };

                    CheckObstacleClash(pt, obstacleBoxes, config);
                    points.Add(pt);
                }
            }

            // Fallback for irregular, L-shaped or corridor rooms:
            // If the regular grid points all landed outside the room (or in walls), find the deepest
            // interior point (pole of inaccessibility) to guarantee proper placement coverage.
            if (points.Count == 0)
            {
                double bestDist = -1.0;
                double bestX = 0, bestY = 0;
                double scanStep = Math.Max(0.5, Math.Min(w, h) / 20.0);
                for (double sy = geometry.MinY + scanStep; sy < geometry.MaxY; sy += scanStep)
                {
                    for (double sx = geometry.MinX + scanStep; sx < geometry.MaxX; sx += scanStep)
                    {
                        if (!geometry.IsPointInsideRoom(sx, sy, config.ToleranceFt)) continue;
                        double d = geometry.DistanceToOuterBoundary(sx, sy);
                        if (d > bestDist)
                        {
                            bestDist = d;
                            bestX = sx;
                            bestY = sy;
                        }
                    }
                }

                if (bestDist >= 0)
                {
                    var fallbackPt = new CalculatedSmokeDetectorPoint
                    {
                        X = Math.Round(bestX, 4),
                        Y = Math.Round(bestY, 4),
                        Z = placementZ,
                        Mount = "Ceiling"
                    };
                    CheckObstacleClash(fallbackPt, obstacleBoxes, config);
                    points.Add(fallbackPt);
                    result.Diagnostics.Add(string.Format(
                        "Irregular room geometric fallback: placed detector at deepest interior point ({0:F2}, {1:F2}) with {2:F2} ft wall clearance.",
                        bestX, bestY, bestDist));
                }
            }

            result.Diagnostics.Add(string.Format(
                "Centered Grid Layout: {0}x{1} heads, step {2:F2}x{3:F2} ft (wall dist {4:F2}x{5:F2} ft). Total={6}.",
                nx, ny, stepX, stepY, stepX / 2.0, stepY / 2.0, points.Count));

            return points;
        }

        /// <summary>
        /// Tile-centric layout solver for smoke detectors:
        /// Smoke detectors follow NFPA 72 maximum coverage rules (MaxSpacing = 30 ft, MaxWall = 15 ft).
        /// First, derive the minimal orthogonal centered point count needed by NFPA 72 (e.g. 1 point for a 24x19 room,
        /// 2 points for a 49x19 room). Then snap those optimal centered points onto the nearest valid tile centers.
        /// </summary>
        private static List<CalculatedSmokeDetectorPoint> LayoutTileCentric(
            SmokeDetectorRoomInput room,
            RoomGeometry geometry,
            SmokeDetectorPlacementRuleSet ruleSet,
            CeilingGridMath.CeilingGrid tileGrid,
            double placementZ,
            List<ObstacleBox> obstacleBoxes,
            BruteForceCalculationConfig config,
            SmokeDetectorRoomCalculationResult result)
        {
            // 1. Solve optimal orthogonal centered points according to NFPA 72 spacing limits
            List<CalculatedSmokeDetectorPoint> centered = LayoutCenteredGrid(
                room, geometry, ruleSet, placementZ, obstacleBoxes, config, result);

            if (centered == null || centered.Count == 0)
            {
                return centered;
            }

            // 2. Snap each NFPA 72 centered point onto the nearest ceiling tile center
            var snappedPoints = new List<CalculatedSmokeDetectorPoint>();
            foreach (var pt in centered)
            {
                CeilingGridMath.SnapToTileCenter(pt.X, pt.Y, in tileGrid, out double tx, out double ty);

                // If the snapped tile center is inside the room and respects boundary clearance, use it
                if (geometry.IsPointInsideRoom(tx, ty, config.ToleranceFt) &&
                    geometry.DistanceToOuterBoundary(tx, ty) >= ruleSet.MinBoundaryClearanceFt - config.ToleranceFt)
                {
                    var snappedPt = new CalculatedSmokeDetectorPoint
                    {
                        X = Math.Round(tx, 4),
                        Y = Math.Round(ty, 4),
                        Z = placementZ,
                        Mount = "Ceiling"
                    };
                    CheckObstacleClash(snappedPt, obstacleBoxes, config);
                    snappedPoints.Add(snappedPt);
                }
                else
                {
                    // If snapped position clipped outside boundary, retain the valid centered point
                    snappedPoints.Add(pt);
                }
            }

            result.Diagnostics.Add(string.Format(
                "Tile-Centric Layout: Snapped {0} optimal NFPA 72 point(s) to tile grid ({1:F1}x{2:F1} ft module).",
                snappedPoints.Count, tileGrid.UFt, tileGrid.VFt));

            return snappedPoints;
        }

        /// <summary>
        /// Wall-mount layout along room perimeter (for corridors or wall-mounted detectors).
        /// </summary>
        private static List<CalculatedSmokeDetectorPoint> LayoutWallMount(
            SmokeDetectorRoomInput room,
            RoomGeometry geometry,
            SmokeDetectorPlacementRuleSet ruleSet,
            double placementZ,
            List<ObstacleBox> obstacleBoxes,
            BruteForceCalculationConfig config,
            SmokeDetectorRoomCalculationResult result)
        {
            var points = new List<CalculatedSmokeDetectorPoint>();
            double maxS = ruleSet.MaxSpacingFt;
            double standoff = 0.25; // 3 inches off wall surface

            var poly = geometry.OuterPolygon;
            int n = poly.Count;

            for (int i = 0; i < n; i++)
            {
                double[] p1 = poly[i];
                double[] p2 = poly[(i + 1) % n];
                double len = GeometryMath.Distance(p1[0], p1[1], p2[0], p2[1]);
                if (len < 5.0) continue; // skip tiny segments

                int count = Math.Max(1, (int)Math.Round(len / maxS));
                double step = len / count;
                double dx = (p2[0] - p1[0]) / len;
                double dy = (p2[1] - p1[1]) / len;
                // Inward normal
                double nx = -dy;
                double ny = dx;

                for (int k = 0; k < count; k++)
                {
                    double dist = (k + 0.5) * step;
                    double x = p1[0] + dx * dist + nx * standoff;
                    double y = p1[1] + dy * dist + ny * standoff;

                    if (!geometry.IsPointInsideRoom(x, y, config.ToleranceFt))
                    {
                        x = p1[0] + dx * dist - nx * standoff;
                        y = p1[1] + dy * dist - ny * standoff;
                    }

                    var pt = new CalculatedSmokeDetectorPoint
                    {
                        X = Math.Round(x, 4),
                        Y = Math.Round(y, 4),
                        Z = placementZ,
                        Mount = "Wall",
                        WallEdgeIndex = i
                    };

                    CheckObstacleClash(pt, obstacleBoxes, config);
                    points.Add(pt);
                }
            }

            result.Diagnostics.Add(string.Format("Wall-Mount Layout: {0} detectors along perimeter.", points.Count));
            return points;
        }

        #endregion

        #region Helpers & Post-Checks

        private static void CheckObstacleClash(
            CalculatedSmokeDetectorPoint pt,
            List<ObstacleBox> obstacleBoxes,
            BruteForceCalculationConfig config)
        {
            if (obstacleBoxes == null || obstacleBoxes.Count == 0) return;

            foreach (var box in obstacleBoxes)
            {
                if (GeometryMath.InsideExpandedBox(pt.X, pt.Y, box.MinX, box.MinY, box.MaxX, box.MaxY, box.ClearanceFt))
                {
                    if (box.SpansZ(pt.Z, config.ToleranceFt))
                    {
                        pt.HasObstacleConflict = true;
                        pt.ObstacleConflictDescription = string.Format(
                            "{0} ({1:F2}, {2:F2} to {3:F2}, {4:F2})",
                            box.Category ?? "Obstacle", box.MinX, box.MinY, box.MaxX, box.MaxY);
                        return;
                    }
                }
            }
        }

        private static bool TrySnapToValidInterior(
            double x, double y,
            RoomGeometry geometry,
            double minClearance,
            BruteForceCalculationConfig config,
            out double bestX, out double bestY)
        {
            bestX = x;
            bestY = y;
            double step = 0.5; // search in 6-inch increments
            double maxR = Math.Max(12.0, Math.Max(geometry.MaxX - geometry.MinX, geometry.MaxY - geometry.MinY) / 2.0);

            // Look for candidates that satisfy minClearance, favoring points with the greatest boundary clearance
            // (e.g. corridor centerline) rather than clipping right at the minClearance edge.
            double bestClearance = -1.0;
            bool found = false;

            for (double r = step; r <= maxR; r += step)
            {
                for (int a = 0; a < 16; a++)
                {
                    double angle = a * Math.PI / 8.0;
                    double testX = x + r * Math.Cos(angle);
                    double testY = y + r * Math.Sin(angle);

                    if (geometry.IsPointInsideRoom(testX, testY, config.ToleranceFt))
                    {
                        double clearance = geometry.DistanceToOuterBoundary(testX, testY);
                        if (clearance >= minClearance - config.ToleranceFt)
                        {
                            if (clearance > bestClearance)
                            {
                                bestClearance = clearance;
                                bestX = testX;
                                bestY = testY;
                                found = true;
                            }
                        }
                    }
                }

                // If we found a candidate with generous clearance (at least 1.5 ft from all walls), we can accept it
                if (found && bestClearance >= Math.Max(minClearance, 1.5))
                {
                    return true;
                }
            }

            return found;
        }

        private static void VerifyNfpa72Coverage(
            RoomGeometry geometry,
            List<CalculatedSmokeDetectorPoint> points,
            SmokeDetectorPlacementRuleSet ruleSet,
            BruteForceCalculationConfig config,
            SmokeDetectorRoomCalculationResult result)
        {
            if (points == null || points.Count == 0) return;

            double maxRadius = ruleSet.CoverageRadiusFt; // 0.7 * S = 21.213 ft
            double maxWall = ruleSet.MaxDistanceFromWallsFt;

            // Check corner coverage (all points on ceiling must be within 0.7S of a detector)
            int uncoveredCorners = 0;
            foreach (double[] corner in geometry.OuterPolygon)
            {
                double d = points.Min(p => GeometryMath.Distance(corner[0], corner[1], p.X, p.Y));
                if (d > maxRadius + config.ToleranceFt)
                {
                    uncoveredCorners++;
                }
            }

            if (uncoveredCorners > 0)
            {
                result.Warnings.Add(string.Format(
                    "NFPA 72 Coverage advisory: {0} corner point(s) exceed maximum radial coverage 0.7S ({1:F2} ft).",
                    uncoveredCorners, maxRadius));
            }

            // Check distance from wall
            double maxDistFound = 0.0;
            foreach (var pt in points)
            {
                double distToWall = geometry.DistanceToOuterBoundary(pt.X, pt.Y);
                if (distToWall > maxDistFound) maxDistFound = distToWall;
            }

            if (maxDistFound > maxWall + config.ToleranceFt)
            {
                result.Warnings.Add(string.Format(
                    "NFPA 72 Spacing advisory: detector at max distance-to-wall {0:F2} ft from wall exceeds maximum S/2 ({1:F2} ft).",
                    maxDistFound, maxWall));
            }

            int conflictCount = points.Count(p => p.HasObstacleConflict);
            if (conflictCount > 0)
            {
                result.Warnings.Add(string.Format(
                    "Obstacle Notice: {0} candidate point(s) conflict with detected obstacles (user confirmation required).",
                    conflictCount));
            }
        }

        #endregion
    }
}
