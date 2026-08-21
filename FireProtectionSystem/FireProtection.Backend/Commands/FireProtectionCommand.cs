using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Services.Extraction;
using FireProtection.Backend.Services.Placement;
using FireProtection.UI.Services;
using Newtonsoft.Json;
using System;

namespace FireProtection.Backend.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class FireProtectionCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApplication = commandData?.Application;
            UIDocument uiDocument = uiApplication?.ActiveUIDocument;
            Document hostDocument = uiDocument?.Document;

            if (hostDocument == null)
            {
                message = "Active Revit document is unavailable. Please open a project model.";
                return Result.Failed;
            }

            try
            {
                // 1. Initialize production model extraction service
                FireProtectionExtractionService extractionService = new FireProtectionExtractionService();

                // 2. Run full extraction pipeline across host MEP document and linked architectural models
                //    Normalizes coordinates to host MEP coordinate system (feet), classifies hazards,
                //    resolves ceiling geometry, extracts obstacles and existing sprinklers, and validates.
                var (snapshot, exportPath) = extractionService.ExtractAndExport(hostDocument);

                // 3. Serialize normalized snapshot to versioned JSON string for UI consumption
                string json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);

                // 4. Initialize production placement executor & family source for UI interaction
                RevitPlacementExecutor executor = new RevitPlacementExecutor(hostDocument);
                RevitSprinklerFamilySource sprinklerFamilySource = new RevitSprinklerFamilySource(hostDocument);

                // 5. Open UI modal window on Revit API thread
                UiLauncher.Show(json, executor, sprinklerFamilySource);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show(
                    "Fire Protection System - Extraction Error",
                    $"An error occurred during model extraction:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}");
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}