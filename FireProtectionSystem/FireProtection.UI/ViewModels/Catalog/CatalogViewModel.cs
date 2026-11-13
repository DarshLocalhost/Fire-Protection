using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Holds ONLY the Excel workbook, never the model-backed catalog.
        ///
        /// Deliberately a different cell from <see cref="_holder"/>. The model catalog reads the
        /// workbook as a value OVERLAY; if it read the shared holder it would find ITSELF in model
        /// mode and recurse indefinitely (GetSprinklerEntry -> Overlay -> itself). A separate
        /// holder makes that impossible by construction rather than by a fragile runtime guard.
        /// </summary>
        private readonly CatalogHolder _workbookHolder;

        private ICatalog _catalog;
        private string _lastError;
        private bool _isLoading;
        private CatalogSourceMode _sourceMode = CatalogSourceModes.Default;
        private string _rememberedCatalogPath;

        /// <summary>
        /// Supplies a model-backed <see cref="ICatalog"/> for <see cref="CatalogSourceMode.RevitModel"/>.
        /// Injected by the Backend, which is the only layer that can read the Revit document. Null
        /// in the designer / test host, in which case model mode reports that no families were found.
        /// </summary>
        private Func<ICatalog> _modelCatalogFactory;

        public CatalogViewModel()
            : this(null, null)
        {
        }

        public CatalogViewModel(Func<string, ICatalog> loader)
            : this(loader, null)
        {
        }

        public CatalogViewModel(Func<string, ICatalog> loader, CatalogHolder holder)
            : this(loader, holder, holder, null)
        {
        }

        public CatalogViewModel(
            Func<string, ICatalog> loader,
            CatalogHolder holder,
            Func<ICatalog> modelCatalogFactory)
            : this(loader, holder, holder, modelCatalogFactory)
        {
        }

        public CatalogViewModel(
            Func<string, ICatalog> loader,
            CatalogHolder holder,
            CatalogHolder workbookHolder,
            Func<ICatalog> modelCatalogFactory)
        {
            _loader = loader;
            _holder = holder;
            _workbookHolder = workbookHolder ?? holder;
            _modelCatalogFactory = modelCatalogFactory;

            // Restore the persisted choice. Doing this in the constructor means the three tabs
            // build their dropdowns from the correct source on first paint, with no flash of the
            // wrong source and no need for the caller to re-trigger a reload.
            CatalogSettings settings = CatalogSettingsStore.Load();
            _sourceMode = settings.SourceMode;
            _rememberedCatalogPath = settings.LastCatalogPath;

            ApplyPersistedSource();
        }

        /// <summary>
        /// Releases references into the Revit host: the model-catalog factory (a closure over the
        /// active <c>Document</c>) and the active catalog.
        ///
        /// Called when the tool window closes. Without this the closed window's object graph — and
        /// therefore the Revit <c>Document</c> captured by the factory closure — stays reachable
        /// from the UI layer, which keeps a document alive across add-in unload and is a known
        /// contributor to teardown failures. Revit add-ins should not hold Document references
        /// beyond the command that opened them.
        /// </summary>
        public void ReleaseHostReferences()
        {
            _modelCatalogFactory = null;
            SetCatalog(null, false);
        }

        /// <summary>
        /// Builds the catalog implied by the persisted mode so the FIRST paint is already correct.
        /// Failures are swallowed deliberately: an unreadable workbook or an unavailable model must
        /// never stop the window from opening — the status banner reports the problem instead.
        /// </summary>
        private void ApplyPersistedSource()
        {
            if (_sourceMode == CatalogSourceMode.RevitModel)
            {
                SetCatalog(BuildModelCatalog(), false);
                return;
            }

            if (_loader != null && !string.IsNullOrEmpty(_rememberedCatalogPath)
                && File.Exists(_rememberedCatalogPath))
            {
                string ignored;
                TryLoadInternal(_rememberedCatalogPath, out ignored);
            }
        }

        /// <summary>
        /// Which source the family/type lists come from. Persisted immediately on change so the
        /// user does not re-pick it every session.
        /// </summary>
        public CatalogSourceMode SourceMode
        {
            get { return _sourceMode; }
            private set
            {
                if (!SetProperty(ref _sourceMode, value)) return;
                OnPropertyChanged(nameof(IsCatalogFileMode));
                OnPropertyChanged(nameof(DisplayHeader));
                OnPropertyChanged(nameof(HasStatusMessage));
                PersistSettings();
            }
        }

        /// <summary>True when the file Browse/Reload controls should be visible.</summary>
        public bool IsCatalogFileMode { get { return SourceMode == CatalogSourceMode.CatalogFile; } }

        /// <summary>
        /// Explains the consequence of the current source, so a missing engineering value is never
        /// silent. Without this the engine quietly falls back to provisional hazard-class spacing
        /// when a model family has no workbook row, and the user has no way to know.
        /// </summary>
        public string StatusMessage
        {
            get
            {
                if (HasError) return LastError;

                if (SourceMode == CatalogSourceMode.RevitModel)
                {
                    if (string.IsNullOrEmpty(RememberedCatalogPath))
                    {
                        return "Families and types are read from the open model. No catalog file is loaded, "
                            + "so every per-type value is provisional and must be reviewed.";
                    }
                    if (!IsCatalogLoadedOverlay)
                    {
                        return "Families and types are read from the open model, and per-type values are read "
                            + "from " + System.IO.Path.GetFileName(RememberedCatalogPath) + ". Any family without a "
                            + "catalog row falls back to provisional values.";
                    }
                    return "Families and types are read from the open model, with per-type values overlaid from "
                        + System.IO.Path.GetFileName(RememberedCatalogPath) + ".";
                }

                if (IsLoaded)
                {
                    return "Families and types are read from " + System.IO.Path.GetFileName(SourcePath) + ".";
                }
                return "No catalog file loaded. Browse to select a workbook.";
            }
        }

        public bool HasStatusMessage { get { return true; } }

        /// <summary>Path offered when switching into CatalogFile mode.</summary>
        public string RememberedCatalogPath
        {
            get { return _rememberedCatalogPath; }
            private set
            {
                if (SetProperty(ref _rememberedCatalogPath, value))
                {
                    OnPropertyChanged(nameof(StatusMessage));
                }
            }
        }

        private bool IsCatalogLoadedOverlay
        {
            get { return _catalog != null && _catalog.IsLoaded && _catalog.TotalRowCount > 0; }
        }

        /// <summary>
        /// Switches source and rebuilds the active catalog.
        ///
        /// Switching INTO model mode always succeeds (the model is always readable) — it never
        /// discards the remembered workbook, because the workbook is still used as a value overlay
        /// in model mode. Switching INTO file mode auto-loads the remembered path when there is
        /// one, so the choice is immediately usable rather than resetting to an empty list.
        /// </summary>
        public bool TrySetSourceMode(CatalogSourceMode mode, out string error)
        {
            error = null;

            if (mode == SourceMode) return true;

            CatalogSourceMode previous = SourceMode;

            if (mode == CatalogSourceMode.RevitModel)
            {
                ICatalog modelCatalog = BuildModelCatalog();
                if (modelCatalog == null)
                {
                    error = "Could not read families from the open model.";
                    LastError = error;
                    return false;
                }
                SourceMode = mode;
                SetCatalog(modelCatalog, false);
                LastError = null;
                PersistSettings();
                return true;
            }

            // CatalogFile mode: try the remembered workbook first so the user is not dropped
            // into an empty dropdown list with no explanation.
            if (!string.IsNullOrEmpty(RememberedCatalogPath) && File.Exists(RememberedCatalogPath))
            {
                string loadError;
                if (TryLoadInternal(RememberedCatalogPath, out loadError))
                {
                    SourceMode = mode;
                    PersistSettings();
                    return true;
                }
                // Fall through: the remembered file failed, but the user still asked for file
                // mode. Enter the mode with the error visible rather than silently reverting.
                SourceMode = mode;
                PersistSettings();
                error = loadError;
                return true;
            }

            SourceMode = mode;
            SetCatalog(null, false);
            LastError = null;
            PersistSettings();
            return true;
        }

        private ICatalog BuildModelCatalog()
        {
            if (_modelCatalogFactory == null) return null;
            try
            {
                return _modelCatalogFactory();
            }
            catch (Exception ex)
            {
                LastError = "Reading families from the model failed: " + ex.Message;
                return null;
            }
        }

        private void PersistSettings()
        {
            CatalogSettingsStore.Save(new CatalogSettings
            {
                SourceMode = SourceMode,
                LastCatalogPath = RememberedCatalogPath
            });
        }

        /// <summary>
        /// Sets the ACTIVE catalog and notifies every listener (the three tabs repopulate their
        /// dropdowns from these notifications). The active catalog is what the Revit family
        /// sources read for mount resolution.
        ///
        /// <paramref name="isWorkbook"/> distinguishes the two holders: a loaded workbook is
        /// written to BOTH the active holder and the workbook-only holder, so the model catalog can
        /// keep overlaying values after the user switches the radio to Model. A model catalog is
        /// written to the active holder ONLY — writing it to the workbook holder would make it its
        /// own overlay and recurse.
        /// </summary>
        private void SetCatalog(ICatalog value, bool isWorkbook)
        {
            Catalog = value;
            if (isWorkbook && _workbookHolder != null) _workbookHolder.Current = value;
            OnPropertyChanged(nameof(IsLoaded));
            OnPropertyChanged(nameof(CatalogVersion));
            OnPropertyChanged(nameof(SourcePath));
            OnPropertyChanged(nameof(TotalRowCount));
            OnPropertyChanged(nameof(DisplayHeader));
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(RememberedCatalogPath));
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
                if (SourceMode == CatalogSourceMode.RevitModel)
                {
                    int nSprinkler = _catalog != null ? CountSprinklerFamilies() : 0;
                    int nDevice = _catalog != null ? CountDeviceFamilies() : 0;
                    if (nSprinkler == 0 && nDevice == 0) return "Model: (no families loaded)";

                    string overlayNote = IsCatalogLoadedOverlay
                        ? "  + catalog " + _catalog.CatalogVersion
                        : "  (provisional values)";
                    return string.Format("Model: {0} sprinkler / {1} device famil{2}{3}",
                        nSprinkler, nDevice, nDevice == 1 ? "y" : "ies", overlayNote);
                }

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

        private int CountSprinklerFamilies()
        {
            try
            {
                IReadOnlyList<string> f = _catalog.GetSprinklerFamilies();
                return f == null ? 0 : f.Count;
            }
            catch { return 0; }
        }

        private int CountDeviceFamilies()
        {
            try
            {
                IReadOnlyList<string> f = _catalog.GetSmokeDetectorFamilies();
                return f == null ? 0 : f.Count;
            }
            catch { return 0; }
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

            // Loading a file is an explicit act by the user, so it also selects CatalogFile mode.
            // Otherwise the file would load and immediately be ignored, because the dropdowns
            // would still be bound to whatever the radio group had selected.
            SourceMode = CatalogSourceMode.CatalogFile;

            bool ok = TryLoadInternal(path, out error);
            if (ok) RememberedCatalogPath = path;
            return ok;
        }

        private bool TryLoadInternal(string path, out string error)
        {
            error = null;
            try
            {
                IsLoading = true;
                ICatalog loaded = _loader(path);
                Catalog = loaded;
                if (_workbookHolder != null) _workbookHolder.Current = loaded;
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
            if (SourceMode == CatalogSourceMode.RevitModel)
            {
                // "Reload" in model mode means re-read the document, which is what the user wants
                // after loading families into the project.
                ICatalog modelCatalog = BuildModelCatalog();
                if (modelCatalog == null)
                {
                    error = "Could not read families from the open model.";
                    LastError = error;
                    return false;
                }
                SetCatalog(modelCatalog, false);
                LastError = null;
                error = null;
                return true;
            }

            if (string.IsNullOrEmpty(SourcePath))
            {
                error = "No catalog file is currently loaded.";
                LastError = error;
                return false;
            }
            return TryLoadInternal(SourcePath, out error);
        }
    }
}
