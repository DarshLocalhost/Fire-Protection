////using System;
////using System.Collections.Generic;
////using Autodesk.Revit.DB;
////using FireProtection.Backend.Models.Data;
////using FireProtection.Backend.Models.Placement;
////using FireProtection.UI.Services;

////namespace FireProtection.Backend.Services.Placement
////{
////    /// <summary>
////    /// Implements the UI-side IPlacementExecutor by:
////    ///   1. Translating each PlacementRequestItem into a SprinklerPlacementRequest.
////    ///   2. Running the existing SprinklerPlacementService (generic algorithm - unchanged).
////    ///   3. Opening a single Transaction on the host document.
////    ///   4. Resolving the host Level ONCE PER ROOM from the linked-level elevation
////    ///      already carried in PlacementRequestItem.LevelElevationFt.
////    ///      (Sprinkler Z is NEVER used to identify a host Level.)
////    ///   5. Resolving the exact user-selected sprinkler FamilySymbol for each room request.
////    ///   6. Placing FamilyInstances via a per-room RevitSprinklerPlacer.
////    ///   7. Returning a PlacementRunReport to the UI with detailed per-room diagnostics.
////    /// </summary>
////    public class RevitPlacementExecutor : IPlacementExecutor
////    {
////        private readonly Document _hostDocument;
////        private readonly SprinklerPlacementService _placementService;
////        private readonly SprinklerFamilyResolver _familyResolver;
////        private readonly HostLevelResolver _levelResolver;

////        public RevitPlacementExecutor(Document hostDocument)
////        {
////            _hostDocument = hostDocument ?? throw new ArgumentNullException(nameof(hostDocument));

////            _placementService = new SprinklerPlacementService(new GenericSprinklerRuleProvider());
////            _familyResolver = new SprinklerFamilyResolver();
////            _levelResolver = new HostLevelResolver(hostDocument);
////        }

////        public PlacementRunReport ExecutePlacement(IReadOnlyList<PlacementRequestItem> items)
////        {
////            PlacementRunReport report = new PlacementRunReport();

////            if (items == null || items.Count == 0)
////            {
////                return report;
////            }

////            if (!_levelResolver.HasAnyLevel)
////            {
////                for (int i = 0; i < items.Count; i++)
////                {
////                    PlacementRequestItem it = items[i];
////                    report.RoomReports.Add(new PlacementRoomReport
////                    {
////                        RoomId = it.RoomId,
////                        RoomName = it.RoomName,
////                        LevelName = it.LevelName,
////                        Status = "Failed",
////                        Message = "PREFLIGHT: Host model has no Levels; cannot place sprinklers."
////                    });
////                    report.RoomsProcessed++;
////                    report.RoomsFailed++;
////                }
////                return report;
////            }

////            using (Transaction tx = new Transaction(_hostDocument, "Place Sprinklers"))
////            {
////                tx.Start();

////                for (int i = 0; i < items.Count; i++)
////                {
////                    PlacementRequestItem item = items[i];
////                    report.RoomsProcessed++;

////                    PlacementRoomReport roomReport = new PlacementRoomReport
////                    {
////                        RoomId = item.RoomId,
////                        RoomName = item.RoomName,
////                        LevelName = item.LevelName
////                    };

////                    try
////                    {
////                        if (string.IsNullOrWhiteSpace(item.SelectedSprinklerFamilyName) ||
////                            string.IsNullOrWhiteSpace(item.SelectedSprinklerTypeName))
////                        {
////                            roomReport.Status = "Failed";
////                            roomReport.Message =
////                                "PREFLIGHT: No sprinkler family/type was provided in the placement request.";
////                            report.RoomsFailed++;
////                            report.RoomReports.Add(roomReport);
////                            continue;
////                        }

////                        FamilySymbol symbol = _familyResolver.Resolve(
////                            _hostDocument,
////                            item.SelectedSprinklerFamilyName,
////                            item.SelectedSprinklerTypeName);

////                        if (symbol == null)
////                        {
////                            roomReport.Status = "Failed";
////                            roomReport.Message =
////                                "PREFLIGHT: The selected sprinkler could not be resolved in the host model: '"
////                                + item.SelectedSprinklerFamilyName + "' / '"
////                                + item.SelectedSprinklerTypeName + "'. "
////                                + "No fallback sprinkler was used.";
////                            report.RoomsFailed++;
////                            report.RoomReports.Add(roomReport);
////                            continue;
////                        }

////                        double deltaFt;
////                        Level hostLevel = _levelResolver.Resolve(item.LevelElevationFt, out deltaFt);

