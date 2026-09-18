using System;
using System.Collections.Generic;
using System.Linq;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Placement.SmokeDetectors.Final;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.Backend.Services.Placement.LocationPoints;
using FireProtection.Backend.Services.Placement.NotificationAppliances.Final.BruteForce;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce;
using FireProtection.UI.Models.Sprinklers.BruteForce;

namespace FireProtection.Backend.Services.Placement.SmokeDetectors.Final.BruteForce
{
    public enum DevicePlacementMode
    {
        SmokeDetector,
        NotificationAppliance
    }

    /// <summary>
    /// Deterministic, Revit-free NFPA 72 smoke detector / notification appliance calculation engine.
    /// Consumes <see cref="SmokeDetectorPlacementInputSnapshot"/> and returns
    /// <see cref="SmokeDetectorCalculationResult"/>. One failing room does not abort others.
    /// Reuses sprinkler BruteForce geometry helpers (<see cref="RoomGeometry"/>, <see cref="GeometryMath"/>).
    /// </summary>
    public static class SmokeDetectorCalculationService
    {
        public static SmokeDetectorCalculationResult Calculate(
            SmokeDetectorPlacementInputSnapshot snapshot,
            ISmokeDetectorPlacementRules rules,
            BruteForceCalculationConfig config)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (config == null) throw new ArgumentNullException(nameof(config));

            SmokeDetectorCalculationResult result = new SmokeDetectorCalculationResult
            {
                IsProvisional = !rules.HasApprovedRules,
                AppliedRulesSummary = rules.HasApprovedRules
                    ? "NFPA 72 standard smoke detector / notification appliance placement rules."
                    : "PROVISIONAL placeholder rules (engineering review required)."
            };

            if (snapshot.Rooms == null || snapshot.Rooms.Count == 0)
            {
                result.Success = false;
                result.Errors.Add("No rooms supplied to the smoke detector calculation.");
                return result;
            }

            int overrideCount = 0;
            foreach (SmokeDetectorRoomInput r in snapshot.Rooms)
            {
                if (r == null) continue;
                if (r.OverrideMaxSpacingFt.HasValue || r.OverrideBoundaryClearanceFt.HasValue)
                    overrideCount++;
            }
            if (overrideCount > 0)
            {
                result.AppliedRulesSummary +=
                    " " + overrideCount + " room(s) use per-room spacing/clearance overrides.";
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
                        RoomId = room != null ? room.RoomId : null,
                        RoomName = room != null ? room.RoomName : null,
                        RoomNumber = room != null ? room.RoomNumber : null,
                        FamilyName = room != null ? room.SelectedFamilyName : null,
                        TypeName = room != null ? room.SelectedTypeName : null,
                        Status = CalculationStatus.Failed
                    };
                    roomResult.Errors.Add("Unexpected calculation error: " + ex.Message);
                }

