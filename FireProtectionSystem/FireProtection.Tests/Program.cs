using System;
using System.Collections.Generic;
using System.Linq;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Hazard;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce;
using FireProtection.UI.Models.Sprinklers.BruteForce;

namespace FireProtection.Tests
{
    internal static class Program
    {
        private static int _failures;

        private static void Main()
        {
            RunAll();
            Console.WriteLine();
            if (_failures == 0)
            {
                Console.WriteLine("ALL TESTS PASSED");
                Environment.Exit(0);
            }
            else
            {
                Console.WriteLine(_failures + " TEST(S) FAILED");
                Environment.Exit(1);
            }
        }

        private static void RunAll()
        {
            TestRectangleRoom();
            TestLShapedRoom();
            TestConcaveRoom();
            TestSmallRoom();
            TestNarrowRoom();
            TestRoomWithOneObstacle();
            TestRoomWithMultipleObstacles();
            TestRoomWithExistingSprinkler();
            TestMultipleIndependentRooms();
            TestMissingCeiling();
            TestUnsupportedCeiling();
            TestInvalidBoundary();
            TestNoValidCandidates();
            TestDeterministicRepeatedCalculation();
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static PlacementRoomInput MakeRoom(
            string id,
            List<double[]> polygon,
            string hazard = "Light",
            double? ceilingHeightFt = 9.0,
            double levelElevationFt = 0.0,
            double areaSqFt = 100.0,
            List<CeilingData> ceilings = null,
            List<ObstacleData> obstacles = null,
            List<ExistingSprinklerData> existing = null)
        {
            PlacementRoomInput room = new PlacementRoomInput
            {
                RoomId = id,
                RoomName = id,
                RoomNumber = id,
                LevelId = "L1",
                LevelElevationFt = levelElevationFt,
                AreaSqFt = areaSqFt,
                EffectiveHazardClass = hazard,
                CeilingHeightFt = ceilingHeightFt,
                Ceilings = ceilings ?? new List<CeilingData>(),
                Obstacles = obstacles ?? new List<ObstacleData>(),
                ExistingSprinklers = existing ?? new List<ExistingSprinklerData>()
            };

            room.BoundaryPolygon = polygon;
            room.Boundary = new BoundaryData
            {
                OuterLoop = new BoundaryLoopData { IsOuter = true, Polygon = polygon }
            };

            return room;
        }

        private static PlacementInputSnapshot Snapshot(params PlacementRoomInput[] rooms)
        {
            PlacementInputSnapshot s = new PlacementInputSnapshot();
            foreach (PlacementRoomInput r in rooms) s.Rooms.Add(r);
            return s;
        }

        private static BruteForceCalculationResult Calc(PlacementInputSnapshot snapshot)
        {
            return BruteForceCalculationService.Calculate(
                snapshot, new DefaultHazardPlacementRules(), BruteForceCalculationConfig.Default());
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

        private static ObstacleData BoxObstacle(string category, double minX, double minY, double maxX, double maxY)
        {
            return new ObstacleData
            {
                Category = category,
                Name = category,
                BoundingBox = new BoundingBox3DData(
                    new Point3DData(minX, minY, 0), new Point3DData(maxX, maxY, 0)),
                Source = new SourceReferenceData()
            };
        }

        private static ExistingSprinklerData ExistingSprinkler(double x, double y)
        {
            return new ExistingSprinklerData
            {
                Location = new Point3DData(x, y, 9.0),
                RoomId = "R",
                Source = new SourceReferenceData()
            };
        }

        private static void Check(bool condition, string message)
        {
            if (condition)
            {
                Console.WriteLine("  PASS: " + message);
            }
            else
            {
                Console.WriteLine("  FAIL: " + message);
                _failures++;
            }
        }

        // ---------------------------------------------------------------
        // Tests
        // ---------------------------------------------------------------

        private static void TestRectangleRoom()
        {
            Console.WriteLine("Test: simple rectangular room");
            PlacementInputSnapshot s = Snapshot(MakeRoom("R", Rect(0, 0, 20, 10), ceilings: new List<CeilingData> { FlatCeiling(9.0) }));
            BruteForceCalculationResult r = Calc(s);
            RoomCalculationResult room = r.Rooms[0];
            Check(room.CalculatedCount > 0, "rectangular room produced sprinkler points");
            Check(room.Points.All(p => p.X >= 0 && p.X <= 20 && p.Y >= 0 && p.Y <= 10), "all points inside rectangle bounds");
            Check(room.Points.All(p => Math.Abs(p.Z - 9.0) < 1e-6), "placement Z equals flat ceiling elevation");
        }

        private static void TestLShapedRoom()
        {
            Console.WriteLine("Test: L-shaped room");
            List<double[]> l = new List<double[]>
            {
                new double[] { 0, 0 },
                new double[] { 20, 0 },
                new double[] { 20, 10 },
                new double[] { 10, 10 },
                new double[] { 10, 20 },
                new double[] { 0, 20 }
            };
            PlacementInputSnapshot s = Snapshot(MakeRoom("L", l, ceilings: new List<CeilingData> { FlatCeiling(9.0) }));
            BruteForceCalculationResult r = Calc(s);
            RoomCalculationResult room = r.Rooms[0];
            Check(room.CalculatedCount > 0, "L-shaped room produced sprinkler points");
            // The notch (x in 10..20, y in 10..20) is OUTSIDE the L.
            Check(room.Points.All(p => !(p.X > 10.0 && p.Y > 10.0)), "no point placed inside the excluded L notch");
        }

        private static void TestConcaveRoom()
        {
            Console.WriteLine("Test: concave room (inner loop / opening)");
            List<double[]> outer = Rect(0, 0, 30, 30);
            List<double[]> inner = Rect(10, 10, 20, 20); // opening
            PlacementRoomInput room = MakeRoom("C", outer, ceilings: new List<CeilingData> { FlatCeiling(9.0) });
            room.Boundary = new BoundaryData
            {
                OuterLoop = new BoundaryLoopData { IsOuter = true, Polygon = outer },
                InnerLoops = new List<BoundaryLoopData>
                {
                    new BoundaryLoopData { IsOuter = false, Polygon = inner }
                }
            };
            PlacementInputSnapshot s = Snapshot(room);
            BruteForceCalculationResult r = Calc(s);
            RoomCalculationResult res = r.Rooms[0];
            Check(res.CalculatedCount > 0, "concave room produced sprinkler points");
            Check(res.Points.All(p => !(p.X > 10 && p.X < 20 && p.Y > 10 && p.Y < 20)), "no point placed inside the inner opening");
        }

        private static void TestSmallRoom()
        {
            Console.WriteLine("Test: very small room");
            PlacementInputSnapshot s = Snapshot(MakeRoom("S", Rect(0, 0, 3, 3), ceilings: new List<CeilingData> { FlatCeiling(9.0) }, areaSqFt: 9));
            BruteForceCalculationResult r = Calc(s);
            Check(r.Rooms[0].CalculatedCount >= 1, "small room produced at least one sprinkler");
        }

        private static void TestNarrowRoom()
        {
            Console.WriteLine("Test: narrow room");
            // 30 ft long, 3 ft wide: enough to satisfy the 1 ft boundary clearance (point at y=1/2).
            PlacementInputSnapshot s = Snapshot(MakeRoom("N", Rect(0, 0, 30, 3), ceilings: new List<CeilingData> { FlatCeiling(9.0) }, areaSqFt: 90));
            BruteForceCalculationResult r = Calc(s);
            RoomCalculationResult room = r.Rooms[0];
            Check(room.CalculatedCount > 0, "narrow room produced sprinkler points");
            Check(room.Points.All(p => p.Y >= 0 && p.Y <= 3), "narrow-room points respect Y bounds");

            // A genuinely too-narrow room (1 ft tall) cannot satisfy a 1 ft boundary clearance and
            // must be reported as having no valid candidates (handled, not crashed).
            PlacementInputSnapshot tooNarrow = Snapshot(MakeRoom("NT", Rect(0, 0, 30, 1), ceilings: new List<CeilingData> { FlatCeiling(9.0) }, areaSqFt: 30));
            RoomCalculationResult tn = Calc(tooNarrow).Rooms[0];
            Check(tn.CalculatedCount == 0, "sub-clearance narrow room reports zero valid candidates");
        }

        private static void TestRoomWithOneObstacle()
        {
            Console.WriteLine("Test: room with one obstacle");
            List<ObstacleData> obs = new List<ObstacleData> { BoxObstacle("Column", 8, 3, 12, 7) };
            PlacementInputSnapshot s = Snapshot(MakeRoom("O1", Rect(0, 0, 20, 10),
                ceilings: new List<CeilingData> { FlatCeiling(9.0) }, obstacles: obs));
            BruteForceCalculationResult r = Calc(s);
            RoomCalculationResult room = r.Rooms[0];
            Check(room.CalculatedCount > 0, "room with obstacle still produced sprinklers");
            Check(room.Points.All(p => !(p.X >= 8 && p.X <= 12 && p.Y >= 3 && p.Y <= 7)),
                "no sprinkler placed inside obstacle box (clearance respected)");
        }

        private static void TestRoomWithMultipleObstacles()
        {
            Console.WriteLine("Test: room with multiple obstacles");
            List<ObstacleData> obs = new List<ObstacleData>
            {
                BoxObstacle("Beam", 2, 2, 5, 4),
                BoxObstacle("Duct", 14, 5, 18, 8)
            };
            PlacementInputSnapshot s = Snapshot(MakeRoom("O2", Rect(0, 0, 20, 10),
                ceilings: new List<CeilingData> { FlatCeiling(9.0) }, obstacles: obs));
            BruteForceCalculationResult r = Calc(s);
            RoomCalculationResult room = r.Rooms[0];
            Check(room.CalculatedCount > 0, "room with multiple obstacles produced sprinklers");
            Check(room.Points.All(p => !(p.X >= 2 && p.X <= 5 && p.Y >= 2 && p.Y <= 4)), "no point in beam box");
            Check(room.Points.All(p => !(p.X >= 14 && p.X <= 18 && p.Y >= 5 && p.Y <= 8)), "no point in duct box");
        }

        private static void TestRoomWithExistingSprinkler()
        {
            Console.WriteLine("Test: room with existing sprinkler");
            List<ExistingSprinklerData> ex = new List<ExistingSprinklerData> { ExistingSprinkler(5, 5) };
            PlacementInputSnapshot s = Snapshot(MakeRoom("E", Rect(0, 0, 20, 10),
                ceilings: new List<CeilingData> { FlatCeiling(9.0) }, existing: ex));
            BruteForceCalculationResult r = Calc(s);
            RoomCalculationResult room = r.Rooms[0];
            Check(room.Points.All(p => Math.Sqrt((p.X - 5) * (p.X - 5) + (p.Y - 5) * (p.Y - 5)) > 7.0),
                "no new sprinkler within separation of existing sprinkler");
        }

        private static void TestMultipleIndependentRooms()
        {
            Console.WriteLine("Test: multiple independent rooms");
            PlacementInputSnapshot s = Snapshot(
                MakeRoom("RA", Rect(0, 0, 10, 10), ceilings: new List<CeilingData> { FlatCeiling(9.0) }),
                MakeRoom("RB", Rect(0, 0, 10, 10), ceilings: new List<CeilingData> { FlatCeiling(9.0) }));
            BruteForceCalculationResult r = Calc(s);
            Check(r.Rooms.Count == 2, "both rooms present in result");
            Check(r.Rooms[0].CalculatedCount > 0 && r.Rooms[1].CalculatedCount > 0, "both rooms produced sprinklers");
            Check(r.TotalCalculatedSprinklers == r.Rooms[0].CalculatedCount + r.Rooms[1].CalculatedCount, "total count sums rooms");
        }

        private static void TestMissingCeiling()
        {
            Console.WriteLine("Test: missing ceiling");
            // No ceilings collection and no ceiling height -> cannot determine a real plane.
            PlacementRoomInput room = MakeRoom("MC", Rect(0, 0, 10, 10), ceilingHeightFt: null);
            room.Ceilings = new List<CeilingData>();
            PlacementInputSnapshot s = Snapshot(room);
            BruteForceCalculationResult r = Calc(s);
            RoomCalculationResult res = r.Rooms[0];
            Check(res.Status == CalculationStatus.MissingCeiling || res.Status == CalculationStatus.ReviewRequired,
                "missing ceiling flagged (MissingCeiling/ReviewRequired)");
            Check(res.CalculatedCount >= 0, "missing ceiling did not crash calculation");
        }

        private static void TestUnsupportedCeiling()
        {
            Console.WriteLine("Test: unsupported (sloped) ceiling");
            CeilingData sloped = new CeilingData { SlopeType = "SLOPED", Source = new SourceReferenceData() };
            PlacementRoomInput room = MakeRoom("UC", Rect(0, 0, 10, 10), ceilings: new List<CeilingData> { sloped });
            PlacementInputSnapshot s = Snapshot(room);
            BruteForceCalculationResult r = Calc(s);
            RoomCalculationResult res = r.Rooms[0];
            Check(res.Status == CalculationStatus.UnsupportedCeiling || res.Status == CalculationStatus.ReviewRequired,
                "sloped ceiling flagged (UnsupportedCeiling/ReviewRequired)");
        }

        private static void TestInvalidBoundary()
        {
            Console.WriteLine("Test: invalid (degenerate) boundary");
            PlacementInputSnapshot s = Snapshot(MakeRoom("IB", new List<double[]> { new double[] { 0, 0 }, new double[] { 1, 1 } }));
            BruteForceCalculationResult r = Calc(s);
            RoomCalculationResult res = r.Rooms[0];
            Check(res.Status == CalculationStatus.InvalidRoomGeometry, "degenerate boundary -> InvalidRoomGeometry");
            Check(res.CalculatedCount == 0, "degenerate boundary produced zero sprinklers");
        }

        private static void TestNoValidCandidates()
        {
            Console.WriteLine("Test: no valid candidates");
            // 1x1 room fully covered by an obstacle box.
            List<ObstacleData> obs = new List<ObstacleData> { BoxObstacle("Column", -1, -1, 2, 2) };
            PlacementInputSnapshot s = Snapshot(MakeRoom("NC", Rect(0, 0, 1, 1),
                ceilings: new List<CeilingData> { FlatCeiling(9.0) }, obstacles: obs, areaSqFt: 1));
            BruteForceCalculationResult r = Calc(s);
            RoomCalculationResult res = r.Rooms[0];
            Check(res.CalculatedCount == 0, "no valid candidates -> zero sprinklers");
            Check(res.Status == CalculationStatus.NoValidCandidates || res.Status == CalculationStatus.ReviewRequired,
                "no-valid-candidates status reported");
        }

        private static void TestDeterministicRepeatedCalculation()
        {
            Console.WriteLine("Test: deterministic repeated calculation");
            PlacementInputSnapshot s = Snapshot(MakeRoom("D", Rect(0, 0, 20, 10), ceilings: new List<CeilingData> { FlatCeiling(9.0) }));
            BruteForceCalculationResult a = Calc(s);
            BruteForceCalculationResult b = Calc(s);
            Check(a.TotalCalculatedSprinklers == b.TotalCalculatedSprinklers, "identical total count on repeat");

            bool same =
                a.Rooms.Count == b.Rooms.Count &&
                a.Rooms[0].Points.Count == b.Rooms[0].Points.Count &&
                a.Rooms[0].Points
                    .Zip(b.Rooms[0].Points, (p, q) => Math.Abs(p.X - q.X) < 1e-9 && Math.Abs(p.Y - q.Y) < 1e-9 && Math.Abs(p.Z - q.Z) < 1e-9)
                    .All(eq => eq);
            Check(same, "identical point coordinates on repeat");
        }
    }
}
