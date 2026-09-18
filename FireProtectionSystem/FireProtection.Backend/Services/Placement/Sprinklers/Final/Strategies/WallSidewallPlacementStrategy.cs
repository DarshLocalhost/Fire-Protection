using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.Strategies
{
    /// <summary>
    /// Wall-hosted sidewall placement. Walls often live in an architectural link.
    /// NewFamilyInstance on a CreateLinkReference wall face is runtime-proven to snap
    /// WorkPlaneBased sprinklers to (0,0,0) while reporting the link instance as Host
    /// (see placement_result JSON 2026-09-08). Correct path: host-space wall plane +
    /// SketchPlane (same discipline as WorkPlaneBased ceiling fallback). Face-host is
    /// attempted first only when post-create location stays near the request.
    /// </summary>
    internal sealed class WallSidewallPlacementStrategy : IFamilyPlacementStrategy
    {
        public string Name => "WallSidewall";

        // Only claim WallSidewall as a FamilyPlacementType string. Service already
        // routes by point.WallEdgeIndex; claiming FaceBased/WorkPlaneBased here steals
        // ceiling families if order ever changes.
        public bool CanHandle(string familyPlacementType) =>
            string.Equals(familyPlacementType, "WallSidewall", StringComparison.OrdinalIgnoreCase);

        public PlacementOutcome Place(PlacementContext context)
        {
            if (!context.WallEdgeIndex.HasValue || context.RoomPolygon == null || context.RoomPolygon.Count < 3)
            {
                return PlacementOutcome.Fail(
                    PlacementStatusCodes.PlacementFailed,
                    "WallSidewall placement requires a wall edge index and room polygon.");
            }

            Document doc = context.Document;
            XYZ xyz = context.RequestedPoint;

            int edgeIdx = context.WallEdgeIndex.Value;
            List<double[]> polygon = context.RoomPolygon;

            if (edgeIdx < 0 || edgeIdx >= polygon.Count)
            {
                return PlacementOutcome.Fail(
                    PlacementStatusCodes.PlacementFailed,
                    $"WallEdgeIndex {edgeIdx} is out of range for polygon with {polygon.Count} vertices.");
            }

            double[] a = polygon[edgeIdx];
            double[] b = polygon[(edgeIdx + 1) % polygon.Count];
            if (a == null || b == null || a.Length < 2 || b.Length < 2)
            {
                return PlacementOutcome.Fail(
                    PlacementStatusCodes.PlacementFailed,
                    "Wall edge vertices are invalid.");
            }

            XYZ midPt2D = new XYZ((a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5, 0);
            XYZ edgeDir = new XYZ(b[0] - a[0], b[1] - a[1], 0);
            if (edgeDir.GetLength() < 1e-9)
            {
                return PlacementOutcome.Fail(
                    PlacementStatusCodes.PlacementFailed,
                    "Wall edge has zero length.");
            }
            edgeDir = edgeDir.Normalize();

            // Inward horizontal direction (from edge midpoint toward room centroid) — used to pick the room-facing face.
            XYZ roomInward2D = ComputeRoomInwardHorizontal(polygon, midPt2D);

            WallFaceHost host = FindWallFaceHost(doc, midPt2D, edgeDir, xyz, roomInward2D);
            if (host == null || host.HostPlane == null)
            {
                return PlacementOutcome.Fail(
                    PlacementStatusCodes.PlacementFailed,
                    $"No wall found near edge {edgeIdx} (midpoint [{midPt2D.X:F2}, {midPt2D.Y:F2}]). " +
                    "Ensure the room boundary corresponds to wall elements in the host or a loaded link.");
            }

            // Placement location must lie on the host plane (candidates are ~0.5 ft inboard of the face).
            XYZ pointOnFace = ProjectPointOntoPlane(xyz, host.HostPlane);
            XYZ refDir = edgeDir;
            // referenceDirection must lie in the plane (perpendicular to normal).
            XYZ n = host.HostPlane.Normal;
            refDir = (refDir - n.Multiply(refDir.DotProduct(n)));
            if (refDir.GetLength() < 1e-9)
                refDir = n.CrossProduct(Math.Abs(n.DotProduct(XYZ.BasisZ)) < 0.9 ? XYZ.BasisZ : XYZ.BasisX);
            refDir = refDir.Normalize();

            if (!context.Symbol.IsActive)
            {
                context.Symbol.Activate();
                doc.Regenerate();
            }

            string linkName = host.LinkInstance != null
                ? (host.LinkInstance.Name ?? host.LinkInstance.Id.ToString())
                : null;
            string ceilingSourceTag = host.LinkInstance != null
                ? ("link-wall-edge-" + edgeIdx)
                : ("wall-edge-" + edgeIdx);

            // --- Attempt 1: face reference (host or CreateLinkReference). Keep only if location is sane. ---
            if (host.FaceReference != null)
            {
                FamilyInstance faceInst = null;
                try
                {
                    faceInst = doc.Create.NewFamilyInstance(
                        host.FaceReference, pointOnFace, refDir, context.Symbol);

                    if (IsSpatiallySane(faceInst, xyz, maxDeviationFt: 2.0))
                    {
                        return PlacementOutcome.CreatedInstance(
                            faceInst, "WallSidewallFace", ceilingSourceTag, linkName, host.WallElementId);
                    }

                    // Origin-snap / bad bind: delete and fall through to SketchPlane.
                    try { doc.Delete(faceInst.Id); } catch { /* best-effort */ }
                }
                catch
                {
                    if (faceInst != null)
                    {
                        try { doc.Delete(faceInst.Id); } catch { }
                    }
                }
            }

            // --- Attempt 2: SketchPlane on the wall plane in HOST coordinates (WorkPlaneBased-safe). ---
            try
            {
                // Plane through the projected point with the wall normal (host space).
                Plane plane = Plane.CreateByNormalAndOrigin(host.HostPlane.Normal, pointOnFace);
                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

                FamilyInstance instance = doc.Create.NewFamilyInstance(
                    sketchPlane.GetPlaneReference(), pointOnFace, refDir, context.Symbol);

                if (!IsSpatiallySane(instance, xyz, maxDeviationFt: 2.0))
                {
                    try { doc.Delete(instance.Id); } catch { }
                    return PlacementOutcome.Fail(
                        PlacementStatusCodes.RevitCreationFailed,
                        "WallSidewall SketchPlane placement produced a spatially invalid instance " +
                        "(e.g. origin snap). Refused rather than keeping a mis-hosted element.",
                        hostingStrategy: "WallSidewallSketchPlane",
                        ceilingSource: ceilingSourceTag,
                        linkInstanceName: linkName,
                        hostCeilingElementId: host.WallElementId);
                }

                return PlacementOutcome.CreatedInstance(
                    instance, "WallSidewallSketchPlane", ceilingSourceTag, linkName, host.WallElementId);
            }
            catch (Exception ex)
            {
                return PlacementOutcome.Fail(
                    PlacementStatusCodes.PlacementFailed,
                    "WallSidewall placement failed on face and SketchPlane: " + ex.Message,
                    hostingStrategy: "WallSidewallSketchPlane",
                    ceilingSource: ceilingSourceTag,
                    linkInstanceName: linkName,
                    hostCeilingElementId: host.WallElementId);
            }
        }

        // -------------------------------------------------------------------------------------------------
        // Spatial sanity (catches the historic (0,0,0) bind without waiting for the service summary)
        // -------------------------------------------------------------------------------------------------

        private static bool IsSpatiallySane(FamilyInstance instance, XYZ requested, double maxDeviationFt)
        {
            if (instance == null || instance.Id == ElementId.InvalidElementId) return false;
            LocationPoint lp = instance.Location as LocationPoint;
            if (lp == null || lp.Point == null) return false;
            XYZ p = lp.Point;
            // Explicit origin-snap guard (any near-origin placement when request is far away).
            if (p.GetLength() < 0.5 && requested.GetLength() > 2.0) return false;
            return p.DistanceTo(requested) <= maxDeviationFt;
        }

        private static XYZ ProjectPointOntoPlane(XYZ p, Plane plane)
        {
            XYZ n = plane.Normal;
            XYZ o = plane.Origin;
            double dist = (p - o).DotProduct(n);
            return p - n.Multiply(dist);
        }

        private static XYZ ComputeRoomInwardHorizontal(List<double[]> polygon, XYZ edgeMid2D)
        {
            double cx = 0, cy = 0;
            int n = 0;
            foreach (double[] v in polygon)
            {
                if (v == null || v.Length < 2) continue;
                cx += v[0]; cy += v[1]; n++;
            }
            if (n == 0) return XYZ.BasisX;
            cx /= n; cy /= n;
            XYZ inward = new XYZ(cx - edgeMid2D.X, cy - edgeMid2D.Y, 0);
            return inward.GetLength() > 1e-9 ? inward.Normalize() : XYZ.BasisX;
        }

        // -------------------------------------------------------------------------------------------------
        // Wall discovery (host + links)
        // -------------------------------------------------------------------------------------------------

        private sealed class WallFaceHost
        {
            public Reference FaceReference;   // may be null if only plane is available
            public Plane HostPlane;           // always in HOST coordinates
            public RevitLinkInstance LinkInstance;
            public string WallElementId;
        }

        private static WallFaceHost FindWallFaceHost(
            Document doc, XYZ midPt2D, XYZ edgeDir, XYZ requestedPoint, XYZ roomInward2D)
        {
            double maxDistFt = 6.0;
            double minDot = 0.707; // 45°

            WallFaceHost best = FindWallInDoc(doc, null, midPt2D, edgeDir, requestedPoint, roomInward2D, maxDistFt, minDot);
            if (best != null) return best;

            foreach (Element e in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)))
            {
                RevitLinkInstance link = e as RevitLinkInstance;
                Document linkDoc = link?.GetLinkDocument();
                if (linkDoc == null) continue;

                Transform toLink = link.GetTotalTransform().Inverse;
                XYZ midLink = toLink.OfPoint(midPt2D);
                XYZ dirLink = toLink.OfVector(edgeDir).Normalize();
                XYZ reqLink = toLink.OfPoint(requestedPoint);
                XYZ inwardLink = toLink.OfVector(roomInward2D);
                if (inwardLink.GetLength() > 1e-9) inwardLink = inwardLink.Normalize();

                WallFaceHost hit = FindWallInDoc(linkDoc, link, midLink, dirLink, reqLink, inwardLink, maxDistFt, minDot);
                if (hit != null) return hit;
            }

            return null;
        }

        private static WallFaceHost FindWallInDoc(
            Document doc,
            RevitLinkInstance linkInstance,
            XYZ midPt2D,
            XYZ edgeDir,
            XYZ requestedPoint,
            XYZ roomInward2D,
            double maxDistFt,
            double minDot)
        {
            Wall bestWall = null;
            double bestDist = double.MaxValue;

            foreach (Wall wall in new FilteredElementCollector(doc).OfClass(typeof(Wall)).WhereElementIsNotElementType())
            {
                LocationCurve lc = wall.Location as LocationCurve;
                if (lc?.Curve == null) continue;

                Curve curve = lc.Curve;
                XYZ p0 = curve.GetEndPoint(0);
                XYZ p1 = curve.GetEndPoint(1);
                XYZ a = new XYZ(p0.X, p0.Y, 0);
                XYZ b = new XYZ(p1.X, p1.Y, 0);
                XYZ wallVec = b - a;
                double len = wallVec.GetLength();
                if (len < 1e-6) continue;

                XYZ wallDir = wallVec.Normalize();
                if (Math.Abs(wallDir.DotProduct(edgeDir)) < minDot) continue;

                double proj = (midPt2D - a).DotProduct(wallDir);
                XYZ closest = a + wallDir * proj;
                double perp = Math.Sqrt(
                    (midPt2D.X - closest.X) * (midPt2D.X - closest.X) +
                    (midPt2D.Y - closest.Y) * (midPt2D.Y - closest.Y));
                if (perp > maxDistFt) continue;
                if (proj < -3.0 || proj > len + 3.0) continue;

                if (perp < bestDist)
                {
                    bestDist = perp;
                    bestWall = wall;
                }
            }

            if (bestWall == null) return null;

            return BuildHostFromWall(doc, bestWall, linkInstance, requestedPoint, roomInward2D);
        }

        private static WallFaceHost BuildHostFromWall(
            Document doc,
            Wall wall,
            RevitLinkInstance linkInstance,
            XYZ requestedPointInThisDoc,
            XYZ roomInward2DInThisDoc)
        {
            Transform toHost = linkInstance != null ? linkInstance.GetTotalTransform() : Transform.Identity;

            // Prefer planar vertical face whose normal points toward the room (inward).
            Options opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
            GeometryElement geom = wall.get_Geometry(opt);

            PlanarFace bestFace = null;
            Reference bestRef = null;
            double bestScore = double.MinValue;

            if (geom != null)
            {
                foreach (GeometryObject obj in geom)
                {
                    foreach (Solid solid in ExtractSolids(obj))
                    {
                        if (solid == null || solid.Faces.Size == 0) continue;
                        foreach (Face face in solid.Faces)
                        {
                            PlanarFace pf = face as PlanarFace;
                            if (pf == null) continue;
                            if (Math.Abs(pf.FaceNormal.Z) > 0.3) continue; // need vertical-ish

                            // Normal in this document; score by alignment with room inward.
                            double towardRoom = pf.FaceNormal.DotProduct(
                                new XYZ(roomInward2DInThisDoc.X, roomInward2DInThisDoc.Y, 0));

                            IntersectionResult ir = pf.Project(requestedPointInThisDoc);
                            double dist = ir != null ? Math.Abs(ir.Distance) : 999.0;
                            // Prefer faces that face the room and are close to the point.
                            double score = towardRoom * 10.0 - dist;
                            if (score > bestScore && pf.Reference != null)
                            {
                                bestScore = score;
                                bestFace = pf;
                                bestRef = pf.Reference;
                            }
                        }
                    }
                }
            }

            // Fallback: HostObjectUtils side faces if geometry walk found nothing.
            if (bestFace == null)
            {
                try
                {
                    IList<Reference> sides = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior);
                    if (sides == null || sides.Count == 0)
                        sides = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior);
                    if (sides != null && sides.Count > 0)
                    {
                        bestRef = sides[0];
                        GeometryObject go = wall.GetGeometryObjectFromReference(bestRef);
                        bestFace = go as PlanarFace;
                    }
                }
                catch { /* ignore */ }
            }

            if (bestFace == null)
            {
                // Last resort: vertical plane from location curve + inward normal.
                LocationCurve lc = wall.Location as LocationCurve;
                if (lc?.Curve == null) return null;
                XYZ p0 = lc.Curve.GetEndPoint(0);
                XYZ p1 = lc.Curve.GetEndPoint(1);
                XYZ dir = (p1 - p0).Normalize();
                XYZ nLocal = new XYZ(-dir.Y, dir.X, 0);
                if (nLocal.DotProduct(roomInward2DInThisDoc) < 0) nLocal = nLocal.Negate();
                XYZ originLocal = new XYZ(
                    (p0.X + p1.X) * 0.5,
                    (p0.Y + p1.Y) * 0.5,
                    requestedPointInThisDoc.Z);

                XYZ nHost = toHost.OfVector(nLocal).Normalize();
                XYZ oHost = toHost.OfPoint(originLocal);
                return new WallFaceHost
                {
                    FaceReference = null,
                    HostPlane = Plane.CreateByNormalAndOrigin(nHost, oHost),
                    LinkInstance = linkInstance,
                    WallElementId = wall.Id.ToString()
                };
            }

            XYZ normalHost = toHost.OfVector(bestFace.FaceNormal).Normalize();
            XYZ originHost = toHost.OfPoint(bestFace.Origin);

            Reference faceRefHost = bestRef;
            if (linkInstance != null && bestRef != null)
                faceRefHost = bestRef.CreateLinkReference(linkInstance);

            return new WallFaceHost
            {
                FaceReference = faceRefHost,
                HostPlane = Plane.CreateByNormalAndOrigin(normalHost, originHost),
                LinkInstance = linkInstance,
                WallElementId = wall.Id.ToString()
            };
        }

        private static IEnumerable<Solid> ExtractSolids(GeometryObject obj)
        {
            if (obj is Solid s && s.Volume > 0)
            {
                yield return s;
            }
            else if (obj is GeometryInstance gi)
            {
                GeometryElement ge = gi.GetInstanceGeometry();
                if (ge == null) yield break;
                foreach (GeometryObject child in ge)
                    foreach (Solid solid in ExtractSolids(child))
                        yield return solid;
            }
        }
    }
}