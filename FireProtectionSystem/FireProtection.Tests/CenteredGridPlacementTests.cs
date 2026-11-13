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
    /// <summary>
    /// Geometry-level tests for the centered rectangular-grid ceiling layout
    /// (<c>BruteForceCalculationService.SelectCenteredGrid</c>). They assert the industry-standard
    /// array properties on OPEN rooms — where every grid target is valid, so the layout is the
    /// exact centered grid: (a) the on-screen S->S spacing drives the head count, (b) center-to-
    /// center spacing stays within S, and (c) perimeter heads stay within S/2 of every wall.
    ///
    /// These verify the ALGORITHM. The NFPA-13 spacing NUMBERS remain provisional
    /// (<see cref="DefaultHazardPlacementRules"/> reports HasApprovedRules=false); the effective
    /// max spacing is therefore &lt;= the 15 ft Light placeholder, so the &lt;= S and &lt;= S/2 bounds
    /// below (measured against S = 15) hold for any factor the rule set applies.
    /// </summary>
    internal static class CenteredGridPlacementTests
    {
        private static int _failures;

        public static void RunAll()
        {
            _failures = 0;
            TestSToSDrivesHeadCount();
            TestGridSpacingWithinMax();
            TestPerimeterHeadsWithinHalfSpacing();
            TestIgnoresSlabOfLevelAbove();
            TestPrefersOwnLevelCeiling();
            TestFlagsOutOfBandCeiling();

            if (_failures == 0)
            {
                Console.WriteLine("CenteredGridPlacementTests: PASS");
            }
            else
            {
                Console.WriteLine("CenteredGridPlacementTests: " + _failures + " FAIL(s)");
                throw new Exception("CenteredGridPlacementTests failed");
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

        private static PlacementRoomInput MakeRoom(string id, List<double[]> polygon, double? maxSpacing = null)
        {
            PlacementRoomInput room = new PlacementRoomInput
            {
                RoomId = id,
                RoomName = id,
                RoomNumber = id,
                LevelId = "L1",
                LevelElevationFt = 0.0,
                AreaSqFt = 1200.0,
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
            return room;
        }

        private static RoomCalculationResult Calc(PlacementRoomInput room)
        {
            PlacementInputSnapshot snap = new PlacementInputSnapshot();
            snap.Rooms.Add(room);
            return BruteForceCalculationService.Calculate(
                snap, new DefaultHazardPlacementRules(), BruteForceCalculationConfig.Default()).Rooms[0];
        }

        private static void TestSToSDrivesHeadCount()
        {
            // THE headline fix: editing the S->S value must change the layout. On an open room a
            // tighter max spacing means a denser grid -> strictly more heads. Under the previous
            // greedy selection (spacing governed by an internal coverage radius) this count did
            // NOT move when S changed.
            Console.WriteLine("Test: S->S max-spacing drives the centered-grid head count");
            List<double[]> poly = Rect(0, 0, 40, 30);

            int defaultCount = Calc(MakeRoom("D", poly)).CalculatedCount;               // 15 ft placeholder
            int tightCount = Calc(MakeRoom("T", poly, maxSpacing: 8.0)).CalculatedCount; // 8 ft override

            Check(defaultCount > 0,
                "open 40x30 room places heads at the 15 ft baseline (got " + defaultCount + ")");
            Check(tightCount > defaultCount,
                "tighter S->S (8 ft) packs more heads than the 15 ft baseline (baseline=" + defaultCount + ", tight=" + tightCount + ")");
        }

        private static void TestGridSpacingWithinMax()
        {
            // NFPA 13 §8.6: center-to-center spacing must not exceed S. On an open room every grid
            // target is valid, so each head's nearest neighbour is exactly one grid step (<= S).
            Console.WriteLine("Test: centered-grid center-to-center spacing stays within S");
            const double S = 15.0;
            List<double[]> poly = Rect(0, 0, 40, 30);
            List<double[]> pts = Calc(MakeRoom("G", poly)).Points
                .Select(p => new double[] { p.X, p.Y }).ToList();

            Check(pts.Count >= 2, "grid produced multiple heads to measure spacing (got " + pts.Count + ")");

            double maxNearest = 0.0;
            for (int i = 0; i < pts.Count; i++)
            {
                double nearest = double.PositiveInfinity;
                for (int j = 0; j < pts.Count; j++)
                {
                    if (i == j) continue;
                    double dx = pts[i][0] - pts[j][0], dy = pts[i][1] - pts[j][1];
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < nearest) nearest = d;
                }
                if (nearest > maxNearest && !double.IsPositiveInfinity(nearest)) maxNearest = nearest;
            }

            Check(maxNearest <= S + 0.01,
                "every head has a neighbour within the max spacing S=" + S.ToString("F1")
                + " ft (worst nearest=" + maxNearest.ToString("F2") + " ft)");
        }

        private static void TestPerimeterHeadsWithinHalfSpacing()
        {
            // NFPA 13: a sprinkler must be within S/2 of each wall. The centered array offsets the
            // first/last line by step/2 (<= S/2), so the head nearest each of the four walls of an
            // open rectangle is within S/2.
            Console.WriteLine("Test: perimeter heads stay within S/2 of every wall");
            const double S = 15.0, W = 40.0, H = 30.0;
            List<double[]> poly = Rect(0, 0, W, H);
            var pts = Calc(MakeRoom("W", poly)).Points;

            Check(pts.Count > 0, "grid produced heads to measure wall distance (got " + pts.Count + ")");
            if (pts.Count == 0) return;

            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            double left = minX, right = W - maxX, bottom = minY, top = H - maxY;
            double worstWall = Math.Max(Math.Max(left, right), Math.Max(bottom, top));

            Check(worstWall <= S / 2.0 + 0.01,
                "nearest head to each wall is within S/2=" + (S / 2.0).ToString("F1")
                + " ft (worst wall gap=" + worstWall.ToString("F2") + " ft: L=" + left.ToString("F2")
                + ", R=" + right.ToString("F2") + ", B=" + bottom.ToString("F2") + ", T=" + top.ToString("F2") + ")");
        }

        // =================================================================
        // Mounting plane (Z) selection — wrong-Z regression tests
        // =================================================================
        //
        // A room in a stacked building has several candidate ceilings: its own, a bulkhead or
        // soffit, and the slab that forms the FLOOR of the level above. Selecting the wrong one
        // places an entire room's heads at the wrong height — a silent error that is very hard
        // to spot in the model and impossible to detect in a plan view.

        private static CeilingData CeilingAt(double bottom, string slope, string levelId)
        {
            return new CeilingData
            {
                SlopeType = slope,
                BottomElevationFt = bottom,
                LevelId = levelId,
                Source = new SourceReferenceData()
            };
        }

        /// <summary>
        /// With ceilings on BOTH the room's level and the level above, the room's own ceiling wins.
        /// The previous fallback ranked by HIGHEST bottom elevation whenever no level match was
        /// required, so a room with no level-tagged ceiling selected the ceiling of the floor
        /// above — a 12 ft Z error across every head in the room.
        /// </summary>
        private static void TestIgnoresSlabOfLevelAbove()
        {
            Console.WriteLine("Test: the ceiling of the level ABOVE is not used as the mounting plane");

            PlacementRoomInput room = MakeRoom("Z", Rect(0, 0, 40, 30));
            room.LevelElevationFt = 0.0;
            room.CeilingHeightFt = 9.0;
            // Room's own ceiling at 9 ft (untagged level), plus the Level 2 slab ceiling at 21 ft.
            room.Ceilings = new List<CeilingData>
            {
                CeilingAt(21.0, "FLAT", "L2"),
                CeilingAt(9.0, "FLAT", null)
            };

            var res = Calc(room);
            Check(res.Points.Count > 0, "room still places heads (got " + res.CalculatedCount + ")");

            // The selected mounting plane is the 9 ft ceiling, less one deflector drop (Ch.19 p.225).
            double expectedZ = 9.0 - Nfpa13RulebookRules.DeflectorMinDropUnobstructedFt;
            double worstZ = res.Points.Count == 0 ? double.NaN : res.Points.Max(p => p.Z);
            Check(Math.Abs(worstZ - expectedZ) < 0.01,
                "all heads land under the room's own 9 ft ceiling, not the 21 ft slab (worst Z="
                + worstZ.ToString("F2") + " ft, expected " + expectedZ.ToString("F2") + " ft)");
        }

        /// <summary>When both levels carry a tagged ceiling, the room's own level wins outright.</summary>
        private static void TestPrefersOwnLevelCeiling()
        {
            Console.WriteLine("Test: a ceiling tagged with the room's own level is preferred");

            PlacementRoomInput room = MakeRoom("ZL", Rect(0, 0, 40, 30));
            room.LevelId = "L1";
            room.LevelElevationFt = 0.0;
            room.CeilingHeightFt = 9.0;
            room.Ceilings = new List<CeilingData>
            {
                CeilingAt(21.0, "FLAT", "L2"),
                CeilingAt(9.0, "FLAT", "L1")
            };

            var res = Calc(room);
            double expectedZ = 9.0 - Nfpa13RulebookRules.DeflectorMinDropUnobstructedFt;
            double worstZ = res.Points.Count == 0 ? double.NaN : res.Points.Max(p => p.Z);
            Check(Math.Abs(worstZ - expectedZ) < 0.01,
                "the L1 ceiling is selected over the L2 ceiling (worst Z=" + worstZ.ToString("F2")
                + " ft, expected " + expectedZ.ToString("F2") + " ft)");
        }

        /// <summary>
        /// A ceiling far above the room's nominal height (a plenum deck) is not a mounting plane.
        /// It must be excluded AND the room must be told, rather than the heads being placed
        /// silently against the deck.
        /// </summary>
        private static void TestFlagsOutOfBandCeiling()
        {
            Console.WriteLine("Test: a ceiling far above the nominal height is rejected and reported");

            PlacementRoomInput room = MakeRoom("ZB", Rect(0, 0, 40, 30));
            room.LevelId = "L1";
            room.LevelElevationFt = 0.0;
            room.CeilingHeightFt = 9.0;
            // Only candidate is a plenum deck at 20 ft — 11 ft above the nominal 9 ft ceiling.
            room.Ceilings = new List<CeilingData> { CeilingAt(20.0, "FLAT", "L1") };

            var res = Calc(room);
            double worstZ = res.Points.Count == 0 ? double.NaN : res.Points.Max(p => p.Z);
            Check(Math.Abs(worstZ - 20.0) > 0.01,
                "heads are NOT placed against the 20 ft plenum deck (worst Z=" + worstZ.ToString("F2") + " ft)");

            Check(res.Status == CalculationStatus.ReviewRequired,
                "the room is flagged ReviewRequired (got " + res.Status + ")");
            Check(HasMessageAboutCeiling(res),
                "the user is told the ceiling needs review");
        }

        private static bool HasMessageAboutCeiling(RoomCalculationResult res)
        {
            foreach (string w in res.Warnings)
            {
                if (w != null && w.IndexOf("ceiling", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            foreach (string e in res.Errors)
            {
                if (e != null && e.IndexOf("ceiling", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }
}
