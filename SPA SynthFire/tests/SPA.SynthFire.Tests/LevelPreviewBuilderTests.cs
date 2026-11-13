using System;
using System.Collections.Generic;
using System.Linq;
using SPA.SynthFire.Core.Devices;
using SPA.SynthFire.Core.Geometry;
using SPA.SynthFire.Placement;
using SPA.SynthFire.Placement.Preview;
using Xunit;

namespace SPA.SynthFire.Tests;

public class LevelPreviewBuilderTests
{
    private static RoomPlacementResult Room(
        string id, Rectangle2D f, IReadOnlyList<SprinklerPoint> pts, double spacing,
        PlacementStatus status = PlacementStatus.Placed,
        IReadOnlyList<ObstacleBox>? obstacles = null, IReadOnlyList<string>? notes = null,
        Polygon2D? boundary = null) => new()
    {
        RoomId = id,
        RoomName = id,
        Status = status,
        Sprinklers = pts,
        ActualMaxSpacing = spacing,
        Footprint = f,
        Boundary = boundary,
        Obstacles = obstacles ?? Array.Empty<ObstacleBox>(),
        Notes = notes ?? Array.Empty<string>()
    };

    private static PlacementResult Level(params RoomPlacementResult[] rooms) => new()
    {
        Rooms = rooms,
        StandardId = "NFPA13",
        HazardClassId = "LightHazard",
        ReviewRequired = rooms.Any(r => r.ReviewRequired)
    };

    [Fact]
    public void GriddedRoom_IsFullyCovered_AndCarriesDevices()
    {
        var f = new Rectangle2D(0, 0, 20, 20);
        var pts = new List<SprinklerPoint>
        {
            new(5, 5, 9), new(15, 5, 9), new(5, 15, 9), new(15, 15, 9)
        };
        var model = LevelPreviewBuilder.Build("L1", Level(Room("A", f, pts, spacing: 10)));

        var room = Assert.Single(model.Rooms);
        Assert.Equal(4, room.DeviceCount);
        Assert.Equal(4, model.TotalDevices);
        Assert.True(room.CoveragePercent > 99.0);      // half-diagonal radius covers the grid
        Assert.NotEmpty(room.Cells);
    }

    [Fact]
    public void SkippedRoom_WithNoFootprint_HasNoCellsAndZeroCoverage()
    {
        var skipped = Room("S", new Rectangle2D(0, 0, 0, 0), Array.Empty<SprinklerPoint>(),
            spacing: 0, status: PlacementStatus.Skipped);
        var model = LevelPreviewBuilder.Build("L1", Level(skipped));

        var room = Assert.Single(model.Rooms);
        Assert.Empty(room.Cells);
        Assert.Equal(0.0, room.CoveragePercent);
        Assert.Equal(RoomPreviewStatus.Skipped, room.Status);
    }

    [Fact]
    public void ReviewRoom_ReportsNonComplianceNoteCount()
    {
        var f = new Rectangle2D(0, 0, 12, 12);
        var review = Room("R", f, new List<SprinklerPoint> { new(6, 6, 9) }, spacing: 12,
            status: PlacementStatus.PlacedReviewRequired,
            notes: new[] { "spacing below minimum; review required." });
        var model = LevelPreviewBuilder.Build("L1", Level(review));

        Assert.Equal(1, model.Rooms[0].NonCompliances);
        Assert.Equal(RoomPreviewStatus.ReviewRequired, model.Rooms[0].Status);
    }

    [Fact]
    public void Bounds_AreUnionOfRealFootprints_IgnoringDegenerate()
    {
        var a = Room("A", new Rectangle2D(0, 0, 10, 10), Array.Empty<SprinklerPoint>(), 10);
        var b = Room("B", new Rectangle2D(20, 5, 40, 30), Array.Empty<SprinklerPoint>(), 10);
        var skipped = Room("S", new Rectangle2D(0, 0, 0, 0), Array.Empty<SprinklerPoint>(), 0);

        var model = LevelPreviewBuilder.Build("L1", Level(a, b, skipped));

        Assert.Equal(0, model.Bounds.MinX);
        Assert.Equal(0, model.Bounds.MinY);
        Assert.Equal(40, model.Bounds.MaxX);
        Assert.Equal(30, model.Bounds.MaxY);
    }

    [Fact]
    public void Boundary_IsCarriedThrough_AndClipsCellsAndArea()
    {
        // L-shape inside a 20x20 box (top-right 10x10 removed): true area = 300.
        var boundary = new Polygon2D(new[]
        {
            new Point2D(0, 0), new Point2D(20, 0), new Point2D(20, 10),
            new Point2D(10, 10), new Point2D(10, 20), new Point2D(0, 20)
        });
        var f = new Rectangle2D(0, 0, 20, 20);
        var room = Room("L", f, new List<SprinklerPoint> { new(10, 10, 9) }, spacing: 10, boundary: boundary);

        var model = LevelPreviewBuilder.Build("L1", Level(room));
        var preview = Assert.Single(model.Rooms);

        Assert.NotNull(preview.Boundary);
        Assert.Equal(300.0, preview.AreaSqFt, precision: 6);            // polygon area, not the 400 box
        Assert.DoesNotContain(preview.Cells, c => c.CenterX > 10 && c.CenterY > 10);
    }

