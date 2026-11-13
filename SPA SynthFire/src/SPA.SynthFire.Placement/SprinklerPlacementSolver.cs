using System;
using System.Collections.Generic;
using System.Linq;
using SPA.SynthFire.Core.Devices;
using SPA.SynthFire.Core.Standards;

namespace SPA.SynthFire.Placement;

/// <summary>
/// Deterministic, Revit-free sprinkler layout solver for rectangular rooms.
/// Delegates the coverage-grid + clash-avoidance work to <see cref="CeilingGridLayout"/>
/// (pendent/upright/free) or <see cref="SidewallLayout"/> (wall-throw), and wraps it with
/// sprinkler-specific rule handling (provisional flagging).
/// </summary>
public sealed class SprinklerPlacementSolver
{
    public PlacementResult Solve(
        IReadOnlyList<RoomInput> rooms, SprinklerRuleSet rules, DeviceMount mount = DeviceMount.Ceiling)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        ArgumentNullException.ThrowIfNull(rules);
        return Solve(rooms, _ => rules, rules.StandardId, rules.HazardClassId, mount);
    }

    /// <summary>
    /// Lays out every room using a PER-ROOM rule set resolved from that room's own
    /// hazard classification (falling back to a default). This is what makes an EH2
    /// store and a Light office in the same run get different grids, instead of one
    /// hazard overriding all rooms. <paramref name="mount"/> is the selected family's
    /// mounting type, so a sidewall family lays out along walls (matching the writer)
    /// instead of on a ceiling grid.
    /// </summary>
    public PlacementResult Solve(
        IReadOnlyList<RoomInput> rooms,
        Func<RoomInput, SprinklerRuleSet> ruleResolver,
        string standardId,
        string defaultHazardId,
        DeviceMount mount = DeviceMount.Ceiling)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        ArgumentNullException.ThrowIfNull(ruleResolver);

        var results = rooms.Select(room => SolveRoom(room, ruleResolver(room), mount)).ToList();
        bool anyReview = results.Any(r => r.ReviewRequired);

        return new PlacementResult
        {
            Rooms = results,
            StandardId = standardId,
            HazardClassId = defaultHazardId,
            ReviewRequired = anyReview
        };
    }

    private static RoomPlacementResult SolveRoom(RoomInput room, SprinklerRuleSet rules, DeviceMount mount)
    {
        return mount == DeviceMount.Sidewall
            ? SolveSidewallRoom(room, rules)
            : SolveCeilingRoom(room, rules, mount);
    }

    private static RoomPlacementResult SolveCeilingRoom(RoomInput room, SprinklerRuleSet rules, DeviceMount mount)
    {
        GridLayoutResult grid = CeilingGridLayout.Compute(
            room, rules.MaxSpacingFeet, rules.MaxCoverageAreaSquareFeet, rules.MinSpacingFeet);

        if (grid.Degenerate)
            return Skipped(room, rules, grid.Notes);

        var notes = new List<string>(grid.Notes);
        bool reviewRequired = grid.ReviewRequired || rules.IsProvisional;
        if (rules.IsProvisional)
            notes.Add($"Rule set '{rules.StandardId}/{rules.HazardClassId}' is provisional (not FPE-verified).");

        // Drop grid points that fall outside the true room polygon (concave/L-shaped rooms).
        IReadOnlyList<(double X, double Y)> gridPoints = grid.Points;
        if (room.Boundary is { } boundary)
        {
            var inside = grid.Points.Where(p => boundary.Contains(p.X, p.Y)).ToList();
            if (inside.Count < grid.Points.Count)
            {
                notes.Add($"{grid.Points.Count - inside.Count} grid point(s) fell outside the room outline and were dropped; verify coverage near the cut-out.");
                reviewRequired = true;
            }
            gridPoints = inside;
        }

        // Auto-infill residual coverage gaps.
        double radius = CoverageField.DefaultRadiusForSpacing(grid.ActualMaxSpacing);
        GapInfill.Result fill = GapInfill.FillCircular(
            room.Footprint, room.Boundary, room.Obstacles,
            gridPoints, radius, cellSizeFt: 1.0, rules.MinSpacingFeet);

        if (fill.Added.Count > 0)
        {
            gridPoints = gridPoints.Concat(fill.Added).ToList();
            notes.Add($"{fill.Added.Count} head(s) auto-added to fill residual coverage gaps " +
                      "(obstruction shadows / narrow or irregular areas); verify positions before issue.");
            reviewRequired = true;
        }
        if (fill.RemainingUncoveredCells > 0)
        {
            notes.Add("Residual uncovered area remains after gap-fill (min-spacing limited); " +
                      "review and place manually if required.");
            reviewRequired = true;
        }

        var points = gridPoints
            .Select(p => new SprinklerPoint(p.X, p.Y, room.CeilingElevation, mount))
            .ToList();

        return new RoomPlacementResult
        {
            RoomId = room.RoomId,
            RoomName = room.Name,
            LevelName = room.LevelName,
            Status = reviewRequired ? PlacementStatus.PlacedReviewRequired : PlacementStatus.Placed,
            Sprinklers = points,
            ActualMaxSpacing = grid.ActualMaxSpacing,
            ActualCoveragePerSprinkler = grid.ActualCoveragePerPoint,
            Notes = notes,
            HazardClassId = rules.HazardClassId,
            Rules = rules,
            Footprint = room.Footprint,
            Boundary = room.Boundary,
            Obstacles = room.Obstacles,
            FloorElevation = room.FloorElevation,
            CeilingElevation = room.CeilingElevation
        };
    }

    private static RoomPlacementResult SolveSidewallRoom(RoomInput room, SprinklerRuleSet rules)
    {
        SidewallLayoutResult layout = SidewallLayout.Compute(
            room, rules.MaxSpacingFeet, rules.MinSpacingFeet);

        if (layout.Degenerate)
            return Skipped(room, rules, layout.Notes);

        var notes = new List<string>(layout.Notes);
        if (rules.IsProvisional)
            notes.Add($"Rule set '{rules.StandardId}/{rules.HazardClassId}' is provisional (not FPE-verified).");
        notes.Add("Sidewall layout is derived from room geometry and is indicative; the live writer refines to real wall faces — review before issue.");

        IReadOnlyList<SidewallPoint> pts = layout.Points;
        if (room.Boundary is { } boundary)
            pts = pts.Where(p => boundary.Contains(p.X, p.Y)).ToList();

        var points = pts
            .Select(p => new SprinklerPoint(p.X, p.Y, room.CeilingElevation, DeviceMount.Sidewall, p.OrientationRadians))
            .ToList();

        return new RoomPlacementResult
        {
            RoomId = room.RoomId,
            RoomName = room.Name,
            LevelName = room.LevelName,
            Status = PlacementStatus.PlacedReviewRequired,
            Sprinklers = points,
            ActualMaxSpacing = layout.ActualMaxSpacing,
            ActualCoveragePerSprinkler = layout.ActualCoveragePerPoint,
            Notes = notes,
            HazardClassId = rules.HazardClassId,
            Rules = rules,
            Footprint = room.Footprint,
            Boundary = room.Boundary,
            Obstacles = room.Obstacles,
            FloorElevation = room.FloorElevation,
            CeilingElevation = room.CeilingElevation
        };
    }

    private static RoomPlacementResult Skipped(RoomInput room, SprinklerRuleSet rules, IReadOnlyList<string> notes) =>
        new()
        {
            RoomId = room.RoomId,
            RoomName = room.Name,
            LevelName = room.LevelName,
            Status = PlacementStatus.Skipped,
            Sprinklers = Array.Empty<SprinklerPoint>(),
            Notes = notes,
            HazardClassId = rules.HazardClassId,
            Rules = rules,
            Footprint = room.Footprint,
            Boundary = room.Boundary,
            Obstacles = room.Obstacles,
            FloorElevation = room.FloorElevation,
            CeilingElevation = room.CeilingElevation
        };
}