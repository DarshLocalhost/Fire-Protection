using System;
using System.Collections.Generic;
using System.Linq;
using FireProtection.UI.Services;

namespace FireProtection.Backend.Services.Catalog
{
    /// <summary>
    /// An <see cref="ICatalog"/> whose FAMILY AND TYPE LISTS come from the open Revit document
    /// (supplied by <see cref="ModelFamilyEnumerator"/>, which is the Revit-aware edge), with every
    /// per-type engineering value overlaid from the Excel workbook when one is loaded.
    ///
    /// REVIT-FREE BY CONSTRUCTION
    /// --------------------------
    /// This class contains no reference to <c>Autodesk.Revit.DB</c>. That is deliberate: the
    /// headless test harness must be able to construct and exercise the source-switch behaviour
    /// without RevitAPI.dll. Reading the document happens in <see cref="ModelFamilyEnumerator.LoadInto"/>,
    /// which passes plain <see cref="ModelFamilyType"/> records in.
    ///
    /// WHY THE SPLIT
    /// -------------
    /// The two sources are complementary, not interchangeable:
    ///
    ///   Model      -> what is really loadable, plus <c>FamilyPlacementType</c> (authoritative for
    ///                 how a device is hosted). NO engineering values whatsoever.
    ///   Workbook   -> every per-type engineering value (hazard class, coverage area, spacing,
    ///                 coverage radius, K-factor, temperature, deflector, sidewall values, and for
    ///                 alarms the detector/appliance attributes). It carries NO placement-type
    ///                 information at all.
    ///
    /// Treating the workbook as the sole source silently blanks the per-type values whenever a
    /// row is missing, and the calculation engine then quietly falls back to provisional
    /// hazard-class spacing with nothing shown to the user. Treating the model as the sole source
    /// discards the engineering data entirely. This class therefore takes NAMES from the model and
    /// VALUES from the workbook; the UI banner reports when a lookup found nothing.
    ///
    /// SHARED DEVICE LIST
    /// ------------------
    /// Smoke detectors and notification appliances share the Revit category
    /// <c>OST_FireAlarmDevices</c>, and the product decision is to show the SAME list on both tabs.
    /// Because the workbook stores them in two separate sheets, every alarm-device lookup goes
    /// through <see cref="DeviceCatalogOverlay"/>, which searches BOTH sheets — otherwise a strobe
    /// shown on the Smoke tab (listed under NotificationAppliances) would find no row and lose its
    /// attributes, re-exposing empty editable pickers.
    /// </summary>
    public sealed class ModelBackedCatalog : ICatalog
    {
        private readonly Func<ICatalog> _overlayAccessor;

        /// <summary>Sprinkler family -> types, read from the model.</summary>
        private readonly List<string> _sprinklerFamilies = new List<string>();
        private readonly Dictionary<string, List<string>> _sprinklerTypes =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Fire-alarm family -> types, read from the model. Shared by both device tabs.</summary>
        private readonly List<string> _deviceFamilies = new List<string>();
        private readonly Dictionary<string, List<string>> _deviceTypes =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Set when the model listing was built, so the UI can rebuild it when the user reloads
        /// families in Revit without restarting the add-in.
        /// </summary>
        public DateTime BuiltUtc { get; private set; }

        /// <summary>Number of sprinkler families found in the model.</summary>
        public int ModelSprinklerFamilyCount { get { return _sprinklerFamilies.Count; } }

        /// <summary>Number of fire-alarm families found in the model (shared by both device tabs).</summary>
        public int ModelDeviceFamilyCount { get { return _deviceFamilies.Count; } }

