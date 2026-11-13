using Autodesk.Revit.DB;
using FireProtection.Backend.Models.DTOs;

namespace FireProtection.Backend.Services.Extraction
{
    public interface IFireProtectionExtractionService
    {
        /// <summary>
        /// Executes full model data extraction from the active host MEP document and linked architectural models.
        /// </summary>
        ModelSnapshot ExtractModelSnapshot(Document hostDocument);

        /// <summary>
        /// Executes fast lightweight model data extraction (Levels, Ceilings, Rooms, Existing Sprinklers)
        /// without heavy obstacle geometry extraction, enabling near-instantaneous UI launch.
        /// </summary>
        ModelSnapshot ExtractLightweightSnapshot(Document hostDocument);

        /// <summary>
        /// Extracts MEP, architectural, and structural obstacles and associates them to rooms on-demand.
        /// </summary>
        System.Collections.Generic.List<ObstacleData> ExtractObstacles(Document hostDocument, System.Collections.Generic.List<RoomData> rooms);

        /// <summary>
        /// Executes extraction and exports the result to a JSON file.
        /// </summary>
        (ModelSnapshot Snapshot, string ExportPath) ExtractAndExport(Document hostDocument, string exportFilePath = null);
    }
}
