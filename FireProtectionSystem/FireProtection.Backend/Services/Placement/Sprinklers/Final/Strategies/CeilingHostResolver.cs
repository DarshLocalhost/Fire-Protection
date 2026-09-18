using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.Strategies
{
    /// <summary>
    /// Result of searching for a ceiling face to host a sprinkler on.
    /// <see cref="HostFace"/> is null when no valid ceiling face is available (no host is ever fabricated).
    /// </summary>
    internal sealed class CeilingHostLookup
    {
        public Reference HostFace;       // null when none found
        public string Source = "none";   // "host" / "link:<name>" / "none"
        public string LinkInstanceName;
        public string CeilingElementId;
    }

    /// <summary>
    /// Dedicated resolver that locates a ceiling/soffit face suitable for hosting a sprinkler.
    /// Searches Ceilings, Floors (slabs), and Roofs in the host document first, then linked models.
    /// </summary>
    internal sealed class CeilingHostResolver
    {
        public CeilingHostLookup FindCeilingHost(Document doc, XYZ point, Level level)
        {
            var lookup = new CeilingHostLookup();
            if (doc == null) return lookup;

            // 1. Host-document ceiling/soffit lookup
            ElementId hostCeilingId;
            Reference hostRef = FindCeilingFaceInDocument(doc, point, level, useLevelFilter: true, ceilingId: out hostCeilingId);
            if (hostRef != null)
            {
                lookup.HostFace = hostRef;
                lookup.Source = "host";
                lookup.CeilingElementId = hostCeilingId?.ToString();
                return lookup;
            }

            // 2. Linked-model ceiling/soffit lookup
            FilteredElementCollector linkCollector = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance));
            foreach (Element element in linkCollector)
            {
                if (!(element is RevitLinkInstance linkInstance)) continue;

                Document linkDoc = linkInstance.GetLinkDocument();
                if (linkDoc == null) continue;

                Transform hostToLink = linkInstance.GetTotalTransform().Inverse;
                XYZ linkPoint = hostToLink.OfPoint(point);

                ElementId linkCeilingId;
                Reference linkFaceRef = FindCeilingFaceInDocument(linkDoc, linkPoint, level, useLevelFilter: false, ceilingId: out linkCeilingId);
                if (linkFaceRef == null) continue;

                Reference hostRefFromLink = linkFaceRef.CreateLinkReference(linkInstance);
                lookup.HostFace = hostRefFromLink;
                lookup.Source = "link:" + (linkInstance.Name ?? linkInstance.Id.ToString());
                lookup.LinkInstanceName = linkInstance.Name ?? linkInstance.Id.ToString();
                lookup.CeilingElementId = linkCeilingId?.ToString();
                return lookup;
            }

            return lookup;
        }

        private static Reference FindCeilingFaceInDocument(Document doc, XYZ point, Level level, bool useLevelFilter, out ElementId ceilingId)
        {
            ceilingId = null;
            if (doc == null) return null;

            // Broaden category search to include Ceilings, Floors (slabs), and Roofs acting as overhead soffits
            List<Element> candidates = new List<Element>();
            candidates.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Ceiling)).ToElements());
            candidates.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Floor)).ToElements());
            candidates.AddRange(new FilteredElementCollector(doc).OfClass(typeof(RoofBase)).ToElements());

            Options geomOptions = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Coarse };

            foreach (Element element in candidates)
            {
                if (useLevelFilter && level != null && element.LevelId != null && element.LevelId != ElementId.InvalidElementId)
                {
                    if (!element.LevelId.Equals(level.Id)) continue;
                }

                BoundingBoxXYZ bb = element.get_BoundingBox(null);
                if (bb == null) continue;

                const double xyTol = 1.0;
                const double zTol = 4.0; // Expanded Z tolerance to catch ceiling soffits

                if (point.X < bb.Min.X - xyTol || point.X > bb.Max.X + xyTol) continue;
                if (point.Y < bb.Min.Y - xyTol || point.Y > bb.Max.Y + xyTol) continue;
                if (point.Z < bb.Min.Z - zTol || point.Z > bb.Max.Z + zTol) continue;

                GeometryElement geom = element.get_Geometry(geomOptions);
                if (geom == null) continue;

                Reference found = FindHostFaceReference(geom, point, zTol);
                if (found != null)
                {
                    ceilingId = element.Id;
                    return found;
                }
            }

            return null;
        }

        private static Reference FindHostFaceReference(GeometryElement geom, XYZ point, double tol)
        {
            Reference bestDownward = null;
            double bestDownwardDist = double.MaxValue;
            Reference bestAny = null;
            double bestAnyDist = double.MaxValue;

            foreach (GeometryObject obj in geom)
            {
                foreach (Solid solid in ExtractSolids(obj))
                {
                    if (solid == null || solid.Faces.Size == 0) continue;

                    foreach (Face face in solid.Faces)
                    {
                        PlanarFace planar = face as PlanarFace;
                        if (planar == null) continue;

                        IntersectionResult ir = planar.Project(point);
                        if (ir == null || double.IsNaN(ir.Distance)) continue;
                        double d = Math.Abs(ir.Distance);
                        if (d > tol) continue;

                        if (planar.FaceNormal.Z < -0.5) // downward-facing soffit face
                        {
                            if (d < bestDownwardDist)
                            {
                                bestDownwardDist = d;
                                bestDownward = planar.Reference;
                            }
                        }
                        if (d < bestAnyDist)
                        {
                            bestAnyDist = d;
                            bestAny = planar.Reference;
                        }
                    }
                }
            }

            return bestDownward ?? bestAny;
        }

        private static IEnumerable<Solid> ExtractSolids(GeometryObject obj)
        {
            var solids = new List<Solid>();

            if (obj is Solid solid && solid.Volume > 0)
            {
                solids.Add(solid);
            }
            else if (obj is GeometryInstance instance)
            {
                GeometryElement instanceGeom = instance.GetInstanceGeometry();
                if (instanceGeom != null)
                {
                    foreach (GeometryObject child in instanceGeom)
                    {
                        solids.AddRange(ExtractSolids(child));
                    }
                }
            }

            return solids;
        }
    }
}