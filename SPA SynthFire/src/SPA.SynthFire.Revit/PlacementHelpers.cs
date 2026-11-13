using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace SPA.SynthFire.Revit;

/// <summary>
/// Shared placement geometry helpers used by both the sprinkler and detector
/// placers: Z verification, overhead hosting, wall-face hosting, and orientation.
/// </summary>
public static class PlacementHelpers
{
    /// <summary>A placed head more than this far (feet) from target Z is wrong (void/floor).</summary>
    public const double ZToleranceFt = 3.0;

    /// <summary>Outcome of an overhead placement.</summary>
    public enum HostResult
    {
        /// <summary>Rejected — could not place at a valid Z at all.</summary>
        Rejected = -1,
        /// <summary>Placed level-based at the real ceiling Z (unhosted, but not floating).</summary>
        PlacedAtZ = 0,
        /// <summary>Hosted to an overhead surface (ceiling/floor/roof/beam face or element).</summary>
        SurfaceHosted = 1,
    }

    /// <summary>
    /// Places ONE overhead device (ceiling detector / pendent head) and verifies it
    /// landed at a real surface — never floating in the void with a fabricated offset.
    /// Ladder, in order:
    ///   (a) active-doc face hit → face-host, verify at the hit Z;
    ///   (b) that face's element → element-host (hosted, non-face-based families);
    ///   (c) any hit (incl. a LINKED ceiling, which cannot host) → place level-based at
    ///       the real hit Z so the device sits at the actual ceiling;
    ///   (d) no hit at all → place level-based at the room's computed ceiling Z.
    /// When <paramref name="preferFaceHost"/> is false (e.g. upright sprinklers, which
    /// must not face-host or they point the wrong way) steps (a)/(b) are skipped.
    /// </summary>
    public static HostResult PlaceOverheadDevice(
        Document doc, FamilySymbol symbol, Level level, double x, double y,
        in HostHit hit, double roomCeilingZ, bool preferFaceHost, out ElementId placedId)
    {
        placedId = ElementId.InvalidElementId;
        double targetZ = hit.Found ? hit.HitZ : roomCeilingZ;

        // (a) + (b): host to an overhead surface in the ACTIVE document. Only attempt this
        // for families Revit can actually host to a face/element (WorkPlaneBased / hosted) —
        // a plain OneLevelBased family cannot, so skip straight to unhosted-at-Z below.
        if (preferFaceHost && CanFaceHost(symbol) && hit.Face is Reference face)
        {
            FamilyInstance? hosted = null;
            try { hosted = doc.Create.NewFamilyInstance(face, new XYZ(x, y, hit.HitZ), XYZ.BasisX, symbol); }
            catch { hosted = null; }
            if (hosted is not null)
            {
                PlacementLevels.SetLevelOnly(doc, hosted, level);
                if (WithinZ(hosted, targetZ)) { placedId = hosted.Id; return HostResult.SurfaceHosted; }
                doc.Delete(hosted.Id);
            }

            if (doc.GetElement(hit.ElementId) is Element hostElem)
            {
                FamilyInstance? onElem = null;
                try
                {
                    onElem = doc.Create.NewFamilyInstance(
                        new XYZ(x, y, hit.HitZ), symbol, hostElem, level, StructuralType.NonStructural);
                }
                catch { onElem = null; }
                if (onElem is not null)
                {
                    PlacementLevels.SetLevelOnly(doc, onElem, level);
                    if (WithinZ(onElem, targetZ)) { placedId = onElem.Id; return HostResult.SurfaceHosted; }
                    doc.Delete(onElem.Id);
                }
            }
        }

        // (c) + (d): unhosted at the real ceiling Z (hit Z when we have one, else the
        // room's computed ceiling). Correct height, unhosted — sits at the ceiling.
        FamilyInstance? free = PlaceUnhosted(doc, symbol, level, new XYZ(x, y, targetZ));
        if (free is not null)
        {
            if (WithinZ(free, targetZ)) { placedId = free.Id; return HostResult.PlacedAtZ; }
            doc.Delete(free.Id);
        }
        return HostResult.Rejected;
    }

