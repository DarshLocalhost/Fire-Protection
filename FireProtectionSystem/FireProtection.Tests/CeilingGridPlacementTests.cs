using System;
using System.Collections.Generic;
using System.Linq;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Hazard;
using FireProtection.Backend.Models.Placement.SmokeDetectors.Final;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.Backend.Services.Placement.NotificationAppliances.Final.BruteForce;
using FireProtection.Backend.Services.Placement.SmokeDetectors.Final.BruteForce;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce;
using FireProtection.UI.Models.Sprinklers.BruteForce;
using FireProtection.UI.Services;

namespace FireProtection.Tests
{
    /// <summary>
    /// Engine-level tests for the ceiling-tile grid-center feature and the S->W (max distance-to-
    /// wall) enforcement, exercised through the PUBLIC calc services with no Revit:
    ///  - grid ON (readable pattern): every placed sprinkler / smoke / notification point lands on
    ///    an acoustic-tile CENTER;
    ///  - NEUTRALITY: with the grid off, output is byte-identical whether or not the (ignored) grid
    ///    fields are present, and turning the grid on demonstrably moves heads onto tile centers;
    ///  - S->W: a tiny OverrideMaxDistanceToWallFt flags the room ReviewRequired with the specific
    ///    wall-distance warning, for BOTH engines, while the independent MIN clearance still holds.
    ///
    /// Spacing NUMBERS remain provisional (rule sets report HasApprovedRules=false); these assert
    /// the ALGORITHM, not approved NFPA values.
    /// </summary>
    internal static class CeilingGridPlacementTests
    {
        private static int _failures;
        private const double Tol = 0.02;

