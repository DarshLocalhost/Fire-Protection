using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final.Strategies
{
    internal sealed class CeilingHostLookup
    {
        public Reference HostFace;
        public string Source = "none";
        public string LinkInstanceName;
        public string CeilingElementId;
        public XYZ ProjectedPointOnFace;
    }

    internal sealed class CeilingHostResolver
    {
        /// <summary>
        /// Per-pass cache of the candidate host elements, keyed by document.
        ///
        /// WHY THIS EXISTS
        /// ---------------
        /// <see cref="FindCeilingHost"/> is called once per CANDIDATE POINT during an eligibility
        /// preflight, and the candidate count is rooms x grid points (hundreds x tens = thousands).
        /// Before this cache, EVERY such call rebuilt the same lists from scratch:
        ///
        ///   FindCeilingFaceInDocument : 3 document-wide collectors, each .ToElements()
        ///   the link sweep             : 1 more collector, then 3 collectors PER LINKED MODEL
        ///
        /// That is (4 + 3L) whole-document sweeps per candidate point - on the same thread as the
        /// WPF dispatcher, so the whole tool (and Revit) froze. The ceiling/floor/roof element set is
        /// identical for every candidate within one pass; only the per-element bounding-box and face
        /// test differ. Hoisting the collection out of the loop removes that entire class of cost.
        ///
        /// LIFETIME AND SAFETY
        /// -------------------
        /// Callers bracket one read-only pass with <see cref="BeginPass"/> / <see cref="EndPass"/>.
        /// Within a pass the document is only READ, so cached Element references stay valid; nothing
        /// is created, modified or deleted, which is the only thing that would invalidate them.
        /// Because the cache never survives a pass, a document modification (placing instances,
        /// loading a family) cannot be served stale elements - which is precisely why this is not a
        /// longer-lived cache.
        ///
        /// Geometry is NOT cached: <c>element.get_Geometry</c> results are still computed per
        /// candidate. Only the cheap bounding-box reject (the first test each candidate hits) can be
        /// reused, and materialising geometry would be a far larger memory risk than the collector
        /// cost it would replace.
        /// </summary>
        private readonly Dictionary<Document, List<Element>> _hostCandidatesByDoc =
            new Dictionary<Document, List<Element>>();

        private readonly Dictionary<Document, List<RevitLinkInstance>> _linksByDoc =
            new Dictionary<Document, List<RevitLinkInstance>>();

        /// <summary>Number of document sweeps avoided this session, for diagnostics.</summary>
        private int _collectorCallsSaved;

        /// <summary>
        /// Marks the start of a read-only pass. Any previously cached elements are dropped first so
        /// nothing can survive across a document modification.
        /// </summary>
        public void BeginPass()
        {
            _hostCandidatesByDoc.Clear();
            _linksByDoc.Clear();
        }

        /// <summary>Releases cached elements at the end of a pass.</summary>
        public void EndPass()
        {
            _hostCandidatesByDoc.Clear();
            _linksByDoc.Clear();
        }

        /// <summary>Total document-wide collector invocations avoided by the cache.</summary>
        public int CollectorCallsSaved { get { return _collectorCallsSaved; } }

        /// <summary>
        /// The Ceiling / Floor / RoofBase elements in a document, collected ONCE per pass.
        /// Returns an empty list rather than throwing if the collector fails, so a broken category
        /// degrades to "no host found" exactly as the uncached code did.
        /// </summary>
        private List<Element> GetHostCandidates(Document doc)
        {
            List<Element> cached;
            if (_hostCandidatesByDoc.TryGetValue(doc, out cached))
            {
                // A hit stands in for the three collectors the uncached path would have run.
                _collectorCallsSaved += 3;
                return cached;
            }

            var candidates = new List<Element>();
            try
            {
                candidates.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Ceiling)).ToElements());
                candidates.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Floor)).ToElements());
                candidates.AddRange(new FilteredElementCollector(doc).OfClass(typeof(RoofBase)).ToElements());
            }
            catch (Exception ex)
            {
                // Same tolerance as before: an unreadable category means "no host here", and the
                // caller already treats a missing host as best-effort rather than an error.
                FireProtection.UI.Services.FireProtectionLog.Warn(
                    "Ceiling/floor/roof host enumeration failed: " + ex.Message);
            }

            _hostCandidatesByDoc[doc] = candidates;
            return candidates;
        }

        /// <summary>The RevitLinkInstance elements in a document, collected ONCE per pass.</summary>
        private List<RevitLinkInstance> GetLinkInstances(Document doc)
        {
            List<RevitLinkInstance> cached;
            if (_linksByDoc.TryGetValue(doc, out cached))
            {
                // A hit stands in for the one link collector the uncached path would have run.
                _collectorCallsSaved += 1;
                return cached;
            }

            var links = new List<RevitLinkInstance>();
            try
            {
                FilteredElementCollector linkCollector = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance));
                foreach (Element element in linkCollector)
                {
                    RevitLinkInstance linkInstance = element as RevitLinkInstance;
                    if (linkInstance != null) links.Add(linkInstance);
                }
            }
            catch (Exception ex)
            {
                FireProtection.UI.Services.FireProtectionLog.Warn(
                    "Link instance enumeration failed: " + ex.Message);
            }

            _linksByDoc[doc] = links;
            return links;
        }

        public CeilingHostLookup FindCeilingHost(Document doc, XYZ point, Level level)
        {
            var lookup = new CeilingHostLookup();
            if (doc == null) return lookup;

            ElementId hostCeilingId;
            XYZ hostProjectedPoint;
            Reference hostRef = FindCeilingFaceInDocument(doc, point, level, useLevelFilter: true, ceilingId: out hostCeilingId, projectedOnFace: out hostProjectedPoint);
            if (hostRef != null)
            {
                lookup.HostFace = hostRef;
                lookup.ProjectedPointOnFace = hostProjectedPoint;
                lookup.Source = "host";
                lookup.CeilingElementId = hostCeilingId?.ToString();
                return lookup;
            }

            foreach (RevitLinkInstance linkInstance in GetLinkInstances(doc))
            {
                Document linkDoc = linkInstance.GetLinkDocument();
                if (linkDoc == null) continue;

                Transform hostToLink = linkInstance.GetTotalTransform().Inverse;
                XYZ linkPoint = hostToLink.OfPoint(point);

                ElementId linkCeilingId;
                XYZ linkProjectedPoint;
                Reference linkFaceRef = FindCeilingFaceInDocument(linkDoc, linkPoint, level, useLevelFilter: false, ceilingId: out linkCeilingId, projectedOnFace: out linkProjectedPoint);
                if (linkFaceRef == null) continue;

                Reference hostRefFromLink = linkFaceRef.CreateLinkReference(linkInstance);
                XYZ hostProjectedPointFromLink = linkInstance.GetTotalTransform().OfPoint(linkProjectedPoint);

                lookup.HostFace = hostRefFromLink;
                lookup.ProjectedPointOnFace = hostProjectedPointFromLink;
                lookup.Source = "link:" + (linkInstance.Name ?? linkInstance.Id.ToString());
                lookup.LinkInstanceName = linkInstance.Name ?? linkInstance.Id.ToString();
                lookup.CeilingElementId = linkCeilingId?.ToString();
                return lookup;
            }

            return lookup;
        }

        private Reference FindCeilingFaceInDocument(Document doc, XYZ point, Level level, bool useLevelFilter, out ElementId ceilingId, out XYZ projectedOnFace)
        {
            ceilingId = null;
            projectedOnFace = null;
            if (doc == null) return null;

            // Host candidates come from the per-pass cache, NOT from a fresh pair of collectors per
            // candidate point. This is the single most expensive line in the whole eligibility path.
            List<Element> candidates = GetHostCandidates(doc);

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
                const double zTol = 4.0;

                if (point.X < bb.Min.X - xyTol || point.X > bb.Max.X + xyTol) continue;
                if (point.Y < bb.Min.Y - xyTol || point.Y > bb.Max.Y + xyTol) continue;
                if (point.Z < bb.Min.Z - zTol || point.Z > bb.Max.Z + zTol) continue;

                GeometryElement geom = element.get_Geometry(geomOptions);
                if (geom == null) continue;

                XYZ tempProj;
                Reference found = FindHostFaceReference(geom, point, zTol, out tempProj);
                if (found != null)
                {
                    ceilingId = element.Id;
                    projectedOnFace = tempProj;
                    return found;
                }
            }

            return null;
        }

        private static Reference FindHostFaceReference(GeometryElement geom, XYZ point, double tol, out XYZ projectedOnFace)
        {
            projectedOnFace = null;
            Reference bestDownward = null;
            double lowestZ = double.MaxValue; // CRITICAL: Guarantee we get the absolute lowest face

            Reference bestAny = null;
            double bestAnyDist = double.MaxValue;
            XYZ bestAnyProj = null;

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

                        // Only consider faces pointing down, and save the one with the lowest Z elevation
                        if (planar.FaceNormal.Z < -0.5)
                        {
                            if (ir.XYZPoint.Z < lowestZ)
                            {
                                lowestZ = ir.XYZPoint.Z;
                                bestDownward = planar.Reference;
                                projectedOnFace = ir.XYZPoint;
                            }
                        }
                        else if (d < bestAnyDist)
                        {
                            bestAnyDist = d;
                            bestAny = planar.Reference;
                            bestAnyProj = ir.XYZPoint;
                        }
                    }
                }
            }

            if (bestDownward != null)
            {
                return bestDownward;
            }

            projectedOnFace = bestAnyProj;
            return bestAny;
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