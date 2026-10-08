using System;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce;
using CG = FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce.CeilingGridMath;

namespace FireProtection.Tests
{
    /// <summary>
    /// Pure, Revit-free tests for <see cref="CeilingGridMath"/>: axis-aligned snap to a tile
    /// center, idempotence of a point already centered, a rotated (30 deg) grid, and the
    /// deterministic synthetic fallback grid. No engine, no Revit.
    /// </summary>
    internal static class CeilingGridSnapTests
    {
        private static int _failures;

        public static void RunAll()
        {
            _failures = 0;
            TestAxisAlignedSnap();
            TestCenteredPointStaysPut();
            TestRotatedGridIdempotent();
            TestFromTileSizeFallback();
            TestInvalidGridIsNeutral();

            if (_failures == 0)
            {
                Console.WriteLine("CeilingGridSnapTests: PASS");
            }
            else
            {
                Console.WriteLine("CeilingGridSnapTests: " + _failures + " FAIL(s)");
                throw new Exception("CeilingGridSnapTests failed");
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

        private static bool Near(double a, double b, double tol = 1e-6)
        {
            return Math.Abs(a - b) <= tol;
        }

        // -----------------------------------------------------------------
        // Test 1 — axis-aligned 2x2 ft grid at origin (0,0) snaps to (i+0.5) centers
        // -----------------------------------------------------------------
        private static void TestAxisAlignedSnap()
        {
            Console.WriteLine("Test: axis-aligned grid snaps to (i+0.5) tile centers");
            var grid = CG.FromTileSize(2.0, 2.0, 0.0, 0.0);
            Check(grid.IsValid, "2x2 ft grid resolves as valid");

            // A point at (0.3, 0.4) is in tile (0,0) -> center (1,1).
            CG.SnapToTileCenter(0.3, 0.4, grid, out double sx, out double sy);
            Check(Near(sx, 1.0) && Near(sy, 1.0),
                "point (0.3,0.4) -> tile center (1,1) (got " + sx.ToString("F3") + "," + sy.ToString("F3") + ")");

            // A point at (5.7, 2.1) is in tile (2,1) -> center (5,3).
            CG.SnapToTileCenter(5.7, 2.1, grid, out double sx2, out double sy2);
            Check(Near(sx2, 5.0) && Near(sy2, 3.0),
                "point (5.7,2.1) -> tile center (5,3) (got " + sx2.ToString("F3") + "," + sy2.ToString("F3") + ")");
        }

        // -----------------------------------------------------------------
        // Test 2 — a point already at a tile center is unchanged (idempotent)
        // -----------------------------------------------------------------
        private static void TestCenteredPointStaysPut()
        {
            Console.WriteLine("Test: a point already at a tile center is unchanged");
            var grid = CG.FromTileSize(2.0, 2.0, 0.0, 0.0);

            CG.SnapToTileCenter(1.0, 1.0, grid, out double sx, out double sy);
            Check(Near(sx, 1.0) && Near(sy, 1.0),
                "center (1,1) snaps to itself (got " + sx.ToString("F3") + "," + sy.ToString("F3") + ")");

            // Snapping twice is a no-op.
            CG.SnapToTileCenter(sx, sy, grid, out double sx2, out double sy2);
            Check(Near(sx2, 1.0) && Near(sy2, 1.0), "double-snap is idempotent");
        }

        // -----------------------------------------------------------------
        // Test 3 — a rotated (30 deg) grid: snapped output is itself a tile center (idempotent)
        // -----------------------------------------------------------------
        private static void TestRotatedGridIdempotent()
        {
            Console.WriteLine("Test: rotated 30-deg grid snap is idempotent");
            double angle = 30.0 * Math.PI / 180.0;
            var grid = new CG.CeilingGrid
            {
                OriginX = 1.5,
                OriginY = -2.0,
                UFt = 2.0,
                VFt = 4.0,
                AngleRad = angle,
                IsValid = true
            };

            CG.SnapToTileCenter(7.3, 3.1, grid, out double sx, out double sy);
            // Snapping the result again must land on the same point (it is a genuine tile center).
            CG.SnapToTileCenter(sx, sy, grid, out double sx2, out double sy2);
            Check(Near(sx2, sx, 1e-6) && Near(sy2, sy, 1e-6),
                "re-snapping a rotated-grid center returns the same point (dx="
                + Math.Abs(sx2 - sx).ToString("E2") + ", dy=" + Math.Abs(sy2 - sy).ToString("E2") + ")");

            // The snapped point, mapped into grid-local coords, must sit at (iu+0.5)U / (iv+0.5)V.
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            double dx = sx - grid.OriginX, dy = sy - grid.OriginY;
            double lu = dx * cos + dy * sin;
            double lv = -dx * sin + dy * cos;
            double fracU = lu / grid.UFt - Math.Floor(lu / grid.UFt);
            double fracV = lv / grid.VFt - Math.Floor(lv / grid.VFt);
            Check(Near(fracU, 0.5, 1e-6) && Near(fracV, 0.5, 1e-6),
                "snapped point lies at (i+0.5) in both grid axes (fracU=" + fracU.ToString("F3")
                + ", fracV=" + fracV.ToString("F3") + ")");
        }

        // -----------------------------------------------------------------
        // Test 4 — FromTileSize fallback: square when V<=0, rejects sub-min pitch
        // -----------------------------------------------------------------
        private static void TestFromTileSizeFallback()
        {
            Console.WriteLine("Test: FromTileSize fallback behavior");
            var square = CG.FromTileSize(2.0, 0.0, 10.0, 20.0);
            Check(square.IsValid && Near(square.VFt, 2.0),
                "V<=0 assumes square tile (V=U=2) (got V=" + square.VFt.ToString("F2") + ")");
            Check(Near(square.OriginX, 10.0) && Near(square.OriginY, 20.0) && Near(square.AngleRad, 0.0),
                "synthetic grid is anchored at the room corner (10,20), zero angle");

            var tooSmall = CG.FromTileSize(0.1, 0.1, 0.0, 0.0);
            Check(!tooSmall.IsValid, "sub-minimum (0.1 ft) pitch is rejected as invalid");
        }

        // -----------------------------------------------------------------
        // Test 5 — an invalid grid leaves the input unchanged (neutrality)
        // -----------------------------------------------------------------
        private static void TestInvalidGridIsNeutral()
        {
            Console.WriteLine("Test: invalid grid is neutral (no snapping)");
            CG.CeilingGrid invalid = default;
            Check(!invalid.IsValid, "default grid is invalid");

            CG.SnapToTileCenter(3.14159, 2.71828, invalid, out double sx, out double sy);
            Check(Near(sx, 3.14159) && Near(sy, 2.71828),
                "invalid grid returns input unchanged (got " + sx.ToString("F5") + "," + sy.ToString("F5") + ")");

            // FromReadable on a non-grid ceiling stays invalid.
            var noGrid = new CeilingData { SlopeType = "FLAT", HasReadableGrid = false };
            Check(!CG.FromReadable(noGrid).IsValid, "ceiling without readable grid -> invalid");
        }
    }
}
