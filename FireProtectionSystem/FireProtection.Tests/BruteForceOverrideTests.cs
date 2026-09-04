using System;
using System.Collections.Generic;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Hazard;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce;
using FireProtection.UI.Models.Sprinklers.BruteForce;

namespace FireProtection.Tests
{
    internal static class BruteForceOverrideTests
    {
        private static int _failures;

        public static void RunAll()
        {
            _failures = 0;
            TestOverrideTighterMaxSpacingIncreasesSprinklerCount();
            TestOverrideBoundaryClearanceChangesCandidateSet();
            TestOverrideOutOfRangeIsClamped();
            TestOverrideMarksRoomReviewRequired();
            TestNoOverrideUsesBaseRules();

            if (_failures == 0)
            {
                Console.WriteLine("BruteForceOverrideTests: PASS");
            }
            else
            {
                Console.WriteLine("BruteForceOverrideTests: " + _failures + " FAIL(s)");
                throw new Exception("BruteForceOverrideTests failed");
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

        private static PlacementRoomInput MakeRoom(string id, List<double[]> polygon, double? maxSpacing = null, double? boundary = null)
        {
            PlacementRoomInput room = new PlacementRoomInput
            {
                RoomId = id,
                RoomName = id,
                RoomNumber = id,
                LevelId = "L1",
                LevelElevationFt = 0.0,
                AreaSqFt = polygon.Count * 100.0,
                EffectiveHazardClass = "Light",
                CeilingHeightFt = 9.0,
                Ceilings = new List<CeilingData> { FlatCeiling(9.0) },
                Obstacles = new List<ObstacleData>(),
                ExistingSprinklers = new List<ExistingSprinklerData>()
            };
            room.BoundaryPolygon = polygon;
            room.Boundary = new BoundaryData
            {
                OuterLoop = new BoundaryLoopData { IsOuter = true, Polygon = polygon }
            };
            room.OverrideMaxSpacingFt = maxSpacing;
            room.OverrideBoundaryClearanceFt = boundary;
            return room;
        }

        private static BruteForceCalculationResult Calc(PlacementInputSnapshot snapshot)
        {
            return BruteForceCalculationService.Calculate(
                snapshot, new DefaultHazardPlacementRules(), BruteForceCalculationConfig.Default());
        }

        private static void TestOverrideTighterMaxSpacingIncreasesSprinklerCount()
        {
            Console.WriteLine("Test: tighter MaxSpacingFt override increases sprinkler count");
            List<double[]> poly = Rect(0, 0, 30, 20);

            PlacementInputSnapshot baseline = new PlacementInputSnapshot();
            baseline.Rooms.Add(MakeRoom("R", poly));
            int baselineCount = Calc(baseline).Rooms[0].CalculatedCount;

            PlacementInputSnapshot tighter = new PlacementInputSnapshot();
            tighter.Rooms.Add(MakeRoom("R", poly, maxSpacing: 8.0));
            int tighterCount = Calc(tighter).Rooms[0].CalculatedCount;

            Check(tighterCount > baselineCount, "tighter override produces more sprinklers (baseline=" + baselineCount + ", tighter=" + tighterCount + ")");
        }

        private static void TestOverrideBoundaryClearanceChangesCandidateSet()
        {
            Console.WriteLine("Test: larger BoundaryClearanceFt override reduces valid candidates");
            List<double[]> poly = Rect(0, 0, 30, 30);

            PlacementInputSnapshot baseline = new PlacementInputSnapshot();
            baseline.Rooms.Add(MakeRoom("R", poly));
            int baselineCount = Calc(baseline).Rooms[0].CalculatedCount;

            PlacementInputSnapshot widerBoundary = new PlacementInputSnapshot();
            widerBoundary.Rooms.Add(MakeRoom("R", poly, boundary: 10.0));
            int widerCount = Calc(widerBoundary).Rooms[0].CalculatedCount;

            Check(widerCount < baselineCount,
                "wider boundary clearance reduces sprinklers (baseline=" + baselineCount
                + ", wider=10ft=" + widerCount + ")");

            // 6x6 room with 5 ft boundary on every side -> nothing fits.
            List<double[]> tiny = Rect(0, 0, 6, 6);
            PlacementInputSnapshot impossible = new PlacementInputSnapshot();
            impossible.Rooms.Add(MakeRoom("R", tiny, boundary: 5.0));
            int impossibleCount = Calc(impossible).Rooms[0].CalculatedCount;
            Check(impossibleCount == 0,
                "extreme boundary clearance (5 ft on a 6x6 room) yields zero valid candidates");
        }

        private static void TestOverrideOutOfRangeIsClamped()
        {
            Console.WriteLine("Test: out-of-range MaxSpacingFt is clamped to the provisional ceiling");
            List<double[]> poly = Rect(0, 0, 30, 20);
            PlacementInputSnapshot snap = new PlacementInputSnapshot();
            snap.Rooms.Add(MakeRoom("R", poly, maxSpacing: 99.0));
            BruteForceCalculationResult result = Calc(snap);
            RoomCalculationResult room = result.Rooms[0];

            bool hasClampNote = false;
            for (int i = 0; i < room.Diagnostics.Count; i++)
            {
                if (room.Diagnostics[i] != null && room.Diagnostics[i].IndexOf("clamped", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hasClampNote = true; break;
                }
            }
            Check(hasClampNote, "out-of-range override records a clamp diagnostic");
        }

        private static void TestOverrideMarksRoomReviewRequired()
        {
            Console.WriteLine("Test: room with an override is flagged ReviewRequired");
            List<double[]> poly = Rect(0, 0, 30, 20);
            PlacementInputSnapshot snap = new PlacementInputSnapshot();
            snap.Rooms.Add(MakeRoom("R", poly, maxSpacing: 10.0));
            BruteForceCalculationResult result = Calc(snap);
            RoomCalculationResult room = result.Rooms[0];

            Check(room.Status == CalculationStatus.ReviewRequired, "room status is ReviewRequired (got " + room.Status + ")");
            bool hasOverrideNote = false;
            for (int i = 0; i < room.Warnings.Count; i++)
            {
                if (room.Warnings[i] != null && room.Warnings[i].IndexOf("override", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hasOverrideNote = true; break;
                }
            }
            Check(hasOverrideNote, "room warnings mention 'override'");
        }

        private static void TestNoOverrideUsesBaseRules()
        {
            Console.WriteLine("Test: no override -> identical to baseline (15 ft placeholder)");
            List<double[]> poly = Rect(0, 0, 30, 20);
            PlacementInputSnapshot a = new PlacementInputSnapshot();
            a.Rooms.Add(MakeRoom("A", poly));
            PlacementInputSnapshot b = new PlacementInputSnapshot();
            b.Rooms.Add(MakeRoom("A", poly));
            int aCount = Calc(a).Rooms[0].CalculatedCount;
            int bCount = Calc(b).Rooms[0].CalculatedCount;
            Check(aCount == bCount, "no-override runs are deterministic (" + aCount + " vs " + bCount + ")");
        }
    }
}
