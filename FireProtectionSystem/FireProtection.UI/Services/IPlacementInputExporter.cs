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
