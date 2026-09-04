using System;
using System.Windows;
using System.Windows.Interop;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Owner resolution for the tool's modal windows.
    /// <para>
    /// WPF only accepts a Window that has already been shown (i.e. that owns an HWND) as an
    /// <see cref="Window.Owner"/>; anything else throws "Cannot set Owner property to a Window that has
    /// not been shown previously". Under Revit, <c>Application.Current.MainWindow</c> is exactly that
    /// illegal case - a wrapper Revit never showed through WPF, because Revit's real main window is a
    /// native HWND. The tool window is the one window we know WPF has shown, so it is the owner to use.
    /// </para>
    /// </summary>
    public static class DialogOwner
    {
        /// <summary>
        /// Sets <paramref name="dialog"/>'s owner to the first usable candidate: the requested owner, then
        /// the tool window. When neither can own it, the dialog is centred on screen instead - without an
        /// owner, <c>WindowStartupLocation="CenterOwner"</c> silently degrades to the top-left corner.
        /// </summary>
        public static void Apply(Window dialog, Window requestedOwner)
        {
            if (dialog == null) return;

            Window owner = Showable(requestedOwner) ?? Showable(Dialogs.Owner);
            if (owner != null && !ReferenceEquals(owner, dialog))
            {
                try
                {
                    dialog.Owner = owner;
                    return;
                }
                catch (InvalidOperationException ex)
                {
                    FireProtectionLog.Warn("Could not set dialog owner - " + ex.Message + " (continuing without one).");
                }
            }

            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        /// <summary>
        /// The candidate if it can legally own a dialog, otherwise null. Reading
        /// <see cref="WindowInteropHelper.Handle"/> does not create a handle, so this is a safe test.
        /// </summary>
        private static Window Showable(Window candidate)
        {
            if (candidate == null) return null;
            try
            {
                return new WindowInteropHelper(candidate).Handle != IntPtr.Zero ? candidate : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
}
