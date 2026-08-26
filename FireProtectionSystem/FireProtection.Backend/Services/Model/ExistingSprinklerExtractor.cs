using Autodesk.Revit.DB;
using FireProtection.Backend.Models.DTOs;
using System;
using System.Collections.Generic;

namespace FireProtection.Backend.Services.Model
{
    public class ExistingSprinklerExtractor
    {
        public List<ExistingSprinklerData> ExtractExistingSprinklers(
            RevitModelContext context,
            List<LevelData> levels,
            List<ExtractionIssue> issues)
        {
            List<ExistingSprinklerData> sprinklers = new List<ExistingSprinklerData>();

            // 1. Host model
            ExtractFromDocument(
                context.HostDocument,
                Transform.Identity,
                new SourceReferenceData
                {
                    DocumentTitle = context.HostDocument.Title ?? string.Empty,
                    DocumentPath = context.HostDocument.PathName ?? string.Empty,
                    IsFromLink = false,
                    LinkInstanceId = string.Empty,
                    LinkName = string.Empty
                },
                sprinklers,
                issues);

            // 2. Loaded linked models (sprinklers may be hosted in architectural/MEP links)
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
                    sprinklers,
                    issues);
            }

            return sprinklers;
        }

        private void ExtractFromDocument(
            Document document,
            Transform transform,
            SourceReferenceData source,
            List<ExistingSprinklerData> sprinklers,
            List<ExtractionIssue> issues)
        {
            if (document == null) return;

            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_Sprinklers)
                    .WhereElementIsNotElementType();

                foreach (Element element in collector)
                {
                    if (!(element is FamilyInstance instance)) continue;

#if REVIT_2024 || REVIT_2025 || REVIT_2026
                    string elementId = instance.Id.Value.ToString();
#else
                    string elementId = instance.Id.ToString();
#endif

                    // Location (transform linked coordinates into canonical host space)
                    XYZ point = null;
                    LocationPoint locPoint = instance.Location as LocationPoint;
                    if (locPoint != null && locPoint.Point != null)
                    {
                        point = RevitModelContext.TransformPoint(locPoint.Point, transform);
                    }
                    else
                    {
                        BoundingBoxXYZ bbox = instance.get_BoundingBox(null);
                        if (bbox != null)
                        {
                            XYZ mid = new XYZ(
                                (bbox.Min.X + bbox.Max.X) / 2.0,
                                (bbox.Min.Y + bbox.Max.Y) / 2.0,
                                (bbox.Min.Z + bbox.Max.Z) / 2.0);
                            point = RevitModelContext.TransformPoint(mid, transform);
                        }
                    }

                    if (point == null) continue;

                    // Family and Type
                    string familyName = instance.Symbol?.Family?.Name ?? instance.Name;
                    string typeName = instance.Symbol?.Name ?? string.Empty;

                    // Level
                    string levelIdStr = string.Empty;
                    string levelName = string.Empty;
                    double levelElev = 0.0;

                    if (instance.LevelId != null && instance.LevelId != ElementId.InvalidElementId)
                    {
#if REVIT_2024 || REVIT_2025 || REVIT_2026
                        levelIdStr = instance.LevelId.Value.ToString();
#else
                        levelIdStr = instance.LevelId.ToString();
#endif
                        Level lvl = document.GetElement(instance.LevelId) as Level;
                        if (lvl != null)
                        {
                            levelName = lvl.Name;
                            levelElev = lvl.Elevation;
                        }
                    }

                    // Host
                    string hostIdStr = string.Empty;
                    string hostName = string.Empty;
                    if (instance.Host != null)
                    {
#if REVIT_2024 || REVIT_2025 || REVIT_2026
                        hostIdStr = instance.Host.Id.Value.ToString();
#else
                        hostIdStr = instance.Host.Id.ToString();
#endif
                        hostName = instance.Host.Name;
                    }

                    // Orientation (Facing / Hand orientation)
                    XYZ orientation = instance.FacingOrientation ?? new XYZ(0, 0, -1);

                    // Mounting classification by family/type name or orientation
                    string mountingType = ClassifyMounting(familyName, typeName, orientation);

                    BoundingBoxXYZ bBox = instance.get_BoundingBox(null);
                    BoundingBox3DData hostBBox = bBox != null
                        ? RevitModelContext.TransformBoundingBox(bBox, transform)
                        : null;

                    ExistingSprinklerData data = new ExistingSprinklerData
                    {
                        ElementId = elementId,
                        FamilyName = familyName,
                        TypeName = typeName,
                        Location = new Point3DData(point.X, point.Y, point.Z),
                        LevelId = levelIdStr,
                        LevelName = levelName,
                        LevelElevationFt = levelElev,
                        HostElementId = hostIdStr,
                        HostName = hostName,
                        Source = new SourceReferenceData
                        {
                            DocumentTitle = source.DocumentTitle ?? string.Empty,
                            DocumentPath = source.DocumentPath ?? string.Empty,
                            IsFromLink = source.IsFromLink,
                            LinkInstanceId = source.LinkInstanceId ?? string.Empty,
                            LinkName = source.LinkName ?? string.Empty
                        },
                        Orientation = new Point3DData(orientation.X, orientation.Y, orientation.Z),
                        MountingType = mountingType,
                        BoundingBox = hostBBox
                    };

                    sprinklers.Add(data);
                }
            }
            catch (Exception ex)
            {
                issues.Add(new ExtractionIssue(
                    ExtractionIssueSeverity.Warning,
                    "ExistingSprinklerExtraction",
                    $"Error extracting existing sprinklers from '{source.DocumentTitle}': {ex.Message}"));
            }
        }

        private static string ClassifyMounting(string familyName, string typeName, XYZ orientation)
        {
            string combined = ((familyName ?? "") + " " + (typeName ?? "")).ToLowerInvariant();

            if (combined.Contains("sidewall")) return "Sidewall";
            if (combined.Contains("upright")) return "Upright";
            if (combined.Contains("concealed")) return "Concealed";
            if (combined.Contains("pendent") || combined.Contains("pendant")) return "Pendent";

            if (orientation != null)
            {
                if (orientation.Z > 0.5) return "Upright";
                if (Math.Abs(orientation.Z) < 0.3) return "Sidewall";
            }

            return "Pendent";
        }
    }
}
