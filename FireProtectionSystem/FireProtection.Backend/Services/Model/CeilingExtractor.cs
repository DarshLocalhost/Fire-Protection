using Autodesk.Revit.DB;
using FireProtection.Backend.Models.DTOs;
using System;
using System.Collections.Generic;

namespace FireProtection.Backend.Services.Model
{
    public class CeilingExtractor
    {
        public class ExtractedCeilingItem
        {
            public Ceiling Ceiling { get; set; }
            public Document Document { get; set; }
            public Transform Transform { get; set; }
            public SourceReferenceData Source { get; set; }
            public CeilingData Dto { get; set; }
            public BoundingBoxXYZ LocalBoundingBox { get; set; }
            public BoundingBox3DData HostBoundingBox { get; set; }
            public List<Solid> Solids { get; set; }
            public List<PlanarFace> BottomFaces { get; set; }
            public double? BottomElevationFt { get; set; }
            public double? TopElevationFt { get; set; }
            public string SlopeType { get; set; }
            public double? SlopeDegrees { get; set; }
        }

        /// <summary>
        /// Collects and processes all ceiling elements from the host model and all loaded linked models.
        /// </summary>
        public List<ExtractedCeilingItem> CollectAllCeilings(RevitModelContext context, List<ExtractionIssue> issues)
        {
            List<ExtractedCeilingItem> ceilingItems = new List<ExtractedCeilingItem>();

            // 1. Host ceilings
            CollectFromDocument(
                context.HostDocument,
                Transform.Identity,
                new SourceReferenceData
                {
                    DocumentTitle = context.HostDocument.Title,
                    DocumentPath = context.HostDocument.PathName ?? string.Empty,
                    IsFromLink = false,
                    LinkInstanceId = string.Empty,
                    LinkName = string.Empty
                },
                ceilingItems,
                issues);

            // 2. Linked ceilings
            foreach (RevitLinkContext link in context.LoadedLinks)
            {
                if (link.LinkedDocument == null) continue;

#if REVIT_2024 || REVIT_2025 || REVIT_2026
                string linkInstanceId = link.InstanceId.Value.ToString();
#else
                string linkInstanceId = link.InstanceId.ToString();
#endif

                CollectFromDocument(
                    link.LinkedDocument,
                    link.TotalTransform ?? link.Transform,
                    new SourceReferenceData
                    {
                        DocumentTitle = link.DocumentTitle,
                        DocumentPath = link.DocumentPath,
                        IsFromLink = true,
                        LinkInstanceId = linkInstanceId,
                        LinkName = link.LinkName
                    },
                    ceilingItems,
                    issues);
            }

            return ceilingItems;
        }

        private void CollectFromDocument(
            Document document,
            Transform transform,
            SourceReferenceData source,
            List<ExtractedCeilingItem> ceilingItems,
            List<ExtractionIssue> issues)
        {
            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_Ceilings)
                    .WhereElementIsNotElementType();

