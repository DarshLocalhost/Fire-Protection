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
        private string _eligibilityStatusCode;
        private string _familyPlacementType;
        private string _hostingStrategy;
        private string _ceilingSource;
        private string _linkInstanceName;
        private string _hostCeilingElementId;
        private string _hostLevelId;
        private string _hostLevelName;
        private string _selectedFamily;
        private string _selectedType;
        private string _defaultFamily;
        private string _defaultType;
        private double? _maxSpacingFtOverride;
        private double? _boundaryClearanceFtOverride;
        private double? _defaultMaxSpacingFt;
        private double? _defaultBoundaryClearanceFt;
        private string _selectedOrientation;
        private string _defaultOrientation;

        private static readonly IReadOnlyList<string> OrientationOptionsList =
            new List<string> { "(auto)", "pendent", "upright", "sidewall" };

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
            _eligibilityState = EligibilityStates.Undetermined;
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
                EligibilityStates.Blocked,
                StringComparison.OrdinalIgnoreCase);

        /// <summary>True when eligibility could not be determined (configuration/infra error). Must NOT be
        /// conflated with BLOCKED — a normal missing ceiling/host is BLOCKED, not UNDETERMINED.</summary>
        public bool IsUndetermined =>
            string.Equals(
                _eligibilityState,
                EligibilityStates.Undetermined,
                StringComparison.OrdinalIgnoreCase);

        /// <summary>Raw three-state classification (ELIGIBLE / BLOCKED / UNDETERMINED).</summary>
        public string EligibilityState => _eligibilityState;

        /// <summary>Human-readable reason the room is blocked (empty when eligible). Shown in the UI tooltip.</summary>
        public string EligibilityReason => _eligibilityReason;

        /// <summary>
        /// Short, status-code-derived version of <see cref="EligibilityReason"/> for the room row, which is
        /// too narrow to show the full sentence without clipping it mid-word. The full reason stays in the
        /// tooltip.
        /// </summary>
        public string EligibilityShortReason =>
            EligibilityShortText.For(_eligibilityStatusCode, _eligibilityReason);

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
                _eligibilityState = EligibilityStates.Undetermined;
                _eligibilityReason = "Placement eligibility not evaluated.";
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

        public IReadOnlyList<string> AvailableFamilies
        {
            get { return _availableFamilies ?? (IReadOnlyList<string>)new List<string>(); }
            set
            {
                _availableFamilies = value;
                OnPropertyChanged();
            }
        }

        public IReadOnlyList<string> AvailableTypes
        {
            get { return _availableTypes ?? (IReadOnlyList<string>)new List<string>(); }
            set
            {
                _availableTypes = value;
                OnPropertyChanged();
            }
        }

        public string SelectedFamily
        {
            get { return _selectedFamily; }
            set
            {
                if (SetProperty(ref _selectedFamily, value))
                {
                    // The row's Type list is a function of the row's Family. Without this the
                    // combo keeps whatever list was seeded at startup (the first catalog family),
                    // so switching Family on a row appeared to have no effect on Type.
                    // forceFirstType: a Family change always re-points Type at the new family's first
                    // type, even when the old type name also exists under the new family — the user
                    // asked for the first type to be selected immediately on every family change.
                    RefreshAvailableTypesForSelectedFamily(true);

                    OnPropertyChanged(nameof(IsFamilyOverridden));
                    OnPropertyChanged(nameof(SelectedFamilyTypeDisplay));
                }
            }
        }

        /// <summary>
        /// Supplies the family -> types lookup (normally <c>ICatalog.GetSprinklerTypesForFamily</c>).
        /// Injected by the parent ViewModel so this row stays free of any catalog reference.
        /// </summary>
        public void SetTypesResolver(Func<string, IReadOnlyList<string>> resolver)
        {
            _typesResolver = resolver;
        }

        /// <summary>
        /// Recomputes <see cref="AvailableTypes"/> from <see cref="SelectedFamily"/> and keeps
        /// <see cref="SelectedType"/> valid for that family (first type when the current one is
        /// not offered by the new family).
        /// </summary>
        /// <param name="forceFirstType">
        /// True from the <see cref="SelectedFamily"/> setter: always select the new family's first type.
        /// False when only re-seeding the list (startup / catalog reload), where a type the user already
        /// picked and that the family still offers must survive.
        /// </param>
        public void RefreshAvailableTypesForSelectedFamily(bool forceFirstType = false)
        {
            if (_typesResolver == null) return;

            IReadOnlyList<string> types =
                _typesResolver(_selectedFamily) ?? new List<string>();

            AvailableTypes = types;

            if (types.Count == 0)
            {
                SelectedType = null;
                return;
            }

            if (forceFirstType
                || string.IsNullOrEmpty(_selectedType)
                || !types.Contains(_selectedType, StringComparer.OrdinalIgnoreCase))
            {
                SelectedType = types[0];
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

        public string DefaultFamily
        {
            get { return _defaultFamily; }
            set
            {
                if (SetProperty(ref _defaultFamily, value))
                {
                    OnPropertyChanged(nameof(IsFamilyOverridden));
                }
            }
        }

        public string DefaultType
        {
            get { return _defaultType; }
            set
            {
                if (SetProperty(ref _defaultType, value))
                {
                    OnPropertyChanged(nameof(IsTypeOverridden));
                }
            }
        }

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

        public double? MaxSpacingFtOverride
        {
            get { return _maxSpacingFtOverride; }
            set
            {
                if (SetProperty(ref _maxSpacingFtOverride, value))
                {
                    OnPropertyChanged(nameof(IsSpacingOverridden));
                    OnPropertyChanged(nameof(MaxSpacingFtOverrideDisplay));
                    SyncEditableText();
                }
            }
        }

        public double? BoundaryClearanceFtOverride
        {
            get { return _boundaryClearanceFtOverride; }
            set
            {
                if (SetProperty(ref _boundaryClearanceFtOverride, value))
                {
                    OnPropertyChanged(nameof(IsWallSpaceOverridden));
                    OnPropertyChanged(nameof(BoundaryClearanceFtOverrideDisplay));
                    SyncEditableText();
                }
            }
        }

        public double? DefaultMaxSpacingFt
        {
            get { return _defaultMaxSpacingFt; }
            set
            {
                if (SetProperty(ref _defaultMaxSpacingFt, value))
                {
                    OnPropertyChanged(nameof(IsSpacingOverridden));
                    OnPropertyChanged(nameof(MaxSpacingFtOverrideDisplay));
                    // The cell shows the default when no override exists, so a re-seeded default must
                    // repaint the cell too (unless the user is mid-keystroke).
                    if (!_editingText) OnPropertyChanged(nameof(MaxSpacingInput));
                }
            }
        }

        public double? DefaultBoundaryClearanceFt
        {
            get { return _defaultBoundaryClearanceFt; }
            set
            {
                if (SetProperty(ref _defaultBoundaryClearanceFt, value))
                {
                    OnPropertyChanged(nameof(IsWallSpaceOverridden));
                    OnPropertyChanged(nameof(BoundaryClearanceFtOverrideDisplay));
                    if (!_editingText) OnPropertyChanged(nameof(BoundaryClearanceInput));
                }
            }
        }

        public bool IsSpacingOverridden
        {
            get
            {
                if (!_maxSpacingFtOverride.HasValue) return false;
                if (!_defaultMaxSpacingFt.HasValue) return true;
                return Math.Abs(_maxSpacingFtOverride.Value - _defaultMaxSpacingFt.Value) > 1e-6;
            }
        }

        public bool IsWallSpaceOverridden
        {
            get
            {
                if (!_boundaryClearanceFtOverride.HasValue) return false;
                if (!_defaultBoundaryClearanceFt.HasValue) return true;
                return Math.Abs(_boundaryClearanceFtOverride.Value - _defaultBoundaryClearanceFt.Value) > 1e-6;
            }
        }

        public string MaxSpacingFtOverrideDisplay
        {
            get { return _maxSpacingFtOverride.HasValue ? _maxSpacingFtOverride.Value.ToString("F2") : "—"; }
        }

        public string BoundaryClearanceFtOverrideDisplay
        {
            get { return _boundaryClearanceFtOverride.HasValue ? _boundaryClearanceFtOverride.Value.ToString("F2") : "—"; }
        }

        public IReadOnlyList<string> OrientationOptions => OrientationOptionsList;

        public string SelectedOrientation
        {
            get { return _selectedOrientation ?? "(auto)"; }
            set
            {
                if (SetProperty(ref _selectedOrientation, value == "(auto)" ? null : value))
                {
                    OnPropertyChanged(nameof(IsOrientationOverridden));
                }
            }
        }

        public bool IsOrientationOverridden =>
            !string.IsNullOrEmpty(_selectedOrientation) &&
            !string.Equals(_selectedOrientation, _defaultOrientation ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // ----- Inline cell validation (item 8) -------------------------------------------------------
        // The editable cells bind to these strings rather than to the nullable doubles. A non-numeric or
        // non-positive entry is refused AT THE CELL: the typed text stays visible, an error is published for
        // the tooltip/highlight, and the underlying override keeps its last good value. Blank clears the
        // override (falling back to the level / universal value). Text is parsed in the current display unit.

        private string _maxSpacingText;
        private string _maxSpacingError;
        private string _clearanceText;
        private string _clearanceError;
        private bool _editingText;

        // Sane engineering bounds (decimal feet) for the editable spacing cells. These are NOT NFPA
        // values - the rule set + backend clamp still decide the real ceiling; they only stop an
        // obviously-wrong keystroke (negative, huge, or a zero that erases the layout) at the cell.
        private const double MaxSpacingMinFt = 1.0;
        private const double MaxSpacingMaxFt = 40.0;
        private const double ClearanceMinFt = 0.0;
        private const double ClearanceMaxFt = 10.0;

        public string MaxSpacingInput
        {
            // Shows the user's override; with no override, the real value the calculation used
            // (seeded from the eligibility calc pass) so the cell never lies about what is active.
            get { return _maxSpacingText ?? FormatEditable(_maxSpacingFtOverride ?? _defaultMaxSpacingFt); }
            set
            {
                _maxSpacingText = value;
                _editingText = true;
                try
                {
                    double? feet;
                    string error;
                    if (TryReadCell(value, MaxSpacingMinFt, MaxSpacingMaxFt, "Spacing", out feet, out error)) MaxSpacingFtOverride = feet;
                    MaxSpacingError = error;
                }
                finally { _editingText = false; }
                OnPropertyChanged();
            }
        }

        public string MaxSpacingError
        {
            get { return _maxSpacingError; }
            private set
            {
                if (_maxSpacingError == value) return;
                _maxSpacingError = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasMaxSpacingError));
                OnPropertyChanged(nameof(MaxSpacingTooltip));
            }
        }

        public bool HasMaxSpacingError => !string.IsNullOrEmpty(_maxSpacingError);

        public string BoundaryClearanceInput
        {
            get { return _clearanceText ?? FormatEditable(_boundaryClearanceFtOverride ?? _defaultBoundaryClearanceFt); }
            set
            {
                _clearanceText = value;
                _editingText = true;
                try
                {
                    double? feet;
                    string error;
                    if (TryReadCell(value, ClearanceMinFt, ClearanceMaxFt, "Wall space", out feet, out error)) BoundaryClearanceFtOverride = feet;
                    BoundaryClearanceError = error;
                }
                finally { _editingText = false; }
                OnPropertyChanged();
            }
        }

        public string BoundaryClearanceError
        {
            get { return _clearanceError; }
            private set
            {
                if (_clearanceError == value) return;
                _clearanceError = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasBoundaryClearanceError));
                OnPropertyChanged(nameof(BoundaryClearanceTooltip));
            }
        }

        public bool HasBoundaryClearanceError => !string.IsNullOrEmpty(_clearanceError);

        /// <summary>Cell tooltip: the validation error when the entry is bad, otherwise the field help text.
        /// One property so the tooltip is always the most useful message for the current state.</summary>
        public string MaxSpacingTooltip
        {
            get
            {
                return HasMaxSpacingError
                    ? _maxSpacingError
                    : "Device-to-device spacing (" + UnitDisplay.Suffix + "). Leave blank to use the rule-set default.";
            }
        }

        public string BoundaryClearanceTooltip
        {
            get
            {
                return HasBoundaryClearanceError
                    ? _clearanceError
                    : "Wall space / boundary clearance (" + UnitDisplay.Suffix + "). Leave blank to use the rule-set default.";
            }
        }

        /// <summary>Blank (or the em-dash placeholder) clears the override; anything else must parse as a
        /// length within the column's sane bounds. Returns false when the text is unusable, in which case
        /// the model is left alone. The bounds are UI sanity checks only — the rule set's hazard ceiling
        /// still clamps in the backend calculation.</summary>
        private static bool TryReadCell(string text, double minFt, double maxFt, string fieldName, out double? feet, out string error)
        {
            feet = null;
            error = null;

            if (string.IsNullOrWhiteSpace(text) || text.Trim() == "—") return true;

            double parsed;
            if (!UnitDisplay.TryParseToFeet(text, out parsed, out error)) return false;
            if (parsed < minFt || parsed > maxFt)
            {
                error = fieldName + " must be between " + UnitDisplay.FromFeet(minFt).ToString("F1") + " and "
                    + UnitDisplay.FromFeet(maxFt).ToString("F1") + " " + UnitDisplay.Suffix + ".";
                return false;
            }
            feet = parsed;
            return true;
        }

        private static string FormatEditable(double? feet)
        {
            return feet.HasValue ? UnitDisplay.FromFeet(feet.Value).ToString("F2") : string.Empty;
        }

        /// <summary>Re-reads the cell text from the model. Called whenever the override changes from somewhere
        /// other than the cell itself (bulk edit, reset, level propagation) so the cell never shows a stale value —
        /// but skipped while the user is typing, which would reformat their input mid-keystroke.</summary>
        private void SyncEditableText()
        {
            if (_editingText) return;
            _maxSpacingText = null;
            _clearanceText = null;
            MaxSpacingError = null;
            BoundaryClearanceError = null;
            OnPropertyChanged(nameof(MaxSpacingInput));
            OnPropertyChanged(nameof(BoundaryClearanceInput));
        }

        public void SetCatalogDefaults(string family, string type, double? maxSpacingFt, double? boundaryClearanceFt, string orientation = null)
        {
            // Decision 019 semantics, row scope: the top-level (universal) selection is the
            // default. A row that is still sitting on the previous default follows the new one;
            // a row the user has explicitly overridden keeps its own value. "Overridden" is
            // measured against the OLD default, so it must be read before the defaults change.
            bool familyWasOverridden = IsFamilyOverridden;
            bool typeWasOverridden = IsTypeOverridden;
            bool spacingWasOverridden = IsSpacingOverridden;
            bool wallSpaceWasOverridden = IsWallSpaceOverridden;
            bool orientationWasOverridden = IsOrientationOverridden;

            _defaultFamily = family;
            _defaultType = type;
            _defaultMaxSpacingFt = maxSpacingFt;
            _defaultBoundaryClearanceFt = boundaryClearanceFt;
            _defaultOrientation = orientation;

            if (_selectedFamily == null || !familyWasOverridden) _selectedFamily = family;
            if (_selectedType == null || !typeWasOverridden) _selectedType = type;
            if (!_maxSpacingFtOverride.HasValue || !spacingWasOverridden) _maxSpacingFtOverride = maxSpacingFt;
            if (!_boundaryClearanceFtOverride.HasValue || !wallSpaceWasOverridden) _boundaryClearanceFtOverride = boundaryClearanceFt;
            if ((_selectedOrientation == null || orientationWasOverridden)) _selectedOrientation = orientation;

            // The row's Type list follows the row's Family, which may have just been re-seeded.
            if (_typesResolver != null)
            {
                IReadOnlyList<string> types = _typesResolver(_selectedFamily) ?? new List<string>();
                _availableTypes = types;
                if (types.Count == 0)
                {
                    _selectedType = null;
                }
                else if (string.IsNullOrEmpty(_selectedType)
                         || !types.Contains(_selectedType, StringComparer.OrdinalIgnoreCase))
                {
                    _selectedType = types[0];
                }

                OnPropertyChanged(nameof(AvailableTypes));
            }

            OnPropertyChanged(nameof(DefaultFamily));
            OnPropertyChanged(nameof(DefaultType));
            OnPropertyChanged(nameof(DefaultMaxSpacingFt));
            OnPropertyChanged(nameof(DefaultBoundaryClearanceFt));
            // _selectedFamily / _selectedType are assigned directly above (bypassing the property
            // setters), so the row combos need an explicit notification - otherwise a catalog loaded
            // after the window is up seeds the backing fields without ever showing the selection.
            OnPropertyChanged(nameof(SelectedFamily));
            OnPropertyChanged(nameof(SelectedType));
            OnPropertyChanged(nameof(MaxSpacingFtOverride));
            OnPropertyChanged(nameof(BoundaryClearanceFtOverride));
            OnPropertyChanged(nameof(IsFamilyOverridden));
            OnPropertyChanged(nameof(IsTypeOverridden));
            OnPropertyChanged(nameof(IsSpacingOverridden));
            OnPropertyChanged(nameof(IsWallSpaceOverridden));
            OnPropertyChanged(nameof(MaxSpacingFtOverrideDisplay));
            OnPropertyChanged(nameof(BoundaryClearanceFtOverrideDisplay));
            OnPropertyChanged(nameof(SelectedFamilyTypeDisplay));
            OnPropertyChanged(nameof(SelectedOrientation));
            OnPropertyChanged(nameof(IsOrientationOverridden));
            SyncEditableText();
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
                    && (string.IsNullOrEmpty(_selectedType)
                        || !types.Contains(_selectedType, StringComparer.OrdinalIgnoreCase)))
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

        public void ResetSpacingOverridesToDefault()
        {
            _maxSpacingFtOverride = _defaultMaxSpacingFt;
            _boundaryClearanceFtOverride = _defaultBoundaryClearanceFt;
            _selectedOrientation = _defaultOrientation;
            OnPropertyChanged(nameof(MaxSpacingFtOverride));
            OnPropertyChanged(nameof(BoundaryClearanceFtOverride));
            OnPropertyChanged(nameof(IsSpacingOverridden));
            OnPropertyChanged(nameof(IsWallSpaceOverridden));
            OnPropertyChanged(nameof(MaxSpacingFtOverrideDisplay));
            OnPropertyChanged(nameof(BoundaryClearanceFtOverrideDisplay));
            OnPropertyChanged(nameof(SelectedOrientation));
            OnPropertyChanged(nameof(IsOrientationOverridden));
            SyncEditableText();
        }

        private IReadOnlyList<string> _availableFamilies;
        private IReadOnlyList<string> _availableTypes;
        private Func<string, IReadOnlyList<string>> _typesResolver;

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