////                        if (hostLevel == null)
////                        {
////                            roomReport.Status = "Failed";
////                            roomReport.Message =
////                                "HOST-LEVEL: No host Level within tolerance ("
////                                + _levelResolver.ToleranceFt.ToString("F2") + " ft) of linked level '"
////                                + (item.LevelName ?? "<unnamed>") + "' elevation "
////                                + item.LevelElevationFt.ToString("F3") + " ft. "
////                                + "Nearest delta = " + (deltaFt == double.MaxValue
////                                    ? "n/a"
////                                    : deltaFt.ToString("F3") + " ft") + ". "
////                                + "Ensure a host Level exists at the same elevation as the linked level, "
////                                + "or increase the host-level tolerance.";
////                            report.RoomsFailed++;
////                            report.RoomReports.Add(roomReport);
////                            continue;
////                        }

////                        SprinklerPlacementRequest req = BuildRequest(item);
////                        SprinklerPlacementResult calc = _placementService.PlaceForRoom(req);

////                        roomReport.PointsRequested = calc.SprinklerCount;
////                        report.SprinklersRequested += calc.SprinklerCount;

////                        if (calc.Status == SprinklerPlacementStatus.Skipped)
////                        {
////                            roomReport.Status = "Skipped";
////                            roomReport.Message = "CALC-SKIPPED: " + (calc.StatusMessage ?? "(no reason)");
////                            report.RoomsSkipped++;
////                            report.RoomReports.Add(roomReport);
////                            continue;
////                        }

////                        if (calc.Status != SprinklerPlacementStatus.Success
////                            || calc.Points == null
////                            || calc.Points.Count == 0)
////                        {
////                            roomReport.Status = "Failed";
////                            roomReport.Message = "CALC-FAILED: "
////                                + (calc.StatusMessage ?? "Placement calculation produced no points.");
////                            report.RoomsFailed++;
////                            report.RoomReports.Add(roomReport);
////                            continue;
////                        }

////                        RevitSprinklerPlacer placer = new RevitSprinklerPlacer(
////                            _hostDocument, symbol, hostLevel);

////                        List<string> perPointErrors = new List<string>();
////                        int placed = placer.Place(calc.Points, perPointErrors);

////                        roomReport.PointsPlaced = placed;
////                        report.SprinklersPlaced += placed;

////                        if (placed == calc.Points.Count)
////                        {
////                            roomReport.Status = "Success";
////                            roomReport.Message = "Placed " + placed + " sprinkler(s) using '"
////                                + item.SelectedSprinklerFamilyName + " / "
////                                + item.SelectedSprinklerTypeName + "' on host level '"
////                                + hostLevel.Name + "' (delta " + deltaFt.ToString("F3") + " ft).";
////                            report.RoomsSucceeded++;
////                        }
////                        else if (placed > 0)
////                        {
////                            roomReport.Status = "Success";
////                            roomReport.Message =
////                                "Placed " + placed + " of " + calc.Points.Count
////                                + " sprinkler(s) using '" + item.SelectedSprinklerFamilyName + " / "
////                                + item.SelectedSprinklerTypeName
////                                + "' on host level '" + hostLevel.Name
////                                + "' (delta " + deltaFt.ToString("F3") + " ft). "
////                                + "Per-point errors: " + Join(perPointErrors);
////                            report.RoomsSucceeded++;
////                        }
////                        else
////                        {
////                            roomReport.Status = "Failed";
////                            roomReport.Message =
////                                "PLACE-FAILED: No sprinkler could be created using selected sprinkler '"
////                                + item.SelectedSprinklerFamilyName + " / "
////                                + item.SelectedSprinklerTypeName
////                                + "' on host level '" + hostLevel.Name + "'. Per-point errors: " + Join(perPointErrors);
////                            report.RoomsFailed++;
////                        }
////                    }
////                    catch (Exception ex)
////                    {
////                        roomReport.Status = "Failed";
////                        roomReport.Message = "UNEXPECTED: " + ex.GetType().Name + " - " + ex.Message;
////                        report.RoomsFailed++;
////                    }

////                    report.RoomReports.Add(roomReport);
////                }

////                tx.Commit();
////            }

////            return report;
////        }

////        private static string Join(List<string> messages)
////        {
////            if (messages == null || messages.Count == 0) return "(none)";
////            return string.Join(" | ", messages);
////        }

