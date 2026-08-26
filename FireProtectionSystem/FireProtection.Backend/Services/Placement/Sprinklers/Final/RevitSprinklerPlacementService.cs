using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
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
            _strategies = new IFamilyPlacementStrategy[]
            {
                new Strategies.FaceBasedPlacementStrategy(),
                new Strategies.WorkPlaneBasedPlacementStrategy(),
                new Strategies.LevelBasedPlacementStrategy()
            };
        }

        public SprinklerPlacementResult PlaceSprinklers(
            string selectedFamilyName,
            string selectedTypeName,
            BruteForceCalculationResult calcResult)
        {
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

            // 1. Resolve FamilySymbol dynamically by name (never by hard-coded ElementId).
            string symbolError;
            FamilySymbol symbol = ResolveSymbol(_hostDocument, selectedFamilyName, selectedTypeName, out symbolError);
            if (symbol == null)
            {
                result.Errors.Add(symbolError ?? "Sprinkler family/type could not be resolved.");
                return result;
            }

            // Pre-collect existing sprinkler locations for a minimal duplicate safety check.
            IReadOnlyList<XYZ> existingSprinklerPoints = _config.SkipNearDuplicates
                ? CollectExistingSprinklerPoints(_hostDocument)
                : new List<XYZ>();

            // --- PHASE 2: Prove the actual family placement type (do NOT infer from the name). ---
            // Revit FamilyPlacementType (version-agnostic string compare) selects the strategy below:
            //   FaceBased       -> requires a host face reference.
            //   WorkPlaneBased  -> hosted on a work plane (ceiling face, else a SketchPlane through the point).
            //   OneLevelBased   -> standard level-based placement.
            string actualPlacementType = "Unknown";
            try
            {
                actualPlacementType = symbol.Family?.FamilyPlacementType.ToString() ?? "None";
            }
            catch
            {
                actualPlacementType = "Unknown";
            }

            // §26/§28: carry the proven placement type structurally on the result (machine-readable),
            // instead of emitting a throwaway diagnostic log string into Warnings.
            result.ResolvedFamilyPlacementType = actualPlacementType;

            // 2. Single batch transaction: activate symbol, place valid points, commit.
            using (Transaction transaction = new Transaction(_hostDocument, "FireProtection Place Sprinklers"))
            {
                transaction.Start();

                bool committed = false;
                try
                {
                    // Activate once per batch, not per point.
#pragma warning disable CS0618 // FamilySymbol.Activate is deprecated in some Revit versions but required for placement.
                    if (!symbol.IsActive)
                        symbol.Activate();
#pragma warning restore CS0618

                    foreach (RoomCalculationResult room in calcResult.Rooms)
                    {
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

                        foreach (CalculatedSprinklerPoint point in room.Points)
                        {
                            PlaceSinglePoint(
                                _hostDocument,
                                symbol,
                                actualPlacementType,
                                point,
                                existingSprinklerPoints,
                                roomResult,
                                result);
                        }

                        result.Rooms.Add(roomResult);
                    }

                    transaction.Commit();
                    committed = true;
                }
                catch (Exception ex)
                {
                    result.Errors.Add("Transaction failed: " + ex.Message);
                    if (!committed)
                    {
                        try { transaction.RollBack(); }
                        catch { /* best-effort rollback */ }
                    }
                }
            }

            result.PlacedSprinklerCount = result.Rooms.Sum(r => r.Placed.Count);
            result.PlacedAndValidCount = result.Rooms.Sum(r => r.Placed.Count(p => p.IsSpatiallyValid));
            result.PlacedButInvalidCount = result.Rooms.Sum(r => r.Placed.Count(p => !p.IsSpatiallyValid));
            result.FailedSprinklerCount = result.Rooms.Sum(r => r.Failed.Count);
            result.SkippedDuplicateCount = result.Rooms.Sum(r => r.Failed.Count(f => f.SkippedDueToDuplicate));

            return result;
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

            string familyKey = (selectedFamilyName ?? string.Empty) + "::" + (selectedTypeName ?? string.Empty);
            string cacheKey = familyKey + "::" + (room.RoomId ?? room.Name ?? "?");
            if (_eligibilityCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var result = new PlacementEligibilityResult();

            try
            {
                var (symbol, placementType, strategy, error) = ResolveFamily(selectedFamilyName, selectedTypeName);
                result.FamilyPlacementType = placementType;

                if (symbol == null)
                {
                    return CacheAndReturn(cacheKey, PlacementEligibilityResult.Blocked(
                        result,
                        PlacementEligibilityStatusCodes.UnsupportedFamilyPlacement,
                        error ?? $"Sprinkler family/type '{selectedFamilyName}:{selectedTypeName}' was not found."));
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
                    // evidence to decide) — this is UNKNOWN, never BLOCKED.
                    return CacheAndReturn(cacheKey, PlacementEligibilityResult.Unknown(
                        "Candidate point calculation could not be run for this room; eligibility is undetermined.",
                        PlacementEligibilityStatusCodes.Unknown));
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

                // --- DETERMINISTIC FEASIBILITY PREFLIGHT (4-state classification; NO instance is created) ---
                // Master prompt §12: prefer a deterministic preflight that MIRRORS placement capability over
                // performing live Revit placements. "Eligible" answers "can the selected family be placed in this
                // room in principle?" and MUST NOT be coupled to a runtime placement defect — otherwise a broken
                // executor makes every room look non-eligible (the reported "0 eligible rooms" pathology, §10/§37).
                // Actual placement correctness is a separate concern, re-checked at placement time and guarded
                // again in the Place command. This reuses the SAME resolvers placement uses (ResolveFamily /
                // ResolveHostLevel / CeilingHostResolver) so eligibility stays faithful to real preconditions;
                // no fire-protection rule is invented and no model change is ever made.
                //
                // Feasibility by proven FamilyPlacementType:
                //   FaceBased      -> requires a real ceiling/host FACE for >=1 candidate (deterministic BLOCKED if none).
                //   WorkPlaneBased -> a ceiling face when present, else a SketchPlane at the requested Z (always
                //                     constructible given a point + resolved level) -> feasible if a level resolves.
                //   OneLevelBased  -> needs only a resolvable level -> feasible if a level resolves.
                // No candidate with a resolvable level -> deterministic BLOCKED (MissingHostLevel).
                // PLACEMENT_ERROR / UNKNOWN are still produced (outer catch below; null/empty candidates above),
                // so the 4-state contract is preserved and defects are never hidden as BLOCKED.
                bool requiresHostFace =
                    placementType != null &&
                    placementType.IndexOf("Face", StringComparison.OrdinalIgnoreCase) >= 0;

                bool anyLevelResolved = false;
                string firstLevelFailure = null;
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

                    // Read-only host lookup (NO placement). Required for FaceBased, diagnostic for the others.
                    CeilingHostLookup host = null;
                    try { host = _ceilingHostResolver.FindCeilingHost(_hostDocument, xyz, level); }
                    catch { /* host lookup is best-effort; never fail eligibility on it */ }

                    bool hasHostFace = host != null && host.HostFace != null;

                    if (requiresHostFace && !hasHostFace)
                        continue; // this candidate has no host face; try the next candidate

                    // Deterministically feasible: record the resolved host/level context and classify ELIGIBLE.
                    result.HostingStrategy = requiresHostFace
                        ? "FaceBasedHost"
                        : (hasHostFace ? "WorkPlaneCeilingFace" : "WorkPlaneSketchPlane");
                    result.CeilingSource = host?.Source ?? "none";
                    result.LinkInstanceName = host?.LinkInstanceName;
                    result.HostCeilingElementId = host?.CeilingElementId;
                    result.HostLevelId = level.Id.ToString();
                    result.HostLevelName = level.Name;

                    EmitCandidateDiagnostic(
                        room, candidateIndex, c, placementType, level, outcome: null,
                        requested: xyz, actual: null, deviation: double.NaN,
                        statusCode: PlacementEligibilityStatusCodes.Eligible,
                        exceptionDetail: null);

                    return CacheAndReturn(cacheKey, PlacementEligibilityResult.Eligible(result));
                }

                if (!anyLevelResolved)
                {
                    // No candidate has a resolvable host level -> deterministic inability for this room.
                    return CacheAndReturn(cacheKey, PlacementEligibilityResult.Blocked(
                        result,
                        PlacementEligibilityStatusCodes.MissingHostLevel,
                        firstLevelFailure ?? "No candidate point has a resolvable host level."));
                }

                // A level resolved for at least one candidate but (FaceBased only) no ceiling/host face was found
                // anywhere in the room -> genuine deterministic inability.
                return CacheAndReturn(cacheKey, PlacementEligibilityResult.Blocked(
                    result,
                    PlacementEligibilityStatusCodes.NoCeilingHost,
                    "The selected face-based family requires a ceiling/host face, but none was found in this room."));
            }
            catch (Exception ex)
            {
                return CacheAndReturn(cacheKey, PlacementEligibilityResult.PlacementError(
                    result,
                    PlacementEligibilityStatusCodes.PreflightError,
                    "Preflight evaluation failed: " + ex.Message));
            }
        }

        // Structured per-candidate probe diagnostic (master prompt §4). Emitted via Debug so it does not affect
        // the UI, but is available in any debugger / trace listener to identify the exact placement defect.
        [System.Diagnostics.Conditional("DEBUG")]
        private void EmitCandidateDiagnostic(
            RoomUiData room,
            int candidateIndex,
            CalculatedSprinklerPoint candidate,
            string placementType,
            Level level,
            PlacementOutcome outcome,
            XYZ requested,
            XYZ actual,
            double deviation,
            string statusCode,
            string exceptionDetail)
        {
            string fmt(double v) => double.IsNaN(v) ? "NaN" : v.ToString("F3");
            string delta = (requested != null && actual != null)
                ? $"Delta=({fmt(actual.X - requested.X)},{fmt(actual.Y - requested.Y)},{fmt(actual.Z - requested.Z)})"
                : "Delta=n/a";

            System.Diagnostics.Debug.WriteLine(
                $"[ROOM-CANDIDATE-DIAGNOSTIC] RoomId={room?.RoomId} RoomName={room?.Name} " +
                $"CandidateIndex={candidateIndex} " +
                $"Requested=({fmt(candidate?.X ?? double.NaN)},{fmt(candidate?.Y ?? double.NaN)},{fmt(candidate?.Z ?? double.NaN)}) " +
                $"PlacementType={placementType} " +
                $"HostLevel={level?.Name} CeilingSource={outcome?.CeilingSource} " +
                $"HostingStrategy={outcome?.HostingStrategy} " +
                $"PlacementSucceeded={outcome?.Created.ToString() ?? "n/a"} " +
                $"CreatedElementId={outcome?.Instance?.Id.ToString() ?? "n/a"} " +
                $"Actual=({fmt(actual?.X ?? double.NaN)},{fmt(actual?.Y ?? double.NaN)},{fmt(actual?.Z ?? double.NaN)}) " +
                $"DistanceFromRequested={fmt(deviation)} {delta} " +
                $"ValidationStatus={statusCode} " +
                (exceptionDetail != null ? $"ExceptionDetail={exceptionDetail}" : string.Empty));
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
            SprinklerPlacementResult result)
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
            IFamilyPlacementStrategy strategy = null;
            for (int i = 0; i < _strategies.Count; i++)
            {
                if (_strategies[i].CanHandle(familyPlacementType))
                {
                    strategy = _strategies[i];
                    break;
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
                    CeilingHostResolver = _ceilingHostResolver
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

        private static IReadOnlyList<XYZ> CollectExistingSprinklerPoints(Document doc)
        {
            var points = new List<XYZ>();
            if (doc == null) return points;

            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Sprinklers)
                .OfClass(typeof(FamilyInstance));

            foreach (Element element in collector)
            {
                if (element is FamilyInstance fi && fi.Location is LocationPoint lp)
                {
                    points.Add(lp.Point);
                }
            }

            return points;
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

        public static RevitSprinklerPlacementConfig Default() => new RevitSprinklerPlacementConfig();
    }
}
