using FireProtection.UI.Models;
using FireProtection.UI.Models.Sprinklers.BruteForce;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Common;
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

namespace FireProtection.UI.ViewModels.Devices
{
    /// <summary>
    /// Device-agnostic base for the "pick rooms/levels + family/type, then place" workflow. Shared by
    /// sprinklers (conceptually), smoke detectors, and notification appliances so the common UI logic
    /// lives in exactly one place. This is the UI-first slice: the geometric selection/filtering/toggle
    /// logic is fully implemented and reusable, but the actual device placement is deferred — the
    /// placement command is disabled and routed through the <see cref="IDevicePlacementExecutor"/> seam.
    ///
    /// Sprinkler files are intentionally NOT modified; the existing SprinklerBruteForceViewModel keeps its
    /// own (hazard-class-aware) implementation. This base is what the new device tabs inherit.
    /// </summary>
    public abstract class DevicePlacementViewModelBase : ObservableObject
    {
        private readonly IDeviceFamilySource _deviceFamilySource;
        private readonly IDevicePlacementExecutor _deviceExecutor;

        private string _levelSearchText;
        private string _roomSearchText;
        private bool _hideUnselectedLevels;
        private bool _hideUnselectedRooms;
        private bool _showEligibleRoomsOnly;
        private bool _showLevelsWithRoomsOnly;
        private bool _isPlacementRunning;
        private string _placementStatusMessage;

        private DeviceFamilyOption _selectedDeviceFamily;
        private DeviceTypeOption _selectedDeviceType;

        protected DevicePlacementViewModelBase(
            FireProtectionUiData data,
            IDeviceFamilySource deviceFamilySource = null,
            IDevicePlacementExecutor deviceExecutor = null)
        {
            Data = data;
            _deviceFamilySource = deviceFamilySource;
            _deviceExecutor = deviceExecutor;

            Levels = new ObservableCollection<DeviceLevelItemViewModel>();
            AllRooms = new ObservableCollection<DeviceRoomItemViewModel>();
            DeviceFamilies = new ObservableCollection<DeviceFamilyOption>();
            DeviceTypes = new ObservableCollection<DeviceTypeOption>();

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

            RefreshEligibility();
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

        public FireProtectionUiData Data { get; }

        public ObservableCollection<DeviceLevelItemViewModel> Levels { get; }
        public ObservableCollection<DeviceRoomItemViewModel> AllRooms { get; }

        public ObservableCollection<DeviceFamilyOption> DeviceFamilies { get; }
        public ObservableCollection<DeviceTypeOption> DeviceTypes { get; }

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
                    RefreshDeviceTypesForSelectedFamily();
                    ValidateSelectedTypeForCurrentFamily();
                    OnPropertyChanged(nameof(IsDeviceFamilySelected));
                    OnPropertyChanged(nameof(IsDeviceTypeSelected));
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
                    CommandManager.InvalidateRequerySuggested();
                    RefreshEligibility();
                }
            }
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

        private void LoadDeviceFamilies()
        {
            DeviceFamilies.Clear();
            DeviceTypes.Clear();
            SelectedDeviceFamily = null;
            SelectedDeviceType = null;

            if (_deviceFamilySource == null) return;

            IReadOnlyList<DeviceFamilyOption> families = _deviceFamilySource.GetAvailableFamilies();
            if (families == null) return;

            foreach (DeviceFamilyOption family in families)
            {
                if (family != null)
                    DeviceFamilies.Add(family);
            }
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
            if (SelectedDeviceType == null) return;
            if (SelectedDeviceFamily == null)
            {
                SelectedDeviceType = null;
                return;
            }

            bool stillValid = DeviceTypes.Any(t =>
                string.Equals(t.FamilyName, SelectedDeviceType.FamilyName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.TypeName, SelectedDeviceType.TypeName, StringComparison.OrdinalIgnoreCase));

            if (!stillValid)
                SelectedDeviceType = null;
        }