////        private static SprinklerPlacementRequest BuildRequest(PlacementRequestItem item)
////        {
////            LevelData level = new LevelData
////            {
////                LevelId = item.LevelId,
////                Name = item.LevelName,
////                ElevationFt = item.LevelElevationFt
////            };

////            RoomData room = new RoomData
////            {
////                RoomId = item.RoomId,
////                Name = item.RoomName,
////                Number = item.RoomNumber,
////                LevelName = item.LevelName,
////                AreaSqFt = item.AreaSqFt,
////                Classification = new ClassificationData
////                {
////                    HazardClass = item.EffectiveHazardClass,
////                    SuggestedByClassifier = item.EffectiveHazardClass
////                },
////                Geometry = new GeometryData
////                {
////                    CeilingHeightFt = item.CeilingHeightFt,
////                    Polygon = item.Polygon != null
////                        ? new List<double[]>(item.Polygon)
////                        : new List<double[]>()
////                }
////            };

////            return new SprinklerPlacementRequest
////            {
////                Level = level,
////                Room = room,
////                EffectiveHazardClass = item.EffectiveHazardClass
////            };
////        }
////    }
////}







/////new
/////











//using System;
//using System.Collections.Generic;
//using Autodesk.Revit.DB;
//using FireProtection.Backend.Models.Data;
//using FireProtection.Backend.Models.Placement;
//using FireProtection.UI.Services;

//namespace FireProtection.Backend.Services.Placement
//{
//    /// <summary>
//    /// Implements the UI-side IPlacementExecutor by:
//    ///   1. Translating each PlacementRequestItem into a SprinklerPlacementRequest.
//    ///   2. Running the existing SprinklerPlacementService (generic algorithm) OR,
//    ///      when a SnowdonPlacementProvider is supplied, bypassing the generic
//    ///      algorithm and using the pre-loaded Snowdon JSON coordinates instead.
//    ///   3. Opening a single Transaction on the host document.
//    ///   4. Resolving the host Level ONCE PER ROOM from the linked-level elevation
//    ///      already carried in PlacementRequestItem.LevelElevationFt.
//    ///      (Sprinkler Z is NEVER used to identify a host Level.)
//    ///   5. Resolving the exact user-selected sprinkler FamilySymbol for each room request.
//    ///   6. Placing FamilyInstances via a per-room RevitSprinklerPlacer.
//    ///   7. Returning a PlacementRunReport to the UI with detailed per-room diagnostics.
//    ///
//    /// SNOWDON TEST MODE:
//    ///   Pass a non-null SnowdonPlacementProvider to the constructor.
//    ///   When active, GenerateGridPoints() is never called.
//    ///   Sprinkler locations come entirely from the Snowdon JSON.
//    ///   The user-selected family/type is still resolved and used.
//    /// </summary>
//    public class RevitPlacementExecutor : IPlacementExecutor
//    {
//        private readonly Document _hostDocument;
//        private readonly SprinklerPlacementService _placementService;
//        private readonly SprinklerFamilyResolver _familyResolver;
//        private readonly HostLevelResolver _levelResolver;

//        // Non-null only in Snowdon test mode.
//        private readonly SnowdonPlacementProvider _snowdonProvider;

//        /// <summary>
//        /// Standard constructor. Uses the generic grid placement algorithm.
//        /// </summary>
//        public RevitPlacementExecutor(Document hostDocument)
//            : this(hostDocument, null)
//        {
//        }

//        /// <summary>
//        /// Snowdon test-mode constructor.
//        /// When snowdonProvider is non-null, sprinkler locations come from the
//        /// Snowdon JSON instead of GenerateGridPoints().
//        /// </summary>
//        public RevitPlacementExecutor(
//            Document hostDocument,
//            SnowdonPlacementProvider snowdonProvider)
//        {
//            _hostDocument = hostDocument
//                ?? throw new ArgumentNullException(nameof(hostDocument));

//            _placementService =
//                new SprinklerPlacementService(new GenericSprinklerRuleProvider());
//            _familyResolver = new SprinklerFamilyResolver();
//            _levelResolver = new HostLevelResolver(hostDocument);

//            // May be null (standard mode).
//            _snowdonProvider = snowdonProvider;
//        }

//        public PlacementRunReport ExecutePlacement(IReadOnlyList<PlacementRequestItem> items)
//        {
//            PlacementRunReport report = new PlacementRunReport();

//            if (items == null || items.Count == 0)
//                return report;

