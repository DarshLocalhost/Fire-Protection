using System;
using System.Collections.Generic;
using System.Linq;
using SPA.SynthFire.Core.Geometry;

namespace SPA.SynthFire.Placement;

/// <summary>One computed sidewall head: a plan point plus the direction it throws
/// INTO the room (radians, standard math convention, +X = 0).</summary>
public readonly record struct SidewallPoint(double X, double Y, double OrientationRadians);

/// <summary>Result of laying sidewall heads along a room's wall(s).</summary>
public sealed record SidewallLayoutResult
{
    public required IReadOnlyList<SidewallPoint> Points { get; init; }
    public required bool Degenerate { get; init; }
    public bool ReviewRequired { get; init; }
    public double ActualMaxSpacing { get; init; }
    public double ActualCoveragePerPoint { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Revit-free sidewall layout: spaces heads along the room's longest wall (adding the
/// opposite parallel wall when the room is deeper than one throw) and orients each head
/// across the room. This mirrors the Revit writer's sidewall strategy so the coverage
/// plan shows what will actually be placed. Geometry is derived from the room polygon
/// (or its bounding rectangle) — the live writer refines to real wall faces, so treat
/// the preview as indicative for irregular rooms. PROVISIONAL, always review before issue.
/// </summary>
public static class SidewallLayout
{
    // The head sits just off the wall face; nudge it inside so it renders within the room.
    private const double WallStandoffFt = 0.5;

    public static SidewallLayoutResult Compute(RoomInput room, double throwFt, double minSpacingFt)
    {
        double width = room.Footprint.Width, height = room.Footprint.Height;
        if (width <= 0 || height <= 0 || throwFt <= 0)
            return new SidewallLayoutResult
            {
                Points = Array.Empty<SidewallPoint>(),
                Degenerate = true,
                Notes = new[] { "Degenerate footprint (zero width or height); skipped." }
            };

        var edges = Edges(room);
        var (cx, cy) = Centroid(room);
        var notes = new List<string>();
        bool review = false;

        // Longest edge = primary throw wall.
        int primaryIndex = 0;
        for (int i = 1; i < edges.Count; i++)
            if (Length(edges[i]) > Length(edges[primaryIndex])) primaryIndex = i;
        var primary = edges[primaryIndex];
        var chosen = new List<Edge> { primary };

        // If the room is deeper than one throw, add the most-parallel FAR wall (greatest
        // perpendicular distance from the primary — its true opposite, not a colinear run).
        double depth = Depth(room, primary, cx, cy);
        if (depth > throwFt)
        {
            var (pdx, pdy) = UnitDir(primary);
            var (pnx, pny) = InwardNormal(primary, cx, cy);
            Edge? opposite = null;
            double bestDist = throwFt * 0.5; // must be meaningfully across the room
            for (int i = 0; i < edges.Count; i++)
            {
                if (i == primaryIndex) continue;
                var e = edges[i];
                if (Math.Abs(Dot(UnitDir(e), (pdx, pdy))) <= 0.85) continue;
                double mx = (e.Pts.ax + e.Pts.bx) / 2.0 - primary.Pts.ax;
                double my = (e.Pts.ay + e.Pts.by) / 2.0 - primary.Pts.ay;
                double dist = mx * pnx + my * pny; // perpendicular distance into the room
                if (dist > bestDist) { bestDist = dist; opposite = e; }
            }
            if (opposite is { } opp) chosen.Add(opp);
            if (depth > 2.0 * throwFt)
            {
                review = true;
                notes.Add($"Room depth {depth:0.#} ft exceeds two sidewall throws ({2 * throwFt:0.#} ft); the centre may be uncovered — review required.");
            }
        }

        double minSpacing = Math.Max(minSpacingFt, 1.0);
        double maxStep = 0;
        var accepted = new List<SidewallPoint>();
        foreach (var edge in chosen)
        {
            double len = Length(edge);
            var (dx, dy) = UnitDir(edge);
            var (nx, ny) = InwardNormal(edge, cx, cy);
            double orient = Math.Atan2(ny, nx);

            int count = Math.Max(1, (int)Math.Ceiling(len / throwFt));
            double step = len / count;
            maxStep = Math.Max(maxStep, step);

            for (int i = 0; i < count; i++)
            {
                double t = step * (i + 0.5);
                double px = edge.Pts.ax + dx * t + nx * WallStandoffFt;
                double py = edge.Pts.ay + dy * t + ny * WallStandoffFt;

                bool tooClose = accepted.Any(a =>
                    (a.X - px) * (a.X - px) + (a.Y - py) * (a.Y - py) < minSpacing * minSpacing);
                if (tooClose) continue;

                accepted.Add(new SidewallPoint(px, py, orient));
            }
        }

        return new SidewallLayoutResult
        {
            Points = accepted,
            Degenerate = false,
            ReviewRequired = review,
            ActualMaxSpacing = maxStep,
            ActualCoveragePerPoint = maxStep * throwFt,
            Notes = notes
        };
    }

    private readonly record struct Edge((double ax, double ay, double bx, double by) Pts);

    private static IReadOnlyList<Edge> Edges(RoomInput room)
    {
        if (room.Boundary is { } poly && poly.Vertices.Count >= 3)
        {
            var v = poly.Vertices;
            var list = new List<Edge>(v.Count);
            for (int i = 0; i < v.Count; i++)
            {
                var a = v[i];
                var b = v[(i + 1) % v.Count];
                list.Add(new Edge((a.X, a.Y, b.X, b.Y)));
            }
            return list;
        }

        var f = room.Footprint;
        return new[]
        {
            new Edge((f.MinX, f.MinY, f.MaxX, f.MinY)),
            new Edge((f.MaxX, f.MinY, f.MaxX, f.MaxY)),
            new Edge((f.MaxX, f.MaxY, f.MinX, f.MaxY)),
            new Edge((f.MinX, f.MaxY, f.MinX, f.MinY))
        };
    }

    private static (double, double) Centroid(RoomInput room)
    {
        if (room.Boundary is { } poly && poly.Vertices.Count >= 3)
        {
            double sx = 0, sy = 0;
            foreach (var p in poly.Vertices) { sx += p.X; sy += p.Y; }
            return (sx / poly.Vertices.Count, sy / poly.Vertices.Count);
        }
        return (room.Footprint.CenterX, room.Footprint.CenterY);
    }

    private static double Length(Edge e)
    {
        double dx = e.Pts.bx - e.Pts.ax, dy = e.Pts.by - e.Pts.ay;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static (double, double) UnitDir(Edge e)
    {
        double dx = e.Pts.bx - e.Pts.ax, dy = e.Pts.by - e.Pts.ay;
        double len = Math.Sqrt(dx * dx + dy * dy);
        return len <= 1e-9 ? (0, 0) : (dx / len, dy / len);
    }

    // Unit normal of the edge that points toward the room centroid (into the room).
    private static (double, double) InwardNormal(Edge e, double cx, double cy)
    {
        var (dx, dy) = UnitDir(e);
        double nx = -dy, ny = dx;
        double mx = (e.Pts.ax + e.Pts.bx) / 2.0, my = (e.Pts.ay + e.Pts.by) / 2.0;
        if (nx * (cx - mx) + ny * (cy - my) < 0) { nx = -nx; ny = -ny; }
        return (nx, ny);
    }

    // How far the room extends inward from this wall (max projection of any vertex onto the inward normal).
    private static double Depth(RoomInput room, Edge e, double cx, double cy)
    {
        var (nx, ny) = InwardNormal(e, cx, cy);
        double ax = e.Pts.ax, ay = e.Pts.ay;
        double max = 0;
        if (room.Boundary is { } poly && poly.Vertices.Count >= 3)
        {
            foreach (var p in poly.Vertices)
                max = Math.Max(max, (p.X - ax) * nx + (p.Y - ay) * ny);
            return max;
        }
        var f = room.Footprint;
        foreach (var (vx, vy) in new[] { (f.MinX, f.MinY), (f.MaxX, f.MinY), (f.MaxX, f.MaxY), (f.MinX, f.MaxY) })
            max = Math.Max(max, (vx - ax) * nx + (vy - ay) * ny);
        return max;
    }

    private static double Dot((double x, double y) a, (double x, double y) b) => a.x * b.x + a.y * b.y;
}