    /// <summary>
    /// True when <paramref name="symbol"/> can be hosted to a FACE (a ceiling/wall/roof
    /// face). In Revit only <see cref="FamilyPlacementType.WorkPlaneBased"/> (which is what
    /// face-based families report) and the hosted point types can take a face reference;
    /// a plain <see cref="FamilyPlacementType.OneLevelBased"/> family cannot, so we never
    /// waste an attempt (and a Revit exception) trying to face-host it.
    /// </summary>
    public static bool CanFaceHost(FamilySymbol symbol) =>
        symbol.Family.FamilyPlacementType is
            FamilyPlacementType.WorkPlaneBased or
            FamilyPlacementType.OneLevelBasedHosted;

    /// <summary>
    /// True when <paramref name="symbol"/> is a strictly HOST-REQUIRING family — one that Revit
    /// will only place with a real host element (wall/ceiling) and refuses to place on a level or
    /// a work/sketch plane. <see cref="FamilyPlacementType.OneLevelBasedHosted"/> is that type
    /// (wall-based / ceiling-based hosted content); <see cref="FamilyPlacementType.WorkPlaneBased"/>
    /// (face-based) does NOT require a host — it can sit on a sketch plane, which is how we place
    /// it against a LINKED wall that cannot host a Revit instance. When this returns true and no
    /// own-model host is reachable, placement genuinely cannot happen — the caller reports why.
    /// </summary>
    public static bool RequiresHost(FamilySymbol symbol) =>
        symbol.Family.FamilyPlacementType is FamilyPlacementType.OneLevelBasedHosted;

