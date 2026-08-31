using System;
using System.Collections.Generic;
using FireProtection.UI.Models;
using FireProtection.UI.ViewModels.Common;

namespace FireProtection.UI.ViewModels.Devices
{
    /// <summary>
    /// Generic level container for device placement. Mirrors <c>LevelItemViewModel</c> (sprinkler)
    /// but is parameterized on the device-agnostic <see cref="DeviceRoomItemViewModel"/> so it can be
    /// shared by sprinklers, smoke detectors, and notification appliances. Sprinkler files are untouched.
    /// </summary>
    public class DeviceLevelItemViewModel : ObservableObject
    {
        private bool _isSelected;
        private int _selectedRoomCount;

        public DeviceLevelItemViewModel(LevelUiData level)
        {
            Level = level ?? throw new ArgumentNullException(nameof(level));

            Rooms = new List<DeviceRoomItemViewModel>();

            if (level.Rooms != null)
            {
                foreach (RoomUiData room in level.Rooms)
                {
                    DeviceRoomItemViewModel roomVm = new DeviceRoomItemViewModel(room, this);

                    roomVm.SelectionChanged += (s, e) => RecalculateSelectedRoomCount();

                    Rooms.Add(roomVm);
                }
            }
        }

        public event EventHandler SelectionChanged;

        public LevelUiData Level { get; }

        public string Id => Level.LevelId;

        public string Name => Level.Name;

        public double ElevationFt => Level.ElevationFt;

        public List<DeviceRoomItemViewModel> Rooms { get; }

        public int TotalRoomCount => Rooms.Count;

        public bool HasRooms => TotalRoomCount > 0;

        public bool IsSelected
        {
            get => _isSelected;

            set
            {
                // Empty levels are never selectable.
                if (value && !HasRooms)
                    return;

                if (SetProperty(ref _isSelected, value))
                {
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public int SelectedRoomCount
        {
            get => _selectedRoomCount;
            private set => SetProperty(ref _selectedRoomCount, value);
        }

        private void RecalculateSelectedRoomCount()
        {
            int count = 0;

            foreach (DeviceRoomItemViewModel room in Rooms)
            {
                if (room.IsSelected)
                    count++;
            }

            SelectedRoomCount = count;
        }
    }
}
