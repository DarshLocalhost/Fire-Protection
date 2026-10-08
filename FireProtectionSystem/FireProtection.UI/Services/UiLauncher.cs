using System;
using System.Windows;
using System.Windows.Interop;
using FireProtection.UI.ViewModels.Catalog;
using FireProtection.UI.Views;

namespace FireProtection.UI.Services
{
    public static class UiLauncher
    {
        // The one live tool window, so a second ribbon click activates it instead of opening a duplicate
        // that would fight the first one over the same Document.
        private static MainWindow _current;

        /// <summary>True while the tool window is open.</summary>
        public static bool IsOpen
        {
            get { return _current != null; }
        }

        /// <summary>
        /// The live tool window, or null. Used as the owner for message boxes: an ownerless dialog from a
        /// modeless window can end up behind Revit, which looks like a hang (see <see cref="Dialogs"/>).
        /// </summary>
        public static Window CurrentWindow
        {
            get { return _current; }
        }

        /// <summary>
        /// Brings an already-open tool window to the front. Returns false when nothing is open, so the
        /// caller knows it still has to do the (expensive) model extraction and open a window.
        /// </summary>
        public static bool TryActivateExisting()
        {
            MainWindow window = _current;
            if (window == null) return false;

            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
            return true;
        }

        public static void Show(
            string json,
            IPlacementInputExporter placementInputExporter,
            ISprinklerFamilySource sprinklerFamilySource,
            ISprinklerPlacementService sprinklerPlacementService)
        {
            Show(json, placementInputExporter, sprinklerFamilySource, sprinklerPlacementService, null, IntPtr.Zero);
        }

        public static void Show(
            string json,
            IPlacementInputExporter placementInputExporter,
            ISprinklerFamilySource sprinklerFamilySource,
            ISprinklerPlacementService sprinklerPlacementService,
            CatalogViewModel catalog)
        {
            Show(json, placementInputExporter, sprinklerFamilySource, sprinklerPlacementService, catalog, IntPtr.Zero);
        }

        /// <summary>
        /// Opens the tool window MODELESS so Revit stays fully usable while it is open. Everything that
        /// needs the Revit API is marshalled back through <see cref="RevitApi"/>.
        /// </summary>
        /// <param name="revitMainWindowHandle">
        /// Revit's main window handle. Setting it as the owner keeps the tool above Revit and makes it
        /// minimise/restore with Revit, without making it modal. <see cref="IntPtr.Zero"/> = no owner.
        /// </param>
        public static void Show(
            string json,
            IPlacementInputExporter placementInputExporter,
            ISprinklerFamilySource sprinklerFamilySource,
            ISprinklerPlacementService sprinklerPlacementService,
            CatalogViewModel catalog,
            IntPtr revitMainWindowHandle,
            DevicePlacementSeams deviceSeams = null)
        {
            if (TryActivateExisting()) return;

            // Revit is not a WPF host: without this, Application.Current is null and the
            // dispatcher can be finalised while Revit still needs it, which surfaces later as
            // SEHException "External component has thrown an exception" at DeactivateActCtx.
            // Must happen BEFORE the first Window is constructed.
            WpfHost.EnsureApplication();

            MainWindow window = new MainWindow(
                json, placementInputExporter, sprinklerFamilySource, sprinklerPlacementService, catalog, deviceSeams);

            if (revitMainWindowHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(window) { Owner = revitMainWindowHandle };
            }

            _current = window;
            window.Closed += delegate
            {
                _current = null;
                // Drop references into the Revit host: the model-catalog factory is a closure over
                // the active Document. Leaving a Document pinned by the UI object graph across
                // teardown is a known contributor to add-in unload failures.
                if (catalog != null) catalog.ReleaseHostReferences();
                FireProtectionLog.Info("Tool window closed; host references released.");
            };
            window.Show();
            FireProtectionLog.Info("Tool window opened (modeless, owner handle "
                + (revitMainWindowHandle == IntPtr.Zero ? "none" : revitMainWindowHandle.ToString()) + ").");
        }
    }
}
