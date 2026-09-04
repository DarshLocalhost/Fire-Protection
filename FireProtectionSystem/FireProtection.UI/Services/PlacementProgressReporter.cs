using System;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// UI-side <see cref="IPlacementProgress"/>: forwards per-room progress to a ViewModel callback, pumps the
    /// dispatcher so the window repaints and the Cancel button stays clickable, and exposes a cancel flag the
    /// Backend polls between rooms.
    /// </summary>
    public sealed class PlacementProgressReporter : IPlacementProgress
    {
        private readonly Action<int, int, string> _onProgress;
        private bool _cancelRequested;

        public PlacementProgressReporter(Action<int, int, string> onProgress)
        {
            _onProgress = onProgress;
        }

        public bool IsCancellationRequested
        {
            get { return _cancelRequested; }
        }

        /// <summary>Called from the Cancel command. Takes effect at the next room boundary.</summary>
        public void RequestCancel()
        {
            _cancelRequested = true;
        }

        public void Report(int completed, int total, string label)
        {
            if (_onProgress != null)
            {
                try { _onProgress(completed, total, label); }
                catch (Exception) { /* progress display must not fail a run */ }
            }

            Dialogs.PumpUi();
        }
    }
}
