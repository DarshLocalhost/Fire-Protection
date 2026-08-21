using System;
using System.Collections.Generic;
using System.Linq;
using FireProtection.UI.Models;
using FireProtection.UI.ViewModels.Common;

namespace FireProtection.UI.ViewModels.Sprinklers.BruteForce
{
    public class RoomItemViewModel : ObservableObject
    {
        private bool _isSelected;
        private string _selectedHazardClass;

        public RoomItemViewModel(
            RoomUiData room,
            LevelItemViewModel parentLevel)
        {
            Room =
                room ?? throw new ArgumentNullException(nameof(room));

            ParentLevel =
                parentLevel ?? throw new ArgumentNullException(nameof(parentLevel));

            HazardClassOptionsList =
                HazardClassOptions.All;

            _selectedHazardClass =
                ResolveInitialHazardClass(
                    room.Classification?.HazardClass);
        }

        public event EventHandler SelectionChanged;

        public RoomUiData Room { get; }

        public LevelItemViewModel ParentLevel { get; }

        public string RoomId =>
            Room.RoomId;

        public string Name =>
            Room.Name;

        public string Number =>
            Room.Number;

        public string LevelName =>
            Room.LevelName;

        public double? CeilingHeightFt =>
            Room.Geometry?.CeilingHeightFt;

        public double AreaSqFt =>
            Room.AreaSqFt;

        public string HazardClassSuggested =>
            Room.Classification?.SuggestedByClassifier
            ?? HazardClassOptions.Light;

        public string SelectedHazardClass
        {
            get => _selectedHazardClass;

            set
            {
                if (SetProperty(
                    ref _selectedHazardClass,
                    value))
                {
                    OnPropertyChanged(
                        nameof(IsHazardClassOverridden));

                    OnPropertyChanged(
                        nameof(IsReadyForPlacement));
                }
            }
        }

        public IReadOnlyList<string> HazardClassOptionsList
        {
            get;
        }

        public bool IsHazardClassOverridden =>
            !string.Equals(
                SelectedHazardClass,
                HazardClassSuggested,
                StringComparison.OrdinalIgnoreCase);

        public bool IsReadyForPlacement =>
            true;

        public bool RequiresHumanReview =>
            Room.RequiresHumanReview;

        public bool IsSelected
        {
            get => _isSelected;

            set
            {
                if (SetProperty(
                    ref _isSelected,
                    value))
                {
                    SelectionChanged?.Invoke(
                        this,
                        EventArgs.Empty);
                }
            }
        }

        public void ResetHazardClassToDefault()
        {
            SelectedHazardClass =
                ResolveInitialHazardClass(
                    HazardClassSuggested);
        }

        private string ResolveInitialHazardClass(
            string suggested)
        {
            if (string.IsNullOrWhiteSpace(suggested))
                return HazardClassOptions.Light;

            string match =
                HazardClassOptionsList.FirstOrDefault(
                    option =>
                        string.Equals(
                            option,
                            suggested,
                            StringComparison.OrdinalIgnoreCase));

            return match ??
                   HazardClassOptions.Light;
        }
    }
}