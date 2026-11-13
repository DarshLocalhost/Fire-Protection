using System;
using System.Collections.Generic;

namespace SPA.SynthFire.Core.Geometry;

/// <summary>A point in plan (feet).</summary>
public readonly record struct Point2D(double X, double Y);

/// <summary>
/// A closed, axis-free polygon in plan (feet) — the true room boundary read from Revit
/// (<c>Room.GetBoundarySegments</c>), so rooms render as their real L-shapes/angles rather
/// than a bounding rectangle. Vertices are in order (open ring: last is joined back to the
/// first). Provides an axis-aligned <see cref="Bounds"/>, shoelace <see cref="Area"/>, and a
/// ray-cast <see cref="Contains"/> for hit-testing and coverage clipping.
/// </summary>
public sealed class Polygon2D
{
    public IReadOnlyList<Point2D> Vertices { get; }
    public Rectangle2D Bounds { get; }
    public double Area { get; }

    public Polygon2D(IReadOnlyList<Point2D> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        if (vertices.Count < 3)
            throw new ArgumentException("A polygon needs at least 3 vertices.", nameof(vertices));

        Vertices = vertices;

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        double twiceArea = 0;
        for (int i = 0; i < vertices.Count; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % vertices.Count];
            if (a.X < minX) minX = a.X;
            if (a.Y < minY) minY = a.Y;
            if (a.X > maxX) maxX = a.X;
            if (a.Y > maxY) maxY = a.Y;
            twiceArea += a.X * b.Y - b.X * a.Y;
        }

        Bounds = new Rectangle2D(minX, minY, maxX, maxY);
        Area = Math.Abs(twiceArea) / 2.0;
    }

    /// <summary>True if (x, y) is inside the polygon (even-odd ray cast; edges count as inside-ish).</summary>
    public bool Contains(double x, double y)
    {
        if (!Bounds.Contains(x, y)) return false;

        bool inside = false;
        var v = Vertices;
        for (int i = 0, j = v.Count - 1; i < v.Count; j = i++)
        {
            bool crosses = (v[i].Y > y) != (v[j].Y > y);
            if (crosses)
            {
                double xCross = (v[j].X - v[i].X) * (y - v[i].Y) / (v[j].Y - v[i].Y) + v[i].X;
                if (x < xCross) inside = !inside;
            }
        }
        return inside;
    }
}
