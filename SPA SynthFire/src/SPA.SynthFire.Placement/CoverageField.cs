using System;
using System.Collections.Generic;
using SPA.SynthFire.Core.Devices;
using SPA.SynthFire.Core.Geometry;

namespace SPA.SynthFire.Placement;

/// <summary>How a sampled floor cell is covered by the placed devices.</summary>
public enum CoverageCellKind
{
    /// <summary>No device reaches this cell — a coverage gap (drawn red).</summary>
    Uncovered = 0,

    /// <summary>Exactly one device covers this cell (drawn light green, ~0.5 alpha).</summary>
    Single = 1,

    /// <summary>Two or more devices cover this cell — overlap (drawn stronger green, ~0.8 alpha).</summary>
    Overlap = 2,

    /// <summary>Cell centre falls inside an obstruction footprint — not protectable floor (drawn grey).</summary>
    Obstructed = 3
}

/// <summary>One sampled cell of the coverage map (feet).</summary>
public readonly record struct CoverageCell(double CenterX, double CenterY, double Size, int DeviceCount, CoverageCellKind Kind);

/// <summary>
/// One device's coverage shape for the field. A symmetric device (pendent/upright/free,
/// <paramref name="OrientationRadians"/> null) reaches every cell within <paramref name="RadiusFt"/>.
/// A sidewall device throws in <paramref name="OrientationRadians"/>: it covers a rectangle
/// extending <paramref name="ThrowFt"/> forward (into the room) and <paramref name="HalfWidthFt"/>
/// either side of the throw line — a provisional directional footprint, not a listing.
/// </summary>
public readonly record struct CoverageDevice(
    double X, double Y,
    double? OrientationRadians = null,
    double RadiusFt = 0.0,
    double ThrowFt = 0.0,
    double HalfWidthFt = 0.0)
{
    // A little slack behind the head so the standoff strip between wall and head still reads covered.
    private const double BackSlackFt = 1.0;

    public bool Covers(double cx, double cy)
    {
        double dx = cx - X, dy = cy - Y;
        if (OrientationRadians is not { } theta)
            return dx * dx + dy * dy <= RadiusFt * RadiusFt;

        // Rotate into the throw frame: forward = along the throw, lateral = across it.
        double cos = Math.Cos(theta), sin = Math.Sin(theta);
        double forward = dx * cos + dy * sin;
        double lateral = -dx * sin + dy * cos;
        return forward >= -BackSlackFt && forward <= ThrowFt && Math.Abs(lateral) <= HalfWidthFt;
    }

    public double Reach => OrientationRadians is null ? RadiusFt : Math.Max(ThrowFt, HalfWidthFt);
}

/// <summary>
/// A geometric coverage map for one room: the footprint is sampled on a regular grid,
/// and each cell is classified by how many device protection-circles reach it. This is
/// what a Tandm-style overlay renders (red = gap, light green = covered, stronger green =
/// overlap) and gives a TRUE geometric coverage percentage rather than the spacing/area
/// proxy used by <see cref="CoverageValidator"/>.
///
/// PROVISIONAL geometry: the protection radius is an idealised circular reach derived from
/// the device spacing (half the grid diagonal by default), not a listing-based coverage
/// area. Treat the percentage as indicative and review before issue.
/// </summary>
public sealed record CoverageField
{
    public required Rectangle2D Footprint { get; init; }
    public required double RadiusFt { get; init; }
    public required double CellSizeFt { get; init; }
    public required IReadOnlyList<CoverageCell> Cells { get; init; }

    public int TotalCells { get; init; }
    public int ObstructedCells { get; init; }
    public int UncoveredCells { get; init; }
    public int OverlapCells { get; init; }

    /// <summary>Protectable cells that at least one device reaches, over all protectable cells (0..1).</summary>
    public double CoverageFraction
    {
        get
        {
            int protectable = TotalCells - ObstructedCells;
            if (protectable <= 0) return 0.0;
            int covered = protectable - UncoveredCells;
            return (double)covered / protectable;
        }
    }

    public double CoveragePercent => CoverageFraction * 100.0;

    /// <summary>
    /// Default protection radius for a grid at centre-to-centre spacing <paramref name="spacingFt"/>:
    /// half the grid diagonal, so a correctly gridded room reads as fully covered and only real
    /// gaps (obstruction shadows, under-provisioned or irregular edges) show red.
    /// </summary>
    public static double DefaultRadiusForSpacing(double spacingFt) =>
        spacingFt > 0 ? spacingFt / Math.Sqrt(2.0) : 0.0;

