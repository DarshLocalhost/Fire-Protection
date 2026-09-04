using System.Collections.Generic;

namespace FireProtection.UI.Models.Sprinklers.BruteForce
{
    /// <summary>
    /// Root result of actual Revit sprinkler placement. Revit-independent (no Autodesk.Revit.DB
    /// references): Revit ElementIds are carried as strings for full traceability.
    /// </summary>
    public class SprinklerPlacementResult
    {
        public int SchemaVersion { get; set; } = 1;

        public string TimestampUtc { get; set; }

        public string ProjectName { get; set; }

        public string SelectedFamily { get; set; }

        public string SelectedType { get; set; }

        public int RoomsProcessed { get; set; }

        public int CalculatedSprinklerCount { get; set; }

        public int PlacedSprinklerCount { get; set; }

        public int FailedSprinklerCount { get; set; }

        public int SkippedDuplicateCount { get; set; }

        /// <summary>Rooms that were skipped because the chosen family/type is not loaded in the
        /// live Revit model at placement time (Decision 017). Distinct from
        /// <see cref="FailedSprinklerCount"/> (placement attempted and failed) and
        /// <see cref="SkippedDuplicateCount"/> (placement would have collided with an existing sprinkler).</summary>
        public int SkippedMissingFamilyCount { get; set; }

        /// <summary>Subset of <see cref="PlacedSprinklerCount"/> that passed post-placement spatial validation (§23).</summary>
        public int PlacedAndValidCount { get; set; }

        /// <summary>Instances created but spatially wrong (actual XYZ far from requested / origin snap) — §23/§24.</summary>
        public int PlacedButInvalidCount { get; set; }

        /// <summary>Proven Revit FamilyPlacementType of the selected family (diagnostic; replaces log-string noise). §26/§28.</summary>
        public string ResolvedFamilyPlacementType { get; set; }

        /// <summary>
        /// True when the user cancelled the run. A cancelled run is rolled back whole — nothing is created —
        /// so this is not a partial success.
        /// </summary>
        public bool WasCancelled { get; set; }

        /// <summary>Rooms left untouched because they already contained sprinklers (Skip policy).</summary>
        public int SkippedExistingRoomCount { get; set; }

        /// <summary>Existing sprinklers deleted before placing the new set (Replace policy).</summary>
        public int ReplacedExistingCount { get; set; }

        /// <summary>
        /// Points refused because they fell outside their own room boundary. Non-zero here almost always means a
        /// coordinate-space bug (a linked-model point that was never transformed into host coordinates).
        /// </summary>
        public int SkippedOutsideRoomCount { get; set; }

        public bool Success => FailedSprinklerCount == 0 && PlacedButInvalidCount == 0 && PlacedSprinklerCount > 0;

        public List<PlacementRoomResult> Rooms { get; set; }

        public List<string> Warnings { get; set; }

        public List<string> Errors { get; set; }

        public SprinklerPlacementResult()
        {
            Rooms = new List<PlacementRoomResult>();
            Warnings = new List<string>();
            Errors = new List<string>();
        }

