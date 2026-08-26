using System;
using System.Collections.Generic;
using System.Linq;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Hazard;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.UI.Models.Sprinklers.BruteForce;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce
{
    /// <summary>
    /// Deterministic, Revit-free BruteForce sprinkler calculation engine.
    /// Consumes the in-memory <see cref="PlacementInputSnapshot"/> (never a JSON file) and returns a
    /// <see cref="BruteForceCalculationResult"/>. Each room is calculated independently; one failing
    /// room is recorded and does not abort the others.
    /// </summary>
    public static class BruteForceCalculationService
    {
        public static BruteForceCalculationResult Calculate(
            PlacementInputSnapshot snapshot,
            IHazardPlacementRules rules,
            BruteForceCalculationConfig config)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (config == null) throw new ArgumentNullException(nameof(config));

            BruteForceCalculationResult result = new BruteForceCalculationResult
            {
                IsProvisional = !rules.HasApprovedRules
            };

            result.AppliedRulesSummary = rules.HasApprovedRules
                ? "Project-approved hazard placement rules."
                : "PROVISIONAL placeholder spacing (NFPA13-2022 approved values not yet supplied).";

            if (snapshot.Rooms == null || snapshot.Rooms.Count == 0)
            {
                result.Success = false;
                result.Errors.Add("No rooms supplied to the BruteForce calculation.");
                return result;
            }

            foreach (PlacementRoomInput room in snapshot.Rooms)
            {
                RoomCalculationResult roomResult;
                try
                {
                    roomResult = CalculateRoom(room, rules, config);
                }
                catch (Exception ex)
                {
                    roomResult = new RoomCalculationResult
                    {
                        RoomId = room?.RoomId,
                        RoomName = room?.RoomName,
                        RoomNumber = room?.RoomNumber,
                        Status = CalculationStatus.Failed
                    };
                    roomResult.Errors.Add("Unexpected calculation error: " + ex.Message);
                }

                result.Rooms.Add(roomResult);
                result.TotalCalculatedSprinklers += roomResult.CalculatedCount;
                result.Warnings.AddRange(roomResult.Warnings);
                result.Errors.AddRange(roomResult.Errors);
            }

            result.Success = result.Rooms.All(r => r.IsSuccessful);

            if (result.IsProvisional)
            {
                result.Warnings.Add(
                    "Calculation used PROVISIONAL spacing rules. Output is NOT NFPA13-2022 compliant; " +
                    "review required before any placement.");
            }

            return result;
        }

        private static RoomCalculationResult CalculateRoom(
            PlacementRoomInput room,
            IHazardPlacementRules rules,
            BruteForceCalculationConfig config)
        {
            RoomCalculationResult result = new RoomCalculationResult
            {
                RoomId = room.RoomId,
                RoomName = room.RoomName,
                RoomNumber = room.RoomNumber,
                Status = CalculationStatus.Success
            };

            List<double[]> outer = ExtractOuterPolygon(room);
            List<List<double[]>> innerLoops = ExtractInnerLoops(room);

            RoomGeometry geometry = new RoomGeometry(outer, innerLoops);
            if (geometry.IsDegenerate)
            {
                result.Status = CalculationStatus.InvalidRoomGeometry;
                result.Errors.Add("Room boundary is missing or degenerate (need at least 3 vertices).");
                return result;
            }

            // ---- Hazard ----
            HazardClass hazardClass = ParseHazardClass(room.EffectiveHazardClass, result);
            HazardPlacementRuleSet ruleSet = rules.GetRules(hazardClass);

            // ---- Ceiling / placement plane (Z) ----
            bool ceilingUnsupported = false;
            double placementZ = room.LevelElevationFt;
            string ceilingNote = null;

            CeilingData flatCeiling = (room.Ceilings != null)
                ? room.Ceilings.FirstOrDefault(c =>
                    c != null &&
                    string.Equals(c.SlopeType, "FLAT", StringComparison.OrdinalIgnoreCase) &&
                    c.BottomElevationFt.HasValue)
                : null;

            bool hasSlopedCeiling = (room.Ceilings != null) &&
                room.Ceilings.Any(c => c != null &&
                    !string.Equals(c.SlopeType, "FLAT", StringComparison.OrdinalIgnoreCase));

            if (flatCeiling != null)
            {
                placementZ = flatCeiling.BottomElevationFt.Value;
            }
            else
            {
                if (room.CeilingHeightFt.HasValue)
                {
                    placementZ = room.LevelElevationFt + room.CeilingHeightFt.Value;
                    ceilingNote = "Ceiling elevation derived from room ceiling height (provisional).";
                }
                else
                {
                    placementZ = room.LevelElevationFt;
                    ceilingNote = "Ceiling elevation unavailable; Z set to floor level provisionally.";
                }

                ceilingUnsupported = true;
                if (hasSlopedCeiling)
                {
                    ceilingNote = (ceilingNote + " Sloped/unsupported ceiling present.").Trim();
                }
            }

            if (ceilingUnsupported)
            {
                result.Status = CalculationStatus.ReviewRequired;
                if (flatCeiling == null)
                {
                    result.Status = hasSlopedCeiling
                        ? CalculationStatus.UnsupportedCeiling
                        : CalculationStatus.MissingCeiling;
                }
                result.Warnings.Add(ceilingNote ?? "Ceiling data requires review.");
            }

            // ---- Candidate generation ----
            List<ObstacleBox> obstacleBoxes = BuildObstacleBoxes(room, ruleSet, result);
            List<double[]> existingSprinklerXy = BuildExistingSprinklerXy(room);

            double gridRes = ComputeGridResolution(geometry, ruleSet, config, out int gridPointEstimate);

            int generated = 0;
            int validCount = 0;
            int rejectedBoundary = 0;
            int rejectedObstacle = 0;
            int rejectedExisting = 0;
            int rejectedOutside = 0;

            List<CandidatePoint> validCandidates = new List<CandidatePoint>();

            // Deterministic ordering: ascending Y, then ascending X.
            for (double y = geometry.MinY; y <= geometry.MaxY + config.ToleranceFt; y += gridRes)
            {
                if (generated >= config.MaxCandidatePoints) break;
                for (double x = geometry.MinX; x <= geometry.MaxX + config.ToleranceFt; x += gridRes)
                {
                    if (generated >= config.MaxCandidatePoints) break;
                    generated++;

                    CandidatePoint candidate = new CandidatePoint
                    {
                        X = x,
                        Y = y,
                        Z = placementZ
                    };

                    if (!geometry.IsPointInsideRoom(x, y, config.ToleranceFt))
                    {
                        candidate.IsValid = false;
                        candidate.RejectionReasons.Add("Outside room boundary");
                        rejectedOutside++;
                        continue;
                    }

                    if (geometry.DistanceToOuterBoundary(x, y) < ruleSet.BoundaryClearanceFt - config.ToleranceFt)
                    {
                        candidate.IsValid = false;
                        candidate.RejectionReasons.Add("Too close to room boundary");
                        rejectedBoundary++;
                        continue;
                    }

                    bool hitObstacle = false;
                    foreach (ObstacleBox box in obstacleBoxes)
                    {
                        if (GeometryMath.InsideExpandedBox(x, y, box.MinX, box.MinY, box.MaxX, box.MaxY, ruleSet.ObstacleClearanceFt))
                        {
                            candidate.IsValid = false;
                            candidate.RejectionReasons.Add("Inside/too close to obstacle: " + box.Category);
                            rejectedObstacle++;
                            hitObstacle = true;
                            break;
                        }
                    }
                    if (hitObstacle) continue;

                    bool hitExisting = false;
                    foreach (double[] es in existingSprinklerXy)
                    {
                        if (GeometryMath.Distance(x, y, es[0], es[1]) <= ruleSet.ExistingSprinklerSeparationFt - config.ToleranceFt)
                        {
                            candidate.IsValid = false;
                            candidate.RejectionReasons.Add("Too close to existing sprinkler");
                            rejectedExisting++;
                            hitExisting = true;
                            break;
                        }
                    }
                    if (hitExisting) continue;

                    candidate.IsValid = true;
                    candidate.Score = 1.0;
                    validCandidates.Add(candidate);
                    validCount++;
                }
            }

            result.Diagnostics.Add(
                $"Candidates generated={generated}, valid={validCount}, " +
                $"rejected(outside={rejectedOutside}, boundary={rejectedBoundary}, obstacle={rejectedObstacle}, existing={rejectedExisting}).");

            if (validCandidates.Count == 0)
            {
                result.Status = CalculationStatus.NoValidCandidates;
                result.Errors.Add("No valid candidate locations found (all rejected by geometry/obstacles/existing sprinklers).");
                result.CalculatedCount = 0;
                result.RequiredCount = 0;
                return result;
            }

            // ---- Deterministic selection (spacing + coverage) ----
            validCandidates.Sort((a, b) =>
            {
                int byY = a.Y.CompareTo(b.Y);
                return byY != 0 ? byY : a.X.CompareTo(b.X);
            });

            List<CalculatedSprinklerPoint> selected = new List<CalculatedSprinklerPoint>();
            int iterations = 0;

            foreach (CandidatePoint candidate in validCandidates)
            {
                if (iterations >= config.MaxSearchIterations) break;
                iterations++;

                bool covered = false;
                foreach (double[] es in existingSprinklerXy)
                {
                    if (GeometryMath.Distance(candidate.X, candidate.Y, es[0], es[1]) <= ruleSet.CoverageRadiusFt - config.ToleranceFt)
                    {
                        covered = true;
                        break;
                    }
                }

                if (!covered)
                {
                    foreach (CalculatedSprinklerPoint s in selected)
                    {
                        if (GeometryMath.Distance(candidate.X, candidate.Y, s.X, s.Y) <= ruleSet.CoverageRadiusFt - config.ToleranceFt)
                        {
                            covered = true;
                            break;
                        }
                    }
                }

                if (covered) continue;

                bool tooClose = false;
                foreach (CalculatedSprinklerPoint s in selected)
                {
                    if (GeometryMath.Distance(candidate.X, candidate.Y, s.X, s.Y) < ruleSet.MaxSpacingFt - config.ToleranceFt)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (tooClose) continue;

                selected.Add(new CalculatedSprinklerPoint
                {
                    X = candidate.X,
                    Y = candidate.Y,
                    Z = candidate.Z,
                    RoomId = room.RoomId,
                    LevelId = room.LevelId,
                    LevelName = room.LevelName
                });
            }

            result.Points = selected;
            result.CalculatedCount = selected.Count;

            double coverageArea = Math.PI * ruleSet.CoverageRadiusFt * ruleSet.CoverageRadiusFt;
            int provisionalRequired = coverageArea > 0
                ? (int)Math.Ceiling(room.AreaSqFt / coverageArea)
                : selected.Count;
            result.RequiredCount = provisionalRequired;

            if (ruleSet.IsProvisional)
            {
                result.Status = CalculationStatus.ReviewRequired;
                result.Warnings.Add(
                    "Spacing/coverage used provisional placeholder values; required count is an estimate. Review required.");
            }

            if (ceilingUnsupported)
            {
                result.Warnings.Add(ceilingNote ?? "Ceiling plane requires human review.");
            }

            return result;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static List<double[]> ExtractOuterPolygon(PlacementRoomInput room)
        {
            if (room.Boundary != null && room.Boundary.OuterLoop != null &&
                room.Boundary.OuterLoop.Polygon != null && room.Boundary.OuterLoop.Polygon.Count >= 3)
            {
                return room.Boundary.OuterLoop.Polygon;
            }

            if (room.BoundaryPolygon != null && room.BoundaryPolygon.Count >= 3)
            {
                return room.BoundaryPolygon;
            }

            return new List<double[]>();
        }

        private static List<List<double[]>> ExtractInnerLoops(PlacementRoomInput room)
        {
            List<List<double[]>> loops = new List<List<double[]>>();
            if (room.Boundary != null && room.Boundary.InnerLoops != null)
            {
                foreach (BoundaryLoopData loop in room.Boundary.InnerLoops)
                {
                    if (loop != null && loop.Polygon != null && loop.Polygon.Count >= 3)
                    {
                        loops.Add(loop.Polygon);
                    }
                }
            }
            return loops;
        }

        private static HazardClass ParseHazardClass(string effectiveHazardClass, RoomCalculationResult result)
        {
            if (string.IsNullOrWhiteSpace(effectiveHazardClass))
            {
                result.Warnings.Add("Missing effective hazard class; defaulted to Light (review required).");
                result.Status = CalculationStatus.ReviewRequired;
                return HazardClass.Light;
            }

            switch (effectiveHazardClass.Trim().ToLowerInvariant())
            {
                case "light": return HazardClass.Light;
                case "oh1": return HazardClass.OH1;
                case "oh2": return HazardClass.OH2;
                case "eh1": return HazardClass.EH1;
                case "eh2": return HazardClass.EH2;
                default:
                    result.Warnings.Add(
                        $"Unrecognized effective hazard class '{effectiveHazardClass}'; defaulted to Light (review required).");
                    result.Status = CalculationStatus.ReviewRequired;
                    return HazardClass.Light;
            }
        }

        private static double ComputeGridResolution(
            RoomGeometry geometry, HazardPlacementRuleSet ruleSet, BruteForceCalculationConfig config, out int estimate)
        {
            double res = config.GridResolutionFt;
            if (res > ruleSet.CoverageRadiusFt)
            {
                res = ruleSet.CoverageRadiusFt;
            }

            estimate = EstimateGridPoints(geometry, res);
            int guard = 0;
            while (estimate > config.MaxCandidatePoints && res < 25.0 && guard < 64)
            {
                res *= 2.0;
                if (res > ruleSet.CoverageRadiusFt * 4.0) res = ruleSet.CoverageRadiusFt * 4.0;
                estimate = EstimateGridPoints(geometry, res);
                guard++;
            }

            return res;
        }

        private static int EstimateGridPoints(RoomGeometry geometry, double res)
        {
            if (res <= 0) res = 1.0;
            int nx = (int)Math.Floor((geometry.MaxX - geometry.MinX) / res) + 2;
            int ny = (int)Math.Floor((geometry.MaxY - geometry.MinY) / res) + 2;
            if (nx < 0) nx = 0;
            if (ny < 0) ny = 0;
            return nx * ny;
        }

        private static List<ObstacleBox> BuildObstacleBoxes(
            PlacementRoomInput room, HazardPlacementRuleSet ruleSet, RoomCalculationResult result)
        {
            List<ObstacleBox> boxes = new List<ObstacleBox>();

            if (room.Obstacles == null) return boxes;

            foreach (ObstacleData obstacle in room.Obstacles)
            {
                if (obstacle == null) continue;

                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                bool haveBox = false;

                if (obstacle.BoundingBox != null && obstacle.BoundingBox.Min != null && obstacle.BoundingBox.Max != null)
                {
                    minX = obstacle.BoundingBox.Min.X;
                    minY = obstacle.BoundingBox.Min.Y;
                    maxX = obstacle.BoundingBox.Max.X;
                    maxY = obstacle.BoundingBox.Max.Y;
                    haveBox = maxX >= minX && maxY >= minY;
                }

                if (!haveBox && obstacle.CenterPoint != null && obstacle.DimensionsFt != null)
                {
                    double hx = Math.Abs(obstacle.DimensionsFt.X) / 2.0;
                    double hy = Math.Abs(obstacle.DimensionsFt.Y) / 2.0;
                    minX = obstacle.CenterPoint.X - hx;
                    minY = obstacle.CenterPoint.Y - hy;
                    maxX = obstacle.CenterPoint.X + hx;
                    maxY = obstacle.CenterPoint.Y + hy;
                    haveBox = true;
                }

                if (!haveBox)
                {
                    result.Warnings.Add(
                        $"Obstacle '{obstacle.Name ?? obstacle.Category ?? "unknown"}' has no usable bounding box; skipped.");
                    continue;
                }

                boxes.Add(new ObstacleBox
                {
                    MinX = minX,
                    MinY = minY,
                    MaxX = maxX,
                    MaxY = maxY,
                    Category = obstacle.Category ?? "obstacle"
                });
            }

            return boxes;
        }

        private static List<double[]> BuildExistingSprinklerXy(PlacementRoomInput room)
        {
            List<double[]> points = new List<double[]>();
            if (room.ExistingSprinklers == null) return points;

            foreach (ExistingSprinklerData sprinkler in room.ExistingSprinklers)
            {
                if (sprinkler?.Location != null)
                {
                    points.Add(new double[] { sprinkler.Location.X, sprinkler.Location.Y });
                }
            }

            return points;
        }

        private sealed class ObstacleBox
        {
            public double MinX { get; set; }
            public double MinY { get; set; }
            public double MaxX { get; set; }
            public double MaxY { get; set; }
            public string Category { get; set; }
        }
    }
}
