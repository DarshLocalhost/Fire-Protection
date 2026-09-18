using System;
using System.Collections.Generic;
using FireProtection.Backend.Services.Placement.Devices;
using FireProtection.UI.Models.Sprinklers.BruteForce;
using FireProtection.UI.Services;

namespace FireProtection.Tests
{
    /// <summary>
    /// Revit-free tests for the sprinkler -> PlacementRunReport mapper (the BruteForce tab's
    /// engineering-grade report) and for the device-kind resolver that scopes existing-device
    /// detection (the cross-device skip/fail fix). Pure logic; no Revit assemblies touched.
    /// </summary>
    public static class DeviceReportAndKindTests
    {
        private static int _failures;

        public static void RunAll()
        {
            _failures = 0;
            TestMapperHappyPath();
            TestMapperInvalidPlacementsBecomeReview();
            TestMapperDuplicatesAndOutsideCountAsSkippedNotFailed();
            TestMapperCancelledRun();
            TestMapperProvisionalFlagSurvives();
            TestKindResolverSeparatesSmokeFromNotification();
            TestKindResolverRejectsUnknownNames();

            if (_failures == 0)
            {
                Console.WriteLine("DeviceReportAndKindTests: PASS");
            }
            else
            {
                Console.WriteLine("DeviceReportAndKindTests: " + _failures + " FAIL(s)");
                throw new Exception("DeviceReportAndKindTests failed");
            }
        }

        private static void Check(bool condition, string message)
        {
            if (condition) Console.WriteLine("  PASS: " + message);
            else
            {
                Console.WriteLine("  FAIL: " + message);
                _failures++;
            }
        }

        // ----- mapper fixtures -----

        private static PlacedSprinklerEntry Placed(string id, bool valid, double devFt = 0.0)
        {
            return new PlacedSprinklerEntry
            {
                RevitElementId = id,
                RoomId = "R1",
                IsSpatiallyValid = valid,
                PlacementDeviationFt = devFt,
                X = 10, Y = 10, Z = 9,
                RequestedX = 10, RequestedY = 10, RequestedZ = 9,
                HostLevelName = "L1"
            };
        }

        private static FailedSprinklerEntry Failed(string code, bool dup = false, bool outside = false)
        {
            return new FailedSprinklerEntry
            {
                RoomId = "R1",
                ErrorCode = code,
                SkippedDueToDuplicate = dup,
                Reason = "test reason"
            };
        }

        private static SprinklerPlacementResult ResultWith(PlacementRoomResult room)
        {
            var r = new SprinklerPlacementResult { RoomsProcessed = 1, ResolvedFamilyPlacementType = "WorkPlaneBased" };
            if (room != null) r.Rooms.Add(room);
            return r;
        }

        private static void TestMapperHappyPath()
        {
            Console.WriteLine("Test: mapper -> all valid placements report Success");
            var room = new PlacementRoomResult { RoomId = "R1", RoomName = "Room 1" };
            room.Placed.Add(Placed("1", true));
            room.Placed.Add(Placed("2", true));
            var calc = new BruteForceCalculationResult { IsProvisional = false, AppliedRulesSummary = "approved" };

            // Mirror the service's post-run recompute (it sums these off the Rooms list before handing over).
            var result = ResultWith(room);
            result.CalculatedSprinklerCount = 2;
            result.PlacedSprinklerCount = 2;
            result.PlacedAndValidCount = 2;

            PlacementRunReport report = result.ToPlacementRunReport(calc, "Fam", "Type");
            Check(report.RoomsSucceeded == 1 && report.RoomsFailed == 0, "room counted as succeeded");
            Check(report.SprinklersPlaced == 2 && report.SprinklersRequested == 2, "placed/requested counts carried through");
            Check(report.OverallStatus == "Success", "overall status Success (got " + report.OverallStatus + ")");
            Check(report.RoomReports[0].LevelName == "L1", "host level name mapped");
        }

        private static void TestMapperInvalidPlacementsBecomeReview()
        {
            Console.WriteLine("Test: mapper -> spatially invalid instance becomes ReviewRequired with deviation in the message");
            var room = new PlacementRoomResult { RoomId = "R1", RoomName = "Room 1" };
            room.Placed.Add(Placed("1", true));
            room.Placed.Add(Placed("2", false, 12.3));
            var result = ResultWith(room);
            result.PlacedButInvalidCount = 1;

            PlacementRunReport report = result.ToPlacementRunReport(null, "F", "T");
            Check(report.ReviewRequiredCount == 1, "room flagged ReviewRequired");
            Check(report.OverallStatus == "ReviewRequired", "overall ReviewRequired (got " + report.OverallStatus + ")");
            Check(report.RoomReports[0].Message.Contains("deviation"), "message carries deviation evidence");
        }

