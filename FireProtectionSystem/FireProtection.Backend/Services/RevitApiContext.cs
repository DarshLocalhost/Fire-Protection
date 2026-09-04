using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using FireProtection.UI.Services;

namespace FireProtection.Backend.Services
{
    /// <summary>
    /// Revit-side implementation of <see cref="IRevitApiContext"/>.
    /// <para>
    /// The tool window is modeless (Revit stays usable while it is open), so WPF handlers are not a
    /// valid Revit API context. Work queued through <see cref="Run"/> is drained inside
    /// <see cref="Execute"/>, which Revit calls on its main thread in a context where reading the
    /// Document and opening Transactions is legal.
    /// </para>
    /// </summary>
    public sealed class RevitApiContext : IRevitApiContext, IExternalEventHandler
    {
        private readonly object _gate = new object();
        private readonly Queue<QueuedWork> _queue = new Queue<QueuedWork>();
        private ExternalEvent _externalEvent;

        private RevitApiContext()
        {
        }

        /// <summary>
        /// Creates the context and publishes it as <see cref="RevitApi.Context"/>.
        /// MUST be called from a valid Revit API context (e.g. inside an ExternalCommand's Execute) —
        /// <c>ExternalEvent.Create</c> is only legal there.
        /// </summary>
        public static RevitApiContext CreateAndRegister()
        {
            RevitApiContext context = new RevitApiContext();
            context._externalEvent = ExternalEvent.Create(context);
            RevitApi.Context = context;
            return context;
        }

        /// <summary>
        /// Queues the work and asks Revit for an API context. Returns immediately: the handler runs on
        /// the Revit main UI thread that also owns the window, so waiting here would deadlock.
        /// </summary>
        public void Run(Action work, Action<Exception> onError = null)
        {
            if (work == null) return;

            lock (_gate)
            {
                _queue.Enqueue(new QueuedWork(work, onError));
            }

            ExternalEvent externalEvent = _externalEvent;
            if (externalEvent != null) externalEvent.Raise();
        }

        /// <summary>
        /// Drains everything queued so far. Revit coalesces multiple <c>Raise()</c> calls into one
        /// Execute, so this must loop rather than handle a single item.
        /// </summary>
        public void Execute(UIApplication app)
        {
            while (true)
            {
                QueuedWork item;
                lock (_gate)
                {
                    if (_queue.Count == 0) return;
                    item = _queue.Dequeue();
                }

                try
                {
                    item.Work();
                }
                catch (Exception ex)
                {
                    // One failing item must not abort the rest of the queue.
                    if (item.OnError != null)
                    {
                        try { item.OnError(ex); } catch { }
                    }
                }
            }
        }

        public string GetName()
        {
            return "Fire Protection Revit API context";
        }

        private sealed class QueuedWork
        {
            public QueuedWork(Action work, Action<Exception> onError)
            {
                Work = work;
                OnError = onError;
            }

            public Action Work { get; private set; }

            public Action<Exception> OnError { get; private set; }
        }
    }
}