//            if (!_levelResolver.HasAnyLevel)
//            {
//                for (int i = 0; i < items.Count; i++)
//                {
//                    PlacementRequestItem it = items[i];
//                    report.RoomReports.Add(new PlacementRoomReport
//                    {
//                        RoomId = it.RoomId,
//                        RoomName = it.RoomName,
//                        LevelName = it.LevelName,
//                        Status = "Failed",
//                        Message =
//                            "PREFLIGHT: Host model has no Levels; cannot place sprinklers."
//                    });
//                    report.RoomsProcessed++;
//                    report.RoomsFailed++;
//                }
//                return report;
//            }

//            using (Transaction tx = new Transaction(_hostDocument, "Place Sprinklers"))
//            {
//                tx.Start();

//                for (int i = 0; i < items.Count; i++)
//                {
//                    PlacementRequestItem item = items[i];
//                    report.RoomsProcessed++;

//                    PlacementRoomReport roomReport = new PlacementRoomReport
//                    {
//                        RoomId = item.RoomId,
//                        RoomName = item.RoomName,
//                        LevelName = item.LevelName
//                    };

//                    try
//                    {
//                        // ── PREFLIGHT: family/type ──────────────────────────────────
//                        if (string.IsNullOrWhiteSpace(item.SelectedSprinklerFamilyName) ||
//                            string.IsNullOrWhiteSpace(item.SelectedSprinklerTypeName))
//                        {
//                            roomReport.Status = "Failed";
//                            roomReport.Message =
//                                "PREFLIGHT: No sprinkler family/type was provided "
//                                + "in the placement request.";
//                            report.RoomsFailed++;
//                            report.RoomReports.Add(roomReport);
//                            continue;
//                        }

//                        FamilySymbol symbol = _familyResolver.Resolve(
//                            _hostDocument,
//                            item.SelectedSprinklerFamilyName,
//                            item.SelectedSprinklerTypeName);

//                        if (symbol == null)
//                        {
//                            roomReport.Status = "Failed";
//                            roomReport.Message =
//                                "PREFLIGHT: The selected sprinkler could not be resolved "
//                                + "in the host model: '"
//                                + item.SelectedSprinklerFamilyName + "' / '"
//                                + item.SelectedSprinklerTypeName + "'. "
//                                + "No fallback sprinkler was used.";
//                            report.RoomsFailed++;
//                            report.RoomReports.Add(roomReport);
//                            continue;
//                        }

//                        // ── PREFLIGHT: host level ───────────────────────────────────
//                        double deltaFt;
//                        Level hostLevel =
//                            _levelResolver.Resolve(item.LevelElevationFt, out deltaFt);

//                        if (hostLevel == null)
//                        {
//                            roomReport.Status = "Failed";
//                            roomReport.Message =
//                                "HOST-LEVEL: No host Level within tolerance ("
//                                + _levelResolver.ToleranceFt.ToString("F2")
//                                + " ft) of linked level '"
//                                + (item.LevelName ?? "<unnamed>")
//                                + "' elevation "
//                                + item.LevelElevationFt.ToString("F3") + " ft. "
//                                + "Nearest delta = "
//                                + (deltaFt == double.MaxValue
//                                    ? "n/a"
//                                    : deltaFt.ToString("F3") + " ft") + ". "
//                                + "Ensure a host Level exists at the same elevation "
//                                + "as the linked level, or increase the host-level tolerance.";
//                            report.RoomsFailed++;
//                            report.RoomReports.Add(roomReport);
//                            continue;
//                        }

//                        // ── PLACEMENT POINTS ────────────────────────────────────────
//                        List<SprinklerPlacementPoint> points;
//                        string modeLabel;

//                        if (_snowdonProvider != null)
//                        {
//                            // SNOWDON TEST MODE:
//                            // Bypass SprinklerPlacementService entirely.
//                            // Get points directly from the Snowdon JSON.
//                            // Z = levelElevationFt + offsetFromLevelFt
//                            points = _snowdonProvider.GetPoints(
//                                item.LevelId,
//                                item.RoomId,
//                                item.LevelElevationFt);

//                            modeLabel = "SNOWDON-JSON";

//                            if (points == null || points.Count == 0)
//                            {
//                                roomReport.Status = "Skipped";
//                                roomReport.Message =
//                                    "SNOWDON-JSON: No sprinkler entries found in the "
//                                    + "Snowdon JSON for roomId='"
//                                    + item.RoomId + "' levelId='"
//                                    + item.LevelId + "'. Room skipped.";
//                                report.RoomsSkipped++;
//                                report.RoomReports.Add(roomReport);
//                                continue;
//                            }
//                        }
//                        else
//                        {
//                            // STANDARD MODE: use the generic placement algorithm.
//                            SprinklerPlacementRequest req = BuildRequest(item);
//                            SprinklerPlacementResult calc =
//                                _placementService.PlaceForRoom(req);

