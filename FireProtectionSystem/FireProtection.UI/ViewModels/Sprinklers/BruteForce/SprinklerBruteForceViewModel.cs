using FireProtection.UI.Models;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Common;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace FireProtection.UI.ViewModels.Sprinklers.BruteForce
{
    public class SprinklerBruteForceViewModel : ObservableObject
    {
        private readonly IPlacementExecutor _placementExecutor;
        private readonly ISprinklerFamilySource _sprinklerFamilySource;

        private string _levelSearchText;
        private string _roomSearchText;
        private bool _hideUnselectedLevels;
        private bool _hideUnselectedRooms;
        private bool _isPlacementRunning;
        private string _placementStatusMessage;

        private SprinklerFamilyOption _selectedSprinklerFamily;
        private SprinklerTypeOption _selectedSprinklerType;

        public SprinklerBruteForceViewModel()
            : this(null, null, null)
        {
        }

        public SprinklerBruteForceViewModel(FireProtectionUiData data)
            : this(data, null, null)
        {
        }

        public SprinklerBruteForceViewModel(
            FireProtectionUiData data,
            IPlacementExecutor placementExecutor)
            : this(data, placementExecutor, null)
        {
        }

        public SprinklerBruteForceViewModel(
            FireProtectionUiData data,
            IPlacementExecutor placementExecutor,
            ISprinklerFamilySource sprinklerFamilySource)
        {
            Data = data;
            _placementExecutor = placementExecutor;
            _sprinklerFamilySource = sprinklerFamilySource;

            Levels = new ObservableCollection<LevelItemViewModel>();
            AllRooms = new ObservableCollection<RoomItemViewModel>();
            SprinklerFamilies = new ObservableCollection<SprinklerFamilyOption>();
            SprinklerTypes = new ObservableCollection<SprinklerTypeOption>();

            if (data != null && data.Levels != null)
            {
                foreach (LevelUiData level in data.Levels)
                {
                    LevelItemViewModel levelVm = new LevelItemViewModel(level);
                    levelVm.SelectionChanged += OnLevelSelectionChanged;

                    foreach (RoomItemViewModel room in levelVm.Rooms)
                    {
                        room.SelectionChanged += OnRoomSelectionChanged;
                        AllRooms.Add(room);
                    }

                    Levels.Add(levelVm);
                }
            }

            LoadSprinklerFamilies();

            LevelsView = CollectionViewSource.GetDefaultView(Levels);
            LevelsView.Filter = FilterLevel;

            RoomsView = CollectionViewSource.GetDefaultView(AllRooms);
            RoomsView.Filter = FilterRoom;
            RoomsView.SortDescriptions.Add(new SortDescription(nameof(RoomItemViewModel.LevelName), ListSortDirection.Ascending));
            RoomsView.SortDescriptions.Add(new SortDescription(nameof(RoomItemViewModel.Number), ListSortDirection.Ascending));

            ToggleHideUnselectedLevelsCommand = new RelayCommand(_ => HideUnselectedLevels = !HideUnselectedLevels);
            ToggleHideUnselectedRoomsCommand = new RelayCommand(_ => HideUnselectedRooms = !HideUnselectedRooms);

            SelectAllVisibleLevelsCommand = new RelayCommand(_ => SetSelectionOnVisibleLevels(true));
            ClearVisibleLevelSelectionCommand = new RelayCommand(_ => SetSelectionOnVisibleLevels(false));

            SelectAllVisibleRoomsCommand = new RelayCommand(_ => SetSelectionOnVisibleRooms(true));
            ClearVisibleRoomSelectionCommand = new RelayCommand(_ => SetSelectionOnVisibleRooms(false));

            PlaceSprinklersCommand = new RelayCommand(
                _ => ExecutePlaceSprinklers(),
                _ => CanExecutePlaceSprinklers());

            ResetCommand = new RelayCommand(_ => Reset());
        }

        public FireProtectionUiData Data { get; }

        public ObservableCollection<LevelItemViewModel> Levels { get; }
        public ObservableCollection<RoomItemViewModel> AllRooms { get; }

        public ObservableCollection<SprinklerFamilyOption> SprinklerFamilies { get; }
        public ObservableCollection<SprinklerTypeOption> SprinklerTypes { get; }

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

        public SprinklerFamilyOption SelectedSprinklerFamily
        {
            get => _selectedSprinklerFamily;
            set
            {
                if (SetProperty(ref _selectedSprinklerFamily, value))
                {
                    RefreshSprinklerTypesForSelectedFamily();
                    ValidateSelectedTypeForCurrentFamily();
                    OnPropertyChanged(nameof(IsSprinklerFamilySelected));
                    OnPropertyChanged(nameof(IsSprinklerTypeSelected));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public SprinklerTypeOption SelectedSprinklerType
        {
            get => _selectedSprinklerType;
            set
            {
                if (SetProperty(ref _selectedSprinklerType, value))
                {
                    OnPropertyChanged(nameof(IsSprinklerTypeSelected));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsSprinklerFamilySelected => SelectedSprinklerFamily != null;
        public bool IsSprinklerTypeSelected => SelectedSprinklerType != null;

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
                        if (obj is RoomItemViewModel r && r.IsSelected) count++;
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
                    LevelItemViewModel only = Levels.First(l => l.IsSelected);
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

        public string RoomsSelectedSummary =>
            SelectedVisibleRoomCount + " of " + VisibleRoomCount + " rooms selected";

        public ICommand ToggleHideUnselectedLevelsCommand { get; }
        public ICommand ToggleHideUnselectedRoomsCommand { get; }

        public ICommand SelectAllVisibleLevelsCommand { get; }
        public ICommand ClearVisibleLevelSelectionCommand { get; }

        public ICommand SelectAllVisibleRoomsCommand { get; }
        public ICommand ClearVisibleRoomSelectionCommand { get; }

        public ICommand PlaceSprinklersCommand { get; }
        public ICommand ResetCommand { get; }

        private void LoadSprinklerFamilies()
        {
            SprinklerFamilies.Clear();
            SprinklerTypes.Clear();
            SelectedSprinklerFamily = null;
            SelectedSprinklerType = null;

            if (_sprinklerFamilySource == null) return;

            IReadOnlyList<SprinklerFamilyOption> families = _sprinklerFamilySource.GetAvailableFamilies();
            if (families == null) return;

            foreach (SprinklerFamilyOption family in families)
            {
                if (family != null)
                    SprinklerFamilies.Add(family);
            }
        }

        private void RefreshSprinklerTypesForSelectedFamily()
        {
            SprinklerTypes.Clear();

            if (SelectedSprinklerFamily == null || SelectedSprinklerFamily.Types == null)
                return;

            foreach (SprinklerTypeOption type in SelectedSprinklerFamily.Types)
            {
                if (type != null)
                    SprinklerTypes.Add(type);
            }
        }

        private void ValidateSelectedTypeForCurrentFamily()
        {
            if (SelectedSprinklerType == null) return;
            if (SelectedSprinklerFamily == null)
            {
                SelectedSprinklerType = null;
                return;
            }

            bool stillValid = SprinklerTypes.Any(t =>
                string.Equals(t.FamilyName, SelectedSprinklerType.FamilyName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.TypeName, SelectedSprinklerType.TypeName, StringComparison.OrdinalIgnoreCase));

            if (!stillValid)
                SelectedSprinklerType = null;
        }

        private bool CanExecutePlaceSprinklers()
        {
            if (IsPlacementRunning) return false;
            if (_placementExecutor == null) return false;
            if (SelectedRoomCount == 0) return false;
            if (SelectedSprinklerFamily == null) return false;
            if (SelectedSprinklerType == null) return false;
            if (!string.Equals(SelectedSprinklerType.FamilyName, SelectedSprinklerFamily.FamilyName, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private void ExecutePlaceSprinklers()
        {
            if (IsPlacementRunning) return;

            string validationMessage = ValidatePlacementInputs();
            if (!string.IsNullOrEmpty(validationMessage))
            {
                PlacementStatusMessage = validationMessage;
                MessageBox.Show(
                    validationMessage,
                    "Place Sprinklers",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            IsPlacementRunning = true;
            PlacementStatusMessage = "Placing sprinklers...";

            try
            {
                List<PlacementRequestItem> items = BuildPlacementRequests();

                if (items.Count == 0)
                {
                    PlacementStatusMessage = "No selected rooms with usable geometry.";
                    MessageBox.Show(
                        "No selected rooms have usable geometry.",
                        "Place Sprinklers",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                PlacementRunReport report = _placementExecutor.ExecutePlacement(items);
                ShowPlacementReport(report);
            }
            catch (Exception ex)
            {
                PlacementStatusMessage = "Placement failed: " + ex.Message;
                MessageBox.Show(
                    "Sprinkler placement failed:\n\n" + ex.Message,
                    "Place Sprinklers",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsPlacementRunning = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private string ValidatePlacementInputs()
        {
            if (_placementExecutor == null)
                return "Placement service is not available.";

            if (SelectedSprinklerFamily == null)
                return "Please select a sprinkler family.";

            if (SelectedSprinklerType == null)
                return "Please select a sprinkler type.";

            if (!string.Equals(SelectedSprinklerType.FamilyName, SelectedSprinklerFamily.FamilyName, StringComparison.OrdinalIgnoreCase))
                return "The selected sprinkler type does not belong to the selected sprinkler family.";

            if (SelectedRoomCount == 0)
                return "Please select at least one room.";

            bool selectedTypeExistsInCurrentFamily = SprinklerTypes.Any(t =>
                string.Equals(t.FamilyName, SelectedSprinklerType.FamilyName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.TypeName, SelectedSprinklerType.TypeName, StringComparison.OrdinalIgnoreCase));

            if (!selectedTypeExistsInCurrentFamily)
                return "The selected sprinkler type is no longer available for the selected family.";

            return null;
        }

        private List<PlacementRequestItem> BuildPlacementRequests()
        {
            List<PlacementRequestItem> list = new List<PlacementRequestItem>();

            string selectedFamilyName = SelectedSprinklerFamily != null ? SelectedSprinklerFamily.FamilyName : null;
            string selectedTypeName = SelectedSprinklerType != null ? SelectedSprinklerType.TypeName : null;

            foreach (LevelItemViewModel levelVm in Levels)
            {
                if (!levelVm.IsSelected) continue;

                LevelUiData levelData = levelVm.Level;

                foreach (RoomItemViewModel roomVm in levelVm.Rooms)
                {
                    if (!roomVm.IsSelected) continue;

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

                    list.Add(new PlacementRequestItem
                    {
                        LevelId = levelData.LevelId,
                        LevelName = levelData.Name,
                        LevelElevationFt = levelData.ElevationFt,

                        RoomId = roomData.RoomId,
                        RoomName = roomData.Name,
                        RoomNumber = roomData.Number,
                        AreaSqFt = roomData.AreaSqFt,
                        CeilingHeightFt = roomData.Geometry?.CeilingHeightFt,
                        Polygon = polyCopy,

                        EffectiveHazardClass = roomVm.SelectedHazardClass,

                        SelectedSprinklerFamilyName = selectedFamilyName,
                        SelectedSprinklerTypeName = selectedTypeName
                    });
                }
            }

            return list;
        }

        private void ShowPlacementReport(PlacementRunReport report)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Rooms processed: " + report.RoomsProcessed);
            sb.AppendLine("  Succeeded: " + report.RoomsSucceeded);
            sb.AppendLine("  Failed:    " + report.RoomsFailed);
            sb.AppendLine("  Skipped:   " + report.RoomsSkipped);
            sb.AppendLine();
            sb.AppendLine("Sprinklers placed: " + report.SprinklersPlaced
                          + " / " + report.SprinklersRequested);
            sb.AppendLine();
            sb.AppendLine("---- Per-room details ----");

            if (report.RoomReports != null)
            {
                foreach (PlacementRoomReport r in report.RoomReports)
                {
                    string header =
                        "[" + (r.Status ?? "?") + "] "
                        + (string.IsNullOrEmpty(r.LevelName) ? "<no level>" : r.LevelName)
                        + " / "
                        + (string.IsNullOrEmpty(r.RoomName) ? "<no name>" : r.RoomName)
                        + "  (points " + r.PointsPlaced + "/" + r.PointsRequested + ")";

                    sb.AppendLine(header);
                    if (!string.IsNullOrEmpty(r.Message))
                    {
                        sb.AppendLine("   " + r.Message);
                    }
                }
            }

            string summary = sb.ToString();
            PlacementStatusMessage =
                "Placed " + report.SprinklersPlaced + "/" + report.SprinklersRequested
                + " sprinklers across " + report.RoomsProcessed + " room(s).";

            MessageBox.Show(
                summary,
                "Place Sprinklers - Result",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private bool FilterLevel(object obj)
        {
            LevelItemViewModel level = obj as LevelItemViewModel;
            if (level == null) return false;
            if (HideUnselectedLevels && !level.IsSelected) return false;
            if (string.IsNullOrWhiteSpace(LevelSearchText)) return true;
            string search = LevelSearchText.Trim();
            return !string.IsNullOrEmpty(level.Name)
                   && level.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool FilterRoom(object obj)
        {
            RoomItemViewModel room = obj as RoomItemViewModel;
            if (room == null) return false;
            if (room.ParentLevel == null || !room.ParentLevel.IsSelected) return false;
            if (HideUnselectedRooms && !room.IsSelected) return false;
            return MatchesRoomSearch(room);
        }

        private bool MatchesRoomSearch(RoomItemViewModel room)
        {
            if (string.IsNullOrWhiteSpace(RoomSearchText)) return true;
            string search = RoomSearchText.Trim();
            bool nameMatch = !string.IsNullOrEmpty(room.Name)
                             && room.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
            bool numberMatch = !string.IsNullOrEmpty(room.Number)
                               && room.Number.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
            return nameMatch || numberMatch;
        }

        private void SetSelectionOnVisibleLevels(bool isSelected)
        {
            if (LevelsView == null) return;
            List<LevelItemViewModel> visible = new List<LevelItemViewModel>();
            foreach (object obj in LevelsView)
                if (obj is LevelItemViewModel level) visible.Add(level);

            foreach (LevelItemViewModel level in visible)
            {
                if (isSelected && !level.HasRooms) continue;
                level.IsSelected = isSelected;
            }
        }

        private void SetSelectionOnVisibleRooms(bool isSelected)
        {
            if (RoomsView == null) return;
            List<RoomItemViewModel> visible = new List<RoomItemViewModel>();
            foreach (object obj in RoomsView)
                if (obj is RoomItemViewModel room) visible.Add(room);
            foreach (RoomItemViewModel room in visible)
                room.IsSelected = isSelected;
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

            SelectedSprinklerFamily = null;
            SelectedSprinklerType = null;
            SprinklerTypes.Clear();

            foreach (LevelItemViewModel level in Levels)
            {
                level.IsSelected = false;
                foreach (RoomItemViewModel room in level.Rooms)
                {
                    room.IsSelected = false;
                    room.ResetHazardClassToDefault();
                }
            }

            PlacementStatusMessage = null;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}