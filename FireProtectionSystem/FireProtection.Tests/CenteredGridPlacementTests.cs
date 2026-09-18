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
    }
}
