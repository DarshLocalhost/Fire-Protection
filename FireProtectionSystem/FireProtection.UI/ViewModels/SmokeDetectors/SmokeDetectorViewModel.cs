using System;
using System.Collections.Generic;
using FireProtection.UI.Models;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Catalog;
using FireProtection.UI.ViewModels.Devices;
using FireProtection.UI.Views.Common;

namespace FireProtection.UI.ViewModels.SmokeDetectors
{
    /// <summary>
    /// Smoke-detector placement tab. Inherits the shared device placement workflow
    /// (<see cref="DevicePlacementViewModelBase"/>) and adds the detector-specific parameters.
    ///
    /// Every option list here comes from the loaded Excel catalog (Decision 017) — nothing is hardcoded.
    /// With no workbook loaded the combos are empty and the rooms report UNDETERMINED / CATALOG_NOT_LOADED.
    /// Detector type / mount / ceiling slope are DERIVED from the catalog row of the selected family/type
    /// and shown read-only (they are facts about the device, not user choices); placement consumes the
    /// derived values per room, following that row's own family/type.
    /// </summary>
    public class SmokeDetectorViewModel : DevicePlacementViewModelBase
    {
        private string _detectorType;
        private string _ceilingMount;
        private string _ceilingSlope;

        public SmokeDetectorViewModel()
            : this(null, null)
        {
        }

        public SmokeDetectorViewModel(FireProtectionUiData data)
            : this(data, null)
        {
        }

        public SmokeDetectorViewModel(FireProtectionUiData data, CatalogViewModel catalogViewModel)
            : this(data, null, null, catalogViewModel)
        {
        }

        public SmokeDetectorViewModel(
            FireProtectionUiData data,
            IDeviceFamilySource deviceFamilySource,
            IDevicePlacementExecutor deviceExecutor,
            CatalogViewModel catalogViewModel)
            : base(data, deviceFamilySource, deviceExecutor, catalogViewModel)
        {
            // The base ctor already loaded families/types; seed the detector attributes from the
            // same workbook and push them down the universal -> level -> row chain.
            SeedAttributeDefaultsFromCatalog();

            // The base ctor applied its default selection before the attributes above existed; re-apply
            // now that they are seeded, matching the sprinkler tab's "arrive ready to place" behaviour.
            ApplyDefaultSelection();

            // Last statement: clear the base ctor's construction-time suppression and run the single
            // initial eligibility preflight now that all defaults/attributes are seeded (no-op with no backend).
            InitializeEligibility();
        }

        public override string DeviceDisplayName => "SMOKE DETECTOR CONFIGURATION";

        protected override FireProtection.UI.Services.DeviceKind TabDeviceKind => FireProtection.UI.Services.DeviceKind.SmokeDetector;

        // ---------------------------------------------------------------------------------------
        // Catalog-driven option lists (workbook only — no hardcoded fallback).
        // ---------------------------------------------------------------------------------------

        public IReadOnlyList<string> DetectorTypeOptions =>
            Catalog != null ? Catalog.AvailableDetectorTypes : Empty;

        public IReadOnlyList<string> CeilingMountOptions =>
            Catalog != null ? Catalog.AvailableMounts : Empty;

        public IReadOnlyList<string> CeilingSlopeOptions =>
            Catalog != null ? Catalog.AvailableCeilingSlopes : Empty;

        // ---------------------------------------------------------------------------------------
        // Family/type-derived attributes. The catalog carries DetectorType, Mount and CeilingSlope
        // per (family, type) row, so with a catalog family/type selected these are READ-ONLY facts
        // about the device, not user choices: the picker is replaced by a text display, and
        // placement uses the derived value ahead of any level default (per-room, following that
        // row's own family/type). A field only keeps its dropdown when it is not derivable.
        // ---------------------------------------------------------------------------------------

        public string DerivedDetectorType => DeriveAttribute("DetectorType", UniversalFamilyName, UniversalTypeName);
        public string DerivedMount => DeriveAttribute("Mount", UniversalFamilyName, UniversalTypeName);
        public string DerivedCeilingSlope => DeriveAttribute("CeilingSlope", UniversalFamilyName, UniversalTypeName);

        public bool ShowDetectorTypePicker => string.IsNullOrWhiteSpace(DerivedDetectorType);
        public bool ShowMountPicker => string.IsNullOrWhiteSpace(DerivedMount);
        public bool ShowCeilingSlopePicker => string.IsNullOrWhiteSpace(DerivedCeilingSlope);

        private string UniversalFamilyName => SelectedDeviceFamily != null ? SelectedDeviceFamily.FamilyName : null;
        private string UniversalTypeName => SelectedDeviceType != null ? SelectedDeviceType.TypeName : null;

        protected override void OnUniversalFamilyTypeChanged()
        {
            RaiseDerivedAttributeNotifications();
        }

        protected override string DeriveAttribute(string key, string familyName, string typeName)
        {
            SmokeDetectorCatalogEntry entry = FindCatalogEntry(Catalog, familyName, typeName);
            if (entry == null) return null;

            switch (key)
            {
                case "DetectorType": return entry.DetectorType;
                case "Mount": return entry.Mount;
                case "CeilingSlope": return entry.CeilingSlope;
                default: return null;
            }
        }

