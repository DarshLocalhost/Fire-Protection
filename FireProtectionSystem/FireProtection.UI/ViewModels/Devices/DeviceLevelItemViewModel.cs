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
        private string _detectorType;
        private string _mount;
        private string _ceilingSlope;
        private string _applianceType;
        private string _candelaDba;
        private string _deviceFamily;
        private string _deviceType;

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

        public string DetectorType
        {
            get => _detectorType;
            set { if (SetProperty(ref _detectorType, value)) { PropagateToRooms("DetectorType"); } }
        }

        public string Mount
        {
            get => _mount;
            set { if (SetProperty(ref _mount, value)) { PropagateToRooms("Mount"); } }
        }

        public string CeilingSlope
        {
            get => _ceilingSlope;
            set { if (SetProperty(ref _ceilingSlope, value)) { PropagateToRooms("CeilingSlope"); } }
        }

        public string ApplianceType
        {
            get => _applianceType;
            set { if (SetProperty(ref _applianceType, value)) { PropagateToRooms("ApplianceType"); } }
        }

        public string CandelaDba
        {
            get => _candelaDba;
            set { if (SetProperty(ref _candelaDba, value)) { PropagateToRooms("CandelaDba"); } }
        }

        /// <summary>
        /// Level default for the device family. Propagates into every room on this level that is
        /// still on the previous default; rows the user overrode keep their own selection.
        /// </summary>
        public string DeviceFamily
        {
            get => _deviceFamily;
            set { if (SetProperty(ref _deviceFamily, value)) { PropagateFamilyTypeToRooms(); } }
        }

        /// <summary>Level default for the device type. See <see cref="DeviceFamily"/>.</summary>
        public string DeviceType
        {
            get => _deviceType;
            set { if (SetProperty(ref _deviceType, value)) { PropagateFamilyTypeToRooms(); } }
        }

        public string GetDefault(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            switch (key)
            {
                case "DetectorType": return _detectorType;
                case "Mount": return _mount;
                case "CeilingSlope": return _ceilingSlope;
                case "ApplianceType": return _applianceType;
                case "CandelaDba": return _candelaDba;
                case "DeviceFamily": return _deviceFamily;
                case "DeviceType": return _deviceType;
                default: return null;
            }
        }

        public void SetDefault(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            switch (key)
            {
                case "DetectorType": DetectorType = value; break;
                case "Mount": Mount = value; break;
                case "CeilingSlope": CeilingSlope = value; break;
                case "ApplianceType": ApplianceType = value; break;
                case "CandelaDba": CandelaDba = value; break;
                case "DeviceFamily": DeviceFamily = value; break;
                case "DeviceType": DeviceType = value; break;
            }
        }

        private void PropagateFamilyTypeToRooms()
        {
            if (Rooms == null) return;
            foreach (DeviceRoomItemViewModel room in Rooms)
            {
                if (room == null) continue;
                room.SetDeviceDefaults(_deviceFamily, _deviceType);
            }
        }

        private void PropagateToRooms(string key)
        {
            if (Rooms == null) return;
            string newDefault = GetDefault(key);
            foreach (DeviceRoomItemViewModel room in Rooms)
            {
                if (room == null) continue;
                // Always push the level default; the row keeps showing its own override if it has one.
                room.SetLevelDefault(key, newDefault);
            }
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
