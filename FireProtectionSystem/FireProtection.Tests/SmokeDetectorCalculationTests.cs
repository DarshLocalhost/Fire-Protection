using System;
using System.Collections.Generic;
using System.Linq;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Placement.SmokeDetectors.Final;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.Backend.Services.Placement.NotificationAppliances.Final.BruteForce;
using FireProtection.Backend.Services.Placement.SmokeDetectors.Final.BruteForce;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce;

namespace FireProtection.Tests
{
    internal static class SmokeDetectorCalculationTests
    {
        private static int _failures;

        public static void RunAll()
        {
            _failures = 0;
            TestSmokeDetectorNfpa72Basics();
            TestSmokeDetectorRules();
            TestNotificationApplianceRules();
            TestAudibleCoverageEngine();
            TestObstacleConflictFlagging();

            if (_failures == 0)
            {
                Console.WriteLine("SmokeDetectorCalculationTests: PASS");
            }
            else
            {
                Console.WriteLine("SmokeDetectorCalculationTests: " + _failures + " FAIL(s)");
                throw new Exception("SmokeDetectorCalculationTests failed");
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

        private static List<double[]> Rect(double x0, double y0, double x1, double y1)
        {
            return new List<double[]>
            {
                new double[] { x0, y0 },
                new double[] { x1, y0 },
                new double[] { x1, y1 },
                new double[] { x0, y1 }
            };
        }

        private static CeilingData FlatCeiling(double bottomElevationFt)
        {
            return new CeilingData
            {
                SlopeType = "FLAT",
                BottomElevationFt = bottomElevationFt,
                Source = new SourceReferenceData()
            };
        }

        private static SmokeDetectorRoomInput MakeRoom(
            string roomId,
            List<double[]> polygon,
            string detectorType = "Photoelectric",
            string mount = "Ceiling",
            string ceilingSlope = "FLAT",
            List<CeilingData> ceilings = null,
            List<ObstacleData> obstacles = null)
        {
            SmokeDetectorRoomInput room = new SmokeDetectorRoomInput
            {
                RoomId = roomId,
                RoomName = roomId,
                RoomNumber = roomId,
                LevelId = "L1",
                LevelName = "Level 1",
                LevelElevationFt = 0.0,
                AreaSqFt = 600.0,
                CeilingHeightFt = 9.0,
                DetectorType = detectorType,
                Mount = mount,
                CeilingSlope = ceilingSlope,
                SelectedFamilyName = "Smoke Detector",
                SelectedTypeName = "Standard",
                BoundaryPolygon = polygon,
                Ceilings = ceilings ?? new List<CeilingData> { FlatCeiling(9.0) },
                Obstacles = obstacles ?? new List<ObstacleData>()
            };
            room.Boundary = new BoundaryData
            {
                OuterLoop = new BoundaryLoopData { IsOuter = true, Polygon = polygon }
            };
            return room;
        }

        private static SmokeDetectorCalculationResult Calc(SmokeDetectorPlacementInputSnapshot snapshot)
        {
            return Nfpa72SmokeDetectorEngine.Calculate(
                snapshot, new Nfpa72SmokeDetectorRules(), BruteForceCalculationConfig.Default());
        }

        private static SmokeDetectorPlacementInputSnapshot BuildSnapshot(SmokeDetectorRoomInput room)
        {
            var snapshot = new SmokeDetectorPlacementInputSnapshot();
            snapshot.Rooms.Add(room);
            return snapshot;
        }

        private static void TestSmokeDetectorNfpa72Basics()
        {
            Console.WriteLine("Test: NFPA 72 standard spacing and wall distance");
            List<double[]> poly = Rect(0, 0, 60, 40);
            var room = MakeRoom("R1", poly);
            var result = Calc(BuildSnapshot(room));
            var roomResult = result.Rooms[0];

            Check(roomResult.CalculatedCount > 0, "detectors placed in room (got " + roomResult.CalculatedCount + ")");
            Check(roomResult.AppliedMaxSpacingFt == 30.0, "applied max spacing is 30 ft (got " + roomResult.AppliedMaxSpacingFt + ")");
            Check(roomResult.Points.All(p => p.Mount == "Ceiling"), "all points ceiling mount");
        }

        private static void TestObstacleConflictFlagging()
        {
            Console.WriteLine("Test: obstacle clash sets HasObstacleConflict without discarding points");
            List<double[]> poly = Rect(0, 0, 30, 20);
            var beam = new ObstacleData
            {
                Category = "Structural Framing",
                BoundingBox = new BoundingBox3DData
                {
                    Min = new Point3DData { X = 10, Y = 5, Z = 8.0 },
                    Max = new Point3DData { X = 20, Y = 15, Z = 10.0 }
                }
            };
            var room = MakeRoom("ObsRoom", poly, obstacles: new List<ObstacleData> { beam });
            var result = Calc(BuildSnapshot(room));
            var roomResult = result.Rooms[0];

            Check(roomResult.Points.Count > 0, "candidates generated despite obstacle (got " + roomResult.Points.Count + ")");
            bool hasClash = roomResult.Points.Any(p => p.HasObstacleConflict);
            Check(hasClash, "at least one candidate point flagged with HasObstacleConflict=true");
        }

        private static void TestSmokeDetectorRules()
        {
            Console.WriteLine("Test: NFPA 72 smoke detector rules produce correct base values");
            var rules = new Nfpa72SmokeDetectorRules();
            var smooth = rules.GetRules("Photoelectric", "Ceiling", "FLAT");

            Check(Math.Abs(smooth.MaxSpacingFt - 30.0) < 0.001,
                "smooth ceiling photoelectric MaxSpacingFt=30 ft (got " + smooth.MaxSpacingFt.ToString("F2") + ")");
            Check(Math.Abs(smooth.MinSpacingFt - 10.0) < 0.001,
                "smooth ceiling MinSpacingFt=10 ft (got " + smooth.MinSpacingFt.ToString("F2") + ")");
            Check(Math.Abs(smooth.MaxCoverageAreaSqFt - 900.0) < 0.001,
                "smooth ceiling MaxCoverageAreaSqFt=900 sq ft (got " + smooth.MaxCoverageAreaSqFt.ToString("F2") + ")");
            Check(!rules.HasApprovedRules, "smoke rules are correctly marked NOT approved (provisional until AHJ sign-off)");
        }

        private static void TestNotificationApplianceRules()
        {
            Console.WriteLine("Test: notification appliance rules derive spacing from candela/dBA");
            var rules = new Nfpa72NotificationApplianceRules();
            var combined = rules.GetRules("HornStrobe|candela=15|dba=87", "Ceiling", "FLAT");

            Check(Math.Abs(combined.MaxSpacingFt - 25.0) < 0.001,
                "combined 15cd/87dBA appliance uses stricter audible limit (got " + combined.MaxSpacingFt.ToString("F2") + ")");
            Check(!rules.HasApprovedRules, "notification rules are correctly marked NOT approved (provisional until AHJ sign-off)");
        }

        private static void TestAudibleCoverageEngine()
        {
            Console.WriteLine("Test: audible coverage engine flags uncovered samples");
            var geometry = new RoomGeometry(Rect(0, 0, 30, 20), new List<List<double[]>>());
            var appliances = new List<CalculatedSmokeDetectorPoint>
            {
                new CalculatedSmokeDetectorPoint { X = 15, Y = 10, Z = 9.0 }
            };
            var config = BruteForceCalculationConfig.Default();

            var result = AudibleCoverageEngine.Evaluate(
                appliances, geometry, new List<ObstacleBox>(),
                sourceDbaAt10Ft: 90.0, ambientDb: 40.0, maxSustainedDb: 70.0,
                isSleepingArea: false, config: config);

            Check(result.SamplesChecked > 0, "audible engine sampled the room (" + result.SamplesChecked + " points)");
            Check(result.CoveragePercentage >= 0.0 && result.CoveragePercentage <= 100.0,
                "audible coverage percentage is bounded (got " + result.CoveragePercentage.ToString("F1") + "%)");
        }
    }
}
