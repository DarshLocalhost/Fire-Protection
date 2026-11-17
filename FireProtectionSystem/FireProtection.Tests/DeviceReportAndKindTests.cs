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
            TestMapperCoverageEvaluationSuccess();
            TestMapperCoverageDeficientTriggersReviewRequired();
            TestMapperFormulaBasedCoverageClampingAndShoelace();
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

        private static void TestMapperCoverageEvaluationSuccess()
        {
            Console.WriteLine("Test: mapper -> 100% coverage reports Success and populates CoveragePercent");
            var room = new PlacementRoomResult { RoomId = "R1", RoomName = "Room 1" };
            room.Placed.Add(new PlacedSprinklerEntry { RevitElementId = "1", RoomId = "R1", IsSpatiallyValid = true, X = 5, Y = 5, Z = 9 });
            var result = ResultWith(room);
            result.CalculatedSprinklerCount = 1;
            result.PlacedSprinklerCount = 1;
            result.PlacedAndValidCount = 1;

            var calc = new BruteForceCalculationResult { IsProvisional = false, AppliedRulesSummary = "approved" };
            calc.Rooms.Add(new RoomCalculationResult
            {
                RoomId = "R1",
                RoomName = "Room 1",
                AppliedMaxSpacingFt = 12.75,
                Polygon = new List<double[]>
                {
                    new double[] { 0, 0 },
                    new double[] { 10, 0 },
                    new double[] { 10, 10 },
                    new double[] { 0, 10 }
                }
            });

            PlacementRunReport report = result.ToPlacementRunReport(calc, "Fam", "Type");
            Check(report.RoomReports.Count == 1, "has 1 room report");
            var r = report.RoomReports[0];
            Check(r.CoveragePercent.HasValue && r.CoveragePercent.Value >= 95.0, "coverage is >= 95% (got " + r.CoveragePercent + ")");
            Check(r.Status == "Success", "status is Success (got " + r.Status + ")");
        }

        private static void TestMapperCoverageDeficientTriggersReviewRequired()
        {
            Console.WriteLine("Test: mapper -> deficient coverage (< 95%) triggers ReviewRequired");
            var room = new PlacementRoomResult { RoomId = "R1", RoomName = "Room 1" };
            // Sprinkler placed in far bottom-left corner (0, 0) with a 5.3 ft throw in a 30x30 room
            room.Placed.Add(new PlacedSprinklerEntry { RevitElementId = "1", RoomId = "R1", IsSpatiallyValid = true, X = 0, Y = 0, Z = 9 });
            var result = ResultWith(room);
            result.CalculatedSprinklerCount = 1;
            result.PlacedSprinklerCount = 1;
            result.PlacedAndValidCount = 1;

            var calc = new BruteForceCalculationResult { IsProvisional = false, AppliedRulesSummary = "approved" };
            calc.Rooms.Add(new RoomCalculationResult
            {
                RoomId = "R1",
                RoomName = "Room 1",
                RoomAreaSqFt = 900.0,
                MaxCoverageAreaSqFt = 25.0, // 1 head * 25 sq ft / 900 sq ft = 2.77% (< 95%)
                AppliedMaxSpacingFt = 5.0,
                Polygon = new List<double[]>
                {
                    new double[] { 0, 0 },
                    new double[] { 30, 0 },
                    new double[] { 30, 30 },
                    new double[] { 0, 30 }
                }
            });

            PlacementRunReport report = result.ToPlacementRunReport(calc, "Fam", "Type");
            Check(report.RoomReports.Count == 1, "has 1 room report");
            var r = report.RoomReports[0];
            Check(r.CoveragePercent.HasValue && r.CoveragePercent.Value < 95.0, "coverage is < 95% (got " + r.CoveragePercent + ")");
            Check(r.Status == "ReviewRequired", "deficient coverage triggers ReviewRequired (got " + r.Status + ")");
            Check(r.Message.Contains("Coverage incomplete"), "message explains coverage deficiency (got " + r.Message + ")");
            Check(report.ReviewRequiredReasons.Count > 0, "review reasons includes coverage issue");
        }

        private static void TestMapperFormulaBasedCoverageClampingAndShoelace()
        {
            Console.WriteLine("Test: mapper -> formula-based coverage clamps at 100% and uses Shoelace when RoomAreaSqFt is null");
            // Case 1: 2 heads * 225 sq ft in a 500 sq ft room = 450 / 500 = 90.0%
            var room1 = new PlacementRoomResult { RoomId = "R1", RoomName = "Room 1" };
            room1.Placed.Add(Placed("1", true));
            room1.Placed.Add(Placed("2", true));
            var result1 = ResultWith(room1);
            result1.CalculatedSprinklerCount = 2;
            result1.PlacedSprinklerCount = 2;
            result1.PlacedAndValidCount = 2;

            var calc1 = new BruteForceCalculationResult { IsProvisional = false };
            calc1.Rooms.Add(new RoomCalculationResult
            {
                RoomId = "R1",
                RoomAreaSqFt = 500.0,
                MaxCoverageAreaSqFt = 225.0
            });
            var report1 = result1.ToPlacementRunReport(calc1, "Fam", "Type");
            var rr1 = report1.RoomReports[0];
            Check(rr1.CoveragePercent.HasValue && Math.Abs(rr1.CoveragePercent.Value - 90.0) < 1e-6,
                "coverage is exactly 90.0% for 2 heads (450 sq ft) in 500 sq ft (got " + rr1.CoveragePercent + "%)");

            // Case 2: 3 heads * 225 sq ft in 500 sq ft room = 675 / 500 = 135% -> clamped to 100.0%
            room1.Placed.Add(Placed("3", true));
            result1.CalculatedSprinklerCount = 3;
            result1.PlacedSprinklerCount = 3;
            result1.PlacedAndValidCount = 3;
            var report2 = result1.ToPlacementRunReport(calc1, "Fam", "Type");
            var rr2 = report2.RoomReports[0];
            Check(rr2.CoveragePercent.HasValue && Math.Abs(rr2.CoveragePercent.Value - 100.0) < 1e-6,
                "coverage is clamped to 100.0% when exceeding room area (got " + rr2.CoveragePercent + "%)");

            // Case 3: Shoelace fallback when RoomAreaSqFt is not set (polygon: 20x20 = 400 sq ft, 1 head * 225 = 56.25%)
            var room3 = new PlacementRoomResult { RoomId = "R3", RoomName = "Room 3" };
            room3.Placed.Add(Placed("10", true));
            var result3 = ResultWith(room3);
            result3.CalculatedSprinklerCount = 1;
            result3.PlacedSprinklerCount = 1;
            result3.PlacedAndValidCount = 1;

            var calc3 = new BruteForceCalculationResult { IsProvisional = false };
            calc3.Rooms.Add(new RoomCalculationResult
            {
                RoomId = "R3",
                RoomAreaSqFt = null,
                MaxCoverageAreaSqFt = 225.0,
                Polygon = new List<double[]>
                {
                    new double[] { 0, 0 },
                    new double[] { 20, 0 },
                    new double[] { 20, 20 },
                    new double[] { 0, 20 }
                }
            });
            var report3 = result3.ToPlacementRunReport(calc3, "Fam", "Type");
            var rr3 = report3.RoomReports[0];
            Check(rr3.CoveragePercent.HasValue && Math.Abs(rr3.CoveragePercent.Value - 56.25) < 1e-6,
                "Shoelace area fallback gives 56.25% for 1 head in 20x20=400 sq ft room (got " + rr3.CoveragePercent + "%)");
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
