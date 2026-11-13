using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using SPA.SynthFire.Core.Devices;
using SPA.SynthFire.Core.Standards;
using SPA.SynthFire.Placement;

namespace SPA.SynthFire.Revit;

/// <summary>Outcome of writing detectors into the model.</summary>
public sealed class DetectorPlacementOutcome
{
    public int Placed { get; init; }
    public int HostedToCeiling { get; init; }
    public int HostedToWall { get; init; }
    public int Cleared { get; init; }
    public int Failed { get; init; }
    public FireAlarmMount Mount { get; init; }
    public string? Error { get; init; }
    public bool Succeeded => Error is null;
}

/// <summary>
/// Writes ceiling detectors (smoke/heat) into the model. Detectors are always
/// ceiling devices, so each is hosted to the ceiling face above its computed grid
/// point (falling back to an unhosted point at the computed height when a room has
/// no ceiling). Heads are tagged so a re-run clears the previous SPA SynthFire
/// detectors first.
/// </summary>
public static class DetectorPlacer
{
    private const string Tag = "SPA SynthFire";

    // Manual coverage-plan placements carry a DISTINCT Comments value so a design-window
    // re-run (Apply → ClearPrevious) leaves them alone: only the auto-solved grid is cleared
    // and rewritten. Clear Devices still removes both (it matches the "SPA SynthFire" prefix).
    private const string ManualTag = "SPA SynthFire (manual)";

    private static bool IsSpaTag(string? comments) => comments == Tag || comments == ManualTag;

    public static DetectorPlacementOutcome Place(
        Document doc,
        PlacementResult result,
        IReadOnlyDictionary<string, ElementId> roomLevels,
        string familySymbolId,
        bool clearPrevious,
        double mountingHeightFt = 80.0 / 12.0,
        double notificationCandela = 75.0)
    {
        if (doc.GetElement(new ElementId(long.Parse(familySymbolId))) is not FamilySymbol symbol)
            return new DetectorPlacementOutcome { Error = "The selected detector family type could not be found." };

        FireAlarmMount mount = FireAlarmMountResolver.Resolve(symbol);
        int placed = 0, ceilingHosted = 0, wallHosted = 0, cleared = 0, failed = 0;

        using (var tx = new Transaction(doc, "SPA SynthFire — Place Detectors"))
        {
            WarningSwallower.AttachTo(tx);
            tx.Start();

            if (clearPrevious) cleared = ClearPrevious(doc);

            if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }

            // Search ceilings, floor slabs above, roofs and beams — in the active model
            // AND in linked (architectural) models — so a detector hosts to, or at least
            // sits at, the REAL overhead surface instead of free-floating at a guessed Z.
            HostFinder hostFinder = mount == FireAlarmMount.Ceiling
                ? HostFinder.Create(GetOrCreate3DView(doc))
                : HostFinder.Create(null);

            foreach (var room in result.Rooms)
            {
                if (!roomLevels.TryGetValue(room.RoomId, out ElementId levelId)) continue;
                if (doc.GetElement(levelId) is not Level level) continue;

                if (mount == FireAlarmMount.Wall)
                {
                    var (ok, fail) = PlaceWallNotification(doc, symbol, room, level, mountingHeightFt, notificationCandela);
                    placed += ok; wallHosted += ok; failed += fail;
                    continue;
                }

                // Grid points come straight from the solver, already laid out at the NFPA 72
                // nominal detector spacing (Nfpa72DetectionRules: 30 ft smoke / 50 ft heat,
                // wall offset = spacing/2), so every point is a detector location as-is.
                // Ray-cast PER POINT so a room that spans ceiling/no-ceiling areas hosts each
                // detector to what is actually above it (the old once-per-room probe made the
                // whole room float when its first point missed).
                foreach (var p in room.Sprinklers)
                {
                    HostHit hit = hostFinder.FindAbove(p.X, p.Y, level.Elevation);
                    var r = PlacementHelpers.PlaceOverheadDevice(
                        doc, symbol, level, p.X, p.Y, hit, p.Z, preferFaceHost: true, out ElementId placedId);
                    if (r == PlacementHelpers.HostResult.SurfaceHosted) { placed++; ceilingHosted++; Tagged(doc, placedId); }
                    else if (r == PlacementHelpers.HostResult.PlacedAtZ) { placed++; Tagged(doc, placedId); }
                    else failed++;
                }
            }

            tx.Commit();
        }

