using System;
using System.Collections.Generic;
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
            TestBeamDepthAnalysis();
            TestSlopedCeilingPeakRow();
            TestSmokeDetectorRules();
            TestNotificationApplianceRules();
            TestAudibleCoverageEngine();

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

        private static CeilingData SlopedCeiling(double bottomElevationFt, double topElevationFt)
        {
            return new CeilingData
            {
                SlopeType = "SLOPED",
                BottomElevationFt = bottomElevationFt,
                TopElevationFt = topElevationFt,
                Source = new SourceReferenceData()
            };
        }

        private static SmokeDetectorRoomInput MakeRoom(
            string id,
            List<double[]> polygon,
            double areaSqFt = 100.0,
            string mount = "Ceiling",
            string ceilingSlope = "FLAT",
            List<ObstacleData> obstacles = null,
            List<CeilingData> ceilings = null)
        {
            SmokeDetectorRoomInput room = new SmokeDetectorRoomInput
            {
                RoomId = id,
                RoomName = id,
                RoomNumber = id,
                LevelId = "L1",
                LevelElevationFt = 0.0,
                AreaSqFt = areaSqFt,
                CeilingHeightFt = 9.0,
                Mount = mount,
                CeilingSlope = ceilingSlope,
                SelectedPlacementBehavior = DevicePlacementBehavior.CeilingOverhead,
                BoundaryPolygon = polygon,
                Obstacles = obstacles ?? new List<ObstacleData>(),
                Ceilings = ceilings ?? new List<CeilingData> { FlatCeiling(9.0) }
            };
            room.Boundary = new BoundaryData
            {
                OuterLoop = new BoundaryLoopData { IsOuter = true, Polygon = polygon }
            };
            return room;
        }

        private static SmokeDetectorCalculationResult Calc(SmokeDetectorPlacementInputSnapshot snapshot)
        {
            return SmokeDetectorCalculationService.Calculate(
                snapshot, new Nfpa72SmokeDetectorRules(), BruteForceCalculationConfig.Default());
        }

        private static SmokeDetectorPlacementInputSnapshot BuildSnapshot(SmokeDetectorRoomInput room)
        {
            var snapshot = new SmokeDetectorPlacementInputSnapshot();
            snapshot.Rooms.Add(room);
            return snapshot;
        }

        // -----------------------------------------------------------------
        // Test 1 — beam depth < 0.1H does not reduce spacing
        // -----------------------------------------------------------------
        private static void TestBeamDepthAnalysis()
        {
            Console.WriteLine("Test: beam depth < 0.1H keeps smooth-ceiling spacing");
            List<double[]> poly = Rect(0, 0, 30, 20);
            var beam = new ObstacleData
            {
                Category = "Structural Framing",
                BoundingBox = new BoundingBox3DData
                {
                    Min = new Point3DData { X = 5, Y = 5, Z = 8.5 },
                    Max = new Point3DData { X = 25, Y = 6, Z = 9.1 }
                },
                DimensionsFt = new Point3DData { X = 20, Y = 1, Z = 0.6 }
            };
            var room = MakeRoom("B1", poly, obstacles: new List<ObstacleData> { beam });
            var result = Calc(BuildSnapshot(room));
            var roomResult = result.Rooms[0];

            Check(roomResult.AppliedMaxSpacingFt >= 29.0,
                "beam depth < 0.1H does not reduce MaxSpacingFt (got " + roomResult.AppliedMaxSpacingFt.ToString("F2") + ")");
        }

        // -----------------------------------------------------------------
        // Test 2 — sloped ceiling adds peak-row candidates
        // -----------------------------------------------------------------
        private static void TestSlopedCeilingPeakRow()
        {
            Console.WriteLine("Test: sloped ceiling generates peak-row candidates");
            List<double[]> poly = Rect(0, 0, 30, 20);
            var room = MakeRoom("S1", poly, ceilingSlope: "SLOPED",
                ceilings: new List<CeilingData> { SlopedCeiling(8.0, 12.0) });
            var result = Calc(BuildSnapshot(room));
            var roomResult = result.Rooms[0];

            bool hasPeakRowDiagnostic = false;
            if (roomResult.Diagnostics != null)
            {
                foreach (string d in roomResult.Diagnostics)
                {
                    if (d.IndexOf("peak-row", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hasPeakRowDiagnostic = true;
                        break;
                    }
                }
            }

            Check(hasPeakRowDiagnostic,
                "sloped ceiling diagnostics mention peak-row rule");
            Check(roomResult.CalculatedCount > 0,
                "sloped ceiling room still places detectors (got " + roomResult.CalculatedCount + ")");
        }

        // -----------------------------------------------------------------
        // Test 3 — NFPA 72 smoke detector rules produce correct base values
        // -----------------------------------------------------------------
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

        // -----------------------------------------------------------------
        // Test 4 — notification appliance rules derive spacing from candela/dBA
        // -----------------------------------------------------------------
        private static void TestNotificationApplianceRules()
        {
            Console.WriteLine("Test: notification appliance rules derive spacing from candela/dBA");
            var rules = new Nfpa72NotificationApplianceRules();
            var combined = rules.GetRules("HornStrobe|candela=15|dba=87", "Ceiling", "FLAT");

            Check(Math.Abs(combined.MaxSpacingFt - 25.0) < 0.001,
                "combined 15cd/87dBA appliance uses stricter audible limit (got " + combined.MaxSpacingFt.ToString("F2") + ")");
            Check(!rules.HasApprovedRules, "notification rules are correctly marked NOT approved (provisional until AHJ sign-off)");
        }

        // -----------------------------------------------------------------
        // Test 5 — audible coverage engine flags uncovered samples
        // -----------------------------------------------------------------
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
                ambientDb: 40.0, maxSustainedDb: 70.0,
                isSleepingArea: false, config);

            Check(result.SamplesChecked > 0, "audible engine sampled the room (" + result.SamplesChecked + " points)");
            Check(result.CoveragePercentage >= 0.0 && result.CoveragePercentage <= 100.0,
                "audible coverage percentage is bounded (got " + result.CoveragePercentage.ToString("F1") + "%)");
        }
    }
}
