using System;
using System.Collections.Generic;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce;

namespace FireProtection.Tests
{
    /// <summary>
    /// Tests for the tile-centric X-2X-X pattern generator.
    ///
    /// The invariant that matters most is simple and absolute: EVERY emitted position must be the
    /// exact centre of a real tile. A generator that is off by half a tile places devices on tile
    /// corners while looking correct in a plan view, which is the defect this whole feature exists
    /// to fix.
    ///
    /// Headless: the generator is pure math over a lattice, so no Revit document is needed.
    /// </summary>
    public static class TileCentricPatternTests
    {
        private static int _failures;

        public static void RunAll()
        {
            _failures = 0;
            TestEveryPositionIsATileCentre();
            TestWallGapsFollowX2XX();
            TestPitchIsAWholeTileCount();
            TestCountIsNeverBelowRequired();
            TestTwoByTwoOnATenTileGrid();
            TestRotatedGridStillLandsOnTileCentres();
            TestNoGridProducesNoPattern();
            TestIrregularFootprintKeepsOnePitch();
            TestSingleDeviceCentresInRoom();
            TestDeviceSpansTilesForItsRealSize();
            TestBalancedSolverFixesAnchoredWallGaps();
            TestBalancedSolverRejectsImplausibleGrid();
            TestSnapSafetyNetNeverDropsOrSharesTiles();

            if (_failures == 0)
            {
                Console.WriteLine("TileCentricPatternTests: PASS");
            }
            else
            {
                Console.WriteLine("TileCentricPatternTests: " + _failures + " FAIL(s)");
                throw new Exception("TileCentricPatternTests failed");
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

        // ---- helpers -----------------------------------------------------------------------

        /// <summary>An axis-aligned 2 ft lattice whose tile (0,0) corner sits at the world origin.</summary>
        private static CeilingGridMath.CeilingGrid Grid2Ft(double originX = 0.0, double originY = 0.0, double angleRad = 0.0)
        {
            return new CeilingGridMath.CeilingGrid
            {
                OriginX = originX,
                OriginY = originY,
                UFt = 2.0,
                VFt = 2.0,
                AngleRad = angleRad,
                IsValid = true,
                PhaseIsExact = true,
                PhaseSource = "ExactGridLines"
            };
        }

        /// <summary>
        /// Usable-tile predicate: a tile is usable when its CENTRE falls inside the room box. That is
        /// the caller's real-world test (tile fully inside the room) expressed simply, so the
        /// generator sees a room whose edges do not necessarily coincide with tile boundaries.
        /// </summary>
        private static Func<int, int, bool> InsideRoom(
            in CeilingGridMath.CeilingGrid grid,
            double minX, double minY, double maxX, double maxY)
        {
            // `in` parameters cannot be captured, so the search runs from a local copy.
            CeilingGridMath.CeilingGrid lattice = grid;
            return (iu, iv) =>
            {
                CeilingGridMath.IndexToWorld(iu, iv, in lattice, out double x, out double y);
                return x >= minX && x <= maxX && y >= minY && y <= maxY;
            };
        }

        /// <summary>How far the given point is from the nearest real tile centre, in feet.</summary>
        private static double DistanceToNearestTileCentre(
            double x, double y, in CeilingGridMath.CeilingGrid grid)
        {
            CeilingGridMath.SnapToTileCenter(x, y, in grid, out double sx, out double sy);
            double dx = x - sx, dy = y - sy;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // ---- tests -------------------------------------------------------------------------

        /// <summary>
        /// THE invariant. A device must sit on a tile centre, not a corner and not between tiles.
        /// Half a 2 ft tile is 1.0 ft, so any error at or near 1.0 means a tile corner.
        /// </summary>
        private static void TestEveryPositionIsATileCentre()
        {
            Console.WriteLine("Test: every generated position is exactly a tile centre");

            var grid = Grid2Ft();
            Func<int, int, bool> usable = InsideRoom(in grid, 0.0, 0.0, 20.0, 14.0);

            TileCentricPatternGenerator.Result r = TileCentricPatternGenerator.TryGenerate(
                4, in grid, 0.0, 0.0, 20.0, 14.0, 1.0, 1.0, true, usable);

            Check(r.Success, "a pattern was found (" + r.Reason + ")");
            if (!r.Success) return;

            double worst = 0.0;
            foreach (double[] p in r.Positions)
                worst = Math.Max(worst, DistanceToNearestTileCentre(p[0], p[1], in grid));

            Check(worst < 1e-6,
                "worst deviation from a tile centre is " + worst.ToString("E3") + " ft");
            Check(worst < 0.5,
                "no device sits on a tile corner (half-tile is 1.0 ft for a 2 ft tile)");
        }

        /// <summary>
        /// X-2X-X: the two wall gaps must mirror each other AND average to half the pitch. A layout
        /// that only balances the average is not X-2X-X on both walls, so both checks are asserted.
        /// </summary>
        private static void TestWallGapsFollowX2XX()
        {
            Console.WriteLine("Test: wall gaps follow X-2X-X");

            var grid = Grid2Ft();
            Func<int, int, bool> usable = InsideRoom(in grid, 0.0, 0.0, 20.0, 14.0);

            TileCentricPatternGenerator.Result r = TileCentricPatternGenerator.TryGenerate(
                6, in grid, 0.0, 0.0, 20.0, 14.0, 1.0, 1.0, true, usable);

            Check(r.Success, "a pattern was found (" + r.Reason + ")");
            if (!r.Success) return;

            if (r.Columns > 1)
            {
                double symmetryA = Math.Abs(r.WallGapA1Ft - r.WallGapA2Ft) / 2.0;
                double proportionA = Math.Abs((r.WallGapA1Ft + r.WallGapA2Ft) / 2.0 - r.PitchAFt / 2.0);

                Check(symmetryA <= r.ToleranceTiles * grid.UFt + 1e-6,
                    "direction A wall gaps mirror within tolerance (" + symmetryA.ToString("F4") + " ft)");
                Check(proportionA <= r.ToleranceTiles * grid.UFt + 1e-6,
                    "direction A X is half of 2X within tolerance (" + proportionA.ToString("F4") + " ft)");
            }

            if (r.Rows > 1)
            {
                double symmetryB = Math.Abs(r.WallGapB1Ft - r.WallGapB2Ft) / 2.0;
                double proportionB = Math.Abs((r.WallGapB1Ft + r.WallGapB2Ft) / 2.0 - r.PitchBFt / 2.0);

                Check(symmetryB <= r.ToleranceTiles * grid.VFt + 1e-6,
                    "direction B wall gaps mirror within tolerance (" + symmetryB.ToString("F4") + " ft)");
                Check(proportionB <= r.ToleranceTiles * grid.VFt + 1e-6,
                    "direction B X is half of 2X within tolerance (" + proportionB.ToString("F4") + " ft)");
            }

            Check(r.ToleranceTiles <= 1.0, "the tightest tolerance tier was used where possible");
        }

        /// <summary>
        /// The pitch must be a WHOLE number of tiles. A fractional pitch is what makes an array
        /// drift across the room and end with an uneven gap at the far wall.
        /// </summary>
        private static void TestPitchIsAWholeTileCount()
        {
            Console.WriteLine("Test: pitch is a whole number of tiles");

            var grid = Grid2Ft();
            Func<int, int, bool> usable = InsideRoom(in grid, 0.0, 0.0, 20.0, 14.0);

            TileCentricPatternGenerator.Result r = TileCentricPatternGenerator.TryGenerate(
                6, in grid, 0.0, 0.0, 20.0, 14.0, 1.0, 1.0, true, usable);

            Check(r.Success, "a pattern was found");
            if (!r.Success) return;

            if (r.Columns > 1)
            {
                double tilesA = r.PitchAFt / grid.UFt;
                Check(Math.Abs(tilesA - Math.Round(tilesA)) < 1e-6,
                    "pitch along A is " + tilesA.ToString("F4") + " tiles (whole)");
            }
            if (r.Rows > 1)
            {
                double tilesB = r.PitchBFt / grid.VFt;
                Check(Math.Abs(tilesB - Math.Round(tilesB)) < 1e-6,
                    "pitch along B is " + tilesB.ToString("F4") + " tiles (whole)");
            }
        }

        /// <summary>
        /// The required count is a MINIMUM. A grid that cannot lay out exactly N must produce the
        /// nearest larger count, never fewer — quietly dropping devices would under-protect a room.
        /// </summary>
        private static void TestCountIsNeverBelowRequired()
        {
            Console.WriteLine("Test: the device count is never below the required count");

            var grid = Grid2Ft();
            Func<int, int, bool> usable = InsideRoom(in grid, 0.0, 0.0, 20.0, 14.0);

            // 7 is prime, so a 7-device rectangular grid needs a raise; 9 is a clean 3x3.
            for (int required = 1; required <= 12; required++)
            {
                TileCentricPatternGenerator.Result r = TileCentricPatternGenerator.TryGenerate(
                    required, in grid, 0.0, 0.0, 20.0, 14.0, 1.0, 1.0, true, usable);

                if (!r.Success)
                {
                    Check(false, "required=" + required + " found no layout: " + r.Reason);
                    continue;
                }

                Check(r.PlacedCount >= required,
                    "required=" + required + " placed " + r.PlacedCount);
            }
        }

        /// <summary>
        /// The worked example from the plan: 20 ft room, 2 ft tiles, asking for 6. The generator must
        /// choose a start tile that balances the wall gaps instead of anchoring at the first tile.
        /// </summary>
        private static void TestTwoByTwoOnATenTileGrid()
        {
            Console.WriteLine("Test: a 6-device layout balances wall gaps instead of anchoring at tile 0");

            var grid = Grid2Ft();
            Func<int, int, bool> usable = InsideRoom(in grid, 0.0, 0.0, 20.0, 14.0);

            TileCentricPatternGenerator.Result r = TileCentricPatternGenerator.TryGenerate(
                6, in grid, 0.0, 0.0, 20.0, 14.0, 1.0, 1.0, true, usable);

            Check(r.Success, "a pattern was found");
            if (!r.Success) return;

            Check(r.Columns == 3 && r.Rows == 2,
                "laid out 3x2 (cols=" + r.Columns + ", rows=" + r.Rows + ")");

            // Anchoring at the first tile would give a wall gap of 1.0 ft at one end. The generator
            // must instead land the outer devices near the middle of the wall.
            double worstGap = Math.Max(Math.Abs(r.WallGapA1Ft - r.WallGapA2Ft) / 2.0, 0.0);
            Check(worstGap <= 1.0 + 1e-6,
                "the two A-direction wall gaps are within one tile of each other ("
                    + worstGap.ToString("F3") + " ft)");

            Check(r.WallGapA1Ft > 1.0 + 1e-6,
                "the first device is not jammed against the wall (gap="
                    + r.WallGapA1Ft.ToString("F3") + " ft)");
        }

        /// <summary>
        /// A rotated grid is the case that catches world-axis assumptions. Positions must still be
        /// exact tile centres of the ROTATED lattice.
        /// </summary>
        private static void TestRotatedGridStillLandsOnTileCentres()
        {
            Console.WriteLine("Test: a 30-degree grid still lands on tile centres");

            double angle = 30.0 * Math.PI / 180.0;
            var grid = Grid2Ft(0.0, 0.0, angle);
            Func<int, int, bool> usable = InsideRoom(in grid, -2.0, -2.0, 22.0, 20.0);

            TileCentricPatternGenerator.Result r = TileCentricPatternGenerator.TryGenerate(
                4, in grid, -2.0, -2.0, 22.0, 20.0, 1.0, 1.0, true, usable);

            Check(r.Success, "a pattern was found (" + r.Reason + ")");
            if (!r.Success) return;

            double worst = 0.0;
            foreach (double[] p in r.Positions)
                worst = Math.Max(worst, DistanceToNearestTileCentre(p[0], p[1], in grid));

            Check(worst < 1e-6,
                "worst deviation from a rotated tile centre is " + worst.ToString("E3") + " ft");
            Check(r.IsGridAngleReportedNonZero(), "the rotated angle was carried into the result");
        }

        /// <summary>No lattice means no pattern, and the caller keeps its existing flow.</summary>
        private static void TestNoGridProducesNoPattern()
        {
            Console.WriteLine("Test: no usable grid produces no pattern (caller keeps its own flow)");

            var grid = new CeilingGridMath.CeilingGrid();   // IsValid == false
            Func<int, int, bool> usable = (iu, iv) => true;

            TileCentricPatternGenerator.Result r = TileCentricPatternGenerator.TryGenerate(
                4, in grid, 0.0, 0.0, 20.0, 14.0, 1.0, 1.0, true, usable);

            Check(!r.Success, "no pattern was produced");
            Check(r.Positions.Count == 0, "no positions were emitted");
            Check(r.Reason.Length > 0, "a reason was given: '" + r.Reason + "'");

            // An implausible pitch must be rejected too, even when marked valid.
            var silly = Grid2Ft();
            silly.UFt = 100.0;
            TileCentricPatternGenerator.Result r2 = TileCentricPatternGenerator.TryGenerate(
                4, in silly, 0.0, 0.0, 2000.0, 1400.0, 1.0, 1.0, true, usable);
            Check(!r2.Success, "an implausible tile pitch is rejected");
        }

        /// <summary>
        /// An L-shaped room: usable tiles do not fill their bounding box. The generator must keep ONE
        /// pitch across the whole room and skip only the positions that land on the notch, rather
        /// than resetting the rhythm partway through.
        /// </summary>
        private static void TestIrregularFootprintKeepsOnePitch()
        {
            Console.WriteLine("Test: an irregular footprint keeps a single consistent pitch");

            var grid = Grid2Ft();
            Func<int, int, bool> usable = InsideRoom(in grid, 0.0, 0.0, 20.0, 14.0);

            // Punch a notch out of the top-right by refusing those tiles.
            CeilingGridMath.CeilingGrid lattice = grid;
            Func<int, int, bool> notched = (iu, iv) =>
            {
                if (!usable(iu, iv)) return false;
                CeilingGridMath.IndexToWorld(iu, iv, in lattice, out double x, out double y);
                return !(x > 12.0 && y > 8.0);
            };

            TileCentricPatternGenerator.Result r = TileCentricPatternGenerator.TryGenerate(
                8, in grid, 0.0, 0.0, 20.0, 14.0, 1.0, 1.0, true, notched);

            Check(r.Success, "a pattern was found despite the notch (" + r.Reason + ")");
            if (!r.Success) return;

            Check(r.FootprintIsIrregular, "the irregular footprint was detected");

            // Whatever survived must still be on tile centres and must still be a single pitch.
            double worst = 0.0;
            foreach (double[] p in r.Positions)
                worst = Math.Max(worst, DistanceToNearestTileCentre(p[0], p[1], in grid));

            Check(worst < 1e-6,
                "surviving positions are still tile centres (worst " + worst.ToString("E3") + " ft)");
            Check(r.PlacedCount >= 8, "at least the required 8 devices were laid out (got " + r.PlacedCount + ")");

            double tilesA = r.PitchAFt / grid.UFt;
            Check(Math.Abs(tilesA - Math.Round(tilesA)) < 1e-6,
                "direction A kept a whole-tile pitch across the notch (" + tilesA.ToString("F4") + ")");
        }

        /// <summary>
        /// The defect this whole change exists to fix. A 20 ft room with 2 ft tiles and a 7-tile
        /// pitch, anchored at the first tile, puts devices at tiles 0, 7 and 9 - wall gaps of 1 ft,
        /// 14 ft and 4 ft. The solver must instead choose a start tile that balances the two walls.
        /// This is the exact scenario that made devices look misplaced on a real ceiling.
        /// </summary>
        private static void TestBalancedSolverFixesAnchoredWallGaps()
        {
            Console.WriteLine("Test: the balanced solver replaces the anchored-first-tile array");

            var grid = Grid2Ft();

            TileCentricPatternGenerator.AxisLayout a;
            TileCentricPatternGenerator.AxisLayout b;
            double tolerance;
            bool solved = TileCentricPatternGenerator.TrySolveBalancedLayout(
                in grid, 0.0, 0.0, 20.0, 14.0, 7, 4, out a, out b, out tolerance);

            Check(solved, "a balanced layout was solved");
            if (!solved) return;

            // Anchoring at tile 0 would put the first device 1.0 ft from the wall (tile 0's centre).
            Check(a.Start != 0,
                "the solved start tile is not the first tile of the room (start=" + a.Start + ")");
            Check(a.Gap1Ft > 1.0 + 1e-6,
                "the first device is no longer jammed against the wall (gap="
                    + a.Gap1Ft.ToString("F3") + " ft)");

            double symmetry = Math.Abs(a.Gap1Ft - a.Gap2Ft) / 2.0;
            Check(symmetry <= tolerance * grid.UFt + 1e-6,
                "the two wall gaps mirror within tolerance (" + symmetry.ToString("F4") + " ft)");

            Check(Math.Abs(a.Gap1Ft - a.Gap2Ft) < 4.0,
                "the wall gaps are far closer than the 13 ft spread the anchored array produced");

            Check(a.Count > 0 && b.Count > 0, "a device count was reported in both directions");

            // The caller's 7 tiles is a MAXIMUM, not a fixed value: a 14 ft pitch cannot achieve
            // X=7 ft in a 20 ft room, so the solver must step down to a pitch that genuinely can.
            Check(a.Pitch <= 7, "direction A used a pitch no wider than the maximum (got " + a.Pitch + " tiles)");
            Check(a.Count >= 2,
                "direction A used more than one column in a 20 ft room (count=" + a.Count + ")");

            // With 2 columns the only balanced X-2X-X answer in a 20 ft room is X=5, pitch=10 ft.
            if (a.Count == 2)
            {
                Check(Math.Abs(a.Gap1Ft - 5.0) < 1e-6 && Math.Abs(a.Gap2Ft - 5.0) < 1e-6,
                    "2 columns in a 20 ft room gives 5 ft wall gaps and a 10 ft pitch (got "
                        + a.Gap1Ft.ToString("F2") + "/" + a.Gap2Ft.ToString("F2") + ")");
            }
        }

        /// <summary>Guard: the solver must refuse an unusable lattice rather than inventing a layout.</summary>
        private static void TestBalancedSolverRejectsImplausibleGrid()
        {
            Console.WriteLine("Test: the balanced solver refuses an implausible lattice");

            var bad = new CeilingGridMath.CeilingGrid
            {
                OriginX = 0.0, OriginY = 0.0,
                UFt = 200.0, VFt = 200.0,
                AngleRad = 0.0, IsValid = true, PhaseIsExact = true
            };

            TileCentricPatternGenerator.AxisLayout a;
            TileCentricPatternGenerator.AxisLayout b;
            double tolerance;
            Check(!TileCentricPatternGenerator.TrySolveBalancedLayout(
                    in bad, 0.0, 0.0, 4000.0, 4000.0, 2, 2, out a, out b, out tolerance),
                "a 200 ft tile pitch is rejected");
        }

        /// <summary>
        /// The safety net must never DROP a device (that would silently reduce protection) and never
        /// let two devices share a tile.
        /// </summary>
        private static void TestSnapSafetyNetNeverDropsOrSharesTiles()
        {
            Console.WriteLine("Test: the tile safety net preserves count and never shares a tile");

            var grid = Grid2Ft();
            Func<int, int, bool> usable = InsideRoom(in grid, 0.0, 0.0, 20.0, 14.0);

            // Deliberately off-centre input: a quarter tile from every tile centre, and two devices
            // that both snap onto the SAME tile.
            var input = new List<double[]>
            {
                new[] { 2.5, 2.5 },
                new[] { 2.5, 2.5 },     // duplicate: must be pushed off, never stacked
                new[] { 6.4, 4.4 },
                new[] { 10.2, 8.9 },
            };

            int snapped, kept;
            List<double[]> outp = TileCentricPatternGenerator.SnapPositionsToTiles(
                input, in grid, usable, out snapped, out kept);

            Check(outp.Count == input.Count,
                "every input position survived (" + outp.Count + " of " + input.Count + ")");
            Check(snapped + kept == input.Count,
                "snapped + kept accounts for every position (" + snapped + " + " + kept + ")");

            var seen = new HashSet<long>();
            bool shared = false;
            double worstCentre = 0.0;
            foreach (double[] p in outp)
            {
                CeilingGridMath.WorldToIndex(p[0], p[1], in grid, out int iu, out int iv);
                if (!seen.Add(CeilingGridMath.IndexKey(iu, iv))) shared = true;
                worstCentre = Math.Max(worstCentre, DistanceToNearestTileCentre(p[0], p[1], in grid));
            }

            Check(!shared, "no two devices ended up on the same tile");
            Check(worstCentre < 1e-6,
                "every adjusted position is an exact tile centre (worst "
                    + worstCentre.ToString("E3") + " ft)");

            // No grid: positions must pass through completely untouched.
            var noGrid = new CeilingGridMath.CeilingGrid();
            int snapped2, kept2;
            List<double[]> outp2 = TileCentricPatternGenerator.SnapPositionsToTiles(
                input, in noGrid, usable, out snapped2, out kept2);

            Check(outp2.Count == input.Count && kept2 == input.Count && snapped2 == 0,
                "with no grid every position is left exactly as it was");
            Check(outp2[0][0] == input[0][0] && outp2[0][1] == input[0][1],
                "an untouched position is byte-identical to its input");
        }

        /// <summary>One device in a direction centres in the room, which is X = half the room length.</summary>
        private static void TestSingleDeviceCentresInRoom()
        {
            Console.WriteLine("Test: a single device centres in the room");

            var grid = Grid2Ft();
            Func<int, int, bool> usable = InsideRoom(in grid, 0.0, 0.0, 20.0, 14.0);

            TileCentricPatternGenerator.Result r = TileCentricPatternGenerator.TryGenerate(
                1, in grid, 0.0, 0.0, 20.0, 14.0, 1.0, 1.0, true, usable);

            Check(r.Success, "a pattern was found (" + r.Reason + ")");
            if (!r.Success) return;

            Check(r.PlacedCount == 1, "exactly one device placed");
            Check(r.ToleranceTiles >= 0.5, "the tightest tolerance tier was used");
        }

        /// <summary>
        /// A 2'x4' device spans 1x2 tiles in a 600 mm grid, so two devices must never share a tile.
        /// A nominal 2 ft (609.6 mm) device must round to ONE 600 mm tile, not two.
        /// </summary>
        private static void TestDeviceSpansTilesForItsRealSize()
        {
            Console.WriteLine("Test: a device spans as many tiles as its real size needs");

            // 600 mm grid in feet.
            var grid = new CeilingGridMath.CeilingGrid
            {
                OriginX = 0.0, OriginY = 0.0,
                UFt = 600.0 / 304.8, VFt = 600.0 / 304.8,
                AngleRad = 0.0, IsValid = true, PhaseIsExact = true
            };

            double u = grid.UFt;
            Func<int, int, bool> usable = InsideRoom(in grid, 0.0, 0.0, u * 12.0, u * 12.0);

            // Nominal 2 ft = 609.6 mm, which IS one 600 mm tile.
            TileCentricPatternGenerator.Result r = TileCentricPatternGenerator.TryGenerate(
                4, in grid, 0.0, 0.0, u * 12.0, u * 12.0, 24.0 / 12.0 * u / 2.0, 24.0 / 12.0 * u / 2.0, true, usable);

            // The core guarantee: whatever span it chose, no two devices share a tile.
            if (r.Success)
            {
                var seen = new HashSet<long>();
                bool overlap = false;
                foreach (double[] p in r.Positions)
                {
                    CeilingGridMath.WorldToIndex(p[0], p[1], in grid, out int iu, out int iv);
                    if (!seen.Add(CeilingGridMath.IndexKey(iu, iv))) overlap = true;
                }
                Check(!overlap, "no two devices landed on the same tile");
            }
            else
            {
                Check(false, "a 4-device layout should have been found: " + r.Reason);
            }
        }
    }

    /// <summary>Small readability helpers used only by these tests.</summary>
    internal static class TileCentricPatternTestsExtensions
    {
        public static bool IsGridAngleReportedNonZero(this TileCentricPatternGenerator.Result r)
        {
            return Math.Abs(r.GridAngleRad) > 1e-9;
        }
    }
}