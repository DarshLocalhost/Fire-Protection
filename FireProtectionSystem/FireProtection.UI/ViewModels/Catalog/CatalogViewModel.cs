using System;
using System.IO;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Common;

namespace FireProtection.UI.ViewModels.Catalog
{
    /// <summary>
    /// Owns the loaded <see cref="ICatalog"/> instance and the user-driven load / reload actions.
    /// The catalog file path is session-scoped (the user picks it each session).
    /// </summary>
    public class CatalogViewModel : ObservableObject
    {
        private readonly Func<string, ICatalog> _loader;
        private readonly CatalogHolder _holder;
        private ICatalog _catalog;
        private string _lastError;
        private bool _isLoading;

        public CatalogViewModel()
            : this(null, null)
        {
        }

        public CatalogViewModel(Func<string, ICatalog> loader)
            : this(loader, null)
        {
        }

        /// <summary>
        /// When <paramref name="holder"/> is supplied, this viewmodel writes the
        /// loaded catalog into <see cref="CatalogHolder.Current"/> on every
        /// successful <see cref="TryLoad"/>. The Revit-aware
        /// <c>RevitSprinklerFamilySource</c> reads the holder lazily so its
        /// mount-lookup sees the user's current catalog at call time, not the
        /// catalog that existed at startup (which is usually <c>null</c> —
        /// the user picks the file after the add-in starts).
        /// </summary>
        public CatalogViewModel(Func<string, ICatalog> loader, CatalogHolder holder)
        {
            _loader = loader;
            _holder = holder;
        }

        public ICatalog Catalog
        {
            get { return _catalog; }
            private set
            {
                if (SetProperty(ref _catalog, value))
                {
                    OnPropertyChanged(nameof(IsLoaded));
                    OnPropertyChanged(nameof(CatalogVersion));
                    OnPropertyChanged(nameof(SourcePath));
                    OnPropertyChanged(nameof(TotalRowCount));
                    OnPropertyChanged(nameof(DisplayHeader));
                    if (_holder != null) _holder.Current = value;
                }
            }
        }

        public bool IsLoaded
        {
            get { return _catalog != null && _catalog.IsLoaded; }
        }

        public string CatalogVersion
        {
            get { return _catalog == null ? null : _catalog.CatalogVersion; }
        }

        public string SourcePath
        {
            get { return _catalog == null ? null : _catalog.SourcePath; }
        }

        public int TotalRowCount
        {
            get { return _catalog == null ? 0 : _catalog.TotalRowCount; }
        }

        public string DisplayHeader
        {
            get
            {
                if (!IsLoaded) return "Catalog: (not loaded)";
                string name = string.IsNullOrEmpty(_catalog.SourcePath)
                    ? "<unknown>"
                    : Path.GetFileName(_catalog.SourcePath);
                string ver = string.IsNullOrEmpty(_catalog.CatalogVersion) ? "?" : _catalog.CatalogVersion;
                int n = _catalog.TotalRowCount;
                return string.Format("Catalog: {0}  v{1}  ({2} row{3})",
                    name, ver, n, n == 1 ? string.Empty : "s");
            }
        }

        public string LastError
        {
            get { return _lastError; }
            private set
            {
                if (SetProperty(ref _lastError, value))
                {
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }

        public bool HasError
        {
            get { return !string.IsNullOrEmpty(_lastError); }
        }

        public bool IsLoading
        {
            get { return _isLoading; }
            private set { SetProperty(ref _isLoading, value); }
        }

        public bool TryLoad(string path, out string error)
        {
            error = null;
            if (_loader == null)
            {
                error = "Catalog loader is not configured.";
                LastError = error;
                return false;
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Catalog path is empty.";
                LastError = error;
                return false;
            }
            try
            {
                IsLoading = true;
                ICatalog loaded = _loader(path);
                Catalog = loaded;
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                error = Describe(ex);
                LastError = error;
                return false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Builds a self-describing message: exception type + message + the full inner-exception
        /// chain. Plain <c>ex.Message</c> is not enough for the failures this path actually hits —
        /// a missing ClosedXML runtime dependency surfaces as a <c>TypeInitializationException</c>
        /// ("The type initializer for X threw an exception") or a <c>TargetInvocationException</c>
        /// whose only useful text (the assembly that could not be loaded) lives in the inner
        /// exception. Without the chain the popup names no cause and cannot be acted on.
        /// </summary>
        private static string Describe(Exception ex)
        {
            if (ex == null) return "<unknown error>";

            string text = ex.GetType().Name + ": " + ex.Message;
            Exception inner = ex.InnerException;
            int depth = 0;
            while (inner != null && depth < 5)
            {
                text += Environment.NewLine + "  -> " + inner.GetType().Name + ": " + inner.Message;
                inner = inner.InnerException;
                depth++;
            }
            return text;
        }

        public bool TryReload(out string error)
        {
            if (string.IsNullOrEmpty(SourcePath))
            {
                error = "No catalog is currently loaded.";
                LastError = error;
                return false;
            }
            return TryLoad(SourcePath, out error);
        }
    }
}