        public string SummaryText()
        {
            string header = WasCancelled
                ? "Sprinkler placement cancelled - the run was rolled back and nothing was created."
                : Success
                    ? "Sprinkler placement complete."
                    : "Sprinkler placement completed with warnings.";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(header);
            sb.AppendLine();
            sb.AppendLine($"Rooms processed : {RoomsProcessed}");
            sb.AppendLine($"Calculated      : {CalculatedSprinklerCount}");
            sb.AppendLine($"Placed (created): {PlacedSprinklerCount}");
            sb.AppendLine($"  ...validated  : {PlacedAndValidCount}");
            if (PlacedButInvalidCount > 0)
                sb.AppendLine($"  ...INVALID    : {PlacedButInvalidCount}");
            sb.AppendLine($"Failed          : {FailedSprinklerCount}");
            if (SkippedDuplicateCount > 0)
                sb.AppendLine($"Skipped (dup)   : {SkippedDuplicateCount}");
            if (SkippedExistingRoomCount > 0)
                sb.AppendLine($"Rooms skipped   : {SkippedExistingRoomCount} (already had sprinklers)");
            if (ReplacedExistingCount > 0)
                sb.AppendLine($"Replaced        : {ReplacedExistingCount} existing sprinkler(s) deleted");
            if (SkippedOutsideRoomCount > 0)
                sb.AppendLine($"Outside room    : {SkippedOutsideRoomCount} (check coordinates)");
            if (!string.IsNullOrEmpty(ResolvedFamilyPlacementType))
                sb.AppendLine($"Placement type  : {ResolvedFamilyPlacementType}");
            sb.AppendLine($"Warnings        : {Warnings.Count}");

            if (FailedSprinklerCount > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Failed:");
                foreach (PlacementRoomResult room in Rooms)
                {
                    foreach (FailedSprinklerEntry f in room.Failed)
                    {
                        sb.AppendLine($"  Room: {room.RoomName} ({room.RoomNumber})");
                        sb.AppendLine($"  Reason: {f.Reason}");
                    }
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Per-room breakdown of placement attempts, preserving room traceability.
    /// </summary>
    public class PlacementRoomResult
    {
        public string RoomId { get; set; }
        public string RoomName { get; set; }
        public string RoomNumber { get; set; }

        public List<PlacedSprinklerEntry> Placed { get; set; }
        public List<FailedSprinklerEntry> Failed { get; set; }

        public PlacementRoomResult()
        {
            Placed = new List<PlacedSprinklerEntry>();
            Failed = new List<FailedSprinklerEntry>();
        }
    }

    /// <summary>
    /// A successfully created Revit FamilyInstance sprinkler.
    /// </summary>
    public class PlacedSprinklerEntry
    {
        // Actual instance location (from instance.Location) — populated after successful placement.
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        // Requested (input) candidate position, preserved separately per Phase 7.
        public double RequestedX { get; set; }
        public double RequestedY { get; set; }
        public double RequestedZ { get; set; }

        public string RoomId { get; set; }
        public string LevelId { get; set; }

        public string HostLevelId { get; set; }
        public string HostLevelName { get; set; }

        /// <summary>Revit ElementId of the created FamilyInstance (string form).</summary>
        public string RevitElementId { get; set; }

        /// <summary>Hosting strategy actually used (FaceBasedHost / WorkPlaneCeilingFace / WorkPlaneSketchPlane / LevelBased).</summary>
        public string HostingStrategy { get; set; }

        /// <summary>Where the ceiling host was found (host / link / none).</summary>
        public string CeilingSource { get; set; }

        /// <summary>Structured post-placement status code (§26): PLACED_AND_VALID or PLACED_BUT_INVALID.</summary>
        public string StatusCode { get; set; }

        /// <summary>True when the created instance's actual location matches the requested point within tolerance (§23).</summary>
        public bool IsSpatiallyValid { get; set; }

        /// <summary>Distance (ft) between the requested point and the instance's actual location — post-placement evidence (§23/§24).</summary>
        public double PlacementDeviationFt { get; set; }

        // --- Hosting diagnostics carried from the placement strategy (§26). ---

        /// <summary>Proven Revit FamilyPlacementType used to select the strategy (per-entry copy for traceability).</summary>
        public string FamilyPlacementType { get; set; }

        /// <summary>ElementId (string) of the host/link ceiling the strategy hosted on, when a ceiling face was used.</summary>
        public string HostCeilingElementId { get; set; }

        /// <summary>Name of the RevitLinkInstance the host ceiling came from (null for a host-document ceiling / non-ceiling host).</summary>
        public string LinkInstanceName { get; set; }

        // --- ACTUAL host/level relationship read back FROM THE CREATED INSTANCE (source of truth) (§23/§24). ---
        // These distinguish a genuine hosting defect from a view-range / level-association symptom
        // (the "sprinkler appears in the wrong floor plan" report) without a second Revit round-trip.

        /// <summary>ElementId (string) of the element the instance is actually hosted on, or null if unhosted.</summary>
        public string ActualHostElementId { get; set; }

        /// <summary>Name of the element the instance is actually hosted on (e.g. the ceiling, or the link instance).</summary>
        public string ActualHostName { get; set; }

        /// <summary>ElementId (string) of the instance's actual associated Level (may be null/invalid for a face/work-plane host).</summary>
        public string ActualInstanceLevelId { get; set; }

        /// <summary>Name of the instance's actual associated Level, if any.</summary>
        public string ActualInstanceLevelName { get; set; }

        /// <summary>Name of the instance's actual "Schedule Level" — the level association that drives plan-view visibility.</summary>
        public string ActualScheduleLevelName { get; set; }

        public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3}) -> {RevitElementId} [{StatusCode}, dev {PlacementDeviationFt:F3}ft]";
    }

    /// <summary>
    /// A calculated point that could not be placed, with a clear diagnostic reason.
    /// </summary>
    public class FailedSprinklerEntry
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public string RoomId { get; set; }
        public string LevelId { get; set; }

        public string Reason { get; set; }

        /// <summary>Structured failure code (§26), e.g. REQUIRED_HOST_UNAVAILABLE / WORKPLANE_UNAVAILABLE / UNSUPPORTED_FAMILY_PLACEMENT.</summary>
        public string ErrorCode { get; set; }

        /// <summary>True when the failure was a safety skip (e.g. existing sprinkler too close).</summary>
        public bool SkippedDueToDuplicate { get; set; }

        // --- Level-resolution diagnostics (populated even on failure) ---
        public string SourceDocument { get; set; }
        public string SourceLevelName { get; set; }
        public string SourceLevelId { get; set; }
        public double? SourceElevationFt { get; set; }
        public string HostDocument { get; set; }
        public string AttemptedHostLevelMatches { get; set; }

        // --- Placement-strategy diagnostics (Phase 11) ---
        public string FamilyPlacementType { get; set; }
        public string HostingStrategy { get; set; }
        public string CeilingSource { get; set; }
        public string LinkInstanceName { get; set; }
        public string HostCeilingElementId { get; set; }
        public string ExceptionDetail { get; set; }

        public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3}) -> {Reason}";
    }
}