        private static void TestMapperDuplicatesAndOutsideCountAsSkippedNotFailed()
        {
            Console.WriteLine("Test: mapper -> duplicate skips and outside-room refusals are issues, not silent failures");
            var room = new PlacementRoomResult { RoomId = "R1", RoomName = "Room 1" };
            room.Placed.Add(Placed("1", true));
            room.Failed.Add(Failed("SKIPPED_DUPLICATE", dup: true));
            room.Failed.Add(Failed("OUTSIDE_ROOM_BOUNDARY"));
            var result = ResultWith(room);
            result.SkippedDuplicateCount = 1;
            result.SkippedOutsideRoomCount = 1;

            PlacementRunReport report = result.ToPlacementRunReport(null, "F", "T");
            Check(report.RoomsFailed == 0, "room not Failed (one valid placement)");
            Check(report.FailedReasons.Count == 0, "no failure reasons");
            Check(report.ReviewRequiredReasons.Count == 2, "skip + outside land in the review issue list (got " + report.ReviewRequiredReasons.Count + ")");
        }

        private static void TestMapperCancelledRun()
        {
            Console.WriteLine("Test: mapper -> cancelled run reports Cancelled with zeroed counters");
            // Mirrors what the real service leaves after the rollback: the per-room list is cleared and
            // the placed counts recomputed to zero, WasCancelled set.
            var result = ResultWith(null);
            result.WasCancelled = true;
            result.Rooms.Clear();
            result.PlacedSprinklerCount = 0;
            result.CalculatedSprinklerCount = 5;

            PlacementRunReport report = result.ToPlacementRunReport(null, "F", "T");
            Check(report.OverallStatus == "Cancelled", "status Cancelled");
            Check(report.RoomReports.Count == 0 && report.SprinklersPlaced == 0, "nothing reported placed after rollback");
            Check(report.Summary != null && report.Summary.ToLowerInvariant().Contains("roll"), "summary explains the rollback");
        }

        private static void TestMapperProvisionalFlagSurvives()
        {
            Console.WriteLine("Test: mapper -> provisional calc flag reaches the report and the summary");
            var room = new PlacementRoomResult { RoomId = "R1", RoomName = "Room 1" };
            room.Placed.Add(Placed("1", true));
            var calc = new BruteForceCalculationResult { IsProvisional = true };

            PlacementRunReport report = ResultWith(room).ToPlacementRunReport(calc, "F", "T");
            Check(report.IsProvisional, "IsProvisional carried from calc");
            Check(report.Summary.Contains("PROVISIONAL"), "summary states provisional rules");
            Check(report.OverallStatus == "ReviewRequired", "provisional never reports plain Success (got " + report.OverallStatus + ")");
        }

        // ----- device-kind resolver -----

        private static void TestKindResolverSeparatesSmokeFromNotification()
        {
            Console.WriteLine("Test: kind resolver -> smoke vs notification separated, sprinkler names excluded");
            Check(DeviceKindResolver.TryResolve("Smoke Detector", "Photoelectric", out DeviceKind k1)
                  && k1 == DeviceKind.SmokeDetector, "smoke family -> SmokeDetector");
            Check(DeviceKindResolver.TryResolve("Speaker Strobe", "20cd", out DeviceKind k2)
                  && k2 == DeviceKind.NotificationAppliance, "strobe family -> NotificationAppliance");
            Check(DeviceKindResolver.TryResolve("Notification Appliance", "Horn", out DeviceKind k3)
                  && k3 == DeviceKind.NotificationAppliance, "notification/horn -> NotificationAppliance");
            Check(!DeviceKindResolver.TryResolve("Sprinkler - Pendent - Hosted", "3/4\" Pendent", out _),
                "sprinkler names are NOT classifiable (never counted by the fire-alarm policy)");
        }

        private static void TestKindResolverRejectsUnknownNames()
        {
            Console.WriteLine("Test: kind resolver -> unknown names are refused, never guessed");
            Check(!DeviceKindResolver.TryResolve("Hand Stop Button", "Momentary", out _),
                "manual call point refused (not own-kind => never skipped/deleted by any kind)");
            Check(!DeviceKindResolver.TryResolve(null, "", out _), "null/empty refused");
            // "Detector" alone must NOT classify (it appears on duct detectors, CO detectors, and the
            // sprinkler-side flow detector too): only the smoke-technical keywords resolve to SmokeDetector.
            Check(!DeviceKindResolver.TryResolve("Duct Detector", "Airflow", out _),
                "generic 'Detector' keyword refused");
        }
    }
}
