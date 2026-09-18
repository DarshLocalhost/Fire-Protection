using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using FireProtection.Backend.Models.Placement.SmokeDetectors.Final;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.Strategies;
using FireProtection.UI.Models.Sprinklers.BruteForce;
using FireProtection.UI.Services;

namespace FireProtection.Backend.Services.Placement.Devices
{
    /// <summary>
    /// Shared Revit placement machinery for fire-alarm devices (smoke detectors AND notification appliances -
    /// both live in <c>OST_FireAlarmDevices</c> and use the same coverage-grid geometry). The device-specific
    /// step - building the input snapshot and running the calculation with the right rule set - is injected as
    /// a <see cref="Func{T,TResult}"/> so this class stays device-neutral. It creates real
    /// <see cref="FamilyInstance"/> elements for every calculated point using the SAME proven placement
    /// strategies as the sprinkler service (dispatched by the family's <c>FamilyPlacementType</c>, or by
    /// wall-edge for sidewall points). Revit API usage is isolated here; the calculation layer stays
    /// Revit-independent. Returns the UI-friendly <see cref="PlacementRunReport"/> expected by the generic
    /// device workflow. Thin per-device wrappers (<c>RevitSmokeDetectorPlacementExecutor</c>,
    /// <c>RevitNotificationAppliancePlacementExecutor</c>) supply the calculation delegate and labels.
    /// </summary>
    public class FireAlarmDevicePlacementCore : IDevicePlacementExecutor
    {
        // Kept in lockstep with RevitSprinklerPlacementConfig so all device kinds behave identically.
        private const double DuplicateProximityFt = 0.25;
        private const double PlacementValidationToleranceFt = 0.5;
        private const double ExistingDeviceZWindowFt = 6.0;
        private const double InRoomToleranceFt = 0.5;

        private readonly Document _document;
        private readonly string _deviceNoun;              // lowercase, e.g. "smoke detector" / "notification appliance"
        private readonly string _transactionGroupName;    // e.g. "Fire Protection: Place Smoke Detectors"
        private readonly Func<IReadOnlyList<DeviceRoomInputItem>, SmokeDetectorCalculationResult> _calculate;
        private readonly CeilingHostResolver _ceilingHostResolver;
        private readonly IReadOnlyList<IFamilyPlacementStrategy> _strategies;

        /// <param name="deviceNoun">Lowercase singular device name used in logs/report messages.</param>
        /// <param name="transactionGroupName">Undo-stack label for the whole run.</param>
        /// <param name="calculate">Builds the device-specific snapshot and runs the coverage-grid engine.</param>
        public FireAlarmDevicePlacementCore(
            Document document,
            string deviceNoun,
            string transactionGroupName,
            Func<IReadOnlyList<DeviceRoomInputItem>, SmokeDetectorCalculationResult> calculate)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _deviceNoun = string.IsNullOrWhiteSpace(deviceNoun) ? "device" : deviceNoun.Trim();
            _transactionGroupName = string.IsNullOrWhiteSpace(transactionGroupName)
                ? "Fire Protection: Place Devices"
                : transactionGroupName;
            _calculate = calculate ?? throw new ArgumentNullException(nameof(calculate));
            _ceilingHostResolver = new CeilingHostResolver();

            // Same ordered strategy set and dispatch rule as the sprinkler service: selection is by the
            // family's PROVEN FamilyPlacementType (no silent substitution); WallSidewall is dispatched by a
            // point's WallEdgeIndex, not by placement type.
            _strategies = new IFamilyPlacementStrategy[]
            {
                new FaceBasedPlacementStrategy(),
                new WorkPlaneBasedPlacementStrategy(),
                new LevelBasedPlacementStrategy(),
                new WallSidewallPlacementStrategy()
            };
        }

        /// <summary>Sentence-start capitalization of <see cref="_deviceNoun"/> (e.g. "Smoke detector").</summary>
        private string DeviceNounCap =>
            _deviceNoun.Length == 0 ? _deviceNoun : char.ToUpperInvariant(_deviceNoun[0]) + _deviceNoun.Substring(1);

