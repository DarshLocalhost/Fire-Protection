using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Services;
using FireProtection.Backend.Services.Catalog;
using FireProtection.Backend.Services.Extraction;
using FireProtection.Backend.Services.Placement;
using FireProtection.Backend.Services.Placement.Sprinklers.Final;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Catalog;
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

            // The tool window is modeless, so it can still be open from a previous click. Re-activate it
            // instead of re-extracting the model and opening a second window onto the same Document.
            if (UiLauncher.TryActivateExisting()) return Result.Succeeded;

            FireProtectionLog.Info("Session started. Document '" + (hostDocument.Title ?? "(untitled)")
                + "', Revit " + (uiApplication.Application != null
                    ? uiApplication.Application.VersionNumber
                    : "?") + ".");

            // Length columns and numeric cells follow the project's own display unit (list-2 item 5).
            // Stored values stay in decimal feet everywhere; this only affects presentation and parsing.
            ConfigureDisplayUnits(hostDocument);

            try
            {
                // Must be created here: ExternalEvent.Create is only legal in a Revit API context. Once
                // registered, the modeless window's Revit-touching flows marshal back through it.
                RevitApiContext.CreateAndRegister();

                FireProtectionExtractionService extractionService = new FireProtectionExtractionService();

                var (snapshot, exportPath) = extractionService.ExtractAndExport(hostDocument);

                string json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);

                RevitSprinklerFamilySource sprinklerFamilySource = new RevitSprinklerFamilySource(hostDocument);

                PlacementInputJsonExporter inputExporter = new PlacementInputJsonExporter(
                    snapshot.Obstacles,
                    snapshot.ExistingSprinklers);

                RevitSprinklerPlacementService placementService = new RevitSprinklerPlacementService(hostDocument);

                // Decision 020: the catalog (Excel) is the catalog of record. The user selects
                // the workbook each session via the in-UI file picker; the loader is Backend-only
                // (ClosedXML), so the Backend constructs the CatalogService and the UI consumes
                // it through ICatalog. The catalog starts unloaded — the user picks a file in
                // the top bar.
                CatalogViewModel catalogViewModel = new CatalogViewModel(BuildCatalogFromPath);

                // Modeless + owned by Revit's main window: Revit stays fully usable while the tool is open.
                UiLauncher.Show(
                    json,
                    inputExporter,
                    sprinklerFamilySource,
                    placementService,
                    catalogViewModel,
                    uiApplication.MainWindowHandle);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                FireProtectionLog.Error("Startup failed.", ex);
                TaskDialog.Show(
                    "Fire Protection System - Extraction Error",
                    $"An error occurred during model extraction:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}");
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Points <see cref="UnitDisplay"/> at the project's length unit so the UI shows and reads mm on a
        /// metric project instead of feet. Feet-and-inches and fractional-inch formats are shown as decimal
        /// feet / decimal inches: the override cells are plain numeric boxes, so a fractional entry such as
        /// 5' 6" has nowhere to go. Never fatal - a failure here just leaves the UI in feet.
        /// </summary>
        private static void ConfigureDisplayUnits(Document document)
        {
            try
            {
                FormatOptions options = document.GetUnits().GetFormatOptions(SpecTypeId.Length);
                if (options == null) return;

                ForgeTypeId unitTypeId = options.GetUnitTypeId();
                if (unitTypeId == null) return;

                string suffix = ShortUnitLabel(unitTypeId);
                double oneFootInDisplayUnits = UnitUtils.ConvertFromInternalUnits(1.0, unitTypeId);
                int decimals = DecimalsFromAccuracy(options.Accuracy);

                UnitDisplay.Configure(suffix, oneFootInDisplayUnits, decimals);
                FireProtectionLog.Info("Display unit: " + suffix + " (1 ft = "
                    + oneFootInDisplayUnits.ToString("G6") + " " + suffix + ", " + decimals + " dp).");
            }
            catch (Exception ex)
            {
                FireProtectionLog.Warn("Could not read the project's length unit; the UI stays in feet. "
                    + ex.Message);
            }
        }

        /// <summary>Short symbol for the units the tool can display as a decimal number. Anything else falls
        /// back to Revit's own label so the header is still honest about what the column contains.</summary>
        private static string ShortUnitLabel(ForgeTypeId unitTypeId)
        {
            if (unitTypeId == UnitTypeId.Millimeters) return "mm";
            if (unitTypeId == UnitTypeId.Centimeters) return "cm";
            if (unitTypeId == UnitTypeId.Decimeters) return "dm";
            if (unitTypeId == UnitTypeId.Meters) return "m";
            if (unitTypeId == UnitTypeId.MetersCentimeters) return "m";
            if (unitTypeId == UnitTypeId.Inches) return "in";
            if (unitTypeId == UnitTypeId.FractionalInches) return "in";
            if (unitTypeId == UnitTypeId.Feet) return "ft";
            if (unitTypeId == UnitTypeId.FeetFractionalInches) return "ft";

            try { return LabelUtils.GetLabelForUnit(unitTypeId); }
            catch { return "ft"; }
        }

        /// <summary>Revit stores rounding as an accuracy value (0.01 = two decimals). Clamped to 0-4 because
        /// the override cells are read back with double.TryParse, not with Revit's own formatter.</summary>
        private static int DecimalsFromAccuracy(double accuracy)
        {
            if (accuracy <= 0 || double.IsNaN(accuracy) || double.IsInfinity(accuracy)) return 2;

            int decimals = (int)Math.Round(-Math.Log10(accuracy), MidpointRounding.AwayFromZero);
            if (decimals < 0) return 0;
            return decimals > 4 ? 4 : decimals;
        }

        private static ICatalog BuildCatalogFromPath(string path)
        {
            Catalog catalog = CatalogLoader.Load(path);
            return CatalogService.FromCatalog(catalog);
        }
    }
}