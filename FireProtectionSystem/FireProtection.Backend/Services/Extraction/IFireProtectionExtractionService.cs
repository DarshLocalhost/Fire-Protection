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
        /// Executes extraction and exports the result to a JSON file.
        /// </summary>
        (ModelSnapshot Snapshot, string ExportPath) ExtractAndExport(Document hostDocument, string exportFilePath = null);
    }
}
