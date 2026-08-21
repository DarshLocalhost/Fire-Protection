using Autodesk.Revit.DB;
using FireProtection.Backend.Models.DTOs;
using System.Collections.Generic;

namespace FireProtection.Backend.Services.Model
{
    public class LevelExtractor
    {
        /// <summary>
        /// Extracts levels from both the host MEP document and all loaded link contexts.
        /// </summary>
        public List<LevelData> ExtractLevels(RevitModelContext context, List<ExtractionIssue> issues)
        {
            List<LevelData> results = new List<LevelData>();
            HashSet<string> seenKeys = new HashSet<string>();

            // 1. Extract host levels
            ExtractFromDocument(
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
                results,
                seenKeys,
                issues);

            // 2. Extract linked levels
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
                    results,
                    seenKeys,
                    issues);
            }

            // Sort levels by elevation ascending
            results.Sort((a, b) => a.ElevationFt.CompareTo(b.ElevationFt));

            return results;
        }

        private void ExtractFromDocument(
            Document document,
            Transform transform,
            SourceReferenceData source,
            List<LevelData> results,
            HashSet<string> seenKeys,
            List<ExtractionIssue> issues)
        {
            FilteredElementCollector collector = new FilteredElementCollector(document)
                .OfClass(typeof(Level));

            foreach (Element element in collector)
            {
                if (element is Level level)
                {
#if REVIT_2024 || REVIT_2025 || REVIT_2026
                    string elementId = level.Id.Value.ToString();
#else
                    string elementId = level.Id.ToString();
#endif
                    string uniqueKey = $"{source.DocumentTitle}|{elementId}";
                    if (seenKeys.Contains(uniqueKey)) continue;
                    seenKeys.Add(uniqueKey);

                    // Transform elevation to host MEP coordinate space
                    XYZ sourcePoint = new XYZ(0, 0, level.Elevation);
                    XYZ hostPoint = transform != null && !transform.IsIdentity
                        ? transform.OfPoint(sourcePoint)
                        : sourcePoint;

                    LevelData levelData = new LevelData
                    {
                        LevelId = elementId,
                        ElementId = elementId,
                        Name = level.Name,
                        ElevationFt = hostPoint.Z,
                        Origin = source.DocumentTitle,
                        Source = new SourceReferenceData
                        {
                            DocumentTitle = source.DocumentTitle,
                            DocumentPath = source.DocumentPath,
                            IsFromLink = source.IsFromLink,
                            LinkInstanceId = source.LinkInstanceId,
                            LinkName = source.LinkName
                        }
                    };

                    results.Add(levelData);
                }
            }
        }
    }
}
