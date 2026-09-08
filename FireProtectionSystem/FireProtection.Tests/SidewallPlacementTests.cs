using System;
using System.Collections.Generic;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.Backend.Services.Placement.Sprinklers.Final;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce;
using FireProtection.UI.Models.Sprinklers.BruteForce;

namespace FireProtection.Tests
{
    /// <summary>
    /// Tests for the sidewall (NFPA 13 §11.3) candidate branch in
    /// <see cref="BruteForceCalculationService"/>. Companion to
    /// <see cref="BruteForceOverrideTests"/>.
    /// </summary>
    internal static class SidewallPlacementTests
    {
        private static int _failures;

        public static void RunAll()
        {
            _failures = 0;
            TestSidewallPlacesAlongWalls();
            TestSidewallDoesNotPlaceInInterior();
            TestSidewallAndPendentCandidateSetsDiffer();
            TestSelectedSprinklerOrientationFlowsFromBehavior();
            TestSidewallOrientationFactorApplies();

            if (_failures == 0)
            {
                Console.WriteLine("SidewallPlacementTests: PASS");
            }
            else
            {
                Console.WriteLine("SidewallPlacementTests: " + _failures + " FAIL(s)");
                throw new Exception("SidewallPlacementTests failed");
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

        private static PlacementRoomInput MakeRoom(
            string id,
            List<double[]> polygon,
            double areaSqFt = 100.0,
            DevicePlacementBehavior behavior = DevicePlacementBehavior.CeilingOverhead)
        {
            PlacementRoomInput room = new PlacementRoomInput
            {
                RoomId = id,
                RoomName = id,
                RoomNumber = id,
                LevelId = "L1",
                LevelElevationFt = 0.0,
                AreaSqFt = areaSqFt,
                EffectiveHazardClass = "Light",
                CeilingHeightFt = 9.0,
                Ceilings = new List<CeilingData> { FlatCeiling(9.0) },
                Obstacles = new List<ObstacleData>(),
                ExistingSprinklers = new List<ExistingSprinklerData>(),
                SelectedSprinklerPlacementBehavior = behavior
            };
            room.BoundaryPolygon = polygon;
            room.Boundary = new BoundaryData
            {
                OuterLoop = new BoundaryLoopData { IsOuter = true, Polygon = polygon }
            };
            return room;
        }

        private static BruteForceCalculationResult Calc(PlacementInputSnapshot snapshot)
        {
            return BruteForceCalculationService.Calculate(
                snapshot, new DefaultHazardPlacementRules(), BruteForceCalculationConfig.Default());
        }

        // -----------------------------------------------------------------
        // Test 1 — every selected sidewall sprinkler is within the
        // boundary-clearance + 1 ft of a wall (industry-standard stand-off).
        // -----------------------------------------------------------------
        private static void TestSidewallPlacesAlongWalls()
        {
            Console.WriteLine("Test: sidewall sprinklers are placed along the room walls");
            List<double[]> poly = Rect(0, 0, 30, 20);
            PlacementInputSnapshot snap = new PlacementInputSnapshot();
            snap.Rooms.Add(MakeRoom("S", poly, areaSqFt: 600, behavior: DevicePlacementBehavior.WallSidewall));
            BruteForceCalculationResult result = Calc(snap);
            RoomCalculationResult room = result.Rooms[0];

            Check(room.CalculatedCount > 0, "sidewall room places at least one sprinkler (got " + room.CalculatedCount + ")");
            if (room.CalculatedCount == 0) return;

            // Default BoundaryClearanceFt for Light Hazard is 1 ft. Sidewall
            // candidates are projected exactly that far inboard, so a sprinkler
            // can land at most BoundaryClearanceFt from the wall. Allow a small
            // tolerance (1.5 ft) for edge cases.
            double maxDistFromWall = 0.0;
            foreach (CalculatedSprinklerPoint p in room.Points)
            {
                double d = MinDistanceToPolygonBoundary(p.X, p.Y, poly);
                if (d > maxDistFromWall) maxDistFromWall = d;
            }
            Check(maxDistFromWall < 1.5,
                "every sidewall sprinkler is within 1.5 ft of a wall (max observed: " + maxDistFromWall.ToString("F3") + " ft)");
        }

        // -----------------------------------------------------------------
        // Test 2 — no sidewall sprinkler lands near the geometric center
        // of the room (the ceiling-grid center is ~7.5 ft from any wall on
        // a 30x20 room).
        // -----------------------------------------------------------------
        private static void TestSidewallDoesNotPlaceInInterior()
        {
            Console.WriteLine("Test: sidewall sprinklers are NOT placed in the room interior");
            List<double[]> poly = Rect(0, 0, 30, 20);
            PlacementInputSnapshot snap = new PlacementInputSnapshot();
            snap.Rooms.Add(MakeRoom("S", poly, areaSqFt: 600, behavior: DevicePlacementBehavior.WallSidewall));
            BruteForceCalculationResult result = Calc(snap);
            RoomCalculationResult room = result.Rooms[0];

            // Room center (15, 10) is ~15 ft from the left/right walls and 10 ft
            // from the top/bottom — far from any wall. The sidewall branch must
            // not place a sprinkler anywhere near it.
            int nearCenter = 0;
            foreach (CalculatedSprinklerPoint p in room.Points)
            {
                double dx = p.X - 15.0;
                double dy = p.Y - 10.0;
                if (dx * dx + dy * dy < 25.0) // within 5 ft of room center
                {
                    nearCenter++;
                }
            }
            Check(nearCenter == 0,
                "no sidewall sprinkler within 5 ft of the room center (15,10) (found " + nearCenter + ")");
        }

        // -----------------------------------------------------------------
        // Test 3 — the same room, run once with pendent behavior and once
        // with sidewall behavior, must yield visibly different point sets.
        // -----------------------------------------------------------------
        private static void TestSidewallAndPendentCandidateSetsDiffer()
        {
            Console.WriteLine("Test: sidewall and pendent produce visibly different point sets");
            List<double[]> poly = Rect(0, 0, 30, 20);

            PlacementInputSnapshot sPendent = new PlacementInputSnapshot();
            sPendent.Rooms.Add(MakeRoom("P", poly, areaSqFt: 600, behavior: DevicePlacementBehavior.CeilingOverhead));
            List<CalculatedSprinklerPoint> pendentPoints = Calc(sPendent).Rooms[0].Points;

            PlacementInputSnapshot sSidewall = new PlacementInputSnapshot();
            sSidewall.Rooms.Add(MakeRoom("S", poly, areaSqFt: 600, behavior: DevicePlacementBehavior.WallSidewall));
            List<CalculatedSprinklerPoint> sidewallPoints = Calc(sSidewall).Rooms[0].Points;

            // Compute the mean distance of each sidewall point to the nearest pendent point.
            // Sidewall points cluster along the walls; pendent points are scattered across
            // the ceiling. The two sets must not be coincident.
            bool differ = false;
            double closestMax = 0.0;
            foreach (CalculatedSprinklerPoint sp in sidewallPoints)
            {
                double closest = double.PositiveInfinity;
                foreach (CalculatedSprinklerPoint pp in pendentPoints)
                {
                    double d = Distance(sp.X, sp.Y, pp.X, pp.Y);
                    if (d < closest) closest = d;
                }
                if (closest > closestMax) closestMax = closest;
            }
            // Light Hazard's ceiling grid is dense; at least one sidewall point should be
            // meaningfully far from any pendent point (> 1.5 ft).
            differ = closestMax > 1.5;
            Check(differ,
                "at least one sidewall point is > 1.5 ft from any pendent point (max sidewall-to-pendent distance: "
                + closestMax.ToString("F2") + " ft; pendent=" + pendentPoints.Count
                + " pts, sidewall=" + sidewallPoints.Count + " pts)");
        }

        // -----------------------------------------------------------------
        // Test 4 — PlacementInputBuilder stamps SelectedSprinklerOrientation
        // = "sidewall" when the resolver returns WallSidewall.
        // -----------------------------------------------------------------
        private static void TestSelectedSprinklerOrientationFlowsFromBehavior()
        {
            Console.WriteLine("Test: PlacementInputBuilder stamps orientation='sidewall' when behavior is WallSidewall");
            List<double[]> poly = Rect(0, 0, 30, 20);
            PlacementRoomSelection sel = new PlacementRoomSelection
            {
                LevelId = "L1",
                LevelName = "L1",
                LevelElevationFt = 0.0,
                RoomId = "R1",
                RoomName = "R1",
                RoomNumber = "R1",
                AreaSqFt = 600,
                EffectiveHazardClass = "Light",
                CeilingHeightFt = 9.0,
                CeilingType = "FLAT",
                Polygon = poly,
                Ceilings = new List<CeilingData> { FlatCeiling(9.0) },
                Obstacles = new List<ObstacleData>(),
                ExistingSprinklers = new List<ExistingSprinklerData>(),
                Source = new SourceReferenceData(),
                SelectedSprinklerFamilyName = "Sprinkler - Dry -Horizontal Sidewall_Hosted",
                SelectedSprinklerTypeName = "1/2\" Dry horizontal Sidewall"
            };

            DeviceContextResolver resolver = (family, type) => new DevicePlacementContext
            {
                FamilyName = family,
                TypeName = type,
                FamilyPlacementType = "WorkPlaneBased",
                PlacementBehavior = DevicePlacementBehavior.WallSidewall,
                Resolved = true,
                Mount = "Sidewall"
            };

            PlacementInputSnapshot snap = PlacementInputBuilder.Build(
                "TestProject", "UniversalFamily", "UniversalType", new[] { sel }, resolver);
            Check(snap.Rooms[0].SelectedSprinklerOrientation == "sidewall",
                "SelectedSprinklerOrientation is 'sidewall' (got '"
                + (snap.Rooms[0].SelectedSprinklerOrientation ?? "<null>") + "')");
            Check(snap.Rooms[0].SelectedSprinklerPlacementBehavior == DevicePlacementBehavior.WallSidewall,
                "SelectedSprinklerPlacementBehavior is WallSidewall (got "
                + snap.Rooms[0].SelectedSprinklerPlacementBehavior + ")");
        }

        // -----------------------------------------------------------------
        // Test 5 — when a sidewall room is run, AppliedMaxSpacingFt is
        // 0.85x the rule-set baseline (NFPA 13 §10.2 sidewall factor).
        // -----------------------------------------------------------------
        private static void TestSidewallOrientationFactorApplies()
        {
            Console.WriteLine("Test: sidewall room's AppliedMaxSpacingFt is 0.85x the rule-set baseline");
            List<double[]> poly = Rect(0, 0, 30, 20);
            PlacementInputSnapshot snap = new PlacementInputSnapshot();
            PlacementRoomInput room = MakeRoom("S", poly, areaSqFt: 600, behavior: DevicePlacementBehavior.CeilingOverhead);
            room.SelectedSprinklerOrientation = "sidewall";
            snap.Rooms.Add(room);

            BruteForceCalculationResult result = Calc(snap);
            RoomCalculationResult r = result.Rooms[0];

            // Default Light Hazard MaxSpacingFt is 15 ft (placeholder). After the 0.85
            // sidewall multiplier it should be 12.75 ft.
            double? spacing = r.AppliedMaxSpacingFt;
            Check(spacing.HasValue, "AppliedMaxSpacingFt is populated (got " + (spacing.HasValue ? spacing.Value.ToString("F3") : "null") + ")");
            if (!spacing.HasValue) return;
            Check(Math.Abs(spacing.Value - 12.75) < 0.01,
                "AppliedMaxSpacingFt is 12.75 ft (got " + spacing.Value.ToString("F3") + " ft)");
        }

        // -----------------------------------------------------------------
        // Geometry helpers
        // -----------------------------------------------------------------

        private static double MinDistanceToPolygonBoundary(double x, double y, List<double[]> poly)
        {
            double best = double.PositiveInfinity;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double[] pi = poly[i];
                double[] pj = poly[j];
                double dx = pj[0] - pi[0];
                double dy = pj[1] - pi[1];
                double len2 = dx * dx + dy * dy;
                if (len2 < 1e-12) continue;
                double t = ((x - pi[0]) * dx + (y - pi[1]) * dy) / len2;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                double projX = pi[0] + t * dx;
                double projY = pi[1] + t * dy;
                double ex = x - projX;
                double ey = y - projY;
                double d = Math.Sqrt(ex * ex + ey * ey);
                if (d < best) best = d;
            }
            return best;
        }

        private static double Distance(double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
