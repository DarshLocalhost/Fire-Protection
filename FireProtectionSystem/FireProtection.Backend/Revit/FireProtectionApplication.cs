using System;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.UI;
using FireProtection.UI.Services;

namespace FireProtection.Backend.Revit
{
    /// <summary>
    /// Revit external application entry point for the Fire Protection System.
    ///
    /// Creates a "Fire Protection" ribbon tab, a "Fire Protection" panel, and
    /// a single "Fire Protection" push button that invokes the existing
    /// FireProtection.Backend.Commands.FireProtectionCommand.
    /// </summary>
    public class FireProtectionApplication : IExternalApplication
    {
        private const string TabName = "Fire Protection";
        private const string PanelName = "Fire Protection";
        private const string ButtonName = "FireProtectionButton";
        private const string ButtonText = "Fire Protection";
        private const string CommandClass = "FireProtection.Backend.Commands.FireProtectionCommand";

        public Result OnStartup(UIControlledApplication application)
        {
            InstallCrashDiagnostics();

            try
            {
                // 1) Ensure the ribbon tab exists (Revit throws if it already exists).
                TryCreateRibbonTab(application, TabName);

                // 2) Ensure the panel exists on that tab; reuse if already present.
                RibbonPanel panel = GetOrCreatePanel(application, TabName, PanelName);

                // 3) Ensure the button exists on that panel; skip if already present.
                if (!PanelContainsButton(panel, ButtonName))
                {
                    string assemblyPath = Assembly.GetExecutingAssembly().Location;

                    PushButtonData buttonData = new PushButtonData(
                        ButtonName,
                        ButtonText,
                        assemblyPath,
                        CommandClass);

                    buttonData.ToolTip =
                        "Open the Fire Protection System tool.";

                    buttonData.LongDescription =
                        "Extracts levels and rooms from the current Revit model and " +
                        "opens the Fire Protection System UI for sprinkler, smoke detector, " +
                        "and notification appliance planning.";

                    panel.AddItem(buttonData);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show(
                    "Fire Protection System",
                    "Failed to initialize the Fire Protection ribbon:\n\n" + ex.Message);

                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private static void InstallCrashDiagnostics()
        {
            try
            {
                // Without this, an unhandled fault anywhere in the add-in is reported to the user as
                // a bare "SEHException: External component has thrown an exception" at
                // DeactivateActCtx during teardown, with the CAUSE already lost. Logging it here
                // is the difference between a diagnosable bug and an unreportable one.
                AppDomain.CurrentDomain.UnhandledException += delegate (object sender, UnhandledExceptionEventArgs e)
                {
                    FireProtectionLog.Error(
                        "UNHANDLED exception (terminating=" + e.IsTerminating + ") — this is the real fault "
                        + "behind any later SEHException/DeactivateActCtx message.",
                        e.ExceptionObject as Exception);
                };

                // WPF swallows binding/converter exceptions by default and they resurface at
                // teardown. The dispatcher's own handler is attached by WpfHost when it creates the
                // Application — it cannot be attached from here because Application.Current is
                // still null at ribbon-load time.
            }
            catch (Exception ex)
            {
                // Diagnostics must never prevent the ribbon from loading.
                FireProtectionLog.Warn("Could not install crash diagnostics: " + ex.Message);
            }
        }

        private static void TryCreateRibbonTab(UIControlledApplication application, string tabName)
        {
            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Tab already exists — Revit throws ArgumentException in that case. Safe to ignore.
            }
        }

        private static RibbonPanel GetOrCreatePanel(
            UIControlledApplication application,
            string tabName,
            string panelName)
        {
            RibbonPanel existing = application
                .GetRibbonPanels(tabName)
                .FirstOrDefault(p => string.Equals(p.Name, panelName, StringComparison.Ordinal));

            if (existing != null)
            {
                return existing;
            }

            return application.CreateRibbonPanel(tabName, panelName);
        }

        private static bool PanelContainsButton(RibbonPanel panel, string buttonName)
        {
            foreach (RibbonItem item in panel.GetItems())
            {
                if (string.Equals(item.Name, buttonName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}