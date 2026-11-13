using System;
using System.Collections.Generic;
using FireProtection.Backend.Models.DTOs;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce
{
    public static class CeilingGridMath
    {
        /// <summary>
        /// The tile lattice for one room: an orthogonal grid of tiles with a known phase.
        /// </summary>
        /// <remarks>
        /// Public so the tile-centric generator and the device engines can pass a lattice around
        /// without duplicating its fields (which is how the two axes got out of step once already).
        /// Construct one with <see cref="FromReadable"/> or <see cref="FromTileSize"/> rather than a
        /// struct initializer: a hand-built grid can leave the rotation unset while still reporting
        /// itself valid, which is how a snapped point once landed on a tile CORNER.
        /// </remarks>
        public struct CeilingGrid
        {
            public double OriginX;
            public double OriginY;
            public double UFt;
            public double VFt;
            public double AngleRad;
            public bool IsValid;

            /// <summary>
            /// cos/sin of <see cref="AngleRad"/>.
            ///
            /// These are deliberately DERIVED ON DEMAND rather than cached in the struct. An
            /// earlier version cached them as fields populated by a factory method, which created
            /// an invalid state: <c>CeilingGrid</c> is a struct with public settable fields, so a
            /// caller can build one with a struct initializer (<c>new CeilingGrid { AngleRad = x }</c>)
            /// and the cached rotation would silently stay (0,0) — rotating nothing while still
            /// reporting a valid grid. That produced a snapped point on a tile CORNER instead of
            /// its centre. AngleRad is now the single source of truth and cannot go stale.
            /// </summary>
            public double Cos => Math.Cos(AngleRad);
            public double Sin => Math.Sin(AngleRad);

            /// <summary>
            /// True when the lattice ORIGIN is exact, so index (i, j) really is the centre of the
            /// drafter's tile.
            ///
            /// False for a lattice whose origin was guessed from ceiling geometry. Such a lattice is
            /// still internally consistent — every head lands on a centre of *some* tile — but those
            /// tiles are offset from the real RCP grid, so the head appears to sit between tiles.
            /// This distinction is the difference between "tile-centric placement" and "centred array
            /// that happens to use a tile pitch", and it must be visible rather than assumed.
            /// </summary>
            public bool PhaseIsExact { get; set; }

            /// <summary>Where the origin came from; one of <see cref="CeilingData.GridOriginSources"/>.</summary>
            public string PhaseSource { get; set; }

            /// <summary>
            /// True when the lattice's tile span looks self-consistent: both pitches positive and an
            /// integer-ish multiple is not required, but a pitch far outside any real tile module is
            /// rejected here rather than producing a nonsensical layout downstream.
            /// </summary>
            public bool IsPlausible =>
                IsValid && UFt >= MinPitchFt && VFt >= MinPitchFt
                          && UFt <= 12.0 && VFt <= 12.0;
        }

        private const double MinPitchFt = 0.25;

        public static CeilingGrid FromReadable(CeilingData ceiling)
        {
            if (ceiling == null || !ceiling.HasReadableGrid)
                return default;

            double u = ceiling.GridSpacingUFt;
            double v = ceiling.GridSpacingVFt > 0 ? ceiling.GridSpacingVFt : ceiling.GridSpacingUFt;

            if (u < MinPitchFt || v < MinPitchFt)
                return default;

            return new CeilingGrid
            {
                OriginX = ceiling.GridOriginXFt,
                OriginY = ceiling.GridOriginYFt,
                UFt = u,
                VFt = v,
                AngleRad = ceiling.GridAngleRad,
                IsValid = true,
                PhaseIsExact = ceiling.HasExactGridPhase,
                PhaseSource = ceiling.GridOriginSource
            };
        }

        public static CeilingGrid FromTileSize(double uFt, double vFt, double minX, double minY)
        {
            double u = uFt;
            double v = vFt > 0 ? vFt : uFt;
            if (u < MinPitchFt || v < MinPitchFt)
                return default;

            return new CeilingGrid
            {
                OriginX = minX,
                OriginY = minY,
                UFt = u,
                VFt = v,
                AngleRad = 0.0,
                IsValid = true,
                // A user-entered tile size anchored at the room's min corner. The PITCH is exactly
                // what the user asked for; the PHASE is only as good as that anchor, and a room wall
                // is frequently not a tile boundary — so this is deliberately NOT marked exact.
                PhaseIsExact = false,
                PhaseSource = CeilingData.GridOriginSources.TypeName
            };
        }

        /// <summary>
        /// A one-line description of how trustworthy the lattice phase is, for the run report.
        /// </summary>
        public static string DescribePhase(in CeilingGrid grid)
        {
            if (!grid.IsValid) return "no usable tile grid";
            return (grid.PhaseIsExact ? "exact" : "PROVISIONAL") + " phase (" +
                   (string.IsNullOrEmpty(grid.PhaseSource) ? "unspecified" : grid.PhaseSource) +
                   "), pitch " + grid.UFt.ToString("F3") + "x" + grid.VFt.ToString("F3") + " ft";
        }

        /// <summary>
        /// World point -> tile index (floor of the local u/v coordinate). This is THE canonical
        /// world-to-tile conversion; the calculation engine calls it rather than re-deriving the
        /// rotation, which previously existed in two places and had already drifted once.
        /// </summary>
        public static void WorldToIndex(double wx, double wy, in CeilingGrid grid, out int iu, out int iv)
        {
            double dx = wx - grid.OriginX;
            double dy = wy - grid.OriginY;
            double lu = dx * grid.Cos + dy * grid.Sin;
            double lv = -dx * grid.Sin + dy * grid.Cos;
            iu = (int)Math.Floor(lu / grid.UFt);
            iv = (int)Math.Floor(lv / grid.VFt);
        }

        /// <summary>
        /// Tile index -> world point at the tile CENTRE (the +0.5 half-tile shift). The single
        /// canonical inverse of <see cref="WorldToIndex"/>; every placement path must land heads
        /// on tile centres, so the shift lives here and nowhere else.
        /// </summary>
        public static void IndexToWorld(int iu, int iv, in CeilingGrid grid, out double wx, out double wy)
        {
            double cu = (iu + 0.5) * grid.UFt;
            double cv = (iv + 0.5) * grid.VFt;
            wx = grid.OriginX + cu * grid.Cos - cv * grid.Sin;
            wy = grid.OriginY + cu * grid.Sin + cv * grid.Cos;
        }

        /// <summary>Packs a tile index pair into a single HashSet key.</summary>
        public static long IndexKey(int iu, int iv)
        {
            return ((long)iu << 32) ^ (uint)iv;
        }

        /// <summary>Distance between two tile indices, measured in real feet on the XY plane.</summary>
        public static double IndexDistance(int iu1, int iv1, int iu2, int iv2, in CeilingGrid grid)
        {
            IndexToWorld(iu1, iv1, in grid, out double x1, out double y1);
            IndexToWorld(iu2, iv2, in grid, out double x2, out double y2);
            double dx = x1 - x2;
            double dy = y1 - y2;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static void SnapToTileCenter(double tx, double ty, in CeilingGrid grid, out double sx, out double sy)
        {
            if (!grid.IsValid)
            {
                sx = tx;
                sy = ty;
                return;
            }

            WorldToIndex(tx, ty, in grid, out int iu, out int iv);
            IndexToWorld(iu, iv, in grid, out sx, out sy);
        }

        public static CeilingGrid TryResolveRoomGrid(
            CeilingData primaryCeiling,
            double? overrideTileUFt,
            double? overrideTileVFt,
            double roomMinX,
            double roomMinY)
        {
            CeilingGrid readable = FromReadable(primaryCeiling);
            if (readable.IsValid)
                return readable;

            if (overrideTileUFt.HasValue && overrideTileUFt.Value > 0)
            {
                return FromTileSize(
                    overrideTileUFt.Value,
                    overrideTileVFt ?? 0.0,
                    roomMinX,
                    roomMinY);
            }

            return default;
        }

        public static List<double[]> EnumerateTileCenters(
            in CeilingGrid grid,
            double minX, double minY, double maxX, double maxY,
            double tolerance)
        {
            var centers = new List<double[]>();
            if (!grid.IsValid) return centers;

            double cos = Math.Cos(grid.AngleRad);
            double sin = Math.Sin(grid.AngleRad);

            double minU = double.PositiveInfinity, maxU = double.NegativeInfinity;
            double minV = double.PositiveInfinity, maxV = double.NegativeInfinity;
            double[,] corners = { { minX, minY }, { maxX, minY }, { minX, maxY }, { maxX, maxY } };
            for (int k = 0; k < 4; k++)
            {
                double dx = corners[k, 0] - grid.OriginX;
                double dy = corners[k, 1] - grid.OriginY;
                double lu = dx * cos + dy * sin;
                double lv = -dx * sin + dy * cos;
                if (lu < minU) minU = lu;
                if (lu > maxU) maxU = lu;
                if (lv < minV) minV = lv;
                if (lv > maxV) maxV = lv;
            }

            int iuStart = (int)Math.Floor(minU / grid.UFt) - 1;
            int iuEnd = (int)Math.Floor(maxU / grid.UFt) + 1;
            int ivStart = (int)Math.Floor(minV / grid.VFt) - 1;
            int ivEnd = (int)Math.Floor(maxV / grid.VFt) + 1;

            for (int iv = ivStart; iv <= ivEnd; iv++)
            {
                double cv = (iv + 0.5) * grid.VFt;
                for (int iu = iuStart; iu <= iuEnd; iu++)
                {
                    double cu = (iu + 0.5) * grid.UFt;
                    double wx = grid.OriginX + cu * cos - cv * sin;
                    double wy = grid.OriginY + cu * sin + cv * cos;
                    if (wx < minX - tolerance || wx > maxX + tolerance) continue;
                    if (wy < minY - tolerance || wy > maxY + tolerance) continue;
                    centers.Add(new[] { wx, wy });
                }
            }

            return centers;
        }
    }
}