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

        private bool _isEligibilityRefreshing;

        /// <summary>True while an eligibility sweep is queued or running (T1.1 coalescing).</summary>
        private bool _eligibilityRefreshPending;

        /// <summary>
        /// Bumped when a trigger is coalesced away, so the running pass knows its result is already
        /// stale and schedules exactly one follow-up (T1.1).
        /// </summary>
        private int _eligibilityInputsVersion;

        // T1.2 idempotence memory: the catalog instance and mode this tab last reacted to.
        private ICatalog _lastHandledCatalog;
        private bool _lastHandledCatalogFileMode;
        private bool _hasHandledCatalog;
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

        public abstract string DeviceDisplayName { get; }

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
                    bool previousSuppress = _suppressEligibilityRefresh;
                    _suppressEligibilityRefresh = true;
                    try
                    {
                        RefreshDeviceTypesForSelectedFamily();
                        ValidateSelectedTypeForCurrentFamily();
                        OnPropertyChanged(nameof(IsDeviceFamilySelected));
                        OnPropertyChanged(nameof(IsDeviceTypeSelected));
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

        protected virtual void OnUniversalFamilyTypeChanged() { }

        protected virtual string DeriveAttribute(string key, string familyName, string typeName) => null;

        /// <summary>
        /// Warns that the selected family/type has no engineering data in the active catalog.
        ///
        /// WHY THIS IS NEEDED IN MODEL MODE
        /// -------------------------------
        /// A Revit model can supply family/type NAMES but never ratings: a fire-alarm FamilySymbol has
        /// no dBA, Candela, DetectorType, Mount or CeilingSlope parameter. So in Model mode those
        /// attributes are simply absent, the derived value is null, and the picker silently reverts to
        /// a free-text box. Placement then runs on a provisional value with nothing on screen saying
        /// so. This message names the gap instead of leaving the user to assume the blank field is
        /// meaningful.
        ///
        /// NULL when nothing is selected, or when the catalog DOES supply the data - it is an
        /// advisory, so it must stay out of the way in the common case. Implementations override
        /// <see cref="DescribeMissingCatalogData"/>, which is invoked only once a family/type is set.
        /// </summary>
        public string MissingCatalogDataMessage
        {
            get
            {
                if (!IsDeviceTypeSelected || Catalog == null || !Catalog.IsLoaded) return null;
                return DescribeMissingCatalogData();
            }
        }

        /// <summary>
        /// Returns a warning when the selected family/type has no usable rating, or null when it does.
        /// Each derived tab overrides this for its own attribute set.
        /// </summary>
        protected virtual string DescribeMissingCatalogData() => null;

        /// <summary>Standard wording so all three tabs say the same thing about the same situation.</summary>
        protected string BuildMissingCatalogDataWarning(string whatIsMissing, string consequence)
        {
            string family = SelectedDeviceFamily != null ? SelectedDeviceFamily.FamilyName : null;
            string type = SelectedDeviceType != null ? SelectedDeviceType.TypeName : null;

            return "No catalog data for " + (family ?? "<family>") + " / " + (type ?? "<type>") + ": "
                   + whatIsMissing + " " + consequence
                   + " Values come from the workbook, not the Revit model — load a catalog workbook "
                   + "with \"Browse...\" in the top bar to supply them.";
        }

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

        public int VisibleEligibleRoomCount
        {
            get
            {
                int count = 0;
                if (RoomsView != null)
                    foreach (object obj in RoomsView)
                        if (obj is DeviceRoomItemViewModel r && r.IsEligible) count++;
                return count;
            }
        }

        public int SelectedVisibleEligibleRoomCount
        {
            get
            {
                int count = 0;
                if (RoomsView != null)
                    foreach (object obj in RoomsView)
                        if (obj is DeviceRoomItemViewModel r && r.IsEligible && r.IsSelected) count++;
                return count;
            }
        }

        public int VisibleBlockedRoomCount
        {
            get
            {
                int count = 0;
                if (RoomsView != null)
                    foreach (object obj in RoomsView)
                        if (obj is DeviceRoomItemViewModel r && r.IsBlocked) count++;
                return count;
            }
        }

        public int VisibleUndeterminedRoomCount
        {
            get
            {
                int count = 0;
                if (RoomsView != null)
                    foreach (object obj in RoomsView)
                        if (obj is DeviceRoomItemViewModel r && r.IsUndetermined) count++;
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
                int eligible = VisibleEligibleRoomCount;
                int blocked = VisibleBlockedRoomCount;
                int undetermined = VisibleUndeterminedRoomCount;
                string baseText = visible + (visible == 1 ? " room shown" : " rooms shown");
                var parts = new List<string>();
                if (eligible > 0) parts.Add(eligible + " eligible");
                if (blocked > 0) parts.Add(blocked + " blocked");
                if (undetermined > 0) parts.Add(undetermined + " undetermined");
                if (parts.Count == 0) return baseText;
                return baseText + " (" + string.Join(", ", parts) + ")";
            }
        }

        public string RoomsSelectedSummary
        {
            get
            {
                int selected = SelectedVisibleEligibleRoomCount;
                int eligible = VisibleEligibleRoomCount;
                int blocked = VisibleBlockedRoomCount;
                int undetermined = VisibleUndeterminedRoomCount;
                string baseSummary = selected + " of " + eligible + " eligible rooms selected";
                var extra = new List<string>();
                if (blocked > 0) extra.Add(blocked + " blocked");
                if (undetermined > 0) extra.Add(undetermined + " undetermined");
                return extra.Count == 0 ? baseSummary : baseSummary + " (" + string.Join(", ", extra) + ")";
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

        public string PlacementProgressText
        {
            get => _placementProgressText;
            private set { _placementProgressText = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPlacementProgressVisible)); }
        }

        public bool IsPlacementProgressVisible => !string.IsNullOrEmpty(PlacementProgressText);

        public bool CanCancelPlacement
        {
            get => _canCancelPlacement;
            private set { _canCancelPlacement = value; OnPropertyChanged(); }
        }

        public bool IsRunLogVisible => RunLogEntries.Count > 0;

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

        private bool CanExecuteCancelPlacement() => IsPlacementRunning && CanCancelPlacement && _progressReporter != null;

        private void ExecuteCancelPlacement()
        {
            if (_progressReporter == null) return;
            _progressReporter.RequestCancel();
            CanCancelPlacement = false;
            PlacementProgressText = "Cancelling - finishing the current room, then rolling back...";
            CommandManager.InvalidateRequerySuggested();
        }

        public string[] ExistingDevicePolicyOptionLabels => ExistingDevicePolicyOptions.Labels;

        public string ExistingDevicePolicyTooltip => ExistingDevicePolicyOptions.Explanation;

        public string CeilingColumnHeader => "Ceiling Height " + UnitDisplay.HeaderSuffix;
        public string AreaColumnHeader => "Area (sq ft)";
        public string UnitSuffix => UnitDisplay.Suffix;

        // -------------------------------------------------------------------------------------------
        // Universal geometry overrides (grid-center placement + S->W). Applies to ALL device tabs
        // (smoke + notification), so they live on the base and propagate through the same
        // universal -> level-default -> room mechanism the device attributes use. Text-typed: blank
        // means "use the readable Revit ceiling grid / rule-set default", parsed to double? at build.
        // -------------------------------------------------------------------------------------------
        private string _maxDistanceToWallInput;
        private string _tileUInput;
        private string _tileVInput;

        public string WallDistanceLabel => "Max S→W " + UnitDisplay.HeaderSuffix;
        public string TileSizeLabel => "Ceiling tile (U × V) " + UnitDisplay.HeaderSuffix;

        public string MaxDistanceToWallTooltip =>
            "S→W: maximum device-to-wall distance. Leave blank to use the rule-set NFPA half-spacing default. Provisional — not clamped to a verified table.";

        public string TileSizeTooltip =>
            "Acoustic-tile fallback size used to center devices when Revit has no readable ceiling grid. Leave blank to use the Revit-read grid.";

        public string MaxDistanceToWallInput
        {
            get => _maxDistanceToWallInput;
            set
            {
                string old = _maxDistanceToWallInput;
                if (SetProperty(ref _maxDistanceToWallInput, value))
                {
                    PropagateUniversalDefault("MaxDistanceToWall", old, value);
                }
            }
        }

        public string TileUInput
        {
            get => _tileUInput;
            set
            {
                string old = _tileUInput;
                if (SetProperty(ref _tileUInput, value))
                {
                    PropagateUniversalDefault("TileU", old, value);
                }
            }
        }

        public string TileVInput
        {
            get => _tileVInput;
            set
            {
                string old = _tileVInput;
                if (SetProperty(ref _tileVInput, value))
                {
                    PropagateUniversalDefault("TileV", old, value);
                }
            }
        }

        public string SelectedExistingDevicePolicyLabel
        {
            get => _selectedExistingDevicePolicyLabel;
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

        private IEnumerable<DeviceRoomItemViewModel> BulkTargets => AllRooms.Where(r => r.IsSelected && r.IsEligible);

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
            bool previousSuppress = _suppressEligibilityRefresh;
            _suppressEligibilityRefresh = true;
            try
            {
                DeviceFamilies.Clear();
                DeviceTypes.Clear();
                SelectedDeviceFamily = null;
                SelectedDeviceType = null;

                IReadOnlyList<DeviceFamilyOption> families = GetCatalogFamilyOptions();

                if ((families == null || families.Count == 0) && CanFallBackToModelFamilies())
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
        /// Whether an empty catalog may be back-filled from the live Revit model.
        ///
        /// True only in Model mode (or when there is no <see cref="CatalogViewModel"/> at all, which
        /// is the designer/test-host case).
        ///
        /// This matters most for the two alarm tabs: smoke detectors and notification appliances
        /// BOTH enumerate <c>OST_FireAlarmDevices</c>, so a workbook that has only a
        /// <c>Sprinklers</c> sheet and no <c>SmokeDetectors</c> sheet used to yield an empty
        /// catalog for that tab and then silently fall back to EVERY fire-alarm family in the
        /// project. The dropdown would contradict the source the user had chosen. In CatalogFile
        /// mode an empty sheet must read as empty.
        /// </summary>
        private bool CanFallBackToModelFamilies()
        {
            if (_deviceFamilySource == null) return false;
            if (_catalogVm == null) return true;
            return !_catalogVm.IsCatalogFileMode;
        }

        protected virtual IReadOnlyList<DeviceFamilyOption> GetCatalogFamilyOptions() => new List<DeviceFamilyOption>();

        protected virtual IReadOnlyList<string> GetCatalogTypesForFamily(string familyName) => new List<string>();

        protected ICatalog Catalog => _catalogVm != null ? _catalogVm.Catalog : null;

        public bool IsCatalogLoaded
        {
            get
            {
                IReadOnlyList<DeviceFamilyOption> families = GetCatalogFamilyOptions();
                return families != null && families.Count > 0;
            }
        }

        /// <summary>
        /// The workbook sheet this tab reads. Overridden per tab so the empty-state message can name
        /// the exact sheet the user must add rows to, instead of a vague "no data".
        /// </summary>
        protected virtual string CatalogSheetName => null;

        /// <summary>
        /// Explains an EMPTY dropdown rather than leaving it blank and unexplained.
        ///
        /// The three failure modes are genuinely different and were previously conflated into one
        /// message that always told the user to "pick the catalog workbook", which is wrong advice in
        /// Model mode and gave no hint when a workbook was simply missing this tab's sheet:
        ///   1. no catalog at all,
        ///   2. catalog loaded but this tab's sheet has no rows,
        ///   3. Model mode with no families of this kind in the open document.
        /// </summary>
        public string CatalogStatusMessage
        {
            get
            {
                string device = DeviceDisplayName.ToLowerInvariant();
                bool catalogFileMode = _catalogVm != null && _catalogVm.IsCatalogFileMode;

                if (_catalogVm == null || _catalogVm.Catalog == null || !_catalogVm.Catalog.IsLoaded)
                {
                    return catalogFileMode
                        ? "No catalog workbook loaded — pick one with Browse... in the top bar."
                        : "No " + device + " families found in the open model — load families with "
                          + "\"Load families...\" in the header, or switch to Catalog file.";
                }

                if (!IsCatalogLoaded)
                {
                    if (!catalogFileMode)
                    {
                        return "No " + device + " families found in the open model — load families with "
                               + "\"Load families...\" in the header, or switch to Catalog file.";
                    }

                    return string.IsNullOrEmpty(CatalogSheetName)
                        ? "The loaded workbook has no rows for " + device + "."
                        : "The loaded workbook has no rows on its \"" + CatalogSheetName
                          + "\" sheet, so there are no " + device + " options.";
                }

                return null;
            }
        }

        protected void SeedPerRowDeviceDefaults()
        {
            IReadOnlyList<string> familyNames = DeviceFamilies
                .Where(f => f != null && !string.IsNullOrWhiteSpace(f.FamilyName))
                .Select(f => f.FamilyName)
                .ToList();

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

            // Listen for the SOURCE MODE as well as the derived catalog facts.
            //
            // IsCatalogFileMode drives two things the other properties do not: which families may
            // legally be listed (CanFallBackToModelFamilies), and the wording of CatalogStatusMessage.
            // Without it a mode switch that produced an equivalent-looking catalog would leave the
            // dropdown populated from the previous source.
            //
            // This is the same property set the sprinkler tab reacts to; the two used to disagree,
            // which meant a change could refresh one tab and leave the other stale.
            //
            // The filter stays WIDE on purpose. The idempotence check below makes the extra
            // notifications cheap; narrowing the filter would be fragile, because a property added to
            // SetCatalog later would then silently stop refreshing these dropdowns.
            if (e.PropertyName != nameof(CatalogViewModel.Catalog)
                && e.PropertyName != nameof(CatalogViewModel.IsLoaded)
                && e.PropertyName != nameof(CatalogViewModel.TotalRowCount)
                && e.PropertyName != nameof(CatalogViewModel.CatalogVersion)
                && e.PropertyName != nameof(CatalogViewModel.SourcePath)
                && e.PropertyName != nameof(CatalogViewModel.IsCatalogFileMode))
                return;

            // IDEMPOTENCE (T1.2). SetCatalog raises EIGHT PropertyChanged events and SourceMode adds
            // IsCatalogFileMode, so one mode switch arrived here ~7 times - each time rebuilding every
            // dropdown and running a full eligibility sweep. Comparing the ACTUAL catalog instance and
            // mode (rather than trimming the property list) keeps this correct if SetCatalog grows
            // another property later.
            ICatalog currentCatalog = _catalogVm != null ? _catalogVm.Catalog : null;
            bool currentMode = _catalogVm != null && _catalogVm.IsCatalogFileMode;

            if (_hasHandledCatalog
                && ReferenceEquals(currentCatalog, _lastHandledCatalog)
                && currentMode == _lastHandledCatalogFileMode)
            {
                return;
            }

            _lastHandledCatalog = currentCatalog;
            _lastHandledCatalogFileMode = currentMode;
            _hasHandledCatalog = true;

            LoadDeviceFamilies();

            bool previousSuppress = _suppressEligibilityRefresh;
            _suppressEligibilityRefresh = true;
            try
            {
                SeedPerRowDeviceDefaults();
                OnCatalogChanged();
                ApplyDefaultSelection();
            }
            finally
            {
                _suppressEligibilityRefresh = previousSuppress;
            }

            OnPropertyChanged(nameof(IsCatalogLoaded));
            OnPropertyChanged(nameof(CatalogStatusMessage));
            OnPropertyChanged(nameof(MissingCatalogDataMessage));
            CommandManager.InvalidateRequerySuggested();
            RefreshEligibility();
        }

        protected virtual void OnCatalogChanged() { }

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

        protected void PropagateUniversalDefault(string key, string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(key)) return;

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

        public virtual void OpenLevelSettings(DeviceLevelItemViewModel level) { }

        public virtual bool HasLevelSettings => false;

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
                    if (!roomVm.IsEligible) continue;
                    if (roomVm.Room == null) continue;

                    list.Add(BuildRoomInputItem(levelVm.Level, roomVm));
                }
            }

            return list;
        }

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
                SelectedFamilyName = effectiveFamily,
                SelectedTypeName = effectiveType,

                DetectorType = EffectiveDeviceAttribute(roomVm, "DetectorType", effectiveFamily, effectiveType),
                Mount = EffectiveDeviceAttribute(roomVm, "Mount", effectiveFamily, effectiveType),
                CeilingSlope = EffectiveDeviceAttribute(roomVm, "CeilingSlope", effectiveFamily, effectiveType),
                ApplianceType = EffectiveDeviceAttribute(roomVm, "ApplianceType", effectiveFamily, effectiveType),
                CandelaDba = EffectiveDeviceAttribute(roomVm, "CandelaDba", effectiveFamily, effectiveType),

                OverrideMaxDistanceToWallFt = ParseNullableDouble(roomVm.GetEffective("MaxDistanceToWall")),
                OverrideCeilingTileUFt = ParseNullableDouble(roomVm.GetEffective("TileU")),
                OverrideCeilingTileVFt = ParseNullableDouble(roomVm.GetEffective("TileV")),

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
                RefreshEligibility();
            }
        }

        protected static string GetEffectiveAttribute(DeviceRoomItemViewModel roomVm, string key)
        {
            if (roomVm == null || string.IsNullOrEmpty(key)) return null;
            return roomVm.GetEffective(key);
        }

        /// <summary>
        /// Parses a user-entered override string ("S->W" / tile size) into feet. Blank / unparseable => null,
        /// which the engine reads as "use the readable Revit grid or the rule-set default". Invariant culture
        /// so a period is always the decimal separator, matching how the values are stored.
        /// </summary>
        private static double? ParseNullableDouble(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v)
                ? (double?)v
                : null;
        }

        protected void ApplyDefaultSelection()
        {
            foreach (DeviceLevelItemViewModel level in Levels)
            {
                if (!level.HasRooms) continue;
                level.IsSelected = true;

                foreach (DeviceRoomItemViewModel room in level.Rooms)
                {
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
            OnPropertyChanged(nameof(VisibleEligibleRoomCount));
            OnPropertyChanged(nameof(SelectedVisibleEligibleRoomCount));
            OnPropertyChanged(nameof(VisibleBlockedRoomCount));
            OnPropertyChanged(nameof(VisibleUndeterminedRoomCount));
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
                        room.ClearAllOverrides();
                    }
                }

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

        protected void InitializeEligibility()
        {
            _suppressEligibilityRefresh = false;
            RefreshEligibility();
        }

        private void RefreshEligibility()
        {
            if (IsBackendPending) return;
            if (_suppressEligibilityRefresh) return;

            // COALESCING (T1.1). RevitApiContext.Run ENQUEUES and never de-duplicates, so a burst of
            // triggers used to enqueue one complete sweep per trigger. A sweep probes every room, so
            // the queue drained for a long time on the thread that also pumps the UI.
            //
            // If a pass is already queued, return: it reads the CURRENT property values when it runs.
            // Bumping the version here (not at each call site) makes the running pass re-arm, and it
            // cannot be forgotten by a future edit.
            if (_eligibilityRefreshPending)
            {
                _eligibilityInputsVersion++;
                return;
            }

            _eligibilityRefreshPending = true;

            _isEligibilityRefreshing = true;
            CommandManager.InvalidateRequerySuggested();

            RevitApi.Run(RefreshEligibilityCore, OnEligibilityRefreshFailed);
        }

        private void OnEligibilityRefreshFailed(Exception ex)
        {
            // MUST clear the pending flag. If it were left set, one transient failure would disable
            // eligibility refresh for the rest of the session - worse than the freeze being fixed.
            _eligibilityRefreshPending = false;
            _isEligibilityRefreshing = false;
            FireProtectionLog.Warn(DeviceDisplayName + ": eligibility preflight could not run — "
                + (ex != null ? ex.Message : "unknown error") + ". Rooms left selectable.");
            CommandManager.InvalidateRequerySuggested();
        }

        private void RefreshEligibilityCore()
        {
            int versionAtStart = _eligibilityInputsVersion;
            DateTime startedAt = DateTime.UtcNow;

            try
            {
                RunEligibilityPass();
            }
            finally
            {
                _eligibilityRefreshPending = false;
                _isEligibilityRefreshing = false;

                double elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;
                FireProtectionLog.Info(
                    DeviceDisplayName + ": eligibility pass complete in " + Math.Round(elapsedMs)
                    + " ms across " + AllRooms.Count + " room(s).");

                CommandManager.InvalidateRequerySuggested();

                if (_eligibilityInputsVersion != versionAtStart) RefreshEligibility();
            }
        }

        /// <summary>
        /// The actual per-room device eligibility sweep, split out so the coalescing and timing
        /// bookkeeping in <see cref="RefreshEligibilityCore"/> wraps one clear region.
        /// </summary>
        private void RunEligibilityPass()
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
                            result = PlacementEligibilityResult.Undetermined(
                                "Eligibility could not be determined for this room.",
                                PlacementEligibilityStatusCodes.CalculationFailed);
                        }
                    }

                    bool wasEligible = roomVm.IsEligible;
                    roomVm.SetEligibility(result);

                    if (!roomVm.IsEligible)
                        roomVm.IsSelected = false;
                    else if (!wasEligible)
                        roomVm.IsSelected = true;
                }

                int blockedCount = AllRooms.Count(r => r.IsBlocked);
                if (blockedCount > 0 && !AllRooms.Any(r => r.IsSelected && r.IsEligible))
                {
                    bool isBeam = SelectedDeviceFamily != null &&
                        (SelectedDeviceFamily.FamilyName ?? string.Empty).IndexOf("Beam", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (isBeam)
                    {
                        PlacementStatusMessage = $"{blockedCount} room(s) cannot satisfy the minimum optical path length (15 ft) for beam detectors. Select a point-type detector family (e.g., Photoelectric) for smaller spaces.";
                    }
                    else
                    {
                        PlacementStatusMessage = $"{blockedCount} room(s) are blocked for the selected device family. Select a valid device family to place.";
                    }
                }
                else if (_deviceExecutor == null)
                {
                    PlacementStatusMessage = "Device placement backend is pending — room/level selection is for layout planning only.";
                }
                else
                {
                    PlacementStatusMessage = null;
                }
                OnPropertyChanged(nameof(AreAllSelectableRoomsSelected));
                OnPropertyChanged(nameof(RoomSelectionToggleLabel));
                if (ShowEligibleRoomsOnly) RoomsView.Refresh();
                RaiseRoomCounts();
        }
    }
}