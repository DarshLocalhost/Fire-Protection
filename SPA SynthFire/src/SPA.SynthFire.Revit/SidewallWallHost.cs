using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace SPA.SynthFire.Revit;

/// <summary>
/// Hosts a sidewall sprinkler head to the wall it sits against — ported from the proven
/// FireProtectionSystem <c>WallSidewallPlacementStrategy</c> (production, industry-standard).
///
/// <para>Why this exists (after a long detour through ray-cast wall finding): the head's wall is
/// almost always in a LINKED architectural model, and two Revit facts defeated the earlier code —
/// (1) a linked wall face is only a valid host once its reference is rebuilt with
/// <see cref="Reference.CreateLinkReference(RevitLinkInstance)"/>; (2) a VERTICAL wall face does
/// not fix the head's Z, so the elevation offset must be set explicitly or the head sits at the
/// floor (the <c>bboxZ=-0.0</c> we saw). This helper does both, and finds the wall by iterating
/// wall ELEMENTS + their geometry faces (own doc, then every link with the right coordinate
/// transform) rather than a ray that missed.</para>
///
/// <para>Placement ladder, in order: (a) face-host to the room-facing wall face (own or linked);
/// (b) level-host at the point + rotate to face the room; (c) a vertical sketch plane on the wall
/// plane. Each result is Z-lifted to the target and sanity-checked (near the requested XY, not
/// collapsed to the origin). Returns null when no wall is reachable — the caller then free-places.</para>
/// </summary>
public static class SidewallWallHost
{
    public enum Strategy { None, Face, LevelHosted, SketchPlane }

    public readonly record struct Result(FamilyInstance? Instance, Strategy How, bool Linked)
    {
        public bool Placed => Instance is not null;
    }

    // How far (feet) from the head, perpendicular to a wall line, that wall still counts as the
    // head's wall. Generous enough for a solved standoff point, tight enough not to grab a far wall.
    private const double WallSearchFt = 3.0;

    // A placed head more than this far (XY, feet) from where it was asked to go is wrong.
    private const double MaxXyDeviationFt = 2.5;

    /// <summary>
    /// Places one sidewall head at <paramref name="point"/> (world X/Y and target deflector Z),
    /// facing <paramref name="inward"/> (the throw direction, into the room). Finds the room-facing
    /// wall face — own doc first, then each linked model — and hosts the head to it at the target Z.
    /// </summary>
    public static Result Place(Document doc, FamilySymbol symbol, Level level, XYZ point, XYZ inward)
    {
        if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }

        var inward2D = new XYZ(inward.X, inward.Y, 0);
        if (inward2D.IsZeroLength()) inward2D = XYZ.BasisX;
        else inward2D = inward2D.Normalize();

        WallFaceHost? host = FindWallFaceHost(doc, point, inward2D);
        if (host is null) return new Result(null, Strategy.None, false);

        bool linked = host.LinkInstance is not null;
        XYZ pointOnWall = ProjectOntoPlane(point, host.HostPlane);
        // Keep the target Z (the wall face is vertical, so projecting onto it must not change Z).
        pointOnWall = new XYZ(pointOnWall.X, pointOnWall.Y, point.Z);

        // (a) Face-host to the wall's room-facing face (own or, via CreateLinkReference, linked).
        if (host.FaceReference is Reference faceRef)
        {
            FamilyInstance? inst = null;
            try { inst = doc.Create.NewFamilyInstance(faceRef, pointOnWall, XYZ.BasisZ, symbol); }
            catch { inst = null; }
            if (inst is not null)
            {
                PlacementLevels.Apply(doc, inst, level, point.Z); // sets level AND offset → lifts to Z
                if (IsSane(inst, point)) return new Result(inst, Strategy.Face, linked);
                TryDelete(doc, inst);
            }
        }

        // (b) Level-host at the point, then rotate to face the room. Works for a plain level family
        //     and for many hosted families that reject the face overload against a link.
        try
        {
            FamilyInstance inst = doc.Create.NewFamilyInstance(
                pointOnWall, symbol, level, StructuralType.NonStructural);
            PlacementLevels.Apply(doc, inst, level, point.Z);
            OrientTowardRoom(doc, inst, pointOnWall, inward2D);
            if (IsSane(inst, point)) return new Result(inst, Strategy.LevelHosted, linked);
            TryDelete(doc, inst);
        }
        catch { /* not level-placeable */ }