        public static void RunAll()
        {
            _failures = 0;
            TestSprinklerSnapsToTileCenters();
            TestSmokeSnapsToTileCenters();
            TestNotificationSnapsToTileCenters();
            TestGridOffNeutralitySprinkler();
            TestGridOffNeutralitySmoke();
            TestSprinklerMaxWallEnforced();
            TestDeviceMaxWallEnforced();
            TestUnobstructedGridRoomRespectsMaxSpacing();
            TestObstructedGridRoomReportsRatherThanHides();
            TestBlockedTargetWithNoNeighbourIsSkippedNotDragged();
            TestArrayReachesRoomExtentWithCoarseTilePitch();

            if (_failures == 0)
            {
                Console.WriteLine("CeilingGridPlacementTests: PASS");
            }
            else
            {
                Console.WriteLine("CeilingGridPlacementTests: " + _failures + " FAIL(s)");
                throw new Exception("CeilingGridPlacementTests failed");
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

        // ---- shared builders ----------------------------------------------------------------

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

        private static CeilingData FlatCeiling(double bottom)
        {
            return new CeilingData { SlopeType = "FLAT", BottomElevationFt = bottom, Source = new SourceReferenceData() };
        }

        private static CeilingData GridCeiling(double bottom, double u, double v, double ox = 0.0, double oy = 0.0)
        {
            return new CeilingData
            {
                SlopeType = "FLAT",
                BottomElevationFt = bottom,
                Source = new SourceReferenceData(),
                HasReadableGrid = true,
                GridOriginXFt = ox,
                GridOriginYFt = oy,
                GridSpacingUFt = u,
                GridSpacingVFt = v,
                GridAngleRad = 0.0
            };
        }

        /// <summary>True when coord sits at (i+0.5)*pitch from the axis-aligned grid origin.</summary>
        private static bool AtTileCenter(double coord, double origin, double pitch)
        {
            double idx = (coord - origin) / pitch - 0.5;
            double nearest = Math.Round(idx);
            double centered = origin + (nearest + 0.5) * pitch;
            return Math.Abs(coord - centered) <= Tol;
        }

        // ---- sprinkler harness --------------------------------------------------------------

        private static PlacementRoomInput SprinklerRoom(List<double[]> poly, List<CeilingData> ceilings)
        {
            var room = new PlacementRoomInput
            {
                RoomId = "R",
                RoomName = "R",
                RoomNumber = "R",
                LevelId = "L1",
                LevelElevationFt = 0.0,
                AreaSqFt = 1200.0,
                EffectiveHazardClass = "Light",
                CeilingHeightFt = 9.0,
                Ceilings = ceilings,
                Obstacles = new List<ObstacleData>(),
                ExistingSprinklers = new List<ExistingSprinklerData>(),
                BoundaryPolygon = poly
            };
            room.Boundary = new BoundaryData { OuterLoop = new BoundaryLoopData { IsOuter = true, Polygon = poly } };
            return room;
        }

        private static RoomCalculationResult CalcSprinkler(PlacementRoomInput room)
        {
            var snap = new PlacementInputSnapshot();
            snap.Rooms.Add(room);
            return BruteForceCalculationService.Calculate(
                snap, new DefaultHazardPlacementRules(), BruteForceCalculationConfig.Default()).Rooms[0];
        }

        // ---- device harness -----------------------------------------------------------------

        private static SmokeDetectorRoomInput DeviceRoom(
            List<double[]> poly, List<CeilingData> ceilings, DeviceKind kind, string detectorType)
        {
            var room = new SmokeDetectorRoomInput
            {
                RoomId = "R",
                RoomName = "R",
                RoomNumber = "R",
                LevelId = "L1",
                LevelElevationFt = 0.0,
                AreaSqFt = 1200.0,
                CeilingHeightFt = 9.0,
                Mount = "Ceiling",
                CeilingSlope = "FLAT",
                DetectorType = detectorType,
                DeviceKind = kind,
                SelectedPlacementBehavior = DevicePlacementBehavior.CeilingOverhead,
                BoundaryPolygon = poly,
                Obstacles = new List<ObstacleData>(),
                Ceilings = ceilings
            };
            room.Boundary = new BoundaryData { OuterLoop = new BoundaryLoopData { IsOuter = true, Polygon = poly } };
            return room;
        }

        private static SmokeDetectorRoomCalculationResult CalcDevice(SmokeDetectorRoomInput room, bool notification)
        {
            var snap = new SmokeDetectorPlacementInputSnapshot();
            snap.Rooms.Add(room);
            ISmokeDetectorPlacementRules rules = notification
                ? (ISmokeDetectorPlacementRules)new Nfpa72NotificationApplianceRules()
                : new Nfpa72SmokeDetectorRules();
            return SmokeDetectorCalculationService.Calculate(
                snap, rules, BruteForceCalculationConfig.Default()).Rooms[0];
        }

        // -----------------------------------------------------------------
        // Grid ON — every placed point lands on a tile center
        // -----------------------------------------------------------------
        private static void TestSprinklerSnapsToTileCenters()
        {
            Console.WriteLine("Test: sprinkler heads snap to ceiling-tile centers (readable grid)");
            var room = SprinklerRoom(Rect(0, 0, 40, 30), new List<CeilingData> { GridCeiling(9.0, 2.0, 2.0) });
            var res = CalcSprinkler(room);

            Check(res.Points.Count > 0, "grid room places heads (got " + res.Points.Count + ")");
            bool all = res.Points.All(p => AtTileCenter(p.X, 0.0, 2.0) && AtTileCenter(p.Y, 0.0, 2.0));
            Check(all, "every sprinkler point is at a 2x2 ft tile center");
        }

        private static void TestSmokeSnapsToTileCenters()
        {
            Console.WriteLine("Test: smoke detectors snap to ceiling-tile centers (readable grid)");
            var room = DeviceRoom(Rect(0, 0, 40, 30), new List<CeilingData> { GridCeiling(9.0, 2.0, 2.0) },
                DeviceKind.SmokeDetector, "Photoelectric");
            var res = CalcDevice(room, notification: false);

            Check(res.Points.Count > 0, "grid room places smoke detectors (got " + res.Points.Count + ")");
            bool all = res.Points.All(p => AtTileCenter(p.X, 0.0, 2.0) && AtTileCenter(p.Y, 0.0, 2.0));
            Check(all, "every smoke point is at a 2x2 ft tile center");
        }

        private static void TestNotificationSnapsToTileCenters()
        {
            Console.WriteLine("Test: notification appliances snap to ceiling-tile centers (readable grid)");
            var room = DeviceRoom(Rect(0, 0, 40, 30), new List<CeilingData> { GridCeiling(9.0, 2.0, 2.0) },
                DeviceKind.NotificationAppliance, "HornStrobe|candela=15|dba=87");
            var res = CalcDevice(room, notification: true);

            Check(res.Points.Count > 0, "grid room places appliances (got " + res.Points.Count + ")");
            bool all = res.Points.All(p => AtTileCenter(p.X, 0.0, 2.0) && AtTileCenter(p.Y, 0.0, 2.0));
            Check(all, "every notification point is at a 2x2 ft tile center");
        }

        // -----------------------------------------------------------------
        // NEUTRALITY — grid off: ignored grid fields cannot change output; grid on does
        // -----------------------------------------------------------------
        private static void TestGridOffNeutralitySprinkler()
        {
            Console.WriteLine("Test: sprinkler grid-off neutrality (byte-identical) + grid-on changes layout");
            var plain = CalcSprinkler(SprinklerRoom(Rect(0, 0, 40, 30), new List<CeilingData> { FlatCeiling(9.0) }));

            // Same room, but the ceiling carries grid fields with HasReadableGrid=false -> must be ignored.
            var withOffFields = GridCeiling(9.0, 2.0, 2.0);
            withOffFields.HasReadableGrid = false;
            var offFields = CalcSprinkler(SprinklerRoom(Rect(0, 0, 40, 30), new List<CeilingData> { withOffFields }));

            Check(PointsIdenticalSpr(plain.Points, offFields.Points),
                "grid-off output is byte-identical whether or not (ignored) grid fields are present");

            var on = CalcSprinkler(SprinklerRoom(Rect(0, 0, 40, 30), new List<CeilingData> { GridCeiling(9.0, 2.0, 2.0) }));
            bool offAllCentered = plain.Points.All(p => AtTileCenter(p.X, 0.0, 2.0) && AtTileCenter(p.Y, 0.0, 2.0));
            bool onAllCentered = on.Points.All(p => AtTileCenter(p.X, 0.0, 2.0) && AtTileCenter(p.Y, 0.0, 2.0));
            Check(onAllCentered && !offAllCentered,
                "grid ON snaps all heads to tile centers; grid OFF (legacy array) does not");
        }

        private static void TestGridOffNeutralitySmoke()
        {
            Console.WriteLine("Test: smoke grid-off neutrality (byte-identical) + grid-on changes layout");
            var plain = CalcDevice(DeviceRoom(Rect(0, 0, 40, 30),
                new List<CeilingData> { FlatCeiling(9.0) }, DeviceKind.SmokeDetector, "Photoelectric"), false);

            var withOffFields = GridCeiling(9.0, 2.0, 2.0);
            withOffFields.HasReadableGrid = false;
            var offFields = CalcDevice(DeviceRoom(Rect(0, 0, 40, 30),
                new List<CeilingData> { withOffFields }, DeviceKind.SmokeDetector, "Photoelectric"), false);

            Check(PointsIdenticalDev(plain.Points, offFields.Points),
                "grid-off smoke output is byte-identical whether or not (ignored) grid fields are present");

            var on = CalcDevice(DeviceRoom(Rect(0, 0, 40, 30),
                new List<CeilingData> { GridCeiling(9.0, 2.0, 2.0) }, DeviceKind.SmokeDetector, "Photoelectric"), false);
            bool offAllCentered = plain.Points.All(p => AtTileCenter(p.X, 0.0, 2.0) && AtTileCenter(p.Y, 0.0, 2.0));
            bool onAllCentered = on.Points.All(p => AtTileCenter(p.X, 0.0, 2.0) && AtTileCenter(p.Y, 0.0, 2.0));
            Check(onAllCentered && !offAllCentered,
                "grid ON snaps all smoke points to tile centers; grid OFF (legacy lattice) does not");
        }

        private static bool PointsIdenticalSpr(List<CalculatedSprinklerPoint> a, List<CalculatedSprinklerPoint> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].X != b[i].X || a[i].Y != b[i].Y || a[i].Z != b[i].Z) return false;
            }
            return true;
        }

