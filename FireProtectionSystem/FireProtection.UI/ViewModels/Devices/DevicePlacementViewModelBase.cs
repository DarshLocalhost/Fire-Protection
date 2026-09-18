using FireProtection.UI.Models;
using FireProtection.UI.Models.Sprinklers.BruteForce;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Common;
using FireProtection.UI.ViewModels.Catalog;
using FireProtection.UI.ViewModels.Devices;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using FireProtection.UI.Views.Common;

namespace FireProtection.UI.ViewModels.Devices
{
    /// <summary>
    /// Device-agnostic base for the "pick rooms/levels + family/type, then place" workflow. Shared by
    /// smoke detectors and notification appliances so the common UI logic lives in exactly one place.
    /// Placement itself is routed through the <see cref="IDevicePlacementExecutor"/> seam — wired to the
    /// real Backend device core under Revit; with no executor (tests, designer, standalone catalog host)
    /// the tab stays selection-only via <see cref="IsBackendPending"/>.
    ///
    /// Sprinkler files are intentionally NOT modified; the existing SprinklerBruteForceViewModel keeps its
    /// own (hazard-class-aware) implementation. This base is what the new device tabs inherit.
    /// </summary>
    public abstract class DevicePlacementViewModelBase : ObservableObject
    {
        private readonly IDeviceFamilySource _deviceFamilySource;
        private readonly IDevicePlacementExecutor _deviceExecutor;
        private readonly CatalogViewModel _catalogVm;

        private string _levelSearchText;
        private string _roomSearchText;
        private bool _hideUnselectedLevels;
        private bool _hideUnselectedRooms;
        private bool _showLevelsWithRoomsOnly;
        private bool _isPlacementRunning;
        private bool _canCancelPlacement;
        private string _placementProgressText;
        private PlacementProgressReporter _progressReporter;
        private string _selectedExistingDevicePolicyLabel = ExistingDevicePolicyOptions.SkipRoomLabel;
        private string _placementStatusMessage;

        // Eligibility preflight (Decision 016), ported from the sprinkler VM. _isEligibilityRefreshing gates
        // placement while a pass is in flight; _suppressEligibilityRefresh coalesces a storm of setter
        // callbacks (construction, catalog reload, bulk edit, level popover) into a single pass;
        // _showEligibleRoomsOnly is the "Eligible rooms only" room-list filter.
        private bool _isEligibilityRefreshing;
        private bool _suppressEligibilityRefresh;
        private bool _showEligibleRoomsOnly;

        private DeviceFamilyOption _selectedDeviceFamily;
        private DeviceTypeOption _selectedDeviceType;

        protected DevicePlacementViewModelBase(
            FireProtectionUiData data,
            IDeviceFamilySource deviceFamilySource = null,
            IDevicePlacementExecutor deviceExecutor = null,
            CatalogViewModel catalogViewModel = null)
        {
            Data = data;
            _deviceFamilySource = deviceFamilySource;
            _deviceExecutor = deviceExecutor;
            _catalogVm = catalogViewModel;

            // Stay suppressed through the whole ctor so the construction storm (family/type auto-select,
            // per-row seeding, and the concrete ctor's attribute seeding) does not fire a preflight per
            // callback. The concrete ctor fires exactly one initial pass via InitializeEligibility() once
            // its own attribute defaults are seeded.
            _suppressEligibilityRefresh = true;

            Levels = new ObservableCollection<DeviceLevelItemViewModel>();
            AllRooms = new ObservableCollection<DeviceRoomItemViewModel>();
            DeviceFamilies = new ObservableCollection<DeviceFamilyOption>();
            DeviceTypes = new ObservableCollection<DeviceTypeOption>();
            RunLogEntries = new ObservableCollection<RunLogEntry>();

            if (data != null && data.Levels != null)
            {
                foreach (LevelUiData level in data.Levels)
                {
                    DeviceLevelItemViewModel levelVm = new DeviceLevelItemViewModel(level);
                    levelVm.SelectionChanged += OnLevelSelectionChanged;

                    foreach (DeviceRoomItemViewModel room in levelVm.Rooms)
                    {
                        room.SelectionChanged += OnRoomSelectionChanged;
                        AllRooms.Add(room);
                    }
                    Levels.Add(levelVm);
                }
            }

            LoadDeviceFamilies();
            SeedPerRowDeviceDefaults();

            if (_catalogVm != null)
            {
                _catalogVm.PropertyChanged += OnCatalogViewModelPropertyChanged;
            }

            LevelsView = CollectionViewSource.GetDefaultView(Levels);
            LevelsView.Filter = FilterLevel;

            RoomsView = CollectionViewSource.GetDefaultView(AllRooms);
            RoomsView.Filter = FilterRoom;
            RoomsView.SortDescriptions.Add(new SortDescription(nameof(DeviceRoomItemViewModel.LevelName), ListSortDirection.Ascending));
            RoomsView.SortDescriptions.Add(new SortDescription(nameof(DeviceRoomItemViewModel.Number), ListSortDirection.Ascending));

            ToggleHideUnselectedLevelsCommand = new RelayCommand(_ => HideUnselectedLevels = !HideUnselectedLevels);
            ToggleHideUnselectedRoomsCommand = new RelayCommand(_ => HideUnselectedRooms = !HideUnselectedRooms);

            ToggleSelectAllLevelsCommand = new RelayCommand(_ => ToggleSelectAllLevels());
            ToggleSelectAllRoomsCommand = new RelayCommand(_ => ToggleSelectAllRooms());

            foreach (DeviceLevelItemViewModel level in Levels)
                level.PropertyChanged += OnLevelItemPropertyChanged;
            foreach (DeviceRoomItemViewModel room in AllRooms)
                room.PropertyChanged += OnRoomItemPropertyChanged;

            PlaceDevicesCommand = new RelayCommand(
                _ => ExecutePlaceDevices(),
                _ => CanExecutePlaceDevices());

            ResetCommand = new RelayCommand(_ => Reset());

            CancelPlacementCommand = new RelayCommand(
                _ => ExecuteCancelPlacement(),
                _ => CanExecuteCancelPlacement());

            ApplyFamilyToSelectedCommand = new RelayCommand(
                _ => ApplyUniversalFamilyTypeToSelected(),
                _ => CanBulkEdit());

            ClearRoomOverridesForSelectedCommand = new RelayCommand(
                _ => ClearOverridesForSelected(),
                _ => CanBulkEdit());

            ClearRunLogCommand = new RelayCommand(_ => ClearRunLog());

            ApplyDefaultSelection();

            OnPropertyChanged(nameof(AreAllSelectableLevelsSelected));
            OnPropertyChanged(nameof(LevelSelectionToggleLabel));
            OnPropertyChanged(nameof(AreAllSelectableRoomsSelected));
            OnPropertyChanged(nameof(RoomSelectionToggleLabel));

            if (_deviceExecutor == null)
            {
                PlacementStatusMessage = "Device placement backend is pending — room/level selection is for layout planning only.";
            }
        }

        /// <summary>Human-readable device name shown in the configuration header (e.g. "SMOKE DETECTOR CONFIGURATION").</summary>
        public abstract string DeviceDisplayName { get; }