        // (c) Last resort — a VERTICAL sketch plane on the wall plane (normal = wall normal),
        //     reference direction world-up so the family stands upright on the wall.
        try
        {
            Plane plane = Plane.CreateByNormalAndOrigin(host.HostPlane.Normal, pointOnWall);
            SketchPlane sp = SketchPlane.Create(doc, plane);
            FamilyInstance inst = doc.Create.NewFamilyInstance(sp.GetPlaneReference(), pointOnWall, XYZ.BasisZ, symbol);
            PlacementLevels.SetLevelOnly(doc, inst, level);
            if (IsSane(inst, point)) return new Result(inst, Strategy.SketchPlane, linked);
            TryDelete(doc, inst);
        }
        catch { /* Revit rejected the wall plane */ }

        return new Result(null, Strategy.None, linked);
    }

    // A placed head must land near where it was asked to go and must not have collapsed to the
    // project origin (the classic hosted-family failure). XY only — the offset fixes Z, and a
    // face-hosted family's LocationPoint.Z is unreliable, so Z is not part of the sanity test.
    private static bool IsSane(FamilyInstance inst, XYZ requested)
    {
        if (inst is not { IsValidObject: true }) return false;
        XYZ? p = (inst.Location as LocationPoint)?.Point;
        if (p is null)
        {
            BoundingBoxXYZ? bb = inst.get_BoundingBox(null);
            if (bb is null) return true; // can't measure — trust the host
            p = (bb.Min + bb.Max) / 2.0;
        }
        if (p.GetLength() < 0.5 && requested.GetLength() > 2.0) return false; // collapsed to origin
        double dx = p.X - requested.X, dy = p.Y - requested.Y;
        return Math.Sqrt(dx * dx + dy * dy) <= MaxXyDeviationFt;
    }

    private static void OrientTowardRoom(Document doc, FamilyInstance inst, XYZ origin, XYZ inward2D)
    {
        try
        {
            Line axis = Line.CreateBound(origin, new XYZ(origin.X, origin.Y, origin.Z + 1.0));
            double angle = XYZ.BasisX.AngleOnPlaneTo(inward2D, XYZ.BasisZ);
            ElementTransformUtils.RotateElement(doc, inst.Id, axis, angle);
        }
        catch { /* best-effort facing */ }
    }

    private static void TryDelete(Document doc, FamilyInstance inst)
    {
        try { doc.Delete(inst.Id); } catch { /* already gone */ }
    }

    private static XYZ ProjectOntoPlane(XYZ p, Plane plane)
    {
        double dist = (p - plane.Origin).DotProduct(plane.Normal);
        return p - plane.Normal.Multiply(dist);
    }

    private sealed class WallFaceHost
    {
        public Reference? FaceReference;
        public Plane HostPlane = null!;
        public RevitLinkInstance? LinkInstance;
    }

    // Find the room-facing wall face nearest the head: own document first, then each linked model
    // (the head's point/direction are transformed into link coordinates for the search, and the
    // resulting face reference is rebuilt into host coordinates via CreateLinkReference).
    private static WallFaceHost? FindWallFaceHost(Document doc, XYZ point, XYZ inward2D)
    {
        var point2D = new XYZ(point.X, point.Y, 0);

        WallFaceHost? own = FindInDoc(doc, null, point2D, inward2D, point);
        if (own is not null) return own;

        foreach (RevitLinkInstance link in new FilteredElementCollector(doc)
                     .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
        {
            Document? linkDoc = link.GetLinkDocument();
            if (linkDoc is null) continue;

            Transform toLink = link.GetTotalTransform().Inverse;
            XYZ pLink2D = toLink.OfPoint(point2D); pLink2D = new XYZ(pLink2D.X, pLink2D.Y, 0);
            XYZ inLink = toLink.OfVector(inward2D);
            inLink = inLink.IsZeroLength() ? XYZ.BasisX : new XYZ(inLink.X, inLink.Y, 0).Normalize();
            XYZ pLink = toLink.OfPoint(point);

            WallFaceHost? hit = FindInDoc(linkDoc, link, pLink2D, inLink, pLink);
            if (hit is not null) return hit;
        }
        return null;
    }

    private static WallFaceHost? FindInDoc(
        Document doc, RevitLinkInstance? link, XYZ point2D, XYZ inward2D, XYZ requestedPoint)
    {
        // Walls whose line runs perpendicular-ish to the throw (the head faces the wall's normal)
        // and pass within WallSearchFt of the head, nearest first.
        var candidates = new List<(Wall wall, double perp)>();
        foreach (Wall wall in new FilteredElementCollector(doc)
                     .OfClass(typeof(Wall)).WhereElementIsNotElementType().Cast<Wall>())
        {
            if (wall.Location is not LocationCurve lc || lc.Curve is null) continue;
            XYZ e0 = lc.Curve.GetEndPoint(0), e1 = lc.Curve.GetEndPoint(1);
            var a = new XYZ(e0.X, e0.Y, 0);
            XYZ v = new XYZ(e1.X - e0.X, e1.Y - e0.Y, 0);
            double len = v.GetLength();
            if (len < 1e-6) continue;
            XYZ dir = v.Normalize();

            // A sidewall head faces perpendicular to its wall, so its wall runs across the throw.
            if (Math.Abs(dir.DotProduct(inward2D)) > 0.6) continue;

            double proj = (point2D - a).DotProduct(dir);
            if (proj < -1.5 || proj > len + 1.5) continue; // off the ends of this wall
            XYZ closest = a + dir.Multiply(proj);
            double perp = new XYZ(point2D.X - closest.X, point2D.Y - closest.Y, 0).GetLength();
            if (perp > WallSearchFt) continue;
            candidates.Add((wall, perp));
        }

        foreach (var (wall, _) in candidates.OrderBy(c => c.perp))
        {
            WallFaceHost? host = BuildHostFromWall(doc, wall, link, requestedPoint, inward2D);
            if (host is not null) return host;
        }
        return null;
    }

    // Build a host (face reference + host-coordinate plane) from a wall's room-facing planar face.
    // Mirrors FireProtectionSystem: score faces by how well their normal faces the room minus the
    // distance to the head; fall back to HostObjectUtils side faces, then to a bare plane.
    private static WallFaceHost? BuildHostFromWall(
        Document doc, Wall wall, RevitLinkInstance? link, XYZ requestedPoint, XYZ inward2D)
    {
        Transform toHost = link is null ? Transform.Identity : link.GetTotalTransform();

        var opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
        GeometryElement? geom = wall.get_Geometry(opt);

        PlanarFace? bestFace = null;
        Reference? bestRef = null;
        double bestScore = double.MinValue;

        if (geom is not null)
        {
            foreach (GeometryObject obj in geom)
                foreach (Solid solid in ExtractSolids(obj))
                    foreach (Face face in solid.Faces)
                    {
                        if (face is not PlanarFace pf) continue;
                        if (Math.Abs(pf.FaceNormal.Z) > 0.3) continue; // vertical faces only
                        double towardRoom = pf.FaceNormal.DotProduct(inward2D);
                        IntersectionResult? ir = pf.Project(requestedPoint);
                        double dist = ir is not null ? Math.Abs(ir.Distance) : 999.0;
                        double score = towardRoom * 10.0 - dist;
                        if (score > bestScore && pf.Reference is not null)
                        {
                            bestScore = score; bestFace = pf; bestRef = pf.Reference;
                        }
                    }
        }

        if (bestFace is null)
        {
            try
            {
                IList<Reference> sides = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior);
                if (sides is null || sides.Count == 0)
                    sides = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior);
                if (sides is { Count: > 0 })
                {
                    bestRef = sides[0];
                    bestFace = wall.GetGeometryObjectFromReference(bestRef) as PlanarFace;
                }
            }
            catch { /* no side faces */ }
        }

        // No usable face at all → a bare plane from the wall line (level-host / sketch-plane paths
        // still work off it; face-host is skipped because FaceReference stays null).
        if (bestFace is null)
        {
            if (wall.Location is not LocationCurve lc || lc.Curve is null) return null;
            XYZ p0 = lc.Curve.GetEndPoint(0), p1 = lc.Curve.GetEndPoint(1);
            XYZ dir = (p1 - p0).Normalize();
            var nLocal = new XYZ(-dir.Y, dir.X, 0);
            if (nLocal.DotProduct(inward2D) < 0) nLocal = nLocal.Negate();
            var originLocal = new XYZ((p0.X + p1.X) * 0.5, (p0.Y + p1.Y) * 0.5, requestedPoint.Z);
            return new WallFaceHost
            {
                FaceReference = null,
                HostPlane = Plane.CreateByNormalAndOrigin(
                    toHost.OfVector(nLocal).Normalize(), toHost.OfPoint(originLocal)),
                LinkInstance = link,
            };
        }

        XYZ normalHost = toHost.OfVector(bestFace.FaceNormal).Normalize();
        XYZ originHost = toHost.OfPoint(bestFace.Origin);
        Reference? faceRefHost = bestRef;
        if (link is not null && bestRef is not null) faceRefHost = bestRef.CreateLinkReference(link);

        return new WallFaceHost
        {
            FaceReference = faceRefHost,
            HostPlane = Plane.CreateByNormalAndOrigin(normalHost, originHost),
            LinkInstance = link,
        };
    }

    private static IEnumerable<Solid> ExtractSolids(GeometryObject obj)
    {
        if (obj is Solid s && s.Volume > 0) { yield return s; }
        else if (obj is GeometryInstance gi)
        {
            GeometryElement? ge = gi.GetInstanceGeometry();
            if (ge is null) yield break;
            foreach (GeometryObject child in ge)
                foreach (Solid solid in ExtractSolids(child))
                    yield return solid;
        }
    }
}