        public PlacementRunReport ExecutePlacement(
            IReadOnlyList<DeviceRoomInputItem> items,
            IPlacementProgress progress = null,
            ExistingDevicePolicy existingDevicePolicy = ExistingDevicePolicy.SkipRoom)
        {
            if (progress == null) progress = NullPlacementProgress.Instance;

            var report = new PlacementRunReport();
            if (items == null || items.Count == 0)
            {
                return report;
            }

            // 1. Run the injected (Revit-free) calculation: it builds the device-specific input snapshot and
            //    invokes the shared coverage-grid engine. The result carries every calculated point.
            SmokeDetectorCalculationResult calc = _calculate(items);

            report.RoomsProcessed = calc.Rooms?.Count ?? 0;
            report.SprinklersRequested = calc.TotalCalculatedDetectors;
            report.IsProvisional = calc.IsProvisional;
            report.AppliedRulesSummary = calc.AppliedRulesSummary;
            if (calc.Warnings != null) report.Warnings.AddRange(calc.Warnings);
            if (calc.Errors != null) report.Warnings.AddRange(calc.Errors);

            if (calc.Rooms == null || calc.Rooms.Count == 0)
            {
                FireProtectionLog.Warn(DeviceNounCap + " placement: calculation produced no rooms.");
                return report;
            }

            FireProtectionLog.Info(DeviceNounCap + " placement run started: " + calc.Rooms.Count
                + " room(s), " + calc.TotalCalculatedDetectors + " calculated point(s), policy "
                + existingDevicePolicy + (calc.IsProvisional ? " (PROVISIONAL rules)." : "."));

            // 2. Collect existing fire-alarm devices once: drives the duplicate guard and the room policy.
            //    This run's own kind comes from the input items (the UI stamps every room with its tab's
            //    DeviceKind); smoke detectors and notification appliances share OST_FireAlarmDevices, so the
            //    skip/replace/duplicate logic below MUST be kind-scoped or a smoke run would skip or delete
            //    (and vice versa) the other kind's devices in the same room.
            DeviceKind ownKind = items[0] != null ? items[0].DeviceKind : DeviceKind.SmokeDetector;
            List<ExistingDevice> existing = CollectExistingDevices(_document, ownKind);
            var existingPoints = existing.Where(e => e.IsOwnKind && e.Point != null).Select(e => e.Point).ToList();

            bool cancelled = false;

            // One TransactionGroup: a single named undo entry; cancel rolls the whole run back.
            using (var group = new TransactionGroup(_document, _transactionGroupName))
            {
                group.Start();
                cancelled = RunPlacement(calc, existing, existingPoints, progress, existingDevicePolicy, report);
                if (cancelled) group.RollBack();
                else group.Assimilate();
            }

            if (cancelled)
            {
                // The rollback undid every element; reporting them as placed would be a false success.
                report.RoomReports.Clear();
                report.RoomsProcessed = 0;
                report.RoomsSucceeded = 0;
                report.RoomsFailed = 0;
                report.RoomsSkipped = 0;
                report.SprinklersPlaced = 0;
                report.OverallStatus = "Cancelled";
                report.Summary = "Placement cancelled by the user; everything created in this run was rolled back.";
                FireProtectionLog.Warn(DeviceNounCap + " placement cancelled by the user; run rolled back.");
                return report;
            }

            report.RoomsSucceeded = report.RoomReports.Count(r => r.Status == "Success");
            report.RoomsFailed = report.RoomReports.Count(r => r.Status == "Failed");
            report.RoomsSkipped = report.RoomReports.Count(r => r.Status == "Skipped");
            report.ReviewRequiredCount = report.RoomReports.Count(r => r.Status == "ReviewRequired" || r.ReviewRequired);
            report.SprinklersPlaced = report.RoomReports.Sum(r => r.PointsPlaced);
            report.OverallStatus = report.RoomsFailed > 0
                ? "Failed"
                : (report.ReviewRequiredCount > 0 ? "ReviewRequired" : (report.RoomsSkipped > 0 ? "PassWithSkips" : "Success"));

            report.FailedReasons = report.RoomReports
                .Where(r => !string.IsNullOrWhiteSpace(r.FailureReason))
                .Select(r => r.RoomName + ": " + r.FailureReason)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            report.ReviewRequiredReasons = report.RoomReports
                .Where(r => r.ReviewRequired || r.Status == "ReviewRequired")
                .Select(r => r.RoomName + ": " + (r.Message ?? "Review required"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            report.Summary = "Processed " + report.RoomsProcessed + " room(s); " + report.RoomsSucceeded
                + " success, " + report.RoomsFailed + " failed, " + report.RoomsSkipped + " skipped, "
                + report.ReviewRequiredCount + " review required; placed " + report.SprinklersPlaced
                + " of " + report.SprinklersRequested + " device(s).";

            FireProtectionLog.Info(DeviceNounCap + " placement run finished: placed "
                + report.SprinklersPlaced + " of " + report.SprinklersRequested + " point(s); "
                + report.RoomsSucceeded + " room(s) ok, " + report.RoomsFailed + " failed, "
                + report.RoomsSkipped + " skipped, " + report.ReviewRequiredCount + " review required.");

            return report;
        }

        // ----- Pre-placement eligibility / preflight (single source of truth with actual placement) -----
        // Consumed by the UI so it can block rooms the production pipeline provably cannot place, BEFORE the
        // user runs. It reuses the EXACT calculation, symbol/level resolution and ceiling-host logic that
        // ExecutePlacement/PlaceSinglePoint use - no second, drifting set of hosting rules - but performs only
        // READ-ONLY queries: no transaction, no FamilyInstance is ever created. Port of
        // RevitSprinklerPlacementService.EvaluateRoomEligibility, adapted to the batch device calculation.

        /// <inheritdoc />
        public IReadOnlyDictionary<string, PlacementEligibilityResult> EvaluateEligibility(
            IReadOnlyList<DeviceRoomInputItem> items)
        {
            var map = new Dictionary<string, PlacementEligibilityResult>(StringComparer.Ordinal);
            if (items == null || items.Count == 0) return map;

            SmokeDetectorCalculationResult calc;
            try
            {
                // Same delegate the real run uses: the preflight can never disagree with placement.
                calc = _calculate(items);
            }
            catch (Exception ex)
            {
                // The calculation itself could not run: every room is UNDETERMINED, never falsely blocked.
                FireProtectionLog.Warn(DeviceNounCap + " eligibility: calculation could not run - " + ex.Message);
                foreach (DeviceRoomInputItem item in items)
                {
                    if (item?.RoomId == null || map.ContainsKey(item.RoomId)) continue;
                    map[item.RoomId] = PlacementEligibilityResult.Undetermined(
                        "Candidate calculation could not be run; eligibility is undetermined.",
                        PlacementEligibilityStatusCodes.CalculationFailed);
                }
                return map;
            }

            var byRoom = new Dictionary<string, SmokeDetectorRoomCalculationResult>(StringComparer.Ordinal);
            if (calc?.Rooms != null)
            {
                foreach (SmokeDetectorRoomCalculationResult r in calc.Rooms)
                {
                    if (r?.RoomId != null && !byRoom.ContainsKey(r.RoomId)) byRoom[r.RoomId] = r;
                }
            }

            foreach (DeviceRoomInputItem item in items)
            {
                if (item?.RoomId == null || map.ContainsKey(item.RoomId)) continue;

                if (!byRoom.TryGetValue(item.RoomId, out SmokeDetectorRoomCalculationResult room) || room == null)
                {
                    // The calculation returned no result for this room: cannot decide -> UNDETERMINED.
                    map[item.RoomId] = PlacementEligibilityResult.Undetermined(
                        "The calculation produced no result for this room; eligibility is undetermined.",
                        PlacementEligibilityStatusCodes.CalculationFailed);
                    continue;
                }

                map[item.RoomId] = EvaluateRoomEligibility(room);
            }

            return map;
        }

        /// <summary>The per-room feasibility ladder, mirroring the sprinkler service. Deterministic calculation
        /// outcomes decide first; otherwise a read-only host/level probe (identical to PlaceSinglePoint's
        /// prerequisites) decides ELIGIBLE vs BLOCKED. Any unexpected failure is UNDETERMINED, never BLOCKED.</summary>
        private PlacementEligibilityResult EvaluateRoomEligibility(SmokeDetectorRoomCalculationResult room)
        {
            try
            {
                // 1. Deterministic calculation outcomes.
                if (room.Status == CalculationStatus.InvalidRoomGeometry)
                    return PlacementEligibilityResult.Blocked(
                        PlacementEligibilityStatusCodes.MissingRoomGeometry,
                        FirstMessage(room) ?? "Room boundary is incomplete (at least 3 points required for placement).");

                if (room.Status == CalculationStatus.InvalidInput)
                    // Set today only when the family's proven placement behaviour is Unsupported.
                    return PlacementEligibilityResult.Blocked(
                        PlacementEligibilityStatusCodes.UnsupportedFamilyPlacementType,
                        FirstMessage(room) ?? "The selected family/type has an unsupported placement behaviour.");

                if (room.Status == CalculationStatus.Failed)
                    return PlacementEligibilityResult.Undetermined(
                        FirstMessage(room) ?? "Calculation failed for this room; eligibility is undetermined.",
                        PlacementEligibilityStatusCodes.CalculationFailed);

                if (room.Points == null || room.Points.Count == 0)
                    // The calculation completed and produced nothing to place: deterministic.
                    return PlacementEligibilityResult.Blocked(
                        PlacementEligibilityStatusCodes.NoCandidatePoints,
                        FirstMessage(room) ?? "The calculation produced no candidate points for this room; nothing can be placed here.");

                // 2. Read-only feasibility preflight (mirrors PlaceSinglePoint prerequisites; creates nothing).
                FamilySymbol symbol = ResolveSymbol(_document, room.FamilyName, room.TypeName, out string symbolError);
                if (symbol == null)
                {
                    // Family/type not resolved is a CONFIGURATION error, not proof the room is unplaceable -> UNDETERMINED.
                    return PlacementEligibilityResult.Undetermined(
                        symbolError ?? ("Family/type '" + (room.FamilyName ?? "?") + ":" + (room.TypeName ?? "?")
                            + "' was not found in the active document."),
                        PlacementEligibilityStatusCodes.UnsupportedFamilyPlacement);
                }

                var result = new PlacementEligibilityResult();
                string placementType = SafePlacementType(symbol);
                result.FamilyPlacementType = placementType;

                // WallSidewall behaviour (a point carrying a WallEdgeIndex) is wall-mounted regardless of the
                // family's FamilyPlacementType and does NOT need a ceiling host - it needs a wall face.
                bool isSidewall = room.Points.Any(p => p != null && p.WallEdgeIndex.HasValue);
                IFamilyPlacementStrategy strategy = isSidewall
                    ? _strategies.FirstOrDefault(s => s is WallSidewallPlacementStrategy)
                    : _strategies.FirstOrDefault(s => s.CanHandle(placementType));
                if (strategy == null)
                    return PlacementEligibilityResult.Blocked(
                        result,
                        PlacementEligibilityStatusCodes.UnsupportedFamilyPlacementType,
                        "No placement strategy supports the family's placement type '" + placementType + "'.");

                // Host requirement by proven FamilyPlacementType (a numeric ceiling height is NOT proof of a host):
                //   FaceBased / WorkPlaneBased -> require a usable ceiling host for >=1 candidate.
                //   OneLevelBased              -> requires only a resolvable level.
                bool requiresCeilingHost = !isSidewall && (
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

                foreach (CalculatedSmokeDetectorPoint c in room.Points)
                {
                    if (c == null || !IsFinite(c.X, c.Y, c.Z)) continue;

                    Level level = ResolveHostLevel(c.LevelId, c.LevelName, out string levelError);
                    if (level == null)
                    {
                        if (firstLevelFailure == null)
                            firstLevelFailure = levelError ?? "The candidate's host level could not be resolved.";
                        continue;
                    }

                    anyLevelResolved = true;
                    XYZ xyz = new XYZ(c.X, c.Y, c.Z);

                    CeilingHostLookup host = null;
                    try { host = _ceilingHostResolver.FindCeilingHost(_document, xyz, level); }
                    catch { /* host lookup is best-effort; never fail eligibility on it */ }

                    bool hasHostFace = host != null && host.HostFace != null;
                    if (requiresCeilingHost && !hasHostFace) continue; // try the next candidate

                    anyHostOk = true;
                    validCandidateCount++;
                    hostSource = host?.Source ?? "none";
                    linkInstanceName = host?.LinkInstanceName;
                    hostCeilingElementId = host?.CeilingElementId;
                    hostLevelId = level.Id.ToString();
                    hostLevelName = level.Name;
                }

                if (!anyLevelResolved)
                    return PlacementEligibilityResult.Blocked(
                        result,
                        PlacementEligibilityStatusCodes.MissingHostLevel,
                        firstLevelFailure ?? "No candidate point has a resolvable host level.");

                if (!anyHostOk)
                {
                    if (requiresCeilingHost)
                        return PlacementEligibilityResult.Blocked(
                            result,
                            PlacementEligibilityStatusCodes.NoUsableCeilingHost,
                            "No usable ceiling host was found for the selected " + _deviceNoun + " family/type.");

                    return PlacementEligibilityResult.Blocked(
                        result,
                        PlacementEligibilityStatusCodes.NoCandidatePoints,
                        "No candidate point could be placed for the selected " + _deviceNoun + " family/type.");
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

                return PlacementEligibilityResult.Eligible(result);
            }
            catch (Exception ex)
            {
                // An unexpected evaluation failure is UNDETERMINED, never BLOCKED.
                return PlacementEligibilityResult.Undetermined(
                    "Preflight evaluation failed: " + ex.Message,
                    PlacementEligibilityStatusCodes.PreflightError);
            }
        }

        private static string SafePlacementType(FamilySymbol symbol)
        {
            try { return symbol?.Family?.FamilyPlacementType.ToString() ?? "None"; }
            catch { return "Unknown"; }
        }

        /// <summary>The placement transaction: activates symbols, walks the rooms, honours the existing-device
        /// policy, reports progress and stops at a room boundary on cancel. Returns true if cancelled.</summary>
        private bool RunPlacement(
            SmokeDetectorCalculationResult calc,
            List<ExistingDevice> existing,
            List<XYZ> existingPoints,
            IPlacementProgress progress,
            ExistingDevicePolicy existingDevicePolicy,
            PlacementRunReport report)
        {
            using (var transaction = new Transaction(_document, "Place " + DeviceNounCap + "s"))
            {
                transaction.Start();

                bool committed = false;
                try
                {
                    var symbolCache = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
                    var activatedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    int totalRooms = calc.Rooms.Count;
                    int roomIndex = 0;

                    foreach (SmokeDetectorRoomCalculationResult room in calc.Rooms)
                    {
                        roomIndex++;

                        // Cancellation is polled only at room boundaries: no half-placed room is ever left,
                        // and the group rollback in the caller undoes everything placed so far.
                        if (progress.IsCancellationRequested)
                        {
                            transaction.Commit();
                            return true;
                        }

                        progress.Report(roomIndex - 1, totalRooms,
                            "Room " + roomIndex + " of " + totalRooms + ": " + (room.RoomName ?? room.RoomId));

                        var roomReport = new PlacementRoomReport
                        {
                            RoomId = room.RoomId,
                            RoomName = room.RoomName,
                            LevelName = room.Points != null && room.Points.Count > 0 ? room.Points[0].LevelName : null,
                            PointsRequested = room.Points?.Count ?? 0
                        };

                        if (room.Points == null || room.Points.Count == 0)
                        {
                            roomReport.Status = "Skipped";
                            roomReport.Message = FirstMessage(room) ?? ("No " + _deviceNoun + "s calculated for this room.");
                            report.RoomReports.Add(roomReport);
                            continue;
                        }

                        // Re-run handling: what a second Place does to a room that already contains devices.
                        double refZ = room.Points[0].Z;
                        List<ExistingDevice> inRoom = DevicesInRoom(existing, room.Polygon, refZ);
                        if (inRoom.Count > 0)
                        {
                            if (existingDevicePolicy == ExistingDevicePolicy.SkipRoom)
                            {
                                roomReport.Status = "Skipped";
                                roomReport.Message = "Room already has " + inRoom.Count
                                    + " " + _deviceNoun + "(s) - other device kinds in this room do not count; "
                                    + "left untouched (policy: skip existing).";
                                report.RoomReports.Add(roomReport);
                                continue;
                            }

                            if (existingDevicePolicy == ExistingDevicePolicy.ReplaceExisting)
                            {
                                DeleteDevices(inRoom, existing, existingPoints);
                            }
                            // AddAnyway: fall through - the coincident-point guard still applies per point.
                        }

                        FamilySymbol symbol = ResolveCachedSymbol(
                            room.FamilyName, room.TypeName, symbolCache, activatedKeys, out string symbolError);
                        if (symbol == null)
                        {
                            roomReport.Status = "Failed";
                            roomReport.Message = "Family/type '" + (room.FamilyName ?? "?") + ":"
                                + (room.TypeName ?? "?") + "' is not loaded in the active document ("
                                + (symbolError ?? "not found") + ").";
                            report.RoomReports.Add(roomReport);
                            continue;
                        }

                        string placementType = symbol.Family?.FamilyPlacementType.ToString() ?? "None";
                        int placed = 0;
                        int invalid = 0;
                        int failed = 0;

                        foreach (CalculatedSmokeDetectorPoint point in room.Points)
                        {
                            PointResult pr = PlaceSinglePoint(symbol, placementType, point, room.Polygon, existingPoints);
                            if (pr == PointResult.Placed) placed++;
                            else if (pr == PointResult.PlacedInvalid) { placed++; invalid++; }
                            else failed++;
                        }

                        roomReport.PointsPlaced = placed;
                        // Calc-level review flags (provisional rules, coverage gaps) must survive placement:
                        // a room whose calculation says ReviewRequired is never reported as a plain Success,
                        // even when every point placed within tolerance.
                        bool calcReview = room.Status == CalculationStatus.ReviewRequired;
                        if (placed == 0)
                        {
                            roomReport.Status = "Failed";
                            roomReport.FailureReason = "No " + _deviceNoun + "s could be placed (" + failed + " point(s) failed).";
                            roomReport.Message = roomReport.FailureReason;
                        }
                        else if (invalid > 0 || calcReview)
                        {
                            roomReport.Status = "ReviewRequired";
                            roomReport.ReviewRequired = true;
                            if (invalid > 0)
                            {
                                roomReport.FailureReason = invalid + " placed point(s) were outside the accepted tolerance and require engineering review.";
                                roomReport.Message = "Placed " + placed + " of " + room.Points.Count + " " + _deviceNoun + "(s), with "
                                    + invalid + " review-required placement(s)." + (failed > 0 ? " " + failed + " point(s) failed." : string.Empty);
                            }
                            else
                            {
                                roomReport.Message = "Placed " + placed + " of " + room.Points.Count + " " + _deviceNoun + "(s)"
                                    + (failed > 0 ? ", " + failed + " failed" : string.Empty)
                                    + " - engineering review required: " + (FirstMessage(room) ?? "calculation flagged this room for review.");
                            }
                        }
                        else
                        {
                            roomReport.Status = "Success";
                            roomReport.Message = "Placed " + placed + " of " + room.Points.Count + " " + _deviceNoun + "(s)."
                                + (failed > 0 ? " " + failed + " failed." : string.Empty);
                        }

                        report.RoomReports.Add(roomReport);
                    }

                    transaction.Commit();
                    committed = true;
                    return false;
                }
                catch (Exception ex)
                {
                    FireProtectionLog.Error(DeviceNounCap + " placement transaction failed.", ex);
                    if (!committed)
                    {
                        try { transaction.RollBack(); }
                        catch { /* best-effort rollback */ }
                    }
                    throw;
                }
            }
        }

        private enum PointResult { Placed, PlacedInvalid, Failed }

        private PointResult PlaceSinglePoint(
            FamilySymbol symbol,
            string familyPlacementType,
            CalculatedSmokeDetectorPoint point,
            List<double[]> roomPolygon,
            List<XYZ> existingPoints)
        {
            if (point == null || !IsFinite(point.X, point.Y, point.Z)) return PointResult.Failed;

            var xyz = new XYZ(point.X, point.Y, point.Z);

            // Coordinate-space guard (mirrors the sprinkler service): a candidate must fall inside its own
            // room polygon in HOST coordinates. One that does not is almost always an untransformed
            // linked-model point, and creating it would drop a device far outside the room. The 0.5 ft
            // tolerance lets wall-edge (sidewall) candidates sit on the boundary itself.
            if (roomPolygon != null && roomPolygon.Count >= 3
                && !GeometryMath.PointInPolygon(point.X, point.Y, roomPolygon, InRoomToleranceFt))
            {
                FireProtectionLog.Warn(DeviceNounCap + " point refused at (" + point.X.ToString("F2") + ","
                    + point.Y.ToString("F2") + "): outside the room boundary in host coordinates "
                    + "(check the linked-model coordinate transform).");
                return PointResult.Failed;
            }

            // Minimal duplicate safety check (does not re-implement calc spacing).
            if (existingPoints.Any(p => p != null && p.DistanceTo(xyz) <= DuplicateProximityFt))
            {
                return PointResult.Failed;
            }

            Level level = ResolveHostLevel(point.LevelId, point.LevelName, out string levelError);
            if (level == null)
            {
                FireProtectionLog.Warn(DeviceNounCap + " point skipped: " + (levelError ?? "level unresolved") + ".");
                return PointResult.Failed;
            }

            // Sidewall override: a point carrying a WallEdgeIndex is wall-mounted regardless of the family's
            // FamilyPlacementType. Otherwise select by proven placement type; no match => refuse (no default).
            IFamilyPlacementStrategy strategy = null;
            if (point.WallEdgeIndex.HasValue)
            {
                strategy = _strategies.FirstOrDefault(s => s is WallSidewallPlacementStrategy);
            }
            if (strategy == null)
            {
                strategy = _strategies.FirstOrDefault(s => s.CanHandle(familyPlacementType));
            }
            if (strategy == null)
            {
                FireProtectionLog.Warn(DeviceNounCap + " point skipped: no strategy supports FamilyPlacementType '"
                    + familyPlacementType + "' with device-kind '" + _deviceNoun + "'.");
                return PointResult.Failed;
            }

            FireProtectionLog.Info(DeviceNounCap + " location-point strategy selected: "
                + strategy.GetType().Name + " (family placement type " + familyPlacementType + ").");

            try
            {
                var context = new PlacementContext
                {
                    Document = _document,
                    Symbol = symbol,
                    Level = level,
                    RequestedPoint = xyz,
                    FamilyPlacementType = familyPlacementType,
                    CeilingHostResolver = _ceilingHostResolver,
                    WallEdgeIndex = point.WallEdgeIndex,
                    RoomPolygon = roomPolygon
                };

                PlacementOutcome outcome = strategy.Place(context);
                if (!outcome.Created || outcome.Instance == null)
                {
                    FireProtectionLog.Warn(DeviceNounCap + " placement failed: "
                        + (outcome.Message ?? outcome.ErrorCode ?? "unknown reason") + ".");
                    return PointResult.Failed;
                }

                existingPoints.Add(xyz);

                // Post-placement validation: a gross deviation (e.g. a hosted family snapping to the origin)
                // is PLACED_BUT_INVALID, never a silent success. The element still exists (counted as placed).
                var placedLocation = outcome.Instance.Location as LocationPoint;
                XYZ actual = placedLocation?.Point;
                bool valid = actual != null && actual.DistanceTo(xyz) <= PlacementValidationToleranceFt;
                if (!valid)
                {
                    FireProtectionLog.Warn("[REVIEW] " + DeviceNounCap + " " + outcome.Instance.Id
                        + " (room " + point.RoomId + ", strategy " + outcome.HostingStrategy
                        + ") placed but spatially INVALID (deviation > "
                        + PlacementValidationToleranceFt.ToString("F2", CultureInfo.InvariantCulture) + " ft).");
                    return PointResult.PlacedInvalid;
                }

                return PointResult.Placed;
            }
            catch (Exception ex)
            {
                FireProtectionLog.Error(DeviceNounCap + " Revit placement failed at (" + point.X.ToString("F2")
                    + "," + point.Y.ToString("F2") + "," + point.Z.ToString("F2") + ").", ex);
                return PointResult.Failed;
            }
        }

        private FamilySymbol ResolveCachedSymbol(
            string familyName, string typeName,
            Dictionary<string, FamilySymbol> cache, HashSet<string> activatedKeys, out string error)
        {
            error = null;
            string key = (familyName ?? string.Empty) + "::" + (typeName ?? string.Empty);
            if (cache.TryGetValue(key, out FamilySymbol cached) && cached != null) return cached;

            FamilySymbol symbol = ResolveSymbol(_document, familyName, typeName, out error);
            if (symbol == null) return null;

            if (activatedKeys.Add(key) && !symbol.IsActive)
            {
#pragma warning disable CS0618
                symbol.Activate();
#pragma warning restore CS0618
            }
            cache[key] = symbol;
            return symbol;
        }

        private static FamilySymbol ResolveSymbol(Document doc, string familyName, string typeName, out string error)
        {
            error = null;
            if (doc == null) { error = "Host document is unavailable."; return null; }
            if (string.IsNullOrWhiteSpace(familyName) || string.IsNullOrWhiteSpace(typeName))
            {
                error = "Selected family/type name is missing.";
                return null;
            }

            var collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_FireAlarmDevices)
                .OfClass(typeof(FamilySymbol));

            foreach (Element element in collector)
            {
                if (element is FamilySymbol sym)
                {
                    string fam = sym.FamilyName ?? sym.Family?.Name;
                    if (string.Equals(fam, familyName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(sym.Name, typeName, StringComparison.OrdinalIgnoreCase))
                    {
                        return sym;
                    }
                }
            }

            error = "Family/type '" + familyName + ":" + typeName + "' was not found in the active document.";
            return null;
        }

        /// <summary>
        /// Maps the level identifier carried by a calculated point to the correct host-document Level.
        /// Handles the direct host-ElementId fast path, then linked-model levels (elevation-transformed into
        /// host space), then a host-name match. Never selects an arbitrary level silently. Port of the
        /// sprinkler service's resolver, reduced to the Level (the report only carries a message).
        /// </summary>
        private Level ResolveHostLevel(string levelId, string levelName, out string error)
        {
            error = null;

            // 1. Direct host-document ElementId.
            if (!string.IsNullOrWhiteSpace(levelId) && long.TryParse(levelId, out long idValue))
            {
                if (_document.GetElement(new ElementId(idValue)) is Level hostLevel) return hostLevel;
            }

            // 2. Linked-model level: find it in each link document, transform its elevation into host space,
            //    then match a host Level by elevation (or name).
            var linkCollector = new FilteredElementCollector(_document).OfClass(typeof(RevitLinkInstance));
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
                    linkLevel = FindLevelByName(linkDoc, levelName);
                }
                if (linkLevel == null) continue;

                double hostElevation = linkInstance.GetTotalTransform().OfPoint(new XYZ(0, 0, linkLevel.Elevation)).Z;
                Level mapped = FindLevelByElevation(_document, hostElevation)
                    ?? FindLevelByName(_document, levelName ?? linkLevel.Name);
                if (mapped != null) return mapped;
            }

            // 3. Last resort: host-document name match.
            if (!string.IsNullOrWhiteSpace(levelName))
            {
                Level byName = FindLevelByName(_document, levelName);
                if (byName != null) return byName;
            }

            error = "Required host level could not be resolved (levelId '" + levelId + "', name '" + levelName + "').";
            return null;
        }

        private static Level FindLevelByElevation(Document doc, double hostElevation)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .FirstOrDefault(l => Math.Abs(l.Elevation - hostElevation) <= 0.01);
        }

        private static Level FindLevelByName(Document doc, string levelName)
        {
            if (string.IsNullOrWhiteSpace(levelName)) return null;
            return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .FirstOrDefault(l => string.Equals(l.Name, levelName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// An existing fire-alarm device: its id and location, for the duplicate guard and room policy.
        /// <see cref="IsOwnKind"/> is true only when the device's family/type name classifies as the kind
        /// this run places — the only kind the policy may skip, delete, or treat as a duplicate.
        /// </summary>
        private sealed class ExistingDevice
        {
            public ElementId Id;
            public XYZ Point;
            public bool IsOwnKind;
        }

        private static List<ExistingDevice> CollectExistingDevices(Document doc, DeviceKind ownKind)
        {
            var found = new List<ExistingDevice>();
            if (doc == null) return found;

            var collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_FireAlarmDevices)
                .OfClass(typeof(FamilyInstance));

            foreach (Element element in collector)
            {
                if (element is FamilyInstance fi && fi.Location is LocationPoint lp && lp.Point != null)
                {
                    // Name-based kind attribution (OST cannot tell smoke from notification). An
                    // unclassifiable name is NOT own-kind: never touched, never a duplicate.
                    bool isOwn = false;
                    try
                    {
                        string fam = fi.Symbol?.FamilyName ?? fi.Symbol?.Family?.Name ?? fi.Name;
                        string typ = fi.Symbol?.Name;
                        if (DeviceKindResolver.TryResolve(fam, typ, out DeviceKind kind))
                            isOwn = kind == ownKind;
                    }
                    catch { /* name read failure -> not own-kind (conservative) */ }

                    found.Add(new ExistingDevice { Id = fi.Id, Point = lp.Point, IsOwnKind = isOwn });
                }
            }

            return found;
        }

        /// <summary>Own-kind existing devices whose XY falls inside the room polygon and whose Z is within the
        /// vertical window of this room's placement plane (so devices on other floors are not counted).
        /// Other-kind devices are deliberately excluded — a room with only sprinklers/NA devices is NOT
        /// "already has smoke detectors".</summary>
        private static List<ExistingDevice> DevicesInRoom(List<ExistingDevice> devices, List<double[]> polygon, double refZ)
        {
            var inRoom = new List<ExistingDevice>();
            if (devices == null || devices.Count == 0 || polygon == null || polygon.Count < 3) return inRoom;

            foreach (ExistingDevice d in devices)
            {
                if (d?.Point == null || !d.IsOwnKind) continue;
                if (Math.Abs(d.Point.Z - refZ) > ExistingDeviceZWindowFt) continue;
                if (GeometryMath.PointInPolygon(d.Point.X, d.Point.Y, polygon, InRoomToleranceFt))
                {
                    inRoom.Add(d);
                }
            }

            return inRoom;
        }

        private void DeleteDevices(List<ExistingDevice> toDelete, List<ExistingDevice> all, List<XYZ> allPoints)
        {
            foreach (ExistingDevice d in toDelete)
            {
                if (d?.Id == null) continue;
                try
                {
                    _document.Delete(d.Id);
                    all.Remove(d);
                    if (d.Point != null) allPoints.Remove(d.Point);
                }
                catch (Exception ex)
                {
                    FireProtectionLog.Warn("Could not delete existing device " + d.Id + ": " + ex.Message);
                }
            }
        }

        private static bool IsFinite(double x, double y, double z)
        {
            return !(double.IsNaN(x) || double.IsInfinity(x)
                  || double.IsNaN(y) || double.IsInfinity(y)
                  || double.IsNaN(z) || double.IsInfinity(z));
        }

        private static string FirstMessage(SmokeDetectorRoomCalculationResult room)
        {
            if (room == null) return null;
            if (room.Errors != null && room.Errors.Count > 0) return room.Errors[0];
            if (room.Warnings != null && room.Warnings.Count > 0) return room.Warnings[0];
            return null;
        }
    }
}
