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
    /// cartesian product of two invented ranges. Appliance type and the Candela/dBA pair are DERIVED from
    /// the catalog row of the selected family/type and shown read-only when derivable; placement consumes
    /// the derived values per room, following that row's own family/type.
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
            : this(data, null, null, catalogViewModel)
        {
        }

        public NotificationApplianceViewModel(
            FireProtectionUiData data,
            IDeviceFamilySource deviceFamilySource,
            IDevicePlacementExecutor deviceExecutor,
            CatalogViewModel catalogViewModel)
            : base(data, deviceFamilySource, deviceExecutor, catalogViewModel)
        {
            SeedAttributeDefaultsFromCatalog();

            // The base ctor applied its default selection before the attributes above existed; re-apply
            // now that they are seeded, matching the sprinkler tab's "arrive ready to place" behaviour.
            ApplyDefaultSelection();

            // Last statement: clear the base ctor's construction-time suppression and run the single
            // initial eligibility preflight now that all defaults/attributes are seeded (no-op with no backend).
            InitializeEligibility();
        }

        public override string DeviceDisplayName => "NOTIFICATION APPLIANCE CONFIGURATION";

        protected override FireProtection.UI.Services.DeviceKind TabDeviceKind => FireProtection.UI.Services.DeviceKind.NotificationAppliance;

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

        // ---------------------------------------------------------------------------------------
        // Family/type-derived attributes. The catalog carries ApplianceType, Candela and dBA per
        // (family, type) row, so with a catalog family/type selected these are READ-ONLY facts about
        // the device, not user choices: the pickers are replaced by text, and placement uses the
        // derived values ahead of any level default (per-room, following that row's own family/type).
        // Candela and dBA are derived together (one catalog row) or not at all.
        // ---------------------------------------------------------------------------------------

        public string DerivedApplianceType => DeriveAttribute("ApplianceType", UniversalFamilyName, UniversalTypeName);
        public string DerivedCandelaDba => DeriveAttribute("CandelaDba", UniversalFamilyName, UniversalTypeName);

        /// <summary>Individual read-only displays for the Candela / dBA pairs (derived together or not at all).</summary>
        public string DerivedCandela
        {
            get
            {
                NotificationApplianceCatalogEntry e = FindCatalogEntry(Catalog, UniversalFamilyName, UniversalTypeName);
                return e == null ? null : e.Candela.ToString(CultureInfo.InvariantCulture);
            }
        }

        public string DerivedNotificationDba
        {
            get
            {
                NotificationApplianceCatalogEntry e = FindCatalogEntry(Catalog, UniversalFamilyName, UniversalTypeName);
                return e == null ? null : e.NotificationDba.ToString(CultureInfo.InvariantCulture);
            }
        }

        public bool ShowApplianceTypePicker => string.IsNullOrWhiteSpace(DerivedApplianceType);
        public bool ShowCandelaDbaPickers => string.IsNullOrWhiteSpace(DerivedCandelaDba);

        private string UniversalFamilyName => SelectedDeviceFamily != null ? SelectedDeviceFamily.FamilyName : null;
        private string UniversalTypeName => SelectedDeviceType != null ? SelectedDeviceType.TypeName : null;

        protected override void OnUniversalFamilyTypeChanged()
        {
            RaiseDerivedAttributeNotifications();
        }

        protected override string DeriveAttribute(string key, string familyName, string typeName)
        {
            NotificationApplianceCatalogEntry entry = FindCatalogEntry(Catalog, familyName, typeName);
            if (entry == null) return null;

            switch (key)
            {
                case "ApplianceType": return entry.ApplianceType;
                case "CandelaDba":
                    // A row with no visible AND no audible rating carries no derivable information; leave
                    // it undesired so the tab/level/row pickers stay functional.
                    if (entry.Candela <= 0 && entry.NotificationDba <= 0) return null;
                    return FormatCandelaDba(
                        entry.Candela.ToString(CultureInfo.InvariantCulture),
                        entry.NotificationDba.ToString(CultureInfo.InvariantCulture));
                default: return null;
            }
        }

        private static NotificationApplianceCatalogEntry FindCatalogEntry(ICatalog catalog, string familyName, string typeName)
        {
            if (catalog == null || string.IsNullOrWhiteSpace(familyName) || string.IsNullOrWhiteSpace(typeName))
                return null;

            IReadOnlyList<NotificationApplianceCatalogEntry> entries;
            try
            {
                entries = catalog.GetNotificationAppliancesForFamily(familyName);
            }
            catch { return null; }

            if (entries == null) return null;
            foreach (NotificationApplianceCatalogEntry entry in entries)
            {
                if (entry != null && string.Equals(entry.TypeName, typeName, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
            return null;
        }

        private void RaiseDerivedAttributeNotifications()
        {
            OnPropertyChanged(nameof(DerivedApplianceType));
            OnPropertyChanged(nameof(DerivedCandelaDba));
            OnPropertyChanged(nameof(DerivedCandela));
            OnPropertyChanged(nameof(DerivedNotificationDba));
            OnPropertyChanged(nameof(ShowApplianceTypePicker));
            OnPropertyChanged(nameof(ShowCandelaDbaPickers));
        }

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
            RaiseDerivedAttributeNotifications();
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

        // Per-level attribute settings (Decision 019 popover) are retired for this tab: ApplianceType and
        // the Candela / dBA pair are now derived from the catalog row of the selected (or per-row)
        // family/type; when not derivable, the tab-wide pickers above are the fallback chain. The base's
        // no-op OpenLevelSettings + HasLevelSettings=false hide the level "..." button.
    }
}
