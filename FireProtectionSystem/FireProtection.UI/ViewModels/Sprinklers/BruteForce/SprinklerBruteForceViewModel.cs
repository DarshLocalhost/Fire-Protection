using FireProtection.UI.Models;
using FireProtection.UI.Models.Sprinklers.BruteForce;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Common;
using FireProtection.UI.ViewModels.Sprinklers.BruteForce;
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

namespace FireProtection.UI.ViewModels.Sprinklers.BruteForce
{
    public class SprinklerBruteForceViewModel : ObservableObject
    {
        private readonly IPlacementInputExporter _placementInputExporter;
        private readonly ISprinklerFamilySource _sprinklerFamilySource;
        private readonly ISprinklerPlacementService _sprinklerPlacementService;
        private string _levelSearchText;
        private string _roomSearchText;
        private bool _hideUnselectedLevels;
        private bool _hideUnselectedRooms;
        private bool _showEligibleRoomsOnly;
        private bool _showLevelsWithRoomsOnly;
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
            ISprinklerFamilySource sprinklerFamilySource)
            : this(data, null, sprinklerFamilySource)
        {
        }

        public SprinklerBruteForceViewModel(
            FireProtectionUiData data,
            IPlacementInputExporter placementInputExporter,
            ISprinklerFamilySource sprinklerFamilySource)
            : this(data, placementInputExporter, sprinklerFamilySource, null)
        {
        }

