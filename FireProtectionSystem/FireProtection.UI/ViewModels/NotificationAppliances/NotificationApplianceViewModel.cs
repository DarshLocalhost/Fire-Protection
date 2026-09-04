using System;
using System.Collections.Generic;
using System.Globalization;
using FireProtection.UI.Models;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Catalog;
using FireProtection.UI.ViewModels.Devices;
using FireProtection.UI.Views.Common;

namespace FireProtection.UI.ViewModels.NotificationAppliances
{
    /// <summary>
    /// Notification-appliance (strobe / speaker / speaker-strobe) placement tab. Inherits the shared device
    /// placement workflow (<see cref="DevicePlacementViewModelBase"/>) and adds the appliance-specific
    /// parameters (candela, dBA).
    ///
    /// Every option list here comes from the loaded Excel catalog (Decision 017) — nothing is hardcoded, and
    /// the Candela / dBA list is the set of combinations that actually exist in the workbook rather than a
    /// cartesian product of two invented ranges. Placement backend is deferred (UI-first slice).
    /// </summary>
    public class NotificationApplianceViewModel : DevicePlacementViewModelBase
    {
        private string _applianceType;
        private string _candela;
        private string _notificationDba;

        public NotificationApplianceViewModel()
            : this(null, null)
        {
        }

        public NotificationApplianceViewModel(FireProtectionUiData data)
            : this(data, null)
        {
        }

        public NotificationApplianceViewModel(FireProtectionUiData data, CatalogViewModel catalogViewModel)
            : base(data, null, null, catalogViewModel)
        {
            SeedAttributeDefaultsFromCatalog();

            // The base ctor applied its default selection before the attributes above existed; re-apply
            // now that they are seeded, matching the sprinkler tab's "arrive ready to place" behaviour.
            ApplyDefaultSelection();
        }

        public override string DeviceDisplayName => "NOTIFICATION APPLIANCE CONFIGURATION";

        // ---------------------------------------------------------------------------------------
        // Catalog-driven option lists (workbook only — no hardcoded fallback).
        // ---------------------------------------------------------------------------------------

        public IReadOnlyList<string> ApplianceTypeOptions =>
            Catalog != null ? Catalog.AvailableApplianceTypes : Empty;

        public IReadOnlyList<string> CandelaOptions =>
            Catalog != null ? Catalog.AvailableCandelas : Empty;

        public IReadOnlyList<string> NotificationDbaOptions =>
            Catalog != null ? Catalog.AvailableNotificationDbas : Empty;

        /// <summary>The Candela / dBA pairs that actually exist in the workbook, deduplicated.</summary>
        public IReadOnlyList<string> CandelaDbaOptions => BuildCandelaDbaOptions();

        public string ApplianceType
        {
            get => _applianceType;
            set
            {
                string old = _applianceType;
                if (SetProperty(ref _applianceType, value))
                {
                    PropagateUniversalDefault("ApplianceType", old, value);
                }
            }
        }

        public string Candela
        {
            get => _candela;
            set
            {
                if (SetProperty(ref _candela, value))
                {
                    PushCandelaDbaDefault();
                }
            }
        }

        public string NotificationDba
        {
            get => _notificationDba;
            set
            {
                if (SetProperty(ref _notificationDba, value))
                {
                    PushCandelaDbaDefault();
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

            IReadOnlyList<string> families = catalog.GetNotificationApplianceFamilies();
            if (families == null) return options;

            foreach (string family in families)
            {
                if (string.IsNullOrWhiteSpace(family)) continue;

                List<DeviceTypeOption> types = new List<DeviceTypeOption>();
                IReadOnlyList<string> typeNames = catalog.GetNotificationApplianceTypesForFamily(family);
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
            return catalog.GetNotificationApplianceTypesForFamily(familyName) ?? Empty;
        }

        protected override void OnCatalogChanged()
        {
            SeedAttributeDefaultsFromCatalog();

            OnPropertyChanged(nameof(ApplianceTypeOptions));
            OnPropertyChanged(nameof(CandelaOptions));
            OnPropertyChanged(nameof(NotificationDbaOptions));
            OnPropertyChanged(nameof(CandelaDbaOptions));
        }

        private void SeedAttributeDefaultsFromCatalog()
        {
            ApplianceType = PickDefault(ApplianceTypeOptions, _applianceType);
            Candela = PickDefault(CandelaOptions, _candela);
            NotificationDba = PickDefault(NotificationDbaOptions, _notificationDba);
        }

        /// <summary>Keeps the level/row-scope "CandelaDba" default in step with the two tab combos.</summary>
        private void PushCandelaDbaDefault()
        {
            string previous = _candelaDbaDefault;
            _candelaDbaDefault = FormatCandelaDba(_candela, _notificationDba);
            OnPropertyChanged(nameof(CandelaDbaOptions));
            PropagateUniversalDefault("CandelaDba", previous, _candelaDbaDefault);
        }

        private static string FormatCandelaDba(string candela, string dba)
        {
            if (string.IsNullOrEmpty(candela) && string.IsNullOrEmpty(dba)) return null;
            return (candela ?? "0") + "cd / " + (dba ?? "0") + "dBA";
        }

        private IReadOnlyList<string> BuildCandelaDbaOptions()
        {
            List<string> list = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ICatalog catalog = Catalog;
            if (catalog == null || !catalog.IsLoaded) return list;

            IReadOnlyList<string> families = catalog.GetNotificationApplianceFamilies();
            if (families == null) return list;

            foreach (string family in families)
            {
                IReadOnlyList<NotificationApplianceCatalogEntry> entries =
                    catalog.GetNotificationAppliancesForFamily(family);
                if (entries == null) continue;

                foreach (NotificationApplianceCatalogEntry entry in entries)
                {
                    if (entry == null) continue;

                    string label = FormatCandelaDba(
                        entry.Candela.ToString(CultureInfo.InvariantCulture),
                        entry.NotificationDba.ToString(CultureInfo.InvariantCulture));

                    if (label != null && seen.Add(label)) list.Add(label);
                }
            }

            return list;
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

        private string _candelaDbaDefault;

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
                "Appliance type and a Candela / dBA combo apply to all rooms on this level. " +
                "Rooms can override these per-row. Values come from the loaded catalog.",
                "ApplianceType", "Appliance Type", level.ApplianceType, ApplianceTypeOptions,
                "CandelaDba", "Candela / dBA", level.CandelaDba, CandelaDbaOptions,
                null, null, null, null);
            if (result == null) return;
            if (result.Field1Key == "ApplianceType" && result.Field1Value != null) level.ApplianceType = result.Field1Value;
            if (result.Field2Key == "CandelaDba" && !string.IsNullOrEmpty(result.Field2Value)) level.CandelaDba = result.Field2Value;
        }
    }
}
