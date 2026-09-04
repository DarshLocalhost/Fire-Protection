using System;
using System.Windows;
using System.Windows.Threading;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Owner-aware message boxes. The tool window is modeless now, so an ownerless MessageBox can be shown
    /// *behind* the Revit window — the user sees a frozen ribbon and no dialog. Every dialog raised from the
    /// tool must therefore name the tool window as its owner.
    /// </summary>
    public static class Dialogs
    {
        /// <summary>The tool window, or null before it opens / after it closes.</summary>
        public static Window Owner
        {
            get
            {
                Window window = UiLauncher.CurrentWindow;
                if (window == null) return null;
                return window.IsLoaded ? window : null;
            }
        }

        public static MessageBoxResult Show(
            string message,
            string caption,
            MessageBoxButton button = MessageBoxButton.OK,
            MessageBoxImage image = MessageBoxImage.Information)
        {
            Window owner = Owner;
            return owner != null
                ? MessageBox.Show(owner, message, caption, button, image)
                : MessageBox.Show(message, caption, button, image);
        }

        public static bool Confirm(string message, string caption)
        {
            return Show(message, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        /// <summary>
        /// Lets pending render/input work run. Placement executes on Revit's own UI thread — the same thread
        /// that paints this window — so without this a long run shows a stale, apparently hung window and the
        /// Cancel click is never delivered.
        /// </summary>
        public static void PumpUi()
        {
            try
            {
                Dispatcher dispatcher = Application.Current != null
                    ? Application.Current.Dispatcher
                    : Dispatcher.CurrentDispatcher;

                if (dispatcher == null) return;
                dispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
            }
            catch (Exception)
            {
                // Pumping is cosmetic; never let it break a placement run.
            }
        }
    }
}