        private bool CanExecutePlaceDevices()
        {
            if (IsBackendPending) return false;
            if (IsPlacementRunning) return false;
            if (SelectedVisibleEligibleRoomCount == 0) return false;
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
                MessageBox.Show(
                    "Device placement backend is not yet implemented. Room/level selection is available for planning only.",
                    DeviceDisplayName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (IsPlacementRunning) return;

            IsPlacementRunning = true;
            PlacementStatusMessage = "Placing devices...";

            try
            {
                List<DeviceRoomInputItem> roomSelections = CollectSelectedRooms();

                if (roomSelections.Count == 0)
                {
                    PlacementStatusMessage = "No selected rooms with usable geometry.";
                    return;
                }

                PlacementRunReport report = _deviceExecutor.ExecutePlacement(roomSelections);
                LastDeviceResult = report;

                PlacementStatusMessage =
                    $"Processed {report.RoomsProcessed} room(s); {report.RoomsSucceeded} succeeded, " +
                    $"{report.RoomsFailed} failed.";
            }
            catch (Exception ex)
            {
                PlacementStatusMessage = "Placement failed: " + ex.Message;
            }
            finally
            {
                IsPlacementRunning = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public PlacementRunReport LastDeviceResult { get; private set; }

        private List<DeviceRoomInputItem> CollectSelectedRooms()
        {
            List<DeviceRoomInputItem> list = new List<DeviceRoomInputItem>();

            foreach (DeviceLevelItemViewModel levelVm in Levels)
            {
                if (!levelVm.IsSelected) continue;

                LevelUiData levelData = levelVm.Level;

                foreach (DeviceRoomItemViewModel roomVm in levelVm.Rooms)
                {
                    if (!roomVm.IsSelected) continue;
                    if (!roomVm.IsEligible) continue;

                    RoomUiData roomData = roomVm.Room;
                    if (roomData == null) continue;

                    List<double[]> polyCopy = new List<double[]>();
                    if (roomData.Geometry != null && roomData.Geometry.Polygon != null)
                    {
                        foreach (double[] v in roomData.Geometry.Polygon)
                        {
                            if (v != null && v.Length >= 2)
                                polyCopy.Add(new double[] { v[0], v[1] });
                        }
                    }

                    list.Add(new DeviceRoomInputItem
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
                        SelectedFamilyName = SelectedDeviceFamily?.FamilyName,
                        SelectedTypeName = SelectedDeviceType?.TypeName
                    });
                }
            }

            return list;
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
            if (ShowEligibleRoomsOnly && !room.IsEligible) return false;
            if (room.ParentLevel == null || !room.ParentLevel.IsSelected) return false;
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
                        room.IsSelected = room.IsEligible ? select : false;
                    }
                }

                OnPropertyChanged(nameof(AreAllSelectableLevelsSelected));
                OnPropertyChanged(nameof(LevelSelectionToggleLabel));
                OnPropertyChanged(nameof(AreAllSelectableRoomsSelected));
                OnPropertyChanged(nameof(RoomSelectionToggleLabel));
            }
        }

        private void OnRoomItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeviceRoomItemViewModel.IsSelected))
            {
                OnPropertyChanged(nameof(AreAllSelectableRoomsSelected));
                OnPropertyChanged(nameof(RoomSelectionToggleLabel));
            }
        }

        /// <summary>
        /// Evaluates placement eligibility for a single room. In the UI-first slice (no device backend) every
        /// room is reported Eligible so the user can plan layouts; the action remains disabled via
        /// <see cref="IsBackendPending"/>. When the device backend lands, override this to run the real
        /// NFPA-72 preflight and return BLOCKED / UNDETERMINED as appropriate.
        /// </summary>
        protected virtual PlacementEligibilityResult EvaluateRoomEligibility(DeviceRoomItemViewModel roomVm)
        {
            return PlacementEligibilityResult.Eligible(new PlacementEligibilityResult
            {
                Reason = "Device backend pending — selectable for layout planning only."
            });
        }

        private void RefreshEligibility()
        {
            foreach (DeviceRoomItemViewModel roomVm in AllRooms)
            {
                PlacementEligibilityResult result = EvaluateRoomEligibility(roomVm);
                bool wasEligible = roomVm.IsEligible;
                roomVm.SetEligibility(result);

                if (!roomVm.IsEligible)
                    roomVm.IsSelected = false;
                else if (!wasEligible)
                    roomVm.IsSelected = true;
            }

            OnPropertyChanged(nameof(AreAllSelectableRoomsSelected));
            OnPropertyChanged(nameof(RoomSelectionToggleLabel));
        }

        private void ApplyDefaultSelection()
        {
            foreach (DeviceLevelItemViewModel level in Levels)
            {
                if (!level.HasRooms) continue;
                level.IsSelected = true;

                foreach (DeviceRoomItemViewModel room in level.Rooms)
                {
                    if (!room.IsEligible) continue;
                    room.IsSelected = true;
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
        }

        private void Reset()
        {
            LevelSearchText = null;
            RoomSearchText = null;
            HideUnselectedLevels = false;
            HideUnselectedRooms = false;

            SelectedDeviceFamily = null;
            SelectedDeviceType = null;
            DeviceTypes.Clear();

            foreach (DeviceLevelItemViewModel level in Levels)
            {
                level.IsSelected = false;
                foreach (DeviceRoomItemViewModel room in level.Rooms)
                {
                    room.IsSelected = false;
                }
            }

            PlacementStatusMessage = null;
            RefreshEligibility();
            ApplyDefaultSelection();
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
