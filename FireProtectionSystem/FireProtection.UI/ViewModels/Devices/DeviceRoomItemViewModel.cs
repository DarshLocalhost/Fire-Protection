using System;
using System.ComponentModel;
using FireProtection.UI.Models;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Common;

namespace FireProtection.UI.ViewModels.Devices
{
    /// <summary>
    /// Generic room row for device placement. Shares the selection + three-state eligibility model of
    /// <c>RoomItemViewModel</c> but deliberately omits sprinkler-specific concepts (hazard class). Eligibility
    /// for devices is driven by the device backend (deferred); until then rooms are shown as selectable for
    /// layout planning via <see cref="SetEligibility"/>.
    /// </summary>
    public class DeviceRoomItemViewModel : ObservableObject
    {
        private bool _isSelected;
        private bool _isEligible;
        private string _eligibilityState;
        private string _eligibilityReason;

        public DeviceRoomItemViewModel(
            RoomUiData room,
            DeviceLevelItemViewModel parentLevel)
        {
            Room =
                room ?? throw new ArgumentNullException(nameof(room));

            ParentLevel =
                parentLevel ?? throw new ArgumentNullException(nameof(parentLevel));

            // Eligibility is NOT decided here. The parent ViewModel runs the authoritative
            // eligibility evaluation (device backend) and calls SetEligibility. Until then the room
            // is treated as not-yet-evaluated so the UI does not present a false "eligible".
            _isEligible = false;
            _eligibilityState = EligibilityStates.Undetermined;
            _eligibilityReason = "Placement eligibility not evaluated.";
        }

        public event EventHandler SelectionChanged;

        public RoomUiData Room { get; }

        public DeviceLevelItemViewModel ParentLevel { get; }

        public string RoomId => Room.RoomId;

        public string Name => Room.Name;

        public string Number => Room.Number;

        public string LevelName => Room.LevelName;

        public double? CeilingHeightFt => Room.Geometry?.CeilingHeightFt;

        public double AreaSqFt => Room.AreaSqFt;

        public bool IsEligible => _isEligible;

        public bool IsBlocked =>
            string.Equals(_eligibilityState, EligibilityStates.Blocked, StringComparison.OrdinalIgnoreCase);

        public bool IsUndetermined =>
            string.Equals(_eligibilityState, EligibilityStates.Undetermined, StringComparison.OrdinalIgnoreCase);

        public string EligibilityState => _eligibilityState;

        public string EligibilityReason => _eligibilityReason;

        public bool IsSelected
        {
            get => _isSelected;

            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// Applies the authoritative eligibility result to this room. Until the device backend is
        /// implemented, the base ViewModel marks every room Eligible for planning; this method still
        /// supports BLOCKED / UNDETERMINED states for when real evaluation lands.
        /// </summary>
        public void SetEligibility(PlacementEligibilityResult result)
        {
            if (result == null)
            {
                _isEligible = false;
                _eligibilityState = EligibilityStates.Undetermined;
                _eligibilityReason = "Placement eligibility not evaluated.";
            }
            else
            {
                _isEligible = result.IsEligible;
                _eligibilityState = result.EligibilityState;
                _eligibilityReason = result.Reason;
            }

            OnPropertyChanged(nameof(IsEligible));
            OnPropertyChanged(nameof(IsBlocked));
            OnPropertyChanged(nameof(IsUndetermined));
            OnPropertyChanged(nameof(EligibilityState));
            OnPropertyChanged(nameof(EligibilityReason));
        }
    }
}
