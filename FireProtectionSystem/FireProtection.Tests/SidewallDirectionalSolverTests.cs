using System;
using System.Collections.Generic;
using System.Linq;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce;
using FireProtection.UI.Models.Sprinklers.BruteForce;

namespace FireProtection.Tests
{
    /// <summary>
    /// Directional sidewall solver (NFPA 13 §11.3). Verifies the behaviour the old circular
    /// greedy got wrong: a room deeper than one throw pulls in the opposing wall, heads are
    /// distributed ALONG each wall (not clustered), a room too deep for any wall is flagged with
    /// a residual gap, and every run carries the FPE-review flag. Nothing here fabricates
    /// manufacturer numbers — coverage geometry is driven by the catalog values.
    /// </summary>
    internal static class SidewallDirectionalSolverTests
    {
        private static int _failures;

        public static void RunAll()
        {
            _failures = 0;
            TestDeepRoomUsesOpposingWalls();
            TestHeadsDistributedAlongWall();
            TestShallowRoomSingleRowCovers();
            TestTooDeepRoomFlagsResidualGap();
            TestAlwaysFlagsReview();
            TestHeadsNotRejectedByTheirOwnHostWall();
            TestMarginalWallDoesNotStackHeads();

            if (_failures == 0)
            {
                Console.WriteLine("SidewallDirectionalSolverTests: PASS");
            }
            else
            {
                Console.WriteLine("SidewallDirectionalSolverTests: " + _failures + " FAIL(s)");
                throw new Exception("SidewallDirectionalSolverTests failed");
            }
        }

