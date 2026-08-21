using FireProtection.Backend.Models.DTOs;
using System.Collections.Generic;

namespace FireProtection.Backend.Services.Extraction
{
    public class ModelExtractionValidator
    {
        public ValidationSummary Validate(
            ModelSnapshot snapshot,
            List<ExtractionIssue> issues)
        {
            ValidationSummary summary = new ValidationSummary();

            if (snapshot == null)
            {
                summary.IsValid = false;
                summary.ErrorCount = 1;
                return summary;
            }

            summary.LevelsExtracted = snapshot.Levels?.Count ?? 0;
            summary.RoomsExtracted = snapshot.Rooms?.Count ?? 0;
            summary.LinkedModelsFound = snapshot.Model?.Links?.Count ?? 0;
            summary.ExistingSprinklersExtracted = snapshot.ExistingSprinklers?.Count ?? 0;
            summary.ObstaclesExtracted = snapshot.Obstacles?.Count ?? 0;

            int loadedLinks = 0;
            if (snapshot.Model?.Links != null)
            {
                foreach (LinkInfo link in snapshot.Model.Links)
                {
                    if (link.IsLoaded) loadedLinks++;
                    else
                    {
                        issues.Add(new ExtractionIssue(
                            ExtractionIssueSeverity.Warning,
                            "LinkValidation",
                            $"Revit link '{link.LinkName}' is unloaded or unresolvable.",
                            link.InstanceId,
                            link.LinkName));
                    }
                }
            }
            summary.LinkedModelsLoaded = loadedLinks;

            int withBoundary = 0;
            int missingBoundary = 0;
            int withCeiling = 0;
            int withoutCeiling = 0;

            HashSet<string> seenRoomIds = new HashSet<string>();

            if (snapshot.Rooms != null)
            {
                foreach (RoomData room in snapshot.Rooms)
                {
                    // Duplicate check
                    if (seenRoomIds.Contains(room.RoomId))
                    {
                        issues.Add(new ExtractionIssue(
                            ExtractionIssueSeverity.Warning,
                            "RoomValidation",
                            $"Duplicate room ID detected: '{room.RoomId}' for room '{room.Number}' ({room.Name}).",
                            room.RoomId,
                            room.Name));
                    }
                    seenRoomIds.Add(room.RoomId);

                    // Boundary check
                    if (room.Boundary != null && room.Boundary.Polygon != null && room.Boundary.Polygon.Count >= 3)
                    {
                        withBoundary++;
                    }
                    else
                    {
                        missingBoundary++;
                        issues.Add(new ExtractionIssue(
                            ExtractionIssueSeverity.Warning,
                            "RoomValidation",
                            $"Room '{room.Number}' ({room.Name}) has fewer than 3 boundary vertices.",
                            room.RoomId,
                            room.Name));
                    }

                    // Ceiling check
                    if (room.Ceilings != null && room.Ceilings.Count > 0)
                    {
                        withCeiling++;
                    }
                    else
                    {
                        withoutCeiling++;
                    }
                }
            }

            summary.RoomsWithBoundaries = withBoundary;
            summary.RoomsMissingBoundaries = missingBoundary;
            summary.CeilingsResolved = withCeiling;
            summary.RoomsWithoutCeilings = withoutCeiling;

            // Count warnings & errors
            int warnings = 0;
            int errors = 0;
            foreach (ExtractionIssue issue in issues)
            {
                if (issue.Severity == ExtractionIssueSeverity.Error.ToString()) errors++;
                else if (issue.Severity == ExtractionIssueSeverity.Warning.ToString()) warnings++;
            }

            summary.WarningCount = warnings;
            summary.ErrorCount = errors;
            summary.IsValid = errors == 0;

            return summary;
        }
    }
}
