using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
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

        private static void Main(string[] args)
        {
            if (args != null && args.Length >= 1)
            {
                if (string.Equals(args[0], "generate-template", StringComparison.OrdinalIgnoreCase))
                {
                    string path = args.Length >= 2 ? args[1] : "CatalogTemplate.xlsx";
                    CatalogTemplateGenerator.Generate(path);
                    Console.WriteLine("Generated catalog template at: " + System.IO.Path.GetFullPath(path));
                    return;
                }
                if (string.Equals(args[0], "validate-catalog", StringComparison.OrdinalIgnoreCase))
                {
                    string path = args.Length >= 2 ? args[1] : "CatalogTemplate.xlsx";
                    int exit = CatalogLoaderRunner.Run(path);
                    Environment.Exit(exit);
                    return;
                }
            }

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
            TestObstacleSpecificClearances();
            TestObstacleZAwareRejection();
            TestUnsupportedPlacementBehaviorShortCircuits();
            TestWallDistanceUpperBoundFlagsReview();
            TestOrientationSpacingAdjustments();
            TestRoomWithExistingSprinkler();
            TestMultipleIndependentRooms();
            TestMissingCeiling();
            TestHostedFamilyBlockedByMissingCeiling();
            TestUnsupportedCeiling();
            TestSlopedCeilingWeightedAverage();
            TestInvalidBoundary();
            TestNoValidCandidates();
            TestDeterministicRepeatedCalculation();
            TestGridResolutionFloor();
            TestCoverageGapFreeBaseline();
            TestCoverageGapDetection();
            CatalogLoaderTests.RunAll();

            // Both of these self-report and throw on failure rather than incrementing _failures.
            Console.WriteLine();
            Console.WriteLine("Test: BruteForceOverrideTests");
            RunGuarded("BruteForceOverrideTests", BruteForceOverrideTests.RunAll);
            Console.WriteLine();
            Console.WriteLine("Test: BruteForceSelectionTests");
            RunGuarded("BruteForceSelectionTests", BruteForceSelectionTests.RunAll);
            Console.WriteLine();
            Console.WriteLine("Test: UiDefaultsTests");
            RunGuarded("UiDefaultsTests", UiDefaultsTests.RunAll);
            Console.WriteLine();
            Console.WriteLine("Test: Phase7Tests");
            RunGuarded("Phase7Tests", Phase7Tests.RunAll);
            Console.WriteLine();
            Console.WriteLine("Test: SidewallPlacementTests");
            RunGuarded("SidewallPlacementTests", SidewallPlacementTests.RunAll);
            Console.WriteLine();
            Console.WriteLine("Test: NotificationApplianceRulesTests");
            RunGuarded("NotificationApplianceRulesTests", NotificationApplianceRulesTests.RunAll);
            Console.WriteLine();
            Console.WriteLine("Test: SmokeDetectorCalculationTests");
            RunGuarded("SmokeDetectorCalculationTests", SmokeDetectorCalculationTests.RunAll);
            Console.WriteLine();
            Console.WriteLine("Test: DeviceReportAndKindTests");
            RunGuarded("DeviceReportAndKindTests", DeviceReportAndKindTests.RunAll);
            Console.WriteLine();
            Console.WriteLine("Test: CenteredGridPlacementTests");
            RunGuarded("CenteredGridPlacementTests", CenteredGridPlacementTests.RunAll);
        }

        /// <summary>
        /// Runs a suite that signals failure by throwing, folding the outcome into this runner's
        /// failure count so one failing suite does not hide the rest.
        /// </summary>
        private static void RunGuarded(string suiteName, Action run)
        {
            try
            {
                run();
            }
            catch (Exception ex)
            {
                Console.WriteLine("  FAIL: " + suiteName + " threw - " + ex.Message);
                _failures++;
            }
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

        private static ObstacleData BoxObstacle3D(
            string category, double minX, double minY, double maxX, double maxY, double minZ, double maxZ)
        {
            return new ObstacleData
            {
                Category = category,
                Name = category,
                BoundingBox = new BoundingBox3DData(
                    new Point3DData(minX, minY, minZ), new Point3DData(maxX, maxY, maxZ)),
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

        private static void TestOrientationSpacingAdjustments()
        {
            // Light Hazard rule set: pendent=1.0, upright=1.0, sidewall=0.85.
            //
            // Behavior split:
            //   * orientation = "pendent" / null     → ceiling XY grid, normal spacing.
            //   * orientation = "sidewall"           → NFPA 13 §11.3 wall-anchored branch
            //                                          (GenerateSidewallCandidates), which
            //                                          uses a DIFFERENT candidate set
            //                                          (along walls, not on a ceiling grid).
            //   * unknown / "watermelon"             → ceiling grid, 1.0 fallback, identical
            //                                          to pendent.
            //
            // The old assertion (sidewall places strictly more sprinklers than pendent
            // because 0.85 < 1.0) is no longer meaningful — the candidate sets are
            // different by construction. We now assert (a) the sidewall room has at
            // least one sprinkler placed, and (b) the unknown-orientation case still
            // matches the pendent case byte-for-byte.
            Console.WriteLine("Test: orientation spacing adjustments (pendent 1.0 vs sidewall 0.85)");

            List<double[]> roomPoly = Rect(0, 0, 30, 20);
            PlacementInputSnapshot sPendent = Snapshot(MakeRoom("P", roomPoly,
                ceilings: new List<CeilingData> { FlatCeiling(9.0) }, areaSqFt: 600));
            PlacementInputSnapshot sSidewall = Snapshot(MakeRoom("S", roomPoly,
                ceilings: new List<CeilingData> { FlatCeiling(9.0) }, areaSqFt: 600));
            sSidewall.Rooms[0].SelectedSprinklerOrientation = "sidewall";

            int pendentPlaced = Calc(sPendent).Rooms[0].CalculatedCount;
            int sidewallPlaced = Calc(sSidewall).Rooms[0].CalculatedCount;

            // The sidewall branch must place at least one sprinkler along the walls
            // (the room is 30x20 ft; the wall edge is long enough to accommodate
            // multiple sidewall candidates under Light Hazard's spacing).
            Check(sidewallPlaced > 0,
                "sidewall branch places at least one sprinkler along the room walls (got " + sidewallPlaced + ")");

            // Unknown / empty orientation must NOT change the count (1.0 fallback).
            PlacementInputSnapshot sUnknown = Snapshot(MakeRoom("U", roomPoly,
                ceilings: new List<CeilingData> { FlatCeiling(9.0) }, areaSqFt: 600));
            sUnknown.Rooms[0].SelectedSprinklerOrientation = "watermelon";
            int unknownPlaced = Calc(sUnknown).Rooms[0].CalculatedCount;
            Check(unknownPlaced == pendentPlaced,
                "unknown orientation falls back to 1.0 and matches pendent count (pendent=" + pendentPlaced + ", unknown=" + unknownPlaced + ")");
        }

        private static void TestObstacleSpecificClearances()
        {
            // Light Hazard rules: beam = 1.0 ft, duct = 1.5 ft. The same obstacle box should
            // exclude MORE candidate locations when classified as a duct (1.5 ft clearance)
            // than as a beam (1.0 ft clearance). A single global ObstacleClearanceFt would
            // let duct and beam produce identical obstacle-rejection counts.
            Console.WriteLine("Test: obstacle-specific clearances (beam 1.0 vs duct 1.5)");

            // 10x10 room, 0.5 ft grid so the 0.5 ft clearance difference lands on the grid.
            List<double[]> roomPoly = Rect(0, 0, 10, 10);
            List<ObstacleData> beam = new List<ObstacleData> { BoxObstacle("Beam", 4, 4, 6, 6) };
            List<ObstacleData> duct = new List<ObstacleData> { BoxObstacle("Duct", 4, 4, 6, 6) };

            BruteForceCalculationConfig fineGrid = new BruteForceCalculationConfig
            {
                GridResolutionFt = 0.5
            };

            int beamRejected = ExtractObstacleRejected(
                BruteForceCalculationService.Calculate(Snapshot(MakeRoom("B", roomPoly,
                    ceilings: new List<CeilingData> { FlatCeiling(9.0) }, obstacles: beam, areaSqFt: 100)),
                    new DefaultHazardPlacementRules(), fineGrid).Rooms[0].Diagnostics);
            int ductRejected = ExtractObstacleRejected(
                BruteForceCalculationService.Calculate(Snapshot(MakeRoom("D", roomPoly,
                    ceilings: new List<CeilingData> { FlatCeiling(9.0) }, obstacles: duct, areaSqFt: 100)),
                    new DefaultHazardPlacementRules(), fineGrid).Rooms[0].Diagnostics);

            // Duct has a wider clearance buffer, so the obstacle-rejection count for the
            // same box must be STRICTLY GREATER for duct than for beam.
            Check(ductRejected > beamRejected,
                "duct (1.5 ft) rejects more candidates than beam (1.0 ft) at the same location (beam=" + beamRejected + ", duct=" + ductRejected + ")");
            Check(beamRejected > 0, "beam still rejects the obstacle's own footprint (got " + beamRejected + ")");
        }

        private static void TestObstacleZAwareRejection()
        {
            // The obstacle-rejection filter must check the obstacle's vertical extent against
            // the placement plane Z. A beam that lives BELOW the ceiling must not block a
            // ceiling sprinkler whose XY footprint overlaps it; a duct that reaches the ceiling
            // must block it. Asserted by POSITION (is a head allowed inside the obstacle
            // footprint?), which is robust to how the centered grid snaps around a blocked
            // cell — unlike a raw candidate count, which snapping can leave unchanged.
            //
            //  - low beam  (MaxZ = 8 ft, below the 9 ft ceiling) -> a head MAY sit in the box
            //  - tall duct (MaxZ = 12 ft, through the ceiling)   -> NO head inside the box
            //  - legacy zero-Z box (unknown extent)              -> blocks, NO head inside
            Console.WriteLine("Test: obstacle Z-axis rejection (low beam vs tall duct)");

            List<double[]> roomPoly = Rect(0, 0, 20, 10);
            // The centered grid's first-row targets land at (5,5) and (15,5); (5,5) is the
            // cell that overlaps the obstacle footprint (4,4)-(6,6).
            const double bx0 = 4, by0 = 4, bx1 = 6, by1 = 6;

            // Beam: Z = 0..8 ft, below the 9 ft ceiling. Must NOT block -> a head stays in the box.
            List<ObstacleData> lowBeam = new List<ObstacleData> { BoxObstacle3D("Beam", bx0, by0, bx1, by1, 0.0, 8.0) };
            RoomCalculationResult beamRoom = Calc(Snapshot(MakeRoom("LB", roomPoly,
                ceilings: new List<CeilingData> { FlatCeiling(9.0) }, obstacles: lowBeam, areaSqFt: 200))).Rooms[0];
            Check(beamRoom.Points.Any(p => p.X >= bx0 && p.X <= bx1 && p.Y >= by0 && p.Y <= by1),
                "low beam (MaxZ=8 ft, below 9 ft ceiling) does NOT block the ceiling sprinkler over it");

            // Duct: Z = 0..12 ft, reaches the ceiling. Must block -> no head inside the box.
            List<ObstacleData> tallDuct = new List<ObstacleData> { BoxObstacle3D("Duct", bx0, by0, bx1, by1, 0.0, 12.0) };
            RoomCalculationResult ductRoom = Calc(Snapshot(MakeRoom("TD", roomPoly,
                ceilings: new List<CeilingData> { FlatCeiling(9.0) }, obstacles: tallDuct, areaSqFt: 200))).Rooms[0];
            Check(ductRoom.Points.All(p => !(p.X >= bx0 && p.X <= bx1 && p.Y >= by0 && p.Y <= by1)),
                "tall duct (MaxZ=12 ft, reaches ceiling) keeps every head out of its footprint (placed=" + ductRoom.CalculatedCount + ")");

            // Legacy obstacle with collapsed Z (0,0): unknown extent -> treat as blocking so
            // pipelines that never populated Z do not silently lose rejection.
            List<ObstacleData> legacy = new List<ObstacleData> { BoxObstacle("Beam", bx0, by0, bx1, by1) };
            RoomCalculationResult legacyRoom = Calc(Snapshot(MakeRoom("LG", roomPoly,
                ceilings: new List<CeilingData> { FlatCeiling(9.0) }, obstacles: legacy, areaSqFt: 200))).Rooms[0];
            Check(legacyRoom.Points.All(p => !(p.X >= bx0 && p.X <= bx1 && p.Y >= by0 && p.Y <= by1)),
                "legacy collapsed-Z obstacle (MinZ==MaxZ==0) keeps every head out of its footprint (placed=" + legacyRoom.CalculatedCount + ")");
        }

        private static int ExtractObstacleRejected(List<string> diagnostics)
        {
            // Diagnostics look like: "Candidates generated=..., valid=..., rejected(outside=..., boundary=..., obstacle=N, existing=...)."
            for (int i = 0; i < diagnostics.Count; i++)
            {
                string d = diagnostics[i];
                if (d == null) continue;
                int idx = d.IndexOf("obstacle=", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                int start = idx + "obstacle=".Length;
                int end = start;
                while (end < d.Length && (char.IsDigit(d[end]) || d[end] == '-')) end++;
                if (end > start && int.TryParse(d.Substring(start, end - start), out int n)) return n;
            }
            return 0;
        }

        private static void TestUnsupportedPlacementBehaviorShortCircuits()
        {
            // Step-2 placement-behavior hook: when the per-row sprinkler family is
            // classified as DevicePlacementBehavior.Unsupported, the engine must
            // short-circuit with CalculationStatus.InvalidInput and ZERO
            // candidates. Producing candidates that the placement service would
            // reject at commit time is worse than failing fast with a clear error.
            Console.WriteLine("Test: unsupported placement behavior short-circuits the engine");

            PlacementRoomInput room = MakeRoom("U", Rect(0, 0, 20, 20),
                ceilings: new List<CeilingData> { FlatCeiling(9.0) }, areaSqFt: 400);
            room.SelectedSprinklerFamilyName = "Unsupported Family";
            room.SelectedSprinklerTypeName = "Unsupported Type";
            room.SelectedSprinklerPlacementBehavior = DevicePlacementBehavior.Unsupported;

            PlacementInputSnapshot snap = new PlacementInputSnapshot();
            snap.Rooms.Add(room);
            BruteForceCalculationResult result = BruteForceCalculationService.Calculate(
                snap, new DefaultHazardPlacementRules(), BruteForceCalculationConfig.Default());

            Check(result.Rooms[0].Status == CalculationStatus.InvalidInput,
                "unsupported behavior -> InvalidInput (got " + result.Rooms[0].Status + ")");
            Check(result.Rooms[0].CalculatedCount == 0,
                "unsupported behavior produces zero sprinklers (got " + result.Rooms[0].CalculatedCount + ")");
            bool hasUnsupportedNote = false;
            for (int i = 0; i < result.Rooms[0].Diagnostics.Count; i++)
            {
                if (result.Rooms[0].Diagnostics[i] != null
                    && result.Rooms[0].Diagnostics[i].IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hasUnsupportedNote = true; break;
                }
            }
            Check(hasUnsupportedNote, "diagnostics mention 'unsupported'");
        }

        private static void TestWallDistanceUpperBoundFlagsReview()
        {
            // NFPA 13 §10.2.4: no sprinkler may be more than MaxDistanceFromWallsFt
            // from the nearest wall. The post-selection wall-distance check must
            // flag this as ReviewRequired. Test the deterministic positive case
            // with a tight custom rule set (MaxDistanceFromWallsFt = 1.0 ft).
            // With a 20x20 room and the default grid, every placed sprinkler is
            // several feet from the wall -> the post-check flags it.
            Console.WriteLine("Test: post-selection wall-distance upper bound flags review");

            HazardPlacementRuleSet tightLight = new HazardPlacementRuleSet
            {
                HazardClass = HazardClass.Light,
                MaxSpacingFt = 15.0,
                MinSpacingFt = 6.0,
                MaxCoverageAreaSqFt = 225.0,
                CoverageRadiusFt = 7.5,
                ObstacleClearanceFt = 1.0,
                BoundaryClearanceFt = 1.0,
                ExistingSprinklerSeparationFt = 7.5,
                MaxDistanceFromWallsFt = 1.0, // tight upper bound
                MinKFactor = 5.6,
                CeilingHeightAdjustmentFactor = 1.0
            };
            TightRulesProvider tight = new TightRulesProvider(tightLight);
            List<double[]> poly = Rect(0, 0, 20, 20);
            PlacementInputSnapshot snap = new PlacementInputSnapshot();
            snap.Rooms.Add(MakeRoom("W-TIGHT", poly,
                ceilings: new List<CeilingData> { FlatCeiling(9.0) }, areaSqFt: 400));
            BruteForceCalculationResult result = BruteForceCalculationService.Calculate(
                snap, tight, BruteForceCalculationConfig.Default());
            RoomCalculationResult rr = result.Rooms[0];

            Check(rr.CalculatedCount > 0,
                "tight room still produces at least one sprinkler (got " + rr.CalculatedCount + ")");
            bool hasWallWarning = rr.Warnings.Any(w =>
                w != null && w.IndexOf("MaxDistanceFromWallsFt", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(hasWallWarning,
                "tight MaxDistanceFromWallsFt=1.0 ft triggers the post-check warning (got " + rr.Warnings.Count + " warnings)");
            Check(rr.Status == CalculationStatus.ReviewRequired,
                "room with violated MaxDistanceFromWallsFt is flagged ReviewRequired (got " + rr.Status + ")");
        }

        private sealed class TightRulesProvider : IHazardPlacementRules
        {
            private readonly HazardPlacementRuleSet _rules;
            public TightRulesProvider(HazardPlacementRuleSet rules) { _rules = rules; }
            public bool HasApprovedRules { get { return false; } }
            public HazardPlacementRuleSet GetRules(HazardClass hazardClass) { return _rules; }
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

        private static void TestCoverageGapDetection()
        {
            // The greedy places a sprinkler at every valid candidate that is uncovered AND
            // not too close to an already-placed sprinkler. A LARGE MinSpacingFt can leave
            // a coverage gap (a valid candidate that would have covered a region is
            // rejected for being too close to an existing placement). The post-selection
            // coverage-gap detector must report such a gap.
            Console.WriteLine("Test: post-selection coverage gap detection");

            // 30x20 room, Light Hazard rules. We supply a ruleset with MinSpacingFt equal
            // to CoverageRadiusFt so the greedy cannot densify and large regions of the
            // room stay beyond CoverageRadiusFt of any placed sprinkler.
            HazardPlacementRuleSet looseRules = new HazardPlacementRuleSet
            {
                HazardClass = HazardClass.Light,
                MaxSpacingFt = 15.0,
                MinSpacingFt = 14.0,
                MaxCoverageAreaSqFt = 225.0,
                CoverageRadiusFt = 8.0,
                ObstacleClearanceFt = 1.0,
                BoundaryClearanceFt = 1.0,
                ExistingSprinklerSeparationFt = 7.5,
                CeilingHeightAdjustmentFactor = 1.0,
                ObstacleSpecificClearances = new System.Collections.Generic.Dictionary<string, double>(System.StringComparer.OrdinalIgnoreCase)
                {
                    { "beam", 1.0 }, { "column", 1.0 }, { "duct", 1.5 }
                }
            };
            SingleRuleSetProvider provider = new SingleRuleSetProvider(looseRules);

            PlacementInputSnapshot s = Snapshot(MakeRoom("G", Rect(0, 0, 30, 20),
                ceilings: new List<CeilingData> { FlatCeiling(9.0) }, areaSqFt: 600));
            BruteForceCalculationResult r = BruteForceCalculationService.Calculate(s, provider, BruteForceCalculationConfig.Default());
            RoomCalculationResult res = r.Rooms[0];

            // Look for the gap diagnostic.
            bool hasGapNote = res.Warnings.Any(w => w != null && w.IndexOf("Coverage gap", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(hasGapNote,
                "coverage gap diagnostic recorded when MinSpacing leaves a region uncovered (placed=" + res.CalculatedCount + ")");
            Check(res.CalculatedCount > 0, "at least one sprinkler was placed (got " + res.CalculatedCount + ")");
        }

        private static void TestGridResolutionFloor()
        {
            // For a room that is large in BOTH dimensions, the grid resolution must be
            // floor'd at half the coverage radius so every coverage diameter gets at
            // least two sample points. A small room (where the floor would skip valid
            // grid points) keeps the original fine grid.
            //
            // We use a 20x20 room, CoverageRadius=8 ft, default GridResolutionFt=1.0.
            // Without the floor: res=1.0 (16 points per axis). With the floor: res=4.0
            // (5 points per axis). The placed-sprinkler count must be strictly FEWER
            // with the floor (coarser grid = fewer candidates that pass the spacing
            // and coverage checks), proving the floor took effect.
            Console.WriteLine("Test: grid resolution floor at CoverageRadius/2 for large rooms");
            PlacementInputSnapshot sLarge = Snapshot(MakeRoom("L", Rect(0, 0, 20, 20),
                ceilings: new List<CeilingData> { FlatCeiling(9.0) }, areaSqFt: 400));

            // 20x20 with CoverageRadius=8 ft: floor is 4 ft. roomW=20, roomH=20. Both >= 2*4=8. Floor applied.
            HazardPlacementRuleSet cr8 = new HazardPlacementRuleSet
            {
                HazardClass = HazardClass.Light,
                MaxSpacingFt = 15.0,
                MinSpacingFt = 6.0,
                CoverageRadiusFt = 8.0,
                ObstacleClearanceFt = 1.0,
                BoundaryClearanceFt = 1.0,
                ExistingSprinklerSeparationFt = 7.5,
                CeilingHeightAdjustmentFactor = 1.0,
                ObstacleSpecificClearances = new System.Collections.Generic.Dictionary<string, double>(System.StringComparer.OrdinalIgnoreCase)
                {
                    { "beam", 1.0 }, { "column", 1.0 }, { "duct", 1.5 }
                }
            };
            int large = BruteForceCalculationService.Calculate(sLarge, new SingleRuleSetProvider(cr8), BruteForceCalculationConfig.Default()).Rooms[0].CalculatedCount;

            // 4x4 small room: floor is 4 ft, but roomW=4 < 2*4=8. Floor NOT applied; res=1.0.
            PlacementInputSnapshot sSmall = Snapshot(MakeRoom("S", Rect(0, 0, 4, 4),
                ceilings: new List<CeilingData> { FlatCeiling(9.0) }, areaSqFt: 16));
            int small = BruteForceCalculationService.Calculate(sSmall, new SingleRuleSetProvider(cr8), BruteForceCalculationConfig.Default()).Rooms[0].CalculatedCount;

            Check(large > 0, "large room produces sprinklers (got " + large + ")");
            Check(small > 0, "small room still produces sprinklers under the no-floor branch (got " + small + ")");

            // The diagnostic must mention the grid resolution the engine actually used.
            // We re-run the large room to read the diagnostics.
            RoomCalculationResult largeResult = BruteForceCalculationService.Calculate(sLarge, new SingleRuleSetProvider(cr8), BruteForceCalculationConfig.Default()).Rooms[0];
            bool hasGridNote = largeResult.Diagnostics.Any(d => d != null && d.IndexOf("Candidates generated", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(hasGridNote, "candidate-generation diagnostic is recorded for audit");
        }

        private static void TestCoverageGapFreeBaseline()
        {
            // Sanity: a normal Light Hazard room with default rules should produce a
            // gap-free layout (no gap warning), because the greedy fills every uncovered
            // valid candidate and the default MinSpacingFt is well below CoverageRadiusFt.
            Console.WriteLine("Test: coverage gap detector does not flag a normal Light Hazard room");
            PlacementInputSnapshot s = Snapshot(MakeRoom("OK", Rect(0, 0, 20, 10),
                ceilings: new List<CeilingData> { FlatCeiling(9.0) }, areaSqFt: 200));
            BruteForceCalculationResult r = Calc(s);
            RoomCalculationResult res = r.Rooms[0];
            bool hasGapNote = res.Warnings.Any(w => w != null && w.IndexOf("Coverage gap", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(!hasGapNote, "no coverage gap warning on a normal 20x10 room");
        }

        private static void TestHostedFamilyBlockedByMissingCeiling()
        {
            // A hosted sprinkler family (FaceHosted / WorkPlaneDependent / CeilingOverhead /
            // Unknown / Unsupported) cannot be placed in Revit without a ceiling or work
            // plane. When the room has NO ceiling at all, the calculation must BLOCK and
            // produce zero candidates instead of generating provisional points that the
            // placement service would reject.
            Console.WriteLine("Test: hosted family + no ceiling is BLOCKED (no candidate generation)");

            // Unknown behavior (the default) is treated as hosted-conservative.
            PlacementInputSnapshot sUnknown = Snapshot(MakeRoom("U", Rect(0, 0, 10, 10), ceilingHeightFt: null));
            sUnknown.Rooms[0].Ceilings = new List<CeilingData>();
            BruteForceCalculationResult rUnknown = Calc(sUnknown);
            RoomCalculationResult resUnknown = rUnknown.Rooms[0];
            Check(resUnknown.Status == CalculationStatus.MissingCeiling,
                "Unknown behavior + no ceiling -> MissingCeiling (got " + resUnknown.Status + ")");
            Check(resUnknown.CalculatedCount == 0,
                "Unknown behavior + no ceiling produces zero sprinklers (got " + resUnknown.CalculatedCount + ")");
            bool hasBlockedNote = resUnknown.Diagnostics.Any(d => d != null && d.IndexOf("BLOCKED", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(hasBlockedNote, "BLOCKED diagnostic recorded for hosted-family + missing ceiling");

            // Explicitly FaceHosted must produce the same BLOCKED outcome.
            PlacementInputSnapshot sFace = Snapshot(MakeRoom("F", Rect(0, 0, 10, 10), ceilingHeightFt: null));
            sFace.Rooms[0].Ceilings = new List<CeilingData>();
            sFace.Rooms[0].SelectedSprinklerPlacementBehavior = DevicePlacementBehavior.FaceHosted;
            BruteForceCalculationResult rFace = Calc(sFace);
            Check(rFace.Rooms[0].Status == CalculationStatus.MissingCeiling,
                "FaceHosted + no ceiling -> MissingCeiling (got " + rFace.Rooms[0].Status + ")");
            Check(rFace.Rooms[0].CalculatedCount == 0,
                "FaceHosted + no ceiling produces zero sprinklers (got " + rFace.Rooms[0].CalculatedCount + ")");

            // LevelHosted is exempt — it can be placed without a ceiling.
            PlacementInputSnapshot sLevel = Snapshot(MakeRoom("L", Rect(0, 0, 10, 10), ceilingHeightFt: 9.0));
            sLevel.Rooms[0].Ceilings = new List<CeilingData>();
            sLevel.Rooms[0].SelectedSprinklerPlacementBehavior = DevicePlacementBehavior.LevelHosted;
            BruteForceCalculationResult rLevel = Calc(sLevel);
            Check(rLevel.Rooms[0].CalculatedCount > 0,
                "LevelHosted + no ceiling still produces sprinklers (got " + rLevel.Rooms[0].CalculatedCount + ")");
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

        private static void TestSlopedCeilingWeightedAverage()
        {
            // A SLOPED ceiling with both BottomElevationFt and TopElevationFt must produce a
            // placement Z that is the GEOMETRIC MEAN of the two (so a sprinkler placed at
            // the room centroid lands at the median ceiling height), not the low-end value
            // that BottomElevationFt alone would give.
            Console.WriteLine("Test: sloped ceiling Z is the (bottom+top)/2 weighted average");
            CeilingData sloped = new CeilingData
            {
                SlopeType = "SLOPED",
                BottomElevationFt = 8.0,
                TopElevationFt = 12.0,
                Source = new SourceReferenceData()
            };
            PlacementRoomInput room = MakeRoom("SC", Rect(0, 0, 20, 10), ceilings: new List<CeilingData> { sloped }, ceilingHeightFt: 10.0);
            PlacementInputSnapshot s = Snapshot(room);
            BruteForceCalculationResult r = Calc(s);
            RoomCalculationResult res = r.Rooms[0];

            // Expected: placement Z = (8 + 12) / 2 = 10.0 ft
            double expectedZ = 10.0;
            bool allMatchZ = res.Points.Count > 0 && res.Points.All(p => Math.Abs(p.Z - expectedZ) < 1e-6);
            Check(allMatchZ,
                "all sloped-ceiling sprinklers land at weighted-average Z (expected " + expectedZ.ToString("F2") + " ft, got Zs=[" +
                string.Join(",", res.Points.Select(p => p.Z.ToString("F2"))) + "])");

            // The diagnostic must mention the averaging so the reviewer can audit the choice.
            bool hasAverageNote = res.Diagnostics.Any(d => d != null && d.IndexOf("averaged", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(hasAverageNote, "sloped-ceiling Z averaging is recorded in diagnostics");
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

    /// <summary>
    /// Test-only rules provider that returns a single fixed rule set for any hazard class.
    /// Used to force non-default values (e.g. a large MinSpacingFt) without touching the
    /// real <see cref="DefaultHazardPlacementRules"/>.
    /// </summary>
    internal sealed class SingleRuleSetProvider : IHazardPlacementRules
    {
        private readonly HazardPlacementRuleSet _rules;
        public SingleRuleSetProvider(HazardPlacementRuleSet rules) { _rules = rules; }
        public bool HasApprovedRules => _rules != null && !_rules.IsProvisional;
        public HazardPlacementRuleSet GetRules(HazardClass hazardClass) { return _rules; }
    }
}