        private static void Check(bool condition, string message)
        {
            if (condition) Console.WriteLine("  PASS: " + message);
            else { Console.WriteLine("  FAIL: " + message); _failures++; }
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

        private static PlacementRoomInput MakeSidewallRoom(
            string id, List<double[]> polygon, double areaSqFt,
            double? typeMaxSpacing = 14.0, double? typeMaxCoverageArea = 196.0)
        {
            PlacementRoomInput room = new PlacementRoomInput
            {
                RoomId = id, RoomName = id, RoomNumber = id,
                LevelId = "L1", LevelElevationFt = 0.0,
                AreaSqFt = areaSqFt,
                EffectiveHazardClass = "Light",
                CeilingHeightFt = 9.0,
                Ceilings = new List<CeilingData>
                {
                    new CeilingData { SlopeType = "FLAT", BottomElevationFt = 9.0, Source = new SourceReferenceData() }
                },
                Obstacles = new List<ObstacleData>(),
                ExistingSprinklers = new List<ExistingSprinklerData>(),
                SelectedSprinklerOrientation = "sidewall",
                SprinklerClass = "Sidewall",
                SelectedSprinklerTypeName = "1/2\" Horizontal Sidewall",
                TypeMaxSpacingFt = typeMaxSpacing,
                TypeMaxCoverageAreaSqFt = typeMaxCoverageArea
            };
            room.BoundaryPolygon = polygon;
            room.Boundary = new BoundaryData { OuterLoop = new BoundaryLoopData { IsOuter = true, Polygon = polygon } };
            return room;
        }

        private static RoomCalculationResult Calc(PlacementRoomInput room)
        {
            PlacementInputSnapshot snap = new PlacementInputSnapshot();
            snap.Rooms.Add(room);
            return BruteForceCalculationService.Calculate(
                snap, new DefaultHazardPlacementRules(), BruteForceCalculationConfig.Default()).Rooms[0];
        }

        // Distinct wall edges are identified by the WallEdgeIndex stamped on each point.
        private static int DistinctWalls(RoomCalculationResult r)
        {
            return r.Points.Where(p => p.WallEdgeIndex.HasValue)
                           .Select(p => p.WallEdgeIndex.Value).Distinct().Count();
        }

        private static void TestDeepRoomUsesOpposingWalls()
        {
            Console.WriteLine("Test: a room deeper than one throw is covered from opposing walls, not one");
            // 50 ft wide x 30 ft deep. Throw ~14 ft each way from a long wall reaches 14 ft;
            // 30 ft depth needs BOTH long walls (14 + 14 = 28 < 30 leaves a sliver -> gap flagged,
            // but opposing walls MUST both be used).
            RoomCalculationResult r = Calc(MakeSidewallRoom("DEEP", Rect(0, 0, 50, 30), 1500.0));

            Check(r.Points.Count >= 6, "deep 50x30 room places multiple heads (got " + r.Points.Count + ")");
            Check(DistinctWalls(r) >= 2,
                "deep room uses at least two walls (got " + DistinctWalls(r) + " distinct wall edges)");
        }

        private static void TestHeadsDistributedAlongWall()
        {
            Console.WriteLine("Test: heads on a long wall are spread along it, not clustered in a corner");
            // 42 ft wall, 14 ft spacing -> at least 3 heads spread across the span.
            RoomCalculationResult r = Calc(MakeSidewallRoom("WIDE", Rect(0, 0, 42, 12), 504.0));

            var xs = r.Points.Select(p => p.X).OrderBy(v => v).ToList();
            Check(r.Points.Count >= 3, "42 ft wall gets >=3 heads (got " + r.Points.Count + ")");
            double spanX = xs.Count > 1 ? xs[xs.Count - 1] - xs[0] : 0.0;
            Check(spanX > 20.0,
                "heads span more than 20 ft along the wall, not bunched (span=" + spanX.ToString("F1") + " ft)");
        }

        private static void TestShallowRoomSingleRowCovers()
        {
            Console.WriteLine("Test: a room shallower than one throw is covered from a single wall");
            // 20 ft wide x 10 ft deep: one long wall throwing 14 ft covers the full 10 ft depth.
            RoomCalculationResult r = Calc(MakeSidewallRoom("SHALLOW", Rect(0, 0, 20, 10), 200.0));

            Check(r.Points.Count >= 1, "shallow room places at least one head (got " + r.Points.Count + ")");
            Check(DistinctWalls(r) == 1,
                "shallow room needs only ONE wall (got " + DistinctWalls(r) + ")");
        }

        private static void TestTooDeepRoomFlagsResidualGap()
        {
            Console.WriteLine("Test: a room too deep for any wall's throw is flagged with a residual gap");
            // 40 ft x 60 ft: too deep on BOTH axes. Opposing walls throw ~15 ft each way, so a
            // 40 ft span (15 + 15 = 30 < 40) and a 60 ft span (30 < 60) both leave an uncovered
            // core no pair of walls can reach. Must warn, not pass silently. (A merely narrow-but-
            // long room like 20x60 is NOT too deep — its two long walls throw across the 20 ft.)
            RoomCalculationResult r = Calc(MakeSidewallRoom("TOODEEP", Rect(0, 0, 40, 60), 2400.0));

            Check(r.Status == CalculationStatus.ReviewRequired,
                "too-deep room is ReviewRequired (got " + r.Status + ")");
            bool hasGap = r.Warnings.Any(w => w != null
                && w.IndexOf("coverage gap", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(hasGap, "too-deep room warns about an uncovered interior");
        }

        private static void TestAlwaysFlagsReview()
        {
            Console.WriteLine("Test: every sidewall layout carries the FPE-review flag (never a silent pass)");
            RoomCalculationResult r = Calc(MakeSidewallRoom("REVIEW", Rect(0, 0, 24, 12), 288.0));

            Check(r.Status == CalculationStatus.ReviewRequired,
                "sidewall run is ReviewRequired (got " + r.Status + ")");
            bool hasListingNote = r.Warnings.Any(w => w != null
                && w.IndexOf("listed coverage", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(hasListingNote, "warning tells the user to verify against the manufacturer's listing");
        }

        // Regression: the walls a sidewall head mounts on are themselves obstacle boxes. A head
        // hangs ~0.5 ft off its host wall, well inside that wall's clearance margin, so the
        // obstacle test must NOT reject a head against the very wall it sits on - only obstacles
        // in the throw path (inboard of the head) count. Live Revit runs (every wall an obstacle)
        // exposed this; the earlier synthetic tests had no wall obstacles and missed it.
        private static void TestHeadsNotRejectedByTheirOwnHostWall()
        {
            Console.WriteLine("Test: a head is not blocked by the clearance margin of the wall it mounts on");
            // Real client Room 100: ~49 ft wide x ~19 ft deep, with wall obstacle boxes on every side.
            PlacementRoomInput room = MakeSidewallRoom("HOSTWALL",
                new List<double[]>
                {
                    new double[] { 62.456, 49.290 },
                    new double[] { 13.123, 49.290 },
                    new double[] { 13.123, 29.956 },
                    new double[] { 62.456, 29.956 },
                }, 953.8);
            room.Obstacles = new List<ObstacleData>
            {
                WallBox("north", 12.456, 49.290, 62.790, 49.956),
                WallBox("east",  62.456,  9.623, 63.123, 49.956),
                WallBox("south", 12.790, 29.290, 62.790, 29.956),
                WallBox("west",  12.456, 29.290, 13.123, 49.956),
            };
            RoomCalculationResult r = Calc(room);

            // A 49 ft wall at ~12 ft along-spacing carries ~5 heads; before the fix only 2 heads
            // survived (on the one wall with no obstacle box) and 77% of the room was flagged dry.
            Check(r.Points.Count >= 4,
                "49 ft room with wall obstacles places a full row (got " + r.Points.Count + ")");
            bool hasGap = r.Warnings.Any(w => w != null
                && w.IndexOf("coverage gap", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(!hasGap,
                "a room within one throw is fully covered, no residual-gap warning");
        }

        // Regression: a wall only marginally longer than 2*endWallMax (e.g. a 13 ft wall with
        // ~11.9 ft of end-offsets) used to drop TWO heads ~1 ft apart - below min spacing and
        // clustered in one spot. Live house model: BEDROOM 2 (13x13) and BEDROOM 5 (13x18.7)
        // showed exactly this. Now such a wall must get a single centered head.
        private static void TestMarginalWallDoesNotStackHeads()
        {
            Console.WriteLine("Test: a marginal-length wall gets one centered head, not two stacked ~1 ft apart");
            RoomCalculationResult r = Calc(MakeSidewallRoom("MARGINAL", Rect(0, 0, 13, 13), 169.0));

            // No two heads on the same wall may sit closer than the min spacing.
            var byWall = r.Points.Where(p => p.WallEdgeIndex.HasValue)
                                 .GroupBy(p => p.WallEdgeIndex.Value);
            double worst = double.PositiveInfinity;
            foreach (var g in byWall)
            {
                var pts = g.ToList();
                for (int i = 0; i < pts.Count; i++)
                    for (int j = i + 1; j < pts.Count; j++)
                    {
                        double dd = Math.Sqrt(
                            (pts[i].X - pts[j].X) * (pts[i].X - pts[j].X) +
                            (pts[i].Y - pts[j].Y) * (pts[i].Y - pts[j].Y));
                        if (dd < worst) worst = dd;
                    }
            }
            Check(!(worst < 5.0),
                "no two heads on one wall are closer than the 5 ft min spacing (closest="
                + (double.IsInfinity(worst) ? "n/a (<2 on any wall)" : worst.ToString("F2") + " ft") + ")");
        }

        private static ObstacleData WallBox(string id, double minx, double miny, double maxx, double maxy)
        {
            return new ObstacleData
            {
                ElementId = id, Name = "Generic - 8\"", Category = "OST_Walls",
                StructuralType = "Walls", LevelId = "L1",
                Source = new SourceReferenceData(),
                BoundingBox = new BoundingBox3DData
                {
                    Min = new Point3DData { X = minx, Y = miny, Z = 0.0 },
                    Max = new Point3DData { X = maxx, Y = maxy, Z = 11.0 }
                }
            };
        }
    }
}
