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
                        if (bbox == null) continue;

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
    }
}