        /// <summary>True when no device placement backend is wired yet (UI-first slice). Disables placement.</summary>
        public bool IsBackendPending => _deviceExecutor == null;

        protected abstract FireProtection.UI.Services.DeviceKind TabDeviceKind { get; }

        public FireProtectionUiData Data { get; }

        public ObservableCollection<DeviceLevelItemViewModel> Levels { get; }
        public ObservableCollection<DeviceRoomItemViewModel> AllRooms { get; }

        public ObservableCollection<DeviceFamilyOption> DeviceFamilies { get; }
        public ObservableCollection<DeviceTypeOption> DeviceTypes { get; }
        public ObservableCollection<RunLogEntry> RunLogEntries { get; }

        public ICollectionView LevelsView { get; }
        public ICollectionView RoomsView { get; }

        public string LevelSearchText
        {
            get => _levelSearchText;
            set { if (SetProperty(ref _levelSearchText, value)) LevelsView.Refresh(); }
        }

        public string RoomSearchText
        {
            get => _roomSearchText;
            set
            {
                if (SetProperty(ref _roomSearchText, value))
                {
                    RoomsView.Refresh();
                    RaiseRoomCounts();
                }
            }
        }

        public bool HideUnselectedLevels
        {
            get => _hideUnselectedLevels;
            set { if (SetProperty(ref _hideUnselectedLevels, value)) LevelsView.Refresh(); }
        }

        public bool HideUnselectedRooms
        {
            get => _hideUnselectedRooms;
            set
            {
                if (SetProperty(ref _hideUnselectedRooms, value))
                {
                    RoomsView.Refresh();
                    RaiseRoomCounts();
                }
            }
        }

        public bool ShowLevelsWithRoomsOnly
        {
            get => _showLevelsWithRoomsOnly;
            set
            {
                if (SetProperty(ref _showLevelsWithRoomsOnly, value))
                {
                    LevelsView.Refresh();
                }
            }
        }

        /// <summary>
        /// When true the room list hides every room the preflight did not mark ELIGIBLE, so the user only
        /// sees rooms that can actually take a device. Mirrors the sprinkler tab's filter of the same name.
        /// </summary>
        public bool ShowEligibleRoomsOnly
        {
            get => _showEligibleRoomsOnly;
            set
            {
                if (SetProperty(ref _showEligibleRoomsOnly, value))
                {
                    RoomsView.Refresh();
                    RaiseRoomCounts();
                }
            }
        }

        /// <summary>True while a read-only eligibility pass is running; placement is gated off until it settles.</summary>
        public bool IsEligibilityRefreshing => _isEligibilityRefreshing;

        public bool IsPlacementRunning
        {
            get => _isPlacementRunning;
            private set => SetProperty(ref _isPlacementRunning, value);
        }

        public string PlacementStatusMessage
        {
            get => _placementStatusMessage;
            private set => SetProperty(ref _placementStatusMessage, value);
        }

        public DeviceFamilyOption SelectedDeviceFamily
        {
            get => _selectedDeviceFamily;
            set
            {
                if (SetProperty(ref _selectedDeviceFamily, value))
                {
                    // Coalesce the type cascade + row re-seed into one preflight: suppress their inner
                    // refresh triggers, then run a single RefreshEligibility for the whole family change.
                    bool previousSuppress = _suppressEligibilityRefresh;
                    _suppressEligibilityRefresh = true;
                    try
                    {
                        RefreshDeviceTypesForSelectedFamily();
                        ValidateSelectedTypeForCurrentFamily();
                        OnPropertyChanged(nameof(IsDeviceFamilySelected));
                        OnPropertyChanged(nameof(IsDeviceTypeSelected));
                        // The top-level selection is the default for every row: re-seed rows still on
                        // the old default and leave user-overridden rows alone.
                        SeedPerRowDeviceDefaults();
                        OnUniversalFamilyTypeChanged();
                    }
                    finally
                    {
                        _suppressEligibilityRefresh = previousSuppress;
                    }
                    CommandManager.InvalidateRequerySuggested();
                    RefreshEligibility();
                }
            }
        }

        public DeviceTypeOption SelectedDeviceType
        {
            get => _selectedDeviceType;
            set
            {
                if (SetProperty(ref _selectedDeviceType, value))
                {
                    OnPropertyChanged(nameof(IsDeviceTypeSelected));
                    bool previousSuppress = _suppressEligibilityRefresh;
                    _suppressEligibilityRefresh = true;
                    try
                    {
                        SeedPerRowDeviceDefaults();
                        OnUniversalFamilyTypeChanged();
                    }
                    finally
                    {
                        _suppressEligibilityRefresh = previousSuppress;
                    }
                    CommandManager.InvalidateRequerySuggested();
                    RefreshEligibility();
                }
            }
        }

        /// <summary>
        /// Hook fired (inside the batch-suppression window) whenever the tab's universal family/type
        /// settles to a new selection. Concrete VMs use it to re-derive catalog-driven attributes.
        /// </summary>
        protected virtual void OnUniversalFamilyTypeChanged()
        {
        }

        /// <summary>
        /// Device-specific attribute derivation: the value implied by the catalog row for
        /// (<paramref name="familyName"/>, <paramref name="typeName"/>), or null when the attribute is not
        /// derivable from family/type. Derived attributes (smoke DetectorType/Mount/CeilingSlope, NA
        /// ApplianceType/CandelaDba) are catalog-owned, not user-owned: the tabs show them read-only and
        /// placement uses them ahead of any level/row default.
        /// </summary>
        protected virtual string DeriveAttribute(string key, string familyName, string typeName)
        {
            return null;
        }

        /// <summary>
        /// Effective attribute value for one room row: the value derived from THAT row's own family/type
        /// (rows can override family per-room, and the attributes must follow), falling back to the
        /// row-override / level-default chain only when nothing is derivable.
        /// </summary>
        private string EffectiveDeviceAttribute(DeviceRoomItemViewModel roomVm, string key, string familyName, string typeName)
        {
            string derived = DeriveAttribute(key, familyName, typeName);
            if (!string.IsNullOrWhiteSpace(derived)) return derived;
            return roomVm.GetEffective(key);
        }

        public bool IsDeviceFamilySelected => SelectedDeviceFamily != null;
        public bool IsDeviceTypeSelected => SelectedDeviceType != null;

        public int SelectedLevelCount => Levels.Count(l => l.IsSelected);

        public string SelectedLevelSummary =>
            SelectedLevelCount == 1 ? "1 level selected" : SelectedLevelCount + " levels selected";

        public int SelectedRoomCount => AllRooms.Count(r => r.IsSelected);

        public int VisibleRoomCount
        {
            get
            {
                int count = 0;
                if (RoomsView != null) foreach (object _ in RoomsView) count++;
                return count;
            }
        }

        public int SelectedVisibleRoomCount
        {
            get
            {
                int count = 0;
                if (RoomsView != null)
                {
                    foreach (object obj in RoomsView)
                        if (obj is DeviceRoomItemViewModel r && r.IsSelected) count++;
                }
                return count;
            }
        }