        return new DetectorPlacementOutcome
        {
            Placed = placed, HostedToCeiling = ceilingHosted, HostedToWall = wallHosted,
            Cleared = cleared, Failed = failed, Mount = mount
        };
    }

    // Wall notification appliances (horns/strobes) mount to walls at ~80 in AFF.
    // Fallback wall spacing when the candela rating maps to no covered room size.
    private const double WallSpacingFallbackFt = 30.0;

    /// <summary>
    /// Places ONE detector/appliance at a user-clicked plan point (Phase 3 click-to-place), hosted
    /// per the selected family's mount, in its own transaction and SPA-tagged. Returns true on
    /// success. Live hosting is unverified outside Revit.
    /// </summary>
    public static bool PlaceOne(
        Document doc, string familySymbolId,
        IReadOnlyDictionary<string, ElementId> roomLevels,
        string roomId, double x, double y, DeviceMount mount, double mountingHeightFt,
        out long placedElementId, out string message)
    {
        placedElementId = 0;
        message = "";
        if (doc.GetElement(new ElementId(long.Parse(familySymbolId))) is not FamilySymbol symbol)
        { message = "The selected detector family type could not be found."; return false; }
        if (!roomLevels.TryGetValue(roomId, out ElementId levelId) || doc.GetElement(levelId) is not Level level)
        { message = "Could not resolve the room's level."; return false; }

        bool ok = false;
        ElementId placed = ElementId.InvalidElementId;
        using (var tx = new Transaction(doc, "SPA SynthFire — Place One Detector"))
        {
            WarningSwallower.AttachTo(tx);
            tx.Start();
            if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }

            if (mount == DeviceMount.Sidewall)
            {
                ok = PlaceWallOne(doc, symbol, roomId, level, x, y, mountingHeightFt, out placed);
            }
            else
            {
                // Same multi-category (+linked) overhead search as a full run, so a manual
                // detector lands on the real ceiling/soffit rather than floating.
                HostHit hit = HostFinder.Create(GetOrCreate3DView(doc)).FindAbove(x, y, level.Elevation);
                double roomCeilingZ = level.Elevation + 9.0;
                var r = PlacementHelpers.PlaceOverheadDevice(
                    doc, symbol, level, x, y, hit, roomCeilingZ, preferFaceHost: true, out placed);
                ok = r != PlacementHelpers.HostResult.Rejected;
                if (ok) Tagged(doc, placed, ManualTag);
            }

            if (ok) tx.Commit(); else tx.RollBack();
        }

        if (!ok) { message = "The device could not be hosted at that point (no wall/ceiling reached)."; return false; }
        placedElementId = placed.Value;
        return true;
    }

    /// <summary>
    /// Deletes ONE previously placed detector/appliance by element id (undo / click-delete).
    /// Verifies the instance is SPA-tagged first so auto or foreign elements are never removed.
    /// Own transaction. Returns true on success.
    /// </summary>
    public static bool RemoveOne(Document doc, long elementId, out string message)
    {
        message = "";
        if (doc.GetElement(new ElementId(elementId)) is not FamilyInstance inst)
        { message = "The device to remove could not be found."; return false; }
        if (!IsSpaTag(inst.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString()))
        { message = "That device was not placed by SPA SynthFire."; return false; }

        using (var tx = new Transaction(doc, "SPA SynthFire — Remove One Detector"))
        {
            WarningSwallower.AttachTo(tx);
            tx.Start();
            doc.Delete(inst.Id);
            tx.Commit();
        }
        return true;
    }

    // Host one wall appliance to the room wall nearest the clicked point, at mounting height.
    private static bool PlaceWallOne(
        Document doc, FamilySymbol symbol, string roomId, Level level, double x, double y,
        double mountingHeightFt, out ElementId placedId)
    {
        placedId = ElementId.InvalidElementId;
        if (!long.TryParse(roomId, out long roomIdVal) ||
            doc.GetElement(new ElementId(roomIdVal)) is not Room revitRoom)
            return false;

        (Wall wall, Line line)? nearest = null;
        double best = double.MaxValue;
        foreach (var loop in revitRoom.GetBoundarySegments(new SpatialElementBoundaryOptions()))
            foreach (BoundarySegment seg in loop)
            {
                if (doc.GetElement(seg.ElementId) is not Wall wall) continue;
                if (seg.GetCurve() is not Line line) continue;
                XYZ a = line.GetEndPoint(0), b = line.GetEndPoint(1);
                double vx = b.X - a.X, vy = b.Y - a.Y, wx = x - a.X, wy = y - a.Y;
                double len2 = vx * vx + vy * vy;
                double t2 = len2 <= 1e-9 ? 0 : Math.Clamp((wx * vx + wy * vy) / len2, 0, 1);
                double dx = wx - t2 * vx, dy = wy - t2 * vy;
                double d = dx * dx + dy * dy;
                if (d < best) { best = d; nearest = (wall, line); }
            }
        if (nearest is not { } chosen) return false;

        XYZ p0 = chosen.line.GetEndPoint(0), p1 = chosen.line.GetEndPoint(1);
        XYZ u = (p1 - p0).Normalize();
        double len = chosen.line.Length;
        double t = Math.Clamp((x - p0.X) * u.X + (y - p0.Y) * u.Y, 0, len);
        double targetZ = level.Elevation + mountingHeightFt;
        var point = new XYZ(p0.X + u.X * t, p0.Y + u.Y * t, targetZ);

        XYZ roomCenter = PlacementHelpers.RoomCenter(revitRoom);
        Reference? faceRef = PlacementHelpers.InteriorWallFace(chosen.wall, roomCenter);
        try
        {
            FamilyInstance? inst = null;
            if (faceRef is not null)
            {
                try { inst = doc.Create.NewFamilyInstance(faceRef, point, XYZ.BasisZ, symbol); }
                catch { inst = null; }
                if (inst is not null) PlacementLevels.SetLevelOnly(doc, inst, level);
            }
            inst ??= doc.Create.NewFamilyInstance(point, symbol, chosen.wall, level, StructuralType.NonStructural);
            if (faceRef is null) PlacementLevels.Apply(doc, inst, level, targetZ);

            if (PlacementHelpers.WithinZ(inst, targetZ))
            {
                PlacementHelpers.OrientTowardRoom(doc, inst, roomCenter);
                Tagged(inst, ManualTag);
                placedId = inst.Id;
                return true;
            }
            doc.Delete(inst.Id);
        }
        catch (Exception) { }
        return false;
    }
    private static (int placed, int failed) PlaceWallNotification(
        Document doc, FamilySymbol symbol, RoomPlacementResult room, Level level, double mountingHeightFt,
        double candela)
    {
        // Linked rooms have non-numeric ids and no host Room element; wall hosting
        // needs the host Room, so skip those gracefully.
        if (!long.TryParse(room.RoomId, out long roomIdVal) ||
            doc.GetElement(new ElementId(roomIdVal)) is not Room revitRoom)
            return (0, 0);

        var walls = new List<(Wall wall, Line line)>();
        foreach (var loop in revitRoom.GetBoundarySegments(new SpatialElementBoundaryOptions()))
            foreach (BoundarySegment seg in loop)
                if (doc.GetElement(seg.ElementId) is Wall w && seg.GetCurve() is Line line)
                    walls.Add((w, line));

        if (walls.Count == 0) return (0, 0);

        // Prefer walls based on the room's own level so the appliance lands on the
        // correct floor (a multi-storey wall pegs it to the wall base = ground).
        var onLevel = walls.Where(w =>
            w.wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT)?.AsElementId() == level.Id).ToList();
        var usable = onLevel.Count > 0 ? onLevel : walls;

        // Spacing from NFPA 72 Table 18.5.5.5.1(a): a wall strobe of this candela covers
        // a square room up to `side` ft, so appliances no more than `side` apart keep the
        // whole wall run within one appliance's coverage. Falls back to a nominal 30 ft if
        // the rating maps to no listed room size. Candela-driven, still review-required.
        usable.Sort((a, b) => b.line.Length.CompareTo(a.line.Length));
        var (hostWall, hostLine) = usable[0];
        XYZ roomCenter = PlacementHelpers.RoomCenter(revitRoom);
        Reference? faceRef = PlacementHelpers.InteriorWallFace(hostWall, roomCenter);

        double coveredSide = NotificationCoverage.MaxRoomSideForCandela(candela);
        double spacingFt = coveredSide > 0 ? coveredSide : WallSpacingFallbackFt;
        double targetZ = level.Elevation + mountingHeightFt;
        int count = Math.Max(1, (int)Math.Ceiling(hostLine.Length / spacingFt));
        double step = hostLine.Length / count;
        int placed = 0, failed = 0;

        for (int i = 0; i < count; i++)
        {
            double t = step * (i + 0.5);
            XYZ along = hostLine.GetEndPoint(0) + (hostLine.Direction.Normalize() * t);
            var point = new XYZ(along.X, along.Y, targetZ);
            try
            {
                // Host to the wall's interior FACE (absolute Z, correct floor); fall
                // back to element-hosting. Then face the appliance into the room.
                FamilyInstance? inst = null;
                if (faceRef is not null)
                {
                    try { inst = doc.Create.NewFamilyInstance(faceRef, point, XYZ.BasisZ, symbol); }
                    catch { inst = null; }
                    if (inst is not null) PlacementLevels.SetLevelOnly(doc, inst, level);
                }
                if (inst is null)
                {
                    inst = doc.Create.NewFamilyInstance(point, symbol, hostWall, level, StructuralType.NonStructural);
                    PlacementLevels.Apply(doc, inst, level, targetZ);
                }

                if (PlacementHelpers.WithinZ(inst, targetZ))
                {
                    PlacementHelpers.OrientTowardRoom(doc, inst, roomCenter);
                    Tagged(inst);
                    placed++;
                }
                else { doc.Delete(inst.Id); failed++; }
            }
            catch (Exception) { failed++; }
        }
        return (placed, failed);
    }

    private static int ClearPrevious(Document doc)
    {
        var ids = new FilteredElementCollector(doc)
            .OfCategory(DetectorFamilyReader.Category)
            .WhereElementIsNotElementType()
            .Where(e => e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() == Tag)
            .Select(e => e.Id)
            .ToList();
        if (ids.Count > 0) doc.Delete(ids);
        return ids.Count;
    }

    private static void Tagged(FamilyInstance inst) => Tagged(inst, Tag);

    private static void Tagged(FamilyInstance inst, string tag)
    {
        Parameter? c = inst.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (c is { IsReadOnly: false }) c.Set(tag);
    }

    private static void Tagged(Document doc, ElementId id) => Tagged(doc, id, Tag);

    private static void Tagged(Document doc, ElementId id, string tag)
    {
        if (id != ElementId.InvalidElementId && doc.GetElement(id) is FamilyInstance inst) Tagged(inst, tag);
    }

    private static View3D? GetOrCreate3DView(Document doc)
    {
        View3D? existing = new FilteredElementCollector(doc)
            .OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(v => !v.IsTemplate);
        if (existing is not null) return existing;

        ViewFamilyType? vft = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
        return vft is null ? null : View3D.CreateIsometric(doc, vft.Id);
    }
}
