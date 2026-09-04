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
    /// The actual placement backend is deferred (UI-first slice), so placement stays disabled via the base seam.
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
            : base(data, null, null, catalogViewModel)
        {
            // The base ctor already loaded families/types; seed the detector attributes from the
            // same workbook and push them down the universal -> level -> row chain.
            SeedAttributeDefaultsFromCatalog();

            // The base ctor applied its default selection before the attributes above existed; re-apply
            // now that they are seeded, matching the sprinkler tab's "arrive ready to place" behaviour.
            ApplyDefaultSelection();
        }

        public override string DeviceDisplayName => "SMOKE DETECTOR CONFIGURATION";

        // ---------------------------------------------------------------------------------------
        // Catalog-driven option lists (workbook only — no hardcoded fallback).
        // ---------------------------------------------------------------------------------------

        public IReadOnlyList<string> DetectorTypeOptions =>
            Catalog != null ? Catalog.AvailableDetectorTypes : Empty;

        public IReadOnlyList<string> CeilingMountOptions =>
            Catalog != null ? Catalog.AvailableMounts : Empty;

        public IReadOnlyList<string> CeilingSlopeOptions =>
            Catalog != null ? Catalog.AvailableCeilingSlopes : Empty;

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

        // ---------------------------------------------------------------------------------------
        // Per-level settings popover (Decision 019).
        // ---------------------------------------------------------------------------------------

        public override void OpenLevelSettings(DeviceLevelItemViewModel level)
        {
            if (level == null) return;
            // Dialogs.Owner is the tool window, the only window WPF will accept as an owner here.
            LevelSettingsResult result = LevelSettingsPopover.Show(
                Dialogs.Owner,
                level.Name,
                "Detector type, mount, and ceiling slope apply to all rooms on this level. " +
                "Rooms can override these per-row. Values come from the loaded catalog.",
                "DetectorType", "Detector Type", level.DetectorType, DetectorTypeOptions,
                "Mount", "Mount", level.Mount, CeilingMountOptions,
                "CeilingSlope", "Ceiling Slope", level.CeilingSlope, CeilingSlopeOptions);
            if (result == null) return;
            if (result.Field1Key == "DetectorType" && result.Field1Value != null) level.DetectorType = result.Field1Value;
            if (result.Field2Key == "Mount" && result.Field2Value != null) level.Mount = result.Field2Value;
            if (result.Field3Key == "CeilingSlope" && result.Field3Value != null) level.CeilingSlope = result.Field3Value;
        }
    }
}
