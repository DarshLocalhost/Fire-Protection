using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Services;
using FireProtection.Backend.Services.Catalog;
using FireProtection.Backend.Services.Extraction;
using FireProtection.Backend.Services.Placement;
using FireProtection.Backend.Services.Placement.NotificationAppliances;
using FireProtection.Backend.Services.Placement.SmokeDetectors;
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

                // CatalogHolder is a tiny shared mutable reference to the current
                // ICatalog. The Revit-aware family source (and its resolver
                // delegate) is constructed BEFORE the user picks a catalog file,
                // so the resolver cannot capture a catalog at construction time.
                // Instead, it reads the holder lazily on every call — the
                // CatalogViewModel writes the holder when the user loads a file.
                CatalogHolder catalogHolder = new CatalogHolder();

                // The family source's resolver looks up the Mount column from the
                // CURRENT catalog. Passing the holder's Current getter as a
                // delegate makes the lookup lazy and survives catalog reloads.
                RevitSprinklerFamilySource sprinklerFamilySource = new RevitSprinklerFamilySource(
                    hostDocument, () => catalogHolder.Current);

                // Step 2 — pass the Revit-aware device-context resolver (produced by
                // sprinklerFamilySource) into the placement input exporter so the
                // per-row (and universal) device placement context can be resolved
                // at the Revit-aware boundary and carried on every PlacementRoomInput.
                // The calculation engine itself does not yet read the context; the
                // calculation algorithm is unchanged byte-for-byte.
                PlacementInputJsonExporter inputExporter = new PlacementInputJsonExporter(
                    snapshot.Obstacles,
                    snapshot.ExistingSprinklers,
                    sprinklerFamilySource.GetDeviceContextResolver());

                RevitSprinklerPlacementService placementService = new RevitSprinklerPlacementService(hostDocument);

                // Decision 020: the catalog (Excel) is the catalog of record. The user selects
                // the workbook each session via the in-UI file picker; the loader is Backend-only
                // (ClosedXML), so the Backend constructs the CatalogService and the UI consumes
                // it through ICatalog. The catalog starts unloaded — the user picks a file in
                // the top bar.
                //
                // The viewmodel receives the holder so it can write the loaded
                // catalog into it on every successful TryLoad — the family
                // source's resolver then sees the new catalog on its next call.
                CatalogViewModel catalogViewModel = new CatalogViewModel(BuildCatalogFromPath, catalogHolder);

                // Device seams: the Revit-aware family source + placement executor for each device tab. The
                // smoke-detector sources read the CURRENT catalog lazily via the same holder as the sprinkler
                // source, so a catalog (re)load after the window opens is picked up on the next call. The
                // notification-appliance executor uses rating-aware NFPA 72 Chapter 18 planning rules
                // keyed by appliance type, candela, dBA, mount, ceiling slope, obstacles, and duplicates;
                // the result remains flagged for engineering review until the project design basis is approved.
                DevicePlacementSeams deviceSeams = new DevicePlacementSeams
                {
                    SmokeFamilySource = new RevitSmokeDetectorFamilySource(hostDocument, () => catalogHolder.Current),
                    SmokeExecutor = new RevitSmokeDetectorPlacementExecutor(hostDocument, () => catalogHolder.Current),
                    NotificationFamilySource = new RevitNotificationApplianceFamilySource(hostDocument),
                    NotificationExecutor = new RevitNotificationAppliancePlacementExecutor(hostDocument, () => catalogHolder.Current)
                };

                // Modeless + owned by Revit's main window: Revit stays fully usable while the tool is open.
                UiLauncher.Show(
                    json,
                    inputExporter,
                    sprinklerFamilySource,
                    placementService,
                    catalogViewModel,
                    uiApplication.MainWindowHandle,
                    deviceSeams);

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