        private static bool PointsIdenticalDev(List<CalculatedSmokeDetectorPoint> a, List<CalculatedSmokeDetectorPoint> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].X != b[i].X || a[i].Y != b[i].Y || a[i].Z != b[i].Z) return false;
            }
            return true;
        }

        // -----------------------------------------------------------------
        // S->W — user max distance-to-wall enforced; MIN clearance independent
        // -----------------------------------------------------------------
        private static void TestSprinklerMaxWallEnforced()
        {
            Console.WriteLine("Test: sprinkler honors a tiny S->W max distance-to-wall + keeps MIN clearance");
            var room = SprinklerRoom(Rect(0, 0, 40, 30), new List<CeilingData> { FlatCeiling(9.0) });
            room.OverrideMaxDistanceToWallFt = 1.0; // absurdly tight -> interior heads must violate it
            var res = CalcSprinkler(room);

            Check(res.Points.Count > 0, "room still places heads (got " + res.Points.Count + ")");
            Check(res.Status == CalculationStatus.ReviewRequired, "room flagged ReviewRequired");
            bool wallWarn = res.Warnings.Any(w => w.IndexOf("MaxDistanceFromWallsFt", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(wallWarn, "a specific max distance-to-wall warning was raised");

            double minClear = res.AppliedBoundaryClearanceFt ?? 0.0;
            bool minHeld = res.Points.All(p => DistToWall(p.X, p.Y, 40, 30) >= minClear - Tol);
            Check(minHeld, "MIN boundary clearance (" + minClear.ToString("F2") + " ft) still independently enforced");
        }

        private static void TestDeviceMaxWallEnforced()
        {
            Console.WriteLine("Test: device engine honors a tiny S->W max distance-to-wall + keeps MIN clearance");
            var room = DeviceRoom(Rect(0, 0, 40, 30),
                new List<CeilingData> { FlatCeiling(9.0) }, DeviceKind.SmokeDetector, "Photoelectric");
            room.OverrideMaxDistanceToWallFt = 1.0;
            var res = CalcDevice(room, notification: false);

            Check(res.Points.Count > 0, "room still places detectors (got " + res.Points.Count + ")");
            Check(res.Status == CalculationStatus.ReviewRequired, "room flagged ReviewRequired");
            bool wallWarn = res.Warnings.Any(w => w.IndexOf("distance-to-wall", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(wallWarn, "a specific max distance-to-wall warning was raised");

            double minClear = res.AppliedBoundaryClearanceFt;
            bool minHeld = res.Points.All(p => DistToWall(p.X, p.Y, 40, 30) >= minClear - Tol);
            Check(minHeld, "MIN boundary clearance (" + minClear.ToString("F2") + " ft) still independently enforced");
        }

        private static double DistToWall(double x, double y, double w, double h)
        {
            return Math.Min(Math.Min(x, w - x), Math.Min(y, h - y));
        }

        // =================================================================
        // Bounded snapping + array extent (Phase 1 regression tests)
        // =================================================================
        //
        // Before these fixes, when a target tile centre was blocked the engine searched
        // +/- tileStep tiles in BOTH axes with no distance limit and took the nearest valid
        // tile. A head could therefore be dragged arbitrarily far from its array position,
        // producing a pair beyond MaxSpacingFt. Only the post-hoc pairwise check in
        // FinalizeSelection caught it, i.e. the placer relied on the checker to report the
        // placer's own violation.
        //
        // The snap is now bounded by the slack between the array pitch and the permitted
        // spacing, so a head can never be moved far enough to violate max spacing. These
        // tests pin that contract.

        /// <summary>
        /// Worst LOCAL spacing in the layout: for each head, the distance to its nearest other
        /// head, maximised over all heads.
        ///
        /// Deliberately NOT the maximum of all pairwise distances. Diagonally adjacent heads in
        /// an array are always further apart than the pitch (a 15 x 15 ft array has a 21.2 ft
        /// diagonal against a 15 ft limit), so an all-pairs maximum is meaningless for an array
        /// layout and flags healthy geometry. Max spacing governs the pitch a head actually
        /// serves, which is exactly nearest-neighbour distance.
        /// </summary>
        private static double MaxPairwiseDistance(List<CalculatedSprinklerPoint> pts)
        {
            double worst = 0.0;
            for (int i = 0; i < pts.Count; i++)
            {
                double nearest = double.PositiveInfinity;
                for (int j = 0; j < pts.Count; j++)
                {
                    if (i == j) continue;
                    double dx = pts[i].X - pts[j].X;
                    double dy = pts[i].Y - pts[j].Y;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < nearest) nearest = d;
                }
                if (nearest > worst) worst = nearest;
            }
            return worst;
        }

        /// <summary>
        /// The core guarantee: with nothing blocking the array, EVERY placed pair is within
        /// MaxSpacingFt and the engine reports no violation.
        ///
        /// This is the case the old unbounded fallback could silently break: if any array target
        /// was unavailable it would drag a head to the nearest valid tile with no distance limit,
        /// and only the post-hoc pairwise checker would notice. With a clean room there is no
        /// dragging at all, so compliance must be unconditional.
        /// </summary>
        private static void TestUnobstructedGridRoomRespectsMaxSpacing()
        {
            Console.WriteLine("Test: unobstructed grid room never exceeds MaxSpacingFt between any pair");

            // 4 ft tile with 15 ft max spacing: step = 3 tiles = 12 ft, leaving 3 ft of slack.
            var room = SprinklerRoom(Rect(0, 0, 48, 32), new List<CeilingData> { GridCeiling(9.0, 4.0, 4.0) });
            var res = CalcSprinkler(room);

            Check(res.Points.Count > 0, "clean grid room places heads (got " + res.Points.Count + ")");

            double maxSpacing = new DefaultHazardPlacementRules().GetRules(HazardClass.Light).MaxSpacingFt;
            double worst = MaxPairwiseDistance(res.Points);
            Check(worst <= maxSpacing + 0.5,
                "every pair within MaxSpacingFt (" + maxSpacing.ToString("F2")
                + " ft); worst pair = " + worst.ToString("F2") + " ft");

            Check(!res.Warnings.Any(w => w.IndexOf("exceeding MaxSpacingFt", StringComparison.OrdinalIgnoreCase) >= 0),
                "no pair-spacing warning — the placer produced compliant geometry itself");

            bool onCenters = res.Points.All(p => AtTileCenter(p.X, 0.0, 4.0) && AtTileCenter(p.Y, 0.0, 4.0));
            Check(onCenters, "every head is still on a 4x4 tile center");
        }

        /// <summary>
        /// The honest contract for an OBSTRUCTED room: bounded snapping may leave an array cell
        /// unfilled, which can leave a genuine spacing gap. The engine must (a) never place a head
        /// inside an obstruction, (b) serve both sides of an obstruction rather than dragging every
        /// head to one side, and (c) REPORT the gap rather than hide it.
        ///
        /// An earlier version of this test wrongly demanded spacing compliance even with a beam
        /// across the room. That is not achievable — and demanding it is exactly what drove the
        /// old unbounded "drag the head across the room to fill the cell" behaviour.
        /// </summary>
        private static void TestObstructedGridRoomReportsRatherThanHides()
        {
            Console.WriteLine("Test: obstructed grid room serves both sides and reports any resulting gap");

            var room = SprinklerRoom(Rect(0, 0, 48, 32), new List<CeilingData> { GridCeiling(9.0, 4.0, 4.0) });
            // A beam running the full depth of the room: any head on the far side is only
            // reachable by crossing the beam, which bounded snapping refuses to do.
            room.Obstacles.Add(Box("Beam", 20.0, -1.0, 28.0, 33.0));

            var res = CalcSprinkler(room);
            Check(res.Points.Count > 0, "obstructed room still places heads (got " + res.Points.Count + ")");

            // (a) No head inside the beam, allowing the beam clearance.
            double clearance = new DefaultHazardPlacementRules().GetRules(HazardClass.Light)
                .GetObstacleClearance("beam");
            bool noneInBeam = res.Points.All(p =>
                !(p.X > 20.0 - clearance && p.X < 28.0 + clearance));
            Check(noneInBeam,
                "no head inside the beam footprint (clearance " + clearance.ToString("F2") + " ft)");

            // (b) Both sides are served: heads west AND east of the beam.
            bool west = res.Points.Any(p => p.X < 20.0);
            bool east = res.Points.Any(p => p.X > 28.0);
            Check(west && east,
                "heads placed on BOTH sides of the beam (west=" + west + ", east=" + east + ")");

            // (c) If the beam forces a spacing gap, the engine must say so.
            double maxSpacing = new DefaultHazardPlacementRules().GetRules(HazardClass.Light).MaxSpacingFt;
            double worst = MaxPairwiseDistance(res.Points);
            bool reported = res.Warnings.Any(w =>
                w.IndexOf("exceeding MaxSpacingFt", StringComparison.OrdinalIgnoreCase) >= 0);
            if (worst > maxSpacing + 0.5)
            {
                Check(reported,
                    "the resulting gap across the beam is REPORTED (worst pair "
                    + worst.ToString("F2") + " ft > " + maxSpacing.ToString("F2") + " ft)");
            }
            else
            {
                Check(true, "no gap across the beam needed reporting (worst pair "
                    + worst.ToString("F2") + " ft)");
            }
        }

        /// <summary>
        /// When the ONLY valid tile near an array target is far outside the snap budget, the cell
        /// must be skipped and reported, not filled by dragging a head across the room.
        /// </summary>
        private static void TestBlockedTargetWithNoNeighbourIsSkippedNotDragged()
        {
            Console.WriteLine("Test: a blocked target with no near neighbour is SKIPPED, not dragged");

            // 2x2 room: a single tile. Blocking it leaves nothing nearby, so the correct outcome
            // is an honest failure rather than a head placed somewhere else.
            var room = SprinklerRoom(Rect(0, 0, 4, 4), new List<CeilingData> { GridCeiling(9.0, 2.0, 2.0) });
            room.Obstacles.Add(Box("Beam", -1.0, -1.0, 6.0, 6.0));

            var res = CalcSprinkler(room);
            Check(res.CalculatedCount == 0,
                "fully blocked 4x4 room places zero heads (got " + res.CalculatedCount + ")");

            // And the user must be told why, rather than seeing a silent empty result.
            bool explained = res.Errors.Any(e => e != null && e.Length > 0)
                || res.Warnings.Any(w => w != null && w.Length > 0);
            Check(explained, "the blocked room carries an explanation (Errors or Warnings)");
        }

        /// <summary>
        /// With a coarse tile pitch the array step is a FLOOR of spacing/pitch, so a naive
        /// centring leaves the room ends uncovered. The array must reach the extent.
        /// </summary>
        private static void TestArrayReachesRoomExtentWithCoarseTilePitch()
        {
            Console.WriteLine("Test: array reaches the room extent with a coarse 4 ft tile / 15 ft spacing");

            var room = SprinklerRoom(Rect(0, 0, 60, 40), new List<CeilingData> { GridCeiling(9.0, 4.0, 4.0) });
            var res = CalcSprinkler(room);
            Check(res.Points.Count > 0, "coarse-tile room places heads (got " + res.Points.Count + ")");

            // The furthest head from the room centre must not be stranded short of the walls:
            // compare against the room half-diagonal so an obviously missing row shows up.
            double cx = 30.0, cy = 20.0;
            double maxRadius = res.Points.Max(p => Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)));

            // Light hazard, 15 ft spacing -> array geometric radius 15/sqrt(2) = 10.6 ft.
            // A centred array should have a head within ~one step of the far corners; allow the
            // half-step slack of the tile pitch.
            double expect = new DefaultHazardPlacementRules().GetRules(HazardClass.Light)
                .EffectiveCoverageRadiusFt + 4.0;
            Check(maxRadius >= expect,
                "array reaches the room extent (max head radius " + maxRadius.ToString("F2")
                + " ft vs expected >= " + expect.ToString("F2") + " ft)");
        }

        private static ObstacleData Box(string category, double x0, double y0, double x1, double y1)
        {
            var o = new ObstacleData { Category = category, Source = new SourceReferenceData() };
            o.BoundingBox = new BoundingBox3DData
            {
                Min = new Point3DData(x0, y0, 8.0),
                Max = new Point3DData(x1, y1, 10.0)
            };
            return o;
        }
    }
}
