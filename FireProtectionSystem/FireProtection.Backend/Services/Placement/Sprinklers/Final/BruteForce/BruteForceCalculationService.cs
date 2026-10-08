using System;
using System.Collections.Generic;
using System.Linq;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Hazard;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.Backend.Services.Placement.LocationPoints;
using FireProtection.UI.Models.Sprinklers.BruteForce;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce
{
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

            int overrideCount = 0;
            foreach (PlacementRoomInput r in snapshot.Rooms)
            {
                if (r == null) continue;
                if (r.OverrideMaxSpacingFt.HasValue || r.OverrideBoundaryClearanceFt.HasValue) overrideCount++;
            }
            if (overrideCount > 0)
            {
                result.AppliedRulesSummary += " " + overrideCount + " room(s) use per-room spacing/clearance overrides.";
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
                        SprinklerFamilyName = room?.SelectedSprinklerFamilyName,
                        SprinklerTypeName = room?.SelectedSprinklerTypeName,
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
                SprinklerFamilyName = room.SelectedSprinklerFamilyName,
                SprinklerTypeName = room.SelectedSprinklerTypeName,
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

            HazardClass hazardClass = ParseHazardClass(room.EffectiveHazardClass, result);
            HazardPlacementRuleSet baseRuleSet = rules.GetRules(hazardClass);
            HazardPlacementRuleSet catalogRuleSet = MergeTypeRuleValues(baseRuleSet, room, result);
            HazardPlacementRuleSet ruleSet = ApplyPerRoomOverrides(catalogRuleSet, room, result);

            if (ReferenceEquals(ruleSet, baseRuleSet))
            {
                ruleSet = baseRuleSet.Clone();
            }

            result.Polygon = outer;
            result.AppliedMaxSpacingFt = ruleSet.MaxSpacingFt;
            result.AppliedBoundaryClearanceFt = ruleSet.BoundaryClearanceFt;
            result.RulesApproved = rules.HasApprovedRules;

            if (string.Equals(room.SprinklerClass, "ESFR", StringComparison.OrdinalIgnoreCase)
                || string.Equals(room.SprinklerClass, "CMSA", StringComparison.OrdinalIgnoreCase)
                || ((hazardClass == HazardClass.EH1 || hazardClass == HazardClass.EH2)
                    && !room.TypeMaxCoverageAreaSqFt.HasValue))
            {
                result.Status = CalculationStatus.ReviewRequired;
                result.Warnings.Add((string.IsNullOrWhiteSpace(room.SprinklerClass) ? "Storage" : room.SprinklerClass)
                    + " layout requires storage-specific design review — geometry uses listed/provisional spacing only; verify minimum head count, K-factor, and pressure with an FPE.");
            }

            bool ceilingUnsupported = false;
            double placementZ = room.LevelElevationFt;
            string ceilingNote = null;

            CeilingData bestCeiling = SelectPrimaryCeiling(room, out string selectionReason, result);

            // ---------------------------------------------------------------------------------
            // Ceiling construction classification (NFPA 13 3.7.2, quoted in Ch.19 p.216)
            // ---------------------------------------------------------------------------------
            // Determines which deflector band applies and which obstruction rules are in force.
            // Must be resolved BEFORE placementZ, because the band depends on it.
            CeilingConstructionClass constructionClass =
                ClassifyCeilingConstruction(room, result);

            if (bestCeiling != null && bestCeiling.BottomElevationFt.HasValue)
            {
                if (string.Equals(bestCeiling.SlopeType, "SLOPED", System.StringComparison.OrdinalIgnoreCase)
                    && bestCeiling.TopElevationFt.HasValue
                    && bestCeiling.TopElevationFt.Value > bestCeiling.BottomElevationFt.Value)
                {
                    double avg = (bestCeiling.BottomElevationFt.Value + bestCeiling.TopElevationFt.Value) / 2.0;
                    if (result != null)
                    {
                        result.Diagnostics.Add(
                            $"Sloped ceiling Z averaged: bottom={bestCeiling.BottomElevationFt.Value:F2} ft, " +
                            $"top={bestCeiling.TopElevationFt.Value:F2} ft, average={avg:F2} ft.");
                    }
                    placementZ = avg;
                }
                else
                {
                    placementZ = bestCeiling.BottomElevationFt.Value;
                }
                if (result != null && !string.IsNullOrEmpty(selectionReason))
                {
                    result.Diagnostics.Add(selectionReason);
                }
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
            }

            // ---------------------------------------------------------------------------------
            // Deflector distance below the ceiling (Ch.19 p.225-226)
            // ---------------------------------------------------------------------------------
            // Previously heads were placed exactly ON the mounting plane (zero drop). The rulebook
            // requires a drop of at least 1 inch ("so that it can be removed from its fitting
            // without removing a piece of the ceiling") and no more than 12 inches unobstructed /
            // 6 inches obstructed below the structural members.
            //
            // The rulebook states a BAND, not a single value, so this picks the shallowest legal
            // drop: closest to the ceiling is both the most code-conservative for activation and the
            // smallest structural change from the previous behaviour.
            double maxDeflectorDrop = constructionClass == CeilingConstructionClass.Obstructed
                ? Nfpa13RulebookRules.DeflectorMaxDropObstructedFt
                : Nfpa13RulebookRules.DeflectorMaxDropUnobstructedFt;

            double appliedDrop = Nfpa13RulebookRules.DeflectorMinDropUnobstructedFt;
            if (appliedDrop > maxDeflectorDrop) appliedDrop = maxDeflectorDrop;

            if (!ceilingUnsupported)
            {
                placementZ -= appliedDrop;
            }

            double maxDropInches = maxDeflectorDrop * 12.0;
            result.Diagnostics.Add(
                $"Deflector drop: {appliedDrop * 12.0:F1} in below the mounting plane " +
                $"(band {Nfpa13RulebookRules.DeflectorMinDropUnobstructedFt * 12.0:F0}-{maxDropInches:F0} in, " +
                $"ceiling construction = {constructionClass}).");

            // Sloped-ceiling peak rule (Ch.19 p.229): the highest head must be within 3 ft of the peak.
            if (bestCeiling != null && bestCeiling.SlopeType != null
                && !string.Equals(bestCeiling.SlopeType, "FLAT", System.StringComparison.OrdinalIgnoreCase)
                && bestCeiling.TopElevationFt.HasValue)
            {
                double peakZ = bestCeiling.TopElevationFt.Value;
                double peakDrop = peakZ - placementZ;
                if (peakDrop > Nfpa13RulebookRules.PeakMaxDropFt)
                {
                    result.Warnings.Add(
                        $"Sloped-ceiling peak rule: the mounting plane is {peakDrop:F2} ft below the peak " +
                        $"(limit {Nfpa13RulebookRules.PeakMaxDropFt:F1} ft per Ch.19). Verify the highest " +
                        "head is within the limit, or apply the 2 ft structural-clearance exception.");
                    result.Status = CalculationStatus.ReviewRequired;
                }
                else
                {
                    result.Diagnostics.Add(
                        $"Peak rule satisfied: highest head is {peakDrop:F2} ft below the peak " +
                        $"(limit {Nfpa13RulebookRules.PeakMaxDropFt:F1} ft).");
                }
            }

            if (room.SelectedSprinklerPlacementBehavior == DevicePlacementBehavior.Unsupported)
            {
                result.Status = CalculationStatus.InvalidInput;
                result.CalculatedCount = 0;
                result.RequiredCount = 0;
                result.Errors.Add(
                    "The selected sprinkler family has an unsupported placement behavior (" +
                    room.SelectedSprinklerFamilyName + " / " + room.SelectedSprinklerTypeName +
                    "). Select a different family or extend the engine with a placement strategy.");
                return result;
            }

            if (ceilingUnsupported)
            {
                result.Status = CalculationStatus.ReviewRequired;
                bool hasSlopedOrStepped = (room.Ceilings != null) &&
                    room.Ceilings.Any(c => c != null &&
                        !string.Equals(c.SlopeType, "FLAT", StringComparison.OrdinalIgnoreCase));
                if (bestCeiling == null)
                {
                    result.Status = hasSlopedOrStepped
                        ? CalculationStatus.UnsupportedCeiling
                        : CalculationStatus.MissingCeiling;
                }
                result.Warnings.Add(ceilingNote ?? "Ceiling data requires review.");

                if (bestCeiling == null
                    && (room.Ceilings == null || room.Ceilings.Count == 0)
                    && RequiresCeilingHost(room.SelectedSprinklerPlacementBehavior))
                {
                    result.Status = CalculationStatus.MissingCeiling;
                    result.CalculatedCount = 0;
                    result.RequiredCount = 0;
                    result.Errors.Add(
                        "Room has no ceiling AND the selected sprinkler family requires " +
                        "a ceiling/host face (placement behavior '" + room.SelectedSprinklerPlacementBehavior +
                        "'). Calculation is blocked — add a ceiling to the room, or switch to a level-hosted family.");
                    return result;
                }
            }

            double ceilingHeightFt = room.CeilingHeightFt.HasValue
                ? room.CeilingHeightFt.Value
                : Math.Max(0, placementZ - room.LevelElevationFt);
            double heightFactor = 1.0;
            bool isLightHazard = ruleSet.HazardClass == HazardClass.Light;
            if (isLightHazard)
            {
                if (ceilingHeightFt > 25.0) heightFactor = 0.80;
                else if (ceilingHeightFt > 20.0) heightFactor = 0.85;
                else if (ceilingHeightFt > 15.0) heightFactor = 0.90;
                else if (ceilingHeightFt > 12.0) heightFactor = 0.95;
            }

            double slopeFactor = 1.0;
            if (bestCeiling != null && !string.Equals(bestCeiling.SlopeType, "FLAT", StringComparison.OrdinalIgnoreCase))
            {
                slopeFactor = ruleSet.GetCeilingSlopeAdjustment(bestCeiling.SlopeType);
                if (slopeFactor <= 0) slopeFactor = 1.0;
            }

            double combinedFactor = heightFactor * slopeFactor * ruleSet.CeilingHeightAdjustmentFactor;
            if (combinedFactor < 1.0)
            {
                double newMax = ruleSet.MaxSpacingFt * combinedFactor;
                if (newMax < ruleSet.MaxSpacingFt - 1e-9)
                {
                    result.Diagnostics.Add(
                        $"Ceiling-height adjustment applied: factor={combinedFactor:F3} (height={ceilingHeightFt:F2}ft, slope={slopeFactor:F2}, class={ruleSet.CeilingHeightAdjustmentFactor:F2}). " +
                        $"MaxSpacingFt: {ruleSet.MaxSpacingFt:F2} -> {newMax:F2} ft.");
                    ruleSet.MaxSpacingFt = newMax;
                    result.AppliedMaxSpacingFt = newMax;
                }
            }

            bool isSidewallOrientation = string.Equals(room.SelectedSprinklerOrientation, "sidewall", StringComparison.OrdinalIgnoreCase);
            double orientationFactor = ruleSet.GetOrientationAdjustment(room.SelectedSprinklerOrientation);
            if (orientationFactor > 0 && orientationFactor < 1.0)
            {
                double prevMax = ruleSet.MaxSpacingFt;
                double newMaxO = prevMax * orientationFactor;
                result.Diagnostics.Add(
                    $"Orientation adjustment applied: orientation='{room.SelectedSprinklerOrientation}', factor={orientationFactor:F3}. " +
                    $"MaxSpacingFt: {prevMax:F2} -> {newMaxO:F2} ft.");
                ruleSet.MaxSpacingFt = newMaxO;
                result.AppliedMaxSpacingFt = newMaxO;
            }
            if (isSidewallOrientation && ruleSet.CoverageRadiusFt > 0)
            {
                double prevCoverage = ruleSet.CoverageRadiusFt;
                double sidewallCoverage = prevCoverage / Math.Sqrt(2.0);
                result.Diagnostics.Add(
                    $"Sidewall coverage halved: CoverageRadiusFt: {prevCoverage:F2} -> {sidewallCoverage:F2} ft (half-circle spray pattern).");
                ruleSet.CoverageRadiusFt = sidewallCoverage;
            }

            List<ObstacleBox> obstacleBoxes = BuildObstacleBoxes(room, ruleSet, result);
            List<double[]> existingSprinklerXy = BuildExistingSprinklerXy(room);

            // Ch.19 p.223, Small Room Rule. Where it applies the max wall distance is relaxed to
            // 9 ft and coverage is computed by averaging instead of the S x L rules. Decided here
            // because it depends on hazard class, room area and construction class, all now resolved.
            bool smallRoomRuleApplied = TryApplySmallRoomRule(room, result, constructionClass, ruleSet);
            if (smallRoomRuleApplied)
            {
                ruleSet.MaxDistanceFromWallsFt = Math.Max(
                    Nfpa13RulebookRules.SmallRoomMaxWallDistanceFt, ruleSet.MaxDistanceFromWallsFt);
            }

            bool isSidewall =
                room.SelectedSprinklerPlacementBehavior == DevicePlacementBehavior.WallSidewall
                || string.Equals(room.SelectedSprinklerOrientation, "sidewall", StringComparison.OrdinalIgnoreCase);

            var sprinklerLocationProfile = DeviceLocationPointIdentifier.ResolveSprinklerProfile(
                room.SelectedSprinklerPlacementBehavior.ToString(),
                room.SelectedSprinklerOrientation,
                ruleSet.MaxSpacingFt,
                ruleSet.BoundaryClearanceFt,
                ruleSet.CoverageRadiusFt,
                placementZ,
                room.RoomId);

            if (isSidewall)
            {
                return SelectSidewallDirectional(
                    room, geometry, ruleSet, obstacleBoxes, existingSprinklerXy,
                    placementZ, config, result, ceilingUnsupported, ceilingNote);
            }

            CeilingGridMath.CeilingGrid tileGrid = CeilingGridMath.TryResolveRoomGrid(
                bestCeiling,
                room.OverrideCeilingTileUFt,
                room.OverrideCeilingTileVFt,
                geometry.MinX,
                geometry.MinY);

            if (tileGrid.IsValid)
            {
                result.Diagnostics.Add(
                    $"GRID: ACTIVE U={tileGrid.UFt:F2} V={tileGrid.VFt:F2} ft angle={tileGrid.AngleRad:F3} " +
                    $"origin=({tileGrid.OriginX:F2},{tileGrid.OriginY:F2}) " +
                    $"source={(bestCeiling != null && bestCeiling.HasReadableGrid ? "readable-pattern" : "user-tile-size")} " +
                    "-> heads snap to tile centers.");
            }
            else
            {
                string why = bestCeiling == null
                    ? "no ceiling associated with this room"
                    : (!bestCeiling.HasReadableGrid && !room.OverrideCeilingTileUFt.HasValue
                        ? $"ceiling '{bestCeiling.TypeName}' has no readable tile pattern AND no user tile size entered"
                        : "grid rejected or below min pitch");
                result.Diagnostics.Add("GRID: OFF (free/centered array) — " + why + ".");
            }

            double gridRes = ComputeGridResolution(geometry, ruleSet, config, out int gridPointEstimate);

            int generated = 0;
            int validCount = 0;
            int rejectedBoundary = 0;
            int rejectedObstacle = 0;
            int rejectedExisting = 0;
            int rejectedOutside = 0;
            int rejectedThreeTimesRule = 0;
            int rejectedBeamRule = 0;

            List<CandidatePoint> validCandidates = new List<CandidatePoint>();

            // Obstructions that sit BELOW the Chapter 20 zone plane (18 in under the sprinkler) are
            // governed by a different rule from those tight to the ceiling. Collected here so the
            // post-selection pass can report fixtures that need a head under them.
            List<ObstacleBox> belowZoneObstacles = new List<ObstacleBox>();

            bool TryAcceptPoint(double x, double y)
            {
                if (!geometry.IsPointInsideRoom(x, y, config.ToleranceFt))
                {
                    rejectedOutside++;
                    return false;
                }

                // Ch.19 p.220: the minimum distance to a wall is 4 inches. The rule-set value may
                // be larger (a design choice) but must never be smaller than the code minimum.
                double effectiveBoundary = Math.Max(ruleSet.BoundaryClearanceFt,
                    Nfpa13RulebookRules.MinWallClearanceFt);

                if (geometry.DistanceToOuterBoundary(x, y) < effectiveBoundary - config.ToleranceFt)
                {
                    rejectedBoundary++;
                    return false;
                }

                foreach (ObstacleBox box in obstacleBoxes)
                {
                    bool spansPlacement = box.SpansZ(placementZ, config.ToleranceFt);

                    // ---- Chapter 20, upper zone: the "Three Times" rule -------------------
                    // Ch.20 p.248: distance from the near edge of a vertical or open horizontal
                    // obstruction must be >= 3 x the obstruction's MAXIMUM dimension, capped at
                    // 24 in. Applies to columns and to open joist/truss bottom chords.
                    if (spansPlacement && box.IsStructuralMember
                        && IsThreeTimesRuleCategory(box.Category))
                    {
                        double required = Math.Min(
                            Nfpa13RulebookRules.ThreeTimesRuleFactor * box.MaxHorizontalDimensionFt,
                            Nfpa13RulebookRules.ThreeTimesRuleMaxRequiredDistanceFt);

                        double toEdge = box.DistanceToNearEdge(x, y);

                        // Exception (Ch.20 p.248): the rule can be ignored when sprinklers are
                        // installed on the OTHER side, provided they are within half the allowable
                        // spacing of the obstruction centreline. Existing sprinklers count too.
                        bool otherSideSprinklerWithinHalfSpacing = false;
                        double halfSpacing = ruleSet.MaxSpacingFt * Nfpa13RulebookRules.ThreeTimesExceptionHalfSpacing;
                        foreach (double[] es in existingSprinklerXy)
                        {
                            if (box.DistanceToCenterline(es[0], es[1]) <= halfSpacing)
                            {
                                otherSideSprinklerWithinHalfSpacing = true;
                                break;
                            }
                        }

                        if (!otherSideSprinklerWithinHalfSpacing && toEdge < required - config.ToleranceFt)
                        {
                            rejectedThreeTimesRule++;
                            return false;
                        }
                    }

                    // ---- Legacy flat clearance, retained for non-structural obstructions ----
                    // Ducts, pipes and cable trays have no Chapter 20 dimension-based rule in this
                    // design basis (see Nfpa13RulebookRules.KnownGaps), so they keep a flat
                    // clearance exclusion.
                    if (!box.IsStructuralMember
                        && GeometryMath.InsideExpandedBox(x, y, box.MinX, box.MinY, box.MaxX, box.MaxY, box.ClearanceFt)
                        && spansPlacement)
                    {
                        rejectedObstacle++;
                        return false;
                    }

                    // ---- Beam-rule candidate rejection ------------------------------------
                    // The Beam rule (NFPA 13 Table 8.6.5.1.2) clear-distance lookup is NOT in the
                    // rulebook, so no numeric distance is invented. Where a member is tight to the
                    // ceiling and continuous, the point is only rejected if it lies inside the
                    // member's own footprint (which is certainly wrong), and the room is flagged.
                    if (spansPlacement && box.IsStructuralMember
                        && GeometryMath.InsideExpandedBox(x, y, box.MinX, box.MinY, box.MaxX, box.MaxY, 0.0))
                    {
                        rejectedBeamRule++;
                        return false;
                    }

                    // ---- Chapter 20 lower zone bookkeeping ---------------------------------
                    if (!double.IsNaN(box.MaxZ) && box.MaxZ <= placementZ - Nfpa13RulebookRules.ObstructionZonePlaneBelowSprinklerFt)
                    {
                        if (!belowZoneObstacles.Contains(box)) belowZoneObstacles.Add(box);
                    }
                }

                foreach (double[] es in existingSprinklerXy)
                {
                    if (GeometryMath.Distance(x, y, es[0], es[1]) <= ruleSet.ExistingSprinklerSeparationFt - config.ToleranceFt)
                    {
                        rejectedExisting++;
                        return false;
                    }
                }

                validCandidates.Add(new CandidatePoint
                {
                    X = x,
                    Y = y,
                    Z = placementZ,
                    IsValid = true,
                    Score = 1.0
                });
                validCount++;
                return true;
            }

            if (tileGrid.IsValid)
            {
                List<double[]> tileCenters = CeilingGridMath.EnumerateTileCenters(
                    in tileGrid, geometry.MinX, geometry.MinY, geometry.MaxX, geometry.MaxY, config.ToleranceFt);

                foreach (double[] center in tileCenters)
                {
                    if (generated >= config.MaxCandidatePoints) break;
                    generated++;
                    TryAcceptPoint(center[0], center[1]);
                }
            }
            else
            {
                for (double y = geometry.MinY; y <= geometry.MaxY + config.ToleranceFt; y += gridRes)
                {
                    if (generated >= config.MaxCandidatePoints) break;
                    for (double x = geometry.MinX; x <= geometry.MaxX + config.ToleranceFt; x += gridRes)
                    {
                        if (generated >= config.MaxCandidatePoints) break;
                        generated++;
                        TryAcceptPoint(x, y);
                    }
                }
            }

            result.Diagnostics.Add(
                $"Candidates generated={generated}, valid={validCount}, " +
                $"rejected(outside={rejectedOutside}, boundary={rejectedBoundary}, obstacle={rejectedObstacle}, " +
                $"threeTimesRule={rejectedThreeTimesRule}, insideStructuralMember={rejectedBeamRule}, " +
                $"existing={rejectedExisting}).");

            ReportLowerZoneObstructions(belowZoneObstacles, ruleSet, result);

            if (validCandidates.Count == 0)
            {
                result.Status = CalculationStatus.NoValidCandidates;
                result.Errors.Add("No valid candidate locations found (all rejected by geometry/obstacles/existing sprinklers).");
                result.CalculatedCount = 0;
                result.RequiredCount = 0;
                return result;
            }

            return SelectCenteredGrid(
                validCandidates, room, geometry, ruleSet, obstacleBoxes,
                existingSprinklerXy, placementZ, gridRes, config, result,
                ceilingUnsupported, ceilingNote, tileGrid, smallRoomRuleApplied);
        }

        private static RoomCalculationResult SelectCenteredGrid(
            List<CandidatePoint> validCandidates,
            PlacementRoomInput room,
            RoomGeometry geometry,
            HazardPlacementRuleSet ruleSet,
            List<ObstacleBox> obstacleBoxes,
            List<double[]> existingSprinklerXy,
            double placementZ,
            double gridRes,
            BruteForceCalculationConfig config,
            RoomCalculationResult result,
            bool ceilingUnsupported,
            string ceilingNote,
            CeilingGridMath.CeilingGrid tileGrid,
            bool smallRoomRuleApplied)
        {
            double spacing = ruleSet.MaxSpacingFt;
            if (spacing <= 0)
                spacing = ruleSet.CoverageRadiusFt > 0 ? ruleSet.CoverageRadiusFt : 12.0;

            double minSpacingFt = ruleSet.MinSpacingFt > 0 ? ruleSet.MinSpacingFt : 0.0;

            if (tileGrid.IsValid)
            {
                return SelectCenteredGridOnTiles(
                    validCandidates, room, geometry, ruleSet, obstacleBoxes,
                    existingSprinklerXy, placementZ, config, result,
                    ceilingUnsupported, ceilingNote, tileGrid,
                    spacing: ruleSet.MaxSpacingFt > 0 ? ruleSet.MaxSpacingFt : 12.0,
                    minSpacingFt: ruleSet.MinSpacingFt > 0 ? ruleSet.MinSpacingFt : 0.0,
                    smallRoomRuleApplied: smallRoomRuleApplied);
            }

            double width = geometry.MaxX - geometry.MinX;
            double height = geometry.MaxY - geometry.MinY;

            int nx = width <= spacing + config.ToleranceFt
                ? 1 : (int)Math.Ceiling(width / spacing - config.ToleranceFt);
            int ny = height <= spacing + config.ToleranceFt
                ? 1 : (int)Math.Ceiling(height / spacing - config.ToleranceFt);
            if (nx < 1) nx = 1;
            if (ny < 1) ny = 1;
            double stepX = width / nx;
            double stepY = height / ny;

            double captureRadius = Math.Max(Math.Max(stepX, stepY) / 2.0, gridRes);

            validCandidates.Sort((a, b) =>
            {
                int byY = a.Y.CompareTo(b.Y);
                return byY != 0 ? byY : a.X.CompareTo(b.X);
            });
            bool[] used = new bool[validCandidates.Count];

            List<CalculatedSprinklerPoint> selected = new List<CalculatedSprinklerPoint>();
            int placedExact = 0, placedSnapped = 0, placedObstacleRecovered = 0, cellsSkipped = 0;

            for (int iy = 0; iy < ny; iy++)
            {
                double ty = geometry.MinY + stepY * (iy + 0.5);
                for (int ix = 0; ix < nx; ix++)
                {
                    double tx = geometry.MinX + stepX * (ix + 0.5);

                    // If target cell is already occupied/covered by an existing sprinkler,
                    // do not place a new sprinkler or snap near it.
                    bool blockedByExisting = false;
                    foreach (double[] es in existingSprinklerXy)
                    {
                        if (GeometryMath.Distance(tx, ty, es[0], es[1]) <= ruleSet.ExistingSprinklerSeparationFt - config.ToleranceFt)
                        {
                            blockedByExisting = true;
                            break;
                        }
                    }
                    if (blockedByExisting)
                    {
                        cellsSkipped++;
                        continue;
                    }

                    double rx, ry;
                    int snapIdx = -1;
                    bool obstacleRecovered = false;

                    if (IsPlacementValid(tx, ty, placementZ, geometry, ruleSet, obstacleBoxes, existingSprinklerXy, config))
                    {
                        rx = tx;
                        ry = ty;
                    }
                    else
                    {
                        double bestDist = double.PositiveInfinity;
                        for (int c = 0; c < validCandidates.Count; c++)
                        {
                            if (used[c]) continue;
                            double d = GeometryMath.Distance(tx, ty, validCandidates[c].X, validCandidates[c].Y);
                            if (d > captureRadius + config.ToleranceFt) continue;
                            if (d < bestDist - config.ToleranceFt)
                            {
                                bestDist = d;
                                snapIdx = c;
                            }
                        }

                        // ---------------------------------------------------------------
                        // Obstacle-recovery expanded search (Bug 2 fix)
                        // ---------------------------------------------------------------
                        // If the tight captureRadius search found nothing (because a physical
                        // obstacle like a beam/column/duct blocked the ideal target), scan ALL
                        // unused candidates within spacing/2. This is the mathematical upper bound
                        // that still keeps the resulting head within NFPA 13 MaxSpacingFt
                        // compliance: moving a head by at most S/2 can produce a gap of at
                        // most S + S/2 = 1.5S to its neighbour in the array, which the
                        // FinalizeSelection nearest-neighbour check will then flag as a
                        // warning rather than silently dropping the head.
                        if (snapIdx < 0)
                        {
                            double expandedRadius = spacing / 2.0;
                            bestDist = double.PositiveInfinity;
                            for (int c = 0; c < validCandidates.Count; c++)
                            {
                                if (used[c]) continue;
                                double d = GeometryMath.Distance(tx, ty, validCandidates[c].X, validCandidates[c].Y);
                                if (d > expandedRadius + config.ToleranceFt) continue;
                                if (d < bestDist - config.ToleranceFt)
                                {
                                    bestDist = d;
                                    snapIdx = c;
                                    obstacleRecovered = true;
                                }
                            }
                        }

                        if (snapIdx < 0)
                        {
                            cellsSkipped++;
                            continue;
                        }
                        rx = validCandidates[snapIdx].X;
                        ry = validCandidates[snapIdx].Y;
                    }

                    if (minSpacingFt > 0)
                    {
                        bool tooClose = false;
                        for (int s = 0; s < selected.Count; s++)
                        {
                            if (GeometryMath.Distance(rx, ry, selected[s].X, selected[s].Y) < minSpacingFt - config.ToleranceFt)
                            {
                                tooClose = true;
                                break;
                            }
                        }
                        if (tooClose)
                        {
                            cellsSkipped++;
                            continue;
                        }
                    }

                    if (snapIdx >= 0)
                    {
                        used[snapIdx] = true;
                        if (obstacleRecovered) placedObstacleRecovered++;
                        else placedSnapped++;
                    }
                    else
                    {
                        placedExact++;
                    }

                    selected.Add(new CalculatedSprinklerPoint
                    {
                        X = rx,
                        Y = ry,
                        Z = placementZ,
                        RoomId = room.RoomId,
                        LevelId = room.LevelId,
                        LevelName = room.LevelName
                    });
                }
            }

            result.Diagnostics.Add(
                $"Centered grid (free): S={spacing:F2} ft, nx={nx}, ny={ny}, stepX={stepX:F2}, stepY={stepY:F2}; " +
                $"placed={selected.Count} (exact={placedExact}, snapped={placedSnapped}, " +
                $"obstacleRecovered={placedObstacleRecovered}), skipped={cellsSkipped}.");


            if (selected.Count == 0)
            {
                result.Status = CalculationStatus.Failed;
                result.CalculatedCount = 0;
                result.RequiredCount = 0;
                result.Errors.Add(
                    "Centered-grid placement produced no sprinklers: every grid position was blocked " +
                    "and no valid fallback candidate was within reach.");
                return result;
            }

            return FinalizeSelection(
                selected, room, geometry, ruleSet, obstacleBoxes,
                existingSprinklerXy, config, result, ceilingUnsupported, ceilingNote, smallRoomRuleApplied);
        }

        private static RoomCalculationResult SelectCenteredGridOnTiles(
            List<CandidatePoint> validCandidates,
            PlacementRoomInput room,
            RoomGeometry geometry,
            HazardPlacementRuleSet ruleSet,
            List<ObstacleBox> obstacleBoxes,
            List<double[]> existingSprinklerXy,
            double placementZ,
            BruteForceCalculationConfig config,
            RoomCalculationResult result,
            bool ceilingUnsupported,
            string ceilingNote,
            CeilingGridMath.CeilingGrid tileGrid,
            double spacing,
            double minSpacingFt,
            bool smallRoomRuleApplied)
        {
double u = tileGrid.UFt;
            double v = tileGrid.VFt;

            // Largest whole number of tiles whose span still fits inside max spacing.
            int tileStepU = Math.Max(1, (int)Math.Floor((spacing + config.ToleranceFt) / u));
            int tileStepV = Math.Max(1, (int)Math.Floor((spacing + config.ToleranceFt) / v));

            if (minSpacingFt > 0)
            {
                int minStepU = Math.Max(1, (int)Math.Ceiling((minSpacingFt - config.ToleranceFt) / u));
                int minStepV = Math.Max(1, (int)Math.Ceiling((minSpacingFt - config.ToleranceFt) / v));
                if (minStepU > tileStepU) tileStepU = minStepU;
                if (minStepV > tileStepV) tileStepV = minStepV;
            }

            var validIndexSet = new HashSet<long>();
            int iuMin = int.MaxValue, iuMax = int.MinValue;
            int ivMin = int.MaxValue, ivMax = int.MinValue;

            foreach (CandidatePoint c in validCandidates)
            {
                CeilingGridMath.WorldToIndex(c.X, c.Y, in tileGrid, out int iu, out int iv);
                if (validIndexSet.Add(CeilingGridMath.IndexKey(iu, iv)))
                {
                    if (iu < iuMin) iuMin = iu;
                    if (iu > iuMax) iuMax = iu;
                    if (iv < ivMin) ivMin = iv;
                    if (iv > ivMax) ivMax = iv;
                }
            }

            if (validIndexSet.Count == 0)
            {
                result.Status = CalculationStatus.NoValidCandidates;
                result.Errors.Add("Tile-grid selection: no valid tile-center candidates inside the room.");
                result.CalculatedCount = 0;
                result.RequiredCount = 0;
                return result;
            }

            int tilesU = iuMax - iuMin + 1;
            int tilesV = ivMax - ivMin + 1;

            // ---------------------------------------------------------------------
            // Array extent (was Defect 4)
            // ---------------------------------------------------------------------
            // tileStep is a FLOOR of spacing/pitch, so the regular array always lands SHORT of
            // the room end whenever (tiles - 1) is not an exact multiple of the step. With a
            // 4 ft tile and 15 ft spacing the step is 3 tiles = 12 ft, so the array covered less
            // than the room and the uncovered tail was left entirely to the "nearest valid tile"
            // search. That is what made the unbounded search fire in practice, not rarely.
            //
            // The fix: size the array to COVER the extent instead of centring a too-short span
            // inside it, and let the bounded search below deal only with genuine local
            // obstructions (a beam or duct sitting on one tile centre).
            //
            // TILE-CENTRIC LAYOUT: when the pitch fits an X-2X-X balance, the START TILE is solved
            // for rather than anchored at the first tile of the room. Anchoring is what produced
            // visibly wrong wall gaps - e.g. a 20 ft room with 2 ft tiles and a 7-tile stride gives
            // tiles 0, 7, 9 and therefore gaps of 1 ft, 14 ft and 4 ft. The solver keeps ONE
            // whole-tile pitch and picks the start that makes the two wall gaps mirror each other
            // and sit closest to half the pitch.
            int nU = (int)Math.Ceiling((double)tilesU / tileStepU);
            int nV = (int)Math.Ceiling((double)tilesV / tileStepV);
            if (nU < 1) nU = 1;
            if (nV < 1) nV = 1;

            var uIndices = new List<int>(nU + 1);
            var vIndices = new List<int>(nV + 1);
            bool solvedBalanced = false;

            TileCentricPatternGenerator.AxisLayout solvedA;
            TileCentricPatternGenerator.AxisLayout solvedB;
            double solvedTolerance;
            if (TileCentricPatternGenerator.TrySolveBalancedLayout(
                    in tileGrid,
                    geometry.MinX, geometry.MinY, geometry.MaxX, geometry.MaxY,
                    tileStepU, tileStepV,
                    out solvedA, out solvedB, out solvedTolerance))
            {
                for (int i = 0; i < solvedA.Count; i++) uIndices.Add(solvedA.Start + i * solvedA.Pitch);
                for (int j = 0; j < solvedB.Count; j++) vIndices.Add(solvedB.Start + j * solvedB.Pitch);
                solvedBalanced = true;

                result.Diagnostics.Add(
                    "Tile grid: X-2X-X layout solved on the grid at tolerance " + solvedTolerance.ToString("0.0")
                    + " tile - " + solvedA.Count + "x" + solvedB.Count + ", pitch "
                    + (solvedA.Pitch * u).ToString("F2") + "x" + (solvedB.Pitch * v).ToString("F2")
                    + " ft, wall gaps " + solvedA.Gap1Ft.ToString("F2") + "/" + solvedA.Gap2Ft.ToString("F2")
                    + " and " + solvedB.Gap1Ft.ToString("F2") + "/" + solvedB.Gap2Ft.ToString("F2") + " ft.");
            }

            if (!solvedBalanced)
            {
                // No balanced start exists at either tolerance for this pitch, so fall back to the
                // previous behaviour: anchor at the start of the extent and cover it.
                int iU0 = iuMin;
                int iV0 = ivMin;

                // Rows/columns the regular stride cannot reach, appended so the far edge of the
                // room still gets a head rather than depending on a wide fallback search.
                for (int i = 0; i < nU; i++) uIndices.Add(iU0 + i * tileStepU);
                if (iU0 + (nU - 1) * tileStepU < iuMax) uIndices.Add(iuMax);

                for (int j = 0; j < nV; j++) vIndices.Add(iV0 + j * tileStepV);
                if (iV0 + (nV - 1) * tileStepV < ivMax) vIndices.Add(ivMax);

                result.Diagnostics.Add(
                    "Tile grid: no balanced X-2X-X start for a " + tileStepU + "x" + tileStepV
                    + "-tile pitch; anchored the array at the first tile instead.");
            }

            // ---------------------------------------------------------------------
            // Bounded snap budget (was Defect 1)
            // ---------------------------------------------------------------------
            // A head may leave its array target only as far as keeps the array legal:
            //   * never closer than min spacing to another head, and
            //   * never far enough that the resulting pair exceeds max spacing.
            //
            // The previous search scanned +/- tileStep tiles in BOTH axes with NO distance limit,
            // so a head whose target tile was blocked could be dragged arbitrarily far from where
            // the array wanted it. The result was a pair beyond MaxSpacingFt that only the
            // post-hoc pairwise check in FinalizeSelection could catch - i.e. the placer relied
            // on the checker to report its own violation.
            //
            // Budget is derived from the slack between the array pitch and the permitted spacing,
            // further capped at half a tile so a snap can never cross more than one tile line.
            double arrayPitchU = tileStepU * u;
            double arrayPitchV = tileStepV * v;
            double slackU = Math.Max(0.0, spacing - arrayPitchU);
            double slackV = Math.Max(0.0, spacing - arrayPitchV);
            double snapBudgetU = Math.Min(u * 0.5, slackU * 0.5 + config.ToleranceFt);
            double snapBudgetV = Math.Min(v * 0.5, slackV * 0.5 + config.ToleranceFt);
            double snapBudget = Math.Sqrt(snapBudgetU * snapBudgetU + snapBudgetV * snapBudgetV);

            var selected = new List<CalculatedSprinklerPoint>();
            int placed = 0;
            int placedSnapped = 0;
            int placedObstacleRecovered = 0;
            int skippedNoValidNeighbour = 0;
            int skippedMinSpacing = 0;
            int skippedExisting = 0;
            int skippedOutsideSnapBudget = 0;

            for (int j = 0; j < vIndices.Count; j++)
            {
                int ivTarget = vIndices[j];
                for (int i = 0; i < uIndices.Count; i++)
                {
                    int iuTarget = uIndices[i];
                    int iu = iuTarget;
                    int iv = ivTarget;

                    long key = CeilingGridMath.IndexKey(iu, iv);
                    bool moved = false;
                    bool obstacleRecovered = false;

                    if (!validIndexSet.Contains(key))
                    {
                        // Bounded search: the 8 immediate neighbours only. Anything further is not
                        // a "snap" but a different array position, and taking it is precisely the
                        // spacing violation this replaces.
                        int bestIu = 0, bestIv = 0;
                        double bestD = double.PositiveInfinity;
                        bool found = false;

                        for (int dv = -1; dv <= 1; dv++)
                        {
                            for (int du = -1; du <= 1; du++)
                            {
                                if (du == 0 && dv == 0) continue;
                                int tiu = iuTarget + du;
                                int tiv = ivTarget + dv;
                                if (!validIndexSet.Contains(CeilingGridMath.IndexKey(tiu, tiv))) continue;

                                double d = CeilingGridMath.IndexDistance(iuTarget, ivTarget, tiu, tiv, in tileGrid);
                                if (d > snapBudget + config.ToleranceFt) continue;
                                if (d < bestD - config.ToleranceFt)
                                {
                                    bestD = d;
                                    bestIu = tiu;
                                    bestIv = tiv;
                                    found = true;
                                }
                            }
                        }

                        if (!found)
                        {
                            // ---------------------------------------------------------------
                            // Obstacle-recovery expanded search (Bug 3 fix)
                            // ---------------------------------------------------------------
                            // If none of the 8 immediate neighbours is within snapBudget, try
                            // any valid tile within spacing/2 of the array target. The upper
                            // bound spacing/2 is derived from max-spacing mathematics: moving
                            // the head by at most S/2 can widen its gap to the next array
                            // position by at most S/2, giving a worst-case distance of
                            // S + S/2 = 1.5S — which FinalizeSelection's nearest-neighbour
                            // check then flags as a warning rather than silently dropping the
                            // head. Search range is capped at extRange tiles in each axis to
                            // avoid O(all tiles) for large rooms.
                            int extRange = Math.Max(tileStepU, tileStepV) + 1;
                            double maxRecoveryDist = Math.Max(spacing, ruleSet.MaxSpacingFt > 0 ? ruleSet.MaxSpacingFt : spacing) / 2.0;

                            for (int dv2 = -extRange; dv2 <= extRange && !found; dv2++)
                            {
                                for (int du2 = -extRange; du2 <= extRange; du2++)
                                {
                                    // Skip the 8 immediate neighbours already searched above.
                                    if (Math.Abs(du2) <= 1 && Math.Abs(dv2) <= 1) continue;
                                    int tiu2 = iuTarget + du2;
                                    int tiv2 = ivTarget + dv2;
                                    if (!validIndexSet.Contains(CeilingGridMath.IndexKey(tiu2, tiv2))) continue;
                                    double d2 = CeilingGridMath.IndexDistance(iuTarget, ivTarget, tiu2, tiv2, in tileGrid);
                                    if (d2 > maxRecoveryDist + config.ToleranceFt) continue;
                                    if (d2 < bestD - config.ToleranceFt)
                                    {
                                        bestD = d2;
                                        bestIu = tiu2;
                                        bestIv = tiv2;
                                        found = true;
                                        obstacleRecovered = true;
                                    }
                                }
                            }
                        }

                        if (!found)
                        {
                            skippedNoValidNeighbour++;
                            continue;
                        }

                        iu = bestIu;
                        iv = bestIv;
                        moved = true;
                        key = CeilingGridMath.IndexKey(iu, iv);
                    }

                    // Consume the tile so two array cells can never claim the same one.
                    if (!validIndexSet.Remove(key))
                    {
                        skippedNoValidNeighbour++;
                        continue;
                    }

                    CeilingGridMath.IndexToWorld(iu, iv, in tileGrid, out double wx, out double wy);

                    if (minSpacingFt > 0)
                    {
                        bool tooClose = false;
                        for (int s = 0; s < selected.Count; s++)
                        {
                            if (GeometryMath.Distance(wx, wy, selected[s].X, selected[s].Y)
                                < minSpacingFt - config.ToleranceFt)
                            {
                                tooClose = true;
                                break;
                            }
                        }
                        if (tooClose)
                        {
                            skippedMinSpacing++;
                            continue;
                        }
                    }

                    bool hitExisting = false;
                    foreach (double[] es in existingSprinklerXy)
                    {
                        if (GeometryMath.Distance(wx, wy, es[0], es[1])
                            <= ruleSet.ExistingSprinklerSeparationFt - config.ToleranceFt)
                        {
                            hitExisting = true;
                            break;
                        }
                    }
                    if (hitExisting)
                    {
                        skippedExisting++;
                        continue;
                    }

                    selected.Add(new CalculatedSprinklerPoint
                    {
                        X = wx,
                        Y = wy,
                        Z = placementZ,
                        RoomId = room.RoomId,
                        LevelId = room.LevelId,
                        LevelName = room.LevelName
                    });
                    placed++;
                    if (moved)
                    {
                        if (obstacleRecovered) placedObstacleRecovered++;
                        else placedSnapped++;
                    }
                }
            }

            // Skip reasons are reported SEPARATELY. "too close to another new head" and "too
            // close to a pre-existing sprinkler" are different problems with different fixes;
            // collapsing them into one bucket (as this method previously did) told the user
            // nothing actionable.
            result.Diagnostics.Add(
                $"Centered grid (TILES): S={spacing:F2} ft, tile={u:F2}x{v:F2}, stepTiles=({tileStepU},{tileStepV}), " +
                $"arrayPitch={arrayPitchU:F2}x{arrayPitchV:F2} ft, extent=U[{iuMin}..{iuMax}] V[{ivMin}..{ivMax}] " +
                $"({tilesU}x{tilesV} tiles), array={uIndices.Count}x{vIndices.Count}, snapBudget={snapBudget:F2} ft, " +
                $"placed={placed} (snapped={placedSnapped}, obstacleRecovered={placedObstacleRecovered}), " +
                $"skipped(noValidNeighbour={skippedNoValidNeighbour}, minSpacing={skippedMinSpacing}, " +
                $"existingSprinkler={skippedExisting}, outsideSnapBudget={skippedOutsideSnapBudget}).");

            if (selected.Count == 0)
            {
                result.Status = CalculationStatus.Failed;
                result.CalculatedCount = 0;
                result.RequiredCount = 0;
                result.Errors.Add(
                    "Tile-centered grid produced no sprinklers. Review room geometry, tile pitch vs spacing, and obstacles.");
                return result;
            }

            return FinalizeSelection(
                selected, room, geometry, ruleSet, obstacleBoxes,
                existingSprinklerXy, config, result, ceilingUnsupported, ceilingNote, smallRoomRuleApplied);
        }

        private static bool IsPlacementValid(
            double x, double y, double placementZ,
            RoomGeometry geometry,
            HazardPlacementRuleSet ruleSet,
            List<ObstacleBox> obstacleBoxes,
            List<double[]> existingSprinklerXy,
            BruteForceCalculationConfig config)
        {
            if (!geometry.IsPointInsideRoom(x, y, config.ToleranceFt)) return false;

            // Ch.19 p.220: the minimum distance to a wall is 4 inches. Match TryAcceptPoint.
            double effectiveBoundary = Math.Max(ruleSet.BoundaryClearanceFt,
                Nfpa13RulebookRules.MinWallClearanceFt);
            if (geometry.DistanceToOuterBoundary(x, y) < effectiveBoundary - config.ToleranceFt) return false;

            foreach (ObstacleBox box in obstacleBoxes)
            {
                bool spansPlacement = box.SpansZ(placementZ, config.ToleranceFt);

                // ---- Chapter 20: Three Times Rule (structural members) --------
                // Mirror TryAcceptPoint exactly. No opposing-sprinkler exception
                // check here (existing sprinklers already placed are in existingSprinklerXy;
                // newly-selected heads are not yet placed so conservative is correct).
                if (spansPlacement && box.IsStructuralMember
                    && IsThreeTimesRuleCategory(box.Category))
                {
                    double required = Math.Min(
                        Nfpa13RulebookRules.ThreeTimesRuleFactor * box.MaxHorizontalDimensionFt,
                        Nfpa13RulebookRules.ThreeTimesRuleMaxRequiredDistanceFt);

                    double toEdge = box.DistanceToNearEdge(x, y);

                    // Opposing-sprinkler exception (Ch.20 p.248): existing sprinklers on
                    // the other side within half the allowable spacing of the centreline.
                    bool otherSideSprinklerWithinHalfSpacing = false;
                    double halfSpacing = ruleSet.MaxSpacingFt * Nfpa13RulebookRules.ThreeTimesExceptionHalfSpacing;
                    foreach (double[] es in existingSprinklerXy)
                    {
                        if (box.DistanceToCenterline(es[0], es[1]) <= halfSpacing)
                        {
                            otherSideSprinklerWithinHalfSpacing = true;
                            break;
                        }
                    }

                    if (!otherSideSprinklerWithinHalfSpacing && toEdge < required - config.ToleranceFt)
                        return false;
                }

                // ---- Flat clearance for non-structural obstacles ---------------
                if (!box.IsStructuralMember
                    && GeometryMath.InsideExpandedBox(x, y, box.MinX, box.MinY, box.MaxX, box.MaxY, box.ClearanceFt)
                    && spansPlacement)
                    return false;

                // ---- Beam rule: reject if inside member footprint --------------
                if (spansPlacement && box.IsStructuralMember
                    && GeometryMath.InsideExpandedBox(x, y, box.MinX, box.MinY, box.MaxX, box.MaxY, 0.0))
                    return false;
            }

            foreach (double[] es in existingSprinklerXy)
            {
                if (GeometryMath.Distance(x, y, es[0], es[1]) <= ruleSet.ExistingSprinklerSeparationFt - config.ToleranceFt)
                    return false;
            }

            return true;
        }

        private static RoomCalculationResult FinalizeSelection(
            List<CalculatedSprinklerPoint> selected,
            PlacementRoomInput room,
            RoomGeometry geometry,
            HazardPlacementRuleSet ruleSet,
            List<ObstacleBox> obstacleBoxes,
            List<double[]> existingSprinklerXy,
            BruteForceCalculationConfig config,
            RoomCalculationResult result,
            bool ceilingUnsupported,
            string ceilingNote,
            bool smallRoomRuleApplied)
        {
            // ---------------------------------------------------------------------
            // Max-spacing check (was Defect 9 - a false positive on virtually every room)
            // ---------------------------------------------------------------------
            // This previously compared EVERY pair of placed heads and warned whenever the
            // distance exceeded MaxSpacingFt. That is geometrically wrong for an array layout:
            // two DIAGONALLY adjacent heads are always further apart than the array pitch
            // (for a 15 x 15 ft array the diagonal is 21.2 ft against a 15 ft limit), so every
            // room with a 2x2-or-larger array produced a spurious "exceeding MaxSpacingFt"
            // warning and was flagged ReviewRequired regardless of how good the layout was.
            //
            // NFPA max spacing governs the ARRAY PITCH - the spacing to the heads a given head
            // actually serves. The correct test is therefore per-head distance to its NEAREST
            // other head: if a head's closest neighbour is further away than MaxSpacingFt, there
            // is a real local gap at that head. A dragged or orphaned head is caught by exactly
            // this test; a healthy array never triggers it.
            if (ruleSet.MaxSpacingFt > 0 && selected.Count > 1)
            {
                double maxSpacing = ruleSet.MaxSpacingFt;
                int strandedHeads = 0;
                double worstLocalGap = 0.0;
                int worstHeadIndex = -1;

                for (int i = 0; i < selected.Count; i++)
                {
                    double nearest = double.PositiveInfinity;
                    for (int j = 0; j < selected.Count; j++)
                    {
                        if (i == j) continue;
                        double d = GeometryMath.Distance(selected[i].X, selected[i].Y, selected[j].X, selected[j].Y);
                        if (d < nearest) nearest = d;
                    }

                    if (nearest > maxSpacing + config.ToleranceFt)
                    {
                        strandedHeads++;
                        if (nearest > worstLocalGap)
                        {
                            worstLocalGap = nearest;
                            worstHeadIndex = i;
                        }
                    }
                }

                if (strandedHeads > 0)
                {
                    result.Warnings.Add(
                        $"Spacing gap: {strandedHeads} of {selected.Count} placed head(s) have no neighbour " +
                        $"within MaxSpacingFt={maxSpacing:F2} ft. Worst: head #{worstHeadIndex + 1} at " +
                        $"({selected[worstHeadIndex].X:F2},{selected[worstHeadIndex].Y:F2}) is " +
                        $"{worstLocalGap:F2} ft from its nearest head. Review required.");
                    result.Status = CalculationStatus.ReviewRequired;
                }
            }

            if (ruleSet.MaxDistanceFromWallsFt > 0 && selected.Count > 0)
            {
                double maxWall = ruleSet.MaxDistanceFromWallsFt;
                for (int i = 0; i < selected.Count; i++)
                {
                    double d = geometry.DistanceToOuterBoundary(selected[i].X, selected[i].Y);
                    if (d > maxWall + config.ToleranceFt)
                    {
                        result.Warnings.Add(
                            $"Placed sprinkler {i + 1} is {d:F2} ft from the nearest wall, " +
                            $"exceeding MaxDistanceFromWallsFt={maxWall:F2} ft. Review required.");
                        result.Status = CalculationStatus.ReviewRequired;
                    }
                }
            }

            // ---------------------------------------------------------------------
            // Per-head coverage area — S x L rules (Ch.19 p.222)
            // ---------------------------------------------------------------------
            // Rulebook: "Each sprinkler needs to be considered separately. When considering the
            // sprinkler, look at the distance between it and the adjacent sprinklers on the same
            // branch line. For sprinklers next to a wall, also look at twice the distance to the
            // wall. The dimension 'S' is given to the greatest of the distances to the next
            // adjacent sprinklers or twice the distance to the wall. Similarly, consider the
            // sprinklers on adjacent branch lines and twice the distance to the wall in this
            // direction. The dimension 'L' is given to the greatest distance between sprinklers on
            // adjacent branch lines or twice the distance to the wall. The area of coverage for that
            // sprinkler will be S x L."
            //
            // This replaces the previous room-level roomArea / headCount estimate, which said
            // nothing about how coverage is actually distributed: a room can average out fine while
            // one head is grossly over-loaded next to a wall.
            //
            // Branch lines are laid along the room's LONGER axis, which is the normal design
            // convention and the orientation that uses fewer heads.
            if (smallRoomRuleApplied)
            {
                // Ch.19 p.224: where the Small Room Rule is used, NFPA 13 requires the averaging
                // technique instead of the S x L rules: "take the total area of the room and divide
                // by the total number of sprinklers in the room."
                if (room.AreaSqFt > 0 && selected.Count > 0)
                {
                    double averaged = room.AreaSqFt / selected.Count;
                    result.Diagnostics.Add(
                        $"Coverage area (Small Room Rule averaging): room {room.AreaSqFt:F0} sq ft / "
                        + $"{selected.Count} head(s) = {averaged:F1} sq ft per head; limit "
                        + $"{ruleSet.MaxCoverageAreaSqFt:F0} sq ft.");

                    if (averaged > ruleSet.MaxCoverageAreaSqFt + 1e-6)
                    {
                        result.Warnings.Add(
                            $"Averaged coverage (Small Room Rule) is {averaged:F1} sq ft per head, above the " +
                            $"{ruleSet.MaxCoverageAreaSqFt:F1} sq ft limit for {ruleSet.HazardClass}. Review required.");
                        result.Status = CalculationStatus.ReviewRequired;
                    }
                }
            }
            else if (ruleSet.MaxCoverageAreaSqFt > 0 && selected.Count > 0)
            {
                CheckCoverageBySxL(selected, geometry, ruleSet, config, result);
            }

            // ---------------------------------------------------------------------
            // Coverage / remote-distance ADVISORY (was Defects 5 + 6)
            // ---------------------------------------------------------------------
            // Two defects used to cancel each other out here and neither was visible:
            //
            //  (6) the threshold was ruleSet.CoverageRadiusFt, which every hazard class set to
            //      MaxSpacingFt / 2. For a rectangular array that value is only correct along the
            //      array EDGES; the true worst case is S / sqrt(2) at a corner or array centre.
            //      Testing a perfectly good centred array against S/2 therefore reported a gap in
            //      EVERY room (false positive).
            //
            //  (5) the reported gap only escalated the room when more than 5% of samples were
            //      uncovered, so a genuinely sparse layout slipped through silently.
            //
            // The pair cancelled, which is why it was never noticed: the too-tight radius
            // over-reported and the 5% escape hatch suppressed. Both are corrected here.
            //
            // Per the agreed product decision this is ADVISORY ONLY. It reports; it never fails
            // the room. Status is left to the spacing, wall-distance and per-head-area checks
            // above, which are the ones with an unambiguous code basis.
            bool isSidewallOrientation = string.Equals(
                room.SelectedSprinklerOrientation, "sidewall", StringComparison.OrdinalIgnoreCase);

            if (isSidewallOrientation)
            {
                // A sidewall head sprays a half-disc into the room. This sampler walks the whole
                // room rectangle and would demand the far corner be within a HALVED radius, which
                // a half-disc can never satisfy - so it would report a large false gap on every
                // sidewall room. The along-wall spacing and wall-distance checks above already
                // cover this case, so the check is skipped rather than faked.
                result.Diagnostics.Add(
                    "Coverage sampler: SKIPPED for sidewall orientation (half-disc spray is not " +
                    "modelled by a 2D radius test; along-wall spacing + wall distance apply instead).");
            }
            else
            {
                double geometricRadius = ruleSet.EffectiveCoverageRadiusFt;
                if (geometricRadius > 0 && selected.Count > 0)
                {
                    // Sample on a grid fine enough to see the worst case at an array corner.
                    double sampleStep = Math.Max(Math.Min(geometricRadius, ruleSet.MaxSpacingFt) / 3.0, 0.5);

                    int samples = 0;
                    int beyondGeometric = 0;
                    double worstGap = 0.0;
                    double worstX = 0.0;
                    double worstY = 0.0;

                    for (double sy = geometry.MinY; sy <= geometry.MaxY + config.ToleranceFt; sy += sampleStep)
                    {
                        for (double sx = geometry.MinX; sx <= geometry.MaxX + config.ToleranceFt; sx += sampleStep)
                        {
                            if (!geometry.IsPointInsideRoom(sx, sy, config.ToleranceFt)) continue;

                            // A floor sample under an overhead obstruction is not "uncovered" in the
                            // sense this check measures; the obstruction rules own that case.
                            double placementZForGapCheck = selected[0].Z;
                            bool insideObstacle = false;
                            foreach (ObstacleBox box in obstacleBoxes)
                            {
                                if (!box.SpansZ(placementZForGapCheck, config.ToleranceFt)) continue;
                                if (GeometryMath.InsideExpandedBox(sx, sy, box.MinX, box.MinY, box.MaxX, box.MaxY, 0.0))
                                {
                                    insideObstacle = true; break;
                                }
                            }
                            if (insideObstacle) continue;

                            samples++;

                            double nearest = double.PositiveInfinity;
                            foreach (CalculatedSprinklerPoint s in selected)
                            {
                                double d = GeometryMath.Distance(sx, sy, s.X, s.Y);
                                if (d < nearest) nearest = d;
                            }
                            // Pre-existing sprinklers contribute their own coverage, so including
                            // them is correct rather than generous.
                            foreach (double[] es in existingSprinklerXy)
                            {
                                double d = GeometryMath.Distance(sx, sy, es[0], es[1]);
                                if (d < nearest) nearest = d;
                            }

                            if (nearest > geometricRadius - config.ToleranceFt)
                            {
                                beyondGeometric++;
                                if (nearest > worstGap)
                                {
                                    worstGap = nearest;
                                    worstX = sx;
                                    worstY = sy;
                                }
                            }
                        }
                    }

                    if (samples > 0)
                    {
                        double pct = (double)beyondGeometric * 100.0 / samples;
                        double remoteLimit = ruleSet.RemoteDistanceLimitFt;
                        double listingRadius = ruleSet.CoverageRadiusFt;

                        result.Diagnostics.Add(
                            $"Coverage check (advisory): {beyondGeometric} of {samples} floor samples " +
                            $"({pct:F1}%) exceed the array geometric radius {geometricRadius:F2} ft " +
                            $"(= S/sqrt2, S={ruleSet.MaxSpacingFt:F2} ft). Worst point ({worstX:F2},{worstY:F2}) " +
                            $"at {worstGap:F2} ft from the nearest head. " +
                            $"NFPA remote-distance limit 0.7S = {remoteLimit:F2} ft; " +
                            $"head listing radius = {listingRadius:F2} ft.");

                        // One concise warning. Previously this fired with no location when the gap
                        // was geometrically expected, which trained users to ignore it.
                        if (beyondGeometric > 0)
                        {
                            result.Warnings.Add(
                                $"Coverage advisory: {beyondGeometric}/{samples} floor points ({pct:F1}%) " +
                                $"are beyond the {geometricRadius:F2} ft array radius; worst at " +
                                $"({worstX:F2},{worstY:F2}) = {worstGap:F2} ft " +
                                $"(0.7S limit {remoteLimit:F2} ft). No action taken — verify manually.");
                        }
                    }
                }
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

        private sealed class SidewallHead
        {
            public double X;
            public double Y;
            public int WallEdgeIndex;
            public double AlongX, AlongY;
            public double InX, InY;
        }

        private static RoomCalculationResult SelectSidewallDirectional(
            PlacementRoomInput room,
            RoomGeometry geometry,
            HazardPlacementRuleSet ruleSet,
            List<ObstacleBox> obstacleBoxes,
            List<double[]> existingSprinklerXy,
            double placementZ,
            BruteForceCalculationConfig config,
            RoomCalculationResult result,
            bool ceilingUnsupported,
            string ceilingNote)
        {
            List<double[]> polygon = ExtractOuterPolygon(room);
            polygon = CleanPolygonLoop(polygon);
            if (polygon == null || polygon.Count < 3)
            {
                result.Status = CalculationStatus.InvalidRoomGeometry;
                result.Errors.Add("Sidewall solver: room polygon is degenerate (need at least 3 vertices).");
                return result;
            }

            double alongSpacing = ruleSet.MaxSpacingFt > 0 ? ruleSet.MaxSpacingFt : 10.0;
            double hazardCeiling = GetNfpa13MaxSpacingCeiling(ruleSet.HazardClass);
            double throwDepth;
            bool throwAssumedSquare = false;
            if (room.TypeMaxCoverageAreaSqFt.HasValue && room.TypeMaxCoverageAreaSqFt.Value > 0 && alongSpacing > 0)
            {
                throwDepth = Math.Min(room.TypeMaxCoverageAreaSqFt.Value / alongSpacing, hazardCeiling);
            }
            else
            {
                throwDepth = alongSpacing;
                throwAssumedSquare = true;
            }
            if (throwDepth <= 0) throwDepth = alongSpacing;

            double endWallMax = alongSpacing * 0.5;
            double standOff = 0.5;

            result.Diagnostics.Add(
                $"Sidewall directional solver: alongSpacing={alongSpacing:F2} ft, throwDepth={throwDepth:F2} ft, "
                + $"endWallMax={endWallMax:F2} ft (derived S/2), standOff={standOff:F2} ft.");

            bool isCCW = ComputeSignedArea(polygon) > 0;
            List<List<SidewallHead>> rowsPerEdge = new List<List<SidewallHead>>();
            double[] edgeLenArr = new double[polygon.Count];
            double[] edgeUx = new double[polygon.Count];
            double[] edgeUy = new double[polygon.Count];
            double[] edgeInX = new double[polygon.Count];
            double[] edgeInY = new double[polygon.Count];

            for (int i = 0; i < polygon.Count; i++)
            {
                double[] a = polygon[i];
                double[] b = polygon[(i + 1) % polygon.Count];
                List<SidewallHead> row = new List<SidewallHead>();
                rowsPerEdge.Add(row);
                if (a == null || b == null || a.Length < 2 || b.Length < 2) continue;

                double ex = b[0] - a[0], ey = b[1] - a[1];
                double edgeLen = Math.Sqrt(ex * ex + ey * ey);
                if (edgeLen < 0.33) continue;
                double ux = ex / edgeLen, uy = ey / edgeLen;
                edgeLenArr[i] = edgeLen;
                edgeUx[i] = ux;
                edgeUy[i] = uy;

                double nx = isCCW ? -uy : uy;
                double ny = isCCW ? ux : -ux;
                double midX = (a[0] + b[0]) * 0.5, midY = (a[1] + b[1]) * 0.5;
                bool inward = false;
                foreach (double step in new[] { 0.1, 0.25, 0.5 })
                {
                    if (geometry.IsPointInsideRoom(midX + nx * step, midY + ny * step, config.ToleranceFt))
                    { inward = true; break; }
                }
                if (!inward) { nx = -nx; ny = -ny; }
                edgeInX[i] = nx;
                edgeInY[i] = ny;

                double minSpacing = ruleSet.MinSpacingFt > 0 ? ruleSet.MinSpacingFt : 0.0;

                List<double> dists = new List<double>();
                if (edgeLen <= 2.0 * endWallMax)
                {
                    dists.Add(edgeLen * 0.5);
                }
                else
                {
                    double span = edgeLen - 2.0 * endWallMax;
                    int segs = Math.Max(1, (int)Math.Ceiling(span / alongSpacing));
                    double step = span / segs;
                    if (segs >= 1 && step < minSpacing)
                    {
                        dists.Add(edgeLen * 0.5);
                    }
                    else
                    {
                        for (int k = 0; k <= segs; k++) dists.Add(endWallMax + k * step);
                    }
                }

                foreach (double d in dists)
                {
                    double bx = a[0] + ux * d, by = a[1] + uy * d;
                    double cx = bx + nx * standOff, cy = by + ny * standOff;
                    if (!geometry.IsPointInsideRoom(cx, cy, config.ToleranceFt)) continue;

                    bool blocked = false;
                    foreach (ObstacleBox box in obstacleBoxes)
                    {
                        if (!box.SpansZ(placementZ, config.ToleranceFt)) continue;

                        if (GeometryMath.InsideExpandedBox(cx, cy, box.MinX, box.MinY, box.MaxX, box.MaxY, 0.0))
                        { blocked = true; break; }

                        double boxCx = (box.MinX + box.MaxX) * 0.5;
                        double boxCy = (box.MinY + box.MaxY) * 0.5;
                        double towardBox = (boxCx - cx) * nx + (boxCy - cy) * ny;
                        if (towardBox <= config.ToleranceFt) continue;

                        if (GeometryMath.InsideExpandedBox(cx, cy, box.MinX, box.MinY, box.MaxX, box.MaxY, box.ClearanceFt))
                        { blocked = true; break; }
                    }
                    if (blocked) continue;

                    row.Add(new SidewallHead
                    {
                        X = cx,
                        Y = cy,
                        WallEdgeIndex = i,
                        AlongX = ux,
                        AlongY = uy,
                        InX = nx,
                        InY = ny
                    });
                }
            }

            double sampleStep = Math.Max(Math.Min(alongSpacing, throwDepth) / 3.0, 0.5);
            List<double[]> samples = new List<double[]>();
            for (double sy = geometry.MinY; sy <= geometry.MaxY + config.ToleranceFt; sy += sampleStep)
            {
                for (double sx = geometry.MinX; sx <= geometry.MaxX + config.ToleranceFt; sx += sampleStep)
                {
                    if (!geometry.IsPointInsideRoom(sx, sy, config.ToleranceFt)) continue;
                    bool inObstacle = false;
                    foreach (ObstacleBox box in obstacleBoxes)
                    {
                        if (!box.SpansZ(placementZ, config.ToleranceFt)) continue;
                        if (GeometryMath.InsideExpandedBox(sx, sy, box.MinX, box.MinY, box.MaxX, box.MaxY, 0.0))
                        { inObstacle = true; break; }
                    }
                    if (inObstacle) continue;
                    samples.Add(new double[] { sx, sy });
                }
            }

            bool[] covered = new bool[samples.Count];
            for (int s = 0; s < samples.Count; s++)
            {
                foreach (double[] es in existingSprinklerXy)
                {
                    if (GeometryMath.Distance(samples[s][0], samples[s][1], es[0], es[1]) <= throwDepth)
                    { covered[s] = true; break; }
                }
            }

            List<SidewallHead> chosen = new List<SidewallHead>();
            bool[] usedEdge = new bool[rowsPerEdge.Count];
            int remaining = 0;
            for (int s = 0; s < covered.Length; s++) if (!covered[s]) remaining++;

            void ApplyEdge(int e)
            {
                usedEdge[e] = true;
                chosen.AddRange(rowsPerEdge[e]);
                for (int s = 0; s < samples.Count; s++)
                {
                    if (covered[s]) continue;
                    if (SampleCoveredByRow(samples[s], rowsPerEdge[e], alongSpacing, throwDepth, standOff, config.ToleranceFt))
                    { covered[s] = true; remaining--; }
                }
            }

            int EdgeGain(int e)
            {
                if (usedEdge[e] || rowsPerEdge[e].Count == 0) return 0;
                int gain = 0;
                for (int s = 0; s < samples.Count; s++)
                {
                    if (covered[s]) continue;
                    if (SampleCoveredByRow(samples[s], rowsPerEdge[e], alongSpacing, throwDepth, standOff, config.ToleranceFt))
                        gain++;
                }
                return gain;
            }

            int longestEdge = -1;
            double maxLen = 0.0;
            for (int e = 0; e < rowsPerEdge.Count; e++)
            {
                if (rowsPerEdge[e].Count == 0) continue;
                if (edgeLenArr[e] > maxLen) maxLen = edgeLenArr[e];
            }

            int bestSeedGain = -1;
            for (int e = 0; e < rowsPerEdge.Count; e++)
            {
                if (rowsPerEdge[e].Count == 0) continue;
                if (edgeLenArr[e] < maxLen - 0.5) continue;
                int g = EdgeGain(e);
                if (g > bestSeedGain) { bestSeedGain = g; longestEdge = e; }
            }
            if (longestEdge >= 0 && remaining > 0)
            {
                ApplyEdge(longestEdge);

                double[] anchor = polygon[longestEdge];
                double roomDepth = 0.0;
                foreach (double[] v in polygon)
                {
                    if (v == null || v.Length < 2) continue;
                    double d = (v[0] - anchor[0]) * edgeInX[longestEdge]
                             + (v[1] - anchor[1]) * edgeInY[longestEdge];
                    if (d > roomDepth) roomDepth = d;
                }

                if (roomDepth > throwDepth + config.ToleranceFt)
                {
                    int opposite = -1;
                    double bestAnti = 0.5;
                    for (int e = 0; e < rowsPerEdge.Count; e++)
                    {
                        if (usedEdge[e] || rowsPerEdge[e].Count == 0) continue;
                        double dot = edgeUx[e] * edgeUx[longestEdge] + edgeUy[e] * edgeUy[longestEdge];
                        if (-dot > bestAnti) { bestAnti = -dot; opposite = e; }
                    }
                    if (opposite >= 0)
                        ApplyEdge(opposite);
                }
            }

            while (remaining > 0)
            {
                int bestEdge = -1, bestGain = 0;
                for (int e = 0; e < rowsPerEdge.Count; e++)
                {
                    int gain = EdgeGain(e);
                    if (gain > bestGain) { bestGain = gain; bestEdge = e; }
                }
                if (bestEdge < 0 || bestGain == 0) break;

                ApplyEdge(bestEdge);
            }

            List<CalculatedSprinklerPoint> selected = new List<CalculatedSprinklerPoint>();
            foreach (SidewallHead h in chosen)
            {
                selected.Add(new CalculatedSprinklerPoint
                {
                    X = h.X,
                    Y = h.Y,
                    Z = placementZ,
                    RoomId = room.RoomId,
                    LevelId = room.LevelId,
                    LevelName = room.LevelName,
                    WallEdgeIndex = h.WallEdgeIndex
                });
            }

            result.Points = selected;
            result.CalculatedCount = selected.Count;
            double coverageArea = alongSpacing * throwDepth;
            result.RequiredCount = coverageArea > 0 && room.AreaSqFt > 0
                ? (int)Math.Ceiling(room.AreaSqFt / coverageArea) : selected.Count;

            if (selected.Count == 0)
            {
                result.Status = CalculationStatus.NoValidCandidates;
                result.Errors.Add("Sidewall solver produced no valid head locations along any wall.");
                return result;
            }

            result.Status = CalculationStatus.ReviewRequired;
            result.Warnings.Add(
                "Horizontal-sidewall layout generated by the directional solver. Verify against the "
                + "manufacturer's listed coverage table with an FPE: along-wall spacing, throw/room-depth, "
                + "end-wall distance, and obstruction rules (NFPA 13 §11.3 / §10.3.5).");
            if (throwAssumedSquare)
            {
                result.Warnings.Add(
                    "Sidewall throw depth was assumed equal to the along-wall spacing (square protection "
                    + "area) because the catalog carries no listed coverage area for this type. Supply "
                    + "MaxCoverageAreaSqFt so the throw is driven by the listing rather than assumed.");
            }
            result.Diagnostics.Add("End-wall max distance derived as S/2 (standard half-spacing); confirm against listing.");

            if (remaining > 0 && samples.Count > 0)
            {
                double pct = remaining * 100.0 / samples.Count;
                result.Warnings.Add(
                    $"Sidewall coverage gap: {remaining} of {samples.Count} interior sample points ({pct:F1}%) "
                    + "are beyond every wall's throw. The room is too deep for sidewall coverage from its walls "
                    + "alone — add ceiling sprinklers or an interior branch, or reduce the protected depth.");
            }

            if (ceilingUnsupported) result.Warnings.Add(ceilingNote ?? "Ceiling plane requires human review.");
            if (ruleSet.IsProvisional)
                result.Warnings.Add("Spacing/coverage used provisional placeholder values; review required.");

            return result;
        }

        private static bool SampleCoveredByRow(
            double[] sample, List<SidewallHead> row, double alongSpacing, double throwDepth,
            double standOff, double tol)
        {
            double halfAlong = alongSpacing * 0.5 + tol;
            double maxThrow = throwDepth + tol;
            double minThrow = -(standOff + tol);
            foreach (SidewallHead h in row)
            {
                double dx = sample[0] - h.X, dy = sample[1] - h.Y;
                double perp = dx * h.InX + dy * h.InY;
                if (perp < minThrow || perp > maxThrow) continue;
                double along = Math.Abs(dx * h.AlongX + dy * h.AlongY);
                if (along <= halfAlong) return true;
            }
            return false;
        }

        private static double ComputeSignedArea(List<double[]> polygon)
        {
            double area = 0;
            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                double[] p1 = polygon[i];
                double[] p2 = polygon[(i + 1) % n];
                area += (p1[0] * p2[1]) - (p2[0] * p1[1]);
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

        private static bool RequiresCeilingHost(DevicePlacementBehavior behavior)
        {
            switch (behavior)
            {
                case DevicePlacementBehavior.CeilingOverhead:
                case DevicePlacementBehavior.FaceHosted:
                case DevicePlacementBehavior.WorkPlaneDependent:
                case DevicePlacementBehavior.Unknown:
                case DevicePlacementBehavior.Unsupported:
                    return true;
                case DevicePlacementBehavior.WallSidewall:
                case DevicePlacementBehavior.LevelHosted:
                default:
                    return false;
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

        private static CeilingData SelectPrimaryCeiling(PlacementRoomInput room, out string reason, RoomCalculationResult result)
        {
            reason = null;
            if (room?.Ceilings == null || room.Ceilings.Count == 0) return null;

            double floorZ = room.LevelElevationFt;
            string roomLevelId = room.LevelId;
            double expectedCeilingZ = floorZ + (room.CeilingHeightFt ?? 0.0);

            // A ceiling that is far above the room's nominal ceiling height is a soffit, a plenum
            // deck, or the slab of the level ABOVE. None of them is the surface a sprinkler hangs
            // from, and using one puts every head at the wrong Z. Bound the search band so those
            // candidates cannot be selected.
            double bandLimit = room.CeilingHeightFt.HasValue && room.CeilingHeightFt.Value > 0
                ? floorZ + room.CeilingHeightFt.Value + 5.0
                : floorZ + 30.0;

            List<CeilingData> candidates = new List<CeilingData>();
            int rejectedOutOfBand = 0;
            foreach (CeilingData c in room.Ceilings)
            {
                if (c == null) continue;
                if (!c.BottomElevationFt.HasValue) continue;
                if (c.BottomElevationFt.Value <= floorZ + 0.05) continue;
                if (c.BottomElevationFt.Value > bandLimit) { rejectedOutOfBand++; continue; }
                candidates.Add(c);
            }

            if (candidates.Count == 0)
            {
                if (rejectedOutOfBand > 0 && result != null)
                {
                    result.Warnings.Add(
                        $"All {rejectedOutOfBand} ceiling candidate(s) sit more than 5 ft above the room's " +
                        $"nominal ceiling height (floor {floorZ:F2} ft + {room.CeilingHeightFt?.ToString("F2") ?? "?"} ft); " +
                        "none can be a valid mounting plane. Ceiling requires review.");
                }
                return null;
            }

            // Ranking, best first:
            //   1. ceilings on the room's own level   (not the slab of the level above/below)
            //   2. FLAT before SLOPED before STEPPED  (a level plane is the normal mounting surface)
            //   3. closest to the room's nominal ceiling height
            //
            // Ranking on "closest to nominal ceiling height" is what removes the wrong-Z defect.
            // The previous fallback took the HIGHEST bottom elevation, which in a stacked model
            // selected the ceiling of the level ABOVE — placing an entire room's heads at the
            // upper floor's ceiling height.
            string[] slopePriority = { "FLAT", "SLOPED", "STEPPED" };

            CeilingData best = null;
            int bestLevelRank = int.MaxValue;
            int bestSlopeRank = int.MaxValue;
            double bestHeightDelta = double.MaxValue;

            foreach (CeilingData c in candidates)
            {
                int levelRank = (!string.IsNullOrEmpty(c.LevelId)
                    && !string.IsNullOrEmpty(roomLevelId)
                    && string.Equals(c.LevelId, roomLevelId, StringComparison.OrdinalIgnoreCase)) ? 0 : 1;

                int slopeRank = 2;
                if (string.Equals(c.SlopeType, "FLAT", StringComparison.OrdinalIgnoreCase)) slopeRank = 0;
                else if (string.Equals(c.SlopeType, "SLOPED", StringComparison.OrdinalIgnoreCase)) slopeRank = 1;

                double heightDelta = Math.Abs(c.BottomElevationFt.Value - expectedCeilingZ);

                if (best == null
                    || levelRank < bestLevelRank
                    || (levelRank == bestLevelRank && slopeRank < bestSlopeRank)
                    || (levelRank == bestLevelRank && slopeRank == bestSlopeRank && heightDelta < bestHeightDelta))
                {
                    best = c;
                    bestLevelRank = levelRank;
                    bestSlopeRank = slopeRank;
                    bestHeightDelta = heightDelta;
                }
            }

            if (best != null)
            {
                bool levelMatched = bestLevelRank == 0;
                reason = $"Primary ceiling: slope={best.SlopeType}, BottomElevationFt={best.BottomElevationFt.Value:F2}, "
                    + $"levelMatch={levelMatched}, deltaFromNominal={bestHeightDelta:F2} ft.";

                // Be explicit when the selected plane is not the ceiling the room describes.
                // A silent substitution here is what makes a Z error hard to spot in the model.
                if (room.CeilingHeightFt.HasValue && bestHeightDelta > 1.0)
                {
                    result?.Warnings.Add(
                        $"Mounting plane {best.BottomElevationFt.Value:F2} ft differs from the room's nominal " +
                        $"ceiling at {expectedCeilingZ:F2} ft by {bestHeightDelta:F2} ft. Verify heads land on the " +
                        "intended ceiling.");
                }
                if (!levelMatched)
                {
                    result?.Warnings.Add(
                        "No ceiling on this room's own level was found; the mounting plane comes from another " +
                        "level. Verify the Z before placing.");
                }
            }

            return best;
        }

        /// <summary>
        /// Per-head coverage area using the "S x L Rules" (Ch.19 p.222).
        ///
        ///   S = greatest of (distance to the next adjacent sprinkler on the same branch line,
        ///                    twice the distance to the wall measured perpendicular to that line)
        ///   L = greatest of (distance to a sprinkler on an adjacent branch line,
        ///                    twice the distance to the wall measured along that line)
        ///   area = S x L
        ///
        /// Branch lines run along the room's longer axis, which is the usual design convention.
        /// Walls are measured per-axis from the room bounding box; where the room is non-rectangular
        /// the bounding-box wall distance is an approximation, which is recorded in the diagnostic
        /// so the user knows the value is indicative.
        /// </summary>
        private static void CheckCoverageBySxL(
            List<CalculatedSprinklerPoint> selected,
            RoomGeometry geometry,
            HazardPlacementRuleSet ruleSet,
            BruteForceCalculationConfig config,
            RoomCalculationResult result)
        {
            if (selected == null || selected.Count == 0) return;

            double roomWidth = geometry.MaxX - geometry.MinX;
            double roomHeight = geometry.MaxY - geometry.MinY;
            bool branchLinesAlongX = roomWidth >= roomHeight;

            int overArea = 0;
            double worstArea = 0.0;
            int worstIndex = -1;
            double worstS = 0.0, worstL = 0.0;

            for (int i = 0; i < selected.Count; i++)
            {
                double px = selected[i].X;
                double py = selected[i].Y;

                // Perpendicular-to-branch-line wall distance.
                double wallAlongBranch = branchLinesAlongX
                    ? Math.Min(px - geometry.MinX, geometry.MaxX - px)   // distance measured along X
                    : Math.Min(py - geometry.MinY, geometry.MaxY - py);

                // Distance to the wall on the ADJACENT-branch-line axis.
                double wallAcrossBranch = branchLinesAlongX
                    ? Math.Min(py - geometry.MinY, geometry.MaxY - py)
                    : Math.Min(px - geometry.MinX, geometry.MaxX - px);

                // Nearest neighbour along the branch-line axis (S) and across it (L).
                double alongBranch = double.PositiveInfinity;
                double acrossBranch = double.PositiveInfinity;

                for (int j = 0; j < selected.Count; j++)
                {
                    if (i == j) continue;
                    double dx = Math.Abs(selected[j].X - px);
                    double dy = Math.Abs(selected[j].Y - py);

                    double along = branchLinesAlongX ? dx : dy;
                    double across = branchLinesAlongX ? dy : dx;

                    // Only consider a neighbour "on the same line" when it is essentially aligned
                    // on that axis; otherwise it belongs to an adjacent line.
                    if (along < alongBranch && across <= config.ToleranceFt) alongBranch = along;
                    if (across < acrossBranch) acrossBranch = across;
                }

                // S and L are each the greatest of the neighbour distance and twice the wall distance.
                double s = Math.Max(alongBranch, wallAlongBranch * 2.0);
                double l = Math.Max(acrossBranch, wallAcrossBranch * 2.0);
                double area = s * l;

                if (area > worstArea)
                {
                    worstArea = area;
                    worstIndex = i;
                    worstS = s;
                    worstL = l;
                }

                if (area > ruleSet.MaxCoverageAreaSqFt + 1e-6) overArea++;
            }

            result.Diagnostics.Add(
                $"Coverage by S x L rules (branch lines along {(branchLinesAlongX ? "X" : "Y")}): " +
                $"{overArea} of {selected.Count} head(s) exceed the {ruleSet.MaxCoverageAreaSqFt:F0} sq ft limit " +
                $"for {ruleSet.HazardClass}. Worst: head #{worstIndex + 1} at ({selected[worstIndex].X:F2}," +
                $"{selected[worstIndex].Y:F2}) with S={worstS:F2} ft, L={worstL:F2} ft, area={worstArea:F1} sq ft.");

            if (overArea > 0)
            {
                result.Warnings.Add(
                    $"S x L coverage: {overArea} of {selected.Count} head(s) cover more than the " +
                    $"{ruleSet.MaxCoverageAreaSqFt:F0} sq ft allowed for {ruleSet.HazardClass}. Worst is head " +
                    $"#{worstIndex + 1} at ({selected[worstIndex].X:F2},{selected[worstIndex].Y:F2}): " +
                    $"S={worstS:F2} ft x L={worstL:F2} ft = {worstArea:F1} sq ft. "
                    + "Review required — a head near a wall covers twice that wall distance.");
                result.Status = CalculationStatus.ReviewRequired;
            }
        }

        private static bool IsThreeTimesRuleCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return false;
            foreach (string c in Nfpa13RulebookRules.ThreeTimesRuleCategories)
            {
                if (string.Equals(category, c, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Ch.20 p.249, obstructions BELOW the 18 in zone plane: "The only real obstruction rule in
        /// this zone is to put a sprinkler under any permanent fixture that is over 4 ft wide. Note
        /// that furniture is not considered a permanent feature. However, one of the things that is
        /// considered a permanent feature is an overhead door."
        ///
        /// The AABB obstacle model cannot place a head UNDER a fixture, so this is reported rather
        /// than silently ignored: a wide fixture with no head above it is a genuine coverage gap the
        /// engineer must close.
        /// </summary>
        private static void ReportLowerZoneObstructions(
            List<ObstacleBox> belowZoneObstacles,
            HazardPlacementRuleSet ruleSet,
            RoomCalculationResult result)
        {
            if (belowZoneObstacles == null || belowZoneObstacles.Count == 0) return;

            int wide = 0;
            List<string> detail = new List<string>();
            foreach (ObstacleBox box in belowZoneObstacles)
            {
                double widest = box.MaxHorizontalDimensionFt;
                if (widest <= Nfpa13RulebookRules.PermanentFixtureRequiringSprinklerWidthFt) continue;
                wide++;
                if (detail.Count < 5)
                {
                    detail.Add($"{box.Category} {widest:F1} ft wide at ({box.CenterX:F1},{box.CenterY:F1})");
                }
            }

            if (wide == 0)
            {
                result.Diagnostics.Add(
                    $"Chapter 20 lower zone (below {Nfpa13RulebookRules.ObstructionZonePlaneBelowSprinklerFt * 12.0:F0} in): " +
                    $"{belowZoneObstacles.Count} obstruction(s), none wider than the " +
                    $"{Nfpa13RulebookRules.PermanentFixtureRequiringSprinklerWidthFt:F0} ft threshold requiring a head beneath.");
                return;
            }

            result.Warnings.Add(
                $"Chapter 20 lower zone: {wide} obstruction(s) sit more than " +
                $"{Nfpa13RulebookRules.ObstructionZonePlaneBelowSprinklerFt * 12.0:F0} in below the sprinkler and are " +
                $"over {Nfpa13RulebookRules.PermanentFixtureRequiringSprinklerWidthFt:F0} ft wide. NFPA 13 requires a " +
                "sprinkler UNDER each permanent fixture over 4 ft wide (overhead doors count as permanent; " +
                "furniture does not). This tool cannot place a head beneath a fixture, so these need manual " +
                "layout: " + string.Join("; ", detail.ToArray()) + ".");

            result.Status = CalculationStatus.ReviewRequired;
        }

        private static ObstacleBox MakeObstacleBox(ObstacleData obstacle, double clearanceOverride, RoomCalculationResult result)
        {
            if (obstacle == null) return null;

            // Walls form the room boundary itself and are handled by the boundary-clearance rule,
            // never as free-standing obstructions.
            if (string.Equals(obstacle.Category, "OST_Walls", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(obstacle.Category, "Walls", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

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
                result?.Warnings.Add(
                    $"Obstacle '{obstacle.Name ?? obstacle.Category ?? "unknown"}' has no usable bounding box; skipped.");
                return null;
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

            return new ObstacleBox
            {
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY,
                MinZ = minZ,
                MaxZ = maxZ,
                Category = obstacle.Category ?? "obstacle",
                WidthFt = maxX - minX,
                DepthFt = maxY - minY,
                HeightFt = (double.IsNaN(minZ) || double.IsNaN(maxZ)) ? double.NaN : maxZ - minZ,
                CenterX = (minX + maxX) / 2.0,
                CenterY = (minY + maxY) / 2.0,
                IsStructuralMember = IsStructuralMember(obstacle.Category),
                ClearanceFt = clearanceOverride
            };
        }

        private static List<ObstacleBox> BuildObstacleBoxes(
            PlacementRoomInput room, HazardPlacementRuleSet ruleSet, RoomCalculationResult result)
        {
            List<ObstacleBox> boxes = new List<ObstacleBox>();

            if (room.Obstacles == null) return boxes;

            foreach (ObstacleData obstacle in room.Obstacles)
            {
                if (obstacle == null) continue;

                ObstacleBox box = MakeObstacleBox(
                    obstacle, ruleSet.GetObstacleClearance(obstacle.Category), result);
                if (box == null) continue;
                boxes.Add(box);
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

        private static HazardPlacementRuleSet MergeTypeRuleValues(
            HazardPlacementRuleSet baseRuleSet, PlacementRoomInput room, RoomCalculationResult result)
        {
            if (baseRuleSet == null || room == null) return baseRuleSet;
            if (!room.TypeMaxCoverageAreaSqFt.HasValue && !room.TypeMaxSpacingFt.HasValue
                && !room.TypeMinSpacingFt.HasValue && !room.TypeCoverageRadiusFt.HasValue)
                return baseRuleSet;

            HazardPlacementRuleSet effective = baseRuleSet.Clone();
            string typeLabel = string.IsNullOrWhiteSpace(room.SelectedSprinklerTypeName)
                ? "selected type" : "type '" + room.SelectedSprinklerTypeName + "'";
            double spacingCeiling = GetNfpa13MaxSpacingCeiling(effective.HazardClass);

            if (room.TypeMaxSpacingFt.HasValue && room.TypeMaxSpacingFt.Value > 0)
            {
                effective.MaxSpacingFt = Math.Min(room.TypeMaxSpacingFt.Value, spacingCeiling);
                result.Diagnostics.Add("MaxSpacing " + effective.MaxSpacingFt.ToString("F2") + " ft from catalog " + typeLabel + ".");
            }
            if (room.TypeMinSpacingFt.HasValue && room.TypeMinSpacingFt.Value > 0)
            {
                effective.MinSpacingFt = Math.Min(room.TypeMinSpacingFt.Value, spacingCeiling);
                result.Diagnostics.Add("MinSpacing " + effective.MinSpacingFt.ToString("F2") + " ft from catalog " + typeLabel + ".");
            }
            if (room.TypeCoverageRadiusFt.HasValue && room.TypeCoverageRadiusFt.Value > 0)
            {
                effective.CoverageRadiusFt = Math.Min(room.TypeCoverageRadiusFt.Value, spacingCeiling / 2.0);
                result.Diagnostics.Add("CoverageRadius " + effective.CoverageRadiusFt.ToString("F2") + " ft from catalog " + typeLabel + ".");
            }
            if (room.TypeMaxCoverageAreaSqFt.HasValue && room.TypeMaxCoverageAreaSqFt.Value > 0)
            {
                double areaCeiling = spacingCeiling * spacingCeiling;
                effective.MaxCoverageAreaSqFt = Math.Min(room.TypeMaxCoverageAreaSqFt.Value, areaCeiling);
                result.Diagnostics.Add("MaxCoverageArea " + effective.MaxCoverageAreaSqFt.ToString("F2") + " sq ft from catalog " + typeLabel + ".");
            }
            return effective;
        }

        private static HazardPlacementRuleSet ApplyPerRoomOverrides(
            HazardPlacementRuleSet baseRuleSet,
            PlacementRoomInput room,
            RoomCalculationResult result)
        {
            if (baseRuleSet == null) return null;
            if (room == null) return baseRuleSet;
            if (!room.OverrideMaxSpacingFt.HasValue
                && !room.OverrideBoundaryClearanceFt.HasValue
                && !room.OverrideMaxDistanceToWallFt.HasValue)
            {
                return baseRuleSet;
            }

            HazardPlacementRuleSet effective = baseRuleSet.Clone();
            effective.IsProvisional = true;

            if (room.OverrideMaxSpacingFt.HasValue)
            {
                double requested = room.OverrideMaxSpacingFt.Value;
                double clamped = ClampToNfpa13MaxSpacing(effective.HazardClass, requested, out string clampNote);
                effective.MaxSpacingFt = clamped;
                if (result != null)
                {
                    string note = "Override applied: MaxSpacingFt=" + clamped.ToString("F2")
                        + " ft (rule=" + baseRuleSet.MaxSpacingFt.ToString("F2") + " ft, requested=" + requested.ToString("F2") + " ft)";
                    if (!string.IsNullOrEmpty(clampNote)) note += " - " + clampNote;
                    result.Diagnostics.Add(note);
                }
            }

            if (room.OverrideBoundaryClearanceFt.HasValue)
            {
                double requested = room.OverrideBoundaryClearanceFt.Value;
                double clamped = Math.Max(0.0, requested);
                effective.BoundaryClearanceFt = clamped;
                if (result != null)
                {
                    result.Diagnostics.Add("Override applied: BoundaryClearanceFt=" + clamped.ToString("F2")
                        + " ft (rule=" + baseRuleSet.BoundaryClearanceFt.ToString("F2")
                        + " ft, requested=" + requested.ToString("F2") + " ft)");
                }
            }

            if (room.OverrideMaxDistanceToWallFt.HasValue)
            {
                double requested = room.OverrideMaxDistanceToWallFt.Value;
                double applied = Math.Max(0.0, requested);
                effective.MaxDistanceFromWallsFt = applied;
                if (result != null)
                {
                    result.Diagnostics.Add("Override applied: MaxDistanceFromWallsFt (S→W)=" + applied.ToString("F2")
                        + " ft (rule=" + baseRuleSet.MaxDistanceFromWallsFt.ToString("F2")
                        + " ft, requested=" + requested.ToString("F2") + " ft) - user value not NFPA-clamped.");
                }
            }

            if (result != null)
            {
                if (result.Status == CalculationStatus.Success)
                {
                    result.Status = CalculationStatus.ReviewRequired;
                }
                result.Warnings.Add(
                    "Per-room spacing override applied; values are not NFPA13-2022 approved. Review required.");
            }

            return effective;
        }

        private static double ClampToNfpa13MaxSpacing(HazardClass hazardClass, double requestedFt, out string note)
        {
            note = null;
            double ceilingFt = GetNfpa13MaxSpacingCeiling(hazardClass);
            if (requestedFt > ceilingFt)
            {
                note = "Requested MaxSpacingFt " + requestedFt.ToString("F2")
                    + " ft exceeds NFPA 13 ceiling for " + hazardClass + " (" + ceilingFt.ToString("F2")
                    + " ft); clamped to " + ceilingFt.ToString("F2") + " ft.";
                return ceilingFt;
            }
            if (requestedFt <= 0.0)
            {
                note = "Requested MaxSpacingFt <= 0; rejected.";
                return 0.0;
            }
            return requestedFt;
        }

        private static double GetNfpa13MaxSpacingCeiling(HazardClass hazardClass)
        {
            switch (hazardClass)
            {
                case HazardClass.EH1:
                case HazardClass.EH2:
                    return 12.0;
                case HazardClass.OH1:
                case HazardClass.OH2:
                    return 15.0;
                case HazardClass.Light:
                default:
                    return 15.0;
            }
        }

        /// <summary>
        /// NFPA 13 3.7.2 ceiling construction classification, as quoted in the rulebook Ch.19 p.216.
        /// </summary>
        public enum CeilingConstructionClass
        {
            /// <summary>No structural members found over the room.</summary>
            Unobstructed,

            /// <summary>Members over 7.5 ft on center: unobstructed regardless of solidity.</summary>
            UnobstructedMemberSpacing,

            /// <summary>
            /// Members between 3 ft and 7.5 ft on center. Unobstructed only if the cross-section
            /// openings exceed 70%, which the extractor does not measure, so this is treated as
            /// OBSTRUCTED (the conservative branch).
            /// </summary>
            Obstructed,

            /// <summary>Members at or under 3 ft on center: necessarily obstructed (Ch.19 p.227).</summary>
            ObstructedDenseMembers
        }

        /// <summary>
        /// Classifies the ceiling construction per NFPA 13 3.7.2 (rulebook Ch.19 p.216):
        ///
        ///   "No matter what kind of structural members they are, even if they are solid, they are
        ///    considered Unobstructed Construction if they are more than 7 1/2 ft on center. For
        ///    structural members spaced less than 7 1/2 ft on center, the construction can only be
        ///    considered Unobstructed if the openings in the cross section of the members are
        ///    greater than 70%."
        ///
        /// The 70% open-cross-section test cannot be evaluated from the extracted AABB data, so any
        /// room with members closer than 7.5 ft is classified OBSTRUCTED. That is deliberately the
        /// conservative branch: it applies the tighter 6 in deflector band and the full Chapter 20
        /// obstruction rules rather than assuming the favourable classification.
        /// </summary>
        private static CeilingConstructionClass ClassifyCeilingConstruction(
            PlacementRoomInput room, RoomCalculationResult result)
        {
            if (room?.Obstacles == null || room.Obstacles.Count == 0)
            {
                result?.Diagnostics.Add("Ceiling construction: UNOBSTRUCTED (no structural members in the room).");
                return CeilingConstructionClass.Unobstructed;
            }

            List<ObstacleBox> members = new List<ObstacleBox>();
            foreach (ObstacleData o in room.Obstacles)
            {
                if (o == null) continue;
                if (!IsStructuralMember(o.Category)) continue;
                members.Add(MakeObstacleBox(o, 0.0, null));
            }

            if (members.Count == 0)
            {
                result?.Diagnostics.Add("Ceiling construction: UNOBSTRUCTED (no beams/joists/framing over the room).");
                return CeilingConstructionClass.Unobstructed;
            }

            double spacing = EstimateMemberSpacing(members);

            CeilingConstructionClass cls;
            string why;
            if (spacing > Nfpa13RulebookRules.UnobstructedIfMemberSpacingOverFt)
            {
                cls = CeilingConstructionClass.UnobstructedMemberSpacing;
                why = "members > 7.5 ft on center, unobstructed regardless of solidity (3.7.2)";
            }
            else if (spacing < Nfpa13RulebookRules.ObstructedIfMemberSpacingUnderFt)
            {
                cls = CeilingConstructionClass.ObstructedDenseMembers;
                why = "members < 3 ft on center, necessarily obstructed (Ch.19 p.227)";
            }
            else
            {
                cls = CeilingConstructionClass.Obstructed;
                why = "members between 3 ft and 7.5 ft on center; the >70% open-cross-section test "
                    + "cannot be measured from extracted geometry, so treated as OBSTRUCTED";
            }

            result?.Diagnostics.Add(
                $"Ceiling construction: {cls.ToString().ToUpperInvariant()} — {members.Count} member(s), " +
                $"estimated on-center spacing {spacing:F2} ft; {why}.");

            return cls;
        }

        private static bool IsStructuralMember(string category)
        {
            if (string.IsNullOrEmpty(category)) return false;
            return string.Equals(category, "OST_StructuralFraming", StringComparison.OrdinalIgnoreCase)
                || string.Equals(category, "OST_StructuralColumns", StringComparison.OrdinalIgnoreCase)
                || string.Equals(category, "OST_Columns", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Estimates on-center spacing of structural members from their extracted centres. Uses the
        /// nearest-neighbour distance between member centres, which is the on-center spacing for a
        /// regular beam grid and a reasonable approximation otherwise.
        /// </summary>
        private static double EstimateMemberSpacing(List<ObstacleBox> members)
        {
            if (members == null || members.Count == 0) return double.PositiveInfinity;
            if (members.Count == 1)
            {
                // A single member tells us nothing about spacing; treat as unobstructed rather than
                // inventing a dense grid.
                return double.PositiveInfinity;
            }

            double nearest = double.PositiveInfinity;
            for (int i = 0; i < members.Count; i++)
            {
                double cx = (members[i].MinX + members[i].MaxX) / 2.0;
                double cy = (members[i].MinY + members[i].MaxY) / 2.0;
                for (int j = 0; j < members.Count; j++)
                {
                    if (i == j) continue;
                    double ox = (members[j].MinX + members[j].MaxX) / 2.0;
                    double oy = (members[j].MinY + members[j].MaxY) / 2.0;
                    double d = GeometryMath.Distance(cx, cy, ox, oy);
                    if (d > 1e-6 && d < nearest) nearest = d;
                }
            }

            return nearest == double.PositiveInfinity ? double.PositiveInfinity : nearest;
        }

        /// <summary>
        /// Ch.19 p.223, the Small Room Rule. All four conditions must hold:
        ///   1) light hazard occupancy
        ///   2) room less than 800 sq ft
        ///   3) unobstructed ceiling construction
        ///   4) room surrounded by walls and a ceiling
        ///
        /// Where it applies, the S/2 maximum wall distance is relaxed to 9 ft and coverage is
        /// computed by AVERAGING (room area / head count) rather than by the S x L rules, exactly as
        /// the rulebook directs.
        ///
        /// Condition 4 is only partially verifiable: the extractor does not model wall openings or
        /// lintels, so a room using this relaxation is always reported so a reviewer can confirm the
        /// 8 in lintel condition.
        /// </summary>
        private static bool TryApplySmallRoomRule(
            PlacementRoomInput room,
            RoomCalculationResult result,
            CeilingConstructionClass constructionClass,
            HazardPlacementRuleSet ruleSet)
        {
            if (ruleSet.HazardClass != HazardClass.Light)
            {
                result.Diagnostics.Add(
                    "Small Room Rule: not applicable (requires Light hazard; this room is "
                    + ruleSet.HazardClass + ").");
                return false;
            }

            if (room.AreaSqFt <= 0
                || room.AreaSqFt >= Nfpa13RulebookRules.SmallRoomMaxAreaSqFt)
            {
                result.Diagnostics.Add(
                    "Small Room Rule: not applicable (requires room area < "
                    + Nfpa13RulebookRules.SmallRoomMaxAreaSqFt.ToString("F0") + " sq ft; this room is "
                    + room.AreaSqFt.ToString("F0") + " sq ft).");
                return false;
            }

            if (constructionClass == CeilingConstructionClass.Obstructed
                || constructionClass == CeilingConstructionClass.ObstructedDenseMembers)
            {
                result.Diagnostics.Add(
                    "Small Room Rule: not applicable (requires UNOBSTRUCTED ceiling construction; this room is "
                    + constructionClass + ").");
                return false;
            }

            // The Small Room Rule RELAXES the normal S/2 wall limit up to 9 ft. It must never be used
            // to loosen a value the rule set already set TIGHTER than S/2: a rule set carrying, say,
            // 1.0 ft is making a deliberate engineering decision that this tool has no business
            // over-riding upward. The relaxation is therefore applied only when the supplied value
            // is the standard S/2 limit, i.e. when the caller is actually relying on S/2.
            double halfSpacing = ruleSet.MaxSpacingFt * Nfpa13RulebookRules.WallDistanceFractionOfMaxSpacing;
            if (ruleSet.MaxDistanceFromWallsFt < halfSpacing - 1e-6)
            {
                result.Diagnostics.Add(
                    $"Small Room Rule: not applied (wall limit {ruleSet.MaxDistanceFromWallsFt:F2} ft is tighter than " +
                    $"the standard S/2 limit of {halfSpacing:F2} ft, so the 9 ft relaxation does not apply).");
                return false;
            }

            // Condition 4 — walls and a ceiling. Verified only as "the room has a closed boundary
            // and a ceiling plane"; lintel depth over openings cannot be checked from the data.
            result.Warnings.Add(
                "Small Room Rule applied (light hazard, " + room.AreaSqFt.ToString("F0")
                + " sq ft, unobstructed ceiling): max wall distance relaxed from "
                + ruleSet.MaxDistanceFromWallsFt.ToString("F2") + " ft to "
                + Nfpa13RulebookRules.SmallRoomMaxWallDistanceFt.ToString("F0")
                + " ft, and coverage is computed by AVERAGING room area / head count. Confirm the room is "
                + "fully enclosed by walls and a ceiling, with any wall opening having at least an "
                + Nfpa13RulebookRules.SmallRoomLintelDepthFt.ToString("F2") + " ft (8 in) lintel to trap heat.");

            result.Warnings.Add(
                "Small Room Rule means the max wall distance is now "
                + Nfpa13RulebookRules.SmallRoomMaxWallDistanceFt.ToString("F0")
                + " ft, which exceeds half the spacing. Per-head coverage is averaged rather than "
                + "computed with the S x L rules, as the rulebook requires.");

            return true;
        }

        public sealed class ObstacleBox
        {
            public double MinX { get; set; }
            public double MinY { get; set; }
            public double MaxX { get; set; }
            public double MaxY { get; set; }
            public double MinZ { get; set; }
            public double MaxZ { get; set; }
            public string Category { get; set; }
            public double ClearanceFt { get; set; }

            /// <summary>Footprint width along X, in feet.</summary>
            public double WidthFt { get; set; }

            /// <summary>Footprint depth along Y, in feet.</summary>
            public double DepthFt { get; set; }

            /// <summary>Vertical extent, in feet. NaN when the source carried no Z.</summary>
            public double HeightFt { get; set; }

            public double CenterX { get; set; }
            public double CenterY { get; set; }

            /// <summary>
            /// True for beams / joists / trusses / columns. Only structural members drive the
            /// unobstructed-vs-obstructed ceiling classification (NFPA 13 3.7.2) and qualify for
            /// the Chapter 20 "Three Times" rule; ducts, pipes and cable trays do not.
            /// </summary>
            public bool IsStructuralMember { get; set; }

            /// <summary>Largest horizontal dimension (Ch.20 "Three Times" rule uses the MAXIMUM).</summary>
            public double MaxHorizontalDimensionFt => Math.Max(WidthFt, DepthFt);

            /// <summary>
            /// Horizontal distance from a point to the NEAR EDGE of this box (0 when inside).
            /// Ch.20 measures the Three Times distance to the near edge, not to the centreline.
            /// </summary>
            public double DistanceToNearEdge(double x, double y)
            {
                // Per-axis gap to the box: 0 when the coordinate lies within that axis' extent.
                double gapX = Math.Max(MinX - x, 0.0);
                if (x > MaxX) gapX = x - MaxX;
                double gapY = Math.Max(MinY - y, 0.0);
                if (y > MaxY) gapY = y - MaxY;
                return Math.Sqrt(gapX * gapX + gapY * gapY);
            }

            /// <summary>Horizontal distance from a point to this box's centreline (Ch.20 exception).</summary>
            public double DistanceToCenterline(double x, double y)
            {
                double dx = x - CenterX;
                double dy = y - CenterY;
                return Math.Sqrt(dx * dx + dy * dy);
            }

            public bool SpansZ(double placementZ, double toleranceFt)
            {
                if (double.IsNaN(MinZ) || double.IsNaN(MaxZ)) return true;
                if (Math.Abs(MaxZ - MinZ) < 1e-6) return true;
                return placementZ >= MinZ - toleranceFt
                    && placementZ <= MaxZ + toleranceFt;
            }
        }
    }
}
