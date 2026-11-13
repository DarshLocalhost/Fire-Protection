using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using FireProtection.UI.Models;
using FireProtection.UI.ViewModels.Common;
using FireProtection.UI.ViewModels.Sprinklers.BruteForce;

namespace FireProtection.UI.ViewModels.Sprinklers.Collision
{
    public class SprinklerReviewItemViewModel : ObservableObject
    {
        public string RoomName { get; set; }
        public string RoomNumber { get; set; }
        public string LevelName { get; set; }
        public double AreaSqFt { get; set; }
        public string HazardClass { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public double? SpacingFt { get; set; }
        public double? ClearanceFt { get; set; }
        public string CeilingHeight { get; set; }
    }

    public class SprinklerCollisionViewModel : ObservableObject
    {
        private readonly SprinklerBruteForceViewModel _preliminaryVm;

        public SprinklerCollisionViewModel()
            : this(null, null)
        {
        }

        public SprinklerCollisionViewModel(FireProtectionUiData data)
            : this(data, null)
        {
        }

        public SprinklerCollisionViewModel(FireProtectionUiData data, SprinklerBruteForceViewModel preliminaryVm)
        {
            Data = data;
            _preliminaryVm = preliminaryVm;
            ReviewRooms = new ObservableCollection<SprinklerReviewItemViewModel>();

            GoBackCommand = new RelayCommand(_ => ExecuteGoBack());
            CancelCommand = new RelayCommand(_ => ExecuteCancel());

            if (_preliminaryVm != null)
            {
                _preliminaryVm.PropertyChanged += OnPreliminaryPropertyChanged;
                RefreshReviewList();
            }
        }

        public FireProtectionUiData Data { get; }

        public SprinklerBruteForceViewModel PreliminaryVm => _preliminaryVm;

        public ObservableCollection<SprinklerReviewItemViewModel> ReviewRooms { get; }

        public Action RequestNavigateToPreliminary { get; set; }

        public ICommand GoBackCommand { get; }
        public ICommand CancelCommand { get; }

        public ICommand PlaceSprinklersCommand => _preliminaryVm?.PlaceSprinklersCommand;
        public ICommand CancelPlacementCommand => _preliminaryVm?.CancelPlacementCommand;

        public bool IgnoreObstacles
        {
            get => _preliminaryVm?.IgnoreObstacles ?? false;
            set
            {
                if (_preliminaryVm != null && _preliminaryVm.IgnoreObstacles != value)
                {
                    _preliminaryVm.IgnoreObstacles = value;
                    OnPropertyChanged(nameof(IgnoreObstacles));
                }
            }
        }

        public bool IsPlacementRunning => _preliminaryVm?.IsPlacementRunning ?? false;
        public bool CanCancelPlacement => _preliminaryVm?.CanCancelPlacement ?? false;
        public string PlacementProgressText => _preliminaryVm?.PlacementProgressText;
        public string PlacementStatusMessage => _preliminaryVm?.PlacementStatusMessage;

        public int SelectedRoomCount => ReviewRooms.Count;

        public string SummaryText
        {
            get
            {
                int count = ReviewRooms.Count;
                string family = _preliminaryVm?.SelectedSprinklerFamily?.FamilyName ?? "—";
                string type = _preliminaryVm?.SelectedSprinklerType?.TypeName ?? "—";
                return $"{count} room{(count == 1 ? "" : "s")} selected for sprinkler placement  •  Default: {family} / {type}";
            }
        }

        public void RefreshReviewList()
        {
            try
            {
                ReviewRooms.Clear();
                if (_preliminaryVm?.AllRooms == null) return;

                string defaultFamily = _preliminaryVm.SelectedSprinklerFamily?.FamilyName ?? "—";
                string defaultType = _preliminaryVm.SelectedSprinklerType?.TypeName ?? "—";

                foreach (RoomItemViewModel room in _preliminaryVm.AllRooms.Where(r => r.IsSelected && r.IsEligible))
                {
                    ReviewRooms.Add(new SprinklerReviewItemViewModel
                    {
                        RoomName = room.Name,
                        RoomNumber = room.Number,
                        LevelName = room.LevelName,
                        AreaSqFt = room.AreaSqFt,
                        HazardClass = room.SelectedHazardClass,
                        FamilyName = !string.IsNullOrEmpty(room.SelectedFamily) ? room.SelectedFamily : defaultFamily,
                        TypeName = !string.IsNullOrEmpty(room.SelectedType) ? room.SelectedType : defaultType,
                        SpacingFt = room.MaxSpacingFtOverride ?? room.DefaultMaxSpacingFt,
                        ClearanceFt = room.BoundaryClearanceFtOverride ?? room.DefaultBoundaryClearanceFt,
                        CeilingHeight = room.CeilingHeightFt.HasValue ? $"{room.CeilingHeightFt.Value:F2} ft" : "—"
                    });
                }

                OnPropertyChanged(nameof(SelectedRoomCount));
                OnPropertyChanged(nameof(SummaryText));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SprinklerCollisionViewModel] Error refreshing review list: {ex}");
            }
        }

        private void OnPreliminaryPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SprinklerBruteForceViewModel.IsPlacementRunning)
                || e.PropertyName == nameof(SprinklerBruteForceViewModel.CanCancelPlacement)
                || e.PropertyName == nameof(SprinklerBruteForceViewModel.PlacementProgressText)
                || e.PropertyName == nameof(SprinklerBruteForceViewModel.PlacementStatusMessage))
            {
                OnPropertyChanged(e.PropertyName);
            }
        }

        private void ExecuteGoBack()
        {
            RequestNavigateToPreliminary?.Invoke();
        }

        private void ExecuteCancel()
        {
            RequestNavigateToPreliminary?.Invoke();
        }
    }
}