    [Fact]
    public void AddManualDevice_AddsOneDevice_AndRaisesCoverage()
    {
        // 20x20 room with a single head bottom-left leaves the far corner uncovered.
        var f = new Rectangle2D(0, 0, 20, 20);
        var one = Room("A", f, new List<SprinklerPoint> { new(5, 5, 9) }, spacing: 10);
        var before = Assert.Single(LevelPreviewBuilder.Build("L1", Level(one)).Rooms);

        // Drop a manual head in the open opposite corner (the red gap).
        var after = LevelPreviewBuilder.AddManualDevice(before, 15, 15, DeviceMount.Ceiling);

        Assert.Equal(before.DeviceCount + 1, after.DeviceCount);
        Assert.True(after.CoveragePercent > before.CoveragePercent);
        // The original is unchanged (returns a new copy).
        Assert.Equal(1, before.DeviceCount);
    }

    // Mirrors the runner's RebuildAndRefresh: base snapshot + the ordered manual heads for the room.
    private static RoomPreview Rebuild(RoomPreview baseRoom, params (double x, double y)[] heads)
    {
        RoomPreview r = baseRoom;
        foreach (var (x, y) in heads)
            r = LevelPreviewBuilder.AddManualDevice(r, x, y, DeviceMount.Ceiling);
        return r;
    }

    [Fact]
    public void RebuildFromBase_IsDeterministic_RegardlessOfIntermediateSteps()
    {
        // The runner never mutates the base; every edit rebuilds from it. Two independent rebuilds
        // of the same base + same ordered heads must produce an identical room (count + coverage).
        var f = new Rectangle2D(0, 0, 20, 20);
        var baseRoom = Assert.Single(LevelPreviewBuilder.Build(
            "L1", Level(Room("A", f, new List<SprinklerPoint> { new(5, 5, 9) }, spacing: 10))).Rooms);

        var a = Rebuild(baseRoom, (15, 15), (15, 5));
        var b = Rebuild(baseRoom, (15, 15), (15, 5));

        Assert.Equal(a.DeviceCount, b.DeviceCount);
        Assert.Equal(a.CoveragePercent, b.CoveragePercent, precision: 6);
        Assert.Equal(baseRoom.DeviceCount + 2, a.DeviceCount); // base untouched
    }

    [Fact]
    public void RemoveManualHead_IsSymmetricWithAdd()
    {
        // add(h1) then add(h2) then remove(h2) — i.e. rebuild base+[h1] — must equal a plain base+[h1]:
        // dropping a head from the ordered list and rebuilding fully reverses that add.
        var f = new Rectangle2D(0, 0, 20, 20);
        var baseRoom = Assert.Single(LevelPreviewBuilder.Build(
            "L1", Level(Room("A", f, new List<SprinklerPoint> { new(5, 5, 9) }, spacing: 10))).Rooms);

        var withOne = Rebuild(baseRoom, (15, 15));           // base + h1
        var withTwo = Rebuild(baseRoom, (15, 15), (15, 5));  // base + h1 + h2
        var afterRemove = Rebuild(baseRoom, (15, 15));       // remove h2 -> rebuild base + h1

        Assert.True(withTwo.DeviceCount > withOne.DeviceCount);
        Assert.Equal(withOne.DeviceCount, afterRemove.DeviceCount);
        Assert.Equal(withOne.CoveragePercent, afterRemove.CoveragePercent, precision: 6);
    }

    [Fact]
    public void UndoThenRedo_RestoresTheSameRoom()
    {
        // Undo pops the last head (rebuild from the shorter list); redo re-applies it. The room after
        // undo+redo must match the room before the undo (count + coverage).
        var f = new Rectangle2D(0, 0, 20, 20);
        var baseRoom = Assert.Single(LevelPreviewBuilder.Build(
            "L1", Level(Room("A", f, new List<SprinklerPoint> { new(5, 5, 9) }, spacing: 10))).Rooms);

        // active list = [h1, h2]
        var active = new List<(double x, double y)> { (15, 15), (15, 5) };
        var full = Rebuild(baseRoom, active.ToArray());

        // undo: pop h2 -> active = [h1]
        var undone = active[^1];
        active.RemoveAt(active.Count - 1);
        var afterUndo = Rebuild(baseRoom, active.ToArray());
        Assert.True(afterUndo.DeviceCount < full.DeviceCount);

        // redo: push h2 back -> active = [h1, h2]
        active.Add(undone);
        var afterRedo = Rebuild(baseRoom, active.ToArray());

        Assert.Equal(full.DeviceCount, afterRedo.DeviceCount);
        Assert.Equal(full.CoveragePercent, afterRedo.CoveragePercent, precision: 6);
    }
}