        public string RoomsHeader
        {
            get
            {
                int selectedLevels = SelectedLevelCount;
                if (selectedLevels == 0) return "Rooms";
                if (selectedLevels == 1)
                {
                    DeviceLevelItemViewModel only = Levels.First(l => l.IsSelected);
                    return "Rooms on " + only.Name;
                }
                return "Rooms on " + selectedLevels + " selected levels";
            }
        }

        public string RoomsFoundText
        {
            get
            {
                int visible = VisibleRoomCount;
                return visible + (visible == 1 ? " room shown" : " rooms shown");
            }
        }

        public string RoomsSelectedSummary
        {
            get
            {
                int selected = SelectedVisibleRoomCount;
                int visible = VisibleRoomCount;
                return selected + " of " + visible + (visible == 1 ? " room selected" : " rooms selected");
            }
        }

        public ICommand ToggleHideUnselectedLevelsCommand { get; }
        public ICommand ToggleHideUnselectedRoomsCommand { get; }

        public ICommand ToggleSelectAllLevelsCommand { get; }
        public ICommand ToggleSelectAllRoomsCommand { get; }

        public bool AreAllSelectableLevelsSelected =>
            Levels.Any(l => l.HasRooms) && Levels.Where(l => l.HasRooms).All(l => l.IsSelected);

        public string LevelSelectionToggleLabel =>
            AreAllSelectableLevelsSelected ? "Clear All" : "Select All";

        // A blocked / undetermined room is not "selectable", so the Select-All toggle only tracks and
        // touches eligible rooms — mirroring the sprinkler tab.
        public bool AreAllSelectableRoomsSelected =>
            AllRooms.Any(r => r.IsEligible) && AllRooms.Where(r => r.IsEligible).All(r => r.IsSelected);

        public string RoomSelectionToggleLabel =>
            AreAllSelectableRoomsSelected ? "Clear All" : "Select All";

        public ICommand PlaceDevicesCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand CancelPlacementCommand { get; }
        public ICommand ApplyFamilyToSelectedCommand { get; }
        public ICommand ClearRoomOverridesForSelectedCommand { get; }
        public ICommand ClearRunLogCommand { get; }

        // ----- Progress + cancel (item 3) ------------------------------------------------------------