    /// <summary>
    /// Places a face-based / work-plane family on a VERTICAL sketch plane whose normal is
    /// <paramref name="normal"/> (a horizontal throw direction), at <paramref name="point"/>. This
    /// is what lets a sidewall/face-based head place against a LINKED wall — the linked wall cannot
    /// host a Revit instance, but a vertical work plane at the wall face gives the family the
    /// vertical plane it expects and orients it facing into the room (normal = throw). Reference
    /// direction is world up, which lies in the vertical plane. Returns null for a host-requiring
    /// family (it rejects a sketch plane) or if Revit refuses the plane — the caller then falls
    /// back to the generic level/horizontal placement.
    /// </summary>
    public static FamilyInstance? PlaceOnVerticalPlane(
        Document doc, FamilySymbol symbol, Level level, XYZ point, XYZ normal)
    {
        var flat = new XYZ(normal.X, normal.Y, 0);
        if (flat.IsZeroLength()) return null;
        if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }
        try
        {
            Plane plane = Plane.CreateByNormalAndOrigin(flat.Normalize(), point);
            SketchPlane sp = SketchPlane.Create(doc, plane);
            FamilyInstance inst = doc.Create.NewFamilyInstance(
                sp.GetPlaneReference(), point, XYZ.BasisZ, symbol);
            if (inst is not null) { PlacementLevels.SetLevelOnly(doc, inst, level); return inst; }
        }
        catch { /* host-requiring family, or Revit rejected the plane */ }
        return null;
    }

    /// <summary>
    /// Places an UNHOSTED family instance at an absolute world point, choosing the correct
    /// Revit overload from the family's <see cref="FamilyPlacementType"/> — no reliance on
    /// exceptions for the common case. Sets level association so the device sits at exactly
    /// <paramref name="point"/>.Z. Returns null only if Revit rejects every overload.
    /// <para>Two placement primitives cover every point family:
    /// (1) a <b>level</b> instance (<c>OneLevelBased</c> — most upright/pendent heads and
    /// spot detectors) landed on the level then offset to point.Z; (2) a <b>work-plane</b>
    /// instance on a horizontal <see cref="SketchPlane"/> at point.Z (<c>WorkPlaneBased</c> /
    /// face-based / hosted families, for which the level overload THROWS). We try the primitive
    /// the family's type calls for first, and keep the other as a defensive fallback so an
    /// unusual family still lands. This is what makes heads place with linked or absent
    /// ceilings instead of failing the whole run.</para>
    /// </summary>
    public static FamilyInstance? PlaceUnhosted(Document doc, FamilySymbol symbol, Level level, XYZ point)
    {
        if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }

        // Work-plane / face-based / hosted families cannot use the level overload (it throws),
        // so put them on a sketch plane first; a plain level-based family goes on the level.
        bool workPlaneFirst = symbol.Family.FamilyPlacementType is
            FamilyPlacementType.WorkPlaneBased or FamilyPlacementType.OneLevelBasedHosted;

        FamilyInstance? inst = workPlaneFirst
            ? (PlaceOnSketchPlane(doc, symbol, level, point) ?? PlaceOnLevel(doc, symbol, level, point))
            : (PlaceOnLevel(doc, symbol, level, point) ?? PlaceOnSketchPlane(doc, symbol, level, point));
        return inst;
    }

    // OneLevelBased primitive: land on the level, then offset up to point.Z. Throws for
    // work-plane/face/hosted families (caught) — the caller falls back to a sketch plane.
    private static FamilyInstance? PlaceOnLevel(Document doc, FamilySymbol symbol, Level level, XYZ point)
    {
        try
        {
            FamilyInstance inst = doc.Create.NewFamilyInstance(
                point, symbol, level, StructuralType.NonStructural);
            if (inst is not null) { PlacementLevels.Apply(doc, inst, level, point.Z); return inst; }
        }
        catch { /* not a level-based family */ }
        return null;
    }

    // WorkPlaneBased primitive: place on a horizontal sketch plane at point.Z (already the
    // right elevation), then associate the level only (the plane fixes Z).
    private static FamilyInstance? PlaceOnSketchPlane(Document doc, FamilySymbol symbol, Level level, XYZ point)
    {
        try
        {
            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, point);
            SketchPlane sp = SketchPlane.Create(doc, plane);
            FamilyInstance inst = doc.Create.NewFamilyInstance(
                sp.GetPlaneReference(), point, XYZ.BasisX, symbol);
            if (inst is not null) { PlacementLevels.SetLevelOnly(doc, inst, level); return inst; }
        }
        catch { /* Revit rejected the work plane */ }
        return null;
    }

    public static bool WithinZ(FamilyInstance instance, double targetZ)
    {
        double? z = (instance.Location as LocationPoint)?.Point.Z;
        if (z is null)
        {
            BoundingBoxXYZ? bb = instance.get_BoundingBox(null);
            if (bb is not null) z = (bb.Min.Z + bb.Max.Z) / 2.0;
        }
        return z is double zz && Math.Abs(zz - targetZ) <= ZToleranceFt;
    }

    public static XYZ RoomCenter(Autodesk.Revit.DB.Architecture.Room room)
    {
        BoundingBoxXYZ? bb = room.get_BoundingBox(null);
        return bb is null ? XYZ.Zero : (bb.Min + bb.Max) / 2.0;
    }

    /// <summary>Interior vertical planar face of a wall (the one facing the room).</summary>
    public static Reference? InteriorWallFace(Wall wall, XYZ roomCenter)
    {
        var opts = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
        GeometryElement? geo = wall.get_Geometry(opts);
        if (geo is null) return null;

        Reference? best = null;
        double bestDot = double.MinValue;
        foreach (GeometryObject obj in geo)
        {
            if (obj is not Solid solid || solid.Volume < 1e-9) continue;
            foreach (Face face in solid.Faces)
            {
                if (face is not PlanarFace pf) continue;
                if (Math.Abs(pf.FaceNormal.Z) > 0.1) continue; // vertical faces only
                XYZ toRoom = new XYZ(roomCenter.X - pf.Origin.X, roomCenter.Y - pf.Origin.Y, 0);
                if (toRoom.IsZeroLength()) continue;
                double dot = toRoom.Normalize().DotProduct(pf.FaceNormal);
                if (dot > bestDot) { bestDot = dot; best = pf.Reference; }
            }
        }
        return best;
    }

    /// <summary>Flips a wall-mounted device so it faces INTO the room, not into the wall.</summary>
    public static void OrientTowardRoom(Document doc, FamilyInstance inst, XYZ roomCenter)
    {
        try
        {
            XYZ origin = (inst.Location as LocationPoint)?.Point ?? inst.GetTransform().Origin;
            XYZ toRoom = new XYZ(roomCenter.X - origin.X, roomCenter.Y - origin.Y, 0);
            if (toRoom.IsZeroLength()) return;
            XYZ facing = new XYZ(inst.FacingOrientation.X, inst.FacingOrientation.Y, 0);
            if (facing.IsZeroLength()) return;

            if (facing.Normalize().DotProduct(toRoom.Normalize()) < 0 && inst.CanFlipFacing)
            {
                inst.flipFacing();
                doc.Regenerate();
            }
        }
        catch { /* best-effort */ }
    }
}