    /// <summary>
    /// Builds a coverage field. <paramref name="cellSizeFt"/> is the sampling resolution; if
    /// non-positive it defaults to radius/4 (min 0.25 ft) to keep the grid bounded. When a
    /// <paramref name="boundary"/> polygon is given, cells whose centre falls outside it are
    /// dropped, so coverage is clipped to the true room shape (not the bounding box).
    /// </summary>
    public static CoverageField Build(
        Rectangle2D footprint,
        IReadOnlyList<(double X, double Y)> devicePoints,
        double radiusFt,
        double cellSizeFt = 0.0,
        IReadOnlyList<ObstacleBox>? obstacles = null,
        Polygon2D? boundary = null)
    {
        ArgumentNullException.ThrowIfNull(devicePoints);
        var devices = new List<CoverageDevice>(devicePoints.Count);
        foreach (var (px, py) in devicePoints)
            devices.Add(new CoverageDevice(px, py, RadiusFt: radiusFt));
        return Build(footprint, devices, cellSizeFt, obstacles, boundary, radiusFt);
    }

    /// <summary>
    /// Builds a coverage field from per-device shapes, so a sidewall's directional throw and a
    /// pendent's circle are sampled differently — the plan then reads coverage the way each
    /// selected family actually protects the floor. <paramref name="cellSizeFt"/> is the
    /// sampling resolution; if non-positive it defaults to (max reach)/4 (min 0.25 ft).
    /// </summary>
    public static CoverageField Build(
        Rectangle2D footprint,
        IReadOnlyList<CoverageDevice> devices,
        double cellSizeFt = 0.0,
        IReadOnlyList<ObstacleBox>? obstacles = null,
        Polygon2D? boundary = null,
        double reportedRadiusFt = 0.0)
    {
        ArgumentNullException.ThrowIfNull(devices);
        obstacles ??= Array.Empty<ObstacleBox>();

        double maxReach = reportedRadiusFt;
        foreach (var d in devices) maxReach = Math.Max(maxReach, d.Reach);

        if (cellSizeFt <= 0)
            cellSizeFt = Math.Max(0.25, maxReach / 4.0);

        double width = footprint.Width;
        double height = footprint.Height;

        var cells = new List<CoverageCell>();
        int obstructed = 0, uncovered = 0, overlap = 0;

        if (width <= 0 || height <= 0 || maxReach <= 0)
        {
            return new CoverageField
            {
                Footprint = footprint,
                RadiusFt = reportedRadiusFt,
                CellSizeFt = cellSizeFt,
                Cells = cells,
                TotalCells = 0
            };
        }

        int nx = Math.Max(1, (int)Math.Ceiling(width / cellSizeFt));
        int ny = Math.Max(1, (int)Math.Ceiling(height / cellSizeFt));
        double stepX = width / nx;
        double stepY = height / ny;

        for (int i = 0; i < nx; i++)
        {
            double cx = footprint.MinX + stepX * (i + 0.5);
            for (int j = 0; j < ny; j++)
            {
                double cy = footprint.MinY + stepY * (j + 0.5);

                // Clip to the real room outline: cells outside the polygon aren't floor.
                if (boundary is not null && !boundary.Contains(cx, cy)) continue;

                CoverageCellKind kind;
                int count = 0;

                if (InsideObstacle(cx, cy, obstacles))
                {
                    kind = CoverageCellKind.Obstructed;
                    obstructed++;
                }
                else
                {
                    foreach (var d in devices)
                        if (d.Covers(cx, cy)) count++;
                    kind = count switch
                    {
                        0 => CoverageCellKind.Uncovered,
                        1 => CoverageCellKind.Single,
                        _ => CoverageCellKind.Overlap
                    };
                    if (kind == CoverageCellKind.Uncovered) uncovered++;
                    else if (kind == CoverageCellKind.Overlap) overlap++;
                }

                cells.Add(new CoverageCell(cx, cy, Math.Min(stepX, stepY), count, kind));
            }
        }

        return new CoverageField
        {
            Footprint = footprint,
            RadiusFt = reportedRadiusFt,
            CellSizeFt = cellSizeFt,
            Cells = cells,
            TotalCells = cells.Count,
            ObstructedCells = obstructed,
            UncoveredCells = uncovered,
            OverlapCells = overlap
        };
    }

    private static bool InsideObstacle(double x, double y, IReadOnlyList<ObstacleBox> obstacles)
    {
        foreach (var o in obstacles)
            if (o.Bounds.Contains(x, y)) return true;
        return false;
    }
}
