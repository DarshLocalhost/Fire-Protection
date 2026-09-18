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
    /// both live in <c>OST_FireAlarmDevices</c> and use the same coverage-grid geometry).
    /// </summary>
    public class FireAlarmDevicePlacementCore : IDevicePlacementExecutor
    {
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

            _strategies = new IFamilyPlacementStrategy[]
            {
                new FaceBasedPlacementStrategy(),
                new WorkPlaneBasedPlacementStrategy(),
                new LevelBasedPlacementStrategy(),
                new WallSidewallPlacementStrategy()
            };
        }

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

            DeviceKind ownKind = items[0] != null ? items[0].DeviceKind : DeviceKind.SmokeDetector;
            List<ExistingDevice> existing = CollectExistingDevices(_document, ownKind);
            var existingPoints = existing.Where(e => e.IsOwnKind && e.Point != null).Select(e => e.Point).ToList();

            bool cancelled = false;

            using (var group = new TransactionGroup(_document, _transactionGroupName))
            {
                group.Start();
                cancelled = RunPlacement(calc, existing, existingPoints, progress, existingDevicePolicy, report);
                if (cancelled) group.RollBack();
                else group.Assimilate();
            }

            if (cancelled)
            {
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

        public IReadOnlyDictionary<string, PlacementEligibilityResult> EvaluateEligibility(
            IReadOnlyList<DeviceRoomInputItem> items)
        {
            var map = new Dictionary<string, PlacementEligibilityResult>(StringComparer.Ordinal);
            if (items == null || items.Count == 0) return map;

            SmokeDetectorCalculationResult calc;
            try
            {
                calc = _calculate(items);
            }
            catch (Exception ex)
            {
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
                    map[item.RoomId] = PlacementEligibilityResult.Undetermined(
                        "The calculation produced no result for this room; eligibility is undetermined.",
                        PlacementEligibilityStatusCodes.CalculationFailed);
                    continue;
                }

                map[item.RoomId] = EvaluateRoomEligibility(room);
            }

            return map;
        }

        private PlacementEligibilityResult EvaluateRoomEligibility(SmokeDetectorRoomCalculationResult room)
        {
            try
            {
                if (room.Status == CalculationStatus.InvalidRoomGeometry)
                    return PlacementEligibilityResult.Blocked(
                        PlacementEligibilityStatusCodes.MissingRoomGeometry,
                        FirstMessage(room) ?? "Room boundary is incomplete (at least 3 points required for placement).");

                if (room.Status == CalculationStatus.InvalidInput)
                {
                    string msg = FirstMessage(room) ?? "The selected family/type has an unsupported placement behaviour.";
                    string code = PlacementEligibilityStatusCodes.UnsupportedFamilyPlacementType;
                    if (msg.IndexOf("beam path", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        msg.IndexOf("minimum listed path", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        msg.IndexOf("Beam smoke detectors", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        msg.IndexOf("Optical beam", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        code = PlacementEligibilityStatusCodes.BeamPathTooShort;
                    }
                    return PlacementEligibilityResult.Blocked(code, msg);
                }

                if (room.Status == CalculationStatus.Failed)
                    return PlacementEligibilityResult.Undetermined(
                        FirstMessage(room) ?? "Calculation failed for this room; eligibility is undetermined.",
                        PlacementEligibilityStatusCodes.CalculationFailed);

                if (room.Points == null || room.Points.Count == 0)
                    return PlacementEligibilityResult.Blocked(
                        PlacementEligibilityStatusCodes.NoCandidatePoints,
                        FirstMessage(room) ?? "The calculation produced no candidate points for this room; nothing can be placed here.");

                FamilySymbol symbol = ResolveSymbol(_document, room.FamilyName, room.TypeName, out string symbolError);
                if (symbol == null)
                {
                    return PlacementEligibilityResult.Undetermined(
                        symbolError ?? ("Family/type '" + (room.FamilyName ?? "?") + ":" + (room.TypeName ?? "?")
                            + "' was not found in the active document."),
                        PlacementEligibilityStatusCodes.FamilyNotLoaded);
                }

                var result = new PlacementEligibilityResult();
                string placementType = SafePlacementType(symbol);
                result.FamilyPlacementType = placementType;

                bool isSidewall = room.Points.Any(p => p != null && p.WallEdgeIndex.HasValue);
                IFamilyPlacementStrategy strategy = isSidewall
                    ? _strategies.FirstOrDefault(s => s is WallSidewallPlacementStrategy)
                    : _strategies.FirstOrDefault(s => s.CanHandle(placementType));
                if (strategy == null)
                    return PlacementEligibilityResult.Blocked(
                        result,
                        PlacementEligibilityStatusCodes.UnsupportedFamilyPlacementType,
                        "No placement strategy supports the family's placement type '" + placementType + "'.");

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
                    catch { /* host lookup is best-effort */ }

                    bool hasHostFace = host != null && host.HostFace != null;
                    if (requiresCeilingHost && !hasHostFace) continue;

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
                        string lastFailureReason = null;

                        foreach (CalculatedSmokeDetectorPoint point in room.Points)
                        {
                            PointResult pr = PlaceSinglePoint(symbol, placementType, point, room.Polygon, existingPoints, out string pointError);
                            if (pr == PointResult.Placed) placed++;
                            else if (pr == PointResult.PlacedInvalid) { placed++; invalid++; }
                            else
                            {
                                failed++;
                                if (pointError != null) lastFailureReason = pointError;
                            }
                        }

                        roomReport.PointsPlaced = placed;
                        bool calcReview = room.Status == CalculationStatus.ReviewRequired;
                        if (placed == 0)
                        {
                            roomReport.Status = "Failed";
                            roomReport.FailureReason = lastFailureReason ?? ("No " + _deviceNoun + "s could be placed (" + failed + " point(s) failed).");
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
    List<XYZ> existingPoints,
    out string failureReason)
        {
            failureReason = null;
            if (point == null || !IsFinite(point.X, point.Y, point.Z))
            {
                failureReason = "Non-finite point coordinates.";
                return PointResult.Failed;
            }

            var xyz = new XYZ(point.X, point.Y, point.Z);

            List<double[]> hostPolygon = roomPolygon;
            if (roomPolygon != null && roomPolygon.Count >= 3)
            {
                hostPolygon = TransformPolygonToHostSpace(roomPolygon, point.LevelId, point.LevelName);
            }

            if (hostPolygon != null && hostPolygon.Count >= 3
                && !GeometryMath.PointInPolygon(point.X, point.Y, hostPolygon, InRoomToleranceFt))
            {
                failureReason = "Point falls outside host room boundary.";
                return PointResult.Failed;
            }

            if (existingPoints.Any(p => p != null && p.DistanceTo(xyz) <= DuplicateProximityFt))
            {
                failureReason = "Point skipped (duplicate existing device nearby).";
                return PointResult.Failed;
            }

            Level level = ResolveHostLevel(point.LevelId, point.LevelName, out string levelError);
            if (level == null)
            {
                failureReason = levelError ?? "Host level unresolved.";
                return PointResult.Failed;
            }

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
                failureReason = "No strategy supports placement type '" + familyPlacementType + "'.";
                return PointResult.Failed;
            }

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
                    RoomPolygon = hostPolygon
                };

                PlacementOutcome outcome = strategy.Place(context);
                if (!outcome.Created || outcome.Instance == null)
                {
                    failureReason = outcome.Message ?? outcome.ErrorCode ?? "Placement strategy returned failure.";
                    return PointResult.Failed;
                }

                // Removed redundant EnforceLevelAndOffset call here since it's strictly handled in the strategies!

                existingPoints.Add(xyz);

                var placedLocation = outcome.Instance.Location as LocationPoint;
                XYZ actual = placedLocation?.Point;
                bool valid = false;

                if (actual != null)
                {
                    double xyDeviationFt = Math.Sqrt(Math.Pow(actual.X - xyz.X, 2) + Math.Pow(actual.Y - xyz.Y, 2));
                    double zDeviationFt = Math.Abs(actual.Z - xyz.Z);

                    valid = xyDeviationFt <= PlacementValidationToleranceFt && zDeviationFt <= ExistingDeviceZWindowFt;
                }

                if (!valid)
                {
                    return PointResult.PlacedInvalid;
                }

                return PointResult.Placed;
            }
            catch (Exception ex)
            {
                failureReason = "Revit creation exception: " + ex.Message;
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

        private Level ResolveHostLevel(string levelId, string levelName, out string error)
        {
            error = null;

            if (!string.IsNullOrWhiteSpace(levelId) && long.TryParse(levelId, out long idValue))
            {
                if (_document.GetElement(new ElementId(idValue)) is Level hostLevel) return hostLevel;
            }

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
                    bool isOwn = false;
                    try
                    {
                        string fam = fi.Symbol?.FamilyName ?? fi.Symbol?.Family?.Name ?? fi.Name;
                        string typ = fi.Symbol?.Name;
                        if (DeviceKindResolver.TryResolve(fam, typ, out DeviceKind kind))
                            isOwn = kind == ownKind;
                    }
                    catch { /* name read failure -> not own-kind */ }

                    found.Add(new ExistingDevice { Id = fi.Id, Point = lp.Point, IsOwnKind = isOwn });
                }
            }

            return found;
        }

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

        private List<double[]> TransformPolygonToHostSpace(
            List<double[]> polygon, string levelId, string levelName)
        {
            if (polygon == null || polygon.Count < 3) return polygon;

            if (!string.IsNullOrWhiteSpace(levelId) && long.TryParse(levelId, out long idValue))
            {
                if (_document.GetElement(new ElementId(idValue)) is Level)
                    return polygon;
            }

            var linkCollector = new FilteredElementCollector(_document).OfClass(typeof(RevitLinkInstance));
            foreach (Element element in linkCollector)
            {
                if (!(element is RevitLinkInstance linkInstance)) continue;
                Document linkDoc = linkInstance.GetLinkDocument();
                if (linkDoc == null) continue;

                Level linkLevel = null;
                if (!string.IsNullOrWhiteSpace(levelId) && long.TryParse(levelId, out long linkIdValue))
                    linkLevel = linkDoc.GetElement(new ElementId(linkIdValue)) as Level;
                if (linkLevel == null && !string.IsNullOrWhiteSpace(levelName))
                    linkLevel = FindLevelByName(linkDoc, levelName);
                if (linkLevel == null) continue;

                Transform toHost = linkInstance.GetTotalTransform();
                var transformed = new List<double[]>(polygon.Count);
                foreach (double[] v in polygon)
                {
                    if (v == null || v.Length < 2) continue;
                    XYZ linkPt = new XYZ(v[0], v[1], 0);
                    XYZ hostPt = toHost.OfPoint(linkPt);
                    transformed.Add(new double[] { hostPt.X, hostPt.Y });
                }
                return transformed;
            }

            return polygon;
        }
    }
}