using System;
using System.Collections.Generic;

// CeilingGrid is nested inside CeilingGridMath, so an unqualified name does not resolve. One alias
// here rather than qualifying the type at every use, and rather than moving the type out of
// CeilingGridMath (which would churn every existing reference in both engines).
using CeilingGrid = FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce.CeilingGridMath.CeilingGrid;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce
{
    /// <summary>
    /// Lays a device pattern out ON the ceiling's tile grid while following the X-2X-X rule
    /// (X from the wall to the first device, 2X between devices, X to the opposite wall) in both
    /// grid directions.
    ///
    /// WHY THE EXISTING TILE PATH WAS NOT ENOUGH
    /// ----------------------------------------
    /// The existing sprinkler path (<c>SelectCenteredGridOnTiles</c>) already targets tile centres,
    /// but it ANCHORS the array at the first tile of the room and appends the far index when the
    /// stride misses it. Worked example: 20 ft room, 2 ft tiles (10 tiles), max spacing 15 ft gives
    /// a stride of 7 tiles = 14 ft; anchoring yields tiles 0, 7, 9, i.e. wall gaps of 1 ft, 14 ft and
    /// 4 ft. Spacing stays legal but the layout is visibly wrong.
    ///
    /// This generator SOLVES for the layout instead. Per direction it uses ONE whole-tile pitch for
    /// every gap and picks the START TILE so the two wall gaps come out as close to half the pitch
    /// as the grid allows. Snapping an ideal X-2X-X point to its nearest tile does not work: the
    /// ideal pitch is rarely a whole number of tiles (7 rows in a 12.6-tile room is 1.8 tiles), so
    /// independent rounding sends some gaps up and others down.
    ///
    /// RULES HONOURED
    /// --------------
    /// - Every device sits on the CENTRE of a whole block of tiles, so no two devices share a tile.
    /// - The required count is a MINIMUM. If the grid cannot lay out exactly that count, the nearest
    ///   larger count that fits is used and the result says so. The count is never reduced.
    /// - Only tiles the caller accepts are usable (that is how "fully inside the room" and any
    ///   obstruction rejection get in).
    /// - A device spans as many tiles along each direction as its real size needs.
    /// - Pitches are whole tile counts.
    ///
    /// SCOPE — READ BEFORE USING THE OUTPUT
    /// -----------------------------------
    /// This is a CANDIDATE GENERATOR, not a placement decision. It knows nothing about NFPA hazard
    /// rules: not min/max spacing, not obstruction clearance, not deflector distances, not coverage
    /// area. It proposes a tile-anchored pattern; the calling engine still runs its own rule checks
    /// and pairwise validation over the result. (The reference implementation this was ported from
    /// is a lighting tool whose counts come from an FC calculation, so its output could be final. In
    /// this tool counts and spacing come from code rules, so it must not be.)
    ///
    /// Pure math — no Revit API, no model changes — so it is directly unit-testable.
    /// </summary>
    public static class TileCentricPatternGenerator
    {
        /// <summary>Tolerance tiers, in TILES, for the wall-gap tests. Tried tightest first.</summary>
        private static readonly double[] Tolerances = { 0.5, 1.0 };

        /// <summary>Usual cap on extra devices versus the required count.</summary>
        private const double IncreaseLimitFraction = 0.20;

        /// <summary>Floor for that cap, so a small required count still gets headroom.</summary>
        private const int IncreaseLimitMin = 2;

        /// <summary>Cap on how much wider one direction's pitch may be than the other's.</summary>
        private const double MaxPitchRatio = 2.0;

        /// <summary>Best-scoring start tiles kept per pitch. Enough to recover from an obstructed
        /// tile without letting the cross-axis search grow combinatorially.</summary>
        private const int StartsKeptPerPitch = 6;

        /// <summary>
        /// Tiles are rounded to a whole count with this tolerance, so a nominal 2 ft (609.6 mm)
        /// device is ONE 600 mm tile rather than two.
        /// </summary>
        private const double SpanTolerance = 0.10;

        /// <summary>Everything the caller needs to place and report the chosen pattern.</summary>
        public sealed class Result
        {
            /// <summary>True when a pattern was found. False means "keep the non-tile flow".</summary>
            public bool Success;

            /// <summary>Why no pattern was found. Diagnostic only.</summary>
            public string Reason = "";

            /// <summary>Device positions in world XY, feet. Every one is a tile centre.</summary>
            public readonly List<double[]> Positions = new List<double[]>();

            /// <summary>Device count the caller's rules asked for.</summary>
            public int RequiredCount;

            /// <summary>Devices in <see cref="Positions"/>. Never below <see cref="RequiredCount"/>
            /// except on an irregular footprint, where the count of surviving blocks is bounded below
            /// by <see cref="RequiredCount"/> at search time.</summary>
            public int PlacedCount;

            /// <summary>Usable tiles inside the room.</summary>
            public int TilesAvailable;

            /// <summary>Devices along grid direction A and along B.</summary>
            public int Columns;
            public int Rows;

            /// <summary>Tiles per device along A and along B, from the device's real footprint.</summary>
            public int SpanA = 1;
            public int SpanB = 1;

            /// <summary>Device-to-device pitch along A and B, feet.</summary>
            public double PitchAFt;
            public double PitchBFt;

            /// <summary>Wall-to-device gap at each end, feet: the X of X-2X-X.</summary>
            public double WallGapA1Ft, WallGapA2Ft;
            public double WallGapB1Ft, WallGapB2Ft;

            /// <summary>Tolerance tier that produced the winner, in tiles (0.5 or 1.0).</summary>
            public double ToleranceTiles;

            /// <summary>The extra-device cap that was applied.</summary>
            public int IncreaseLimit;

            /// <summary>True when nothing fit inside the cap and the count had to go above it.</summary>
            public bool BeyondIncreaseLimit;

            /// <summary>True when the usable tiles do not fill their own bounding box (L-shape etc.),
            /// so a global pattern has to tolerate missing positions.</summary>
            public bool FootprintIsIrregular;

            /// <summary>Carried for the caller's report; not used for layout.</summary>
            public double GridAngleRad;

            // The chosen per-axis pattern, exposed as INDEX PARAMETERS rather than as positions.
            // A caller that already iterates tile indices (the sprinkler engine does) rebuilds its
            // target lists from these directly, instead of round-tripping each world position back
            // through WorldToIndex and risking a float-exactness mismatch with its own snapping.

            /// <summary>First tile index of the first device along direction A.</summary>
            public int StartA;

            /// <summary>Tiles between devices along direction A (0 for a single device).</summary>
            public int PitchA;

            /// <summary>First tile index of the first device along direction B.</summary>
            public int StartB;

            /// <summary>Tiles between devices along direction B (0 for a single device).</summary>
            public int PitchB;

            /// <summary>
            /// True when the long axis of the device runs along grid direction A.
            /// </summary>
            public bool LongAxisIsAlongA;
        }

        /// <summary>One arrangement of <c>n</c> equal-pitch devices along a single direction.</summary>
        private sealed class AxisOption
        {
            /// <summary>Wall-gap error in feet: symmetry error plus X-vs-2X proportion error.</summary>
            public double Score;

            /// <summary>First tile index of the first device along this axis.</summary>
            public int Start;

            /// <summary>Tiles between devices. Zero when this axis holds a single device.</summary>
            public int Pitch;

            /// <summary>Pitch in feet, for the pitch-ratio check.</summary>
            public double PitchFt;

            /// <summary>Wall gaps at each end, feet.</summary>
            public double W1, W2;
        }

        /// <summary>How a device's footprint maps onto the lattice.</summary>
        private sealed class Orientation
        {
            public readonly int SpanA, SpanB;
            public readonly bool LongAlongA;
            public readonly double RealAFt, RealBFt;

            public Orientation(int spanA, int spanB, bool longAlongA, double realAFt, double realBFt)
            {
                SpanA = spanA; SpanB = spanB; LongAlongA = longAlongA;
                RealAFt = realAFt; RealBFt = realBFt;
            }
        }

        /// <summary>
        /// Finds the best tile-anchored pattern for at least <paramref name="requiredCount"/> devices.
        /// </summary>
        /// <param name="requiredCount">Minimum number of devices. Never reduced.</param>
        /// <param name="grid">The room's tile lattice.</param>
        /// <param name="roomMinX">Room bounding box, world feet.</param>
        /// <param name="roomMinY">Room bounding box, world feet.</param>
        /// <param name="roomMaxX">Room bounding box, world feet.</param>
        /// <param name="roomMaxY">Room bounding box, world feet.</param>
        /// <param name="deviceLengthFt">Device real size along its long axis.</param>
        /// <param name="deviceWidthFt">Device real size along its short axis.</param>
        /// <param name="longAxisIsAlongA">Which grid direction the long axis runs along.</param>
        /// <param name="isTileUsable">
        /// Per-tile admissibility from the caller: is tile (iu, iv) a real tile that may carry a
        /// device? This is how "fully inside the room" and any obstruction rejection get in, which is
        /// what keeps rule knowledge out of this class.
        /// </param>
        public static Result TryGenerate(
            int requiredCount,
            in CeilingGrid grid,
            double roomMinX, double roomMinY, double roomMaxX, double roomMaxY,
            double deviceLengthFt, double deviceWidthFt,
            bool longAxisIsAlongA,
            Func<int, int, bool> isTileUsable)
        {
            var res = new Result { RequiredCount = requiredCount, GridAngleRad = grid.AngleRad };

            if (requiredCount < 1) { res.Reason = "no devices to place"; return res; }
            if (!grid.IsValid || !grid.IsPlausible) { res.Reason = "no usable tile grid"; return res; }
            if (isTileUsable == null) { res.Reason = "no tile admissibility supplied"; return res; }
            if (roomMaxX <= roomMinX || roomMaxY <= roomMinY) { res.Reason = "room extent is degenerate"; return res; }

            // `in` parameters cannot be captured by the local function or lambdas below, so the
            // search works from a local copy. It is a small struct; the copy is free and keeps the
            // public signature read-only for callers.
            CeilingGrid lattice = grid;

            // ── 1. Wall extents in lattice-local coordinates ────────────────────────────────
            // Measured from the room's real boundary, not from the lattice, so the X gaps are the
            // actual distances to the actual walls (correct for a rotated grid and, to the extent
            // the caller's box describes the room, for a non-rectangular one too).
            LocalBounds(in lattice, roomMinX, roomMinY, roomMaxX, roomMaxY,
                        out double loA, out double hiA, out double loB, out double hiB);
            double lenA = hiA - loA;
            double lenB = hiB - loB;
            if (lenA <= 0 || lenB <= 0) { res.Reason = "room has no extent along the grid axes"; return res; }

            // ── 2. Tile index range covering the room ───────────────────────────────────────
            // Derived from the lattice, exactly like EnumerateTileCenters, so the generator and the
            // enumerator can never disagree about which tile is (iu, iv).
            IndexRange(in lattice, roomMinX, roomMinY, roomMaxX, roomMaxY,
                       out int iuMin, out int iuMax, out int ivMin, out int ivMax);

            int tilesInBox = (iuMax - iuMin + 1) * (ivMax - ivMin + 1);
            int usable = 0;
            for (int iu = iuMin; iu <= iuMax; iu++)
                for (int iv = ivMin; iv <= ivMax; iv++)
                    if (isTileUsable(iu, iv)) usable++;

            res.TilesAvailable = usable;
            if (usable == 0) { res.Reason = "no usable tile inside this room"; return res; }

            // A rectangular room's usable tiles fill their own bounding box; an L-shape's do not,
            // because the notch sits inside that box. That is the whole test for irregularity.
            res.FootprintIsIrregular = usable != tilesInBox;

            // ── 3. Device footprint on the lattice ──────────────────────────────────────────
            var orientations = new List<Orientation>();
            int spanA = SpanFor(longAxisIsAlongA ? deviceLengthFt : deviceWidthFt, lattice.UFt);
            int spanB = SpanFor(longAxisIsAlongA ? deviceWidthFt : deviceLengthFt, lattice.VFt);
            orientations.Add(new Orientation(
                spanA, spanB, longAxisIsAlongA,
                longAxisIsAlongA ? deviceLengthFt : deviceWidthFt,
                longAxisIsAlongA ? deviceWidthFt : deviceLengthFt));

            if (deviceLengthFt > deviceWidthFt * 1.05)
            {
                // A non-square device also gets the rotated orientation: a 2'x4' fitting may suit
                // either alignment, and which one gives the better X-2X-X split is room-dependent.
                orientations.Add(new Orientation(
                    SpanFor(deviceWidthFt, lattice.UFt), SpanFor(deviceLengthFt, lattice.VFt),
                    false, deviceWidthFt, deviceLengthFt));
            }

            int minSpanCells = int.MaxValue;
            foreach (Orientation o in orientations)
                if (o.SpanA * o.SpanB < minSpanCells) minSpanCells = o.SpanA * o.SpanB;
            if (minSpanCells == int.MaxValue) minSpanCells = 1;

            int maxCount = Math.Min(usable / minSpanCells, (int)Math.Ceiling(requiredCount * 1.5) + 1);
            if (maxCount < requiredCount)
            {
                res.Reason = "only " + usable + " usable tile(s) — not enough for "
                             + requiredCount + " device(s)";
                return res;
            }

            int increaseLimit = Math.Max(IncreaseLimitMin, (int)Math.Ceiling(requiredCount * IncreaseLimitFraction));
            int limitCount = Math.Min(maxCount, requiredCount + increaseLimit);
            res.IncreaseLimit = increaseLimit;

            // ── 4. Search: nearest count first, tightest tolerance first ───────────────────
            Result Search(int fromCount, int toCount)
            {
                for (int count = fromCount; count <= toCount; count++)
                {
                    foreach (double tolTiles in Tolerances)
                    {
                        AxisOption bestA = null, bestB = null;
                        int bestNA = 0, bestNB = 0;
                        Orientation bestO = null;
                        double bestScore = double.MaxValue, bestSum = double.MaxValue, bestRatio = double.MaxValue;

                        foreach (Orientation o in orientations)
                        {
                            for (int nA = 1; nA <= count; nA++)
                            {
                                if (count % nA != 0) continue;
                                int nB = count / nA;

                                List<AxisOption> optsA = AxisOptions(
                                    iuMin, iuMax, loA, lenA, lattice.UFt,
                                    nA, o.SpanA, o.RealAFt, tolTiles * lattice.UFt, isTileUsable);
                                if (optsA.Count == 0) continue;

                                List<AxisOption> optsB = AxisOptions(
                                    ivMin, ivMax, loB, lenB, lattice.VFt,
                                    nB, o.SpanB, o.RealBFt, tolTiles * lattice.VFt, isTileUsable);
                                if (optsB.Count == 0) continue;

                                foreach (AxisOption a in optsA)
                                {
                                    // Options are score-sorted and the layout score is max(a, b), so
                                    // once a alone is worse than the incumbent, no b can rescue it.
                                    if (a.Score > bestScore + 1e-9) break;

                                    foreach (AxisOption b in optsB)
                                    {
                                        double score = Math.Max(a.Score, b.Score);
                                        if (score > bestScore + 1e-9) break;

                                        double ratio = PitchRatio(a, nA, lenA, b, nB, lenB);
                                        if (ratio > MaxPitchRatio + 1e-9) continue;

                                        // Every block must be usable in a rectangular room. On an
                                        // irregular footprint the pattern is accepted once enough of
                                        // its blocks survive, which keeps ONE pitch across the whole
                                        // room instead of resetting the rhythm at the notch.
                                        int blocks = CountUsableBlocks(a, nA, o.SpanA, b, nB, o.SpanB, isTileUsable);
                                        int needed = res.FootprintIsIrregular ? requiredCount : count;
                                        if (blocks < needed) continue;

                                        double sum = a.Score + b.Score;
                                        bool better;
                                        if (score < bestScore - 1e-9) better = true;
                                        else if (score > bestScore + 1e-9) better = false;
                                        else if (sum < bestSum - 1e-9) better = true;
                                        else if (sum > bestSum + 1e-9) better = false;
                                        else better = ratio < bestRatio - 1e-9;

                                        if (!better) continue;

                                        bestScore = score; bestSum = sum; bestRatio = ratio;
                                        bestA = a; bestB = b; bestNA = nA; bestNB = nB; bestO = o;
                                    }
                                }
                            }
                        }

                        if (bestA == null) continue;

                        Result built = Build(in lattice, bestA, bestNA, bestO.SpanA,
                                               bestB, bestNB, bestO.SpanB, bestO.LongAlongA, isTileUsable);
                        built.ToleranceTiles = tolTiles;
                        built.WallGapA1Ft = bestA.W1;
                        built.WallGapA2Ft = bestA.W2;
                        built.WallGapB1Ft = bestB.W1;
                        built.WallGapB2Ft = bestB.W2;
                        return built;
                    }
                }
                return null;
            }

            Result best = Search(requiredCount, limitCount);
            if (best == null && limitCount < maxCount)
            {
                best = Search(limitCount + 1, maxCount);
                if (best != null) best.BeyondIncreaseLimit = true;
            }

            if (best != null)
            {
                best.Success = true;
                best.RequiredCount = requiredCount;
                best.IncreaseLimit = increaseLimit;
                best.TilesAvailable = res.TilesAvailable;
                best.FootprintIsIrregular = res.FootprintIsIrregular;
                // Build() creates its own Result, so the angle has to be carried across explicitly —
                // otherwise a rotated grid's report reads as axis-aligned.
                best.GridAngleRad = grid.AngleRad;
                return best;
            }

            res.Reason = "no even X-2X-X layout of " + requiredCount + "-" + maxCount
                         + " device(s) fits this room's tile grid";
            return res;
        }

        /// <summary>
        /// Every equal-pitch way to place <paramref name="n"/> devices of <paramref name="span"/> tiles
        /// each along one direction, keeping only those whose wall gaps sit within tolerance of half
        /// the pitch. The best few starts per pitch are kept, most balanced first.
        ///
        /// The X-2X-X test is two checks and BOTH must pass:
        ///   symmetry  — the two wall gaps mirror each other (the two Xs are equal), and
        ///   proportion — their average is half the pitch (X is half of 2X).
        /// Testing only the proportion would let a layout whose gaps merely average out correctly
        /// pass, which is not X-2X-X on both walls.
        /// </summary>
        private static List<AxisOption> AxisOptions(
            int idxMin, int idxMax, double lo, double roomLen, double moduleFt,
            int n, int span, double realSizeFt, double tolFt,
            Func<int, int, bool> isTileUsable)
        {
            var result = new List<AxisOption>();
            if (n < 1 || span < 1 || moduleFt <= 0 || roomLen <= 0) return result;

            int spanIdx = idxMax - idxMin + 1;
            double realHalf = realSizeFt / 2.0;

            // Tile centre position along this direction, measured from the lattice origin. Index (i)
            // centres at (i + 0.5) * module — the same half-tile shift CeilingGridMath.IndexToWorld
            // applies, which is what keeps the generator and the placer on identical tiles.
            var centreOf = new Dictionary<int, double>(spanIdx + 1);
            for (int i = idxMin; i <= idxMax; i++)
                centreOf[i] = (i + 0.5) * moduleFt;

            // n == 1 has no pitch: one device simply centres in the room.
            var pitches = new List<int>();
            if (n == 1) pitches.Add(0);
            else
                for (int p = span; p <= spanIdx - span; p++) pitches.Add(p);

            foreach (int p in pitches)
            {
                if (n > 1 && (n - 1) * p + span > spanIdx) break;

                // Real-size clash guard: a tile-count pitch can be smaller than the device's real
                // size (the span was rounded up within tolerance), which would seat two devices close
                // enough for their bodies to overlap even though their tile blocks do not.
                if (n > 1 && p * moduleFt + 1e-9 < realSizeFt) continue;

                var perStart = new List<AxisOption>();
                for (int c0 = idxMin; c0 <= idxMax; c0++)
                {
                    // INDEX existence only. Per-tile admissibility is applied later in
                    // CountUsableBlocks / Build, because on an irregular footprint the pattern is
                    // allowed to lose individual positions to the notch.
                    if (c0 < idxMin) continue;
                    if (c0 + (n - 1) * p + span - 1 > idxMax) continue;

                    double first = centreOf[c0];
                    double last = centreOf[c0 + (n - 1) * p];

                    double w1 = first - lo;
                    double w2 = (lo + roomLen) - last;

                    // Real-size wall guard: w1/w2 are measured to the tile-block CENTRE, so if the
                    // device's real half-size reaches past that gap it clips the wall even though the
                    // tile-block arithmetic looked fine.
                    if (w1 - realHalf < -1e-9) continue;
                    if (w2 - realHalf < -1e-9) continue;

                    double symmetryErr = Math.Abs(w1 - w2) / 2.0;
                    double proportionErr = n == 1 ? 0.0 : Math.Abs((w1 + w2) / 2.0 - p * moduleFt / 2.0);

                    if (n > 1 && symmetryErr > tolFt + 1e-9) continue;
                    if (proportionErr > tolFt + 1e-9) continue;

                    perStart.Add(new AxisOption
                    {
                        Score = symmetryErr + proportionErr,
                        Start = c0,
                        Pitch = p,
                        PitchFt = n == 1 ? roomLen : p * moduleFt,
                        W1 = w1,
                        W2 = w2
                    });
                }

                perStart.Sort((x, y) => x.Score.CompareTo(y.Score));
                for (int i = 0; i < perStart.Count && i < StartsKeptPerPitch; i++)
                    result.Add(perStart[i]);
            }

            result.Sort((x, y) => x.Score.CompareTo(y.Score));
            return result;
        }

        /// <summary>
        /// How much wider one direction's pitch is than the other's. A single device in a direction is
        /// judged against that direction's ROOM length, so one lonely row in a very wide room is
        /// penalised while a single row in a narrow corridor is not.
        /// </summary>
        private static double PitchRatio(AxisOption a, int nA, double lenA, AxisOption b, int nB, double lenB)
        {
            if (nA == 1 && nB == 1) return 1.0;
            if (nA == 1) return Math.Max(1.0, lenA / Math.Max(1e-9, b.PitchFt));
            if (nB == 1) return Math.Max(1.0, lenB / Math.Max(1e-9, a.PitchFt));
            return Math.Max(a.PitchFt, b.PitchFt) / Math.Max(1e-9, Math.Min(a.PitchFt, b.PitchFt));
        }

        /// <summary>How many of the nA x nB device blocks are actually usable.</summary>
        private static int CountUsableBlocks(
            AxisOption a, int nA, int spanA, AxisOption b, int nB, int spanB,
            Func<int, int, bool> isTileUsable)
        {
            int count = 0;
            for (int j = 0; j < nB; j++)
                for (int i = 0; i < nA; i++)
                    if (BlockUsable(a, i, spanA, b, j, spanB, isTileUsable))
                        count++;
            return count;
        }

        /// <summary>
        /// Whether every tile in one device's block is admissible. A device must sit on the centre of
        /// a WHOLE block: two devices sharing a tile is never acceptable, and a device half on a cut
        /// edge tile reads as off-centre on a real ceiling.
        /// </summary>
        private static bool BlockUsable(
            AxisOption a, int i, int spanA, AxisOption b, int j, int spanB,
            Func<int, int, bool> isTileUsable)
        {
            for (int dr = 0; dr < spanB; dr++)
                for (int dc = 0; dc < spanA; dc++)
                {
                    int iu = a.Start + i * a.Pitch + dc;
                    int iv = b.Start + j * b.Pitch + dr;
                    if (!isTileUsable(iu, iv)) return false;
                }
            return true;
        }

        /// <summary>
        /// Materialises the chosen pattern. A block whose tiles are not all usable is SKIPPED rather
        /// than aborting, because an irregular footprint legitimately removes tiles; the caller has
        /// already checked that enough blocks survive.
        /// </summary>
        private static Result Build(
            in CeilingGrid grid,
            AxisOption a, int nA, int spanA,
            AxisOption b, int nB, int spanB,
            bool longAlongA,
            Func<int, int, bool> isTileUsable)
        {
            var r = new Result
            {
                SpanA = spanA,
                SpanB = spanB,
                Columns = nA,
                Rows = nB,
                LongAxisIsAlongA = longAlongA,
                PitchAFt = a.Pitch == 0 ? 0.0 : a.Pitch * grid.UFt,
                PitchBFt = b.Pitch == 0 ? 0.0 : b.Pitch * grid.VFt,
                StartA = a.Start,
                PitchA = a.Pitch,
                StartB = b.Start,
                PitchB = b.Pitch
            };

            for (int j = 0; j < nB; j++)
                for (int i = 0; i < nA; i++)
                {
                    if (!BlockUsable(a, i, spanA, b, j, spanB, isTileUsable)) continue;

                    // Device centre = mean of its tiles' centres, so a 1x2 block sits exactly between
                    // the two tile centres rather than on the first one.
                    double sx = 0.0, sy = 0.0;
                    int n = 0;
                    for (int dr = 0; dr < spanB; dr++)
                        for (int dc = 0; dc < spanA; dc++)
                        {
                            CeilingGridMath.IndexToWorld(
                                a.Start + i * a.Pitch + dc,
                                b.Start + j * b.Pitch + dr,
                                in grid, out double tx, out double ty);
                            sx += tx; sy += ty; n++;
                        }

                    if (n > 0) r.Positions.Add(new[] { sx / n, sy / n });
                }

            r.PlacedCount = r.Positions.Count;
            return r;
        }

        /// <summary>One solved arrangement along a single direction.</summary>
        public sealed class AxisLayout
        {
            /// <summary>First tile index of the first device.</summary>
            public int Start;

            /// <summary>Tiles between devices. Zero for a single device.</summary>
            public int Pitch;

            /// <summary>Devices along this direction.</summary>
            public int Count;

            /// <summary>Device-to-device pitch, feet (the room length when Count == 1).</summary>
            public double PitchFt;

            /// <summary>Wall gap at each end, feet: the X of X-2X-X.</summary>
            public double Gap1Ft;
            public double Gap2Ft;

            /// <summary>True when a wall gap had to be negative, i.e. the room is too small for the
            /// requested pitch at this count. The caller decides whether to reduce the count.</summary>
            public bool OverflowsWall;
        }

/// <summary>
        /// Solves a balanced START TILE, treating the caller's pitch as a MAXIMUM rather than a fixed
        /// value, and reports how many devices fit in each direction.
        /// </summary>
        /// <remarks>
        /// THIS is the entry point the three placement engines use, because all of them already derive
        /// a maximum pitch from their own spacing rules - they do not arrive with a fixed device count.
        ///
        /// It fixes exactly the defect in the existing sprinkler array: the pitch there is already a
        /// whole number of tiles, but the array ANCHORS at the first tile of the room and appends the
        /// far index when the stride misses it, leaving the wall gaps uneven.
        ///
        /// WHY THE PITCH IS TREATED AS A MAXIMUM
        /// -----------------------------------
        /// X-2X-X requires X = half the pitch, and the room length must then be about
        /// <c>count x pitch</c>. A pitch floored to the largest whole tile count that fits max spacing
        /// frequently cannot satisfy that: a 20 ft room with 2 ft tiles and a 7-tile (14 ft) pitch
        /// yields two columns whose wall gaps are 3 ft each - balanced, but nowhere near the 7 ft X the
        /// pitch implies. So the solver searches pitches DOWN from the caller's maximum and keeps the
        /// LARGEST one that genuinely achieves X-2X-X, which is both compliant and closest to what
        /// the spacing rule asked for.
        ///
        /// Does NOT verify that every block is usable; a beam sitting on one tile centre is the
        /// caller's existing bounded-snap logic's job, and rejecting a whole layout over one
        /// obstruction would discard an otherwise good pattern.
        /// </remarks>
        public static bool TrySolveBalancedLayout(
            in CeilingGrid grid,
            double roomMinX, double roomMinY, double roomMaxX, double roomMaxY,
            int maxPitchTilesA, int maxPitchTilesB,
            out AxisLayout a, out AxisLayout b, out double toleranceTiles)
        {
            a = null;
            b = null;
            toleranceTiles = 0.5;

            if (!grid.IsValid || !grid.IsPlausible) return false;
            if (roomMaxX <= roomMinX || roomMaxY <= roomMinY) return false;
            if (maxPitchTilesA < 1 || maxPitchTilesB < 1) return false;

            CeilingGrid lattice = grid;

            LocalBounds(in lattice, roomMinX, roomMinY, roomMaxX, roomMaxY,
                        out double loA, out double hiA, out double loB, out double hiB);
            IndexRange(in lattice, roomMinX, roomMinY, roomMaxX, roomMaxY,
                       out int iuMin, out int iuMax, out int ivMin, out int ivMax);

            double lenA = hiA - loA;
            double lenB = hiB - loB;
            if (lenA <= 0 || lenB <= 0) return false;

            foreach (double tol in Tolerances)
            {
                AxisLayout solvedA = SolveAxis(iuMin, iuMax, loA, lenA, lattice.UFt, maxPitchTilesA, tol * lattice.UFt);
                AxisLayout solvedB = SolveAxis(ivMin, ivMax, loB, lenB, lattice.VFt, maxPitchTilesB, tol * lattice.VFt);
                if (solvedA == null || solvedB == null) continue;

                // Reject a wildly lopsided pair: one direction much wider than the other is a worse
                // layout than a slightly looser but proportionate one, and the next tolerance tier
                // may still find that.
                double ratio = PitchRatio(solvedA, solvedB);
                if (ratio > MaxPitchRatio + 1e-9) continue;

                toleranceTiles = tol;
                a = solvedA;
                b = solvedB;
                return true;
            }

            return false;
        }

        private static double PitchRatio(AxisLayout a, AxisLayout b)
        {
            if (a.Count == 1 && b.Count == 1) return 1.0;
            if (a.Count == 1) return Math.Max(1.0, a.PitchFt / Math.Max(1e-9, b.PitchFt));
            if (b.Count == 1) return Math.Max(1.0, b.PitchFt / Math.Max(1e-9, a.PitchFt));
            return Math.Max(a.PitchFt, b.PitchFt) / Math.Max(1e-9, Math.Min(a.PitchFt, b.PitchFt));
        }

        /// <summary>
        /// The best arrangement along one direction for some whole-tile pitch at or below
        /// <paramref name="maxPitchTiles"/>, or null when no pitch achieves X-2X-X within tolerance.
        ///
        /// The count is derived rather than searched blindly: because the two wall gaps must average
        /// half the pitch, the room length must be about <c>count x pitch</c>, so the count for a given
        /// pitch is essentially fixed at <c>round(len / pitch)</c>. Only that count and its two
        /// neighbours can satisfy the proportion test, so those are the only ones tried. The START is
        /// then chosen to balance the two wall gaps against each other.
        ///
        /// Largest qualifying pitch wins, because a wider pitch means fewer devices, which is what a
        /// maximum-spacing rule is asking for.
        /// </summary>
        private static AxisLayout SolveAxis(
            int idxMin, int idxMax, double lo, double roomLen, double moduleFt,
            int maxPitchTiles, double tolFt)
        {
            if (roomLen <= 0 || moduleFt <= 0) return null;

            int topPitch = Math.Min(maxPitchTiles, Math.Max(1, idxMax - idxMin + 1));

for (int p = topPitch; p >= 1; p--)
            {
                double pitchFt = p * moduleFt;
                if (pitchFt > roomLen + tolFt) continue;   // a lone device already spans the room

                int baseCount = (int)Math.Round(roomLen / pitchFt);
                if (baseCount < 1) baseCount = 1;

                // A single device is only a candidate in a room narrower than two tiles. Otherwise a
                // large pitch would "win" trivially: one device in a 20 ft room has balanced 10 ft/10 ft
                // gaps and no 2X at all, so it would satisfy the loose test at the largest pitch and
                // leave the room with a single lonely column. Coverage is the rule engine's job, but
                // handing it a one-device layout is never the right starting point for a room that can
                // plainly hold more.
                bool allowSingle = roomLen < 2.0 * moduleFt;

                AxisLayout best = null;
                for (int dc = -1; dc <= 1; dc++)
                {
                    int n = baseCount + dc;
                    if (n < 1) continue;
                    if (n == 1 && !allowSingle) continue;

                    // Proportion: the two gaps must average half the pitch. Independent of start.
                    double avgGap = (roomLen - (n - 1) * pitchFt) / 2.0;
                    double proportionErr = n == 1 ? 0.0 : Math.Abs(avgGap - pitchFt / 2.0);
                    if (n > 1 && proportionErr > tolFt + 1e-9) continue;

                    // A negative average gap means the devices cannot fit at this pitch and count.
                    if (avgGap < -tolFt) continue;

                    AxisLayout candidate = BestStart(idxMin, idxMax, lo, roomLen, moduleFt, n, p, tolFt);
                    if (candidate == null) continue;

                    // Prefer the layout whose wall gaps mirror each other most closely. Ties keep the
                    // earlier (larger) pitch, which this loop order already provides.
                    if (best == null
                        || Math.Abs(candidate.Gap1Ft - candidate.Gap2Ft)
                           < Math.Abs(best.Gap1Ft - best.Gap2Ft) - 1e-9)
                    {
                        best = candidate;
                    }
                }

                if (best != null) return best;
            }

            return null;
        }

        /// <summary>
        /// The start tile whose two wall gaps are closest to mirroring each other for a fixed pitch
        /// and count, or null when no start in range qualifies.
        /// </summary>
        private static AxisLayout BestStart(
            int idxMin, int idxMax, double lo, double roomLen, double moduleFt,
            int n, int pitchTiles, double tolFt)
        {
            double pitchFt = pitchTiles * moduleFt;
            AxisLayout best = null;
            double bestSymmetry = double.MaxValue;

            for (int c0 = idxMin; c0 <= idxMax; c0++)
            {
                int lastIndex = n == 1 ? c0 : c0 + (n - 1) * pitchTiles;
                if (lastIndex > idxMax) break;

                double w1 = (c0 + 0.5) * moduleFt - lo;
                double w2 = (lo + roomLen) - ((lastIndex + 0.5) * moduleFt);

                double symmetry = Math.Abs(w1 - w2) / 2.0;

                if (n == 1)
                {
                    // One device: best balance is the tile centre nearest the room's centre.
                    if (symmetry >= bestSymmetry - 1e-12) continue;
                    bestSymmetry = symmetry;
                    best = new AxisLayout
                    {
                        Start = c0, Pitch = 0, Count = 1,
                        PitchFt = roomLen, Gap1Ft = w1, Gap2Ft = w2,
                        OverflowsWall = w1 < 0.0 || w2 < 0.0
                    };
                    continue;
                }

                if (symmetry > tolFt + 1e-9) continue;
                if (symmetry >= bestSymmetry - 1e-12) continue;

                bestSymmetry = symmetry;
                best = new AxisLayout
                {
                    Start = c0,
                    Pitch = pitchTiles,
                    Count = n,
                    PitchFt = pitchFt,
                    Gap1Ft = w1,
                    Gap2Ft = w2,
                    OverflowsWall = w1 < 0.0 || w2 < 0.0
                };
            }

return best;
        }

        /// <summary>
        /// Moves an already-computed set of positions onto the centre of the nearest free block of
        /// whole tiles, without changing how many there are.
        /// </summary>
        /// <remarks>
        /// SAFETY NET, for rooms where the X-2X-X search found nothing: a grid should never leave a
        /// device sitting BETWEEN tiles. Each position is mapped to the nearest free tile block; two
        /// devices never share a tile, and a position with no free block anywhere is left exactly
        /// where it was rather than dropped — dropping a device would silently reduce protection.
        ///
        /// Unlike the solver this preserves the original PATTERN (row/column lines are kept rather
        /// than each point drifting independently), so a layout that could not be balanced still
        /// reads as a regular array on the grid.
        /// </remarks>
        /// <param name="positions">Positions to move, world XY feet. Not modified.</param>
        /// <param name="isTileUsable">Per-tile admissibility, as for <see cref="TryGenerate"/>.</param>
        /// <returns>
        /// The adjusted positions, plus how many were snapped and how many had to be left alone.
        /// </returns>
        public static List<double[]> SnapPositionsToTiles(
            IList<double[]> positions,
            in CeilingGrid grid,
            Func<int, int, bool> isTileUsable,
            out int snapped,
            out int kept)
        {
            snapped = 0;
            kept = 0;
            var result = new List<double[]>();
            if (positions == null || positions.Count == 0) return result;

            CeilingGrid lattice = grid;

            if (!lattice.IsValid || !lattice.IsPlausible || isTileUsable == null)
            {
                result.AddRange(positions);
                kept = positions.Count;
                return result;
            }

            // Every usable tile's centre, keyed by index, as a candidate landing spot.
            var tileOf = new Dictionary<long, double[]>();
            var perPosition = new List<List<double[]>>();

            foreach (double[] p in positions)
            {
                CeilingGridMath.SnapToTileCenter(p[0], p[1], in lattice, out double cx, out double cy);
                CeilingGridMath.WorldToIndex(cx, cy, in lattice, out int iu, out int iv);
                long key = CeilingGridMath.IndexKey(iu, iv);

                if (!tileOf.ContainsKey(key) && isTileUsable(iu, iv))
                    tileOf[key] = new[] { cx, cy };

                perPosition.Add(new List<double[]> { tileOf.ContainsKey(key) ? tileOf[key] : null });
            }

            var occupied = new HashSet<long>();

            for (int n = 0; n < positions.Count; n++)
            {
                double[] target = perPosition[n][0];

                if (target != null)
                {
                    CeilingGridMath.WorldToIndex(target[0], target[1], in lattice, out int iu, out int iv);
                    long key = CeilingGridMath.IndexKey(iu, iv);
                    if (occupied.Add(key))
                    {
                        result.Add(target);
                        snapped++;
                        continue;
                    }
                }

                // Taken, or the snapped tile was not usable: take the nearest FREE usable tile.
                CeilingGridMath.SnapToTileCenter(positions[n][0], positions[n][1], in lattice,
                                                out double cx, out double cy);
                CeilingGridMath.WorldToIndex(cx, cy, in lattice, out int su, out int sv);

                double bestD = double.MaxValue;
                double[] best = null;
                for (int dv = -3; dv <= 3; dv++)
                    for (int du = -3; du <= 3; du++)
                    {
                        int iu = su + du, iv = sv + dv;
                        if (!isTileUsable(iu, iv)) continue;
                        long key = CeilingGridMath.IndexKey(iu, iv);
                        if (occupied.Contains(key)) continue;

                        CeilingGridMath.IndexToWorld(iu, iv, in lattice, out double tx, out double ty);
                        double dx = tx - positions[n][0], dy = ty - positions[n][1];
                        double d = dx * dx + dy * dy;
                        if (d < bestD) { bestD = d; best = new[] { tx, ty }; }
                    }

                if (best != null)
                {
                    CeilingGridMath.WorldToIndex(best[0], best[1], in lattice, out int bu, out int bv);
                    occupied.Add(CeilingGridMath.IndexKey(bu, bv));
                    result.Add(best);
                    snapped++;
                }
                else
                {
                    // Never drop a device because the grid is full.
                    result.Add(positions[n]);
                    kept++;
                }
            }

            return result;
        }

        /// <summary>Tiles a device of <paramref name="sizeFt"/> spans in a <paramref name="moduleFt"/>
        /// grid, rounded with a tolerance so a nominal 2 ft device is one 600 mm tile, not two.</summary>
        private static int SpanFor(double sizeFt, double moduleFt)
        {
            if (sizeFt <= 0 || moduleFt <= 0) return 1;
            return Math.Max(1, (int)Math.Ceiling(sizeFt / moduleFt - SpanTolerance));
        }

        /// <summary>
        /// The room's wall extents along the lattice's own axes (û and v̂), feet from the lattice
        /// origin. Projecting the bounding-box corners is what makes this correct for a ROTATED grid;
        /// measuring along world X/Y would silently mix axes.
        /// </summary>
        private static void LocalBounds(
            in CeilingGrid grid,
            double minX, double minY, double maxX, double maxY,
            out double loA, out double hiA, out double loB, out double hiB)
        {
            double dx0 = minX - grid.OriginX, dy0 = minY - grid.OriginY;
            double dx1 = maxX - grid.OriginX, dy1 = maxY - grid.OriginY;

            double a00 = dx0 * grid.Cos + dy0 * grid.Sin;
            double a01 = dx0 * grid.Cos + dy1 * grid.Sin;
            double a10 = dx1 * grid.Cos + dy0 * grid.Sin;
            double a11 = dx1 * grid.Cos + dy1 * grid.Sin;
            loA = Math.Min(Math.Min(a00, a01), Math.Min(a10, a11));
            hiA = Math.Max(Math.Max(a00, a01), Math.Max(a10, a11));

            double b00 = -dx0 * grid.Sin + dy0 * grid.Cos;
            double b01 = -dx0 * grid.Sin + dy1 * grid.Cos;
            double b10 = -dx1 * grid.Sin + dy0 * grid.Cos;
            double b11 = -dx1 * grid.Sin + dy1 * grid.Cos;
            loB = Math.Min(Math.Min(b00, b01), Math.Min(b10, b11));
            hiB = Math.Max(Math.Max(b00, b01), Math.Max(b10, b11));
        }

        /// <summary>
        /// The tile index range covering a room, widened by one tile each way exactly as
        /// <c>CeilingGridMath.EnumerateTileCenters</c> does, so the generator and the enumerator
        /// agree on tile membership.
        /// </summary>
        private static void IndexRange(
            in CeilingGrid grid,
            double minX, double minY, double maxX, double maxY,
            out int iuMin, out int iuMax, out int ivMin, out int ivMax)
        {
            LocalBounds(in grid, minX, minY, maxX, maxY,
                        out double loA, out double hiA, out double loB, out double hiB);

            iuMin = (int)Math.Floor(loA / grid.UFt) - 1;
            iuMax = (int)Math.Floor(hiA / grid.UFt) + 1;
            ivMin = (int)Math.Floor(loB / grid.VFt) - 1;
            ivMax = (int)Math.Floor(hiB / grid.VFt) + 1;
        }
    }
}
