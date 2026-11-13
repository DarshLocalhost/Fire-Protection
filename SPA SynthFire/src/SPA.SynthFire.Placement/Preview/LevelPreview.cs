using System;
using System.Collections.Generic;
using System.Linq;
using SPA.SynthFire.Core.Devices;
using SPA.SynthFire.Core.Geometry;

namespace SPA.SynthFire.Placement.Preview;

/// <summary>Layout status of a room, for the plan-canvas legend/colouring.</summary>
public enum RoomPreviewStatus { Placed, ReviewRequired, Skipped }

/// <summary>
/// One placed device to draw on the plan (feet, plan coordinates). <paramref name="Mount"/>
/// and <paramref name="OrientationRadians"/> let the canvas draw a sidewall as a directional
/// wedge pointing across the room and a pendent as a plain dot, so the plan looks like what
/// will be placed. Orientation is the throw direction (radians, +X = 0); null for circular.
/// <paramref name="Z"/> is the device elevation (feet) for 3D visualisation; 0 when unknown.
/// </summary>
public sealed record DevicePreview(
    double X, double Y, string Kind = "sprinkler",
    DeviceMount Mount = DeviceMount.Ceiling,
    double? OrientationRadians = null,
    double Z = 0);

/// <summary>
/// A Revit-free, WPF-free snapshot of one room for the coverage plan canvas: its
/// footprint, the sampled <see cref="CoverageField"/> cells (green/red/overlap/grey),
/// the device points, the obstruction boxes, and headline properties for the info
/// panel. Built from a <see cref="RoomPlacementResult"/> so the same model drives the
/// standalone preview lab and the in-add-in design window.
/// </summary>
public sealed record RoomPreview
{
    public required string RoomId { get; init; }
    public required string Name { get; init; }
    public required Rectangle2D Footprint { get; init; }

    /// <summary>The true room outline in plan, or null to fall back to the footprint rectangle.</summary>
    public Polygon2D? Boundary { get; init; }

    public required IReadOnlyList<CoverageCell> Cells { get; init; }
    public required IReadOnlyList<DevicePreview> Devices { get; init; }
    public required IReadOnlyList<ObstacleBox> Obstacles { get; init; }
    public required RoomPreviewStatus Status { get; init; }

    /// <summary>Geometric coverage over protectable floor (0..100), from the CoverageField.</summary>
    public double CoveragePercent { get; init; }

    /// <summary>Number of review/non-compliance notes on this room (for the "Non-Compliances" field).</summary>
    public int NonCompliances { get; init; }

    // Coverage-shape parameters this room's field was built with, retained so a manually
    // placed head (Phase 3 click-to-place) can be overlaid and the coverage re-computed
    // WITHOUT a solver re-run.
    /// <summary>Circle radius (ft) used for symmetric mounts. 0 when unknown.</summary>
    public double CoverageRadiusFt { get; init; }
    /// <summary>Sidewall throw distance (ft) used for directional coverage. 0 when unknown.</summary>
    public double ThrowFt { get; init; }
    /// <summary>Sidewall half lateral width (ft) used for directional coverage. 0 when unknown.</summary>
    public double HalfWidthFt { get; init; }
    /// <summary>Cell size (ft) the coverage field was sampled at, for rebuilds.</summary>
    public double CellSizeFt { get; init; } = 1.0;

    /// <summary>Finished floor elevation (Z, feet) for 3D visualisation.</summary>
    public double FloorElevation { get; init; }

    /// <summary>Ceiling elevation (Z, feet) for 3D visualisation.</summary>
    public double CeilingElevation { get; init; }

    public double AreaSqFt => Boundary?.Area ?? Footprint.Area;
    public int DeviceCount => Devices.Count;
}

/// <summary>
/// A whole-level snapshot for the plan canvas: every room with its coverage field and
/// devices, plus the overall bounds so the canvas can fit-to-view. Purely geometric and
/// Revit-free, so it is unit-testable and reusable across the preview lab and the add-in.
/// </summary>
public sealed record LevelPreviewModel
{
    public required string LevelName { get; init; }
    public required IReadOnlyList<RoomPreview> Rooms { get; init; }

    /// <summary>Union of all room footprints (feet); default/empty when there are no rooms.</summary>
    public Rectangle2D Bounds { get; init; }

    public int TotalDevices => Rooms.Sum(r => r.DeviceCount);
    public int TotalNonCompliances => Rooms.Sum(r => r.NonCompliances);
    public int RoomCount => Rooms.Count;

    /// <summary>Returns a copy with the room matching <paramref name="updated"/>'s RoomId replaced.
    /// Used to refresh a single room after a manual click-to-place without rebuilding the level.</summary>
    public LevelPreviewModel WithRoom(RoomPreview updated)
    {
        var rooms = Rooms
            .Select(r => r.RoomId == updated.RoomId ? updated : r)
            .ToList();
        return this with { Rooms = rooms };
    }
}