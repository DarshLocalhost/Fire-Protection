using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.Strategies
{
    internal sealed class WallSidewallPlacementStrategy : IFamilyPlacementStrategy
    {
        public string Name => "WallSidewall";

        public bool CanHandle(string familyPlacementType)
        {
            if (string.IsNullOrWhiteSpace(familyPlacementType)) return false;
            return string.Equals(familyPlacementType, "WallSidewall", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(familyPlacementType, "FaceBased", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(familyPlacementType, "WorkPlaneBased", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(familyPlacementType, "OneLevelBased", StringComparison.OrdinalIgnoreCase);
        }

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

            XYZ roomInward2D = ComputeRoomInwardHorizontal(polygon, midPt2D);

            if (!context.Symbol.IsActive)
            {
                context.Symbol.Activate();
                doc.Regenerate();
            }

            WallFaceHost host = FindWallFaceHostForEdge(doc, a, b, edgeDir, xyz, roomInward2D);

            Plane wallPlane = host?.HostPlane;
            Reference faceRef = host?.FaceReference;
            string linkName = host?.LinkInstance != null ? (host.LinkInstance.Name ?? host.LinkInstance.Id.ToString()) : null;
            string wallElemId = host?.WallElementId ?? ("wall-edge-" + edgeIdx);
            string ceilingSourceTag = host?.LinkInstance != null ? ("link-wall-edge-" + edgeIdx) : ("wall-edge-" + edgeIdx);

            if (wallPlane == null)
            {
                XYZ wallNormal = new XYZ(-edgeDir.Y, edgeDir.X, 0);
                if (wallNormal.DotProduct(roomInward2D) < 0) wallNormal = wallNormal.Negate();
                XYZ edgePoint3D = new XYZ(midPt2D.X, midPt2D.Y, xyz.Z);
                wallPlane = Plane.CreateByNormalAndOrigin(wallNormal, edgePoint3D);
            }

            // Project the candidate point onto the wall plane strictly along the wall normal
            // so its X and Y coordinates along the wall are preserved exactly at the candidate location
            XYZ pointOnWall = ProjectPointOntoPlane(xyz, wallPlane);

            // Guard against end-face / oblique host planes. A true side plane is
            // perpendicular to the wall direction, so projecting along its normal can
            // never move the point ALONG the edge. A large drift means the host plane
            // was an end face (or the wall runs obliquely to the polygon edge) and every
            // head would collapse onto the same wall corner — reject rather than place.
            double alongDrift = Math.Abs((pointOnWall - xyz).DotProduct(edgeDir));
            if (alongDrift > 3.0)
            {
                return PlacementOutcome.Fail(
                    PlacementStatusCodes.PlacementFailed,
                    $"Host plane for edge {edgeIdx} is not a side face (along-wall drift {alongDrift:F2} ft); " +
                    "aborted rather than misplacing the head.",
                    hostingStrategy: "WallSidewall",
                    ceilingSource: "wall-edge-" + edgeIdx,
                    hostCeilingElementId: wallElemId);
            }

            List<string> errors = new List<string>();

            // Calculate exact in-plane reference direction orthogonal to normal
            XYZ planeNormal = wallPlane.Normal.Normalize();
            XYZ inPlaneRefDir = edgeDir - planeNormal.Multiply(edgeDir.DotProduct(planeNormal));
            if (inPlaneRefDir.GetLength() < 1e-4)
            {
                XYZ fallbackAxis = Math.Abs(planeNormal.DotProduct(XYZ.BasisZ)) < 0.8 ? XYZ.BasisZ : XYZ.BasisY;
                inPlaneRefDir = planeNormal.CrossProduct(fallbackAxis);
            }
            inPlaneRefDir = inPlaneRefDir.Normalize();

            if (faceRef != null)
            {
                FamilyInstance faceInst = null;
                try
                {
                    faceInst = doc.Create.NewFamilyInstance(faceRef, pointOnWall, inPlaneRefDir, context.Symbol);
                    LevelAssociation.EnforceAndVerify(doc, faceInst, context.Level, xyz.Z);

                    if (IsSpatiallySane(faceInst, pointOnWall, maxDeviationFt: 3.5, out string sanityDiag))
                    {
                        return PlacementOutcome.CreatedInstance(
                            faceInst, "WallSidewallFace", ceilingSourceTag, linkName, wallElemId, pointOnWall);
                    }

                    try { doc.Delete(faceInst.Id); } catch { }
                    errors.Add("Face: " + (sanityDiag ?? "outside sanity tolerance"));
                }
                catch (Exception ex)
                {
                    errors.Add("Face: " + ex.Message);
                    if (faceInst != null) { try { doc.Delete(faceInst.Id); } catch { } }
                }
            }

            // Direct Wall Element hosting attempt if wall element is in host document
            if (host?.Wall != null && host.LinkInstance == null)
            {
                FamilyInstance wallElemInst = null;
                try
                {
                    wallElemInst = doc.Create.NewFamilyInstance(
                        pointOnWall, context.Symbol, host.Wall, context.Level, StructuralType.NonStructural);
                    LevelAssociation.EnforceAndVerify(doc, wallElemInst, context.Level, xyz.Z);
                    OrientTowardRoomInterior(doc, wallElemInst, pointOnWall, roomInward2D);

                    if (IsSpatiallySane(wallElemInst, pointOnWall, maxDeviationFt: 3.5, out string sanityDiag))
                    {
                        return PlacementOutcome.CreatedInstance(
                            wallElemInst, "WallSidewallHostElement", ceilingSourceTag, linkName, wallElemId, pointOnWall);
                    }

                    try { doc.Delete(wallElemInst.Id); } catch { }
                    errors.Add("WallElem: " + (sanityDiag ?? "outside sanity tolerance"));
                }
                catch (Exception ex)
                {
                    errors.Add("WallElem: " + ex.Message);
                    if (wallElemInst != null) { try { doc.Delete(wallElemInst.Id); } catch { } }
                }
            }

            try
            {
                FamilyInstance lvlInst = doc.Create.NewFamilyInstance(
                    pointOnWall, context.Symbol, context.Level, StructuralType.NonStructural);

                LevelAssociation.EnforceAndVerify(doc, lvlInst, context.Level, xyz.Z);
                OrientTowardRoomInterior(doc, lvlInst, pointOnWall, roomInward2D);

                if (IsSpatiallySane(lvlInst, pointOnWall, maxDeviationFt: 3.5, out string sanityDiag))
                {
                    return PlacementOutcome.CreatedInstance(
                        lvlInst, "WallSidewallLevelHosted", ceilingSourceTag, linkName, wallElemId, pointOnWall);
                }

                try { doc.Delete(lvlInst.Id); } catch { }
                errors.Add("LevelHosted: " + (sanityDiag ?? "outside sanity tolerance"));
            }
            catch (Exception ex)
            {
                errors.Add("LevelHosted: " + ex.Message);
            }

            try
            {
                Plane plane = Plane.CreateByNormalAndOrigin(wallPlane.Normal, pointOnWall);
                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

                FamilyInstance instance = doc.Create.NewFamilyInstance(
                    sketchPlane.GetPlaneReference(), pointOnWall, inPlaneRefDir, context.Symbol);

                LevelAssociation.EnforceAndVerify(doc, instance, context.Level, xyz.Z);

                if (IsSpatiallySane(instance, pointOnWall, maxDeviationFt: 3.5, out string sanityDiag))
                {
                    return PlacementOutcome.CreatedInstance(
                        instance, "WallSidewallSketchPlane", ceilingSourceTag, linkName, wallElemId, pointOnWall);
                }

                try { doc.Delete(instance.Id); } catch { }
                errors.Add("SketchPlane: " + (sanityDiag ?? "outside sanity tolerance"));
            }
            catch (Exception ex)
            {
                errors.Add("SketchPlane: " + ex.Message);
            }

            string combinedError = errors.Count > 0 ? string.Join(" | ", errors) : "all hosting attempts failed";
            return PlacementOutcome.Fail(
                PlacementStatusCodes.PlacementFailed,
                $"Wall placement failed near edge {edgeIdx} ({combinedError}).",
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

        private static bool IsSpatiallySane(FamilyInstance instance, XYZ requested, double maxDeviationFt, out string diagnostic)
        {
            diagnostic = null;
            if (instance == null || instance.Id == ElementId.InvalidElementId)
            {
                diagnostic = "instance null or invalid";
                return false;
            }

            XYZ p = null;
            LocationPoint lp = instance.Location as LocationPoint;
            if (lp != null && lp.Point != null)
            {
                p = lp.Point;
            }
            else
            {
                BoundingBoxXYZ bbox = instance.get_BoundingBox(null);
                if (bbox != null)
                {
                    p = (bbox.Min + bbox.Max) * 0.5;
                }
            }

            if (p == null)
            {
                // Cannot inspect location; accept instance
                return true;
            }

            // Only reject obvious project-origin (0,0,0) snaps when requested is far from origin
            if (p.GetLength() < 0.25 && requested.GetLength() > 3.0)
            {
                diagnostic = $"origin snap (0,0,0) (req={requested.X:F1},{requested.Y:F1})";
                return false;
            }

            // For face-hosted sidewall instances, Revit attaches them to the physical wall face,
            // which sits at most ~1 ft from the inboard calculation point (the requested point is
            // `standOff` ≈ 0.5 ft off the face, plus wall finish thickness).
            // Anything beyond the strict envelope is a misplacement (end-face snap, wrong wall,
            // project-origin snap) and must be rejected, not accepted.
            double xyDev = Math.Sqrt(Math.Pow(p.X - requested.X, 2) + Math.Pow(p.Y - requested.Y, 2));
            if (xyDev > maxDeviationFt)
            {
                diagnostic = $"dev {xyDev:F2}ft > {maxDeviationFt:F2}ft (actual={p.X:F1},{p.Y:F1} req={requested.X:F1},{requested.Y:F1})";
                return false;
            }
            return true;
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
            public Wall Wall;
            public double Score;
        }

        private static WallFaceHost FindWallFaceHostForEdge(
            Document doc, double[] pA, double[] pB, XYZ edgeDir, XYZ requestedPoint, XYZ roomInward2D)
        {
            XYZ midPt2D = new XYZ((pA[0] + pB[0]) * 0.5, (pA[1] + pB[1]) * 0.5, 0);
            XYZ edgeEndA = new XYZ(pA[0], pA[1], 0);
            XYZ edgeEndB = new XYZ(pB[0], pB[1], 0);

            WallFaceHost bestHost = null;
            double lowestScore = double.MaxValue;

            void Consider(WallFaceHost candidate)
            {
                if (candidate == null) return;
                // Penalize hosts without a face reference heavily (+1000) so a real wall with a usable face
                // in the linked architectural model always beats a faceless host or subgrade wall in the host doc.
                double effectiveScore = candidate.Score + (candidate.FaceReference == null ? 1000.0 : 0.0);
                if (effectiveScore < lowestScore)
                {
                    lowestScore = effectiveScore;
                    bestHost = candidate;
                }
            }

            // 1. Search in host document (tight and broad)
            Consider(FindWallInDoc(doc, null, midPt2D, edgeDir, requestedPoint, roomInward2D,
                1.5, 0.707, edgeEndA, edgeEndB));
            Consider(FindWallInDoc(doc, null, midPt2D, edgeDir, requestedPoint, roomInward2D,
                3.5, 0.5, edgeEndA, edgeEndB));

            // 2. Search in all linked documents (where architectural room-bounding walls reside in MEP models)
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
                XYZ endALink = toLink.OfPoint(edgeEndA);
                XYZ endBLink = toLink.OfPoint(edgeEndB);

                Consider(FindWallInDoc(linkDoc, link, midLink, dirLink, reqLink, inwardLink,
                    1.5, 0.707, endALink, endBLink));
                Consider(FindWallInDoc(linkDoc, link, midLink, dirLink, reqLink, inwardLink,
                    3.5, 0.5, endALink, endBLink));
            }

            return bestHost;
        }

        private static WallFaceHost FindWallInDoc(
    Document doc,
    RevitLinkInstance linkInstance,
    XYZ midPt2D,
    XYZ edgeDir,
    XYZ requestedPoint,
    XYZ roomInward2D,
    double maxDistFt,
    double minDot,
    XYZ edgeEndA,
    XYZ edgeEndB)
        {
            Wall bestWall = null;
            double bestScore = double.MaxValue;

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

                // ---------------------------------------------------------------
                // Composite scoring (replaces raw perpendicular distance).
                //
                // The previous logic picked the wall whose centerline was closest
                // to the edge midpoint. This caused interior partitions that run
                // parallel to the room boundary (and happen to be 0.5 ft closer
                // to the midpoint) to win over the actual room-bounding wall.
                //
                // The new score combines three signals:
                //   1. perp       — perpendicular distance (primary, unchanged)
                //   2. endMatch   — how well the wall's endpoints match the
                //                   polygon edge endpoints. A partition wall's
                //                   endpoints will NOT coincide with the room
                //                   corner, so this penalises it heavily.
                //   3. roomBound  — room-bounding walls are the actual room
                //                   boundary; non-room-bounding walls (casework,
                //                   partitions) get a fixed penalty.
                // ---------------------------------------------------------------
                double edgeLen = Math.Max(edgeEndA.DistanceTo(edgeEndB), 1.0);

                // Each wall endpoint is matched to the nearest polygon edge endpoint.
                // A perfect match gives ~0; a partition offset by 10 ft gives ~10/edgeLen.
                double dA = Math.Min(
                    new XYZ(a.X, a.Y, 0).DistanceTo(edgeEndA),
                    new XYZ(a.X, a.Y, 0).DistanceTo(edgeEndB));
                double dB = Math.Min(
                    new XYZ(b.X, b.Y, 0).DistanceTo(edgeEndA),
                    new XYZ(b.X, b.Y, 0).DistanceTo(edgeEndB));
                double endMismatch = (dA + dB) / edgeLen;

                // Room-bounding preference. Walls that are not room-bounding are
                // interior partitions or casework and should not host perimeter
                // sprinklers. The penalty (5 ft equivalent) is large enough to
                // override a small perpendicular-distance advantage but small
                // enough that a genuinely correct non-bounding wall (e.g., a
                // linked-model wall whose parameter is unset) can still win if
                // its endpoints match perfectly.
                double rbPenalty = 0.0;
                try
                {
                    Parameter rbParam = wall.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                    if (rbParam != null && rbParam.StorageType == StorageType.Integer
                        && rbParam.AsInteger() == 0)
                    {
                        rbPenalty = 5.0;
                    }
                }
                catch { /* parameter may not exist for all wall types; default to no penalty */ }

                // Foundation wall filter: Foundation walls sit below grade (often below room floor)
                // and should never host room perimeter sprinklers when an above-grade wall exists.
                double foundationPenalty = 0.0;
                try
                {
                    if (wall.WallType != null && wall.WallType.Kind == WallKind.Basic)
                    {
                        string wallTypeName = wall.WallType.Name ?? "";
                        if (wallTypeName.IndexOf("Found", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            // Foundation / subgrade walls should never host room-perimeter sidewall
                            // sprinklers. Use a very large penalty (500) so no above-grade wall with
                            // a reasonable perp-distance can be beaten by a foundation wall even when
                            // the foundation wall happens to span the placement Z (e.g. a deep
                            // foundation that extends to 9.84 ft above grade but is not the room wall).
                            foundationPenalty = 500.0;
                        }
                    }
                    // Also check if the wall's top elevation is below the requested placement Z —
                    // this catches short stem walls and subgrade blocks that do NOT carry "Found" in
                    // their type name. Add a separate penalty so both conditions can stack.
                    BoundingBoxXYZ bb = wall.get_BoundingBox(null);
                    if (bb != null && bb.Max.Z < requestedPoint.Z - 0.5)
                    {
                        foundationPenalty += 500.0;
                    }
                }
                catch { }

                double score = perp + endMismatch * 2.0 + rbPenalty + foundationPenalty;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestWall = wall;
                }
            }

            if (bestWall == null) return null;

            WallFaceHost host = BuildHostFromWall(doc, bestWall, linkInstance, requestedPoint, roomInward2D);
            if (host != null)
            {
                host.Score = bestScore;
            }
            return host;
        }

        private static WallFaceHost BuildHostFromWall(
            Document doc,
            Wall wall,
            RevitLinkInstance linkInstance,
            XYZ requestedPointInThisDoc,
            XYZ roomInward2DInThisDoc)
        {
            Transform toHost = linkInstance != null ? linkInstance.GetTotalTransform() : Transform.Identity;

            // Wall direction in this document. Needed to reject END faces: a side face
            // normal is perpendicular to the wall direction, an end face normal is
            // parallel to it. Projecting the requested point onto an end-face plane
            // destroys the along-wall coordinate and stacks every head on the same
            // wall corner (observed on exterior walls whose room-facing side face
            // scored worse than the end face).
            LocationCurve wallCurve = wall.Location as LocationCurve;
            if (wallCurve?.Curve == null) return null;
            XYZ w0 = wallCurve.Curve.GetEndPoint(0);
            XYZ w1 = wallCurve.Curve.GetEndPoint(1);
            XYZ wallDir = new XYZ(w1.X - w0.X, w1.Y - w0.Y, 0);
            if (wallDir.GetLength() < 1e-9) return null;
            wallDir = wallDir.Normalize();

            PlanarFace bestFace = null;
            Reference bestRef = null;
            double bestScore = double.MinValue;

            void ConsiderFace(PlanarFace pf, Reference r)
            {
                if (pf == null || r == null) return;
                XYZ n = pf.FaceNormal;
                if (Math.Abs(n.Z) > 0.3) return;                    // horizontal face
                if (Math.Abs(n.DotProduct(wallDir)) > 0.7) return;  // end face, not a side face

                double towardRoom = n.DotProduct(
                    new XYZ(roomInward2DInThisDoc.X, roomInward2DInThisDoc.Y, 0));

                // Prefer faces pointing toward the room interior, then the face
                // closest to the requested point.
                double score = towardRoom * 100.0;
                try
                {
                    IntersectionResult ir = pf.Project(requestedPointInThisDoc);
                    if (ir != null) score -= Math.Abs(ir.Distance);
                }
                catch { }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestFace = pf;
                    bestRef = r;
                }
            }

            // 1. Core side faces from the host object itself. These can never be end
            //    faces and stay valid even when the geometry scan misses the exposed
            //    face (fully recessed / joined geometry). Evaluate both shells and
            //    keep whichever faces the room interior.
            try
            {
                AddSideFaces(wall, HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior), ConsiderFace);
            }
            catch { }
            try
            {
                AddSideFaces(wall, HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior), ConsiderFace);
            }
            catch { }

            // 2. Geometry scan fallback (finish faces, joined solids); the same
            //    side-face filter is applied inside ConsiderFace.
            Options opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
            GeometryElement geom = null;
            try { geom = wall.get_Geometry(opt); } catch { }

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
                            ConsiderFace(pf, pf.Reference);
                        }
                    }
                }
            }

            // 3. No usable side face at all -> synthesize the plane from the wall
            //    curve so hosting still happens at the correct along-wall position
            //    (this plane is perpendicular to the wall direction by construction).
            if (bestFace == null)
            {
                XYZ nLocal = new XYZ(-wallDir.Y, wallDir.X, 0);
                if (nLocal.DotProduct(roomInward2DInThisDoc) < 0) nLocal = nLocal.Negate();
                XYZ originLocal = new XYZ(
                    (w0.X + w1.X) * 0.5,
                    (w0.Y + w1.Y) * 0.5,
                    requestedPointInThisDoc.Z);

                XYZ nHost = toHost.OfVector(nLocal).Normalize();
                XYZ oHost = toHost.OfPoint(originLocal);
                return new WallFaceHost
                {
                    FaceReference = null,
                    HostPlane = Plane.CreateByNormalAndOrigin(nHost, oHost),
                    LinkInstance = linkInstance,
                    WallElementId = wall.Id.ToString(),
                    Wall = wall
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
                WallElementId = wall.Id.ToString(),
                Wall = wall
            };
        }

        private static void AddSideFaces(Wall wall, IList<Reference> refs, Action<PlanarFace, Reference> consider)
        {
            if (refs == null) return;
            foreach (Reference r in refs)
            {
                if (r == null) continue;
                try
                {
                    GeometryObject go = wall.GetGeometryObjectFromReference(r);
                    consider(go as PlanarFace, r);
                }
                catch { }
            }
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
