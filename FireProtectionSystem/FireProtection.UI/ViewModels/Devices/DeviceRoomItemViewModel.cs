using System;
using System.Collections.Generic;
using FireProtection.UI.Models;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Common;

namespace FireProtection.UI.ViewModels.Devices
{
    /// <summary>
    /// Generic room row for device placement. Shares the selection model of <c>RoomItemViewModel</c> and now
    /// its eligibility column too, so the smoke / notification grids show the same ELIGIBLE / BLOCKED /
    /// UNDETERMINED treatment the sprinkler grid does. Still omits sprinkler-specific concepts (hazard class).
    /// Deliberate difference: a device row DEFAULTS to eligible so the no-backend / designer path stays fully
    /// selectable; only the backend preflight (via <see cref="SetEligibility"/>) can move a row off ELIGIBLE.
    /// </summary>
    public class DeviceRoomItemViewModel : ObservableObject
    {
        private readonly Dictionary<string, string> _overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _levelDefaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private bool _isSelected;

        // Eligibility (preflight) state - mirrors RoomItemViewModel on the sprinkler side so both grids can
        // show the same treatment. Deliberate difference: a device row DEFAULTS to eligible (see the
        // constructor) so the no-backend / designer path stays fully selectable; the backend preflight only
        // ever RESTRICTS a room once it has actually run.
        private bool _isEligible;
        private string _eligibilityState;
        private string _eligibilityReason;
        private string _eligibilityStatusCode;
        private string _familyPlacementType;
        private string _hostingStrategy;
        private string _ceilingSource;
        private string _linkInstanceName;
        private string _hostCeilingElementId;
        private string _hostLevelId;
        private string _hostLevelName;