        /// <summary>
        /// Builds the family/type indexes from already-enumerated model families.
        ///
        /// Separated from <see cref="Refresh"/> so the grouping, ordering and shared-device-list
        /// behaviour can be exercised WITHOUT a Revit document, which cannot be constructed in the
        /// headless test harness.
        /// </summary>
        public void LoadFrom(
            IEnumerable<ModelFamilyType> sprinklerTypes,
            IEnumerable<ModelFamilyType> deviceTypes)
        {
            _sprinklerFamilies.Clear();
            _sprinklerTypes.Clear();
            _deviceFamilies.Clear();
            _deviceTypes.Clear();

            Index(sprinklerTypes, _sprinklerFamilies, _sprinklerTypes);
            Index(deviceTypes, _deviceFamilies, _deviceTypes);

            _sprinklerFamilies.Sort(StringComparer.OrdinalIgnoreCase);
            _deviceFamilies.Sort(StringComparer.OrdinalIgnoreCase);

            BuiltUtc = DateTime.UtcNow;
        }

        private static void Index(
            IEnumerable<ModelFamilyType> types,
            List<string> families,
            Dictionary<string, List<string>> byFamily)
        {
            if (types == null) return;

            foreach (ModelFamilyType t in types)
            {
                if (t == null || string.IsNullOrWhiteSpace(t.FamilyName)) continue;

                List<string> familyTypes;
                if (!byFamily.TryGetValue(t.FamilyName, out familyTypes))
                {
                    familyTypes = new List<string>();
                    byFamily[t.FamilyName] = familyTypes;
                    families.Add(t.FamilyName);
                }
                if (!string.IsNullOrWhiteSpace(t.TypeName)) familyTypes.Add(t.TypeName);
            }
        }

        /// <param name="overlayAccessor">
        /// Lazy accessor for the currently loaded Excel catalog, or null when none is. Read on
        /// EVERY call because the user may load or change the workbook at any time.
        /// </param>
        public ModelBackedCatalog(Func<ICatalog> overlayAccessor)
        {
            _overlayAccessor = overlayAccessor;
        }

        /// <summary>The loaded Excel catalog, or null. Never throws.</summary>
        private ICatalog Overlay
        {
            get
            {
                if (_overlayAccessor == null) return null;
                try
                {
                    ICatalog c = _overlayAccessor();
                    if (c != null && c.IsLoaded) return c;
                }
                catch (Exception ex)
                {
                    FireProtectionLog.Warn("Model catalog overlay lookup failed: " + ex.Message);
                }
                return null;
            }
        }

        // -----------------------------------------------------------------------------------
        // Model-owned identity
        // -----------------------------------------------------------------------------------

        public bool IsLoaded { get { return _sprinklerFamilies.Count > 0 || _deviceFamilies.Count > 0; } }

        public string CatalogVersion
        {
            get
            {
                ICatalog o = Overlay;
                string v = o != null ? o.CatalogVersion : null;
                return "model" + (string.IsNullOrEmpty(v) ? string.Empty : " + catalog v" + v);
            }
        }

        public string SourcePath { get { return Overlay != null ? Overlay.SourcePath : null; } }

        /// <summary>
        /// Row count of the OVERLAY workbook. In model mode the meaningful population is the family
        /// count, so this deliberately reports 0 when no workbook is loaded, which keeps the UI's
        /// "N rows" phrasing meaningful.
        /// </summary>
        public int TotalRowCount { get { ICatalog o = Overlay; return o != null ? o.TotalRowCount : 0; } }

        public IReadOnlyList<string> GetSprinklerFamilies() { return _sprinklerFamilies.ToList(); }

        public IReadOnlyList<string> GetSprinklerTypesForFamily(string familyName)
        {
            List<string> types;
            if (!string.IsNullOrEmpty(familyName) && _sprinklerTypes.TryGetValue(familyName.Trim(), out types))
                return types.ToList();
            return new List<string>();
        }

        // Both alarm-device tabs return the SAME shared list (OST_FireAlarmDevices).
        public IReadOnlyList<string> GetSmokeDetectorFamilies() { return _deviceFamilies.ToList(); }

        public IReadOnlyList<string> GetSmokeDetectorTypesForFamily(string familyName)
        {
            List<string> types;
            if (!string.IsNullOrEmpty(familyName) && _deviceTypes.TryGetValue(familyName.Trim(), out types))
                return types.ToList();
            return new List<string>();
        }

