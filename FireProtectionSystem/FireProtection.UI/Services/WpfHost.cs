using System;
using System.Windows;
using System.Windows.Threading;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Hosts a single WPF <see cref="Application"/> for the lifetime of the Revit session.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// Revit is not a WPF application: it never creates a
    /// <see cref="System.Windows.Application"/>. Showing a WPF window from a Revit add-in without
    /// creating one produces two failures, both observed in the field:
    ///
    /// 1. <c>Application.Current</c> is <c>null</c>, so any code that dereferences
    ///    <c>Application.Current.Dispatcher</c> throws <see cref="NullReferenceException"/>. That bug
    ///    was latent in <c>MainWindowViewModel.LoadFamilies</c>.
    ///
    /// 2. WPF's dispatcher lifecycle is tied to the <see cref="Application"/> object. With no
    ///    Application holding it, the garbage collector can finalise the dispatcher once the tool
    ///    window closes, tearing down the activation context underneath Revit's own UI thread. Revit
    ///    then fails on the NEXT ribbon interaction (or on shutdown) with:
    ///
    ///      System.Runtime.InteropServices.SEHException
    ///      HResult = 0x80004005
    ///      Message = "External component has thrown an exception."
    ///      at ...Dispose(Boolean disposing) -> DeactivateActCtx(...)
    ///
    ///    That message is a MASK. The real fault happens earlier and is swallowed; the SEHException
    ///    is only what surfaces during teardown.
    ///
    /// THE TWO RULES
    /// -------------
    ///   a) Keep a STATIC strong reference to the Application. Without one the GC finalises it and
    ///      the very failure above comes back.
    ///   b) Use <see cref="ShutdownMode.OnExplicitShutdown"/>. The default
    ///      <see cref="ShutdownMode.OnLastWindowClose"/> would tear the dispatcher down as soon as
    ///      the tool window closes, which breaks every later ribbon click in that Revit session.
    /// </summary>
    public static class WpfHost
    {
        private static Application _application;
        private static readonly object Gate = new object();

        /// <summary>True once a WPF Application has been established for this session.</summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>
        /// Creates the WPF Application if Revit has not already done so. Safe to call repeatedly and
        /// safe to call more than once: the first caller wins.
        ///
        /// MUST be called on the thread that will show the windows (Revit's UI thread) and BEFORE
        /// the first <see cref="Window"/> is constructed, because WPF resolves implicit resource
        /// dictionaries and theme data through the ambient Application.
        /// </summary>
        public static void EnsureApplication()
        {
            if (IsInitialized) return;

            lock (Gate)
            {
                if (IsInitialized) return;

                // Revit (or a test host) may already have created one.
                if (Application.Current != null)
                {
                    IsInitialized = true;
                    return;
                }

                try
                {
                    _application = new Application
                    {
                        // NEVER OnLastWindowClose: closing the tool window must not shut down the
                        // dispatcher Revit is still using.
                        ShutdownMode = ShutdownMode.OnExplicitShutdown
                    };

                    // WPF swallows binding and converter exceptions by default; they then resurface
                    // as an unrelated SEHException during teardown. Log them where they happen.
                    _application.Dispatcher.UnhandledException += delegate (object sender, DispatcherUnhandledExceptionEventArgs e)
                    {
                        FireProtectionLog.Error("UNHANDLED UI dispatcher exception (binding/converter?).", e.Exception);
                        // Deliberately NOT setting e.Handled: swallowing it here is what hides the
                        // fault in the first place. It is logged, then allowed to surface.
                    };

                    IsInitialized = true;

                    FireProtectionLog.Info(
                        "WPF Application established (ShutdownMode.OnExplicitShutdown) and pinned for the session.");
                }
                catch (Exception ex)
                {
                    // An Application that cannot be created is not fatal on its own — the window may
                    // still show. Record it loudly so a later failure is attributable.
                    IsInitialized = false;
                    FireProtectionLog.Error(
                        "Could not create the WPF Application; WPF may be running without one.", ex);
                }
            }
        }

        /// <summary>
        /// The dispatcher to marshal UI work onto, or null when none can be resolved.
        ///
        /// Falls back to <see cref="Dispatcher.CurrentDispatcher"/> ONLY when an Application already
        /// exists. Calling <c>CurrentDispatcher</c> on a thread with no dispatcher would CREATE one
        /// and attach an activation context to that thread, which is precisely what produces the
        /// <c>DeactivateActCtx</c> SEHException when that thread later ends.
        /// </summary>
        public static Dispatcher UiDispatcher
        {
            get
            {
                try
                {
                    Application app = Application.Current;
                    if (app != null) return app.Dispatcher;
                    return null;
                }
                catch (Exception ex)
                {
                    FireProtectionLog.Warn("UI dispatcher lookup failed: " + ex.Message);
                    return null;
                }
            }
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the UI thread when a dispatcher is available, otherwise
        /// runs it inline. Never throws: a failed UI marshal must not break the caller's flow.
        /// </summary>
        public static void RunOnUi(Action action)
        {
            if (action == null) return;

            Dispatcher dispatcher = UiDispatcher;
            if (dispatcher == null)
            {
                try { action(); }
                catch (Exception ex) { FireProtectionLog.Warn("Inline UI action failed: " + ex.Message); }
                return;
            }

            try
            {
                if (dispatcher.CheckAccess()) action();
                else dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
            }
            catch (Exception ex)
            {
                FireProtectionLog.Warn("UI dispatch failed: " + ex.Message);
            }
        }
    }
}