        public DeviceRoomItemViewModel(
            RoomUiData room,
            DeviceLevelItemViewModel parentLevel)
        {
            Room =
                room ?? throw new ArgumentNullException(nameof(room));

            ParentLevel =
                parentLevel ?? throw new ArgumentNullException(nameof(parentLevel));

            // Deliberate difference from the sprinkler row: a device room starts ELIGIBLE, not
            // "not-yet-evaluated". The device tabs support a no-backend / designer path (IsBackendPending when
            // the executor is null); with no backend the preflight never runs, and every room must stay
            // selectable exactly as it did before this column existed. Once the real preflight runs it calls
            // SetEligibility, which is the only thing that can move a row to BLOCKED / UNDETERMINED.
            _isEligible = true;
            _eligibilityState = EligibilityStates.Eligible;
            _eligibilityReason = null;
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

        // ----- Eligibility (preflight) -------------------------------------------------------------------
        // Copied from RoomItemViewModel (sprinkler) so DevicePlacementView can reuse the same row treatment.

        /// <summary>True when the room can be placed by the production pipeline for the currently selected
        /// device family/type. Driven by the Backend preflight (<see cref="PlacementEligibilityResult"/>) via
        /// <see cref="SetEligibility"/>; defaults to true so the no-backend path stays fully selectable.</summary>
        public bool IsEligible => _isEligible;

        /// <summary>Deterministic inability to place (explicit reason + status code).</summary>
        public bool IsBlocked =>
            string.Equals(_eligibilityState, EligibilityStates.Blocked, StringComparison.OrdinalIgnoreCase);

        /// <summary>Eligibility could not be determined (config/infra error). NOT the same as BLOCKED -
        /// a normal missing ceiling/host is BLOCKED, not UNDETERMINED.</summary>
        public bool IsUndetermined =>
            string.Equals(_eligibilityState, EligibilityStates.Undetermined, StringComparison.OrdinalIgnoreCase);

        /// <summary>Raw three-state classification (ELIGIBLE / BLOCKED / UNDETERMINED).</summary>
        public string EligibilityState => _eligibilityState;

        /// <summary>Human-readable reason the room is blocked (empty when eligible). Shown in the tooltip.</summary>
        public string EligibilityReason => _eligibilityReason;

        /// <summary>Short, status-code-derived reason for the narrow room row; the full reason stays in the tooltip.</summary>
        public string EligibilityShortReason =>
            EligibilityShortText.For(_eligibilityStatusCode, _eligibilityReason);

        /// <summary>Proven Revit FamilyPlacementType of the selected family (FaceBased / WorkPlaneBased / OneLevelBased).</summary>
        public string FamilyPlacementType => _familyPlacementType;

        /// <summary>Hosting strategy the placement would use (FaceBasedHost / WorkPlaneCeilingFace / LevelBased).</summary>
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

        /// <summary>
        /// Applies the authoritative preflight result to this room. Called by the base ViewModel after the
        /// device eligibility service evaluates the room for the currently selected family/type. A null result
        /// resets the row to ELIGIBLE (the no-backend / not-evaluated default), never to a false BLOCKED.
        /// </summary>
        public void SetEligibility(PlacementEligibilityResult result)
        {
            if (result == null)
            {
                // No result => treat as not-evaluated: stay selectable (matches the pre-preflight behaviour).
                _isEligible = true;
                _eligibilityState = EligibilityStates.Eligible;
                _eligibilityReason = null;
                _eligibilityStatusCode = null;
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
                _eligibilityStatusCode = result.StatusCode;
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
            OnPropertyChanged(nameof(IsUndetermined));
            OnPropertyChanged(nameof(EligibilityState));
            OnPropertyChanged(nameof(EligibilityReason));
            OnPropertyChanged(nameof(EligibilityShortReason));
            OnPropertyChanged(nameof(FamilyPlacementType));
            OnPropertyChanged(nameof(HostingStrategy));
            OnPropertyChanged(nameof(CeilingSource));
            OnPropertyChanged(nameof(LinkInstanceName));
            OnPropertyChanged(nameof(HostCeilingElementId));
            OnPropertyChanged(nameof(HostLevelId));
            OnPropertyChanged(nameof(HostLevelName));
        }

        // Per-row overrides (Decision 019). If a key is not overridden, the level default applies.
        // Two separate bags on purpose: _levelDefaults is what the level pushed down, _overrides is
        // what the user typed on this row. Keeping them apart means a later level-default change still
        // reaches a row the user never touched, and "is this row overridden?" stays answerable.
        public string GetOverride(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return _overrides.TryGetValue(key, out string v) ? v : null;
        }

        /// <summary>The value the parent level pushed onto this row (no user override involved).</summary>
        public string GetLevelDefault(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return _levelDefaults.TryGetValue(key, out string v) ? v : null;
        }

        /// <summary>Row override if present, otherwise the level default. This is the value placement uses.</summary>
        public string GetEffective(string key)
        {
            string over = GetOverride(key);
            return !string.IsNullOrEmpty(over) ? over : GetLevelDefault(key);
        }

        /// <summary>True when the user has set this key on this row, diverging from the level default.</summary>
        public bool IsOverridden(string key)
        {
            string over = GetOverride(key);
            if (string.IsNullOrEmpty(over)) return false;
            return !string.Equals(over, GetLevelDefault(key) ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Called by the parent level when its default for <paramref name="key"/> changes.</summary>
        public void SetLevelDefault(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            string current = GetLevelDefault(key);
            if (string.Equals(current ?? string.Empty, value ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                return;

            if (string.IsNullOrEmpty(value)) _levelDefaults.Remove(key);
            else _levelDefaults[key] = value;

            // Only a row without its own override changes what it shows.
            if (string.IsNullOrEmpty(GetOverride(key)))
                OnPropertyChanged("Override_" + key);
        }

        public void SetOverride(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            string current = GetOverride(key);
            if (string.Equals(current ?? string.Empty, value ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                return;
            if (string.IsNullOrEmpty(value))
            {
                _overrides.Remove(key);
            }
            else
            {
                _overrides[key] = value;
            }
            OnPropertyChanged("Override_" + key);
        }

        public void ClearAllOverrides()
        {
            if (_overrides.Count == 0) return;
            List<string> keys = new List<string>(_overrides.Keys);
            _overrides.Clear();
            foreach (string k in keys) OnPropertyChanged("Override_" + k);
        }

        // The bound value is the effective one (row override, else level default), so the grid shows what
        // placement would actually use; writing sets the row override.
        public string DetectorTypeOverride { get { return GetEffective("DetectorType"); } set { SetOverride("DetectorType", value); } }
        public string MountOverride { get { return GetEffective("Mount"); } set { SetOverride("Mount", value); } }
        public string CeilingSlopeOverride { get { return GetEffective("CeilingSlope"); } set { SetOverride("CeilingSlope", value); } }
        public string ApplianceTypeOverride { get { return GetEffective("ApplianceType"); } set { SetOverride("ApplianceType", value); } }
        public string CandelaDbaOverride { get { return GetEffective("CandelaDba"); } set { SetOverride("CandelaDba", value); } }

        // -----------------------------------------------------------------------------------
        // Per-row device family / type (Decision 019, row scope).
        // The tab's top-level Family/Type selection is the default; a row may override it.
        // Mirrors RoomItemViewModel on the sprinkler side so both grids behave identically.
        // -----------------------------------------------------------------------------------

        public IReadOnlyList<string> AvailableFamilies
        {
            get { return _availableFamilies ?? (IReadOnlyList<string>)new List<string>(); }
            set { _availableFamilies = value; OnPropertyChanged(); }
        }

        public IReadOnlyList<string> AvailableTypes
        {
            get { return _availableTypes ?? (IReadOnlyList<string>)new List<string>(); }
            set { _availableTypes = value; OnPropertyChanged(); }
        }

        public string SelectedFamily
        {
            get { return _selectedFamily; }
            set
            {
                if (SetProperty(ref _selectedFamily, value))
                {
                    // forceFirstType: a Family change always re-points Type at the new family's first type.
                    RefreshAvailableTypesForSelectedFamily(true);
                    OnPropertyChanged(nameof(IsFamilyOverridden));
                    OnPropertyChanged(nameof(SelectedFamilyTypeDisplay));
                }
            }
        }

        public string SelectedType
        {
            get { return _selectedType; }
            set
            {
                if (SetProperty(ref _selectedType, value))
                {
                    OnPropertyChanged(nameof(IsTypeOverridden));
                    OnPropertyChanged(nameof(SelectedFamilyTypeDisplay));
                }
            }
        }

        public string DefaultFamily { get { return _defaultFamily; } }

        public string DefaultType { get { return _defaultType; } }

        public bool IsFamilyOverridden
        {
            get
            {
                if (string.IsNullOrEmpty(_defaultFamily) && string.IsNullOrEmpty(_selectedFamily)) return false;
                return !string.Equals(_selectedFamily ?? string.Empty, _defaultFamily ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool IsTypeOverridden
        {
            get
            {
                if (string.IsNullOrEmpty(_defaultType) && string.IsNullOrEmpty(_selectedType)) return false;
                return !string.Equals(_selectedType ?? string.Empty, _defaultType ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
        }

        public string SelectedFamilyTypeDisplay
        {
            get
            {
                if (string.IsNullOrEmpty(_selectedFamily) && string.IsNullOrEmpty(_selectedType)) return "(none)";
                if (string.IsNullOrEmpty(_selectedType)) return _selectedFamily;
                return _selectedFamily + " / " + _selectedType;
            }
        }

        /// <summary>Supplies the family -&gt; types lookup so the row's Type combo follows its Family.</summary>
        public void SetTypesResolver(Func<string, IReadOnlyList<string>> resolver)
        {
            _typesResolver = resolver;
        }

        /// <param name="forceFirstType">
        /// True from the <see cref="SelectedFamily"/> setter: always select the new family's first type.
        /// False when only re-seeding the list, where an explicit user choice the family still offers survives.
        /// </param>
        public void RefreshAvailableTypesForSelectedFamily(bool forceFirstType = false)
        {
            if (_typesResolver == null) return;

            IReadOnlyList<string> types = _typesResolver(_selectedFamily) ?? new List<string>();
            AvailableTypes = types;

            if (types.Count == 0)
            {
                SelectedType = null;
                return;
            }

            if (forceFirstType
                || string.IsNullOrEmpty(_selectedType)
                || !ContainsIgnoreCase(types, _selectedType))
            {
                SelectedType = types[0];
            }
        }

        /// <summary>
        /// Applies the tab's universal Family/Type as this row's default. A row still sitting on the
        /// previous default follows the new one; a row the user overrode keeps its own selection.
        /// </summary>
        public void SetDeviceDefaults(string family, string type)
        {
            bool familyWasOverridden = IsFamilyOverridden;
            bool typeWasOverridden = IsTypeOverridden;

            _defaultFamily = family;
            _defaultType = type;

            if (_selectedFamily == null || !familyWasOverridden) _selectedFamily = family;
            if (_selectedType == null || !typeWasOverridden) _selectedType = type;

            if (_typesResolver != null)
            {
                IReadOnlyList<string> types = _typesResolver(_selectedFamily) ?? new List<string>();
                _availableTypes = types;
                if (types.Count == 0) _selectedType = null;
                else if (string.IsNullOrEmpty(_selectedType) || !ContainsIgnoreCase(types, _selectedType))
                    _selectedType = types[0];
                OnPropertyChanged(nameof(AvailableTypes));
            }

            OnPropertyChanged(nameof(DefaultFamily));
            OnPropertyChanged(nameof(DefaultType));
            OnPropertyChanged(nameof(SelectedFamily));
            OnPropertyChanged(nameof(SelectedType));
            OnPropertyChanged(nameof(IsFamilyOverridden));
            OnPropertyChanged(nameof(IsTypeOverridden));
            OnPropertyChanged(nameof(SelectedFamilyTypeDisplay));
        }

        public void ResetFamilyAndTypeToDefault()
        {
            _selectedFamily = _defaultFamily;
            _selectedType = _defaultType;

            if (_typesResolver != null)
            {
                IReadOnlyList<string> types = _typesResolver(_selectedFamily) ?? new List<string>();
                _availableTypes = types;
                if (types.Count > 0
                    && (string.IsNullOrEmpty(_selectedType) || !ContainsIgnoreCase(types, _selectedType)))
                {
                    _selectedType = types[0];
                }
                OnPropertyChanged(nameof(AvailableTypes));
            }

            OnPropertyChanged(nameof(SelectedFamily));
            OnPropertyChanged(nameof(SelectedType));
            OnPropertyChanged(nameof(IsFamilyOverridden));
            OnPropertyChanged(nameof(IsTypeOverridden));
            OnPropertyChanged(nameof(SelectedFamilyTypeDisplay));
        }

        private static bool ContainsIgnoreCase(IReadOnlyList<string> list, string value)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private IReadOnlyList<string> _availableFamilies;
        private IReadOnlyList<string> _availableTypes;
        private Func<string, IReadOnlyList<string>> _typesResolver;
        private string _selectedFamily;
        private string _selectedType;
        private string _defaultFamily;
        private string _defaultType;
    }
}