        public IReadOnlyList<string> GetNotificationApplianceFamilies() { return _deviceFamilies.ToList(); }

        public IReadOnlyList<string> GetNotificationApplianceTypesForFamily(string familyName)
        {
            return GetSmokeDetectorTypesForFamily(familyName);
        }

        // -----------------------------------------------------------------------------------
        // Overlay-owned engineering values (sprinklers)
        // -----------------------------------------------------------------------------------

        public IReadOnlyList<string> AvailableHazardClasses { get { return OverlayValue(o => o.AvailableHazardClasses); } }

        public IReadOnlyList<string> AvailableSprinklerMounts { get { return OverlayValue(o => o.AvailableSprinklerMounts); } }

        public IReadOnlyList<string> GetHazardClassesForSprinklerFamily(string familyName)
        {
            ICatalog o = Overlay;
            return o != null ? o.GetHazardClassesForSprinklerFamily(familyName) : new List<string>();
        }

        public string GetHazardClassForSprinkler(string familyName, string typeName)
        {
            ICatalog o = Overlay;
            return o != null ? o.GetHazardClassForSprinkler(familyName, typeName) : null;
        }

        public SprinklerCatalogEntry GetSprinklerEntry(string familyName, string typeName)
        {
            ICatalog o = Overlay;
            return o != null ? o.GetSprinklerEntry(familyName, typeName) : null;
        }

        public IReadOnlyList<SprinklerCatalogEntry> GetSprinklerEntriesForFamily(string familyName)
        {
            ICatalog o = Overlay;
            return o != null ? o.GetSprinklerEntriesForFamily(familyName) : new List<SprinklerCatalogEntry>();
        }

        public string GetSprinklerMount(string familyName, string typeName)
        {
            ICatalog o = Overlay;
            return o != null ? o.GetSprinklerMount(familyName, typeName) : null;
        }

        // -----------------------------------------------------------------------------------
        // Overlay-owned engineering values (alarms) — BOTH SHEETS searched
        // -----------------------------------------------------------------------------------

        public IReadOnlyList<string> AvailableDetectorTypes { get { return OverlayValue(o => o.AvailableDetectorTypes); } }

        public IReadOnlyList<string> AvailableMounts { get { return OverlayValue(o => o.AvailableMounts); } }

        public IReadOnlyList<string> AvailableCeilingSlopes { get { return OverlayValue(o => o.AvailableCeilingSlopes); } }

        public IReadOnlyList<string> AvailableApplianceTypes { get { return OverlayValue(o => o.AvailableApplianceTypes); } }

        public IReadOnlyList<string> AvailableCandelas { get { return OverlayValue(o => o.AvailableCandelas); } }

        public IReadOnlyList<string> AvailableNotificationDbas { get { return OverlayValue(o => o.AvailableNotificationDbas); } }

        /// <summary>
        /// Smoke-detector attributes for a family, searched across BOTH alarm sheets via
        /// <see cref="DeviceCatalogOverlay"/>. See that class for why the cross-sheet scan is
        /// required by the shared device list.
        /// </summary>
        public IReadOnlyList<SmokeDetectorCatalogEntry> GetSmokeDetectorEntriesForFamily(string familyName)
        {
            return DeviceCatalogOverlay.SmokeEntries(Overlay, familyName);
        }

        /// <summary>
        /// Notification-appliance attributes for a family, searched across BOTH alarm sheets.
        /// Mirror image of <see cref="GetSmokeDetectorEntriesForFamily"/>.
        /// </summary>
        public IReadOnlyList<NotificationApplianceCatalogEntry> GetNotificationAppliancesForFamily(string familyName)
        {
            return DeviceCatalogOverlay.ApplianceEntries(Overlay, familyName);
        }

        /// <summary>
        private IReadOnlyList<string> OverlayValue(Func<ICatalog, IReadOnlyList<string>> selector)
        {
            ICatalog o = Overlay;
            if (o == null) return new List<string>();
            try
            {
                IReadOnlyList<string> v = selector(o);
                return v ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }
    }
}