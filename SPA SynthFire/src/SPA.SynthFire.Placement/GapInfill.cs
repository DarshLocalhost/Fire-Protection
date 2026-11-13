using System;
using System.Collections.Generic;
using System.Linq;
using SPA.SynthFire.Core.Geometry;

namespace SPA.SynthFire.Placement;

/// <summary>
/// Revit-free residual-gap filler. After the primary grid/wall layout leaves red
/// uncovered floor (obstruction shadows, narrow or irregular areas, or a room whose
/// grid points were all clipped away by the room outline), this drops extra candidate
/// heads into the gaps so no floor ships uncovered. It is deliberately conservative:
/// heads land only on protectable floor (inside the boundary, off obstructions),
/// honour the rule set's minimum spacing, and every filled layout stays
/// <see cref="PlacementStatus.PlacedReviewRequired"/> — this covers + flags, it never
/// auto-exempts an area (allowable-omission rules are not FPE-verified here).
///
/// Circular (pendent/upright/free) only. Sidewall directional infill is a separate
/// design and is intentionally out of scope.
/// </summary>
public static class GapInfill
{
    /// <param name="Added">Points added to cover residual gaps (feet).</param>
    /// <param name="RemainingUncoveredCells">
    /// Protectable cells still uncovered after filling (min-spacing limited); &gt; 0 means
    /// a genuine residual the engineer must review/place manually.
    /// </param>
    public sealed record Result(IReadOnlyList<(double X, double Y)> Added, int RemainingUncoveredCells);

    private static readonly Result Empty = new(Array.Empty<(double, double)>(), 0);

    /// <summary>
    /// Greedily fills the uncovered cells of a coverage field with circular heads.
    /// Reuses <see cref="CoverageField"/> for sampling so the fill agrees exactly with
    /// what the plan paints (obstructed / out-of-boundary cells are already excluded, so
    /// candidates only ever sit on protectable floor). Each step adds the uncovered cell
    /// centre that covers the most still-uncovered cells while staying at least
    /// <paramref name="minSpacingFt"/> from every existing and added head. Deterministic:
    /// cells are evaluated in field order and the first maximum wins.
    /// </summary>
    public static Result FillCircular(
        Rectangle2D footprint,
        Polygon2D? boundary,
        IReadOnlyList<ObstacleBox> obstacles,
        IReadOnlyList<(double X, double Y)> existing,
        double radiusFt,
        double cellSizeFt,
        double minSpacingFt,
        int maxAdds = 64)
    {
        ArgumentNullException.ThrowIfNull(existing);
        if (radiusFt <= 0 || footprint.Width <= 0 || footprint.Height <= 0)
            return Empty;

        CoverageField field = CoverageField.Build(footprint, existing, radiusFt, cellSizeFt, obstacles, boundary);

        var uncovered = field.Cells
            .Where(c => c.Kind == CoverageCellKind.Uncovered)
            .Select(c => (c.CenterX, c.CenterY))
            .ToList();
        if (uncovered.Count == 0)
            return Empty;

        double r2 = radiusFt * radiusFt;
        double minSp2 = minSpacingFt > 0 ? minSpacingFt * minSpacingFt : 0.0;

        var heads = existing.ToList();
        var added = new List<(double X, double Y)>();

        while (uncovered.Count > 0 && added.Count < maxAdds)
        {
            int bestIdx = -1, bestGain = 0;
            for (int k = 0; k < uncovered.Count; k++)
            {
                var cand = uncovered[k];
                if (minSp2 > 0 && heads.Any(h => Dist2(h, cand) < minSp2))
                    continue; // too close to an existing/added head — would cram

                int gain = 0;
                foreach (var u in uncovered)
                    if (Dist2(cand, u) <= r2) gain++;

                if (gain > bestGain) { bestGain = gain; bestIdx = k; }
            }

            if (bestIdx < 0)
                break; // every remaining gap cell is min-spacing blocked — leave residual

            var chosen = uncovered[bestIdx];
            added.Add(chosen);
            heads.Add(chosen);
            uncovered = uncovered.Where(u => Dist2(chosen, u) > r2).ToList();
        }

        return new Result(added, uncovered.Count);
    }

    private static double Dist2((double X, double Y) a, (double X, double Y) b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}