//                            roomReport.PointsRequested = calc.SprinklerCount;
//                            report.SprinklersRequested += calc.SprinklerCount;

//                            if (calc.Status == SprinklerPlacementStatus.Skipped)
//                            {
//                                roomReport.Status = "Skipped";
//                                roomReport.Message =
//                                    "CALC-SKIPPED: "
//                                    + (calc.StatusMessage ?? "(no reason)");
//                                report.RoomsSkipped++;
//                                report.RoomReports.Add(roomReport);
//                                continue;
//                            }

//                            if (calc.Status != SprinklerPlacementStatus.Success
//                                || calc.Points == null
//                                || calc.Points.Count == 0)
//                            {
//                                roomReport.Status = "Failed";
//                                roomReport.Message =
//                                    "CALC-FAILED: "
//                                    + (calc.StatusMessage
//                                       ?? "Placement calculation produced no points.");
//                                report.RoomsFailed++;
//                                report.RoomReports.Add(roomReport);
//                                continue;
//                            }

//                            points = calc.Points;
//                            modeLabel = "GENERIC-GRID";
//                        }

//                        // ── REVIT PLACEMENT ─────────────────────────────────────────
//                        roomReport.PointsRequested = points.Count;
//                        report.SprinklersRequested += points.Count;

//                        RevitSprinklerPlacer placer = new RevitSprinklerPlacer(
//                            _hostDocument, symbol, hostLevel);

//                        List<string> perPointErrors = new List<string>();
//                        int placed = placer.Place(points, perPointErrors);

//                        roomReport.PointsPlaced = placed;
//                        report.SprinklersPlaced += placed;

//                        if (placed == points.Count)
//                        {
//                            roomReport.Status = "Success";
//                            roomReport.Message =
//                                "[" + modeLabel + "] Placed " + placed
//                                + " sprinkler(s) using '"
//                                + item.SelectedSprinklerFamilyName + " / "
//                                + item.SelectedSprinklerTypeName
//                                + "' on host level '" + hostLevel.Name
//                                + "' (delta " + deltaFt.ToString("F3") + " ft).";
//                            report.RoomsSucceeded++;
//                        }
//                        else if (placed > 0)
//                        {
//                            roomReport.Status = "Success";
//                            roomReport.Message =
//                                "[" + modeLabel + "] Placed " + placed
//                                + " of " + points.Count
//                                + " sprinkler(s) using '"
//                                + item.SelectedSprinklerFamilyName + " / "
//                                + item.SelectedSprinklerTypeName
//                                + "' on host level '" + hostLevel.Name
//                                + "' (delta " + deltaFt.ToString("F3") + " ft). "
//                                + "Per-point errors: " + Join(perPointErrors);
//                            report.RoomsSucceeded++;
//                        }
//                        else
//                        {
//                            roomReport.Status = "Failed";
//                            roomReport.Message =
//                                "[" + modeLabel
//                                + "] PLACE-FAILED: No sprinkler could be created using '"
//                                + item.SelectedSprinklerFamilyName + " / "
//                                + item.SelectedSprinklerTypeName
//                                + "' on host level '" + hostLevel.Name
//                                + "'. Per-point errors: " + Join(perPointErrors);
//                            report.RoomsFailed++;
//                        }
//                    }
//                    catch (Exception ex)
//                    {
//                        roomReport.Status = "Failed";
//                        roomReport.Message =
//                            "UNEXPECTED: " + ex.GetType().Name + " - " + ex.Message;
//                        report.RoomsFailed++;
//                    }

//                    report.RoomReports.Add(roomReport);
//                }

//                tx.Commit();
//            }

//            return report;
//        }

//        // ── Helpers ────────────────────────────────────────────────────────────

//        private static string Join(List<string> messages)
//        {
//            if (messages == null || messages.Count == 0) return "(none)";
//            return string.Join(" | ", messages);
//        }

//        private static SprinklerPlacementRequest BuildRequest(PlacementRequestItem item)
//        {
//            LevelData level = new LevelData
//            {
//                LevelId = item.LevelId,
//                Name = item.LevelName,
//                ElevationFt = item.LevelElevationFt
//            };

