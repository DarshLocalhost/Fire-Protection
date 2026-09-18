using System.Collections.Generic;
using FireProtection.UI.Models.Sprinklers.BruteForce;
using Newtonsoft.Json.Linq;

namespace FireProtection.UI.Services
{
    public class PlacementRoomInputItem
    {
        public string LevelId { get; set; }
        public string LevelName { get; set; }
        public double LevelElevationFt { get; set; }

        public string RoomId { get; set; }
        public string RoomName { get; set; }
        public string RoomNumber { get; set; }

        public double AreaSqFt { get; set; }
        public string EffectiveHazardClass { get; set; }

        public double? CeilingHeightFt { get; set; }
        public string CeilingType { get; set; }
        public List<double[]> Polygon { get; set; }

        /// <summary>
        /// Per-row sprinkler family (Decision 017). Replaces the universal
        /// <see cref="IPlacementInputExporter.CalculateBruteForce"/>'s
        /// <c>selectedFamilyName</c> argument when the room's per-row value is set.
        /// </summary>
        public string SelectedSprinklerFamilyName { get; set; }

        /// <summary>Per-row sprinkler type (Decision 017).</summary>
        public string SelectedSprinklerTypeName { get; set; }

        /// <summary>Per-row spacing override in feet (Decision 018). null = use rule-set default.</summary>
        public double? OverrideMaxSpacingFt { get; set; }

        /// <summary>Per-row boundary (wall) clearance override in feet (Decision 018). null = use rule-set default.</summary>
        public double? OverrideBoundaryClearanceFt { get; set; }

        /// <summary>Per-room placement behavior override as string ("WallSidewall", "CeilingOverhead", etc.). null = use the universal/default behavior.</summary>
        public string SelectedSprinklerPlacementBehavior { get; set; }

        /// <summary>Per-room sprinkler orientation override ("pendent", "upright", "sidewall"). null = use default.</summary>
        public string SelectedSprinklerOrientation { get; set; }

        // Full room payload (ceilings, obstacles, existingSprinklers, source, etc.) captured
        // from the ModelSnapshot JSON and forwarded to the exporter. The Backend rehydrates
        // its own DTOs from this, avoiding any circular project dependency.
        public JObject FullRoomJson { get; set; }

        public PlacementRoomInputItem()
        {
            Polygon = new List<double[]>();
        }
    }

    public class PlacementInputExportResult
    {
        public bool Success { get; set; }
        public int RoomsCount { get; set; }
        public double TotalAreaSqFt { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string ExportFilePath { get; set; }
        public string ErrorMessage { get; set; }
    }

    public interface IPlacementInputExporter
    {
        PlacementInputExportResult ExportInput(
            string projectName,
            string selectedFamilyName,
            string selectedTypeName,
            IReadOnlyList<PlacementRoomInputItem> selectedRooms);

        /// <summary>
        /// Runs the in-memory BruteForce sprinkler calculation over the selected rooms and returns
        /// the structured result. The calculation consumes the in-memory placement DTOs directly
        /// (never a JSON file). No Revit elements are created.
        /// </summary>
        BruteForceCalculationResult CalculateBruteForce(
            string projectName,
            string selectedFamilyName,
            string selectedTypeName,
            IReadOnlyList<PlacementRoomInputItem> selectedRooms);

        /// <summary>
        /// Writes the separate audit JSON produced after actual Revit placement. Runtime placement
        /// does NOT depend on this file; it records placed/failed points and Revit ElementIds.
        /// </summary>
        string ExportPlacementResult(SprinklerPlacementResult result);
    }
}
