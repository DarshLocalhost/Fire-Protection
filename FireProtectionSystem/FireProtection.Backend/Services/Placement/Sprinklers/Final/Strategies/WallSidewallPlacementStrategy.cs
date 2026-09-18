using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.Strategies
{
    /// <summary>
    /// Professional Wall-Hosted / Sidewall Placement Strategy.
    /// Strictly anchors devices to the room's boundary edge line (segment A->B) and ensures
    /// correct Level association and positive elevation offsets.
    /// </summary>
    internal sealed class WallSidewallPlacementStrategy : IFamilyPlacementStrategy
    {
        public string Name => "WallSidewall";

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

            // Inward horizontal unit vector pointing into the room interior
            XYZ roomInward2D = ComputeRoomInwardHorizontal(polygon, midPt2D);

            if (!context.Symbol.IsActive)
            {
                context.Symbol.Activate();
                doc.Regenerate();
            }

            // Tight 1.5 ft search along the specific edge segment A->B
            WallFaceHost host = FindWallFaceHostForEdge(doc, a, b, edgeDir, xyz, roomInward2D);

            Plane wallPlane = host?.HostPlane;
            Reference faceRef = host?.FaceReference;
            string linkName = host?.LinkInstance != null ? (host.LinkInstance.Name ?? host.LinkInstance.Id.ToString()) : null;
            string wallElemId = host?.WallElementId ?? ("wall-edge-" + edgeIdx);
            string ceilingSourceTag = host?.LinkInstance != null ? ("link-wall-edge-" + edgeIdx) : ("wall-edge-" + edgeIdx);

            // Precision Fallback: Construct plane directly on room boundary line A->B
            if (wallPlane == null)
            {
                XYZ wallNormal = new XYZ(-edgeDir.Y, edgeDir.X, 0);
                if (wallNormal.DotProduct(roomInward2D) < 0) wallNormal = wallNormal.Negate();
                XYZ edgePoint3D = new XYZ(midPt2D.X, midPt2D.Y, xyz.Z);
                wallPlane = Plane.CreateByNormalAndOrigin(wallNormal, edgePoint3D);
            }

            XYZ pointOnWall = ProjectPointOntoPlane(xyz, wallPlane);

            // --- Attempt 1: Physical Face Reference (FaceBased wall families) ---
            if (faceRef != null)
            {
                FamilyInstance faceInst = null;
                try
                {
                    faceInst = doc.Create.NewFamilyInstance(faceRef, pointOnWall, XYZ.BasisZ, context.Symbol);

                    // FIX: Enforce level AND offset BEFORE checking spatial sanity
                    LevelAssociation.EnforceAndVerify(doc, faceInst, context.Level, xyz.Z);

                    if (IsSpatiallySane(faceInst, xyz, maxDeviationFt: 2.0))
                    {
                        return PlacementOutcome.CreatedInstance(
                            faceInst, "WallSidewallFace", ceilingSourceTag, linkName, wallElemId);
                    }

                    try { doc.Delete(faceInst.Id); } catch { }
                }
                catch
                {
                    if (faceInst != null) { try { doc.Delete(faceInst.Id); } catch { } }
                }
            }

            // --- Attempt 2: Level-Hosted Family Placement (OneLevelBased wall-strobe families) ---
            try
            {
                FamilyInstance lvlInst = doc.Create.NewFamilyInstance(
                    pointOnWall, context.Symbol, context.Level, StructuralType.NonStructural);

                // FIX: Enforce level AND offset BEFORE checking spatial sanity
                LevelAssociation.EnforceAndVerify(doc, lvlInst, context.Level, xyz.Z);
                OrientTowardRoomInterior(doc, lvlInst, pointOnWall, roomInward2D);

                if (IsSpatiallySane(lvlInst, xyz, maxDeviationFt: 2.0))
                {
                    return PlacementOutcome.CreatedInstance(
                        lvlInst, "WallSidewallLevelHosted", ceilingSourceTag, linkName, wallElemId);
                }

                try { doc.Delete(lvlInst.Id); } catch { }
            }
            catch { }

            // --- Attempt 3: SketchPlane Work-Plane Fallback ---
            try
            {
                Plane plane = Plane.CreateByNormalAndOrigin(wallPlane.Normal, pointOnWall);
                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

                FamilyInstance instance = doc.Create.NewFamilyInstance(
                    sketchPlane.GetPlaneReference(), pointOnWall, edgeDir, context.Symbol);

                // FIX: Enforce level AND offset BEFORE checking spatial sanity
                LevelAssociation.EnforceAndVerify(doc, instance, context.Level, xyz.Z);

                if (IsSpatiallySane(instance, xyz, maxDeviationFt: 2.0))
                {
                    return PlacementOutcome.CreatedInstance(
                        instance, "WallSidewallSketchPlane", ceilingSourceTag, linkName, wallElemId);
                }

                try { doc.Delete(instance.Id); } catch { }
            }
            catch { }

            return PlacementOutcome.Fail(
                PlacementStatusCodes.PlacementFailed,
                $"Wall placement failed near edge {edgeIdx}.",
                hostingStrategy: "WallSidewall",
                ceilingSource: ceilingSourceTag,
                linkInstanceName: linkName,
                hostCeilingElementId: wallElemId);
        }

        private static void OrientTowardRoomInterior(Document doc, FamilyInstance instance, XYZ origin, XYZ roomInward2D)
        {
            if (instance == null || roomInward2D == null) return;
            try
            {
                Line axis = Line.CreateBound(origin, new XYZ(origin.X, origin.Y, origin.Z + 1.0));
                double angle = XYZ.BasisX.AngleOnPlaneTo(roomInward2D, XYZ.BasisZ);
                ElementTransformUtils.RotateElement(doc, instance.Id, axis, angle);
            }
            catch { }
        }

        private static bool IsSpatiallySane(FamilyInstance instance, XYZ requested, double maxDeviationFt)
        {
            if (instance == null || instance.Id == ElementId.InvalidElementId) return false;
            LocationPoint lp = instance.Location as LocationPoint;
            if (lp == null || lp.Point == null) return false;
            XYZ p = lp.Point;

            // Reject origin (0,0,0) snaps
            if (p.GetLength() < 0.5 && requested.GetLength() > 2.0) return false;

            double xyDev = Math.Sqrt(Math.Pow(p.X - requested.X, 2) + Math.Pow(p.Y - requested.Y, 2));
            return xyDev <= maxDeviationFt;
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

        private sealed class WallFaceHost
        {
            public Reference FaceReference;
            public Plane HostPlane;
            public RevitLinkInstance LinkInstance;
            public string WallElementId;
        }

        private static WallFaceHost FindWallFaceHostForEdge(
            Document doc, double[] pA, double[] pB, XYZ edgeDir, XYZ requestedPoint, XYZ roomInward2D)
        {
            XYZ midPt2D = new XYZ((pA[0] + pB[0]) * 0.5, (pA[1] + pB[1]) * 0.5, 0);
            double tightSearchMaxDistFt = 1.5;
            double minDot = 0.707;

            WallFaceHost best = FindWallInDoc(doc, null, midPt2D, edgeDir, requestedPoint, roomInward2D, tightSearchMaxDistFt, minDot);
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

                WallFaceHost hit = FindWallInDoc(linkDoc, link, midLink, dirLink, reqLink, inwardLink, tightSearchMaxDistFt, minDot);
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
                if (proj < -1.5 || proj > len + 1.5) continue;

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
                            if (Math.Abs(pf.FaceNormal.Z) > 0.3) continue;

                            double towardRoom = pf.FaceNormal.DotProduct(
                                new XYZ(roomInward2DInThisDoc.X, roomInward2DInThisDoc.Y, 0));

                            IntersectionResult ir = pf.Project(requestedPointInThisDoc);
                            double dist = ir != null ? Math.Abs(ir.Distance) : 999.0;
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
                catch { }
            }

            if (bestFace == null)
            {
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