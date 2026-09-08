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

            // ---- Hazard ----
            HazardClass hazardClass = ParseHazardClass(room.EffectiveHazardClass, result);
            HazardPlacementRuleSet baseRuleSet = rules.GetRules(hazardClass);
            HazardPlacementRuleSet ruleSet = ApplyPerRoomOverrides(baseRuleSet, room, result);
            // ApplyPerRoomOverrides clones when an override is present; for the no-override
            // path it returns the base instance. Clone defensively here so downstream
            // in-place mutations (ceiling-height factor, obstacle/orientation adjustments
            // added by later tasks) never leak back to the base rule set.
            if (ReferenceEquals(ruleSet, baseRuleSet))
            {
                ruleSet = baseRuleSet.Clone();
            }

            // Carried onto the result so placement can prove points fall inside this room and can stamp the
            // spacing it actually used onto each created element.
            result.Polygon = outer;
            result.AppliedMaxSpacingFt = ruleSet.MaxSpacingFt;
            result.AppliedBoundaryClearanceFt = ruleSet.BoundaryClearanceFt;
            result.RulesApproved = rules.HasApprovedRules;

            // ---- Ceiling / placement plane (Z) ----
            // DETERMINISTIC selection: pick the ceiling that is unambiguously the room's ceiling,
            // not the first one iteration happens to return.
            //   1. Filter by level match (host-room level id); only when the candidate carries a level.
            //   2. Among matches, prefer FLAT over SLOPED over STEPPED (FLAT is the "clean" case).
            //   3. Within the preferred slope class, pick the highest BottomElevationFt that is still
            //      ABOVE the room's floor — a ceiling below floor Z is a slab of the level below.
            bool ceilingUnsupported = false;
            double placementZ = room.LevelElevationFt;
            string ceilingNote = null;

            CeilingData bestCeiling = SelectPrimaryCeiling(room, out string selectionReason, result);

            if (bestCeiling != null && bestCeiling.BottomElevationFt.HasValue)
            {
                // For a SLOPED ceiling, BottomElevationFt is the LOW point of the slope and
                // TopElevationFt is the HIGH point. A weighted average (geometric mean) places
                // the placement plane at the centroid height, which is closer to the median
                // sprinkler position than the low end. FLAT ceilings ignore the average (top
                // == bottom) so this is a no-op for them.
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
                // No usable ceiling — fall back to a provisional Z and flag for review.
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

            // Step 2 placement-behavior read (Decision 011 hookup): the
            // per-row SelectedSprinklerPlacementBehavior now drives the engine.
            // If the resolved behavior is Unsupported the engine explicitly does
            // NOT support that family class — fail fast with a clear error
            // rather than producing candidates that the placement service would
            // reject at commit time. This is the only Step-2 behavior switch
            // that has algorithmic effect; the full placement-strategy hookup
            // (FaceBased / WorkPlaneBased / OneLevelBased) is a future change.
            // Placed BEFORE the ceilingUnsupported block so it fires regardless
            // of whether the room has a valid ceiling.
            if (room.SelectedSprinklerPlacementBehavior == DevicePlacementBehavior.Unsupported)
            {
                result.Status = CalculationStatus.InvalidInput;
                result.CalculatedCount = 0;
                result.RequiredCount = 0;
                result.Errors.Add(
                    "The selected sprinkler family has an unsupported placement behavior (" +
                    room.SelectedSprinklerFamilyName + " / " + room.SelectedSprinklerTypeName +
                    "). The brute-force engine does not currently support this family class. " +
                    "Select a different family or extend the engine with a placement strategy.");
                result.Diagnostics.Add(
                    "BLOCKED: unsupported placement behavior. " +
                    "SelectedSprinklerPlacementBehavior=Unsupported for family '" +
                    (room.SelectedSprinklerFamilyName ?? "<none>") + "'.");
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

                // For HOSTED sprinkler families (CeilingOverhead / FaceHosted /
                // WorkPlaneDependent / Unknown / Unsupported) the candidates produced by
                // a missing-ceiling room are unusable in Revit — there is no host face to
                // anchor the placement on. The room is therefore BLOCKED rather than
                // producing provisional points that would be rejected at placement time.
                // LevelHosted families (plain level-anchored sprinklers) are exempt: they
                // do not need a ceiling. Only block when the room has NO ceilings at all
                // (so MissingCeiling already implies "absent"). A room that HAS ceilings
                // but none were selected (e.g. only sloped) keeps its UnsupportedCeiling
                // status and proceeds.
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
                    result.Diagnostics.Add(
                        "BLOCKED: hosted-family + missing ceiling. The selected sprinkler family requires a " +
                        "ceiling or work plane that the room does not have. Add a ceiling to the room or " +
                        "select a level-hosted family.");
                    return result;
                }
            }

            // ---- Ceiling-height adjustment (NFPA 13 §11.1 / §C.11) ----
            // For high ceilings the standard requires a reduction factor on the maximum spacing:
            //   ceiling height > 10 ft: factor 1.0 (no change)
            //   ceiling height > 12 ft: factor 0.95
            //   ceiling height > 15 ft: factor 0.90
            //   ceiling height > 20 ft: factor 0.85
            //   ceiling height > 25 ft: factor 0.80
            // For sloped ceilings the slope reduces coverage area (water travels further down the
            // slope); the rule-set's GetCeilingSlopeAdjustment supplies a class-specific multiplier.
            // The combined adjustment reduces BOTH MaxSpacing and CoverageRadius proportionally so
            // the resulting layout still satisfies the higher-hazard requirement of high rooms.
            double ceilingHeightFt = room.CeilingHeightFt.HasValue
                ? room.CeilingHeightFt.Value
                : Math.Max(0, placementZ - room.LevelElevationFt);
            double heightFactor = 1.0;
            if (ceilingHeightFt > 25.0) heightFactor = 0.80;
            else if (ceilingHeightFt > 20.0) heightFactor = 0.85;
            else if (ceilingHeightFt > 15.0) heightFactor = 0.90;
            else if (ceilingHeightFt > 12.0) heightFactor = 0.95;

            double slopeFactor = 1.0;
            if (bestCeiling != null && !string.Equals(bestCeiling.SlopeType, "FLAT", StringComparison.OrdinalIgnoreCase))
            {
                slopeFactor = ruleSet.GetCeilingSlopeAdjustment(bestCeiling.SlopeType);
                if (slopeFactor <= 0) slopeFactor = 1.0;
            }

            // Combined factor — never worsens spacing below the user's override, but reduces
            // for high or sloped ceilings. The rule-set field CeilingHeightAdjustmentFactor is
            // a CLASS-WIDE multiplier (1.0 default, 0.75–0.9 for higher-hazard classes).
            double combinedFactor = heightFactor * slopeFactor * ruleSet.CeilingHeightAdjustmentFactor;
            if (combinedFactor < 1.0)
            {
                double newMax = ruleSet.MaxSpacingFt * combinedFactor;
                double newCoverage = ruleSet.CoverageRadiusFt * combinedFactor;
                if (newMax < ruleSet.MaxSpacingFt - 1e-9)
                {
                    result.Diagnostics.Add(
                        $"Ceiling-height adjustment applied: factor={combinedFactor:F3} (height={ceilingHeightFt:F2}ft, slope={slopeFactor:F2}, class={ruleSet.CeilingHeightAdjustmentFactor:F2}). " +
                        $"MaxSpacingFt: {ruleSet.MaxSpacingFt:F2} -> {newMax:F2} ft, CoverageRadiusFt: {ruleSet.CoverageRadiusFt:F2} -> {newCoverage:F2} ft.");
                    ruleSet.MaxSpacingFt = newMax;
                    ruleSet.CoverageRadiusFt = newCoverage;
                    result.AppliedMaxSpacingFt = newMax;
                }
            }

            // ---- Orientation adjustment (NFPA 13 §10.2) ----
            // Sidewall sprinklers have a different spray pattern (half-circle) than pendent or
            // upright, so the standard allows a tighter spacing. The rule set supplies a
            // class-specific multiplier; the per-row SelectedSprinklerOrientation drives it.
            // null / empty / unknown orientation = no adjustment (multiplier 1.0).
            double orientationFactor = ruleSet.GetOrientationAdjustment(room.SelectedSprinklerOrientation);
            if (orientationFactor > 0 && orientationFactor < 1.0)
            {
                double prevMax = ruleSet.MaxSpacingFt;
                double prevCoverage = ruleSet.CoverageRadiusFt;
                double newMaxO = prevMax * orientationFactor;
                double newCoverageO = prevCoverage * orientationFactor;
                result.Diagnostics.Add(
                    $"Orientation adjustment applied: orientation='{room.SelectedSprinklerOrientation}', factor={orientationFactor:F3}. " +
                    $"MaxSpacingFt: {prevMax:F2} -> {newMaxO:F2} ft, CoverageRadiusFt: {prevCoverage:F2} -> {newCoverageO:F2} ft.");
                ruleSet.MaxSpacingFt = newMaxO;
                ruleSet.CoverageRadiusFt = newCoverageO;
                result.AppliedMaxSpacingFt = newMaxO;
            }

            // ---- Candidate generation ----
            List<ObstacleBox> obstacleBoxes = BuildObstacleBoxes(room, ruleSet, result);
            List<double[]> existingSprinklerXy = BuildExistingSprinklerXy(room);

            // ---- Sidewall branch (NFPA 13 §11.3) ----
            // When the per-row sprinkler is wall-anchored, the candidate set is NOT
            // the 2-D ceiling grid; it is a step along each wall edge, projected
            // ~BoundaryClearanceFt inboard. This is the industry-standard sidewall
            // pattern: throw one direction into the room, half-ellipse coverage.
            // The selection loop below is reused unchanged — it only consumes
            // (X, Y, Z) tuples; sidewall candidates carry the same shape.
            bool isSidewall =
                room.SelectedSprinklerPlacementBehavior == DevicePlacementBehavior.WallSidewall
                || string.Equals(room.SelectedSprinklerOrientation, "sidewall", StringComparison.OrdinalIgnoreCase);

            if (isSidewall)
            {
                List<CandidatePoint> sidewallCandidates = GenerateSidewallCandidates(
                    room, geometry, ruleSet, obstacleBoxes, existingSprinklerXy, placementZ, config, result);
                if (sidewallCandidates.Count == 0)
                {
                    result.Status = CalculationStatus.NoValidCandidates;
                    result.Errors.Add("No valid sidewall candidate locations found along the room boundary.");
                    result.CalculatedCount = 0;
                    result.RequiredCount = 0;
                    return result;
                }
                return SelectFromCandidates(
                    sidewallCandidates, room, geometry, ruleSet, obstacleBoxes,
                    existingSprinklerXy, config, result, ceilingUnsupported, ceilingNote);
            }

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
                        // Per-obstacle clearance (NFPA 13 distinguishes beam/column/duct
                        // clearances per hazard class). The rule set supplies a fallback
                        // ObstacleClearanceFt when the category is unknown.
                        if (GeometryMath.InsideExpandedBox(x, y, box.MinX, box.MinY, box.MaxX, box.MaxY, box.ClearanceFt))
                        {
                            // Z-axis guard: a low beam (MaxZ well below the placement
                            // plane) does NOT block a sprinkler at the ceiling, even if
                            // its XY footprint overlaps. Only obstacles whose vertical
                            // extent actually contains placementZ are real conflicts
                            // (ducts, soffits, tall columns). SpansZ returns true when
                            // the obstacle has no Z info — preserving pre-3D behavior
                            // for legacy input.
                            if (!box.SpansZ(placementZ, config.ToleranceFt)) continue;

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
            // The selection + post-selection verification is shared between the
            // ceiling-grid branch and the sidewall branch (NFPA 13 §11.3). It consumes
            // any list of (X, Y, Z) candidate points; the algorithm is mount-agnostic.
            return SelectFromCandidates(
                validCandidates, room, geometry, ruleSet, obstacleBoxes,
                existingSprinklerXy, config, result, ceilingUnsupported, ceilingNote);
        }

        // ------------------------------------------------------------------
        // Selection (shared by ceiling-grid and sidewall branches)
        // ------------------------------------------------------------------

        /// <summary>
        /// Greedy coverage-driven selection over any candidate set (ceiling grid
        /// or sidewall-anchored). The algorithm is mount-agnostic; the caller is
        /// responsible for producing the candidate list.
        /// </summary>
        private static RoomCalculationResult SelectFromCandidates(
            List<CandidatePoint> validCandidates,
            PlacementRoomInput room,
            RoomGeometry geometry,
            HazardPlacementRuleSet ruleSet,
            List<ObstacleBox> obstacleBoxes,
            List<double[]> existingSprinklerXy,
            BruteForceCalculationConfig config,
            RoomCalculationResult result,
            bool ceilingUnsupported,
            string ceilingNote)
        {
            // Semantics (this is what NFPA 13 actually says, and what the ruleset's fields mean):
            //   CoverageRadiusFt   = maximum radius one sprinkler can cover. A point WITHIN this
            //                        radius of an existing/placed sprinkler is ALREADY COVERED and
            //                        does NOT need a new sprinkler.
            //   MaxSpacingFt       = NFPA 13 upper bound on center-to-center distance. The greedy
            //                        algorithm is allowed to place sprinklers further apart to
            //                        achieve coverage, but the maximum is verified POST-SELECTION
            //                        (Task #9 - coverage gap detection); it is NOT a per-candidate
            //                        gate, because that would make the algorithm stop after the
            //                        first sprinkler.
            //   MinSpacingFt       = minimum permitted center-to-center distance. Two sprinklers
            //                        must not be closer than this (default = CoverageRadius when
            //                        not separately provided). Prevents bunching.
            //
            // The previous code used CoverageRadiusFt as if it were MinSpacingFt (rejecting any
            // candidate within CoverageRadius of a placed sprinkler), which both:
            //   (a) let sprinklers spread to MaxSpacing rather than stopping at CoverageRadius, and
            //   (b) silently violated the spacing rule by inverting its meaning.
            // The corrected algorithm enforces coverage (skip) + min spacing (no new sprinkler
            // within MinSpacing of an existing one). Max spacing is verified as a post-selection
            // warning, not a hard reject.

            double minSpacingFt = ruleSet.MinSpacingFt > 0
                ? ruleSet.MinSpacingFt
                : ruleSet.CoverageRadiusFt; // conservative default: at least one coverage radius apart
            double coverageRadiusFt = ruleSet.CoverageRadiusFt > 0 ? ruleSet.CoverageRadiusFt : 7.5;

            // Sort by (Y, X) for a deterministic, room-traversable ordering. The same input
            // must always produce the same output.
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

                // 1. COVERAGE CHECK: skip the candidate if an existing OR already-placed sprinkler
                //    within CoverageRadiusFt already covers it.
                bool alreadyCovered = false;
                foreach (double[] es in existingSprinklerXy)
                {
                    if (GeometryMath.Distance(candidate.X, candidate.Y, es[0], es[1]) <= coverageRadiusFt - config.ToleranceFt)
                    {
                        alreadyCovered = true;
                        break;
                    }
                }
                if (!alreadyCovered)
                {
                    foreach (CalculatedSprinklerPoint s in selected)
                    {
                        if (GeometryMath.Distance(candidate.X, candidate.Y, s.X, s.Y) <= coverageRadiusFt - config.ToleranceFt)
                        {
                            alreadyCovered = true;
                            break;
                        }
                    }
                }
                if (alreadyCovered) continue;

                // 2. MIN SPACING CHECK: reject if too close to an already-placed sprinkler
                //    (prevents bunching that would skew hydraulic calculations).
                bool tooClose = false;
                foreach (CalculatedSprinklerPoint s in selected)
                {
                    double d = GeometryMath.Distance(candidate.X, candidate.Y, s.X, s.Y);
                    if (d < minSpacingFt - config.ToleranceFt)
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

            // 3. POST-SELECTION MAX-SPACING VERIFICATION: NFPA 13 sets an upper bound on
            //    center-to-center distance. If any pair of placed (or existing+placed) sprinklers
            //    exceeds MaxSpacingFt, the room is flagged for review — the greedy selection
            //    could not satisfy the spacing rule with the available candidates. This is a
            //    warning, not a failure, because reducing spacing to satisfy the rule would
            //    require more candidates (finer grid) than the cap allows.
            if (ruleSet.MaxSpacingFt > 0)
            {
                double maxSpacing = ruleSet.MaxSpacingFt;
                for (int i = 0; i < selected.Count; i++)
                {
                    for (int j = i + 1; j < selected.Count; j++)
                    {
                        double d = GeometryMath.Distance(selected[i].X, selected[i].Y, selected[j].X, selected[j].Y);
                        if (d > maxSpacing + config.ToleranceFt)
                        {
                            result.Warnings.Add(
                                $"Pair of placed sprinklers ({i + 1},{j + 1}) are {d:F2} ft apart, " +
                                $"exceeding MaxSpacingFt={maxSpacing:F2} ft. Review required.");
                            result.Status = CalculationStatus.ReviewRequired;
                        }
                    }
                }
            }

            // 3b. POST-SELECTION WALL-DISTANCE VERIFICATION (NFPA 13 §10.2.4 / §11.3):
            //    a sprinkler must be no further than MaxDistanceFromWallsFt from the
            //    nearest wall. The greedy selector could place a sprinkler in the room
            //    center (still within max spacing, still covering, but too far from any
            //    wall). Verify after the fact and flag the room for review. BoundaryClearanceFt
            //    is the LOWER bound (min distance, enforced at candidate-gen); this is
            //    the UPPER bound.
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

            // 4. POST-SELECTION COVERAGE GAP DETECTION: the greedy places a sprinkler at
            //    every VALID candidate that is uncovered AND not too close to a placed
            //    sprinkler. MinSpacingFt can leave a gap when a candidate that would cover
            //    an uncovered region is too close to an already-placed sprinkler and is
            //    therefore rejected. We re-sample the room interior on a coarse grid (half
            //    the smaller of coverage radius / max spacing) and count samples that are
            //    beyond CoverageRadiusFt of any placed sprinkler. A non-zero gap is flagged
            //    for review.
            if (ruleSet.CoverageRadiusFt > 0 && selected.Count > 0)
            {
                double sampleStep = Math.Min(ruleSet.CoverageRadiusFt, ruleSet.MaxSpacingFt);
                if (sampleStep <= 0) sampleStep = ruleSet.CoverageRadiusFt;
                sampleStep = Math.Max(sampleStep / 2.0, 0.5); // floor at 0.5 ft to keep the test deterministic

                int samples = 0;
                int uncovered = 0;
                double worstGap = 0.0;
                double worstX = 0.0;
                double worstY = 0.0;
                double coverageThreshold = ruleSet.CoverageRadiusFt - config.ToleranceFt;

                for (double sy = geometry.MinY; sy <= geometry.MaxY + config.ToleranceFt; sy += sampleStep)
                {
                    for (double sx = geometry.MinX; sx <= geometry.MaxX + config.ToleranceFt; sx += sampleStep)
                    {
                        if (!geometry.IsPointInsideRoom(sx, sy, config.ToleranceFt)) continue;
                        // Skip sample points inside obstacles (those are not "room" to cover).
                        bool insideObstacle = false;
                        foreach (ObstacleBox box in obstacleBoxes)
                        {
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
                        foreach (double[] es in existingSprinklerXy)
                        {
                            double d = GeometryMath.Distance(sx, sy, es[0], es[1]);
                            if (d < nearest) nearest = d;
                        }
                        if (nearest > coverageThreshold)
                        {
                            uncovered++;
                            if (nearest > worstGap)
                            {
                                worstGap = nearest;
                                worstX = sx;
                                worstY = sy;
                            }
                        }
                    }
                }

                if (samples > 0 && uncovered > 0)
                {
                    double pct = (double)uncovered * 100.0 / samples;
                    result.Warnings.Add(
                        $"Coverage gap detected: {uncovered} of {samples} sample points ({pct:F1}%) " +
                        $"are beyond CoverageRadiusFt={ruleSet.CoverageRadiusFt:F2} ft from any placed " +
                        $"sprinkler. Worst gap at ({worstX:F2},{worstY:F2}) = {worstGap:F2} ft. " +
                        $"Review required (consider finer grid or relax MinSpacingFt).");
                    if (pct > 5.0)
                    {
                        result.Status = CalculationStatus.ReviewRequired;
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

        // ------------------------------------------------------------------
        // Sidewall candidate generation (NFPA 13 §11.3)
        // ------------------------------------------------------------------

        /// <summary>
        /// Generates wall-anchored candidate points for a sidewall sprinkler.
        /// Walks each edge of the room's outer polygon, projects
        /// <see cref="HazardPlacementRuleSet.BoundaryClearanceFt"/> inboard
        /// perpendicular to the edge, and steps along the edge at the (already
        /// orientation-adjusted) <see cref="HazardPlacementRuleSet.MaxSpacingFt"/>.
        /// Inward normal is disambiguated with the room's
        /// <see cref="RoomGeometry.IsPointInsideRoom"/> test.
        ///
        /// Candidates that fall outside the room (concave corners), too close
        /// to an obstacle, or too close to an existing sprinkler are rejected.
        /// The boundary-clearance lower bound is intentionally NOT applied —
        /// sidewall candidates are SUPPOSED to be near the wall.
        /// </summary>
        private static List<CandidatePoint> GenerateSidewallCandidates(
            PlacementRoomInput room,
            RoomGeometry geometry,
            HazardPlacementRuleSet ruleSet,
            List<ObstacleBox> obstacleBoxes,
            List<double[]> existingSprinklerXy,
            double placementZ,
            BruteForceCalculationConfig config,
            RoomCalculationResult result)
        {
            List<CandidatePoint> candidates = new List<CandidatePoint>();

            List<double[]> polygon = room.BoundaryPolygon;
            if (polygon == null || polygon.Count < 3)
            {
                if (room.Boundary != null && room.Boundary.OuterLoop != null &&
                    room.Boundary.OuterLoop.Polygon != null && room.Boundary.OuterLoop.Polygon.Count >= 3)
                {
                    polygon = room.Boundary.OuterLoop.Polygon;
                }
            }
            if (polygon == null || polygon.Count < 3) return candidates;

            double standOff = ruleSet.BoundaryClearanceFt > 0 ? ruleSet.BoundaryClearanceFt : 0.5;
            double step = ruleSet.MaxSpacingFt > 0 ? ruleSet.MaxSpacingFt : 12.0;

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

                double ux = ex / edgeLen;
                double uy = ey / edgeLen;

                // Two candidate normals (left-hand + right-hand perpendicular).
                // Disambiguate by IsPointInsideRoom: the inboard side is the one
                // where midpoint + 0.5 * n is inside the room.
                double nxLeft = -uy;
                double nyLeft = ux;
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
                    double baseX = a[0] + ex * t;
                    double baseY = a[1] + ey * t;
                    double cx = baseX + nx * standOff;
                    double cy = baseY + ny * standOff;
                    totalGenerated++;

                    if (!geometry.IsPointInsideRoom(cx, cy, config.ToleranceFt))
                    {
                        rejectedOutside++;
                        continue;
                    }

                    bool hitObstacle = false;
                    foreach (ObstacleBox box in obstacleBoxes)
                    {
                        if (GeometryMath.InsideExpandedBox(cx, cy, box.MinX, box.MinY, box.MaxX, box.MaxY, box.ClearanceFt))
                        {
                            if (!box.SpansZ(placementZ, config.ToleranceFt)) continue;
                            rejectedObstacle++;
                            hitObstacle = true;
                            break;
                        }
                    }
                    if (hitObstacle) continue;

                    bool hitExisting = false;
                    foreach (double[] es in existingSprinklerXy)
                    {
                        if (GeometryMath.Distance(cx, cy, es[0], es[1]) <= ruleSet.ExistingSprinklerSeparationFt - config.ToleranceFt)
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
                        Z = placementZ,
                        IsValid = true,
                        Score = 1.0
                    });
                }
            }

            result.Diagnostics.Add(
                $"Sidewall candidates: generated={totalGenerated}, valid={candidates.Count}, " +
                $"rejected(outside={rejectedOutside}, obstacle={rejectedObstacle}, existing={rejectedExisting}). " +
                $"StandOff={standOff:F2} ft, step={step:F2} ft.");
            return candidates;
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

        /// <summary>
        /// True when the selected sprinkler family's placement behavior REQUIRES a real
        /// ceiling or work-plane host face to anchor each candidate. Level-hosted families
        /// (e.g. <see cref="DevicePlacementBehavior.LevelHosted"/>) are exempt — they sit
        /// on a level plane and do not need a ceiling. The default <c>Unknown</c> /
        /// <c>Unsupported</c> is treated as hosted (conservative) so the room is blocked
        /// rather than producing unusable points.
        /// </summary>
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

            // Floor the grid step at half the coverage radius so every coverage diameter
            // is sampled by AT LEAST two candidate points. Without this floor a sparse
            // grid (e.g. 4 ft step on a 12 ft coverage radius) would let the post-selection
            // coverage-gap detector flag the room as under-covered. The floor is applied
            // ONLY when the room is at least 2 * floor wide in BOTH dimensions; otherwise
            // the floor would skip grid points that the original fine grid sampled (e.g.
            // a 30 ft x 3 ft room has only 2 y positions that clear the boundary).
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

        /// <summary>
        /// Deterministically picks the ceiling that should host this room's sprinklers.
        /// Decision order (in priority):
        ///   1. FLAT ceiling matching the room's level with the highest BottomElevationFt above floor Z.
        ///   2. SLOPED ceiling matching the room's level with the highest BottomElevationFt above floor Z.
        ///   3. STEPPED ceiling matching the room's level with the highest BottomElevationFt above floor Z.
        ///   4. Any FLAT ceiling with the highest BottomElevationFt above floor Z (level mismatch fallback).
        ///   5. Any SLOPED ceiling with the highest BottomElevationFt above floor Z.
        ///   6. Any ceiling whatsoever (last resort).
        /// A ceiling below floor Z is NEVER picked — that is a slab of the level below.
        /// Returns the picked ceiling and a human-readable selection reason (or null when no ceiling qualifies).
        /// </summary>
        private static CeilingData SelectPrimaryCeiling(PlacementRoomInput room, out string reason, RoomCalculationResult result)
        {
            reason = null;
            if (room?.Ceilings == null || room.Ceilings.Count == 0) return null;

            double floorZ = room.LevelElevationFt;
            string roomLevelId = room.LevelId;

            // A candidate must be a real ceiling with a measured bottom elevation, located above the
            // room's floor (a slab below the floor is structural, not a ceiling).
            List<CeilingData> candidates = new List<CeilingData>();
            foreach (CeilingData c in room.Ceilings)
            {
                if (c == null) continue;
                if (!c.BottomElevationFt.HasValue) continue;
                if (c.BottomElevationFt.Value <= floorZ + 0.05) continue; // tolerate 0.05 ft slop
                candidates.Add(c);
            }

            if (candidates.Count == 0) return null;

            // Priority buckets, in descending preference.
            string[] slopePriority = { "FLAT", "SLOPED", "STEPPED" };

            for (int pass = 0; pass < 2; pass++)
            {
                bool requireLevelMatch = (pass == 0);

                foreach (string slope in slopePriority)
                {
                    CeilingData best = null;
                    foreach (CeilingData c in candidates)
                    {
                        if (!string.Equals(c.SlopeType, slope, StringComparison.OrdinalIgnoreCase)) continue;
                        if (requireLevelMatch)
                        {
                            if (string.IsNullOrEmpty(c.LevelId)) continue;
                            if (!string.Equals(c.LevelId, roomLevelId, StringComparison.OrdinalIgnoreCase)) continue;
                        }
                        if (best == null || c.BottomElevationFt.Value > best.BottomElevationFt.Value) best = c;
                    }

                    if (best != null)
                    {
                        reason = $"Primary ceiling: slope={best.SlopeType}, BottomElevationFt={best.BottomElevationFt.Value:F2}, levelMatch={requireLevelMatch}.";
                        return best;
                    }
                }
            }

            // No ceiling matched any of the preferred buckets with a level match; pick the
            // highest Z ceiling regardless of slope/level (better than no ceiling at all).
            CeilingData fallback = null;
            foreach (CeilingData c in candidates)
            {
                if (fallback == null || c.BottomElevationFt.Value > fallback.BottomElevationFt.Value) fallback = c;
            }
            if (fallback != null)
            {
                reason = $"Primary ceiling (fallback, no level match): slope={fallback.SlopeType}, BottomElevationFt={fallback.BottomElevationFt.Value:F2}.";
            }
            return fallback;
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

                double minZ = double.NaN;
                double maxZ = double.NaN;
                if (obstacle.BoundingBox != null
                    && obstacle.BoundingBox.Min != null
                    && obstacle.BoundingBox.Max != null)
                {
                    minZ = obstacle.BoundingBox.Min.Z;
                    maxZ = obstacle.BoundingBox.Max.Z;
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
                    ClearanceFt = ruleSet.GetObstacleClearance(obstacle.Category)
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

        // ------------------------------------------------------------------
        // Per-room rule overrides (Decision 018)
        // ------------------------------------------------------------------

        private static HazardPlacementRuleSet ApplyPerRoomOverrides(
            HazardPlacementRuleSet baseRuleSet,
            PlacementRoomInput room,
            RoomCalculationResult result)
        {
            if (baseRuleSet == null) return null;
            if (room == null) return baseRuleSet;
            if (!room.OverrideMaxSpacingFt.HasValue && !room.OverrideBoundaryClearanceFt.HasValue)
            {
                return baseRuleSet;
            }

            // Always clone when ANY override is present so the per-room mutations
            // (this method + the ceiling-height adjustment below) cannot leak back
            // into the base rule set returned by the rules provider.
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
            // NFPA 13 hard-limit ceilings per the current placeholder rule set (15 ft). The
            // current authoritative values are not yet encoded; the placeholder is the only
            // hard ceiling we can enforce. Engineers can request tighter (smaller) values.
            note = null;
            const double provisionalCeilingFt = 15.0;
            if (requestedFt > provisionalCeilingFt)
            {
                note = "Requested MaxSpacingFt " + requestedFt.ToString("F2")
                    + " ft exceeds the current provisional ceiling (" + provisionalCeilingFt.ToString("F2")
                    + " ft); clamped to " + provisionalCeilingFt.ToString("F2") + " ft.";
                return provisionalCeilingFt;
            }
            if (requestedFt <= 0.0)
            {
                note = "Requested MaxSpacingFt <= 0; rejected.";
                return 0.0;
            }
            return requestedFt;
        }

        private sealed class ObstacleBox
        {
            public double MinX { get; set; }
            public double MinY { get; set; }
            public double MaxX { get; set; }
            public double MaxY { get; set; }

            /// <summary>Obstacle bottom in feet. <c>double.NaN</c> when the obstacle has no Z info.</summary>
            public double MinZ { get; set; }

            /// <summary>Obstacle top in feet. <c>double.NaN</c> when the obstacle has no Z info.</summary>
            public double MaxZ { get; set; }

            public string Category { get; set; }
            /// <summary>
            /// Per-box clearance in feet. Resolved from
            /// <see cref="HazardPlacementRuleSet.ObstacleSpecificClearances"/> via
            /// <see cref="HazardPlacementRuleSet.GetObstacleClearance"/>; falls back
            /// to <see cref="HazardPlacementRuleSet.ObstacleClearanceFt"/> when the
            /// category is unknown.
            /// </summary>
            public double ClearanceFt { get; set; }

            /// <summary>
            /// True when the obstacle's vertical extent contains the candidate's
            /// placement Z (with a small tolerance). Beams that sit below ceiling
            /// will have MaxZ well below the placement plane and therefore do NOT
            /// block; ducts/soffits that reach up to the ceiling plane DO block.
            /// When the obstacle has no usable Z info (NaN or a collapsed
            /// <c>MinZ == MaxZ</c> range — the legacy "set Z to 0" default) the
            /// default is to assume it blocks. This preserves the pre-3D
            /// behaviour for legacy/test input that never populated Z properly.
            /// </summary>
            public bool SpansZ(double placementZ, double toleranceFt)
            {
                if (double.IsNaN(MinZ) || double.IsNaN(MaxZ)) return true;
                if (System.Math.Abs(MaxZ - MinZ) < 1e-6) return true;
                return placementZ >= MinZ - toleranceFt
                    && placementZ <= MaxZ + toleranceFt;
            }
        }
    }
}
