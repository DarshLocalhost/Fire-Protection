using System;
using System.Collections.Generic;

namespace SPA.SynthFire.Placement;

/// <summary>Result of laying out a coverage grid in one room.</summary>
public sealed record GridLayoutResult
{
    public required IReadOnlyList<(double X, double Y)> Points { get; init; }
    public required bool Degenerate { get; init; }
    public required bool ReviewRequired { get; init; }
    public double ActualMaxSpacing { get; init; }
    public double ActualCoveragePerPoint { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = new List<string>();
}

/// <summary>
/// Shared, Revit-free coverage-grid engine used for both sprinklers and ceiling
/// detectors. Lays out a regular grid honouring a maximum centre-to-centre spacing
/// and maximum coverage area per device, keeps heads a wall-standoff inside the
/// room, refines the grid when the area limit is tighter than the spacing limit,
/// and nudges points off obstructions (searching the nearest clear spot). Flags
/// review when the minimum spacing conflicts with coverage or a point cannot clear
/// an obstruction.
/// </summary>
public static class CeilingGridLayout
{
    private const double ObstacleClearanceFt = 0.5;
    private const double WallStandoffFt = 4.0 / 12.0;

    public static GridLayoutResult Compute(
        RoomInput room, double maxSpacingFt, double maxCoverageSqFt, double minSpacingFt)
    {
        double width = room.Footprint.Width;
        double height = room.Footprint.Height;
        var notes = new List<string>();

        if (width <= 0 || height <= 0)
        {
            return new GridLayoutResult
            {
                Points = Array.Empty<(double, double)>(),
                Degenerate = true,
                ReviewRequired = false,
                Notes = new[] { "Degenerate footprint (zero width or height); skipped." }
            };
        }

        int countX = Math.Max(1, (int)Math.Ceiling(width / maxSpacingFt));
        int countY = Math.Max(1, (int)Math.Ceiling(height / maxSpacingFt));

        int guard = 0;
        while ((width / countX) * (height / countY) > maxCoverageSqFt && guard++ < 10_000)
        {
            if (width / countX >= height / countY) countX++;
            else countY++;
        }

        double spacingX = width / countX;
        double spacingY = height / countY;
        bool reviewRequired = false;

        if (countX > 1 && spacingX < minSpacingFt)
        {
            reviewRequired = true;
            notes.Add($"X spacing {spacingX:0.##} ft is below the {minSpacingFt:0.##} ft minimum; review required.");
        }
        if (countY > 1 && spacingY < minSpacingFt)
        {
            reviewRequired = true;
            notes.Add($"Y spacing {spacingY:0.##} ft is below the {minSpacingFt:0.##} ft minimum; review required.");
        }

        var points = new List<(double X, double Y)>(countX * countY);
        int nudged = 0, unresolved = 0, snapped = 0;
        double step = Math.Min(spacingX, spacingY);
        for (int i = 0; i < countX; i++)
        {
            double baseX = room.Footprint.MinX + spacingX * (i + 0.5);
            for (int j = 0; j < countY; j++)
            {
                double baseY = room.Footprint.MinY + spacingY * (j + 0.5);
                double x = baseX, y = baseY;

                // Snap to the ceiling tile grid when one is known, but only if the snapped
                // point still clears the wall standoff — otherwise keep the even-grid point.
                if (room.CeilingTile is { } tile)
                {
                    double sx = SnapToTileCenter(baseX, tile.OriginXFt, tile.ModuleXFt);
                    double sy = SnapToTileCenter(baseY, tile.OriginYFt, tile.ModuleYFt);
                    if (InsideStandoff(sx, sy, room))
                    {
                        if (Math.Abs(sx - baseX) > 1e-9 || Math.Abs(sy - baseY) > 1e-9) snapped++;
                        x = sx; y = sy;
                    }
                }

                var (nx, ny, state) = Avoid(x, y, room, step);
                if (state == ClashState.Nudged) nudged++;
                else if (state == ClashState.Unresolved) unresolved++;
                points.Add((nx, ny));
            }
        }

        if (snapped > 0) notes.Add($"{snapped} device(s) snapped to the ceiling tile grid.");
        if (nudged > 0) notes.Add($"{nudged} device(s) shifted to clear obstructions.");
        if (unresolved > 0)
        {
            reviewRequired = true;
            notes.Add($"{unresolved} device(s) could not clear an obstruction; review required.");
        }

        return new GridLayoutResult
        {
            Points = points,
            Degenerate = false,
            ReviewRequired = reviewRequired,
            ActualMaxSpacing = Math.Max(spacingX, spacingY),
            ActualCoveragePerPoint = spacingX * spacingY,
            Notes = notes
        };
    }

    private enum ClashState { Clear, Nudged, Unresolved }

    private static (double x, double y, ClashState state) Avoid(
        double x, double y, RoomInput room, double gridStep)
    {
        if (room.Obstacles.Count == 0 || IsClear(x, y, room)) return (x, y, ClashState.Clear);

        double maxShift = gridStep / 2.0;
        double stepSize = Math.Max(gridStep / 8.0, 0.25);
        double diag = 0.70710678;

        (double dx, double dy)[] dirs =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1),
            (diag, diag), (diag, -diag), (-diag, diag), (-diag, -diag)
        };

        for (double d = stepSize; d <= maxShift + 1e-9; d += stepSize)
            foreach (var (dx, dy) in dirs)
            {
                double cx = x + dx * d, cy = y + dy * d;
                if (IsClear(cx, cy, room)) return (cx, cy, ClashState.Nudged);
            }

        return (x, y, ClashState.Unresolved);
    }

    private static bool IsClear(double x, double y, RoomInput room)
    {
        if (!InsideStandoff(x, y, room)) return false;

        foreach (var o in room.Obstacles)
        {
            var b = o.Bounds;
            if (x >= b.MinX - ObstacleClearanceFt && x <= b.MaxX + ObstacleClearanceFt &&
                y >= b.MinY - ObstacleClearanceFt && y <= b.MaxY + ObstacleClearanceFt)
                return false;
        }
        return true;
    }

    private static bool InsideStandoff(double x, double y, RoomInput room)
    {
        var f = room.Footprint;
        return x >= f.MinX + WallStandoffFt && x <= f.MaxX - WallStandoffFt &&
               y >= f.MinY + WallStandoffFt && y <= f.MaxY - WallStandoffFt;
    }

    // Nearest tile-centre to v on a grid of pitch 'module' whose tile centres sit at
    // origin + (k + 0.5) * module. Returns v unchanged for a non-positive module.
    private static double SnapToTileCenter(double v, double origin, double module)
    {
        if (module <= 0) return v;
        double k = Math.Round((v - origin) / module - 0.5, MidpointRounding.AwayFromZero);
        return origin + (k + 0.5) * module;
    }
}
