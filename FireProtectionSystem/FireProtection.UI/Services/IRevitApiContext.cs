using System;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Revit-independent contract for "run this work where the Revit API is legal".
    /// <para>
    /// The tool window is modeless so Revit stays usable while it is open. A modeless window's WPF
    /// event handlers are NOT a valid Revit API context, so anything that reads the Document or opens
    /// a Transaction has to be handed back to Revit and executed when Revit is idle. The Backend
    /// implementation does that with an <c>IExternalEventHandler</c>; this interface keeps the UI free
    /// of Revit references.
    /// </para>
    /// </summary>
    public interface IRevitApiContext
    {
        /// <summary>
        /// Queues <paramref name="work"/> to run in a valid Revit API context and returns immediately.
        /// <para>
        /// Fire-and-forget by design: the queued work runs on the same Revit main UI thread that owns
        /// this window, so blocking here to wait for a result would deadlock. Because it is the same
        /// thread, <paramref name="work"/> may touch ViewModels/bindings directly.
        /// </para>
        /// </summary>
        /// <param name="onError">Invoked (also in the API context) if <paramref name="work"/> throws.</param>
        void Run(Action work, Action<Exception> onError = null);
    }

    /// <summary>
    /// Pass-through context: runs the work inline on the calling thread. Used by tests, the standalone
    /// catalog host and the WPF designer, where there is no Revit to marshal onto.
    /// </summary>
    public sealed class ImmediateRevitApiContext : IRevitApiContext
    {
        public void Run(Action work, Action<Exception> onError = null)
        {
            if (work == null) return;

            try
            {
                work();
            }
            catch (Exception ex)
            {
                if (onError == null) throw;
                onError(ex);
            }
        }
    }

    /// <summary>
    /// Ambient accessor for the current <see cref="IRevitApiContext"/>.
    /// <para>
    /// Deliberately static: the context is a per-Revit-session singleton and every ViewModel that owns
    /// a Revit-touching flow would otherwise need it threaded through several constructor overloads.
    /// Defaults to <see cref="ImmediateRevitApiContext"/> so non-Revit hosts keep working unchanged.
    /// </para>
    /// </summary>
    public static class RevitApi
    {
        private static IRevitApiContext _context = new ImmediateRevitApiContext();

        /// <summary>Never null; assigning null restores the immediate (pass-through) context.</summary>
        public static IRevitApiContext Context
        {
            get { return _context; }
            set { _context = value ?? new ImmediateRevitApiContext(); }
        }

        /// <summary>Shorthand for <c>Context.Run(work, onError)</c>.</summary>
        public static void Run(Action work, Action<Exception> onError = null)
        {
            Context.Run(work, onError);
        }
    }
}