        public SprinklerBruteForceViewModel(
            FireProtectionUiData data,
            IPlacementInputExporter placementInputExporter,
            ISprinklerFamilySource sprinklerFamilySource,
            ISprinklerPlacementService sprinklerPlacementService)
        {
            Data = data;
            _placementInputExporter = placementInputExporter;
            _sprinklerFamilySource = sprinklerFamilySource;
            _sprinklerPlacementService = sprinklerPlacementService;

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

            ToggleSelectAllLevelsCommand = new RelayCommand(_ => ToggleSelectAllLevels());
            ToggleSelectAllRoomsCommand = new RelayCommand(_ => ToggleSelectAllRooms());

            // Keep the smart toggle state (button label + derived bool) in sync when a
            // level or room checkbox is toggled manually (or by ApplyDefaultSelection/Reset).
            foreach (LevelItemViewModel level in Levels)
                level.PropertyChanged += OnLevelItemPropertyChanged;
            foreach (RoomItemViewModel room in AllRooms)
                room.PropertyChanged += OnRoomItemPropertyChanged;

            PlaceSprinklersCommand = new RelayCommand(
                _ => ExecutePlaceSprinklers(),
                _ => CanExecutePlaceSprinklers());

            ResetCommand = new RelayCommand(_ => Reset());

            // Evaluate placement eligibility for every room against the (initially unselected)
            // family/type BEFORE default selection, so ApplyDefaultSelection only selects rooms
            // the production pipeline can actually place.
            RefreshEligibility();

            // Apply default selection (eligible levels + eligible rooms selected; blocked
            // rooms and empty levels remain unselected) once the collections are built.
            ApplyDefaultSelection();

            // Publish the initial smart-toggle state so the single toggle buttons render
            // with the correct label on first show.
            OnPropertyChanged(nameof(AreAllSelectableLevelsSelected));
            OnPropertyChanged(nameof(LevelSelectionToggleLabel));
            OnPropertyChanged(nameof(AreAllSelectableRoomsSelected));
            OnPropertyChanged(nameof(RoomSelectionToggleLabel));
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

        /// <summary>
        /// When ON, rooms that are not eligible (blocked) are filtered out of the visible
        /// room collection. This is visibility-only: the underlying room selection and
        /// source collection are never modified by filtering.
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

        /// <summary>
        /// When ON, levels that contain no rooms are filtered out of the visible level
        /// collection. A level's eligibility is derived from the underlying room
        /// collection (HasRooms), never from the transient room filter.
        /// </summary>
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

        /// <summary>
        /// Most recent BruteForce calculation result (in-memory; no Revit elements created).
        /// Exposed for UI display/binding; the calculation itself runs in the Backend service.
        /// </summary>
        public BruteForceCalculationResult LastCalculationResult { get; private set; }

        /// <summary>
        /// Most recent actual Revit placement result (populated only when a placement service is wired up).
        /// Exposed for UI display/binding.
        /// </summary>
        public SprinklerPlacementResult LastPlacementResult { get; private set; }

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
                    RefreshEligibility();
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
                    RefreshEligibility();
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

        /// <summary>Rooms currently visible in the room view that are placement-eligible (not blocked).</summary>
        public int VisibleEligibleRoomCount
        {
            get
            {
                int count = 0;
                if (RoomsView != null)
                    foreach (object obj in RoomsView)
                        if (obj is RoomItemViewModel r && r.IsEligible) count++;
                return count;
            }
        }

        /// <summary>Eligible, visible rooms that are currently selected.</summary>
        public int SelectedVisibleEligibleRoomCount
        {
            get
            {
                int count = 0;
                if (RoomsView != null)
                    foreach (object obj in RoomsView)
                        if (obj is RoomItemViewModel r && r.IsEligible && r.IsSelected) count++;
                return count;
            }
        }

        /// <summary>Visible rooms that are placement-blocked (deterministic inability; excluded from selection).</summary>
        public int VisibleBlockedRoomCount
        {
            get
            {
                int count = 0;
                if (RoomsView != null)
                    foreach (object obj in RoomsView)
                        if (obj is RoomItemViewModel r && r.IsBlocked) count++;
                return count;
            }
        }

        /// <summary>Visible rooms for which eligibility is undetermined (configuration/infra error; not BLOCKED, not selectable).</summary>
        public int VisibleUndeterminedRoomCount
        {
            get
            {
                int count = 0;
                if (RoomsView != null)
                    foreach (object obj in RoomsView)
                        if (obj is RoomItemViewModel r && r.IsUndetermined) count++;
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
                int eligible = VisibleEligibleRoomCount;
                int blocked = VisibleBlockedRoomCount;
                int undetermined = VisibleUndeterminedRoomCount;
                string baseText = visible + (visible == 1 ? " room shown" : " rooms shown");
                var parts = new System.Collections.Generic.List<string>();
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
                var extra = new System.Collections.Generic.List<string>();
                if (blocked > 0) extra.Add(blocked + " blocked");
                if (undetermined > 0) extra.Add(undetermined + " undetermined");
                return extra.Count == 0 ? baseSummary : baseSummary + " (" + string.Join(", ", extra) + ")";
            }
        }

        public ICommand ToggleHideUnselectedLevelsCommand { get; }
        public ICommand ToggleHideUnselectedRoomsCommand { get; }

        public ICommand ToggleSelectAllLevelsCommand { get; }
        public ICommand ToggleSelectAllRoomsCommand { get; }

        /// <summary>
        /// True when every selectable level (a level that has at least one room) is selected.
        /// Used to derive the single Level toggle button label. Blocked/empty levels are not
        /// part of the selectable population, so they never keep the button stuck on "Select All".
        /// </summary>
        public bool AreAllSelectableLevelsSelected =>
            Levels.Any(l => l.HasRooms) && Levels.Where(l => l.HasRooms).All(l => l.IsSelected);

        /// <summary>Dynamic label for the single Level selection toggle.</summary>
        public string LevelSelectionToggleLabel =>
            AreAllSelectableLevelsSelected ? "Clear All" : "Select All";

        /// <summary>
        /// True when every selectable room (an ELIGIBLE room) is selected. Only ELIGIBLE rooms are part of the
        /// selectable population; BLOCKED and UNDETERMINED rooms are never selected, so they cannot
        /// keep the single toggle stuck on "Select All".
        /// </summary>
        public bool AreAllSelectableRoomsSelected =>
            AllRooms.Any(r => r.IsEligible) && AllRooms.Where(r => r.IsEligible).All(r => r.IsSelected);

        /// <summary>Dynamic label for the single Room selection toggle.</summary>
        public string RoomSelectionToggleLabel =>
            AreAllSelectableRoomsSelected ? "Clear All" : "Select All";

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
            if (SelectedVisibleEligibleRoomCount == 0) return false;
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
            PlacementStatusMessage = "Exporting placement input snapshot...";

            try
            {
                List<PlacementRoomInputItem> roomSelections = CollectSelectedRooms();

                if (roomSelections.Count == 0)
                {
                    PlacementStatusMessage = "No selected rooms with usable geometry.";
                    MessageBox.Show(
                        "No selected rooms have usable geometry.",
                        "Place Sprinklers",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                string projectName = Data?.Project?.Name ?? "FireProtectionModel";
                string familyName = SelectedSprinklerFamily.FamilyName;
                string typeName = SelectedSprinklerType.TypeName;

                if (_placementInputExporter == null)
                {
                    throw new InvalidOperationException("Placement input exporter service is not configured.");
                }

                // Delegate building & exporting to placement input exporter (debug JSON aid).
                PlacementInputExportResult result = _placementInputExporter.ExportInput(
                    projectName,
                    familyName,
                    typeName,
                    roomSelections);

                if (!result.Success)
                {
                    throw new InvalidOperationException(result.ErrorMessage ?? "Failed to export placement input snapshot.");
                }

                // Run the in-memory BruteForce calculation (does NOT create Revit elements).
                BruteForceCalculationResult calc = _placementInputExporter.CalculateBruteForce(
                    projectName,
                    familyName,
                    typeName,
                    roomSelections);

                LastCalculationResult = calc;

                PlacementStatusMessage =
                    $"Calculated {calc.TotalCalculatedSprinklers} sprinkler point(s) " +
                    $"across {calc.Rooms.Count} room(s). (Input JSON also exported.)";

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("BruteForce sprinkler calculation complete.");
                sb.AppendLine();
                sb.AppendLine(calc.SummaryText());
                sb.AppendLine();
                sb.AppendLine($"Placement input JSON exported to:\n{result.ExportFilePath}");
                if (calc.IsProvisional)
                {
                    sb.AppendLine();
                    sb.AppendLine("NOTE: Provisional spacing rules were used. No NFPA13-2022");
                    sb.AppendLine("compliance is implied. Human review is required.");
                }

                // Phase 2: actual Revit FamilyInstance placement (does nothing if no service is wired).
                bool didPlace = false;
                string placementPath = null;

                if (_sprinklerPlacementService != null)
                {
                    SprinklerPlacementResult placement = _sprinklerPlacementService.PlaceSprinklers(
                        familyName,
                        typeName,
                        calc);

                    LastPlacementResult = placement;
                    didPlace = true;

                    if (_placementInputExporter != null)
                    {
                        try
                        {
                            placementPath = _placementInputExporter.ExportPlacementResult(placement);
                        }
                        catch (Exception ex)
                        {
                            placement.Warnings.Add("Failed to export placement result JSON: " + ex.Message);
                        }
                    }

                    sb.AppendLine();
                    sb.AppendLine("---- Actual Revit Placement ----");
                    sb.AppendLine(placement.SummaryText());
                    if (!string.IsNullOrEmpty(placementPath))
                    {
                        sb.AppendLine();
                        sb.AppendLine($"Placement result JSON:\n{placementPath}");
                    }
                }

                MessageBox.Show(
                    sb.ToString(),
                    didPlace ? "Place Sprinklers — Result" : "Place Sprinklers — BruteForce Result",
                    MessageBoxButton.OK,
                    calc.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                PlacementStatusMessage = "Export failed: " + ex.Message;
                MessageBox.Show(
                    "Failed to export placement input JSON:\n\n" + ex.Message,
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
            if (SelectedSprinklerFamily == null)
                return "Please select a sprinkler family.";

            if (SelectedSprinklerType == null)
                return "Please select a sprinkler type.";

            if (!string.Equals(SelectedSprinklerType.FamilyName, SelectedSprinklerFamily.FamilyName, StringComparison.OrdinalIgnoreCase))
                return "The selected sprinkler type does not belong to the selected sprinkler family.";

            if (SelectedVisibleEligibleRoomCount == 0)
                return "Please select at least one eligible room.";

            bool selectedTypeExistsInCurrentFamily = SprinklerTypes.Any(t =>
                string.Equals(t.FamilyName, SelectedSprinklerType.FamilyName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.TypeName, SelectedSprinklerType.TypeName, StringComparison.OrdinalIgnoreCase));

            if (!selectedTypeExistsInCurrentFamily)
                return "The selected sprinkler type is no longer available for the selected family.";

            return null;
        }

        private List<PlacementRoomInputItem> CollectSelectedRooms()
        {
            List<PlacementRoomInputItem> list = new List<PlacementRoomInputItem>();

            foreach (LevelItemViewModel levelVm in Levels)
            {
                if (!levelVm.IsSelected) continue;

                LevelUiData levelData = levelVm.Level;

                foreach (RoomItemViewModel roomVm in levelVm.Rooms)
                {
                    if (!roomVm.IsSelected) continue;
                    if (!roomVm.IsEligible)
                    {
                        // Hard guard: only ELIGIBLE rooms reach the placement pipeline. BLOCKED and UNDETERMINED
                        // rooms are rejected (rejecting an undetermined room does NOT mean it was "blocked" —
                        // the distinction is preserved on the room's EligibilityState).
                        System.Diagnostics.Debug.WriteLine(
                            $"[ROOM-SELECTION-GUARD] RoomId={roomVm.Room?.RoomId} State={roomVm.EligibilityState} " +
                            $"IsEligible={roomVm.IsEligible} IsSelected={roomVm.IsSelected} -> REJECTED");
                        continue;
                    }

                    RoomUiData roomData = roomVm.Room;
                    if (roomData == null) continue;

                    System.Diagnostics.Debug.WriteLine(
                        $"[ROOM-SELECTION-GUARD] RoomId={roomData.RoomId} IsEligible={roomVm.IsEligible} " +
                        $"IsSelected={roomVm.IsSelected} -> ACCEPTED");

                    List<double[]> polyCopy = new List<double[]>();
                    if (roomData.Geometry != null && roomData.Geometry.Polygon != null)
                    {
                        foreach (double[] v in roomData.Geometry.Polygon)
                        {
                            if (v != null && v.Length >= 2)
                                polyCopy.Add(new double[] { v[0], v[1] });
                        }
                    }

                    // Forward the complete room payload so the placement input retains
                    // ceilings, obstacles, existing sprinklers, and source metadata.
                    JObject fullRoomJson = BuildFullRoomJson(roomData);

                    list.Add(new PlacementRoomInputItem
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

                        EffectiveHazardClass = roomVm.SelectedHazardClass,
                        FullRoomJson = fullRoomJson
                    });
                }
            }

            return list;
        }

        /// <summary>
        /// Reconstructs the full room JSON (including ceilings, obstacles, existing
        /// sprinklers, source, boundary, and hazard) from the reduced display model plus
        /// the extension data captured during deserialization of the ModelSnapshot JSON.
        /// </summary>
        private static JObject BuildFullRoomJson(RoomUiData roomData)
        {
            if (roomData == null) return null;

            JObject full = JObject.FromObject(roomData);

            // Remove the extension-data container if Newtonsoft emitted it as a nested object.
            if (full["ExtensionData"] != null) full.Remove("ExtensionData");

            // Merge the captured full-model properties (ceilings, obstacles, source, etc.).
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
            LevelItemViewModel level = obj as LevelItemViewModel;
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
            RoomItemViewModel room = obj as RoomItemViewModel;
            if (room == null) return false;
            // "Show eligible rooms only" hides everything that is NOT ELIGIBLE (BLOCKED and UNDETERMINED rooms
            // are all filtered out, so a defect/undetermined room is never masked as "just hidden").
            if (ShowEligibleRoomsOnly && !room.IsEligible) return false;
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

        /// <summary>
        /// One smart toggle: select every selectable level when not all are selected, else
        /// clear every selectable level. State is derived from the actual selectable
        /// collection (never a fragile independent bool). Blocked/empty levels excluded.
        /// </summary>
        private void ToggleSelectAllLevels()
        {
            bool select = !AreAllSelectableLevelsSelected;
            foreach (LevelItemViewModel level in Levels.Where(l => l.HasRooms))
                level.IsSelected = select;
        }

        /// <summary>
        /// One smart toggle for rooms. Selects every eligible (non-blocked) room when not all
        /// are selected, else clears them. Blocked rooms are never selected.
        /// </summary>
        private void ToggleSelectAllRooms()
        {
            bool select = !AreAllSelectableRoomsSelected;
            // Only ELIGIBLE rooms participate in the select-all toggle. BLOCKED / UNDETERMINED rooms
            // are never bulk-selected (and selecting them is meaningless — they cannot be placed).
            foreach (RoomItemViewModel room in AllRooms.Where(r => r.IsEligible))
                room.IsSelected = select;
        }

        private void OnLevelItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LevelItemViewModel.IsSelected))
            {
                // §7 / §8: a level selection drives its rooms. Selecting a level selects its ELIGIBLE rooms;
                // clearing a level deselects its ELIGIBLE rooms. Non-eligible (BLOCKED/UNDETERMINED) rooms are
                // never selected, regardless of level state, and other selected levels are unaffected.
                var levelVm = sender as LevelItemViewModel;
                if (levelVm != null)
                {
                    bool select = levelVm.IsSelected;
                    foreach (RoomItemViewModel room in levelVm.Rooms)
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
            if (e.PropertyName == nameof(RoomItemViewModel.IsSelected))
            {
                OnPropertyChanged(nameof(AreAllSelectableRoomsSelected));
                OnPropertyChanged(nameof(RoomSelectionToggleLabel));
            }
            else if (e.PropertyName == nameof(RoomItemViewModel.SelectedHazardClass))
            {
                // HazardClass participates in eligibility (it drives candidate generation and the
                // preflight cache key), so a per-room hazard edit must re-run the deterministic preflight.
                RefreshEligibility();
            }
        }

        /// <summary>
        /// Re-runs the authoritative pre-placement eligibility preflight for every room, given the
        /// currently selected sprinkler family/type, and pushes the result into each
        /// <see cref="RoomItemViewModel"/>. Non-destructive: it never creates a Revit instance — it
        /// reuses the exact strategy/host-resolution logic the placement service uses. When no
        /// family/type is selected yet, rooms are left "pending" (eligible, pending family selection)
        /// so they stay selectable until the user picks a family (the Place command is gated separately).
        /// Any room that becomes blocked is automatically deselected so a stale selection can never
        /// reach placement.
        /// </summary>
        private void RefreshEligibility()
        {
            if (_sprinklerPlacementService == null) return;

            string familyName = SelectedSprinklerFamily?.FamilyName;
            string typeName = SelectedSprinklerType?.TypeName;
            bool canEvaluate = !string.IsNullOrWhiteSpace(familyName) && !string.IsNullOrWhiteSpace(typeName);

            // Drop any cached probe results so this evaluation is authoritative, not stale.
            _sprinklerPlacementService.ClearEligibilityCache();

            // Compute the REAL candidate points for every visible room via the exact BruteForce calculation
            // the placement step consumes. Eligibility then probes actual placement of those candidates.
            Dictionary<string, RoomCalculationResult> calcByRoom = null;
            bool calcRan = canEvaluate && _placementInputExporter != null;
            if (calcRan)
            {
                try
                {
                    List<PlacementRoomInputItem> allRooms = CollectAllVisibleRooms();
                    BruteForceCalculationResult calc = _placementInputExporter.CalculateBruteForce(
                        Data?.Project?.Name ?? "FireProtectionModel", familyName, typeName, allRooms);
                    if (calc?.Rooms != null)
                        calcByRoom = calc.Rooms.Where(r => r != null).ToDictionary(r => r.RoomId);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[ROOM-ELIGIBILITY] candidate calculation failed: " + ex.Message);
                    calcByRoom = null;
                }
            }

            // If we intended to calculate but it threw, we have INSUFFICIENT EVIDENCE -> UNKNOWN for every
            // room. We must NOT fall through to probing with a null candidate list and report BLOCKED, because
            // that would hide a calculation/infrastructure failure behind "blocked".
            bool calcFailed = calcRan && calcByRoom == null;

            foreach (RoomItemViewModel roomVm in AllRooms)
            {
                PlacementEligibilityResult result;
                int candidateCount = 0;

                if (!canEvaluate)
                {
                    // Family/type not yet chosen: cannot prove feasibility. The Place command is gated on a
                    // family/type too, so rooms are marked UNDETERMINED, never falsely BLOCKED.
                    result = PlacementEligibilityResult.Pending();
                }
                else if (calcFailed)
                {
                    result = PlacementEligibilityResult.Undetermined(
                        "Candidate calculation could not be run for this room; eligibility is undetermined.",
                        PlacementEligibilityStatusCodes.CalculationFailed);
                }
                else
                {
                    IReadOnlyList<CalculatedSprinklerPoint> candidates = null;
                    if (calcByRoom != null && roomVm.Room != null &&
                        calcByRoom.TryGetValue(roomVm.Room.RoomId, out RoomCalculationResult rc))
                    {
                        candidates = rc.Points;
                    }
                    candidateCount = candidates?.Count ?? 0;
                    result = _sprinklerPlacementService.EvaluateRoomEligibility(
                        roomVm.Room, candidates, familyName, typeName);
                }

                bool wasEligible = roomVm.IsEligible;
                roomVm.SetEligibility(result);

                System.Diagnostics.Debug.WriteLine(
                    $"[ROOM-ELIGIBILITY] RoomId={roomVm.Room?.RoomId} RoomName={roomVm.Room?.Name} " +
                    $"Family={familyName} Type={typeName} CandidateCount={candidateCount} " +
                    $"State={roomVm.EligibilityState} Status={result.StatusCode} Reason={result.Reason}");

                // Normalize selection to the authoritative eligibility state (§4 / §10 / §11 / §19):
                //  - a room that is NOT eligible (BLOCKED / UNDETERMINED) can never be selected;
                //  - a room that JUST became ELIGIBLE (was not eligible before this evaluation) is selected
                //    by default; an already-eligible room keeps the user's manual selection choice.
                if (!roomVm.IsEligible)
                    roomVm.IsSelected = false;
                else if (!wasEligible)
                    roomVm.IsSelected = true;
            }

            OnPropertyChanged(nameof(AreAllSelectableRoomsSelected));
            OnPropertyChanged(nameof(RoomSelectionToggleLabel));
        }

        /// <summary>
        /// Builds <see cref="PlacementRoomInputItem"/> for every visible room (regardless of selection state)
        /// so the authoritative eligibility probe can evaluate the real candidate points for all rooms. Mirrors
        /// <see cref="CollectSelectedRooms"/> but does not filter on <c>IsSelected</c>/<c>IsBlocked</c>.
        /// </summary>
        private List<PlacementRoomInputItem> CollectAllVisibleRooms()
        {
            List<PlacementRoomInputItem> list = new List<PlacementRoomInputItem>();

            foreach (LevelItemViewModel levelVm in Levels)
            {
                LevelUiData levelData = levelVm.Level;
                if (levelData == null) continue;

                foreach (RoomItemViewModel roomVm in levelVm.Rooms)
                {
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

                    list.Add(new PlacementRoomInputItem
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
                        EffectiveHazardClass = roomVm.SelectedHazardClass,
                        FullRoomJson = BuildFullRoomJson(roomData)
                    });
                }
            }

            return list;
        }

        /// <summary>
        /// Selects all eligible levels and all eligible rooms by default. Blocked rooms and
        /// levels with no rooms are intentionally left unselected. Selection state lives on
        /// the source collections, so subsequent filtering cannot destroy it.
        /// </summary>
        private void ApplyDefaultSelection()
        {
            foreach (LevelItemViewModel level in Levels)
            {
                if (!level.HasRooms) continue;          // empty levels not selectable
                level.IsSelected = true;

                foreach (RoomItemViewModel room in level.Rooms)
                {
                    if (!room.IsEligible) continue;        // only ELIGIBLE rooms are selected by default
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
            RefreshEligibility();
            ApplyDefaultSelection();
            CommandManager.InvalidateRequerySuggested();
        }
    }
}