                foreach (Element element in collector)
                {
                    if (element is Ceiling ceiling)
                    {
                        ExtractedCeilingItem item = ProcessCeiling(ceiling, document, transform, source, issues);
                        if (item != null)
                        {
                            ceilingItems.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add(new ExtractionIssue(
                    ExtractionIssueSeverity.Warning,
                    "CeilingExtraction",
                    $"Error collecting ceilings from document '{source.DocumentTitle}': {ex.Message}"));
            }
        }

        private ExtractedCeilingItem ProcessCeiling(
            Ceiling ceiling,
            Document document,
            Transform transform,
            SourceReferenceData source,
            List<ExtractionIssue> issues)
        {
#if REVIT_2024 || REVIT_2025 || REVIT_2026
            string elementId = ceiling.Id.Value.ToString();
#else
            string elementId = ceiling.Id.ToString();
#endif

            BoundingBoxXYZ localBBox = ceiling.get_BoundingBox(null);
            BoundingBox3DData hostBBox = localBBox != null
                ? RevitModelContext.TransformBoundingBox(localBBox, transform)
                : null;

            // Geometry extraction with recursive instance inspection
            List<Solid> solids = new List<Solid>();
            ExtractSolids(ceiling, solids);

            double minBottomZ = double.MaxValue;
            double maxTopZ = double.MinValue;
            List<PlanarFace> bottomFaces = new List<PlanarFace>();
            bool hasSlopedFace = false;
            double maxSlopeDeg = 0.0;

            foreach (Solid solid in solids)
            {
                if (solid == null || solid.Volume <= 1e-9) continue;

                foreach (Face face in solid.Faces)
                {
                    if (face is PlanarFace planarFace)
                    {
                        XYZ normal = planarFace.FaceNormal;
                        // Transform normal vector
                        XYZ hostNormal = RevitModelContext.TransformVector(normal, transform);

                        // If normal points downward (Z < -0.5), it's a bottom ceiling surface
                        if (hostNormal.Z < -0.1)
                        {
                            bottomFaces.Add(planarFace);

                            // Calculate slope in degrees relative to vertical Z-axis
                            double angleFromDown = hostNormal.AngleTo(new XYZ(0, 0, -1));
                            double angleDeg = angleFromDown * (180.0 / Math.PI);
                            if (angleDeg > 1.0)
                            {
                                hasSlopedFace = true;
                                if (angleDeg > maxSlopeDeg) maxSlopeDeg = angleDeg;
                            }
                        }
                    }

                    // Also evaluate bbox of face in host coords
                    BoundingBoxUV uvBox = face.GetBoundingBox();
                    XYZ faceMin = face.Evaluate(uvBox.Min);
                    XYZ faceMax = face.Evaluate(uvBox.Max);
                    XYZ hMin = RevitModelContext.TransformPoint(faceMin, transform);
                    XYZ hMax = RevitModelContext.TransformPoint(faceMax, transform);

                    if (hMin.Z < minBottomZ) minBottomZ = hMin.Z;
                    if (hMax.Z > maxTopZ) maxTopZ = hMax.Z;
                }
            }

            // Fallback to bounding box if solid inspection found no faces
            if (minBottomZ == double.MaxValue && hostBBox != null)
            {
                minBottomZ = hostBBox.Min.Z;
                maxTopZ = hostBBox.Max.Z;
            }

            string slopeType = "FLAT";
            if (hasSlopedFace)
            {
                slopeType = "SLOPED";
            }
            else if (hostBBox != null && Math.Abs(hostBBox.Max.Z - hostBBox.Min.Z) > 0.5)
            {
                slopeType = "STEPPED";
            }

            // Get ceiling type name
            string typeName = string.Empty;
            string familyName = string.Empty;
            ElementType elemType = document.GetElement(ceiling.GetTypeId()) as ElementType;
            if (elemType != null)
            {
                typeName = elemType.Name;
                familyName = elemType.FamilyName;
            }

            CeilingData dto = new CeilingData
            {
                ElementId = elementId,
                CeilingName = ceiling.Name,
                FamilyName = familyName,
                TypeName = typeName,
                Category = "OST_Ceilings",
                Source = new SourceReferenceData
                {
                    DocumentTitle = source.DocumentTitle,
                    DocumentPath = source.DocumentPath,
                    IsFromLink = source.IsFromLink,
                    LinkInstanceId = source.LinkInstanceId,
                    LinkName = source.LinkName
                },
                BoundingBox = hostBBox,
                BottomElevationFt = minBottomZ != double.MaxValue ? (double?)minBottomZ : null,
                TopElevationFt = maxTopZ != double.MinValue ? (double?)maxTopZ : null,
                SlopeType = slopeType,
                SlopeDegrees = hasSlopedFace ? (double?)maxSlopeDeg : 0.0,
                IsRoomDirectCeiling = false
            };

            return new ExtractedCeilingItem
            {
                Ceiling = ceiling,
                Document = document,
                Transform = transform,
                Source = source,
                Dto = dto,
                LocalBoundingBox = localBBox,
                HostBoundingBox = hostBBox,
                Solids = solids,
                BottomFaces = bottomFaces,
                BottomElevationFt = minBottomZ != double.MaxValue ? (double?)minBottomZ : null,
                TopElevationFt = maxTopZ != double.MinValue ? (double?)maxTopZ : null,
                SlopeType = slopeType,
                SlopeDegrees = hasSlopedFace ? (double?)maxSlopeDeg : 0.0
            };
        }

        private static void ExtractSolids(Element element, List<Solid> solids)
        {
            Options options = new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                ComputeReferences = false,
                IncludeNonVisibleObjects = true
            };

            GeometryElement geomElem = element.get_Geometry(options);
            if (geomElem == null) return;

            ExtractSolidsRecursive(geomElem, solids);
        }

        private static void ExtractSolidsRecursive(GeometryElement geomElem, List<Solid> solids)
        {
            foreach (GeometryObject obj in geomElem)
            {
                if (obj is Solid solid && solid.Volume > 1e-9)
                {
                    solids.Add(solid);
                }
                else if (obj is GeometryInstance inst)
                {
                    GeometryElement instGeom = inst.GetInstanceGeometry();
                    if (instGeom != null)
                    {
                        ExtractSolidsRecursive(instGeom, solids);
                    }
                }
            }
        }
    }
}