//            RoomData room = new RoomData
//            {
//                RoomId = item.RoomId,
//                Name = item.RoomName,
//                Number = item.RoomNumber,
//                LevelName = item.LevelName,
//                AreaSqFt = item.AreaSqFt,
//                Classification = new ClassificationData
//                {
//                    HazardClass = item.EffectiveHazardClass,
//                    SuggestedByClassifier = item.EffectiveHazardClass
//                },
//                Geometry = new GeometryData
//                {
//                    CeilingHeightFt = item.CeilingHeightFt,
//                    Polygon = item.Polygon != null
//                        ? new List<double[]>(item.Polygon)
//                        : new List<double[]>()
//                }
//            };

//            return new SprinklerPlacementRequest
//            {
//                Level = level,
//                Room = room,
//                EffectiveHazardClass = item.EffectiveHazardClass
//            };
//        }
//    }
//}

























using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Placement;
using FireProtection.UI.Services;

namespace FireProtection.Backend.Services.Placement
{
    /// <summary>
    /// Implements the UI-side IPlacementExecutor by:
    ///   1. Translating each PlacementRequestItem into a SprinklerPlacementRequest.
    ///   2. Running the existing SprinklerPlacementService (generic algorithm) OR,
    ///      when a SnowdonPlacementProvider is supplied, bypassing the generic
    ///      algorithm and using the pre-loaded Snowdon JSON coordinates instead.
    ///   3. Opening a single Transaction on the host document.
    ///   4. Resolving the host Level ONCE PER ROOM from the linked-level elevation
    ///      already carried in PlacementRequestItem.LevelElevationFt.
    ///      (Sprinkler Z is NEVER used to identify a host Level.)
    ///   5. Resolving the exact user-selected sprinkler FamilySymbol for each room.
    ///   6. Placing FamilyInstances via a per-room RevitSprinklerPlacer.
    ///   7. Returning a PlacementRunReport to the UI with detailed per-room diagnostics.
    ///
    /// SNOWDON TEST MODE:
    ///   Pass a non-null SnowdonPlacementProvider to the constructor.
    ///   When active, GenerateGridPoints() is never called.
    ///   Sprinkler locations come entirely from the Snowdon JSON.
    ///   The user-selected family/type is still resolved and used.
    ///
    /// GENERIC MODE:
    ///   Pass null (or use the single-argument constructor).
    ///   SprinklerPlacementService generates grid points as before.
    ///
    /// COUNTER RULE:
    ///   roomReport.PointsRequested and report.SprinklersRequested are set
    ///   exactly once per room, inside whichever branch runs. The shared
    ///   placement block does NOT touch these counters.
    /// </summary>
    public class RevitPlacementExecutor : IPlacementExecutor
    {
        private readonly Document _hostDocument;
        private readonly SprinklerPlacementService _placementService;
        private readonly SprinklerFamilyResolver _familyResolver;
        private readonly HostLevelResolver _levelResolver;

        // Non-null only in Snowdon test mode.
        private readonly SnowdonPlacementProvider _snowdonProvider;

        /// <summary>
        /// Standard constructor. Uses the generic grid placement algorithm.
        /// </summary>
        public RevitPlacementExecutor(Document hostDocument)
            : this(hostDocument, null)
        {
        }

        /// <summary>
        /// Snowdon test-mode constructor.
        /// When snowdonProvider is non-null, sprinkler locations come from the
        /// Snowdon JSON instead of GenerateGridPoints().
        /// </summary>
        public RevitPlacementExecutor(
            Document hostDocument,
            SnowdonPlacementProvider snowdonProvider)
        {
            _hostDocument = hostDocument
                ?? throw new ArgumentNullException(nameof(hostDocument));

            _placementService =
                new SprinklerPlacementService(new GenericSprinklerRuleProvider());
            _familyResolver = new SprinklerFamilyResolver();
            _levelResolver = new HostLevelResolver(hostDocument);

            // May be null (standard/generic mode).
            _snowdonProvider = snowdonProvider;
        }

        public PlacementRunReport ExecutePlacement(IReadOnlyList<PlacementRequestItem> items)
        {
            PlacementRunReport report = new PlacementRunReport();

            if (items == null || items.Count == 0)
                return report;

            if (!_levelResolver.HasAnyLevel)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    PlacementRequestItem it = items[i];
                    report.RoomReports.Add(new PlacementRoomReport
                    {
                        RoomId = it.RoomId,
                        RoomName = it.RoomName,
                        LevelName = it.LevelName,
                        Status = "Failed",
                        Message =
                            "PREFLIGHT: Host model has no Levels; cannot place sprinklers."
                    });
                    report.RoomsProcessed++;
                    report.RoomsFailed++;
                }
                return report;
            }

