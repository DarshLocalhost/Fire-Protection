using System;
using System.Collections.Generic;
using System.Linq;
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
            TestSidewallIgnoresNonWallBoundarySegments();
            TestSelectedSprinklerOrientationFlowsFromBehavior();
            TestSidewallOrientationFactorApplies();
            TestSidewallObstacleConflictFlagging();
            TestSidewallCoverageNotCompromisedByObstacles();
            TestSidewallBalancedWallDistribution();
            TestSidewallObstacleNotchBridgedAndFlagged();

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

        private static void TestSidewallIgnoresNonWallBoundarySegments()
        {
            Console.WriteLine("Test: sidewall solver ignores room-boundary segments that are not actual walls");
            List<double[]> poly = Rect(0, 0, 30, 20);
            PlacementRoomInput room = MakeRoom("S", poly, areaSqFt: 600, behavior: DevicePlacementBehavior.WallSidewall);

            room.Boundary = new BoundaryData
            {
                OuterLoop = new BoundaryLoopData { IsOuter = true, Polygon = poly },
                Polygon = poly
            };
            room.Boundary.OuterLoop.Segments = new List<BoundarySegmentData>
            {
                new BoundarySegmentData { Start = new Point3DData(0,0,0), End = new Point3DData(30,0,0), IsWall = true },
                new BoundarySegmentData { Start = new Point3DData(30,0,0), End = new Point3DData(30,20,0), IsWall = false },
                new BoundarySegmentData { Start = new Point3DData(30,20,0), End = new Point3DData(0,20,0), IsWall = true },
                new BoundarySegmentData { Start = new Point3DData(0,20,0), End = new Point3DData(0,0,0), IsWall = true }
            };

            PlacementInputSnapshot snap = new PlacementInputSnapshot();
            snap.Rooms.Add(room);

            BruteForceCalculationResult result = Calc(snap);
            RoomCalculationResult roomResult = result.Rooms[0];
            bool onFakeWall = roomResult.Points.Exists(p => p.WallEdgeIndex == 1);
            Check(!onFakeWall,
                "sidewall points never come from a non-wall boundary segment (fake wall edge index 1 was used)");
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
        // Test 6 — when an obstacle is located directly over a sidewall
        // candidate point, that candidate receives HasObstacleConflict = true,
        // while candidate points away from the obstacle have HasObstacleConflict = false.
        // -----------------------------------------------------------------
        private static void TestSidewallObstacleConflictFlagging()
        {
            Console.WriteLine("Test: sidewall obstacle conflict is flagged ONLY on colliding points");
            List<double[]> poly = Rect(0, 0, 30, 20);
            PlacementRoomInput room = MakeRoom("S_Obs", poly, areaSqFt: 600, behavior: DevicePlacementBehavior.WallSidewall);

            // First run without obstacles to know where candidates land
            PlacementInputSnapshot snapClean = new PlacementInputSnapshot();
            snapClean.Rooms.Add(room);
            BruteForceCalculationResult cleanResult = Calc(snapClean);
            RoomCalculationResult cleanRoom = cleanResult.Rooms[0];
            Check(cleanRoom.Points.Count > 1, "clean sidewall run produces multiple points (got " + cleanRoom.Points.Count + ")");
            if (cleanRoom.Points.Count < 2) return;

            CalculatedSprinklerPoint targetPt = cleanRoom.Points[0];
            CalculatedSprinklerPoint otherPt = cleanRoom.Points[1];

            // Now place an obstacle box covering targetPt's location
            ObstacleData obs = new ObstacleData
            {
                ElementId = "Col-101",
                Category = "OST_Columns",
                BoundingBox = new BoundingBox3DData(
                    new Point3DData(targetPt.X - 1.0, targetPt.Y - 1.0, 0.0),
                    new Point3DData(targetPt.X + 1.0, targetPt.Y + 1.0, 10.0))
            };

            PlacementRoomInput roomWithObs = MakeRoom("S_Obs", poly, areaSqFt: 600, behavior: DevicePlacementBehavior.WallSidewall);
            roomWithObs.Obstacles.Add(obs);

            PlacementInputSnapshot snapObs = new PlacementInputSnapshot();
            snapObs.Rooms.Add(roomWithObs);
            BruteForceCalculationResult obsResult = Calc(snapObs);
            RoomCalculationResult obsRoom = obsResult.Rooms[0];

            int conflictCount = 0;
            int nonConflictCount = 0;
            foreach (CalculatedSprinklerPoint pt in obsRoom.Points)
            {
                if (pt.HasObstacleConflict)
                {
                    conflictCount++;
                    Check(Math.Abs(pt.X - targetPt.X) < 1.0 && Math.Abs(pt.Y - targetPt.Y) < 1.0,
                        "conflict point matches target obstacle location");
                }
                else
                {
                    nonConflictCount++;
                }
            }

            Check(conflictCount == 1, "exactly 1 candidate flagged with obstacle conflict (got " + conflictCount + ")");
            Check(nonConflictCount >= 1, "other candidate points have HasObstacleConflict = false (got " + nonConflictCount + ")");
        }

        // -----------------------------------------------------------------
        // Test 7 — large interior obstacles (columns/casework) do not reduce
        // room coverage evaluation; the solver still provides full opposing-wall
        // heads as required for room depth.
        // -----------------------------------------------------------------
        private static void TestSidewallCoverageNotCompromisedByObstacles()
        {
            Console.WriteLine("Test: interior obstacles do not reduce sidewall coverage calculation");
            List<double[]> poly = Rect(0, 0, 30, 20);

            // Clean run
            PlacementRoomInput roomClean = MakeRoom("S_Clean", poly, areaSqFt: 600, behavior: DevicePlacementBehavior.WallSidewall);
            PlacementInputSnapshot snapClean = new PlacementInputSnapshot();
            snapClean.Rooms.Add(roomClean);
            BruteForceCalculationResult resClean = Calc(snapClean);

            // Run with large interior obstacles in the middle of the room
            PlacementRoomInput roomObs = MakeRoom("S_ObsMid", poly, areaSqFt: 600, behavior: DevicePlacementBehavior.WallSidewall);
            roomObs.Obstacles.Add(new ObstacleData
            {
                ElementId = "KitchenIsland",
                Category = "OST_Casework",
                BoundingBox = new BoundingBox3DData(
                    new Point3DData(5.0, 5.0, 0.0),
                    new Point3DData(25.0, 15.0, 10.0))
            });

            PlacementInputSnapshot snapObs = new PlacementInputSnapshot();
            snapObs.Rooms.Add(roomObs);
            BruteForceCalculationResult resObs = Calc(snapObs);

            // Both runs must produce the same number of heads along the perimeter walls,
            // because the interior obstacle does not alter the perimeter walls' coverage requirement.
            Check(resObs.Rooms[0].CalculatedCount >= resClean.Rooms[0].CalculatedCount,
                "room with interior obstacle does not produce fewer sidewall heads (clean="
                + resClean.Rooms[0].CalculatedCount + ", withObs=" + resObs.Rooms[0].CalculatedCount + ")");
        }

        private static void TestSidewallBalancedWallDistribution()
        {
            Console.WriteLine("Test: sidewall distribution is balanced and centered along walls (NFPA 13 & Industry Standard)");
            // 21.61 ft wall (matching the kitchen south wall)
            List<double[]> poly = Rect(0, 0, 21.61, 10.0);
            PlacementRoomInput room = MakeRoom("S_Balanced", poly, areaSqFt: 216.1, behavior: DevicePlacementBehavior.WallSidewall);
            PlacementInputSnapshot snap = new PlacementInputSnapshot();
            snap.Rooms.Add(room);
            BruteForceCalculationResult res = Calc(snap);
            RoomCalculationResult r = res.Rooms[0];

            // Primary wall is 21.61 ft. Throw is 14 ft >= 10 ft depth, so 1 wall is sufficient.
            Check(r.Points.Count == 2, "21.61 ft wall gets exactly 2 heads (got " + r.Points.Count + ")");
            if (r.Points.Count == 2)
            {
                var pts = r.Points.OrderBy(p => p.X).ToList();
                double leftEnd = pts[0].X;
                double rightEnd = 21.61 - pts[1].X;
                double spacing = pts[1].X - pts[0].X;

                Check(Math.Abs(leftEnd - rightEnd) < 0.1,
                    $"end clearances are symmetrical: left={leftEnd:F2} ft, right={rightEnd:F2} ft");
                Check(Math.Abs(leftEnd - 5.40) < 0.1,
                    $"left clearance is centered (~5.40 ft): got {leftEnd:F2} ft");
                Check(Math.Abs(spacing - 10.81) < 0.1,
                    $"spacing between heads is uniform (~10.81 ft): got {spacing:F2} ft");
                Check(spacing >= 6.0, "spacing respects 6 ft NFPA 13 minimum");
                Check(leftEnd <= 7.0 && rightEnd <= 7.0, "end clearances respect S/2 NFPA 13 maximum");
            }
        }

        private static void TestSidewallObstacleNotchBridgedAndFlagged()
        {
            Console.WriteLine("Test: sidewall obstacle notch is bridged and colliding head is flagged as conflict");

            // A 30x15 room where the south wall (y=0) has an architectural column projection
            // from X=6.0 to X=9.0, depth=1.5 ft (y=0 to 1.5).
            List<double[]> polygonWithNotch = new List<double[]>
            {
                new double[] { 0, 0 },
                new double[] { 6.0, 0 },
                new double[] { 6.0, 1.5 },
                new double[] { 9.0, 1.5 },
                new double[] { 9.0, 0 },
                new double[] { 30, 0 },
                new double[] { 30, 15 },
                new double[] { 0, 15 }
            };

            PlacementRoomInput room = MakeRoom("NotchRoom", polygonWithNotch, 450.0, DevicePlacementBehavior.WallSidewall);
            room.Obstacles.Add(new ObstacleData
            {
                ElementId = "Col-Notch",
                Category = "OST_Columns",
                BoundingBox = new BoundingBox3DData(
                    new Point3DData(6.0, 0.0, 0.0),
                    new Point3DData(9.0, 1.5, 10.0))
            });

            PlacementInputSnapshot snap = new PlacementInputSnapshot();
            snap.Rooms.Add(room);
            BruteForceCalculationResult calcRes = Calc(snap);
            RoomCalculationResult result = calcRes.Rooms[0];

            Check(result.Status != CalculationStatus.Failed && result.Status != CalculationStatus.InvalidRoomGeometry,
                "calculation succeeds on room with obstacle notch");
            Check(result.Points != null && result.Points.Count > 0, "generates sprinkler points");

            // Verify that the South wall edge was bridged back to a 30 ft continuous wall
            // and produced a head near X=7.5 which lands directly on the column obstacle.
            CalculatedSprinklerPoint colHead = result.Points.FirstOrDefault(p => Math.Abs(p.X - 7.5) < 1.0 && p.Y < 2.0);
            Check(colHead != null, "head calculated directly at the column obstacle location on the continuous wall baseline");
            if (colHead != null)
            {
                Check(colHead.HasObstacleConflict, "head on obstacle is flagged HasObstacleConflict=true");
            }
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
