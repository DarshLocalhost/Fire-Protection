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
    /// Dedicated resolver (master prompt §21, Phase 4) that locates a ceiling face suitable for hosting a
    /// sprinkler. Searches the host document first, then linked models (ceilings commonly live in the
    /// architectural link). For a linked ceiling the face reference is converted into a host-document
    /// reference via <see cref="Reference.CreateLinkReference"/>.
    ///
    /// Coordinate discipline (hard rules 10, 11): the placement point supplied by the caller is in HOST
    /// coordinates and is returned unchanged; only the internal SEARCH point is transformed into link space
    /// (exactly once) to locate link geometry.
    /// </summary>
    internal sealed class CeilingHostResolver
    {
        /// <summary>
        /// Find a ceiling face whose footprint contains <paramref name="point"/> (host coords). Returns a
        /// lookup whose <see cref="CeilingHostLookup.HostFace"/> is null when none is available.
        /// </summary>
        public CeilingHostLookup FindCeilingHost(Document doc, XYZ point, Level level)
        {
            var lookup = new CeilingHostLookup();
            if (doc == null) return lookup;

            // 1. Host-document ceilings (level-aware).
            ElementId hostCeilingId;
            Reference hostRef = FindCeilingFaceInDocument(doc, point, level, useLevelFilter: true, ceilingId: out hostCeilingId);
            if (hostRef != null)
            {
                lookup.HostFace = hostRef;
                lookup.Source = "host";
                lookup.CeilingElementId = hostCeilingId?.ToString();
                return lookup;
            }

            // 2. Linked-model ceilings.
            FilteredElementCollector linkCollector = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance));
            foreach (Element element in linkCollector)
            {
                if (!(element is RevitLinkInstance linkInstance)) continue;

                Document linkDoc = linkInstance.GetLinkDocument();
                if (linkDoc == null) continue;

                // Link geometry lives in link coordinate space; map the host-space point into it (once).
                Transform hostToLink = linkInstance.GetTotalTransform().Inverse;
                XYZ linkPoint = hostToLink.OfPoint(point);

                ElementId linkCeilingId;
                Reference linkFaceRef = FindCeilingFaceInDocument(linkDoc, linkPoint, level, useLevelFilter: false, ceilingId: out linkCeilingId);
                if (linkFaceRef == null) continue;

                // Convert the linked-document face reference into a host-document reference.
                Reference hostRefFromLink = linkFaceRef.CreateLinkReference(linkInstance);
                lookup.HostFace = hostRefFromLink;
                lookup.Source = "link:" + (linkInstance.Name ?? linkInstance.Id.ToString());
                lookup.LinkInstanceName = linkInstance.Name ?? linkInstance.Id.ToString();
                lookup.CeilingElementId = linkCeilingId?.ToString();
                return lookup;
            }

            return lookup;
        }

        /// <summary>
        /// Find a ceiling face reference in a specific document near the supplied point. When
        /// <paramref name="useLevelFilter"/> is true the ceiling must belong to the supplied host Level;
        /// for linked documents this is false because <c>ceiling.LevelId</c> references the link document,
        /// so correctness is enforced by the 3D face-proximity check instead.
        /// </summary>
        private static Reference FindCeilingFaceInDocument(Document doc, XYZ point, Level level, bool useLevelFilter, out ElementId ceilingId)
        {
            ceilingId = null;
            if (doc == null) return null;

            FilteredElementCollector collector = new FilteredElementCollector(doc).OfClass(typeof(Ceiling));
            Options geomOptions = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Coarse };

            foreach (Element element in collector)
            {
                if (!(element is Ceiling ceiling)) continue;

                if (useLevelFilter && level != null && ceiling.LevelId != null && ceiling.LevelId != ElementId.InvalidElementId)
                {
                    if (!ceiling.LevelId.Equals(level.Id)) continue;
                }

                BoundingBoxXYZ bb = ceiling.get_BoundingBox(null);
                if (bb == null) continue;

                const double tol = 0.5;
                if (point.X < bb.Min.X - tol || point.X > bb.Max.X + tol) continue;
                if (point.Y < bb.Min.Y - tol || point.Y > bb.Max.Y + tol) continue;
                // Z-range pre-filter: skip ceilings whose vertical extent doesn't
                // overlap the search point. For linked ceilings (useLevelFilter=false)
                // this avoids expensive geometry iteration on ceilings at other levels.
                if (point.Z < bb.Min.Z - tol || point.Z > bb.Max.Z + tol) continue;

                GeometryElement geom = ceiling.get_Geometry(geomOptions);
                if (geom == null) continue;

                Reference found = FindHostFaceReference(geom, point, tol);
                if (found != null)
                {
                    ceilingId = ceiling.Id;
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// A pendent sprinkler is hosted on the UNDERSIDE of a ceiling — the face whose normal points DOWN
        /// (Z &lt; 0). Prefer the closest downward-facing planar face; fall back to the closest planar face so
        /// placement is not needlessly rejected when face orientation is ambiguous.
        /// </summary>
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

                        if (planar.FaceNormal.Z < -0.5) // downward-facing (pendant host)
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