        private static SmokeDetectorCatalogEntry FindCatalogEntry(ICatalog catalog, string familyName, string typeName)
        {
            if (catalog == null || string.IsNullOrWhiteSpace(familyName) || string.IsNullOrWhiteSpace(typeName))
                return null;

            IReadOnlyList<SmokeDetectorCatalogEntry> entries;
            try
            {
                entries = catalog.GetSmokeDetectorEntriesForFamily(familyName);
            }
            catch { return null; }

            if (entries == null) return null;
            foreach (SmokeDetectorCatalogEntry entry in entries)
            {
                if (entry != null && string.Equals(entry.TypeName, typeName, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
            return null;
        }

        private void RaiseDerivedAttributeNotifications()
        {
            OnPropertyChanged(nameof(DerivedDetectorType));
            OnPropertyChanged(nameof(DerivedMount));
            OnPropertyChanged(nameof(DerivedCeilingSlope));
            OnPropertyChanged(nameof(ShowDetectorTypePicker));
            OnPropertyChanged(nameof(ShowMountPicker));
            OnPropertyChanged(nameof(ShowCeilingSlopePicker));
        }

        public string DetectorType
        {
            get => _detectorType;
            set
            {
                string old = _detectorType;
                if (SetProperty(ref _detectorType, value))
                {
                    PropagateUniversalDefault("DetectorType", old, value);
                }
            }
        }

        public string CeilingMount
        {
            get => _ceilingMount;
            set
            {
                string old = _ceilingMount;
                if (SetProperty(ref _ceilingMount, value))
                {
                    PropagateUniversalDefault("Mount", old, value);
                }
            }
        }

        public string CeilingSlope
        {
            get => _ceilingSlope;
            set
            {
                string old = _ceilingSlope;
                if (SetProperty(ref _ceilingSlope, value))
                {
                    PropagateUniversalDefault("CeilingSlope", old, value);
                }
            }
        }

        // ---------------------------------------------------------------------------------------
        // Catalog -> family/type wiring consumed by the base.
        // ---------------------------------------------------------------------------------------

        protected override IReadOnlyList<DeviceFamilyOption> GetCatalogFamilyOptions()
        {
            List<DeviceFamilyOption> options = new List<DeviceFamilyOption>();

            ICatalog catalog = Catalog;
            if (catalog == null || !catalog.IsLoaded) return options;

            IReadOnlyList<string> families = catalog.GetSmokeDetectorFamilies();
            if (families == null) return options;

            foreach (string family in families)
            {
                if (string.IsNullOrWhiteSpace(family)) continue;

                List<DeviceTypeOption> types = new List<DeviceTypeOption>();
                IReadOnlyList<string> typeNames = catalog.GetSmokeDetectorTypesForFamily(family);
                if (typeNames != null)
                {
                    foreach (string typeName in typeNames)
                    {
                        if (string.IsNullOrWhiteSpace(typeName)) continue;
                        types.Add(new DeviceTypeOption { FamilyName = family, TypeName = typeName });
                    }
                }

                options.Add(new DeviceFamilyOption { FamilyName = family, Types = types });
            }

            return options;
        }

        protected override IReadOnlyList<string> GetCatalogTypesForFamily(string familyName)
        {
            ICatalog catalog = Catalog;
            if (catalog == null || string.IsNullOrEmpty(familyName)) return Empty;
            return catalog.GetSmokeDetectorTypesForFamily(familyName) ?? Empty;
        }

        protected override void OnCatalogChanged()
        {
            SeedAttributeDefaultsFromCatalog();

            OnPropertyChanged(nameof(DetectorTypeOptions));
            OnPropertyChanged(nameof(CeilingMountOptions));
            OnPropertyChanged(nameof(CeilingSlopeOptions));
            RaiseDerivedAttributeNotifications();
        }

        /// <summary>
        /// Takes the first workbook value for each detector attribute as the tab-wide (universal)
        /// default, keeping a value the user already picked if the new workbook still offers it.
        /// </summary>
        private void SeedAttributeDefaultsFromCatalog()
        {
            DetectorType = PickDefault(DetectorTypeOptions, _detectorType);
            CeilingMount = PickDefault(CeilingMountOptions, _ceilingMount);
            CeilingSlope = PickDefault(CeilingSlopeOptions, _ceilingSlope);
        }

        private static string PickDefault(IReadOnlyList<string> options, string current)
        {
            if (options == null || options.Count == 0) return null;
            if (!string.IsNullOrEmpty(current) && Contains(options, current)) return current;
            return options[0];
        }

        private static bool Contains(IReadOnlyList<string> options, string value)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static readonly IReadOnlyList<string> Empty = new List<string>();

        // Per-level attribute settings (Decision 019 popover) are retired for this tab: DetectorType,
        // Mount and CeilingSlope are now derived from the catalog row of the selected (or per-row)
        // family/type, so there is nothing meaningful left to set per level. The base's no-op
        // OpenLevelSettings + HasLevelSettings=false hide the level "..." button.
    }
}
