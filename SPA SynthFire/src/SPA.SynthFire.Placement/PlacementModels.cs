using System;
using System.Collections.Generic;
using SPA.SynthFire.Core.Devices;
using SPA.SynthFire.Core.Geometry;
using SPA.SynthFire.Core.Standards;

namespace SPA.SynthFire.Placement;

/// <summary>
/// A Revit-free description of one room to lay out. The Revit layer builds these
/// from model rooms; the solver never touches the Revit API, so it is unit-tested
/// in isolation. All values are in feet (Revit internal units).
/// </summary>
public sealed record RoomInput
{
    public required string RoomId { get; init; }
    public required string Name { get; init; }

    /// <summary>Name of the level this room sits on (for grouping/reporting).</summary>
    public string LevelName { get; init; } = string.Empty;

    /// <summary>Axis-aligned room footprint (bounding box). Used for the coverage grid.</summary>
    public required Rectangle2D Footprint { get; init; }

    /// <summary>
    /// The true room outline in plan (from <c>Room.GetBoundarySegments</c>), so rooms
    /// render as their real L-shape/angled polygon and coverage is clipped to it. Null
    /// when only the bounding box is known (older callers, or non-polygonal rooms).
    /// </summary>
    public Polygon2D? Boundary { get; init; }

    /// <summary>Elevation (Z, feet) to place sprinkler heads at — the ceiling plane.</summary>
    public required double CeilingElevation { get; init; }

    /// <summary>Finished floor elevation (Z, feet) — the level elevation the room sits on.
    /// Used by the 3D coverage plan to position the heatmap and extrude room walls.</summary>
    public double FloorElevation { get; init; }

    /// <summary>Obstruction footprints (ducts, beams, ...) heads must avoid.</summary>
    public IReadOnlyList<ObstacleBox> Obstacles { get; init; } = new List<ObstacleBox>();

    /// <summary>
    /// Ceiling grid module to snap heads to (tile centres), or null to place on the
    /// even coverage grid without snapping. Set from the room's ceiling when known.
    /// </summary>
    public CeilingTile? CeilingTile { get; init; }

    /// <summary>
    /// Hazard class stored on this room (from the Hazard Classification tool), or
    /// null when the room was never classified. Placement uses this per room and
    /// falls back to the design-window default only when null.
    /// </summary>
    public string? HazardClassId { get; init; }
}

/// <summary>
/// A single computed device location (feet). <paramref name="Mount"/> and
/// <paramref name="OrientationRadians"/> let a sidewall carry its wall-throw direction
/// through to both the Revit writer and the coverage preview, so the two agree.
/// Orientation is the direction the device throws INTO the room (radians, standard math
/// convention, +X = 0); null for symmetric (circular) devices.
/// </summary>
public sealed record SprinklerPoint(
    double X, double Y, double Z,
    DeviceMount Mount = DeviceMount.Ceiling,
    double? OrientationRadians = null)
{
    public Point3D AsPoint3D() => new(X, Y, Z);
}

/// <summary>
/// A Revit-free footprint of an obstruction in a room (a duct, beam, light, etc.),
/// projected to plan (feet). The solver nudges sprinkler heads out of these boxes
/// so heads don't land inside ductwork or structure. MinZ/MaxZ carry the real
/// vertical extent for 3D visualisation (0 when unknown, e.g. legacy callers).
/// </summary>
public sealed record ObstacleBox(
    Rectangle2D Bounds, string Kind = "", double MinZ = 0, double MaxZ = 0);

/// <summary>
/// A ceiling tile grid (feet): module size and the origin of one tile's lower-left
/// corner, so heads can be snapped to tile centres. Tile centres lie at
/// Origin + (k + 0.5) * Module for integer k.
/// </summary>
public sealed record CeilingTile(double ModuleXFt, double ModuleYFt, double OriginXFt = 0.0, double OriginYFt = 0.0);

public enum PlacementStatus
{
    /// <summary>Laid out within all limits.</summary>
    Placed,

    /// <summary>Laid out, but a rule could not be fully satisfied or values are provisional.</summary>
    PlacedReviewRequired,

    /// <summary>Not laid out (e.g. degenerate/zero-area footprint).</summary>
    Skipped
}

/// <summary>Result of laying out one room.</summary>
public sealed record RoomPlacementResult
{
    public required string RoomId { get; init; }
    public required string RoomName { get; init; }
    public string LevelName { get; init; } = string.Empty;
    public required PlacementStatus Status { get; init; }
    public required IReadOnlyList<SprinklerPoint> Sprinklers { get; init; }

    /// <summary>Largest centre-to-centre spacing used (feet).</summary>
    public double ActualMaxSpacing { get; init; }

    /// <summary>Floor area each sprinkler covers under the chosen grid (sq ft).</summary>
    public double ActualCoveragePerSprinkler { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = new List<string>();

    /// <summary>
    /// The rule set actually used for this room (per-room hazard-resolved). The Revit
    /// placer reads this for sidewall spacing so each room uses its own hazard, not a
    /// single global one. Null only for skipped rooms.
    /// </summary>
    public SprinklerRuleSet? Rules { get; init; }

    /// <summary>Hazard class this room was laid out under (per-room; for reporting).</summary>
    public string HazardClassId { get; init; } = string.Empty;

    /// <summary>
    /// The room footprint used for the layout (axis-aligned, feet). Retained on the
    /// result so downstream consumers (coverage map, reports) can render the room
    /// without re-reading the model. Null for results built before this was captured.
    /// </summary>
    public Rectangle2D? Footprint { get; init; }

    /// <summary>
    /// The true room outline in plan (from <c>Room.GetBoundarySegments</c>), retained so
    /// the coverage map can render the real polygon and clip cells to it. Null when only
    /// the bounding box was captured.
    /// </summary>
    public Polygon2D? Boundary { get; init; }

    /// <summary>Obstruction footprints considered during layout (for the coverage map).</summary>
    public IReadOnlyList<ObstacleBox> Obstacles { get; init; } = new List<ObstacleBox>();

    /// <summary>Finished floor elevation (Z, feet) for 3D visualisation.</summary>
    public double FloorElevation { get; init; }

    /// <summary>Ceiling elevation (Z, feet) for 3D visualisation.</summary>
    public double CeilingElevation { get; init; }

    public bool ReviewRequired => Status == PlacementStatus.PlacedReviewRequired;
    public int Count => Sprinklers.Count;
}

/// <summary>Aggregate result across all rooms in a run.</summary>
public sealed record PlacementResult
{
    public required IReadOnlyList<RoomPlacementResult> Rooms { get; init; }
    public required string StandardId { get; init; }
    public required string HazardClassId { get; init; }

    /// <summary>True if any room needs engineering review (incl. provisional rule sets).</summary>
    public required bool ReviewRequired { get; init; }

    public int TotalSprinklers
    {
        get
        {
            int n = 0;
            foreach (var r in Rooms) n += r.Count;
            return n;
        }
    }
}