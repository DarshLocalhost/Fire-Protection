using System;
using System.Collections.Generic;
using FireProtection.UI.Models;
using FireProtection.UI.ViewModels.Common;

namespace FireProtection.UI.ViewModels.Sprinklers.BruteForce
{
    public class LevelItemViewModel : ObservableObject
    {
        private bool _isSelected;
        private int _selectedRoomCount;

        public LevelItemViewModel(LevelUiData level)
        {
            Level = level ?? throw new ArgumentNullException(nameof(level));

            Rooms = new List<RoomItemViewModel>();

            if (level.Rooms != null)
            {
                foreach (RoomUiData room in level.Rooms)
                {
                    RoomItemViewModel roomVm =
                        new RoomItemViewModel(room, this);

                    roomVm.SelectionChanged +=
                        (s, e) => RecalculateSelectedRoomCount();

                    Rooms.Add(roomVm);
                }
            }
        }

        public event EventHandler SelectionChanged;

        public LevelUiData Level { get; }

        public string Id => Level.LevelId;

        public string Name => Level.Name;

        public double ElevationFt => Level.ElevationFt;

        public List<RoomItemViewModel> Rooms { get; }

        public int TotalRoomCount => Rooms.Count;

        public bool HasRooms => TotalRoomCount > 0;

        public bool IsSelected
        {
            get => _isSelected;

            set
            {
                if (value && !HasRooms)
                    return;

                if (SetProperty(ref _isSelected, value))
                {
                    SelectionChanged?.Invoke(
                        this,
                        EventArgs.Empty);
                }
            }
        }

        public int SelectedRoomCount
        {
            get => _selectedRoomCount;
            private set => SetProperty(
                ref _selectedRoomCount,
                value);
        }

        private void RecalculateSelectedRoomCount()
        {
            int count = 0;

            foreach (RoomItemViewModel room in Rooms)
            {
                if (room.IsSelected)
                    count++;
            }

            SelectedRoomCount = count;
        }
    }
}