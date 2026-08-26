using Autodesk.Revit.DB;
using FireProtection.Backend.Models.DTOs;
using System;
using System.Collections.Generic;

namespace FireProtection.Backend.Services.Model
{
    public class ObstacleExtractor
    {
        private static readonly BuiltInCategory[] ObstacleCategories = new BuiltInCategory[]
        {
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_Columns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_CableTray
        };

        public List<ObstacleData> ExtractObstacles(RevitModelContext context, List<ExtractionIssue> issues)
        {
            List<ObstacleData> obstacles = new List<ObstacleData>();

            // 1. Host obstacles
            ExtractFromDocument(
                context.HostDocument,
                Transform.Identity,
                new SourceReferenceData
                {
                    DocumentTitle = context.HostDocument.Title,
                    DocumentPath = context.HostDocument.PathName ?? string.Empty,
                    IsFromLink = false
                },
                obstacles,
                issues);

            // 2. Linked obstacles (structural columns, architectural columns, framing)
            foreach (RevitLinkContext link in context.LoadedLinks)
            {
                if (link.LinkedDocument == null) continue;

#if REVIT_2024 || REVIT_2025 || REVIT_2026
                string linkInstanceId = link.InstanceId.Value.ToString();
#else
                string linkInstanceId = link.InstanceId.ToString();
#endif

                ExtractFromDocument(
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
                    obstacles,
                    issues);
            }

            return obstacles;
        }

        private void ExtractFromDocument(
            Document document,
            Transform transform,
            SourceReferenceData source,
            List<ObstacleData> obstacles,
            List<ExtractionIssue> issues)
        {
            foreach (BuiltInCategory cat in ObstacleCategories)
            {
                try
                {
                    FilteredElementCollector collector = new FilteredElementCollector(document)
                        .OfCategory(cat)
                        .WhereElementIsNotElementType();

                    foreach (Element element in collector)
                    {
                        if (element == null) continue;

                    BoundingBoxXYZ bbox = element.get_BoundingBox(null);
                    if (bbox == null)
                    {
                        // Fallback: derive a bounding box from the element's solid geometry
                        // so valid obstacles are never silently discarded.
                        bbox = GetGeometryBoundingBox(element);
                    }
                    if (bbox == null)
                    {
                        issues.Add(new ExtractionIssue(
                            ExtractionIssueSeverity.Info,
                            "ObstacleExtraction",
                            $"Obstacle '{element.Id}' ({element.Name}) has no resolvable bounding box and was skipped.",
                            element.Id.ToString(),
                            element.Name));
                        continue;
                    }

#if REVIT_2024 || REVIT_2025 || REVIT_2026
                        string elementId = element.Id.Value.ToString();
#else
                        string elementId = element.Id.ToString();
#endif
                        BoundingBox3DData hostBBox = RevitModelContext.TransformBoundingBox(bbox, transform);

                        // Compute center point & dimensions
                        Point3DData min = hostBBox.Min;
                        Point3DData max = hostBBox.Max;
                        Point3DData center = new Point3DData((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0, (min.Z + max.Z) / 2.0);
                        Point3DData dims = new Point3DData(Math.Abs(max.X - min.X), Math.Abs(max.Y - min.Y), Math.Abs(max.Z - min.Z));

                        string levelIdStr = string.Empty;
                        if (element.LevelId != null && element.LevelId != ElementId.InvalidElementId)
                        {
#if REVIT_2024 || REVIT_2025 || REVIT_2026
                            levelIdStr = element.LevelId.Value.ToString();
#else
                            levelIdStr = element.LevelId.ToString();
#endif
                        }

                        ObstacleData obstacle = new ObstacleData
                        {
                            ElementId = elementId,
                            Name = element.Name,
                            Category = cat.ToString(),
                            StructuralType = element.Category?.Name ?? cat.ToString(),
                            LevelId = levelIdStr,
                            Source = new SourceReferenceData
                            {
                                DocumentTitle = source.DocumentTitle,
                                DocumentPath = source.DocumentPath,
                                IsFromLink = source.IsFromLink,
                                LinkInstanceId = source.LinkInstanceId,
                                LinkName = source.LinkName
                            },
                            BoundingBox = hostBBox,
                            CenterPoint = center,
                            DimensionsFt = dims
                        };

                        obstacles.Add(obstacle);
                    }
                }
                catch (Exception ex)
                {
                    issues.Add(new ExtractionIssue(
                        ExtractionIssueSeverity.Info,
                        "ObstacleExtraction",
                        $"Category '{cat}' obstacle extraction notice in '{source.DocumentTitle}': {ex.Message}"));
                }
            }
        }

        /// <summary>
        /// Builds a bounding box from the element's solid geometry when the direct
        /// bounding-box call returns null. Returns null if no usable geometry is found.
        /// </summary>
        private static BoundingBoxXYZ GetGeometryBoundingBox(Element element)
        {
            if (element == null) return null;

            try
            {
                Options options = new Options
                {
                    DetailLevel = ViewDetailLevel.Coarse,
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = true
                };

                GeometryElement geom = element.get_Geometry(options);
                if (geom == null) return null;

                BoundingBoxXYZ result = null;
                foreach (GeometryObject obj in geom)
                {
                    BoundingBoxXYZ candidate = null;
                    if (obj is Solid solid && solid.Volume > 1e-9)
                    {
                        candidate = solid.GetBoundingBox();
                    }
                    else if (obj is GeometryInstance inst)
                    {
                        GeometryElement instGeom = inst.GetInstanceGeometry();
                        if (instGeom != null)
                        {
                            foreach (GeometryObject inner in instGeom)
                            {
                                if (inner is Solid s && s.Volume > 1e-9)
                                {
                                    candidate = s.GetBoundingBox();
                                    if (candidate != null) break;
                                }
                            }
                        }
                    }

                    if (candidate == null) continue;

                    if (result == null)
                    {
                        result = candidate;
                    }
                    else
                    {
                        // Expand result to encompass candidate
                        XYZ min = new XYZ(
                            Math.Min(result.Min.X, candidate.Min.X),
                            Math.Min(result.Min.Y, candidate.Min.Y),
                            Math.Min(result.Min.Z, candidate.Min.Z));
                        XYZ max = new XYZ(
                            Math.Max(result.Max.X, candidate.Max.X),
                            Math.Max(result.Max.Y, candidate.Max.Y),
                            Math.Max(result.Max.Z, candidate.Max.Z));
                        result = new BoundingBoxXYZ
                        {
                            Min = min,
                            Max = max
                        };
                    }
                }

                return result;
            }
            catch
            {
                return null;
            }
        }
    }
}
