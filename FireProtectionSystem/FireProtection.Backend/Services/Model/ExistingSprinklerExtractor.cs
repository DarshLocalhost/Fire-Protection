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

            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(context.HostDocument)
                    .OfCategory(BuiltInCategory.OST_Sprinklers)
                    .WhereElementIsNotElementType();

                foreach (Element element in collector)
                {
                    if (element is FamilyInstance instance)
                    {
#if REVIT_2024 || REVIT_2025 || REVIT_2026
                        string elementId = instance.Id.Value.ToString();
#else
                        string elementId = instance.Id.ToString();
#endif

                        // Location
                        LocationPoint locPoint = instance.Location as LocationPoint;
                        XYZ point = locPoint != null ? locPoint.Point : null;

                        if (point == null)
                        {
                            BoundingBoxXYZ bbox = instance.get_BoundingBox(null);
                            if (bbox != null)
                            {
                                point = new XYZ(
                                    (bbox.Min.X + bbox.Max.X) / 2.0,
                                    (bbox.Min.Y + bbox.Max.Y) / 2.0,
                                    (bbox.Min.Z + bbox.Max.Z) / 2.0);
                            }
                            else
                            {
                                continue;
                            }
                        }

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
                            Level hostLevel = context.HostDocument.GetElement(instance.LevelId) as Level;
                            if (hostLevel != null)
                            {
                                levelName = hostLevel.Name;
                                levelElev = hostLevel.Elevation;
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
                            ? RevitModelContext.TransformBoundingBox(bBox, Transform.Identity)
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
                                DocumentTitle = context.HostDocument.Title,
                                DocumentPath = context.HostDocument.PathName ?? string.Empty,
                                IsFromLink = false
                            },
                            Orientation = new Point3DData(orientation.X, orientation.Y, orientation.Z),
                            MountingType = mountingType,
                            BoundingBox = hostBBox
                        };

                        sprinklers.Add(data);
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add(new ExtractionIssue(
                    ExtractionIssueSeverity.Warning,
                    "ExistingSprinklerExtraction",
                    $"Error extracting existing sprinklers: {ex.Message}"));
            }

            return sprinklers;
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