        public string PlacementProgressText
        {
            get { return _placementProgressText; }
            private set { _placementProgressText = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPlacementProgressVisible)); }
        }

        public bool IsPlacementProgressVisible => !string.IsNullOrEmpty(PlacementProgressText);

        public bool CanCancelPlacement
        {
            get { return _canCancelPlacement; }
            private set { _canCancelPlacement = value; OnPropertyChanged(); }
        }

        // ----- Run log -----------------------------------------------------------------------------

        public bool IsRunLogVisible
        {
            get => RunLogEntries.Count > 0;
        }

        public void AddRunLogEntry(string message, string level = "INFO")
        {
            RunLogEntries.Add(new RunLogEntry
            {
                Timestamp = DateTime.Now,
                Message = message,
                Level = level
            });
            OnPropertyChanged(nameof(IsRunLogVisible));
        }

        public void ClearRunLog()
        {
            RunLogEntries.Clear();
            OnPropertyChanged(nameof(IsRunLogVisible));
        }

        private void OnPlacementProgress(int completed, int total, string label)
        {
            PlacementProgressText = total > 0 ? label + "  (" + completed + "/" + total + ")" : label;
            PlacementStatusMessage = PlacementProgressText;
        }

        private bool CanExecuteCancelPlacement()
        {
            return IsPlacementRunning && CanCancelPlacement && _progressReporter != null;
        }

        private void ExecuteCancelPlacement()
        {
            if (_progressReporter == null) return;
            _progressReporter.RequestCancel();
            CanCancelPlacement = false;
            PlacementProgressText = "Cancelling - finishing the current room, then rolling back...";
            CommandManager.InvalidateRequerySuggested();
        }

        // ----- Re-run / existing-device policy (list-2 item 3) --------------------------------------

        public string[] ExistingDevicePolicyOptionLabels => ExistingDevicePolicyOptions.Labels;

        /// <summary>Explains the three re-run options in one tooltip, so the combo does not need three labels.</summary>
        public string ExistingDevicePolicyTooltip => ExistingDevicePolicyOptions.Explanation;

        // ----- Unit-aware column headers (list-2 item 5) --------------------------------------------
        // Only the suffix follows the project's display unit; stored values stay in decimal feet.
        // Areas are deliberately NOT converted, so the area header stays sq ft.

        public string CeilingColumnHeader => "Ceiling Height " + UnitDisplay.HeaderSuffix;
        public string AreaColumnHeader => "Area (sq ft)";
        public string UnitSuffix => UnitDisplay.Suffix;

        /// <summary>Defaults to Skip so a second Place never silently doubles the devices in a room.</summary>
        public string SelectedExistingDevicePolicyLabel
        {
            get { return _selectedExistingDevicePolicyLabel; }
            set
            {
                if (_selectedExistingDevicePolicyLabel == value) return;
                _selectedExistingDevicePolicyLabel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ExistingDevicePolicySelection));
            }
        }

        public ExistingDevicePolicy ExistingDevicePolicySelection =>
            ExistingDevicePolicyOptions.Parse(_selectedExistingDevicePolicyLabel);

        // ----- Bulk edit (list-2 item 8) -----------------------------------------------------------
        // Target = the checked rooms. On the device tabs the editable per-room values are the family/type
        // and the device attributes, so bulk edit pushes the universal selection down and can clear per-row
        // overrides back to the level / universal value.

        private IEnumerable<DeviceRoomItemViewModel> BulkTargets => AllRooms.Where(r => r.IsSelected);

        public int BulkTargetCount => BulkTargets.Count();

        public string BulkTargetSummary
        {
            get
            {
                int count = BulkTargetCount;
                return count == 1 ? "1 selected room" : count + " selected rooms";
            }
        }

        private bool CanBulkEdit() => BulkTargets.Any();

        private void ApplyUniversalFamilyTypeToSelected()
        {
            if (SelectedDeviceFamily == null) return;

            string family = SelectedDeviceFamily.FamilyName;
            string type = SelectedDeviceType != null ? SelectedDeviceType.TypeName : null;

            // Suppress the per-row refresh storm; one preflight after the whole bulk edit.
            bool previousSuppress = _suppressEligibilityRefresh;
            _suppressEligibilityRefresh = true;
            try
            {
                foreach (DeviceRoomItemViewModel room in BulkTargets.ToList())
                {
                    room.SelectedFamily = family;
                    room.AvailableTypes = GetCatalogTypesForFamily(family) ?? new List<string>();
                    room.SelectedType = type;
                }
            }
            finally
            {
                _suppressEligibilityRefresh = previousSuppress;
            }
            RefreshEligibility();
        }

        private void ClearOverridesForSelected()
        {
            bool previousSuppress = _suppressEligibilityRefresh;
            _suppressEligibilityRefresh = true;
            try
            {
                foreach (DeviceRoomItemViewModel room in BulkTargets.ToList())
                    room.ResetFamilyAndTypeToDefault();
            }
            finally
            {
                _suppressEligibilityRefresh = previousSuppress;
            }
            RefreshEligibility();
        }

        private void LoadDeviceFamilies()
        {
            // The family/type assignments below fire the setters (and their RefreshEligibility). Suppress
            // them here and let the caller (ctor via InitializeEligibility, catalog reload, reset) run one
            // preflight once the whole family/type + attribute re-seed has settled.
            bool previousSuppress = _suppressEligibilityRefresh;
            _suppressEligibilityRefresh = true;
            try
            {
                DeviceFamilies.Clear();
                DeviceTypes.Clear();
                SelectedDeviceFamily = null;
                SelectedDeviceType = null;

                // The Excel catalog is the source of truth (Decision 017). IDeviceFamilySource is only a
                // fallback for a host that lists families out of the open Revit document.
                IReadOnlyList<DeviceFamilyOption> families = GetCatalogFamilyOptions();

                if ((families == null || families.Count == 0) && _deviceFamilySource != null)
                {
                    families = _deviceFamilySource.GetAvailableFamilies();
                }

                if (families == null) return;

                foreach (DeviceFamilyOption family in families)
                {
                    if (family != null)
                        DeviceFamilies.Add(family);
                }

                if (DeviceFamilies.Count > 0)
                {
                    // Give the tab a usable universal default instead of an empty combo.
                    SelectedDeviceFamily = DeviceFamilies[0];
                    if (DeviceTypes.Count > 0) SelectedDeviceType = DeviceTypes[0];
                }
            }
            finally
            {
                _suppressEligibilityRefresh = previousSuppress;
            }
        }

        /// <summary>
        /// Builds the family/type options for this device category out of the loaded catalog.
        /// Concrete VMs map their own sheet (SmokeDetectors / NotificationAppliances).
        /// </summary>
        protected virtual IReadOnlyList<DeviceFamilyOption> GetCatalogFamilyOptions()
        {
            return new List<DeviceFamilyOption>();
        }

        /// <summary>Family -&gt; type names for this device category, from the catalog.</summary>
        protected virtual IReadOnlyList<string> GetCatalogTypesForFamily(string familyName)
        {
            return new List<string>();
        }

        /// <summary>The loaded catalog, or null when the user has not picked a workbook yet.</summary>
        protected ICatalog Catalog
        {
            get { return _catalogVm != null ? _catalogVm.Catalog : null; }
        }

        /// <summary>True when a workbook is loaded and carries rows for this device category.</summary>
        public bool IsCatalogLoaded
        {
            get
            {
                IReadOnlyList<DeviceFamilyOption> families = GetCatalogFamilyOptions();
                return families != null && families.Count > 0;
            }
        }

        public string CatalogStatusMessage
        {
            get
            {
                if (_catalogVm == null || _catalogVm.Catalog == null || !_catalogVm.Catalog.IsLoaded)
                    return "No catalog loaded — pick the catalog workbook in the top bar to populate the "
                           + DeviceDisplayName.ToLowerInvariant() + " options.";
                if (!IsCatalogLoaded)
                    return "The loaded catalog has no rows for " + DeviceDisplayName.ToLowerInvariant() + ".";
                return null;
            }
        }

        /// <summary>
        /// Pushes the tab's universal Family/Type onto every room row as that row's default, and
        /// gives each row the family -&gt; types resolver so its Type combo follows its own Family.
        /// Rows the user overrode are preserved (Decision 019).
        /// </summary>
        protected void SeedPerRowDeviceDefaults()
        {
            IReadOnlyList<string> familyNames = DeviceFamilies
                .Where(f => f != null && !string.IsNullOrWhiteSpace(f.FamilyName))
                .Select(f => f.FamilyName)
                .ToList();

            // Resolve the effective universal default once — every row and every level gets the same
            // value, so resolving it per row would let a null tab selection wipe a just-seeded row.
            string defaultFamily = null;
            string defaultType = null;

            if (familyNames.Count > 0)
            {
                string wantedFamily = SelectedDeviceFamily != null ? SelectedDeviceFamily.FamilyName : null;
                defaultFamily = !string.IsNullOrEmpty(wantedFamily)
                                && familyNames.Contains(wantedFamily, StringComparer.OrdinalIgnoreCase)
                    ? wantedFamily
                    : familyNames[0];

                IReadOnlyList<string> types = GetCatalogTypesForFamily(defaultFamily) ?? new List<string>();
                string wantedType = SelectedDeviceType != null ? SelectedDeviceType.TypeName : null;
                defaultType = !string.IsNullOrEmpty(wantedType)
                              && types.Contains(wantedType, StringComparer.OrdinalIgnoreCase)
                    ? wantedType
                    : (types.Count > 0 ? types[0] : null);
            }

            // Level defaults first: their propagation must not overwrite the row seeding below.
            foreach (DeviceLevelItemViewModel level in Levels)
            {
                level.DeviceFamily = defaultFamily;
                level.DeviceType = defaultType;
            }

            foreach (DeviceRoomItemViewModel room in AllRooms)
            {
                room.SetTypesResolver(GetCatalogTypesForFamily);
                room.AvailableFamilies = familyNames;
                room.SetDeviceDefaults(defaultFamily, defaultType);
            }
        }

        private void OnCatalogViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null) return;
            if (e.PropertyName != nameof(CatalogViewModel.IsLoaded)
                && e.PropertyName != nameof(CatalogViewModel.TotalRowCount)
                && e.PropertyName != nameof(CatalogViewModel.CatalogVersion)
                && e.PropertyName != nameof(CatalogViewModel.SourcePath))
                return;

            LoadDeviceFamilies();

            // The catalog reload re-seeds family/type, attributes, and selection; coalesce all of it into
            // one preflight instead of letting every seeded setter fire its own.
            bool previousSuppress = _suppressEligibilityRefresh;
            _suppressEligibilityRefresh = true;
            try
            {
                SeedPerRowDeviceDefaults();
                OnCatalogChanged();

                // The catalog supplies the family/type every row needs, so re-apply the default selection here
                // too - otherwise a catalog load could leave the device tabs with nothing checked while the
                // sprinkler tab arrives fully selected.
                ApplyDefaultSelection();
            }
            finally
            {
                _suppressEligibilityRefresh = previousSuppress;
            }

            OnPropertyChanged(nameof(IsCatalogLoaded));
            OnPropertyChanged(nameof(CatalogStatusMessage));
            CommandManager.InvalidateRequerySuggested();
            RefreshEligibility();
        }

        /// <summary>
        /// Called after a catalog load/reload so a concrete VM can re-raise its own
        /// catalog-derived option lists (detector types, mounts, candela, ...).
        /// </summary>
        protected virtual void OnCatalogChanged()
        {
        }

        private void RefreshDeviceTypesForSelectedFamily()
        {
            DeviceTypes.Clear();

            if (SelectedDeviceFamily == null || SelectedDeviceFamily.Types == null)
                return;

            foreach (DeviceTypeOption type in SelectedDeviceFamily.Types)
            {
                if (type != null)
                    DeviceTypes.Add(type);
            }
        }

        private void ValidateSelectedTypeForCurrentFamily()
        {
            if (SelectedDeviceFamily == null || DeviceTypes.Count == 0)
            {
                SelectedDeviceType = null;
                return;
            }

            // DeviceTypes only ever holds the selected family's types, so a family change always fails
            // this check and lands on the new family's first type: Type is never left blank and never
            // keeps a type that belonged to the previous family.
            bool stillValid = SelectedDeviceType != null && DeviceTypes.Any(t =>
                string.Equals(t.FamilyName, SelectedDeviceType.FamilyName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.TypeName, SelectedDeviceType.TypeName, StringComparison.OrdinalIgnoreCase));

            if (!stillValid)
                SelectedDeviceType = DeviceTypes[0];
        }

        private bool CanExecutePlaceDevices()
        {
            if (IsBackendPending) return false;
            if (IsPlacementRunning) return false;
            if (_isEligibilityRefreshing) return false;
            // Selected-eligible count, not visible count: a blocked room is auto-deselected, but gate on
            // eligibility explicitly so Place is enabled iff at least one room will actually be placed.
            if (!AllRooms.Any(r => r.IsSelected && r.IsEligible)) return false;
            if (SelectedDeviceFamily == null) return false;
            if (SelectedDeviceType == null) return false;
            if (!string.Equals(SelectedDeviceType.FamilyName, SelectedDeviceFamily.FamilyName, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private void ExecutePlaceDevices()
        {
            if (IsBackendPending)
            {
                PlacementStatusMessage = "Device placement backend is not yet implemented.";
                Dialogs.Show(
                    "Device placement backend is not yet implemented. Room/level selection is available for planning only.",
                    DeviceDisplayName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (IsPlacementRunning) return;

            // Replace deletes existing devices. Undoable, but destructive enough to confirm explicitly.
            if (ExistingDevicePolicySelection == ExistingDevicePolicy.ReplaceExisting &&
                !Dialogs.Confirm(
                    "Replace will DELETE the existing " + DeviceDisplayName.ToLowerInvariant()
                    + " devices inside every selected room before placing the new set.\n\nThis can be undone in "
                    + "Revit (the run is a single undo entry), but the existing devices and any data on them will "
                    + "be gone.\n\nContinue?",
                    "Replace existing devices"))
            {
                PlacementStatusMessage = "Placement cancelled.";
                return;
            }

            // Document mutation is illegal on this modeless window's WPF event thread. Hand the work back
            // to Revit: on the real context Run is fire-and-forget (queued onto the Revit UI thread that
            // owns this window); on the Tests/designer context it runs inline. State is reset in
            // PlaceDevicesCore's finally, or in OnPlacementFailed if Run itself surfaces the throw.
            IsPlacementRunning = true;
            PlacementStatusMessage = "Waiting for Revit...";
            PlacementProgressText = "Waiting for Revit...";
            _progressReporter = new PlacementProgressReporter(OnPlacementProgress);
            CanCancelPlacement = true;
            CommandManager.InvalidateRequerySuggested();

            RevitApi.Run(PlaceDevicesCore, OnPlacementFailed);
        }

        private void PlaceDevicesCore()
        {
            try
            {
                List<DeviceRoomInputItem> roomSelections = CollectSelectedRooms();

                if (roomSelections.Count == 0)
                {
                    PlacementStatusMessage = "No selected rooms with usable geometry.";
                    Dialogs.Show("No selected rooms have usable geometry.", DeviceDisplayName,
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_deviceFamilySource != null)
                {
                    List<MissingFamiliesModal.MissingEntry> missing = FindMissingFamilies(roomSelections);
                    if (missing.Count > 0)
                    {
                        bool proceed = MissingFamiliesModal.ShowDialog(
                            Dialogs.Owner,
                            missing,
                            path => _deviceFamilySource.TryLoadFamily(path, out string loadError) ? null : loadError);
                        if (!proceed)
                        {
                            PlacementStatusMessage = "Placement cancelled: required family/type is not loaded.";
                            return;
                        }
                    }
                }

                FireProtectionLog.Info(DeviceDisplayName + ": placement run started for "
                    + roomSelections.Count + " room(s), policy " + ExistingDevicePolicySelection + ".");

                ClearRunLog();
                AddRunLogEntry(DeviceDisplayName + " run started", "INFO");
                AddRunLogEntry("Rooms: " + roomSelections.Count + ", Policy: " + ExistingDevicePolicySelection, "INFO");

                PlacementRunReport report = _deviceExecutor.ExecutePlacement(
                    roomSelections, _progressReporter, ExistingDevicePolicySelection);
                LastDeviceResult = report;

                AddRunLogEntry("Processed " + report.RoomsProcessed + " room(s): " +
                    report.RoomsSucceeded + " succeeded, " + report.RoomsFailed + " failed", "INFO");

                PlacementStatusMessage =
                    $"Processed {report.RoomsProcessed} room(s); {report.RoomsSucceeded} succeeded, " +
                    $"{report.RoomsFailed} failed.";

                var reportView = new PlacementResultReportView
                {
                    DataContext = new PlacementResultReportViewModel(report, DeviceDisplayName)
                };

                var reportWindow = new PlacementResultReportWindow(reportView)
                {
                    Owner = Dialogs.Owner
                };
                reportWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                AddRunLogEntry("Placement failed: " + ex.Message, "ERROR");
                PlacementStatusMessage = "Placement failed: " + ex.Message;
                FireProtectionLog.Error(DeviceDisplayName + ": placement failed.", ex);
                Dialogs.Show("Placement failed:\n\n" + ex.Message, DeviceDisplayName,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AddRunLogEntry("Run finished", "INFO");
                IsPlacementRunning = false;
                CanCancelPlacement = false;
                PlacementProgressText = null;
                _progressReporter = null;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        /// <summary>Invoked in the Revit API context if <see cref="RevitApi"/> surfaces a throw that
        /// <see cref="PlaceDevicesCore"/>'s own try/catch did not handle. Mirrors the sprinkler flow.</summary>
        private void OnPlacementFailed(Exception ex)
        {
            AddRunLogEntry("Placement failed: " + (ex?.Message ?? "unknown error"), "ERROR");
            IsPlacementRunning = false;
            CanCancelPlacement = false;
            PlacementProgressText = null;
            _progressReporter = null;

            PlacementStatusMessage = "Placement failed: " + (ex?.Message ?? "unknown error");
            FireProtectionLog.Error(DeviceDisplayName + ": placement failed (unhandled).", ex);
            Dialogs.Show("Placement failed:\n\n" + (ex?.Message ?? "unknown error"), DeviceDisplayName,
                MessageBoxButton.OK, MessageBoxImage.Error);
            CommandManager.InvalidateRequerySuggested();
        }

        public PlacementRunReport LastDeviceResult { get; private set; }

        private List<MissingFamiliesModal.MissingEntry> FindMissingFamilies(IReadOnlyList<DeviceRoomInputItem> rooms)
        {
            var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<DeviceFamilyOption> families = _deviceFamilySource.GetAvailableFamilies();
            if (families != null)
            {
                foreach (DeviceFamilyOption family in families)
                {
                    if (family == null || family.Types == null) continue;
                    foreach (DeviceTypeOption type in family.Types)
                    {
                        if (type != null) available.Add((family.FamilyName ?? string.Empty) + "::" + (type.TypeName ?? string.Empty));
                    }
                }
            }

            var missing = new List<MissingFamiliesModal.MissingEntry>();
            foreach (DeviceRoomInputItem room in rooms)
            {
                string key = (room.SelectedFamilyName ?? string.Empty) + "::" + (room.SelectedTypeName ?? string.Empty);
                if (!available.Contains(key))
                {
                    missing.Add(new MissingFamiliesModal.MissingEntry
                    {
                        RoomName = room.RoomName,
                        FamilyName = room.SelectedFamilyName,
                        TypeName = room.SelectedTypeName
                    });
                }
            }
            return missing;
        }

        // -------------------------------------------------------------------------------------------
        // Universal (tab) default -> per-level default -> per-row override (Decision 019).
        // The tab's combo is the universal default. DeviceLevelItemViewModel does the level -> row
        // propagation itself; the base only owns the tab -> level step.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Pushes a new tab-scope (universal) attribute default down to every level that was still on the
        /// previous universal value; each level then propagates it to its rows that have no override.
        /// A level (or row) the user customised keeps its own value — item 7 / Decision 019 semantics.
        /// </summary>
        protected void PropagateUniversalDefault(string key, string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(key)) return;

            // Level -> row propagation raises a per-row notification each; suppress the resulting refresh
            // storm and run a single preflight, since a mount/slope/candela/dBA change can move candidate
            // geometry and therefore eligibility. Suppressed to a no-op during construction/seeding.
            bool previousSuppress = _suppressEligibilityRefresh;
            _suppressEligibilityRefresh = true;
            try
            {
                foreach (DeviceLevelItemViewModel level in Levels)
                {
                    if (level == null) continue;

                    string current = level.GetDefault(key);
                    bool levelWasCustomised = !string.IsNullOrEmpty(current)
                        && !string.Equals(current, oldValue ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                    if (levelWasCustomised) continue;

                    level.SetDefault(key, newValue);
                }
            }
            finally
            {
                _suppressEligibilityRefresh = previousSuppress;
            }
            RefreshEligibility();
        }

        /// <summary>
        /// Opens the per-level settings popover (Decision 019). Device attributes are now catalog-derived
        /// (family/type-owned), so the device tabs no longer offer them per level; this stays virtual so a
        /// future level-scoped setting can hook in without re-plumbing the click path.
        /// </summary>
        public virtual void OpenLevelSettings(DeviceLevelItemViewModel level)
        {
        }

        /// <summary>True when this tab still offers level-scoped settings; drives the ⋯ button's visibility.</summary>
        public virtual bool HasLevelSettings => false;

        /// <summary>
        /// Applies a batch of per-level default changes (from the settings popover) as one unit: suppresses
        /// the per-row refresh storm the level -&gt; row propagation raises, then runs a single preflight.
        /// Concrete VMs wrap their <see cref="OpenLevelSettings"/> writes in this so a level with many rooms
        /// re-evaluates eligibility once, not once per room per field.
        /// </summary>
        protected void ApplyLevelSettings(Action apply)
        {
            if (apply == null) return;

            bool previousSuppress = _suppressEligibilityRefresh;
            _suppressEligibilityRefresh = true;
            try
            {
                apply();
            }
            finally
            {
                _suppressEligibilityRefresh = previousSuppress;
            }
            RefreshEligibility();
        }

        private List<DeviceRoomInputItem> CollectSelectedRooms()
        {
            List<DeviceRoomInputItem> list = new List<DeviceRoomInputItem>();

            foreach (DeviceLevelItemViewModel levelVm in Levels)
            {
                if (!levelVm.IsSelected) continue;

                foreach (DeviceRoomItemViewModel roomVm in levelVm.Rooms)
                {
                    if (!roomVm.IsSelected) continue;
                    // Never feed a blocked / undetermined room to the real run: the preflight already
                    // deselects them, but guard here too so placement input can never disagree with it.
                    if (!roomVm.IsEligible) continue;
                    if (roomVm.Room == null) continue;

                    list.Add(BuildRoomInputItem(levelVm.Level, roomVm));
                }
            }

            return list;
        }

        /// <summary>
        /// Builds the batch placement input for EVERY room (no selection / eligibility filter) so the
        /// read-only preflight can classify all rooms up front. Counterpart to <see cref="CollectSelectedRooms"/>,
        /// which gathers only what the user checked (and only eligible rooms) for the real run.
        /// </summary>
        private List<DeviceRoomInputItem> CollectAllRoomsForEligibility()
        {
            List<DeviceRoomInputItem> list = new List<DeviceRoomInputItem>();

            foreach (DeviceLevelItemViewModel levelVm in Levels)
            {
                foreach (DeviceRoomItemViewModel roomVm in levelVm.Rooms)
                {
                    if (roomVm.Room == null) continue;
                    list.Add(BuildRoomInputItem(levelVm.Level, roomVm));
                }
            }

            return list;
        }

        /// <summary>
        /// Builds one <see cref="DeviceRoomInputItem"/> from a room row. Shared by the real run and the
        /// preflight so the eligibility pass sees exactly the input the placement would — same family/type
        /// fallback, same effective per-room attributes — and can never disagree with it.
        /// </summary>
        private DeviceRoomInputItem BuildRoomInputItem(LevelUiData levelData, DeviceRoomItemViewModel roomVm)
        {
            RoomUiData roomData = roomVm.Room;

            List<double[]> polyCopy = new List<double[]>();
            if (roomData.Geometry != null && roomData.Geometry.Polygon != null)
            {
                foreach (double[] v in roomData.Geometry.Polygon)
                {
                    if (v != null && v.Length >= 2)
                        polyCopy.Add(new double[] { v[0], v[1] });
                }
            }

            // Per-row override wins; the tab's universal selection is the fallback.
            string effectiveFamily = !string.IsNullOrEmpty(roomVm.SelectedFamily)
                ? roomVm.SelectedFamily
                : SelectedDeviceFamily?.FamilyName;
            string effectiveType = !string.IsNullOrEmpty(roomVm.SelectedType)
                ? roomVm.SelectedType
                : SelectedDeviceType?.TypeName;

            return new DeviceRoomInputItem
            {
                LevelId = levelData.LevelId,
                LevelName = levelData.Name,
                LevelElevationFt = levelData.ElevationFt,

                RoomId = roomData.RoomId,
                RoomName = roomData.Name,
                RoomNumber = roomData.Number,
                AreaSqFt = roomData.AreaSqFt,
                CeilingHeightFt = roomData.Geometry?.CeilingHeightFt,
                CeilingType = roomData.Geometry?.CeilingType,
                Polygon = polyCopy,

                FullRoomJson = BuildFullRoomJson(roomData),
                // Per-row override wins; the tab's universal selection is the fallback.
                SelectedFamilyName = effectiveFamily,
                SelectedTypeName = effectiveType,

                // Effective per-room device attributes. Catalog-derived attributes (family/type-owned)
                // win over the row-override / level-default chain; everything else falls through it.
                // The device kind that does not expose a given key returns null here; the backend calc
                // engine treats null as "use the engine default", so populating all five unconditionally is safe.
                DetectorType = EffectiveDeviceAttribute(roomVm, "DetectorType", effectiveFamily, effectiveType),
                Mount = EffectiveDeviceAttribute(roomVm, "Mount", effectiveFamily, effectiveType),
                CeilingSlope = EffectiveDeviceAttribute(roomVm, "CeilingSlope", effectiveFamily, effectiveType),
                ApplianceType = EffectiveDeviceAttribute(roomVm, "ApplianceType", effectiveFamily, effectiveType),
                CandelaDba = EffectiveDeviceAttribute(roomVm, "CandelaDba", effectiveFamily, effectiveType),

                DeviceKind = TabDeviceKind
            };
        }

        private static JObject BuildFullRoomJson(RoomUiData roomData)
        {
            if (roomData == null) return null;

            JObject full = JObject.FromObject(roomData);

            if (full["ExtensionData"] != null) full.Remove("ExtensionData");

            if (roomData.ExtensionData != null)
            {
                foreach (KeyValuePair<string, JToken> kvp in roomData.ExtensionData)
                {
                    full[kvp.Key] = kvp.Value;
                }
            }

            return full;
        }

        private bool FilterLevel(object obj)
        {
            DeviceLevelItemViewModel level = obj as DeviceLevelItemViewModel;
            if (level == null) return false;
            if (ShowLevelsWithRoomsOnly && !level.HasRooms) return false;
            if (HideUnselectedLevels && !level.IsSelected) return false;
            if (string.IsNullOrWhiteSpace(LevelSearchText)) return true;
            string search = LevelSearchText.Trim();
            return !string.IsNullOrEmpty(level.Name)
                   && level.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool FilterRoom(object obj)
        {
            DeviceRoomItemViewModel room = obj as DeviceRoomItemViewModel;
            if (room == null) return false;
            if (room.ParentLevel == null || !room.ParentLevel.IsSelected) return false;
            if (ShowEligibleRoomsOnly && !room.IsEligible) return false;
            if (HideUnselectedRooms && !room.IsSelected) return false;
            return MatchesRoomSearch(room);
        }

        private bool MatchesRoomSearch(DeviceRoomItemViewModel room)
        {
            if (string.IsNullOrWhiteSpace(RoomSearchText)) return true;
            string search = RoomSearchText.Trim();
            bool nameMatch = !string.IsNullOrEmpty(room.Name)
                             && room.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
            bool numberMatch = !string.IsNullOrEmpty(room.Number)
                               && room.Number.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
            return nameMatch || numberMatch;
        }

        private void ToggleSelectAllLevels()
        {
            bool select = !AreAllSelectableLevelsSelected;
            foreach (DeviceLevelItemViewModel level in Levels.Where(l => l.HasRooms))
                level.IsSelected = select;
        }

        private void ToggleSelectAllRooms()
        {
            bool select = !AreAllSelectableRoomsSelected;
            // Only eligible rooms are selectable; a blocked / undetermined room stays deselected.
            foreach (DeviceRoomItemViewModel room in AllRooms.Where(r => r.IsEligible))
                room.IsSelected = select;
        }

        private void OnLevelItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeviceLevelItemViewModel.IsSelected))
            {
                var levelVm = sender as DeviceLevelItemViewModel;
                if (levelVm != null)
                {
                    bool select = levelVm.IsSelected;
                    foreach (DeviceRoomItemViewModel room in levelVm.Rooms)
                    {
                        // Selecting a level selects only its eligible rooms; a blocked / undetermined room
                        // stays off. Deselecting the level clears every room.
                        room.IsSelected = select && room.IsEligible;
                    }
                }

                OnPropertyChanged(nameof(AreAllSelectableLevelsSelected));
                OnPropertyChanged(nameof(LevelSelectionToggleLabel));
                OnPropertyChanged(nameof(AreAllSelectableRoomsSelected));
                OnPropertyChanged(nameof(RoomSelectionToggleLabel));
            }
            else if (e.PropertyName == nameof(DeviceLevelItemViewModel.DeviceFamily)
                     || e.PropertyName == nameof(DeviceLevelItemViewModel.DeviceType))
            {
                // The level default has already propagated into its non-overridden rows; refresh the
                // counters so the header text follows.
                RaiseRoomCounts();
            }
        }

        private void OnRoomItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeviceRoomItemViewModel.IsSelected))
            {
                OnPropertyChanged(nameof(AreAllSelectableRoomsSelected));
                OnPropertyChanged(nameof(RoomSelectionToggleLabel));
            }
            else if (e.PropertyName == nameof(DeviceRoomItemViewModel.SelectedFamily)
                     || e.PropertyName == nameof(DeviceRoomItemViewModel.SelectedType)
                     || (e.PropertyName != null && e.PropertyName.StartsWith("Override_", StringComparison.Ordinal)))
            {
                OnPropertyChanged(nameof(AreAllSelectableRoomsSelected));
                OnPropertyChanged(nameof(RoomSelectionToggleLabel));
                RaiseRoomCounts();
                // A per-row family/type/attribute change can move this room's eligibility (e.g. to an
                // unsupported family). Re-run the preflight; suppressed to a no-op during batch seeding,
                // bulk edit, and level-popover application, which each run one pass of their own.
                RefreshEligibility();
            }
        }

        /// <summary>
        /// Helper for concrete VMs: the effective value of a per-level/per-row device attribute
        /// (row override first, then the level default the row was given).
        /// </summary>
        protected static string GetEffectiveAttribute(DeviceRoomItemViewModel roomVm, string key)
        {
            if (roomVm == null || string.IsNullOrEmpty(key)) return null;
            return roomVm.GetEffective(key);
        }

        /// <summary>
        /// Selects every level that has rooms and every room on it - the same "arrive ready to place"
        /// default the sprinkler tab uses. Only ever selects, never clears, so it is safe to call
        /// repeatedly: concrete VMs call it again once their attribute defaults are seeded.
        /// </summary>
        protected void ApplyDefaultSelection()
        {
            foreach (DeviceLevelItemViewModel level in Levels)
            {
                if (!level.HasRooms) continue;
                level.IsSelected = true;

                foreach (DeviceRoomItemViewModel room in level.Rooms)
                {
                    // Only eligible rooms default to selected. Before the first preflight every room is
                    // eligible (so this selects all, as before); afterwards blocked rooms stay off.
                    if (room.IsEligible) room.IsSelected = true;
                }
            }
        }

        private void OnLevelSelectionChanged(object sender, EventArgs e)
        {
            OnPropertyChanged(nameof(SelectedLevelCount));
            OnPropertyChanged(nameof(SelectedLevelSummary));
            OnPropertyChanged(nameof(RoomsHeader));
            RoomsView.Refresh();
            RaiseRoomCounts();
            if (HideUnselectedLevels) LevelsView.Refresh();
            CommandManager.InvalidateRequerySuggested();
        }

        private void OnRoomSelectionChanged(object sender, EventArgs e)
        {
            OnPropertyChanged(nameof(SelectedRoomCount));
            OnPropertyChanged(nameof(SelectedVisibleRoomCount));
            OnPropertyChanged(nameof(RoomsSelectedSummary));

            if (HideUnselectedRooms)
            {
                RoomsView.Refresh();
                OnPropertyChanged(nameof(VisibleRoomCount));
                OnPropertyChanged(nameof(RoomsFoundText));
            }
            CommandManager.InvalidateRequerySuggested();
        }

        private void RaiseRoomCounts()
        {
            OnPropertyChanged(nameof(VisibleRoomCount));
            OnPropertyChanged(nameof(SelectedVisibleRoomCount));
            OnPropertyChanged(nameof(RoomsFoundText));
            OnPropertyChanged(nameof(RoomsSelectedSummary));
            OnPropertyChanged(nameof(BulkTargetCount));
            OnPropertyChanged(nameof(BulkTargetSummary));
        }

        private void Reset()
        {
            LevelSearchText = null;
            RoomSearchText = null;
            HideUnselectedLevels = false;
            HideUnselectedRooms = false;
            ShowEligibleRoomsOnly = false;

            // Reset re-seeds selection, family/type, overrides and attributes; coalesce all of it into one
            // preflight rather than firing on every cleared override and re-seeded default.
            bool previousSuppress = _suppressEligibilityRefresh;
            _suppressEligibilityRefresh = true;
            try
            {
                foreach (DeviceLevelItemViewModel level in Levels)
                {
                    level.IsSelected = false;
                    foreach (DeviceRoomItemViewModel room in level.Rooms)
                    {
                        room.IsSelected = false;
                        // Drop per-row overrides so the row falls back to the tab/level default.
                        room.ClearAllOverrides();
                    }
                }

                // Re-seed the universal family/type from the catalog, then push it back onto every row
                // (which also clears any per-row family/type override).
                LoadDeviceFamilies();
                OnCatalogChanged();
                foreach (DeviceRoomItemViewModel room in AllRooms)
                {
                    room.ResetFamilyAndTypeToDefault();
                }
                SeedPerRowDeviceDefaults();

                PlacementStatusMessage = null;
                ApplyDefaultSelection();
            }
            finally
            {
                _suppressEligibilityRefresh = previousSuppress;
            }

            OnPropertyChanged(nameof(IsCatalogLoaded));
            OnPropertyChanged(nameof(CatalogStatusMessage));
            CommandManager.InvalidateRequerySuggested();
            RefreshEligibility();
        }

        // -------------------------------------------------------------------------------------------
        // Eligibility preflight (Decision 016). Read-only "can this room take a device?" pass that runs the
        // SAME device calc + hosting resolution the real placement uses (via
        // IDevicePlacementExecutor.EvaluateEligibility), so the preflight can never disagree with the run.
        // Ported from SprinklerBruteForceViewModel; the device version is batch (one call keyed by RoomId)
        // because the device calc is batch. Creates nothing and opens no transaction.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Fires the initial eligibility pass. Concrete VMs MUST call this as the LAST statement of their
        /// constructor, once their own attribute defaults are seeded: the base ctor stays suppressed so the
        /// whole construction settles into this single pass instead of one per seeded setter. A no-op when
        /// the backend is absent (<see cref="IsBackendPending"/>), so the designer / catalog-only path is
        /// unaffected.
        /// </summary>
        protected void InitializeEligibility()
        {
            _suppressEligibilityRefresh = false;
            RefreshEligibility();
        }

        /// <summary>
        /// Kicks off a read-only eligibility pass. No-op when the backend is absent (with no executor there
        /// is nothing to probe and every room stays selectable, preserving the designer / catalog-only path)
        /// and while suppressed (batch re-seeding coalesces to a single pass).
        /// </summary>
        private void RefreshEligibility()
        {
            if (IsBackendPending) return;
            if (_suppressEligibilityRefresh) return;

            _isEligibilityRefreshing = true;
            CommandManager.InvalidateRequerySuggested();

            // Document reads are only legal on the Revit API thread. On the real context Run marshals there
            // (fire-and-forget); on Tests/designer it runs inline. The same seam the placement run uses.
            RevitApi.Run(RefreshEligibilityCore, OnEligibilityRefreshFailed);
        }

        /// <summary>Invoked if <see cref="RevitApi"/> surfaces a throw the core's own try/finally did not
        /// handle. Leaves rooms selectable (never falsely blocked) and clears the running gate.</summary>
        private void OnEligibilityRefreshFailed(Exception ex)
        {
            _isEligibilityRefreshing = false;
            FireProtectionLog.Warn(DeviceDisplayName + ": eligibility preflight could not run — "
                + (ex != null ? ex.Message : "unknown error") + ". Rooms left selectable.");
            CommandManager.InvalidateRequerySuggested();
        }

        private void RefreshEligibilityCore()
        {
            try
            {
                List<DeviceRoomInputItem> allRooms = CollectAllRoomsForEligibility();

                IReadOnlyDictionary<string, PlacementEligibilityResult> map = null;
                if (allRooms.Count > 0)
                {
                    try
                    {
                        map = _deviceExecutor.EvaluateEligibility(allRooms);
                    }
                    catch (Exception ex)
                    {
                        // A calc/infra failure must never masquerade as "blocked": leave the map null so
                        // every room falls to UNDETERMINED below, not to a false BLOCKED.
                        FireProtectionLog.Warn(DeviceDisplayName + ": eligibility evaluation threw — "
                            + ex.Message + ". Rooms marked undetermined.");
                        map = null;
                    }
                }

                foreach (DeviceRoomItemViewModel roomVm in AllRooms)
                {
                    PlacementEligibilityResult result = null;

                    if (allRooms.Count > 0)
                    {
                        string roomId = roomVm.Room != null ? roomVm.Room.RoomId : null;
                        if (map != null && roomId != null &&
                            map.TryGetValue(roomId, out PlacementEligibilityResult r))
                        {
                            result = r;
                        }
                        else
                        {
                            // We ran the pass but got nothing back for this room -> UNDETERMINED, never BLOCKED.
                            result = PlacementEligibilityResult.Undetermined(
                                "Eligibility could not be determined for this room.",
                                PlacementEligibilityStatusCodes.CalculationFailed);
                        }
                    }
                    // allRooms.Count == 0 -> nothing to evaluate; SetEligibility(null) resets the row to the
                    // eligible / not-yet-evaluated default, preserving the pre-preflight "all selectable" state.

                    bool wasEligible = roomVm.IsEligible;
                    roomVm.SetEligibility(result);

                    // Normalise selection to the authoritative verdict:
                    //   blocked / undetermined -> never selected;
                    //   just became eligible   -> selected;
                    //   already eligible       -> keep the user's manual choice.
                    if (!roomVm.IsEligible)
                        roomVm.IsSelected = false;
                    else if (!wasEligible)
                        roomVm.IsSelected = true;
                }

                OnPropertyChanged(nameof(AreAllSelectableRoomsSelected));
                OnPropertyChanged(nameof(RoomSelectionToggleLabel));
                if (ShowEligibleRoomsOnly) RoomsView.Refresh();
                RaiseRoomCounts();
            }
            finally
            {
                _isEligibilityRefreshing = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}