            using (Transaction tx = new Transaction(_hostDocument, "Place Sprinklers"))
            {
                tx.Start();

                for (int i = 0; i < items.Count; i++)
                {
                    PlacementRequestItem item = items[i];
                    report.RoomsProcessed++;

                    PlacementRoomReport roomReport = new PlacementRoomReport
                    {
                        RoomId = item.RoomId,
                        RoomName = item.RoomName,
                        LevelName = item.LevelName
                    };

                    try
                    {
                        // ── PREFLIGHT: family/type ──────────────────────────────
                        if (string.IsNullOrWhiteSpace(item.SelectedSprinklerFamilyName) ||
                            string.IsNullOrWhiteSpace(item.SelectedSprinklerTypeName))
                        {
                            roomReport.Status = "Failed";
                            roomReport.Message =
                                "PREFLIGHT: No sprinkler family/type was provided "
                                + "in the placement request.";
                            report.RoomsFailed++;
                            report.RoomReports.Add(roomReport);
                            continue;
                        }

                        FamilySymbol symbol = _familyResolver.Resolve(
                            _hostDocument,
                            item.SelectedSprinklerFamilyName,
                            item.SelectedSprinklerTypeName);

                        if (symbol == null)
                        {
                            roomReport.Status = "Failed";
                            roomReport.Message =
                                "PREFLIGHT: The selected sprinkler could not be resolved "
                                + "in the host model: '"
                                + item.SelectedSprinklerFamilyName + "' / '"
                                + item.SelectedSprinklerTypeName + "'. "
                                + "No fallback sprinkler was used.";
                            report.RoomsFailed++;
                            report.RoomReports.Add(roomReport);
                            continue;
                        }

                        // ── PREFLIGHT: host level ───────────────────────────────
                        double deltaFt;
                        Level hostLevel =
                            _levelResolver.Resolve(item.LevelElevationFt, out deltaFt);

                        if (hostLevel == null)
                        {
                            roomReport.Status = "Failed";
                            roomReport.Message =
                                "HOST-LEVEL: No host Level within tolerance ("
                                + _levelResolver.ToleranceFt.ToString("F2")
                                + " ft) of linked level '"
                                + (item.LevelName ?? "<unnamed>")
                                + "' elevation "
                                + item.LevelElevationFt.ToString("F3") + " ft. "
                                + "Nearest delta = "
                                + (deltaFt == double.MaxValue
                                    ? "n/a"
                                    : deltaFt.ToString("F3") + " ft") + ". "
                                + "Ensure a host Level exists at the same elevation "
                                + "as the linked level, or increase the host-level tolerance.";
                            report.RoomsFailed++;
                            report.RoomReports.Add(roomReport);
                            continue;
                        }

                        // ── PLACEMENT POINTS ────────────────────────────────────
                        //
                        // Each branch sets roomReport.PointsRequested and
                        // report.SprinklersRequested exactly once.
                        // The shared placement block below does NOT touch these.
                        //
                        List<SprinklerPlacementPoint> points;
                        string modeLabel;

                        if (_snowdonProvider != null)
                        {
                            // ── SNOWDON TEST MODE ───────────────────────────────
                            // Bypass SprinklerPlacementService entirely.
                            // X = Snowdon JSON x   (host-model coordinates, feet)
                            // Y = Snowdon JSON y   (host-model coordinates, feet)
                            // Z = item.LevelElevationFt + entry.OffsetFromLevelFt
                            points = _snowdonProvider.GetPoints(
                                item.LevelId,
                                item.RoomId,
                                item.LevelElevationFt);

                            modeLabel = "SNOWDON-JSON";

                            if (points == null || points.Count == 0)
                            {
                                roomReport.Status = "Skipped";
                                roomReport.Message =
                                    "SNOWDON-JSON: No sprinkler entries found in the "
                                    + "Snowdon JSON for roomId='"
                                    + item.RoomId + "' levelId='"
                                    + item.LevelId + "'. Room skipped.";
                                report.RoomsSkipped++;
                                report.RoomReports.Add(roomReport);
                                continue;
                            }

                            // Counters set exactly once — Snowdon branch.
                            roomReport.PointsRequested = points.Count;
                            report.SprinklersRequested += points.Count;
                        }
                        else
                        {
                            // ── GENERIC GRID MODE ───────────────────────────────
                            SprinklerPlacementRequest req = BuildRequest(item);
                            SprinklerPlacementResult calc =
                                _placementService.PlaceForRoom(req);

                            // Counters set exactly once — generic branch.
                            roomReport.PointsRequested = calc.SprinklerCount;
                            report.SprinklersRequested += calc.SprinklerCount;

                            if (calc.Status == SprinklerPlacementStatus.Skipped)
                            {
                                roomReport.Status = "Skipped";
                                roomReport.Message =
                                    "CALC-SKIPPED: "
                                    + (calc.StatusMessage ?? "(no reason)");
                                report.RoomsSkipped++;
                                report.RoomReports.Add(roomReport);
                                continue;
                            }

                            if (calc.Status != SprinklerPlacementStatus.Success
                                || calc.Points == null
                                || calc.Points.Count == 0)
                            {
                                roomReport.Status = "Failed";
                                roomReport.Message =
                                    "CALC-FAILED: "
                                    + (calc.StatusMessage
                                       ?? "Placement calculation produced no points.");
                                report.RoomsFailed++;
                                report.RoomReports.Add(roomReport);
                                continue;
                            }

                            points = calc.Points;
                            modeLabel = "GENERIC-GRID";
                        }

                        // ── REVIT PLACEMENT ─────────────────────────────────────
                        // PointsRequested and SprinklersRequested were set above
                        // in the branch that ran. Do NOT set them again here.
                        RevitSprinklerPlacer placer = new RevitSprinklerPlacer(
                            _hostDocument, symbol, hostLevel);

                        List<string> perPointErrors = new List<string>();
                        int placed = placer.Place(points, perPointErrors);

                        roomReport.PointsPlaced = placed;
                        report.SprinklersPlaced += placed;

                        if (placed == points.Count)
                        {
                            roomReport.Status = "Success";
                            roomReport.Message =
                                "[" + modeLabel + "] Placed " + placed
                                + " sprinkler(s) using '"
                                + item.SelectedSprinklerFamilyName + " / "
                                + item.SelectedSprinklerTypeName
                                + "' on host level '" + hostLevel.Name
                                + "' (delta " + deltaFt.ToString("F3") + " ft).";
                            report.RoomsSucceeded++;
                        }
                        else if (placed > 0)
                        {
                            roomReport.Status = "Success";
                            roomReport.Message =
                                "[" + modeLabel + "] Placed " + placed
                                + " of " + points.Count
                                + " sprinkler(s) using '"
                                + item.SelectedSprinklerFamilyName + " / "
                                + item.SelectedSprinklerTypeName
                                + "' on host level '" + hostLevel.Name
                                + "' (delta " + deltaFt.ToString("F3") + " ft). "
                                + "Per-point errors: " + Join(perPointErrors);
                            report.RoomsSucceeded++;
                        }
                        else
                        {
                            roomReport.Status = "Failed";
                            roomReport.Message =
                                "[" + modeLabel
                                + "] PLACE-FAILED: No sprinkler could be created using '"
                                + item.SelectedSprinklerFamilyName + " / "
                                + item.SelectedSprinklerTypeName
                                + "' on host level '" + hostLevel.Name
                                + "'. Per-point errors: " + Join(perPointErrors);
                            report.RoomsFailed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        roomReport.Status = "Failed";
                        roomReport.Message =
                            "UNEXPECTED: " + ex.GetType().Name + " - " + ex.Message;
                        report.RoomsFailed++;
                    }

                    report.RoomReports.Add(roomReport);
                }

                tx.Commit();
            }

            return report;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static string Join(List<string> messages)
        {
            if (messages == null || messages.Count == 0) return "(none)";
            return string.Join(" | ", messages);
        }

        private static SprinklerPlacementRequest BuildRequest(PlacementRequestItem item)
        {
            LevelData level = new LevelData
            {
                LevelId = item.LevelId,
                Name = item.LevelName,
                ElevationFt = item.LevelElevationFt
            };

            RoomData room = new RoomData
            {
                RoomId = item.RoomId,
                Name = item.RoomName,
                Number = item.RoomNumber,
                LevelName = item.LevelName,
                AreaSqFt = item.AreaSqFt,
                Classification = new ClassificationData
                {
                    HazardClass = item.EffectiveHazardClass,
                    SuggestedByClassifier = item.EffectiveHazardClass
                },
                Geometry = new GeometryData
                {
                    CeilingHeightFt = item.CeilingHeightFt,
                    Polygon = item.Polygon != null
                        ? new List<double[]>(item.Polygon)
                        : new List<double[]>()
                }
            };

            return new SprinklerPlacementRequest
            {
                Level = level,
                Room = room,
                EffectiveHazardClass = item.EffectiveHazardClass
            };
        }
    }
}