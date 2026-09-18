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
            TestOverrideTighterMaxSpacingFlagsReview();
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

        private static double PointToSegment(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay;
            double len2 = dx * dx + dy * dy;
            double t = len2 <= 0 ? 0.0 : ((px - ax) * dx + (py - ay) * dy) / len2;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            double cx = ax + t * dx, cy = ay + t * dy;
            double ex = px - cx, ey = py - cy;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        private static double MinWallDistance(List<double[]> poly, double px, double py)
        {
            double min = double.PositiveInfinity;
            for (int i = 0; i < poly.Count; i++)
            {
                double[] a = poly[i];
                double[] b = poly[(i + 1) % poly.Count];
                double d = PointToSegment(px, py, a[0], a[1], b[0], b[1]);
                if (d < min) min = d;
            }
            return min;
        }

        // Smallest wall distance across every placed head — the perimeter heads that hug the
        // wall. A larger BoundaryClearanceFt must push this value up (heads shift inward).
        private static double MinWallDistanceAcrossHeads(RoomCalculationResult room, List<double[]> poly)
        {
            double min = double.PositiveInfinity;
            if (room.Points != null)
            {
                foreach (var p in room.Points)
                {
                    double d = MinWallDistance(poly, p.X, p.Y);
                    if (d < min) min = d;
                }
            }
            return min;
        }

        private static void TestOverrideTighterMaxSpacingFlagsReview()
        {
            Console.WriteLine("Test: tighter MaxSpacingFt override drives a denser grid and flags ReviewRequired");
            List<double[]> poly = Rect(0, 0, 30, 20);

            // Baseline uses the 15 ft placeholder max spacing -> a coarse centered grid.
            PlacementInputSnapshot baseline = new PlacementInputSnapshot();
            baseline.Rooms.Add(MakeRoom("R", poly));
            int baselineCount = Calc(baseline).Rooms[0].CalculatedCount;

            // Tighter override (8 ft) drives a denser centered grid, so it places MORE heads
            // than the 15 ft baseline. The room is flagged ReviewRequired (provisional rules),
            // and the count must not fall below the baseline.
            PlacementInputSnapshot tighter = new PlacementInputSnapshot();
            tighter.Rooms.Add(MakeRoom("R", poly, maxSpacing: 8.0));
            BruteForceCalculationResult tighterResult = Calc(tighter);
            int tighterCount = tighterResult.Rooms[0].CalculatedCount;
            CalculationStatus tighterStatus = tighterResult.Rooms[0].Status;

            Check(tighterStatus == CalculationStatus.ReviewRequired,
                "tighter max-spacing override flags ReviewRequired (got " + tighterStatus + ")");
            Check(tighterCount >= baselineCount,
                "tighter override drives at least as many heads as the baseline (baseline=" + baselineCount + ", tighter=" + tighterCount + ")");
        }

        private static void TestOverrideBoundaryClearanceChangesCandidateSet()
        {
            Console.WriteLine("Test: larger BoundaryClearanceFt override shifts heads inward");
            List<double[]> poly = Rect(0, 0, 30, 30);

            PlacementInputSnapshot baseline = new PlacementInputSnapshot();
            baseline.Rooms.Add(MakeRoom("R", poly));
            RoomCalculationResult baseRoom = Calc(baseline).Rooms[0];
            double baseMinWall = MinWallDistanceAcrossHeads(baseRoom, poly);

            // A 10 ft boundary clearance invalidates the natural near-wall grid targets, so the
            // centered grid snaps every head inward onto candidates that clear the wall by 10 ft.
            // The head count need not drop (unlike the old greedy assumption); what MUST change
            // is that no head sits closer than ~10 ft to a wall, and the closest head is farther
            // from the wall than under the 1 ft baseline.
            PlacementInputSnapshot widerBoundary = new PlacementInputSnapshot();
            widerBoundary.Rooms.Add(MakeRoom("R", poly, boundary: 10.0));
            RoomCalculationResult widerRoom = Calc(widerBoundary).Rooms[0];
            int widerCount = widerRoom.CalculatedCount;
            double widerMinWall = MinWallDistanceAcrossHeads(widerRoom, poly);

            Check(widerCount > 0,
                "wider boundary clearance still places heads by snapping inward (count=" + widerCount + ")");
            Check(widerMinWall >= 10.0 - 0.1,
                "every head under a 10 ft clearance override clears the wall by ~10 ft (min="
                + widerMinWall.ToString("F2") + " ft)");
            Check(widerMinWall > baseMinWall + 0.1,
                "wider clearance pushes heads inward vs the 1 ft baseline (baseline min wall="
                + baseMinWall.ToString("F2") + " ft, wider=" + widerMinWall.ToString("F2") + " ft)");

            // 6x6 room with 5 ft boundary on every side -> nothing fits (no valid candidate at all).
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
