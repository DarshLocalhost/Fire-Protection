using System;
using System.Collections.Generic;
using System.Linq;
using FireProtection.UI.Models;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Common;

namespace FireProtection.UI.ViewModels.Sprinklers.BruteForce
{
    public class RoomItemViewModel : ObservableObject
    {
        private bool _isSelected;
        private string _selectedHazardClass;
        private bool _isEligible;
        private string _eligibilityState;
        private string _eligibilityReason;
        private string _familyPlacementType;
        private string _hostingStrategy;
        private string _ceilingSource;
        private string _linkInstanceName;
        private string _hostCeilingElementId;
        private string _hostLevelId;
        private string _hostLevelName;

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

            // Eligibility is NOT decided here. The parent ViewModel runs the authoritative
            // Backend preflight (EvaluateRoomEligibility) once rooms and the sprinkler
            // family/type are available, then calls SetEligibility. Until then the room is
            // treated as not-yet-evaluated so the UI does not present a false "eligible".
            _isEligible = false;
            _eligibilityState = PlacementEligibilityStates.Unknown;
            _eligibilityReason = "Placement eligibility not evaluated.";
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

        /// <summary>
        /// True when the room has the authoritative data required for sprinkler
        /// placement (a valid boundary polygon and a known ceiling height). Drives the
        /// room-eligibility UI; blocked rooms are shown disabled and excluded from
        /// selection / placement.
        /// </summary>
        /// <summary>
        /// True when the room can be placed by the production pipeline for the currently selected
        /// sprinkler family/type. Driven by the Backend preflight (<see cref="PlacementEligibilityResult"/>),
        /// not by a UI-local heuristic. Updated via <see cref="SetEligibility"/>.
        /// </summary>
        public bool IsEligible => _isEligible;

        /// <summary>Convenience inverse of <see cref="IsEligible"/>.</summary>
        public bool IsBlocked =>
            string.Equals(
                _eligibilityState,
                PlacementEligibilityStates.Blocked,
                StringComparison.OrdinalIgnoreCase);

        /// <summary>True when placement was attempted but failed unexpectedly (must NOT be conflated with BLOCKED).</summary>
        public bool IsPlacementError =>
            string.Equals(
                _eligibilityState,
                PlacementEligibilityStates.PlacementError,
                StringComparison.OrdinalIgnoreCase);

        /// <summary>True when there was insufficient evidence to classify the room (must NOT be conflated with BLOCKED).</summary>
        public bool IsUnknown =>
            string.Equals(
                _eligibilityState,
                PlacementEligibilityStates.Unknown,
                StringComparison.OrdinalIgnoreCase);

        /// <summary>Raw four-state classification (ELIGIBLE / BLOCKED / PLACEMENT_ERROR / UNKNOWN).</summary>
        public string EligibilityState => _eligibilityState;

        /// <summary>Human-readable reason the room is blocked (empty when eligible). Shown in the UI tooltip.</summary>
        public string EligibilityReason => _eligibilityReason;

        /// <summary>Proven Revit FamilyPlacementType of the selected family (FaceBased / WorkPlaneBased / OneLevelBased).</summary>
        public string FamilyPlacementType => _familyPlacementType;

        /// <summary>Hosting strategy the placement would use (FaceBasedHost / WorkPlaneCeilingFace / WorkPlaneSketchPlane / LevelBased).</summary>
        public string HostingStrategy => _hostingStrategy;

        /// <summary>Where a ceiling host was found: "host" / "link:&lt;name&gt;" / "none".</summary>
        public string CeilingSource => _ceilingSource;

        /// <summary>Linked model name supplying the ceiling host, when applicable.</summary>
        public string LinkInstanceName => _linkInstanceName;

        /// <summary>Discovered ceiling host ElementId (string), when applicable.</summary>
        public string HostCeilingElementId => _hostCeilingElementId;

        /// <summary>Resolved host Level id (string), when available.</summary>
        public string HostLevelId => _hostLevelId;

        /// <summary>Resolved host Level name, when available.</summary>
        public string HostLevelName => _hostLevelName;

        public bool RequiresHumanReview =>
            Room.RequiresHumanReview;

        /// <summary>
        /// Applies the authoritative preflight result to this room. Called by the parent ViewModel after the
        /// Backend eligibility service evaluates the room for the currently selected family/type. Raises
        /// PropertyChanged for every derived property so the UI (checkbox enabled state, tooltip, toggle
        /// button label) updates immediately.
        /// </summary>
        public void SetEligibility(PlacementEligibilityResult result)
        {
            if (result == null)
            {
                _isEligible = false;
                _eligibilityState = PlacementEligibilityStates.Unknown;
                _eligibilityReason = "Placement eligibility not evaluated.";
                _familyPlacementType = null;
                _hostingStrategy = null;
                _ceilingSource = null;
                _linkInstanceName = null;
                _hostCeilingElementId = null;
                _hostLevelId = null;
                _hostLevelName = null;
            }
            else
            {
                _isEligible = result.IsEligible;
                _eligibilityState = result.EligibilityState;
                _eligibilityReason = result.Reason;
                _familyPlacementType = result.FamilyPlacementType;
                _hostingStrategy = result.HostingStrategy;
                _ceilingSource = result.CeilingSource;
                _linkInstanceName = result.LinkInstanceName;
                _hostCeilingElementId = result.HostCeilingElementId;
                _hostLevelId = result.HostLevelId;
                _hostLevelName = result.HostLevelName;
            }

            OnPropertyChanged(nameof(IsEligible));
            OnPropertyChanged(nameof(IsBlocked));
            OnPropertyChanged(nameof(IsPlacementError));
            OnPropertyChanged(nameof(IsUnknown));
            OnPropertyChanged(nameof(EligibilityState));
            OnPropertyChanged(nameof(EligibilityReason));
            OnPropertyChanged(nameof(FamilyPlacementType));
            OnPropertyChanged(nameof(HostingStrategy));
            OnPropertyChanged(nameof(CeilingSource));
            OnPropertyChanged(nameof(LinkInstanceName));
            OnPropertyChanged(nameof(HostCeilingElementId));
            OnPropertyChanged(nameof(HostLevelId));
            OnPropertyChanged(nameof(HostLevelName));
        }

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