                result.Rooms.Add(roomResult);
                result.TotalCalculatedDetectors += roomResult.CalculatedCount;
                result.Warnings.AddRange(roomResult.Warnings);
                result.Errors.AddRange(roomResult.Errors);
            }

            result.Success = result.Rooms.All(r => r.IsSuccessful);

            if (result.IsProvisional)
            {
                result.Warnings.Add(
                    "Calculation used PROVISIONAL spacing rules. Review required before placement.");
            }
            return result;
        }

        private static SmokeDetectorRoomCalculationResult CalculateRoom(
            SmokeDetectorRoomInput room,
            ISmokeDetectorPlacementRules rules,
            BruteForceCalculationConfig config)
        {
            SmokeDetectorRoomCalculationResult result = new SmokeDetectorRoomCalculationResult
            {
                RoomId = room.RoomId,
                RoomName = room.RoomName,
                RoomNumber = room.RoomNumber,
                FamilyName = room.SelectedFamilyName,
                TypeName = room.SelectedTypeName,
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

            result.Polygon = outer;

            CeilingData bestCeiling = SelectPrimaryCeiling(room, out string selectionReason);
            string effectiveCeilingSlope = room.CeilingSlope;
            if (string.IsNullOrWhiteSpace(effectiveCeilingSlope) && bestCeiling != null)
                effectiveCeilingSlope = bestCeiling.SlopeType;

            SmokeDetectorPlacementRuleSet baseRuleSet = rules.GetRules(
                room.DetectorType,
                room.Mount,
                effectiveCeilingSlope,
                room.AirChangesPerHour);

            SmokeDetectorPlacementRuleSet ruleSet = ApplyPerRoomOverrides(baseRuleSet, room, result);
            if (ReferenceEquals(ruleSet, baseRuleSet))
            {
                ruleSet = baseRuleSet.Clone();
            }

            result.AppliedMaxSpacingFt = ruleSet.MaxSpacingFt;
            result.AppliedBoundaryClearanceFt = ruleSet.MinBoundaryClearanceFt;

            bool ceilingUnsupported = false;
            double placementZ = room.LevelElevationFt;
            string ceilingNote = null;

            if (bestCeiling != null && bestCeiling.BottomElevationFt.HasValue)
            {
                if (string.Equals(bestCeiling.SlopeType, "SLOPED", StringComparison.OrdinalIgnoreCase)
                    && bestCeiling.TopElevationFt.HasValue
                    && bestCeiling.TopElevationFt.Value > bestCeiling.BottomElevationFt.Value)
                {
                    double avg = (bestCeiling.BottomElevationFt.Value + bestCeiling.TopElevationFt.Value) / 2.0;
                    placementZ = avg;
                    result.Diagnostics.Add(
                        "Sloped ceiling Z averaged: bottom=" + bestCeiling.BottomElevationFt.Value.ToString("F2")
                        + " ft, top=" + bestCeiling.TopElevationFt.Value.ToString("F2")
                        + " ft, average=" + avg.ToString("F2") + " ft.");
                }
                else
                {
                    placementZ = bestCeiling.BottomElevationFt.Value;
                }

                if (!string.IsNullOrEmpty(selectionReason))
                    result.Diagnostics.Add(selectionReason);
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
                    placementZ = room.LevelElevationFt + 10.0;
                    ceilingNote = "Ceiling elevation unavailable; Z defaulted to floor + 10 ft provisionally.";
                }

                ceilingUnsupported = true;
            }

            if (room.SelectedPlacementBehavior == DevicePlacementBehavior.Unsupported)
            {
                result.Status = CalculationStatus.InvalidInput;
                result.CalculatedCount = 0;
                result.RequiredCount = 0;
                result.Errors.Add(
                    "The selected smoke detector family has an unsupported placement behavior ("
                    + room.SelectedFamilyName + " / " + room.SelectedTypeName
                    + "). Select a different family or extend the engine.");
                return result;
            }

            if (ceilingUnsupported)
            {
                result.Status = CalculationStatus.ReviewRequired;
                result.Warnings.Add(ceilingNote ?? "Ceiling data requires review.");
            }

            List<ObstacleBox> obstacleBoxes = BuildObstacleBoxes(room, ruleSet, result);
            List<double[]> existingDetectorXy = BuildExistingDetectorXy(room);

            bool isWallMount =
                string.Equals(ruleSet.Mount, "Wall", StringComparison.OrdinalIgnoreCase)
                || room.SelectedPlacementBehavior == DevicePlacementBehavior.WallSidewall;
            DevicePlacementMode deviceMode = room.DeviceKind == FireProtection.UI.Services.DeviceKind.NotificationAppliance
                ? DevicePlacementMode.NotificationAppliance
                : DevicePlacementMode.SmokeDetector;

            if (!isWallMount && deviceMode == DevicePlacementMode.SmokeDetector)
            {
                ApplyBeamAndSlopeAdjustments(room, geometry, ruleSet, obstacleBoxes, placementZ, result);
            }

            var locationProfile = deviceMode == DevicePlacementMode.NotificationAppliance
                ? DeviceLocationPointIdentifier.ResolveNotificationApplianceProfile(
                    room.DetectorType,
                    ruleSet.Mount,
                    ruleSet.MaxSpacingFt,
                    ruleSet.MinSpacingFt,
                    ruleSet.CoverageRadiusFt,
                    placementZ,
                    room.RoomId)
                : DeviceLocationPointIdentifier.ResolveSmokeDetectorProfile(
                    room.DetectorType,
                    ruleSet.Mount,
                    effectiveCeilingSlope,
                    ruleSet.MaxSpacingFt,
                    ruleSet.MinSpacingFt,
                    ruleSet.CoverageRadiusFt,
                    placementZ,
                    room.RoomId);

            result.Diagnostics.Add(
                "Device placement mode=" + deviceMode
                + ", detected strategy=" + locationProfile.StrategyName
                + ", mount=" + locationProfile.Mount
                + ", grid=" + locationProfile.GridResolutionFt.ToString("F2")
                + " ft, min-spacing=" + locationProfile.MinSpacingFt.ToString("F2")
                + " ft, coverage=" + locationProfile.CoverageRadiusFt.ToString("F2")
                + " ft, basis=" + locationProfile.SelectionBasis);

            List<CandidatePoint> candidates;
            if (deviceMode == DevicePlacementMode.NotificationAppliance)
            {
                result.Diagnostics.Add(
                    "Notification appliance branch selected: visible/audible coverage spacing is used instead of smoke-detector coverage geometry.");
                candidates = GenerateNotificationApplianceCandidates(
                    room, geometry, ruleSet, obstacleBoxes, existingDetectorXy,
                    placementZ, config, result, isWallMount, locationProfile);
            }
            else if (isWallMount)
            {
                result.Diagnostics.Add(
                    "Smoke detector branch selected: wall-mounted detector spacing and clearance are used for wall location-point identification.");
                double wallZ = placementZ - ruleSet.WallMountDropFromCeilingFt;
                candidates = GenerateWallMountCandidates(
                    room, geometry, ruleSet, obstacleBoxes, existingDetectorXy, wallZ, config, result);
            }
            else
            {
                result.Diagnostics.Add(
                    "Smoke detector branch selected: ceiling-grid detection is used with smoke-detector spacing and boundary checks.");
                candidates = GenerateCeilingCandidates(
                    geometry, ruleSet, obstacleBoxes, existingDetectorXy, placementZ, config, result);

                List<CandidatePoint> peakRowCandidates = GenerateSlopedCeilingPeakRowCandidates(
                    room, geometry, ruleSet, obstacleBoxes, existingDetectorXy, placementZ, config, result);
                if (peakRowCandidates.Count > 0)
                {
                    candidates.AddRange(peakRowCandidates);
                    result.Diagnostics.Add(
                        "Sloped-ceiling peak-row candidates added: " + peakRowCandidates.Count + " point(s) within 3 ft of peak.");
                }
            }

            if (candidates == null || candidates.Count == 0)
            {
                result.Status = CalculationStatus.NoValidCandidates;
                result.Errors.Add("No valid candidate locations found (geometry/obstacles/existing detectors).");
                result.CalculatedCount = 0;
                result.RequiredCount = 0;
                return result;
            }

            return SelectFromCandidates(
                candidates, room, geometry, ruleSet, obstacleBoxes, existingDetectorXy,
                config, result, isWallMount, ceilingUnsupported, ceilingNote);
        }

        private static List<CandidatePoint> GenerateNotificationApplianceCandidates(
            SmokeDetectorRoomInput room,
            RoomGeometry geometry,
            SmokeDetectorPlacementRuleSet ruleSet,
            List<ObstacleBox> obstacleBoxes,
            List<double[]> existingDetectorXy,
            double placementZ,
            BruteForceCalculationConfig config,
            SmokeDetectorRoomCalculationResult result,
            bool isWallMount,
            FireProtection.Backend.Services.Placement.LocationPoints.DeviceLocationPointProfile locationProfile)
        {
            List<CandidatePoint> validCandidates = new List<CandidatePoint>();
            double baseGridRes = ComputeGridResolution(geometry, ruleSet, config);
            double notificationGridRes = Math.Max(1.0, Math.Min(baseGridRes, Math.Max(2.0, ruleSet.MaxSpacingFt / 3.0)));
            if (locationProfile == null)
            {
                locationProfile = new FireProtection.Backend.Services.Placement.LocationPoints.DeviceLocationPointProfile
                {
                    StrategyName = isWallMount ? "notification-wall-mount" : "notification-ceiling-grid",
                    GridResolutionFt = notificationGridRes,
                    MinSpacingFt = Math.Max(5.0, ruleSet.MaxSpacingFt * 0.35),
                    CoverageRadiusFt = Math.Max(5.0, ruleSet.CoverageRadiusFt)
                };
            }

            if (isWallMount)
            {
                double wallZ = placementZ - ruleSet.WallMountDropFromCeilingFt;
                return GenerateWallMountCandidates(
                    room, geometry, ruleSet, obstacleBoxes, existingDetectorXy, wallZ, config, result);
            }

            for (double y = geometry.MinY; y <= geometry.MaxY + config.ToleranceFt; y += notificationGridRes)
            {
                for (double x = geometry.MinX; x <= geometry.MaxX + config.ToleranceFt; x += notificationGridRes)
                {
                    if (!geometry.IsPointInsideRoom(x, y, config.ToleranceFt)) continue;
                    if (geometry.DistanceToOuterBoundary(x, y) < Math.Max(ruleSet.MinBoundaryClearanceFt, 0.333) - config.ToleranceFt)
                        continue;

                    bool hitObstacle = false;
                    foreach (ObstacleBox box in obstacleBoxes)
                    {
                        if (GeometryMath.InsideExpandedBox(
                            x, y, box.MinX, box.MinY, box.MaxX, box.MaxY, Math.Max(box.ClearanceFt, ruleSet.ObstacleClearanceFt)))
                        {
                            if (!box.SpansZ(placementZ, config.ToleranceFt)) continue;
                            hitObstacle = true;
                            break;
                        }
                    }
                    if (hitObstacle) continue;

                    bool hitExisting = false;
                    foreach (double[] es in existingDetectorXy)
                    {
                        if (GeometryMath.Distance(x, y, es[0], es[1]) <= Math.Max(ruleSet.ExistingDetectorSeparationFt, ruleSet.MinSpacingFt) - config.ToleranceFt)
                        {
                            hitExisting = true;
                            break;
                        }
                    }
                    if (hitExisting) continue;

                    validCandidates.Add(new CandidatePoint
                    {
                        X = x,
                        Y = y,
                        Z = placementZ,
                        IsValid = true,
                        Score = 1.0
                    });
                }
            }

            result.Diagnostics.Add(
                "Notification-appliance candidates generated=" + validCandidates.Count
                + " using stricter audible/visible spacing at grid step " + notificationGridRes.ToString("F2") + " ft.");

            return validCandidates;
        }

        private static List<CandidatePoint> GenerateCeilingCandidates(
            RoomGeometry geometry,
            SmokeDetectorPlacementRuleSet ruleSet,
            List<ObstacleBox> obstacleBoxes,
            List<double[]> existingDetectorXy,
            double placementZ,
            BruteForceCalculationConfig config,
            SmokeDetectorRoomCalculationResult result)
        {
            List<CandidatePoint> validCandidates = new List<CandidatePoint>();

            double gridRes = ComputeGridResolution(geometry, ruleSet, config);

            int generated = 0;
            int rejectedOutside = 0;
            int rejectedObstacle = 0;
            int rejectedExisting = 0;
            int rejectedBoundary = 0;

            for (double y = geometry.MinY; y <= geometry.MaxY + config.ToleranceFt; y += gridRes)
            {
                if (generated >= config.MaxCandidatePoints) break;
                for (double x = geometry.MinX; x <= geometry.MaxX + config.ToleranceFt; x += gridRes)
                {
                    if (generated >= config.MaxCandidatePoints) break;
                    generated++;

                    if (!geometry.IsPointInsideRoom(x, y, config.ToleranceFt))
                    {
                        rejectedOutside++;
                        continue;
                    }

                    // NFPA 72 §17.7.3.2.1: >= 4 in from wall
                    if (geometry.DistanceToOuterBoundary(x, y) < ruleSet.MinBoundaryClearanceFt - config.ToleranceFt)
                    {
                        rejectedBoundary++;
                        continue;
                    }

                    bool hitObstacle = false;
                    foreach (ObstacleBox box in obstacleBoxes)
                    {
                        if (GeometryMath.InsideExpandedBox(
                            x, y, box.MinX, box.MinY, box.MaxX, box.MaxY, box.ClearanceFt))
                        {
                            if (!box.SpansZ(placementZ, config.ToleranceFt)) continue;
                            rejectedObstacle++;
                            hitObstacle = true;
                            break;
                        }
                    }
                    if (hitObstacle) continue;

                    bool hitExisting = false;
                    foreach (double[] es in existingDetectorXy)
                    {
                        if (GeometryMath.Distance(x, y, es[0], es[1])
                            <= ruleSet.ExistingDetectorSeparationFt - config.ToleranceFt)
                        {
                            rejectedExisting++;
                            hitExisting = true;
                            break;
                        }
                    }
                    if (hitExisting) continue;

                    validCandidates.Add(new CandidatePoint
                    {
                        X = x,
                        Y = y,
                        Z = placementZ,
                        IsValid = true,
                        Score = 1.0
                    });
                }
            }

            result.Diagnostics.Add(
                "Ceiling candidates: generated=" + generated
                + ", valid=" + validCandidates.Count
                + ", rejected(outside=" + rejectedOutside
                + ", boundary=" + rejectedBoundary
                + ", obstacle=" + rejectedObstacle
                + ", existing=" + rejectedExisting + ").");

            return validCandidates;
        }

        private static List<CandidatePoint> GenerateSlopedCeilingPeakRowCandidates(
            SmokeDetectorRoomInput room,
            RoomGeometry geometry,
            SmokeDetectorPlacementRuleSet ruleSet,
            List<ObstacleBox> obstacleBoxes,
            List<double[]> existingDetectorXy,
            double placementZ,
            BruteForceCalculationConfig config,
            SmokeDetectorRoomCalculationResult result)
        {
            List<CandidatePoint> candidates = new List<CandidatePoint>();

            if (room.Ceilings == null || room.Ceilings.Count == 0) return candidates;

            CeilingData slopedCeiling = null;
            foreach (CeilingData c in room.Ceilings)
            {
                if (c == null || !c.BottomElevationFt.HasValue || !c.TopElevationFt.HasValue) continue;
                if (string.Equals(c.SlopeType, "FLAT", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(c.SlopeType, "NONE", StringComparison.OrdinalIgnoreCase)) continue;
                if (slopedCeiling == null || c.TopElevationFt.Value > slopedCeiling.TopElevationFt.Value)
                    slopedCeiling = c;
            }

            if (slopedCeiling == null) return candidates;

            double peakZ = slopedCeiling.TopElevationFt.Value;
            double bottomZ = slopedCeiling.BottomElevationFt.Value;
            double peakRowZ = peakZ - 3.0;
            if (peakRowZ < bottomZ) peakRowZ = bottomZ;

            double gridRes = ComputeGridResolution(geometry, ruleSet, config);
            int generated = 0;
            int rejectedOutside = 0;
            int rejectedObstacle = 0;
            int rejectedExisting = 0;
            int rejectedBoundary = 0;

            for (double y = geometry.MinY; y <= geometry.MaxY + config.ToleranceFt; y += gridRes)
            {
                for (double x = geometry.MinX; x <= geometry.MaxX + config.ToleranceFt; x += gridRes)
                {
                    if (generated >= config.MaxCandidatePoints) break;
                    generated++;

                    if (!geometry.IsPointInsideRoom(x, y, config.ToleranceFt))
                    {
                        rejectedOutside++;
                        continue;
                    }

                    if (geometry.DistanceToOuterBoundary(x, y) < ruleSet.MinBoundaryClearanceFt - config.ToleranceFt)
                    {
                        rejectedBoundary++;
                        continue;
                    }

                    bool hitObstacle = false;
                    foreach (ObstacleBox box in obstacleBoxes)
                    {
                        if (GeometryMath.InsideExpandedBox(
                            x, y, box.MinX, box.MinY, box.MaxX, box.MaxY, box.ClearanceFt))
                        {
                            if (!box.SpansZ(peakRowZ, config.ToleranceFt)) continue;
                            rejectedObstacle++;
                            hitObstacle = true;
                            break;
                        }
                    }
                    if (hitObstacle) continue;

                    bool hitExisting = false;
                    foreach (double[] es in existingDetectorXy)
                    {
                        if (GeometryMath.Distance(x, y, es[0], es[1])
                            <= ruleSet.ExistingDetectorSeparationFt - config.ToleranceFt)
                        {
                            rejectedExisting++;
                            hitExisting = true;
                            break;
                        }
                    }
                    if (hitExisting) continue;

                    candidates.Add(new CandidatePoint
                    {
                        X = x,
                        Y = y,
                        Z = peakRowZ,
                        IsValid = true,
                        Score = 1.0
                    });
                }
            }

            result.Diagnostics.Add(
                "Sloped-ceiling peak-row candidates (NFPA 72 17.7.3.4): generated=" + candidates.Count
                + ", rejected(outside=" + rejectedOutside + ", boundary=" + rejectedBoundary
                + ", obstacle=" + rejectedObstacle
                + ", existing=" + rejectedExisting + "), peakZ=" + peakZ.ToString("F2")
                + " ft, peakRowZ=" + peakRowZ.ToString("F2") + " ft.");

            return candidates;
        }

        private static List<CandidatePoint> GenerateWallMountCandidates(
            SmokeDetectorRoomInput room,
            RoomGeometry geometry,
            SmokeDetectorPlacementRuleSet ruleSet,
            List<ObstacleBox> obstacleBoxes,
            List<double[]> existingDetectorXy,
            double wallZ,
            BruteForceCalculationConfig config,
            SmokeDetectorRoomCalculationResult result)
        {
            List<CandidatePoint> candidates = new List<CandidatePoint>();

            List<double[]> polygon = null;
            if (room.BoundaryPolygon != null && room.BoundaryPolygon.Count >= 3)
                polygon = room.BoundaryPolygon;
            else if (room.Boundary != null
                     && room.Boundary.OuterLoop != null
                     && room.Boundary.OuterLoop.Polygon != null
                     && room.Boundary.OuterLoop.Polygon.Count >= 3)
                polygon = room.Boundary.OuterLoop.Polygon;

            if (polygon == null || polygon.Count < 3) return candidates;

            const double wallStandoffFt = 0.15;
            double step = ruleSet.MaxSpacingFt > 0 ? ruleSet.MaxSpacingFt : 15.0;

            int totalGenerated = 0;
            int rejectedOutside = 0;
            int rejectedObstacle = 0;
            int rejectedExisting = 0;

            for (int i = 0; i < polygon.Count; i++)
            {
                double[] a = polygon[i];
                double[] b = polygon[(i + 1) % polygon.Count];
                if (a == null || b == null || a.Length < 2 || b.Length < 2) continue;

                double ex = b[0] - a[0];
                double ey = b[1] - a[1];
                double edgeLen = Math.Sqrt(ex * ex + ey * ey);
                if (edgeLen < 1e-6) continue;

                double nxLeft = -ey / edgeLen;
                double nyLeft = ex / edgeLen;
                double midX = (a[0] + b[0]) * 0.5;
                double midY = (a[1] + b[1]) * 0.5;

                bool leftIsInboard = geometry.IsPointInsideRoom(
                    midX + nxLeft * 0.5, midY + nyLeft * 0.5, config.ToleranceFt);
                double nx = leftIsInboard ? nxLeft : -nxLeft;
                double ny = leftIsInboard ? nyLeft : -nyLeft;

                int n = Math.Max(1, (int)Math.Ceiling(edgeLen / step));
                for (int k = 0; k <= n; k++)
                {
                    double t = (double)k / (double)n;
                    double cx = a[0] + ex * t + nx * wallStandoffFt;
                    double cy = a[1] + ey * t + ny * wallStandoffFt;
                    totalGenerated++;

                    if (!geometry.IsPointInsideRoom(cx, cy, config.ToleranceFt))
                    {
                        rejectedOutside++;
                        continue;
                    }

                    bool hitObstacle = false;
                    foreach (ObstacleBox box in obstacleBoxes)
                    {
                        if (GeometryMath.InsideExpandedBox(
                            cx, cy, box.MinX, box.MinY, box.MaxX, box.MaxY, box.ClearanceFt))
                        {
                            if (!box.SpansZ(wallZ, config.ToleranceFt)) continue;
                            rejectedObstacle++;
                            hitObstacle = true;
                            break;
                        }
                    }
                    if (hitObstacle) continue;

                    bool hitExisting = false;
                    foreach (double[] es in existingDetectorXy)
                    {
                        if (GeometryMath.Distance(cx, cy, es[0], es[1])
                            <= ruleSet.ExistingDetectorSeparationFt - config.ToleranceFt)
                        {
                            rejectedExisting++;
                            hitExisting = true;
                            break;
                        }
                    }
                    if (hitExisting) continue;

                    candidates.Add(new CandidatePoint
                    {
                        X = cx,
                        Y = cy,
                        Z = wallZ,
                        IsValid = true,
                        Score = 1.0,
                        WallEdgeIndex = i
                    });
                }
            }

            result.Diagnostics.Add(
                "Wall candidates: generated=" + totalGenerated
                + ", valid=" + candidates.Count
                + ", rejected(outside=" + rejectedOutside
                + ", obstacle=" + rejectedObstacle
                + ", existing=" + rejectedExisting + ").");

            return candidates;
        }

        private static SmokeDetectorRoomCalculationResult SelectFromCandidates(
            List<CandidatePoint> validCandidates,
            SmokeDetectorRoomInput room,
            RoomGeometry geometry,
            SmokeDetectorPlacementRuleSet ruleSet,
            List<ObstacleBox> obstacleBoxes,
            List<double[]> existingDetectorXy,
            BruteForceCalculationConfig config,
            SmokeDetectorRoomCalculationResult result,
            bool isWallMount,
            bool ceilingUnsupported,
            string ceilingNote)
        {
            bool isNotificationAppliance = room.DeviceKind == FireProtection.UI.Services.DeviceKind.NotificationAppliance;

            double minSpacingFt = isNotificationAppliance
                ? Math.Max(ruleSet.MinSpacingFt > 0 ? ruleSet.MinSpacingFt : 5.0, Math.Min(ruleSet.MaxSpacingFt, ruleSet.MaxSpacingFt * 0.35))
                : (ruleSet.MinSpacingFt > 0 ? ruleSet.MinSpacingFt : ruleSet.CoverageRadiusFt);

            double coverageRadiusFt = isNotificationAppliance
                ? Math.Min(ruleSet.CoverageRadiusFt > 0 ? ruleSet.CoverageRadiusFt : 21.213, Math.Max(ruleSet.MaxSpacingFt * 0.50, ruleSet.MinSpacingFt))
                : (ruleSet.CoverageRadiusFt > 0 ? ruleSet.CoverageRadiusFt : 21.213);

            result.Diagnostics.Add(
                "Selected industry strategy=" + (isNotificationAppliance ? "notification-appliance-visible-audible" : (isWallMount ? "smoke-detector-wall-mounted" : "smoke-detector-ceiling-grid"))
                + ", minSpacingFt=" + minSpacingFt.ToString("F2")
                + ", coverageRadiusFt=" + coverageRadiusFt.ToString("F2")
                + ", wallMount=" + isWallMount.ToString().ToLowerInvariant() + ".");

            validCandidates.Sort((a, b) =>
            {
                int byY = a.Y.CompareTo(b.Y);
                return byY != 0 ? byY : a.X.CompareTo(b.X);
            });

            List<CalculatedSmokeDetectorPoint> selected = new List<CalculatedSmokeDetectorPoint>();
            int iterations = 0;

            foreach (CandidatePoint candidate in validCandidates)
            {
                if (iterations >= config.MaxSearchIterations) break;
                iterations++;

                bool alreadyCovered = false;
                foreach (double[] es in existingDetectorXy)
                {
                    if (GeometryMath.Distance(candidate.X, candidate.Y, es[0], es[1])
                        <= coverageRadiusFt - config.ToleranceFt)
                    {
                        alreadyCovered = true;
                        break;
                    }
                }
                if (!alreadyCovered)
                {
                    foreach (CalculatedSmokeDetectorPoint s in selected)
                    {
                        if (GeometryMath.Distance(candidate.X, candidate.Y, s.X, s.Y)
                            <= coverageRadiusFt - config.ToleranceFt)
                        {
                            alreadyCovered = true;
                            break;
                        }
                    }
                }
                if (alreadyCovered) continue;

                bool tooClose = false;
                foreach (CalculatedSmokeDetectorPoint s in selected)
                {
                    if (GeometryMath.Distance(candidate.X, candidate.Y, s.X, s.Y)
                        < minSpacingFt - config.ToleranceFt)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) continue;

                selected.Add(new CalculatedSmokeDetectorPoint
                {
                    X = candidate.X,
                    Y = candidate.Y,
                    Z = candidate.Z,
                    RoomId = room.RoomId,
                    LevelId = room.LevelId,
                    LevelName = room.LevelName,
                    Mount = isWallMount ? "Wall" : "Ceiling",
                    WallEdgeIndex = candidate.WallEdgeIndex
                });
            }

            // Max spacing pair check
            if (ruleSet.MaxSpacingFt > 0)
            {
                double maxSpacing = ruleSet.MaxSpacingFt;
                for (int i = 0; i < selected.Count; i++)
                {
                    for (int j = i + 1; j < selected.Count; j++)
                    {
                        double d = GeometryMath.Distance(
                            selected[i].X, selected[i].Y, selected[j].X, selected[j].Y);
                        if (d > maxSpacing + config.ToleranceFt)
                        {
                            result.Warnings.Add(
                                "Detector pair (" + (i + 1) + "," + (j + 1) + ") are "
                                + d.ToString("F2") + " ft apart, exceeding MaxSpacingFt="
                                + maxSpacing.ToString("F2") + " ft. Review required.");
                            result.Status = CalculationStatus.ReviewRequired;
                        }
                    }
                }
            }

            // Max distance from walls (ceiling mounts only)
            if (!isWallMount && ruleSet.MaxDistanceFromWallsFt > 0 && selected.Count > 0)
            {
                double maxWall = ruleSet.MaxDistanceFromWallsFt;
                for (int i = 0; i < selected.Count; i++)
                {
                    double d = geometry.DistanceToOuterBoundary(selected[i].X, selected[i].Y);
                    if (d > maxWall + config.ToleranceFt)
                    {
                        result.Warnings.Add(
                            "Detector " + (i + 1) + " is " + d.ToString("F2")
                            + " ft from nearest wall, exceeding MaxDistanceFromWallsFt="
                            + maxWall.ToString("F2") + " ft. Review required.");
                        result.Status = CalculationStatus.ReviewRequired;
                    }
                }
            }

            // Coverage area check
            if (ruleSet.MaxCoverageAreaSqFt > 0 && selected.Count > 0 && room.AreaSqFt > 0)
            {
                double perDetectorArea = room.AreaSqFt / selected.Count;
                if (perDetectorArea > ruleSet.MaxCoverageAreaSqFt + 1e-6)
                {
                    result.Warnings.Add(
                        "Per-detector coverage area (" + perDetectorArea.ToString("F1")
                        + " sq ft) exceeds MaxCoverageAreaSqFt="
                        + ruleSet.MaxCoverageAreaSqFt.ToString("F1")
                        + " sq ft. Review required.");
                    result.Status = CalculationStatus.ReviewRequired;
                }
            }

            // Coverage gap sample
            if (ruleSet.CoverageRadiusFt > 0 && selected.Count > 0)
            {
                double sampleStep = Math.Min(ruleSet.CoverageRadiusFt, ruleSet.MaxSpacingFt);
                if (sampleStep <= 0) sampleStep = ruleSet.CoverageRadiusFt;
                sampleStep = Math.Max(sampleStep / 2.0, 0.5);

                int samples = 0;
                int uncovered = 0;
                double coverageThreshold = ruleSet.CoverageRadiusFt - config.ToleranceFt;
                double placementZGap = selected[0].Z;

                for (double sy = geometry.MinY; sy <= geometry.MaxY + config.ToleranceFt; sy += sampleStep)
                {
                    for (double sx = geometry.MinX; sx <= geometry.MaxX + config.ToleranceFt; sx += sampleStep)
                    {
                        if (!geometry.IsPointInsideRoom(sx, sy, config.ToleranceFt)) continue;

                        bool insideObstacle = false;
                        foreach (ObstacleBox box in obstacleBoxes)
                        {
                            if (!box.SpansZ(placementZGap, config.ToleranceFt)) continue;
                            if (GeometryMath.InsideExpandedBox(
                                sx, sy, box.MinX, box.MinY, box.MaxX, box.MaxY, 0.0))
                            {
                                insideObstacle = true;
                                break;
                            }
                        }
                        if (insideObstacle) continue;

                        samples++;
                        double nearest = double.PositiveInfinity;
                        foreach (CalculatedSmokeDetectorPoint s in selected)
                        {
                            double d = GeometryMath.Distance(sx, sy, s.X, s.Y);
                            if (d < nearest) nearest = d;
                        }
                        foreach (double[] es in existingDetectorXy)
                        {
                            double d = GeometryMath.Distance(sx, sy, es[0], es[1]);
                            if (d < nearest) nearest = d;
                        }

                        if (nearest > coverageThreshold)
                            uncovered++;
                    }
                }

                if (samples > 0 && uncovered > 0)
                {
                    double pct = (double)uncovered * 100.0 / samples;
                    result.Warnings.Add(
                        "Coverage gap detected: " + uncovered + " of " + samples
                        + " sample points (" + pct.ToString("F1")
                        + "%) are beyond CoverageRadiusFt="
                        + ruleSet.CoverageRadiusFt.ToString("F2") + " ft. Review required.");
                    if (pct > 5.0)
                        result.Status = CalculationStatus.ReviewRequired;
                }
            }

            result.Points = selected;
            result.CalculatedCount = selected.Count;

            double coverageArea = ruleSet.MaxCoverageAreaSqFt > 0
                ? ruleSet.MaxCoverageAreaSqFt
                : Math.PI * ruleSet.CoverageRadiusFt * ruleSet.CoverageRadiusFt;
            result.RequiredCount = coverageArea > 0
                ? (int)Math.Ceiling(room.AreaSqFt / coverageArea)
                : selected.Count;

            if (ruleSet.IsProvisional)
            {
                result.Status = CalculationStatus.ReviewRequired;
                result.Warnings.Add(
                    "Spacing/coverage used provisional or overridden values. Review required.");
            }

            if (ceilingUnsupported && !string.IsNullOrEmpty(ceilingNote))
            {
                if (!result.Warnings.Contains(ceilingNote))
                    result.Warnings.Add(ceilingNote);
            }

            if (room.DeviceKind == FireProtection.UI.Services.DeviceKind.NotificationAppliance && selected.Count > 0)
            {
                var audibleResult = AudibleCoverageEngine.Evaluate(
                    selected,
                    geometry,
                    obstacleBoxes,
                    ambientDb: 40.0,
                    maxSustainedDb: 70.0,
                    isSleepingArea: room.AreaSqFt <= 500.0 && selected[0].Z > 8.0,
                    config);

                result.Diagnostics.Add(
                    "Audible coverage (NFPA 72 Ch. 18): " + audibleResult.Status + " - " + audibleResult.Message);

                if (audibleResult.Status == "CoverageGap")
                {
                    result.Status = CalculationStatus.ReviewRequired;
                    result.Warnings.Add("Audible coverage gap detected: " + audibleResult.Message);
                }
            }

            return result;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static List<double[]> ExtractOuterPolygon(SmokeDetectorRoomInput room)
        {
            if (room.Boundary != null
                && room.Boundary.OuterLoop != null
                && room.Boundary.OuterLoop.Polygon != null
                && room.Boundary.OuterLoop.Polygon.Count >= 3)
            {
                return room.Boundary.OuterLoop.Polygon;
            }

            if (room.BoundaryPolygon != null && room.BoundaryPolygon.Count >= 3)
                return room.BoundaryPolygon;

            return new List<double[]>();
        }

        private static List<List<double[]>> ExtractInnerLoops(SmokeDetectorRoomInput room)
        {
            List<List<double[]>> loops = new List<List<double[]>>();
            if (room.Boundary != null && room.Boundary.InnerLoops != null)
            {
                foreach (BoundaryLoopData loop in room.Boundary.InnerLoops)
                {
                    if (loop != null && loop.Polygon != null && loop.Polygon.Count >= 3)
                        loops.Add(loop.Polygon);
                }
            }
            return loops;
        }

        private static double ComputeGridResolution(
            RoomGeometry geometry,
            SmokeDetectorPlacementRuleSet ruleSet,
            BruteForceCalculationConfig config)
        {
            double res = config.GridResolutionFt;
            if (ruleSet.CoverageRadiusFt > 0 && res > ruleSet.CoverageRadiusFt)
                res = ruleSet.CoverageRadiusFt;

            if (ruleSet.CoverageRadiusFt > 0)
            {
                double roomW = geometry.MaxX - geometry.MinX;
                double roomH = geometry.MaxY - geometry.MinY;
                double floor = ruleSet.CoverageRadiusFt / 2.0;
                if (floor > 0 && roomW >= 2.0 * floor && roomH >= 2.0 * floor)
                {
                    if (res < floor) res = floor;
                }
            }

            if (res <= 0) res = 1.0;
            return res;
        }

        private static CeilingData SelectPrimaryCeiling(SmokeDetectorRoomInput room, out string reason)
        {
            reason = null;
            if (room == null || room.Ceilings == null || room.Ceilings.Count == 0) return null;

            double floorZ = room.LevelElevationFt;
            string roomLevelId = room.LevelId;

            List<CeilingData> candidates = new List<CeilingData>();
            foreach (CeilingData c in room.Ceilings)
            {
                if (c == null || !c.BottomElevationFt.HasValue) continue;
                if (c.BottomElevationFt.Value <= floorZ + 0.05) continue;
                candidates.Add(c);
            }

            if (candidates.Count == 0) return null;

            string[] slopePriority = { "FLAT", "SLOPED", "PEAKED", "STEPPED" };

            for (int pass = 0; pass < 2; pass++)
            {
                bool requireLevelMatch = (pass == 0);
                foreach (string slope in slopePriority)
                {
                    CeilingData best = null;
                    foreach (CeilingData c in candidates)
                    {
                        if (!string.Equals(c.SlopeType, slope, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (requireLevelMatch)
                        {
                            if (string.IsNullOrEmpty(c.LevelId)) continue;
                            if (!string.Equals(c.LevelId, roomLevelId, StringComparison.OrdinalIgnoreCase))
                                continue;
                        }
                        if (best == null || c.BottomElevationFt.Value > best.BottomElevationFt.Value)
                            best = c;
                    }

                    if (best != null)
                    {
                        reason = "Primary ceiling: slope=" + best.SlopeType
                            + ", BottomElevationFt=" + best.BottomElevationFt.Value.ToString("F2")
                            + ", levelMatch=" + requireLevelMatch + ".";
                        return best;
                    }
                }
            }

            CeilingData fallback = null;
            foreach (CeilingData c in candidates)
            {
                if (fallback == null || c.BottomElevationFt.Value > fallback.BottomElevationFt.Value)
                    fallback = c;
            }
            if (fallback != null)
            {
                reason = "Primary ceiling (fallback): slope=" + fallback.SlopeType
                    + ", BottomElevationFt=" + fallback.BottomElevationFt.Value.ToString("F2") + ".";
            }
            return fallback;
        }

        private static List<ObstacleBox> BuildObstacleBoxes(
            SmokeDetectorRoomInput room,
            SmokeDetectorPlacementRuleSet ruleSet,
            SmokeDetectorRoomCalculationResult result)
        {
            List<ObstacleBox> boxes = new List<ObstacleBox>();
            if (room.Obstacles == null) return boxes;

            foreach (ObstacleData obstacle in room.Obstacles)
            {
                if (obstacle == null) continue;

                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                bool haveBox = false;

                if (obstacle.BoundingBox != null
                    && obstacle.BoundingBox.Min != null
                    && obstacle.BoundingBox.Max != null)
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
                        "Obstacle '" + (obstacle.Name ?? obstacle.Category ?? "unknown")
                        + "' has no usable bounding box; skipped.");
                    continue;
                }

                double minZ = double.NaN;
                double maxZ = double.NaN;
                if (obstacle.BoundingBox != null
                    && obstacle.BoundingBox.Min != null
                    && obstacle.BoundingBox.Max != null)
                {
                    minZ = obstacle.BoundingBox.Min.Z;
                    maxZ = obstacle.BoundingBox.Max.Z;
                }

                // HVAC terminals get explicit supply-register clearance
                double clearance = ruleSet.GetObstacleClearance(obstacle.Category);
                if (!string.IsNullOrEmpty(obstacle.Category)
                    && obstacle.Category.IndexOf("DuctTerminal", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    clearance = Math.Max(clearance, ruleSet.HvacSupplyRegisterClearanceFt);
                }

                boxes.Add(new ObstacleBox
                {
                    MinX = minX,
                    MinY = minY,
                    MaxX = maxX,
                    MaxY = maxY,
                    MinZ = minZ,
                    MaxZ = maxZ,
                    Category = obstacle.Category ?? "obstacle",
                    ClearanceFt = clearance
                });
            }

            return boxes;
        }

        private static List<double[]> BuildExistingDetectorXy(SmokeDetectorRoomInput room)
        {
            List<double[]> points = new List<double[]>();
            if (room.ExistingDetectors == null) return points;

            foreach (Point3DData d in room.ExistingDetectors)
            {
                if (d != null)
                    points.Add(new double[] { d.X, d.Y });
            }
            return points;
        }

        private static void ApplyBeamAndSlopeAdjustments(
            SmokeDetectorRoomInput room,
            RoomGeometry geometry,
            SmokeDetectorPlacementRuleSet ruleSet,
            List<ObstacleBox> obstacleBoxes,
            double placementZ,
            SmokeDetectorRoomCalculationResult result)
        {
            if (room == null || geometry == null || ruleSet == null || result == null) return;

            bool isSloped = !string.Equals(ruleSet.CeilingSlope, "FLAT", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ruleSet.CeilingSlope, "NONE", StringComparison.OrdinalIgnoreCase);

            if (!isSloped && obstacleBoxes.Count == 0) return;

            if (isSloped)
            {
                ApplySlopedCeilingAdjustment(room, geometry, ruleSet, placementZ, result);
            }

            if (obstacleBoxes.Count > 0)
            {
                ApplyBeamAdjustment(room, geometry, ruleSet, obstacleBoxes, placementZ, result);
            }
        }

        private static void ApplySlopedCeilingAdjustment(
            SmokeDetectorRoomInput room,
            RoomGeometry geometry,
            SmokeDetectorPlacementRuleSet ruleSet,
            double placementZ,
            SmokeDetectorRoomCalculationResult result)
        {
            if (room.Ceilings == null || room.Ceilings.Count == 0) return;

            CeilingData slopedCeiling = null;
            foreach (CeilingData c in room.Ceilings)
            {
                if (c == null || !c.BottomElevationFt.HasValue || !c.TopElevationFt.HasValue) continue;
                if (string.Equals(c.SlopeType, "FLAT", StringComparison.OrdinalIgnoreCase)) continue;
                if (slopedCeiling == null || c.BottomElevationFt.Value > slopedCeiling.BottomElevationFt.Value)
                    slopedCeiling = c;
            }

            if (slopedCeiling == null) return;

            double bottomZ = slopedCeiling.BottomElevationFt.Value;
            double topZ = slopedCeiling.TopElevationFt.Value;
            double slopeHeight = topZ - bottomZ;
            if (slopeHeight < 0.01) return;

            bool isHighSlope = slopeHeight > 3.0 || (room.CeilingHeightFt.HasValue && room.CeilingHeightFt.Value > 10.0);

            if (isHighSlope)
            {
                double peakZ = topZ;
                double peakRowZ = peakZ - 3.0;
                if (peakRowZ < bottomZ) peakRowZ = bottomZ;

                result.Diagnostics.Add(
                    "Sloped ceiling peak-row rule (NFPA 72 17.7.3.4): first detector row within 3 ft of peak."
                    + " peakZ=" + peakZ.ToString("F2") + " ft, peakRowZ=" + peakRowZ.ToString("F2") + " ft.");

                ruleSet.MaxSpacingFt *= 0.90;
                ruleSet.CoverageRadiusFt = Math.Round(ruleSet.MaxSpacingFt / Math.Sqrt(2.0), 3);
                ruleSet.MaxDistanceFromWallsFt = Math.Round(ruleSet.MaxSpacingFt / 2.0, 2);
                result.Diagnostics.Add(
                    "Sloped ceiling spacing reduced: MaxSpacingFt=" + ruleSet.MaxSpacingFt.ToString("F2")
                    + " ft, CoverageRadiusFt=" + ruleSet.CoverageRadiusFt.ToString("F2") + " ft.");
            }
            else
            {
                result.Diagnostics.Add(
                    "Sloped ceiling (low slope): spacing uses peak height (top=" + topZ.ToString("F2")
                    + " ft, bottom=" + bottomZ.ToString("F2") + " ft).");
            }
        }

        private static void ApplyBeamAdjustment(
            SmokeDetectorRoomInput room,
            RoomGeometry geometry,
            SmokeDetectorPlacementRuleSet ruleSet,
            List<ObstacleBox> obstacleBoxes,
            double placementZ,
            SmokeDetectorRoomCalculationResult result)
        {
            List<ObstacleData> beams = new List<ObstacleData>();
            if (room.Obstacles != null)
            {
                foreach (ObstacleData o in room.Obstacles)
                {
                    if (o == null) continue;
                    string cat = o.Category ?? string.Empty;
                    if (cat.IndexOf("beam", StringComparison.OrdinalIgnoreCase) >= 0
                        || cat.IndexOf("joist", StringComparison.OrdinalIgnoreCase) >= 0
                        || cat.IndexOf("structural", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        beams.Add(o);
                    }
                }
            }

            if (beams.Count == 0) return;

            double ceilingHeight = room.CeilingHeightFt ?? (placementZ - room.LevelElevationFt);
            if (ceilingHeight <= 0.01) ceilingHeight = 10.0;

            double maxBeamDepth = 0.0;
            foreach (ObstacleData beam in beams)
            {
                double depth = EstimateBeamDepth(beam);
                if (depth > maxBeamDepth) maxBeamDepth = depth;
            }

            if (maxBeamDepth < 0.05)
            {
                result.Diagnostics.Add("Beams present but depth < 0.05 ft; treated as smooth ceiling.");
                return;
            }

            double ratio = maxBeamDepth / ceilingHeight;
            result.Diagnostics.Add(
                "Beam analysis: max depth=" + maxBeamDepth.ToString("F2") + " ft, ceiling height="
                + ceilingHeight.ToString("F2") + " ft, d/H=" + ratio.ToString("F3") + ".");

            if (ratio < 0.10)
            {
                result.Diagnostics.Add("Beam depth < 0.1H: smooth-ceiling spacing applies (NFPA 72 17.7.3.2.4).");
                return;
            }

            double beamSpacing = EstimateBeamSpacing(beams, geometry);
            result.Diagnostics.Add(
                "Beam spacing (estimated): " + beamSpacing.ToString("F2") + " ft.");

            if (beamSpacing >= 0.40 * ceilingHeight)
            {
                result.Diagnostics.Add(
                    "Beam spacing >= 0.4H: detectors required in every beam pocket (NFPA 72 17.7.3.2.4).");
                ruleSet.MaxSpacingFt *= 0.50;
                ruleSet.CoverageRadiusFt = Math.Round(ruleSet.MaxSpacingFt / Math.Sqrt(2.0), 3);
                ruleSet.MaxDistanceFromWallsFt = Math.Round(ruleSet.MaxSpacingFt / 2.0, 2);
                result.Diagnostics.Add(
                    "Beam-pocket spacing applied: MaxSpacingFt=" + ruleSet.MaxSpacingFt.ToString("F2") + " ft.");
            }
            else
            {
                result.Diagnostics.Add(
                    "Beam spacing < 0.4H: smooth spacing parallel to beams, 50% spacing perpendicular (NFPA 72 17.7.3.2.4).");
                ruleSet.MaxSpacingFt *= 0.50;
                ruleSet.CoverageRadiusFt = Math.Round(ruleSet.MaxSpacingFt / Math.Sqrt(2.0), 3);
                ruleSet.MaxDistanceFromWallsFt = Math.Round(ruleSet.MaxSpacingFt / 2.0, 2);
                result.Diagnostics.Add(
                    "Beam-perpendicular spacing applied: MaxSpacingFt=" + ruleSet.MaxSpacingFt.ToString("F2") + " ft.");
            }

            if (room.AreaSqFt <= 900.0)
            {
                result.Diagnostics.Add(
                    "Room area <= 900 sq ft: smooth-ceiling spacing exception applies (NFPA 72 17.7.3.2.4).");
                ruleSet.MaxSpacingFt = Math.Max(ruleSet.MaxSpacingFt, 30.0);
                ruleSet.CoverageRadiusFt = Math.Round(ruleSet.MaxSpacingFt / Math.Sqrt(2.0), 3);
                result.Diagnostics.Add(
                    "Smooth-ceiling exception restored: MaxSpacingFt=" + ruleSet.MaxSpacingFt.ToString("F2") + " ft.");
            }

            if (geometry.MaxX - geometry.MinX <= 15.0 || geometry.MaxY - geometry.MinY <= 15.0)
            {
                result.Diagnostics.Add(
                    "Corridor width <= 15 ft with perpendicular beams: smooth-ceiling spacing exception applies (NFPA 72 17.7.3.2.4).");
                ruleSet.MaxSpacingFt = Math.Max(ruleSet.MaxSpacingFt, 30.0);
                ruleSet.CoverageRadiusFt = Math.Round(ruleSet.MaxSpacingFt / Math.Sqrt(2.0), 3);
                result.Diagnostics.Add(
                    "Corridor exception restored: MaxSpacingFt=" + ruleSet.MaxSpacingFt.ToString("F2") + " ft.");
            }
        }

        private static double EstimateBeamDepth(ObstacleData beam)
        {
            if (beam == null) return 0.0;

            if (beam.BoundingBox != null && beam.BoundingBox.Min != null && beam.BoundingBox.Max != null)
            {
                double dz = Math.Abs(beam.BoundingBox.Max.Z - beam.BoundingBox.Min.Z);
                if (dz > 0.01) return dz;
            }

            if (beam.DimensionsFt != null)
            {
                double zDim = Math.Abs(beam.DimensionsFt.Z);
                if (zDim > 0.01) return zDim;

                double yDim = Math.Abs(beam.DimensionsFt.Y);
                double xDim = Math.Abs(beam.DimensionsFt.X);
                return Math.Max(yDim, xDim);
            }

            return 0.0;
        }

        private static double EstimateBeamSpacing(List<ObstacleData> beams, RoomGeometry geometry)
        {
            if (beams == null || beams.Count < 2 || geometry == null) return double.PositiveInfinity;

            List<double> centersX = new List<double>();
            List<double> centersY = new List<double>();

            foreach (ObstacleData beam in beams)
            {
                if (beam.BoundingBox != null && beam.BoundingBox.Min != null && beam.BoundingBox.Max != null)
                {
                    double cx = (beam.BoundingBox.Min.X + beam.BoundingBox.Max.X) / 2.0;
                    double cy = (beam.BoundingBox.Min.Y + beam.BoundingBox.Max.Y) / 2.0;
                    centersX.Add(cx);
                    centersY.Add(cy);
                }
                else if (beam.CenterPoint != null)
                {
                    centersX.Add(beam.CenterPoint.X);
                    centersY.Add(beam.CenterPoint.Y);
                }
            }

            if (centersX.Count < 2) return double.PositiveInfinity;

            centersX.Sort();
            centersY.Sort();

            double minGapX = double.PositiveInfinity;
            for (int i = 1; i < centersX.Count; i++)
            {
                double gap = centersX[i] - centersX[i - 1];
                if (gap < minGapX && gap > 0.01) minGapX = gap;
            }

            double minGapY = double.PositiveInfinity;
            for (int i = 1; i < centersY.Count; i++)
            {
                double gap = centersY[i] - centersY[i - 1];
                if (gap < minGapY && gap > 0.01) minGapY = gap;
            }

            double roomW = geometry.MaxX - geometry.MinX;
            double roomH = geometry.MaxY - geometry.MinY;

            if (minGapX < roomW * 0.9 && minGapX < minGapY) return minGapX;
            if (minGapY < roomH * 0.9 && minGapY < minGapX) return minGapY;

            return Math.Min(minGapX, minGapY);
        }

        private static SmokeDetectorPlacementRuleSet ApplyPerRoomOverrides(
            SmokeDetectorPlacementRuleSet baseRuleSet,
            SmokeDetectorRoomInput room,
            SmokeDetectorRoomCalculationResult result)
        {
            if (baseRuleSet == null) return new SmokeDetectorPlacementRuleSet();
            if (room == null) return baseRuleSet;
            if (!room.OverrideMaxSpacingFt.HasValue && !room.OverrideBoundaryClearanceFt.HasValue)
                return baseRuleSet;

            SmokeDetectorPlacementRuleSet effective = baseRuleSet.Clone();
            effective.IsProvisional = true;

            if (room.OverrideMaxSpacingFt.HasValue)
            {
                double requested = room.OverrideMaxSpacingFt.Value;
                double clamped = Math.Min(30.0, Math.Max(5.0, requested));
                effective.MaxSpacingFt = clamped;
                effective.CoverageRadiusFt = clamped / Math.Sqrt(2.0);
                effective.MaxDistanceFromWallsFt = clamped / 2.0;
                effective.MaxCoverageAreaSqFt = clamped * clamped;
                result.Diagnostics.Add(
                    "Override applied: MaxSpacingFt=" + clamped.ToString("F2")
                    + " ft (requested=" + requested.ToString("F2") + " ft).");
            }

            if (room.OverrideBoundaryClearanceFt.HasValue)
            {
                double requested = room.OverrideBoundaryClearanceFt.Value;
                double clamped = Math.Max(0.333, requested);
                effective.MinBoundaryClearanceFt = clamped;
                result.Diagnostics.Add(
                    "Override applied: MinBoundaryClearanceFt=" + clamped.ToString("F2") + " ft.");
            }

            if (result.Status == CalculationStatus.Success)
                result.Status = CalculationStatus.ReviewRequired;

            result.Warnings.Add(
                "Per-room detector spacing override applied. Engineering review required.");

            return effective;
        }
    }
}