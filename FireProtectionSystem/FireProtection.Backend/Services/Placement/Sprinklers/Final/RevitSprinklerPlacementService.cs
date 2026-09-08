using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.Strategies;
using FireProtection.UI.Models;
using FireProtection.UI.Models.Sprinklers.BruteForce;
using FireProtection.UI.Services;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final
{
    /// <summary>
    /// Authoritative Revit placement service. Resolves the selected sprinkler FamilySymbol from the
    /// active host document, activates it once, and creates real <see cref="FamilyInstance"/> elements
    /// for every calculated point using the correct placement overload for the family's host behavior.
    /// Revit API usage is isolated here; the calculation layer and its models remain Revit-independent.
    /// </summary>
    public class RevitSprinklerPlacementService : ISprinklerPlacementService
    {
        private readonly Document _hostDocument;
        private readonly RevitSprinklerPlacementConfig _config;
        private readonly CeilingHostResolver _ceilingHostResolver;
        private readonly IReadOnlyList<IFamilyPlacementStrategy> _strategies;

        /// <summary>Timestamp written into every traceability stamp of this run (one value per service instance,
        /// so all elements from one Place share an identifiable batch marker).</summary>
        private readonly string _runStampUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

        public RevitSprinklerPlacementService(Document hostDocument)
            : this(hostDocument, RevitSprinklerPlacementConfig.Default())
        {
        }

        public RevitSprinklerPlacementService(Document hostDocument, RevitSprinklerPlacementConfig config)
        {
            _hostDocument = hostDocument ?? throw new ArgumentNullException(nameof(hostDocument));
            _config = config ?? RevitSprinklerPlacementConfig.Default();
            _ceilingHostResolver = new CeilingHostResolver();

            // Ordered strategy set. Selection is by the family's PROVEN FamilyPlacementType via CanHandle;
            // there is no silent substitution and no default-to-level for unknown types (hard rules 5, 6, 7).
            // WallSidewall is registered last — it is dispatched by DevicePlacementBehavior, not by
            // FamilyPlacementType (wall-mounted families often have WorkPlaneBased or FaceBased type).
            _strategies = new IFamilyPlacementStrategy[]
            {
                new Strategies.FaceBasedPlacementStrategy(),
                new Strategies.WorkPlaneBasedPlacementStrategy(),
                new Strategies.LevelBasedPlacementStrategy(),
                new Strategies.WallSidewallPlacementStrategy()
            };
        }

        public SprinklerPlacementResult PlaceSprinklers(
            string selectedFamilyName,
            string selectedTypeName,
            BruteForceCalculationResult calcResult,
            IPlacementProgress progress = null,
            ExistingDevicePolicy existingDevicePolicy = ExistingDevicePolicy.SkipRoom)
        {
            if (progress == null) progress = NullPlacementProgress.Instance;

            var result = new SprinklerPlacementResult
            {
                TimestampUtc = DateTime.UtcNow.ToString("o"),
                ProjectName = SafeTitle(_hostDocument),
                SelectedFamily = selectedFamilyName,
                SelectedType = selectedTypeName,
                RoomsProcessed = calcResult?.Rooms?.Count ?? 0,
                CalculatedSprinklerCount = calcResult?.TotalCalculatedSprinklers ?? 0
            };

            if (calcResult == null || calcResult.Rooms == null || calcResult.Rooms.Count == 0)
            {
                result.Warnings.Add("No calculation result or rooms available to place.");
                return result;
            }

            if (string.IsNullOrWhiteSpace(selectedFamilyName) || string.IsNullOrWhiteSpace(selectedTypeName))
            {
                result.Errors.Add("Selected sprinkler family/type name is missing.");
                return result;
            }

            FireProtectionLog.Info("Placement run started: " + result.RoomsProcessed + " room(s), "
                + result.CalculatedSprinklerCount + " calculated point(s), family '" + selectedFamilyName
                + "' / type '" + selectedTypeName + "', existing-device policy " + existingDevicePolicy + ".");

            // Decision 017: per-row family/type comes from RoomCalculationResult; the universal selection is
            // only a fallback for rooms that did not override it. Per-row resolution is done in the loop below.
            // Existing sprinklers are collected once, with element ids, because they drive three things: the
            // coincident-point guard, the "this room already has devices" policy and the Replace policy.
            List<ExistingSprinkler> existingSprinklers = CollectExistingSprinklers(_hostDocument);
            List<XYZ> existingPoints = _config.SkipNearDuplicates
                ? existingSprinklers.Select(e => e.Point).ToList()
                : new List<XYZ>();

            // One TransactionGroup around the run: it gives the whole run a single named undo entry and keeps
            // it atomic if it ever grows a second transaction. Cancel rolls the group back, so a cancelled run
            // leaves the model exactly as it was.
            using (TransactionGroup group = new TransactionGroup(_hostDocument, "Fire Protection: Place Sprinklers"))
            {
                group.Start();

                RunPlacementTransaction(
                    calcResult, selectedFamilyName, selectedTypeName,
                    existingSprinklers, existingPoints, progress, existingDevicePolicy, result);

                if (result.WasCancelled) group.RollBack();
                else group.Assimilate();
            }

            if (result.WasCancelled)
            {
                // The rollback undid every element, so the per-room detail now describes elements that do not
                // exist. Reporting rolled-back elements as "placed" would be a false success.
                result.Rooms.Clear();
                result.ReplacedExistingCount = 0;
                result.SkippedExistingRoomCount = 0;
                result.SkippedOutsideRoomCount = 0;
                result.SkippedMissingFamilyCount = 0;
            }

            result.PlacedSprinklerCount = result.Rooms.Sum(r => r.Placed.Count);
            result.PlacedAndValidCount = result.Rooms.Sum(r => r.Placed.Count(p => p.IsSpatiallyValid));
            result.PlacedButInvalidCount = result.Rooms.Sum(r => r.Placed.Count(p => !p.IsSpatiallyValid));
            result.FailedSprinklerCount = result.Rooms.Sum(r => r.Failed.Count);
            result.SkippedDuplicateCount = result.Rooms.Sum(r => r.Failed.Count(f => f.SkippedDueToDuplicate));

            FireProtectionLog.Info("Placement run finished: placed " + result.PlacedSprinklerCount
                + " (valid " + result.PlacedAndValidCount + ", invalid " + result.PlacedButInvalidCount
                + "), failed " + result.FailedSprinklerCount + ", rooms skipped " + result.SkippedExistingRoomCount
                + ", replaced " + result.ReplacedExistingCount + ", outside room " + result.SkippedOutsideRoomCount
                + (result.WasCancelled ? ", CANCELLED (rolled back)" : string.Empty) + ".");

            return result;
        }

        /// <summary>
        /// The placement transaction itself: activates symbols, walks the rooms, honours the existing-device
        /// policy, reports progress and stops at a room boundary when the user cancels.
        /// </summary>
        private void RunPlacementTransaction(
            BruteForceCalculationResult calcResult,
            string selectedFamilyName,
            string selectedTypeName,
            List<ExistingSprinkler> existingSprinklers,
            List<XYZ> existingSprinklerPoints,
            IPlacementProgress progress,
            ExistingDevicePolicy existingDevicePolicy,
            SprinklerPlacementResult result)
        {
            using (Transaction transaction = new Transaction(_hostDocument, "Place Sprinklers"))
            {
                transaction.Start();

                bool committed = false;
                try
                {
                    HashSet<string> activatedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    Dictionary<string, FamilySymbol> symbolCache =
                        new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
                    string resolvedPlacementType = null;
                    int totalRooms = calcResult.Rooms.Count;
                    int roomIndex = 0;

                    foreach (RoomCalculationResult room in calcResult.Rooms)
                    {
                        roomIndex++;

                        // Cancellation is polled only at room boundaries, never mid-element: no half-placed room is
                        // ever left behind, and the group rollback in the caller undoes everything placed so far.
                        if (progress.IsCancellationRequested)
                        {
                            result.WasCancelled = true;
                            FireProtectionLog.Warn("Placement cancelled by the user after "
                                + (roomIndex - 1) + " of " + totalRooms + " room(s); rolling the run back.");
                            break;
                        }

                        progress.Report(roomIndex - 1, totalRooms,
                            "Room " + roomIndex + " of " + totalRooms + ": " + (room.RoomName ?? room.RoomId));

                        PlacementRoomResult roomResult = new PlacementRoomResult
                        {
                            RoomId = room.RoomId,
                            RoomName = room.RoomName,
                            RoomNumber = room.RoomNumber
                        };

                        if (room.Points == null || room.Points.Count == 0)
                        {
                            result.Rooms.Add(roomResult);
                            continue;
                        }

                        // Re-run handling: what a second Place does to a room that already contains sprinklers.
                        List<ExistingSprinkler> inRoom = ExistingSprinklersInRoom(existingSprinklers, room);
                        if (inRoom.Count > 0)
                        {
                            if (existingDevicePolicy == ExistingDevicePolicy.SkipRoom)
                            {
                                result.SkippedExistingRoomCount++;
                                result.Warnings.Add("Room '" + (room.RoomName ?? room.RoomId) + "' already has "
                                    + inRoom.Count + " sprinkler(s) - left untouched (policy: skip).");
                                result.Rooms.Add(roomResult);
                                continue;
                            }

                            if (existingDevicePolicy == ExistingDevicePolicy.ReplaceExisting)
                            {
                                result.ReplacedExistingCount +=
                                    DeleteExistingSprinklers(inRoom, existingSprinklers, existingSprinklerPoints, result);
                            }
                            // AddAnyway: fall through - the coincident-point guard in PlaceSinglePoint still applies.
                        }

                        string family = !string.IsNullOrEmpty(room.SprinklerFamilyName) ? room.SprinklerFamilyName : selectedFamilyName;
                        string type = !string.IsNullOrEmpty(room.SprinklerTypeName) ? room.SprinklerTypeName : selectedTypeName;
                        string key = family + "::" + type;

                        FamilySymbol symbol;
                        if (!symbolCache.TryGetValue(key, out symbol) || symbol == null)
                        {
                            string symbolError;
                            symbol = ResolveSymbol(_hostDocument, family, type, out symbolError);
                            if (symbol == null)
                            {
                                result.SkippedMissingFamilyCount++;
                                result.Warnings.Add(
                                    "Skipped room '" + (room.RoomName ?? room.RoomId)
                                    + "': family '" + family + "' / type '" + type
                                    + "' is not loaded in the active Revit document ("
                                    + (symbolError ?? "not found") + ").");
                                result.Rooms.Add(roomResult);
                                continue;
                            }
                            if (activatedKeys.Add(key) && !symbol.IsActive)
                            {
#pragma warning disable CS0618
                                symbol.Activate();
#pragma warning restore CS0618
                            }
                            symbolCache[key] = symbol;
                        }

                        string placementType = ResolvePlacementType(symbol, ref resolvedPlacementType);
                        if (string.IsNullOrEmpty(result.ResolvedFamilyPlacementType))
                        {
                            result.ResolvedFamilyPlacementType = placementType;
                        }

                        foreach (CalculatedSprinklerPoint point in room.Points)
                        {
                            // Coordinate-space guard: a candidate must fall inside its own room polygon in HOST
                            // coordinates. One that does not is almost always an untransformed linked-model point,
                            // and creating it would drop a sprinkler far outside the room.
                            if (!IsPointInsideRoom(point, room))
                            {
                                result.SkippedOutsideRoomCount++;
                                roomResult.Failed.Add(new FailedSprinklerEntry
                                {
                                    X = point.X,
                                    Y = point.Y,
                                    Z = point.Z,
                                    RoomId = room.RoomId,
                                    LevelId = point.LevelId,
                                    ErrorCode = PlacementStatusCodes.OutsideRoomBoundary,
                                    Reason = "Point falls outside its own room boundary in host coordinates - refused "
                                        + "(check the linked-model coordinate transform)."
                                });
                                continue;
                            }

                            PlaceSinglePoint(
                                _hostDocument,
                                symbol,
                                placementType,
                                point,
                                existingSprinklerPoints,
                                roomResult,
                                result,
                                room);
                        }

                        result.Rooms.Add(roomResult);
                    }

                    transaction.Commit();
                    committed = true;
                }
                catch (Exception ex)
                {
                    result.Errors.Add("Transaction failed: " + ex.Message);
                    FireProtectionLog.Error("Sprinkler placement transaction failed.", ex);
                    if (!committed)
                    {
                        try { transaction.RollBack(); }
                        catch { /* best-effort rollback */ }
                    }
                }
            }
        }

        private string ResolvePlacementType(FamilySymbol symbol, ref string cached)
        {
            if (cached != null) return cached;
            try { cached = symbol?.Family?.FamilyPlacementType.ToString() ?? "None"; }
            catch { cached = "Unknown"; }
            return cached;
        }

        // ----- Pre-placement eligibility / preflight (single source of truth with actual placement) -----
        // The UI consumes this so it can block rooms the production pipeline provably cannot place. It reuses
        // the EXACT family-resolution, strategy-selection, level-resolution and ceiling-host logic that
        // PlaceSinglePoint uses — no second, drifting set of hosting rules.

        private string _cachedFamilyKey;
        private FamilySymbol _cachedSymbol;
        private string _cachedPlacementType;
        private IFamilyPlacementStrategy _cachedStrategy;

        // Eligibility probe results are cached per (family/type/room) so repeated evaluations (e.g. on
        // filtering or selection refresh) do not re-run the Revit transactional probe. The key includes the
        // family and type, so a family/type change naturally misses the cache; ClearEligibilityCache() also
        // exists for explicit invalidation when level/linked-model state changes.
        private readonly Dictionary<string, PlacementEligibilityResult> _eligibilityCache =
            new Dictionary<string, PlacementEligibilityResult>();

        /// <inheritdoc />
        public PlacementEligibilityResult EvaluateRoomEligibility(
            RoomUiData room,
            IReadOnlyList<CalculatedSprinklerPoint> candidates,
            string selectedFamilyName,
            string selectedTypeName)
        {
            // Fail closed: without a usable boundary we can never place anything here.
            if (room?.Geometry == null || room.Geometry.Polygon == null || room.Geometry.Polygon.Count < 3)
            {
                return PlacementEligibilityResult.Blocked(
                    PlacementEligibilityStatusCodes.MissingRoomGeometry,
                    "Room boundary is incomplete (at least 3 points required for placement).");
            }

            // §15: the eligibility cache key must include every eligibility-affecting input. Room identity is
            // the primary key, but a bare element-based RoomId can collide across linked models (Revit element
            // ids are only unique within a single document), so the room Name + LevelName are folded in to keep
            // linked rooms from returning another model's cached result. (A fully link-unique key also threading
            // LinkInstanceId end-to-end is the recommended follow-up — see SESSION_NOTES.)
            string hazardKey = room?.Classification?.HazardClass ?? "?";
            string familyKey = (selectedFamilyName ?? string.Empty) + "::" + (selectedTypeName ?? string.Empty);
            string identity = (room.RoomId ?? room.Name ?? "?")
                + "::" + (room.Name ?? "?")
                + "::" + (room.LevelName ?? "?");
            string cacheKey = familyKey + "::" + hazardKey + "::" + identity;
            if (_eligibilityCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var result = new PlacementEligibilityResult();
            result.HazardClass = room?.Classification?.HazardClass;

            try
            {
                var (symbol, placementType, strategy, error) = ResolveFamily(selectedFamilyName, selectedTypeName);
                result.FamilyPlacementType = placementType;

                if (symbol == null)
                {
                    // Family/type not resolved is a CONFIGURATION error, not proof the room is unplaceable.
                    // Per master prompt §19 it must NOT be converted into "every room is non-eligible" -> UNDETERMINED.
                    return CacheAndReturn(cacheKey, PlacementEligibilityResult.Undetermined(
                        error ?? $"Sprinkler family/type '{selectedFamilyName}:{selectedTypeName}' was not found.",
                        PlacementEligibilityStatusCodes.UnsupportedFamilyPlacement));
                }

                if (strategy == null)
                {
                    return CacheAndReturn(cacheKey, PlacementEligibilityResult.Blocked(
                        result,
                        PlacementEligibilityStatusCodes.UnsupportedFamilyPlacementType,
                        $"No placement strategy supports the family's placement type '{placementType}'."));
                }

                if (candidates == null)
                {
                    // Null candidates means the caller could not run/produce the calculation at all (insufficient
                    // evidence to decide) — this is UNDETERMINED, never BLOCKED.
                    return CacheAndReturn(cacheKey, PlacementEligibilityResult.Undetermined(
                        "Candidate point calculation could not be run for this room; eligibility is undetermined.",
                        PlacementEligibilityStatusCodes.CalculationFailed));
                }

                if (candidates.Count == 0)
                {
                    // Empty candidates is deterministic: the calculation completed and decided nothing can be placed.
                    return CacheAndReturn(cacheKey, PlacementEligibilityResult.Blocked(
                        result,
                        PlacementEligibilityStatusCodes.NoCandidatePoints,
                        "The BruteForce calculation produced no candidate sprinkler points for this room; " +
                        "nothing can be placed here."));
                }

                // --- DETERMINISTIC FEASIBILITY PREFLIGHT (NO instance is created; mirrors placement prerequisites) ---
                // Reuses the SAME resolvers placement uses (ResolveHostLevel / CeilingHostResolver.FindCeilingHost),
                // but performs only READ-ONLY queries — no transaction, no FamilyInstance creation (master prompt §8).
                // This is the single source of truth for "can this room receive a sprinkler with the current config".
                //
                // Host requirement by proven FamilyPlacementType:
                //   FaceBased      -> REQUIRES a usable ceiling/host face for >=1 candidate.
                //   WorkPlaneBased -> REQUIRES a usable ceiling/host face for >=1 candidate. Its SketchPlane
                //                     fallback (SPRINKLER_ACTUAL_Z_DIAGNOSTIC / the yfbxcv 1234 failure) is the
                //                     documented, unreliable path, so the preflight must require a real ceiling host
                //                     rather than trusting the fallback that already failed in production.
                //   OneLevelBased  -> requires only a resolvable level (legitimately placed without a ceiling).
                //
                // WallSidewall behavior overrides: a wall-mounted family does NOT need a ceiling host
                // regardless of FamilyPlacementType — it needs a wall face.
                //
                // A numeric "Ceiling Height" (CeilingHeightFt) is NEVER treated as proof of a usable host (§4).
                bool isSidewallBehavior = string.Equals(
                    room?.SelectedSprinklerPlacementBehavior,
                    "WallSidewall",
                    StringComparison.OrdinalIgnoreCase);
                bool requiresCeilingHost = !isSidewallBehavior && (
                    string.Equals(placementType, "FaceBased", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(placementType, "WorkPlaneBased", StringComparison.OrdinalIgnoreCase));

                bool anyLevelResolved = false;
                bool anyHostOk = false;
                int validCandidateCount = 0;
                string firstLevelFailure = null;
                string hostSource = null;
                string linkInstanceName = null;
                string hostCeilingElementId = null;
                string hostLevelId = null;
                string hostLevelName = null;
                int candidateIndex = 0;

                foreach (CalculatedSprinklerPoint c in candidates)
                {
                    candidateIndex++;
                    if (c == null || !IsFinite(c.X, c.Y, c.Z)) continue;

                    LevelResolution levelRes = ResolveHostLevel(_hostDocument, c.LevelId, c.LevelName);
                    if (!levelRes.Resolved)
                    {
                        if (firstLevelFailure == null)
                            firstLevelFailure = levelRes.FailureReason
                                ?? "The candidate's host level could not be resolved.";
                        continue;
                    }

                    anyLevelResolved = true;
                    Level level = levelRes.HostLevel;
                    XYZ xyz = new XYZ(c.X, c.Y, c.Z);

                    // Read-only host lookup (NO placement). For ceiling-requiring families this is the gating check.
                    CeilingHostLookup host = null;
                    try { host = _ceilingHostResolver.FindCeilingHost(_hostDocument, xyz, level); }
                    catch { /* host lookup is best-effort; never fail eligibility on it */ }

                    bool hasHostFace = host != null && host.HostFace != null;

                    if (requiresCeilingHost && !hasHostFace)
                        continue; // this candidate has no usable ceiling host; try the next candidate

                    // This candidate satisfies every placement prerequisite: a resolved level AND (if required) a
                    // usable ceiling host.
                    anyHostOk = true;
                    validCandidateCount++;
                    hostSource = host?.Source ?? "none";
                    linkInstanceName = host?.LinkInstanceName;
                    hostCeilingElementId = host?.CeilingElementId;
                    hostLevelId = level.Id.ToString();
                    hostLevelName = level.Name;
                }

                if (!anyLevelResolved)
                {
                    // No candidate has a resolvable host level -> deterministic inability for this room.
                    return CacheAndReturn(cacheKey, PlacementEligibilityResult.Blocked(
                        result,
                        PlacementEligibilityStatusCodes.MissingHostLevel,
                        firstLevelFailure ?? "No candidate point has a resolvable host level."));
                }

                if (!anyHostOk)
                {
                    // A level resolved, but the room cannot satisfy the selected family's hosting requirement.
                    if (requiresCeilingHost)
                    {
                        return CacheAndReturn(cacheKey, PlacementEligibilityResult.Blocked(
                            result,
                            PlacementEligibilityStatusCodes.NoUsableCeilingHost,
                            "No usable ceiling host was found for the selected sprinkler family/type."));
                    }

                    // Non-ceiling-requiring family with no feasible candidate (every candidate rejected by an
                    // unexpected precondition despite a resolved level).
                    return CacheAndReturn(cacheKey, PlacementEligibilityResult.Blocked(
                        result,
                        PlacementEligibilityStatusCodes.NoCandidatePoints,
                        "No candidate point could be placed for the selected sprinkler family/type."));
                }

                // ELIGIBLE: at least one candidate satisfies every placement prerequisite.
                if (string.Equals(placementType, "FaceBased", StringComparison.OrdinalIgnoreCase))
                    result.HostingStrategy = "FaceBasedHost";
                else if (string.Equals(placementType, "WorkPlaneBased", StringComparison.OrdinalIgnoreCase))
                    result.HostingStrategy = "WorkPlaneCeilingFace";
                else
                    result.HostingStrategy = "LevelBased";
                result.CeilingSource = hostSource;
                result.HostSource = hostSource;
                result.LinkInstanceName = linkInstanceName;
                result.HostCeilingElementId = hostCeilingElementId;
                result.HostLevelId = hostLevelId;
                result.HostLevelName = hostLevelName;
                result.ValidCandidateCount = validCandidateCount;

                return CacheAndReturn(cacheKey, PlacementEligibilityResult.Eligible(result));
            }
            catch (Exception ex)
            {
                // An unexpected evaluation failure is UNDETERMINED, never BLOCKED (master prompt §3/§19).
                return CacheAndReturn(cacheKey, PlacementEligibilityResult.Undetermined(
                    "Preflight evaluation failed: " + ex.Message,
                    PlacementEligibilityStatusCodes.PreflightError));
            }
        }

        private PlacementEligibilityResult CacheAndReturn(string key, PlacementEligibilityResult value)
        {
            _eligibilityCache[key] = value;
            return value;
        }

        /// <inheritdoc />
        public void ClearEligibilityCache()
        {
            _eligibilityCache.Clear();
        }

        /// <inheritdoc />
        public IReadOnlyList<MissingFamilyEntry> ProbeMissingFamilies(BruteForceCalculationResult calcResult)
        {
            List<MissingFamilyEntry> missing = new List<MissingFamilyEntry>();
            if (calcResult == null || calcResult.Rooms == null) return missing;

            HashSet<string> checkedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (RoomCalculationResult room in calcResult.Rooms)
            {
                if (room == null) continue;
                if (room.Points == null || room.Points.Count == 0) continue;

                string family = room.SprinklerFamilyName;
                string type = room.SprinklerTypeName;
                if (string.IsNullOrEmpty(family) || string.IsNullOrEmpty(type)) continue;

                string key = family + "::" + type;
                if (!checkedKeys.Add(key)) continue;

                string error;
                if (ResolveSymbol(_hostDocument, family, type, out error) == null)
                {
                    missing.Add(new MissingFamilyEntry
                    {
                        RoomId = room.RoomId,
                        RoomName = room.RoomName,
                        FamilyName = family,
                        TypeName = type
                    });
                }
            }

            return missing;
        }

        private (FamilySymbol Symbol, string PlacementType, IFamilyPlacementStrategy Strategy, string Error) ResolveFamily(
            string familyName,
            string typeName)
        {
            string key = (familyName ?? string.Empty) + "::" + (typeName ?? string.Empty);
            if (_cachedFamilyKey == key && _cachedSymbol != null)
            {
                return (_cachedSymbol, _cachedPlacementType, _cachedStrategy, null);
            }

            string error;
            FamilySymbol symbol = ResolveSymbol(_hostDocument, familyName, typeName, out error);
            string placementType = "Unknown";
            IFamilyPlacementStrategy strategy = null;

            if (symbol != null)
            {
                try { placementType = symbol.Family?.FamilyPlacementType.ToString() ?? "None"; }
                catch { placementType = "Unknown"; }

                strategy = _strategies.FirstOrDefault(s => s.CanHandle(placementType));
            }

            _cachedFamilyKey = symbol != null ? key : null;
            _cachedSymbol = symbol;
            _cachedPlacementType = placementType;
            _cachedStrategy = strategy;

            return (symbol, placementType, strategy, error);
        }

        private void PlaceSinglePoint(
            Document doc,
            FamilySymbol symbol,
            string familyPlacementType,
            CalculatedSprinklerPoint point,
            IReadOnlyList<XYZ> existingSprinklerPoints,
            PlacementRoomResult roomResult,
            SprinklerPlacementResult result,
            RoomCalculationResult room = null)
        {
            // Validate point before any Revit interaction.
            if (point == null || !IsFinite(point.X, point.Y, point.Z))
            {
                roomResult.Failed.Add(new FailedSprinklerEntry
                {
                    X = point?.X ?? 0,
                    Y = point?.Y ?? 0,
                    Z = point?.Z ?? 0,
                    RoomId = point?.RoomId,
                    LevelId = point?.LevelId,
                    ErrorCode = PlacementStatusCodes.NonFiniteCoordinates,
                    Reason = "Calculated point has non-finite XYZ coordinates."
                });
                return;
            }

            XYZ xyz = new XYZ(point.X, point.Y, point.Z);

            // Minimal duplicate safety check (does not re-implement BruteForce spacing).
            if (_config.SkipNearDuplicates && existingSprinklerPoints.Any(p => p != null && p.DistanceTo(xyz) <= _config.DuplicateProximityFt))
            {
                roomResult.Failed.Add(new FailedSprinklerEntry
                {
                    X = point.X,
                    Y = point.Y,
                    Z = point.Z,
                    RoomId = point.RoomId,
                    LevelId = point.LevelId,
                    ErrorCode = PlacementStatusCodes.SkippedDuplicate,
                    Reason = $"Existing sprinkler within {_config.DuplicateProximityFt:F2} ft; duplicate not created.",
                    SkippedDueToDuplicate = true
                });
                return;
            }

            // Resolve the host level (handles linked-model levels via elevation/name mapping).
            LevelResolution levelResolution = ResolveHostLevel(doc, point.LevelId, point.LevelName);
            if (!levelResolution.Resolved)
            {
                roomResult.Failed.Add(new FailedSprinklerEntry
                {
                    X = point.X,
                    Y = point.Y,
                    Z = point.Z,
                    RoomId = point.RoomId,
                    LevelId = point.LevelId,
                    ErrorCode = PlacementStatusCodes.LevelResolutionFailed,
                    Reason = levelResolution.FailureReason,
                    SourceDocument = levelResolution.SourceDocument,
                    SourceLevelName = levelResolution.SourceLevelName,
                    SourceLevelId = levelResolution.SourceLevelId,
                    SourceElevationFt = levelResolution.SourceElevationFt,
                    HostDocument = levelResolution.HostDocument,
                    AttemptedHostLevelMatches = levelResolution.AttemptedHostLevelMatches
                });
                return;
            }

            Level level = levelResolution.HostLevel;

            // §13: select the strategy for the PROVEN placement type. No match => refuse to place; an
            // unknown/unsupported type is NEVER defaulted onto a Level (hard rules 5, 6, 7).
            //
            // Sidewall override: when the point carries a WallEdgeIndex, it was generated as a
            // wall-mounted candidate — use WallSidewallPlacementStrategy regardless of the
            // family's FamilyPlacementType (wall families often report WorkPlaneBased or FaceBased).
            IFamilyPlacementStrategy strategy = null;
            if (point.WallEdgeIndex.HasValue)
            {
                strategy = _strategies.FirstOrDefault(s => s is Strategies.WallSidewallPlacementStrategy);
            }
            if (strategy == null)
            {
                for (int i = 0; i < _strategies.Count; i++)
                {
                    if (_strategies[i].CanHandle(familyPlacementType))
                    {
                        strategy = _strategies[i];
                        break;
                    }
                }
            }

            if (strategy == null)
            {
                roomResult.Failed.Add(new FailedSprinklerEntry
                {
                    X = point.X,
                    Y = point.Y,
                    Z = point.Z,
                    RoomId = point.RoomId,
                    LevelId = point.LevelId,
                    ErrorCode = PlacementStatusCodes.UnsupportedFamilyPlacement,
                    Reason = $"No placement strategy supports FamilyPlacementType '{familyPlacementType}'. " +
                             "The family must be FaceBased, WorkPlaneBased, or OneLevelBased; the sprinkler was " +
                             "NOT placed (no silent substitution — hard rule 5).",
                    FamilyPlacementType = familyPlacementType,
                    SourceDocument = levelResolution.SourceDocument,
                    SourceLevelName = levelResolution.SourceLevelName,
                    SourceLevelId = levelResolution.SourceLevelId,
                    SourceElevationFt = levelResolution.SourceElevationFt,
                    HostDocument = levelResolution.HostDocument,
                    AttemptedHostLevelMatches = levelResolution.AttemptedHostLevelMatches
                });
                return;
            }

            try
            {
                var context = new PlacementContext
                {
                    Document = doc,
                    Symbol = symbol,
                    Level = level,
                    RequestedPoint = xyz,
                    FamilyPlacementType = familyPlacementType,
                    CeilingHostResolver = _ceilingHostResolver,
                    WallEdgeIndex = point.WallEdgeIndex,
                    RoomPolygon = room?.Polygon
                };

                PlacementOutcome outcome = strategy.Place(context);

                // A created instance is NOT success on its own (hard rule 8). A failed outcome carries a
                // structured ErrorCode; record it verbatim.
                if (!outcome.Created)
                {
                    roomResult.Failed.Add(new FailedSprinklerEntry
                    {
                        X = point.X,
                        Y = point.Y,
                        Z = point.Z,
                        RoomId = point.RoomId,
                        LevelId = point.LevelId,
                        ErrorCode = outcome.ErrorCode ?? PlacementStatusCodes.PlacementFailed,
                        Reason = outcome.Message ?? "Placement failed without a specific reason.",
                        FamilyPlacementType = familyPlacementType,
                        HostingStrategy = outcome.HostingStrategy,
                        CeilingSource = outcome.CeilingSource,
                        LinkInstanceName = outcome.LinkInstanceName,
                        HostCeilingElementId = outcome.HostCeilingElementId,
                        SourceDocument = levelResolution.SourceDocument,
                        SourceLevelName = levelResolution.SourceLevelName,
                        SourceLevelId = levelResolution.SourceLevelId,
                        SourceElevationFt = levelResolution.SourceElevationFt,
                        HostDocument = levelResolution.HostDocument,
                        AttemptedHostLevelMatches = levelResolution.AttemptedHostLevelMatches
                    });
                    return;
                }

                FamilyInstance instance = outcome.Instance;

                // Traceability stamp: write what produced this element onto the instance itself, so a reviewer
                // opening the model months later can tell tool-placed devices from hand-placed ones and can see
                // whether the geometry came from an approved rule set or the provisional placeholder.
                StampTraceability(instance, room);

                // --- §23 POST-PLACEMENT VALIDATION ---
                // Read the ACTUAL instance location and compare it with the requested point. A gross
                // deviation (e.g. a hosted family snapping to the project origin — the historic (0,0,0)
                // defect) yields PLACED_BUT_INVALID, never a silent success (hard rules 8, 19, 20).
                LocationPoint placedLocation = instance.Location as LocationPoint;
                XYZ actualLocation = placedLocation != null ? placedLocation.Point : null;

                bool locationKnown = actualLocation != null;
                double deviationFt = locationKnown ? actualLocation.DistanceTo(xyz) : 0.0;
                bool spatiallyValid = locationKnown && deviationFt <= _config.PlacementValidationToleranceFt;
                string statusCode = spatiallyValid
                    ? PlacementStatusCodes.PlacedAndValid
                    : PlacementStatusCodes.PlacedButInvalid;

                roomResult.Placed.Add(new PlacedSprinklerEntry
                {
                    X = actualLocation?.X ?? point.X,
                    Y = actualLocation?.Y ?? point.Y,
                    Z = actualLocation?.Z ?? point.Z,
                    RequestedX = point.X,
                    RequestedY = point.Y,
                    RequestedZ = point.Z,
                    RoomId = point.RoomId,
                    LevelId = point.LevelId,
                    HostLevelId = level.Id.ToString(),
                    HostLevelName = level.Name,
                    RevitElementId = instance.Id.ToString(),
                    HostingStrategy = outcome.HostingStrategy,
                    CeilingSource = outcome.CeilingSource,
                    StatusCode = statusCode,
                    IsSpatiallyValid = spatiallyValid,
                    PlacementDeviationFt = deviationFt,
                    FamilyPlacementType = familyPlacementType,
                    HostCeilingElementId = outcome.HostCeilingElementId,
                    LinkInstanceName = outcome.LinkInstanceName,
                    // ACTUAL host/level read back from the created instance (read-only; best-effort).
                    ActualHostElementId = ReadActualHostElementId(instance),
                    ActualHostName = ReadActualHostName(instance),
                    ActualInstanceLevelId = ReadActualInstanceLevelId(instance),
                    ActualInstanceLevelName = ReadActualInstanceLevelName(doc, instance),
                    ActualScheduleLevelName = ReadActualScheduleLevelName(doc, instance)
                });

                // Surface ONLY anomalies (no per-placement log spam — §47). Every invalid placement is
                // both counted (PlacedButInvalidCount, which fails result.Success) and explained here.
                if (!spatiallyValid)
                {
                    string detail = locationKnown
                        ? $"actual ({actualLocation.X:F2},{actualLocation.Y:F2},{actualLocation.Z:F2}) is {deviationFt:F2} ft from requested " +
                          $"({point.X:F2},{point.Y:F2},{point.Z:F2}); tolerance {_config.PlacementValidationToleranceFt:F2} ft"
                        : "the created instance exposes no LocationPoint, so its placed position cannot be verified";
                    result.Warnings.Add(
                        $"[REVIEW] Instance {instance.Id} (room {point.RoomId}, strategy {outcome.HostingStrategy}) " +
                        $"placed but spatially INVALID: {detail}.");
                }
            }
            catch (Exception ex)
            {
                roomResult.Failed.Add(new FailedSprinklerEntry
                {
                    X = point.X,
                    Y = point.Y,
                    Z = point.Z,
                    RoomId = point.RoomId,
                    LevelId = point.LevelId,
                    ErrorCode = PlacementStatusCodes.RevitCreationFailed,
                    Reason = "Revit API placement failed: " + ex.Message,
                    FamilyPlacementType = familyPlacementType,
                    HostingStrategy = strategy.Name,
                    SourceDocument = levelResolution.SourceDocument,
                    SourceLevelName = levelResolution.SourceLevelName,
                    SourceLevelId = levelResolution.SourceLevelId,
                    SourceElevationFt = levelResolution.SourceElevationFt,
                    HostDocument = levelResolution.HostDocument,
                    AttemptedHostLevelMatches = levelResolution.AttemptedHostLevelMatches,
                    ExceptionDetail = ex.ToString()
                });
            }
        }

        private static FamilySymbol ResolveSymbol(Document doc, string familyName, string typeName, out string error)
        {
            error = null;
            if (doc == null) { error = "Host document is unavailable."; return null; }

            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Sprinklers)
                .OfClass(typeof(FamilySymbol));

            FamilySymbol match = null;
            foreach (Element element in collector)
            {
                if (element is FamilySymbol sym)
                {
                    string fam = sym.FamilyName ?? sym.Family?.Name;
                    if (string.Equals(fam, familyName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(sym.Name, typeName, StringComparison.OrdinalIgnoreCase))
                    {
                        match = sym;
                        break;
                    }
                }
            }

            if (match == null)
            {
                error = $"Sprinkler family/type '{familyName}:{typeName}' was not found in the active document.";
            }

            return match;
        }

        /// <summary>
        /// Result of mapping a (possibly linked-model) level identifier to a real host-document Level.
        /// Captures the diagnostics required when resolution fails.
        /// </summary>
        private class LevelResolution
        {
            public Level HostLevel;
            public bool Resolved;
            public string FailureReason;
            public string SourceDocument;
            public string SourceLevelName;
            public string SourceLevelId;
            public double? SourceElevationFt;
            public string HostDocument;
            public string AttemptedHostLevelMatches;
        }

        /// <summary>
        /// Maps the level identifier carried by a calculated point to the correct Level in the active
        /// host document. The point's LevelId may be a host-document ElementId, OR it may belong to a
        /// linked architectural model (in which case it cannot be resolved directly). For linked levels
        /// we locate the Level in the link document, transform its elevation into host space via the
        /// RevitLinkInstance transform, then match the corresponding host Level by elevation (or name).
        /// No arbitrary level is ever selected silently.
        /// </summary>
        private LevelResolution ResolveHostLevel(Document doc, string levelId, string levelName)
        {
            var res = new LevelResolution { HostDocument = SafeTitle(doc) };

            if (doc == null)
            {
                res.FailureReason = "Host document unavailable.";
                return res;
            }

            // 1. Direct host-document ElementId (host-model room fast path).
            if (!string.IsNullOrWhiteSpace(levelId) && long.TryParse(levelId, out long idValue))
            {
                Level hostLevel = doc.GetElement(new ElementId(idValue)) as Level;
                if (hostLevel != null)
                {
                    res.HostLevel = hostLevel;
                    res.Resolved = true;
                    res.SourceLevelName = hostLevel.Name;
                    res.SourceLevelId = levelId;
                    res.SourceDocument = res.HostDocument;
                    return res;
                }
            }

            // 2. Linked-model level: search each RevitLinkInstance's document for the ElementId (or name),
            //    then map to a host Level using the transformed elevation.
            var attempted = new List<string>();

            FilteredElementCollector linkCollector = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance));
            foreach (Element element in linkCollector)
            {
                if (!(element is RevitLinkInstance linkInstance)) continue;

                Document linkDoc = linkInstance.GetLinkDocument();
                if (linkDoc == null) continue;

                Level linkLevel = null;
                if (!string.IsNullOrWhiteSpace(levelId) && long.TryParse(levelId, out long linkIdValue))
                {
                    linkLevel = linkDoc.GetElement(new ElementId(linkIdValue)) as Level;
                }

                if (linkLevel == null && !string.IsNullOrWhiteSpace(levelName))
                {
                    linkLevel = FindHostLevelByName(linkDoc, levelName, null);
                }

                if (linkLevel == null) continue;

                res.SourceDocument = linkDoc.Title ?? linkInstance.Name;
                res.SourceLevelName = linkLevel.Name;
                res.SourceLevelId = linkLevel.Id.ToString();

                // Transform the linked level elevation into host coordinate space (exactly once).
                double linkElevation = linkLevel.Elevation;
                Transform linkTransform = linkInstance.GetTotalTransform();
                double hostElevation = linkTransform.OfPoint(new XYZ(0, 0, linkElevation)).Z;
                res.SourceElevationFt = hostElevation;

                Level mapped = FindHostLevelByElevation(doc, hostElevation, attempted);
                if (mapped != null)
                {
                    res.HostLevel = mapped;
                    res.Resolved = true;
                    return res;
                }

                Level mappedByName = FindHostLevelByName(doc, levelName ?? linkLevel.Name, attempted);
                if (mappedByName != null)
                {
                    res.HostLevel = mappedByName;
                    res.Resolved = true;
                    return res;
                }
            }

            // 3. Last resort: host-document name match (no link context available).
            if (!string.IsNullOrWhiteSpace(levelName))
            {
                Level byName = FindHostLevelByName(doc, levelName, attempted);
                if (byName != null)
                {
                    res.HostLevel = byName;
                    res.Resolved = true;
                    return res;
                }
            }

            res.AttemptedHostLevelMatches = attempted.Count > 0 ? string.Join("; ", attempted) : "(none)";
            res.FailureReason = "Required host level could not be resolved for sprinkler placement.";
            return res;
        }

        private static Level FindHostLevelByElevation(Document doc, double hostElevation, List<string> attempted)
        {
            Level found = null;
            FilteredElementCollector collector = new FilteredElementCollector(doc).OfClass(typeof(Level));
            foreach (Level level in collector)
            {
                attempted?.Add($"{level.Name}@{level.Elevation:F3}ft");
                if (found == null && Math.Abs(level.Elevation - hostElevation) <= 0.01)
                {
                    found = level;
                }
            }

            return found;
        }

        private static Level FindHostLevelByName(Document doc, string levelName, List<string> attempted)
        {
            if (string.IsNullOrWhiteSpace(levelName)) return null;

            Level found = null;
            FilteredElementCollector collector = new FilteredElementCollector(doc).OfClass(typeof(Level));
            foreach (Level level in collector)
            {
                attempted?.Add($"{level.Name}@{level.Elevation:F3}ft");
                if (found == null && string.Equals(level.Name, levelName, StringComparison.OrdinalIgnoreCase))
                {
                    found = level;
                }
            }

            return found;
        }

        /// <summary>An existing sprinkler in the host document: its id and location. The id is what makes the
        /// Replace policy possible; the point drives the coincident-point guard and the "room already has
        /// devices" test.</summary>
        private sealed class ExistingSprinkler
        {
            public ElementId Id;
            public XYZ Point;
        }

        private static List<ExistingSprinkler> CollectExistingSprinklers(Document doc)
        {
            var found = new List<ExistingSprinkler>();
            if (doc == null) return found;

            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Sprinklers)
                .OfClass(typeof(FamilyInstance));

            foreach (Element element in collector)
            {
                if (element is FamilyInstance fi && fi.Location is LocationPoint lp)
                {
                    found.Add(new ExistingSprinkler { Id = fi.Id, Point = lp.Point });
                }
            }

            return found;
        }

        /// <summary>
        /// Existing sprinklers whose plan position falls inside this room's polygon and whose elevation is within
        /// <see cref="PlacementConfig.ExistingDeviceZWindowFt"/> of the room's calculated points. The Z window keeps
        /// a sprinkler on the floor above from being mistaken for one in this room (rooms are 2D polygons here, so
        /// elevation is the only thing separating stacked rooms).
        /// </summary>
        private List<ExistingSprinkler> ExistingSprinklersInRoom(List<ExistingSprinkler> all, RoomCalculationResult room)
        {
            var hits = new List<ExistingSprinkler>();
            if (all == null || all.Count == 0 || room == null || room.Polygon == null || room.Polygon.Count < 3)
                return hits;

            double? referenceZ = room.Points != null && room.Points.Count > 0 ? room.Points[0].Z : (double?)null;

            foreach (ExistingSprinkler existing in all)
            {
                if (existing == null || existing.Point == null) continue;
                if (!IsPointInPolygon(existing.Point.X, existing.Point.Y, room.Polygon)) continue;
                if (referenceZ.HasValue &&
                    Math.Abs(existing.Point.Z - referenceZ.Value) > _config.ExistingDeviceZWindowFt) continue;
                hits.Add(existing);
            }

            return hits;
        }

        /// <summary>Deletes the given existing sprinklers (Replace policy) and drops them from the in-memory
        /// caches so they cannot later block a new point as a "duplicate". Returns the number deleted.</summary>
        private int DeleteExistingSprinklers(
            List<ExistingSprinkler> toDelete,
            List<ExistingSprinkler> all,
            List<XYZ> existingPoints,
            SprinklerPlacementResult result)
        {
            int deleted = 0;
            foreach (ExistingSprinkler existing in toDelete)
            {
                try
                {
                    _hostDocument.Delete(existing.Id);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add("Could not delete existing sprinkler " + existing.Id + ": " + ex.Message);
                    continue;
                }

                deleted++;
                all.Remove(existing);
                existingPoints.RemoveAll(p => p != null && existing.Point != null && p.IsAlmostEqualTo(existing.Point));
            }

            return deleted;
        }

        /// <summary>
        /// True when the candidate lies inside its own room polygon (host coordinates), within the boundary
        /// tolerance. Rooms without a usable polygon are not blocked — the guard can only refuse what it can prove.
        /// </summary>
        private static bool IsPointInsideRoom(CalculatedSprinklerPoint point, RoomCalculationResult room)
        {
            if (point == null) return false;
            if (room == null || room.Polygon == null || room.Polygon.Count < 3) return true;
            return IsPointInPolygon(point.X, point.Y, room.Polygon);
        }

        /// <summary>Standard ray-casting point-in-polygon test on the XY plane (feet). Points exactly on an edge
        /// may fall either way; that is harmless here because boundary clearance already keeps candidates off the
        /// wall, and the caller only uses this to catch grossly misplaced (untransformed) points.</summary>
        private static bool IsPointInPolygon(double x, double y, List<double[]> polygon)
        {
            bool inside = false;
            int count = polygon.Count;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                double[] pi = polygon[i];
                double[] pj = polygon[j];
                if (pi == null || pi.Length < 2 || pj == null || pj.Length < 2) continue;

                bool straddles = (pi[1] > y) != (pj[1] > y);
                if (!straddles) continue;

                double t = (y - pi[1]) / (pj[1] - pi[1]);
                if (x < pi[0] + t * (pj[0] - pi[0])) inside = !inside;
            }

            return inside;
        }

        /// <summary>
        /// Writes the traceability stamp onto a created instance: the tool, the run timestamp and the spacing /
        /// clearance actually applied, flagged PROVISIONAL whenever the geometry came from the placeholder rule
        /// set rather than an approved NFPA-13 rule set. Best-effort and never fatal — a read-only or
        /// missing Comments parameter must not fail a placement.
        /// </summary>
        private void StampTraceability(FamilyInstance instance, RoomCalculationResult room)
        {
            if (instance == null) return;

            try
            {
                Parameter comments = instance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (comments == null || comments.IsReadOnly) return;

                var sb = new System.Text.StringBuilder();
                sb.Append("FireProtection auto-placed ").Append(_runStampUtc);
                if (room != null)
                {
                    if (room.AppliedMaxSpacingFt.HasValue)
                        sb.Append(" | spacing ").Append(room.AppliedMaxSpacingFt.Value.ToString("F2", CultureInfo.InvariantCulture)).Append(" ft");
                    if (room.AppliedBoundaryClearanceFt.HasValue)
                        sb.Append(" | wall ").Append(room.AppliedBoundaryClearanceFt.Value.ToString("F2", CultureInfo.InvariantCulture)).Append(" ft");
                    if (!room.RulesApproved)
                        sb.Append(" | PROVISIONAL RULES - NOT NFPA-13 APPROVED");
                }

                comments.Set(sb.ToString());
            }
            catch
            {
                /* stamping is diagnostic metadata, never a reason to fail a placement */
            }
        }

        private static bool IsFinite(double x, double y, double z)
        {
            return !double.IsInfinity(x) && !double.IsNaN(x) &&
                   !double.IsInfinity(y) && !double.IsNaN(y) &&
                   !double.IsInfinity(z) && !double.IsNaN(z);
        }

        private static string SafeTitle(Document doc)
        {
            try { return doc?.Title; }
            catch { return null; }
        }

        // --- ACTUAL host/level read-back helpers (§23/§24) ---------------------------------------------
        // All are read-only and best-effort: a failure returns null and NEVER affects placement. They
        // capture what Revit actually created (host element, associated level, schedule level) so a
        // "wrong floor plan" report can be triaged as either a genuine hosting defect or a pure
        // view-range / level-association (visibility) issue — without a second Revit run.

        private static string ReadActualHostElementId(FamilyInstance instance)
        {
            try { return instance?.Host?.Id.ToString(); }
            catch { return null; }
        }

        private static string ReadActualHostName(FamilyInstance instance)
        {
            try { return SafeName(instance?.Host); }
            catch { return null; }
        }

        private static string ReadActualInstanceLevelId(FamilyInstance instance)
        {
            try
            {
                ElementId levelId = instance?.LevelId;
                if (levelId == null || levelId == ElementId.InvalidElementId) return null;
                return levelId.ToString();
            }
            catch { return null; }
        }

        private static string ReadActualInstanceLevelName(Document doc, FamilyInstance instance)
        {
            try
            {
                ElementId levelId = instance?.LevelId;
                if (levelId == null || levelId == ElementId.InvalidElementId) return null;
                return SafeName(doc?.GetElement(levelId));
            }
            catch { return null; }
        }

        private static string ReadActualScheduleLevelName(Document doc, FamilyInstance instance)
        {
            try
            {
                if (instance == null) return null;
                // The "Schedule Level" association drives which plan view shows the instance; for a
                // face/work-plane-hosted family this is often the only level association present.
                Parameter scheduleLevel = instance.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
                if (scheduleLevel == null || scheduleLevel.StorageType != StorageType.ElementId) return null;

                ElementId scheduleLevelId = scheduleLevel.AsElementId();
                if (scheduleLevelId == null || scheduleLevelId == ElementId.InvalidElementId) return null;
                return SafeName(doc?.GetElement(scheduleLevelId));
            }
            catch { return null; }
        }

        private static string SafeName(Element element)
        {
            try { return element?.Name; }
            catch { return null; }
        }
    }

    /// <summary>
    /// Minimal, configurable safety settings for the placement step. Does not encode any NFPA spacing
    /// logic (that lives in the BruteForce calculation layer).
    /// </summary>
    public class RevitSprinklerPlacementConfig
    {
        /// <summary>When true, a calculated point within DuplicateProximityFt of an existing sprinkler is skipped.</summary>
        public bool SkipNearDuplicates { get; set; } = true;

        /// <summary>Safety proximity (feet) used only to avoid uncontrolled duplicates; not a spacing rule.</summary>
        public double DuplicateProximityFt { get; set; } = 0.25;

        /// <summary>
        /// §23 post-placement tolerance (feet). After an instance is created, its actual LocationPoint is
        /// compared with the requested point; a deviation greater than this marks the placement
        /// PLACED_BUT_INVALID (the historic (0,0,0) origin-snap defect is far outside any sane tolerance).
        /// 0.5 ft accommodates legitimate host-face snapping while still catching gross misplacement.
        /// </summary>
        public double PlacementValidationToleranceFt { get; set; } = 0.5;

        /// <summary>
        /// Vertical window (feet) within which an existing sprinkler counts as belonging to the room being
        /// processed. Rooms are compared as 2D polygons, so without this window a sprinkler on the floor above
        /// or below would look like it was inside this room. 6 ft is under any realistic floor-to-floor height
        /// and comfortably wider than the ceiling-height variation inside one room.
        /// </summary>
        public double ExistingDeviceZWindowFt { get; set; } = 6.0;

        public static RevitSprinklerPlacementConfig Default() => new RevitSprinklerPlacementConfig();
    }
}
