using System;
using System.Collections.Generic;
using System.Linq;
using SPA.SynthFire.Core.Devices;
using SPA.SynthFire.Core.Geometry;

namespace SPA.SynthFire.Placement.Preview;

/// <summary>
/// Builds a <see cref="LevelPreviewModel"/> from solver output. Revit-free: it takes the
/// already-computed <see cref="PlacementResult"/> (which now carries each room's footprint
/// and obstacles) and runs a <see cref="CoverageField"/> per room to produce the
/// green/red/overlap cells the plan canvas paints. The protection radius is the provisional
/// half-diagonal reach derived from the actual grid spacing.
/// </summary>
public static class LevelPreviewBuilder
{
    public static LevelPreviewModel Build(
        string levelName,
        PlacementResult result,
        string deviceKind = "sprinkler",
        double cellSizeFt = 1.0)
    {
        ArgumentNullException.ThrowIfNull(result);

        var rooms = new List<RoomPreview>(result.Rooms.Count);
        foreach (var r in result.Rooms)
            rooms.Add(BuildRoom(r, deviceKind, cellSizeFt));

        return new LevelPreviewModel
        {
            LevelName = levelName,
            Rooms = rooms,
            Bounds = UnionBounds(rooms)
        };
    }

    private static RoomPreview BuildRoom(RoomPlacementResult r, string deviceKind, double cellSizeFt)
    {
        Rectangle2D footprint = r.Footprint ?? new Rectangle2D(0, 0, 0, 0);
        var devices = r.Sprinklers
            .Select(s => new DevicePreview(s.X, s.Y, deviceKind, s.Mount, s.OrientationRadians, s.Z))
            .ToList();

        // Coverage shape per device: a circle for symmetric mounts, a directional throw rectangle
        // for sidewalls — so the plan reads coverage the way the selected family actually protects.
        double circleRadius = CoverageField.DefaultRadiusForSpacing(r.ActualMaxSpacing);
        double throwFt = r.Rules?.MaxSpacingFeet ?? r.ActualMaxSpacing;
        double halfWidth = r.ActualMaxSpacing > 0 ? r.ActualMaxSpacing / 2.0 : throwFt / 2.0;

        var coverage = r.Sprinklers.Select(s => s.Mount == DeviceMount.Sidewall
            ? new CoverageDevice(s.X, s.Y, s.OrientationRadians, ThrowFt: throwFt, HalfWidthFt: halfWidth)
            : new CoverageDevice(s.X, s.Y, RadiusFt: circleRadius)).ToList();

        CoverageField field = CoverageField.Build(footprint, coverage, cellSizeFt, r.Obstacles, r.Boundary, circleRadius);

        return new RoomPreview
        {
            RoomId = r.RoomId,
            Name = r.RoomName,
            Footprint = footprint,
            Boundary = r.Boundary,
            Cells = field.Cells,
            Devices = devices,
            Obstacles = r.Obstacles,
            Status = Map(r.Status),
            CoveragePercent = field.CoveragePercent,
            NonCompliances = r.ReviewRequired ? r.Notes.Count : 0,
            CoverageRadiusFt = circleRadius,
            ThrowFt = throwFt,
            HalfWidthFt = halfWidth,
            CellSizeFt = cellSizeFt,
            FloorElevation = r.FloorElevation,
            CeilingElevation = r.CeilingElevation
        };
    }

    /// <summary>
    /// Overlays one manually placed device on an existing room and re-computes its coverage
    /// field, WITHOUT a solver re-run (which would re-derive the grid and drop the manual head).
    /// Reuses the room's retained coverage-shape params so the added head protects the same way
    /// the auto-placed heads do. For a sidewall mount the throw is oriented toward the room
    /// centroid. Returns a new <see cref="RoomPreview"/>; the original is unchanged.
    /// </summary>
    public static RoomPreview AddManualDevice(
        RoomPreview room, double x, double y, DeviceMount mount, string deviceKind = "sprinkler")
    {
        ArgumentNullException.ThrowIfNull(room);

        double? orientation = mount == DeviceMount.Sidewall ? OrientationToCentroid(room, x, y) : null;

        var devices = room.Devices.ToList();
        devices.Add(new DevicePreview(x, y, deviceKind, mount, orientation, room.CeilingElevation));

        double radius = room.CoverageRadiusFt > 0
            ? room.CoverageRadiusFt
            : CoverageField.DefaultRadiusForSpacing(room.ThrowFt);

        var coverage = devices.Select(d => d.Mount == DeviceMount.Sidewall
            ? new CoverageDevice(d.X, d.Y, d.OrientationRadians, ThrowFt: room.ThrowFt, HalfWidthFt: room.HalfWidthFt)
            : new CoverageDevice(d.X, d.Y, RadiusFt: radius)).ToList();

        double cell = room.CellSizeFt > 0 ? room.CellSizeFt : 1.0;
        CoverageField field = CoverageField.Build(
            room.Footprint, coverage, cell, room.Obstacles, room.Boundary, radius);

        return room with
        {
            Devices = devices,
            Cells = field.Cells,
            CoveragePercent = field.CoveragePercent
        };
    }

    // Throw direction (radians, +X = 0) from a wall point toward the room's centre.
    private static double OrientationToCentroid(RoomPreview room, double x, double y)
    {
        double cx, cy;
        if (room.Boundary is { } poly && poly.Vertices.Count >= 3)
        {
            double sx = 0, sy = 0;
            foreach (var p in poly.Vertices) { sx += p.X; sy += p.Y; }
            cx = sx / poly.Vertices.Count; cy = sy / poly.Vertices.Count;
        }
        else { cx = room.Footprint.CenterX; cy = room.Footprint.CenterY; }
        return Math.Atan2(cy - y, cx - x);
    }

    private static RoomPreviewStatus Map(PlacementStatus s) => s switch
    {
        PlacementStatus.Placed => RoomPreviewStatus.Placed,
        PlacementStatus.PlacedReviewRequired => RoomPreviewStatus.ReviewRequired,
        _ => RoomPreviewStatus.Skipped
    };

    private static Rectangle2D UnionBounds(IReadOnlyList<RoomPreview> rooms)
    {
        var valid = rooms.Where(r => r.Footprint.Width > 0 && r.Footprint.Height > 0).ToList();
        if (valid.Count == 0) return new Rectangle2D(0, 0, 0, 0);

        double minX = valid.Min(r => r.Footprint.MinX);
        double minY = valid.Min(r => r.Footprint.MinY);
        double maxX = valid.Max(r => r.Footprint.MaxX);
        double maxY = valid.Max(r => r.Footprint.MaxY);
        return new Rectangle2D(minX, minY, maxX, maxY);
    }
}