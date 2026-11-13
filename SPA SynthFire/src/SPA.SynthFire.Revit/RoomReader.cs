using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using SPA.SynthFire.Core.Geometry;
using SPA.SynthFire.Placement;

namespace SPA.SynthFire.Revit;

/// <summary>Room inputs read from the model, plus the level each room sits on.</summary>
public sealed class RoomReadResult
{
    public required IReadOnlyList<RoomInput> Rooms { get; init; }
    public required IReadOnlyDictionary<string, ElementId> RoomLevels { get; init; }
}

/// <summary>
/// Builds Revit-free <see cref="RoomInput"/>s from placed rooms in the active
/// document AND in any linked (e.g. architectural) models. Linked rooms are
/// transformed into host coordinates and mapped to the nearest host level, so
/// placement works when the rooms live in a linked model.
/// </summary>
public static class RoomReader
{

    public static RoomReadResult ReadRooms(Document doc, CeilingTile? ceilingTile = null)
    {
        var rooms = new List<RoomInput>();
        var levels = new Dictionary<string, ElementId>();
        var obstacles = ReadAllObstacles(doc);
        var hostLevels = HostLevels(doc);

        // Host rooms.
        ReadFrom(doc, Transform.Identity, rooms, levels, obstacles, hostLevels, ceilingTile, linkTag: null);

        // Linked-model rooms (architectural links).
        foreach (RevitLinkInstance link in new FilteredElementCollector(doc)
                     .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
        {
            Document? linkDoc = link.GetLinkDocument();
            if (linkDoc is null) continue;
            ReadFrom(linkDoc, link.GetTotalTransform(), rooms, levels, obstacles, hostLevels,
                     ceilingTile, linkTag: link.Id.Value.ToString());
        }

        return new RoomReadResult { Rooms = rooms, RoomLevels = levels };
    }

    /// <summary>
    /// The room's real ceiling elevation (host Z) used as the FALLBACK height when the
    /// per-point overhead ray-cast finds no surface. Derived from the room's own computed
    /// height (<see cref="Room.UnboundedHeight"/>, else the bounding-box height), so an
    /// unhosted device sits at the room's actual upper bound instead of a flat 9 ft guess.
    /// Heights outside a believable finished-ceiling band fall back to the 9 ft default.
    /// (Link transforms rotate/translate only, so a Z height is the same in host coords.)
    /// </summary>
    private static double CeilingElevationFor(Room room, BoundingBoxXYZ bbox, double hostLevelZ)
    {
        return hostLevelZ + CeilingHeight.Resolve(room.UnboundedHeight, bbox.Max.Z - bbox.Min.Z);
    }

    private static void ReadFrom(
        Document source, Transform transform,
        List<RoomInput> rooms, Dictionary<string, ElementId> levels,
        List<ObstacleBox> obstacles, List<(ElementId Id, double Elevation, string Name)> hostLevels,
        CeilingTile? ceilingTile, string? linkTag)
    {
        foreach (Element element in new FilteredElementCollector(source)
                     .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType())
        {
            if (element is not Room room || room.Area <= 0) continue;
            BoundingBoxXYZ? bbox = room.get_BoundingBox(null);
            if (bbox is null) continue;

            // Transform the footprint corners into host coordinates.
            Rectangle2D footprint = TransformedFootprint(bbox, transform);

            // Room level elevation → host Z, then map to the nearest host level.
            double roomLevelZ = transform.OfPoint(new XYZ(0, 0, (source.GetElement(room.LevelId) as Level)?.Elevation ?? 0)).Z;
            (ElementId hostLevelId, double hostLevelZ, string hostLevelName) = NearestHostLevel(hostLevels, roomLevelZ);

            string id = linkTag is null ? room.Id.Value.ToString() : $"LINK:{linkTag}:{room.Id.Value}";

            // Read the per-room hazard the Hazard Classification tool stored in the
            // room's Comments ("SPA Hazard: std/hazard"). Only host rooms carry a
            // writable tag; linked rooms fall back to the design-window default.
            string? hazardId = linkTag is null
                ? HazardRoomReader.ParseExistingHazardId(room.LookupParameter("Comments")?.AsString())
                : null;

            rooms.Add(new RoomInput
            {
                RoomId = id,
                Name = room.Name ?? $"Room {room.Id.Value}",
                LevelName = hostLevelName,
                Footprint = footprint,
                Boundary = TryReadBoundary(room, transform),
                CeilingElevation = CeilingElevationFor(room, bbox, hostLevelZ),
                FloorElevation = hostLevelZ,
                Obstacles = FilterObstacles(obstacles, footprint),
                CeilingTile = ceilingTile,
                HazardClassId = hazardId
            });
            if (hostLevelId != ElementId.InvalidElementId) levels[id] = hostLevelId;
        }
    }

    /// <summary>
    /// Reads the room's true plan outline from <c>GetBoundarySegments</c>, tessellating
    /// each edge (so arcs become polylines) and transforming into host coordinates. Returns
    /// the largest loop (the outer boundary), or null if it can't be read as a valid polygon.
    /// </summary>
    private static Polygon2D? TryReadBoundary(Room room, Transform t)
    {
        try
        {
            var loops = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
            if (loops is null || loops.Count == 0) return null;

            Polygon2D? best = null;
            foreach (var loop in loops)
            {
                var pts = new List<Point2D>();
                foreach (BoundarySegment seg in loop)
                {
                    Curve? curve = seg.GetCurve();
                    if (curve is null) continue;
                    foreach (XYZ p in curve.Tessellate())
                    {
                        XYZ h = t.OfPoint(p);
                        if (pts.Count == 0 || !Close(pts[^1], h))
                            pts.Add(new Point2D(h.X, h.Y));
                    }
                }
                // Drop the closing vertex if it repeats the first.
                if (pts.Count > 1 && Close(pts[^1], pts[0])) pts.RemoveAt(pts.Count - 1);
                if (pts.Count < 3) continue;

                var poly = new Polygon2D(pts);
                if (best is null || poly.Area > best.Area) best = poly;
            }
            return best;
        }
        catch
        {
            return null; // never let geometry reading break the room read
        }
    }

    private static bool Close(Point2D a, XYZ b) => System.Math.Abs(a.X - b.X) < 1e-6 && System.Math.Abs(a.Y - b.Y) < 1e-6;
    private static bool Close(Point2D a, Point2D b) => System.Math.Abs(a.X - b.X) < 1e-6 && System.Math.Abs(a.Y - b.Y) < 1e-6;

    private static Rectangle2D TransformedFootprint(BoundingBoxXYZ bbox, Transform t)
    {
        // Transform all four plan corners (handles rotated links) and take the AABB.
        XYZ[] corners =
        {
            t.OfPoint(new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Min.Z)),
            t.OfPoint(new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Min.Z)),
            t.OfPoint(new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Min.Z)),
            t.OfPoint(new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Min.Z)),
        };
        double minX = corners.Min(c => c.X), maxX = corners.Max(c => c.X);
        double minY = corners.Min(c => c.Y), maxY = corners.Max(c => c.Y);
        return new Rectangle2D(minX, minY, maxX, maxY);
    }

    private static List<(ElementId Id, double Elevation, string Name)> HostLevels(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .Select(l => (l.Id, l.Elevation, l.Name)).ToList();

    private static (ElementId, double, string) NearestHostLevel(
        List<(ElementId Id, double Elevation, string Name)> hostLevels, double z)
    {
        if (hostLevels.Count == 0) return (ElementId.InvalidElementId, z, "(no level)");
        var best = hostLevels.OrderBy(l => System.Math.Abs(l.Elevation - z)).First();
        return (best.Id, best.Elevation, best.Name);
    }

    private static readonly List<BuiltInCategory> ObstacleCategories = new()
    {
        BuiltInCategory.OST_DuctCurves,
        BuiltInCategory.OST_StructuralFraming,
        BuiltInCategory.OST_CableTray,
        BuiltInCategory.OST_PipeCurves
    };

    private static List<ObstacleBox> ReadAllObstacles(Document doc)
    {
        var filter = new ElementMulticategoryFilter(ObstacleCategories);
        var obstacles = new List<ObstacleBox>();
        foreach (Element e in new FilteredElementCollector(doc).WherePasses(filter).WhereElementIsNotElementType())
        {
            BoundingBoxXYZ? bb = e.get_BoundingBox(null);
            if (bb is null) continue;
            obstacles.Add(new ObstacleBox(
                new Rectangle2D(bb.Min.X, bb.Min.Y, bb.Max.X, bb.Max.Y),
                e.Category?.Name ?? "obstruction",
                bb.Min.Z, bb.Max.Z));
        }
        return obstacles;
    }

    private static IReadOnlyList<ObstacleBox> FilterObstacles(List<ObstacleBox> all, Rectangle2D room)
    {
        var hits = new List<ObstacleBox>();
        foreach (var o in all)
        {
            var b = o.Bounds;
            if (b.MinX <= room.MaxX && b.MaxX >= room.MinX && b.MinY <= room.MaxY && b.MaxY >= room.MinY)
                hits.Add(o);
        }
        return hits;
    }
}