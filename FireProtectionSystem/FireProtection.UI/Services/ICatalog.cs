using System.Collections.Generic;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Revit-free read-only view of the loaded Excel catalog. The concrete implementation
    /// lives in FireProtection.Backend (where ClosedXML is loaded). The UI consumes this
    /// interface so it never depends on the workbook directly.
    /// </summary>
    public interface ICatalog
    {
        bool IsLoaded { get; }

        string CatalogVersion { get; }

        string SourcePath { get; }

        int TotalRowCount { get; }

        IReadOnlyList<string> AvailableHazardClasses { get; }

        IReadOnlyList<string> AvailableSprinklerMounts { get; }

        IReadOnlyList<string> AvailableDetectorTypes { get; }

        IReadOnlyList<string> AvailableMounts { get; }

        IReadOnlyList<string> AvailableCeilingSlopes { get; }

        IReadOnlyList<string> AvailableApplianceTypes { get; }

        /// <summary>Distinct Candela values present in the workbook, ascending, as strings.</summary>
        IReadOnlyList<string> AvailableCandelas { get; }

        /// <summary>Distinct non-zero NotificationDba values present in the workbook, ascending.</summary>
        IReadOnlyList<string> AvailableNotificationDbas { get; }

        IReadOnlyList<string> GetSprinklerFamilies();

        IReadOnlyList<string> GetSprinklerTypesForFamily(string familyName);

        IReadOnlyList<string> GetHazardClassesForSprinklerFamily(string familyName);

        string GetHazardClassForSprinkler(string familyName, string typeName);

        /// <summary>
        /// Full per-type sprinkler entry (identity + listed coverage/spacing/K-factor/etc.) for the
        /// given (family, type), or <c>null</c> when no row matches. Numeric fields are nullable:
        /// a null means the workbook left the cell blank and the engine should fall back to the
        /// provisional hazard-class default.
        /// </summary>
        SprinklerCatalogEntry GetSprinklerEntry(string familyName, string typeName);

        /// <summary>All per-type sprinkler entries for a family (used by the UI parameter card).</summary>
        IReadOnlyList<SprinklerCatalogEntry> GetSprinklerEntriesForFamily(string familyName);

        /// <summary>
        /// Returns the <c>Mount</c> value (e.g. <c>"Pendent"</c>, <c>"Sidewall"</c>,
        /// <c>"Upright"</c>) for the given sprinkler (family, type), or <c>null</c>
        /// when no row matches. Comparison is case-insensitive and trims
        /// whitespace. Consumed by the device-placement resolver to refine
        /// <c>DevicePlacementBehavior.WorkPlaneDependent</c> into the mount-specific
        /// bucket (<c>CeilingOverhead</c> / <c>WallSidewall</c>).
        /// </summary>
        string GetSprinklerMount(string familyName, string typeName);

        IReadOnlyList<string> GetSmokeDetectorFamilies();

        IReadOnlyList<string> GetSmokeDetectorTypesForFamily(string familyName);

        IReadOnlyList<SmokeDetectorCatalogEntry> GetSmokeDetectorEntriesForFamily(string familyName);

        IReadOnlyList<string> GetNotificationApplianceFamilies();

        IReadOnlyList<string> GetNotificationApplianceTypesForFamily(string familyName);

        IReadOnlyList<NotificationApplianceCatalogEntry> GetNotificationAppliancesForFamily(string familyName);
    }

    public sealed class SmokeDetectorCatalogEntry
    {
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string DetectorType { get; set; }
        public string Mount { get; set; }
        public string CeilingSlope { get; set; }
    }

    public sealed class SprinklerCatalogEntry
    {
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string HazardClass { get; set; }
        public string Mount { get; set; }
        public string SprinklerClass { get; set; }
        public double? MaxCoverageAreaSqFt { get; set; }
        public double? MaxSpacingFt { get; set; }
        public double? MinSpacingFt { get; set; }
        public double? CoverageRadiusFt { get; set; }
        public double? KFactor { get; set; }
        public string ResponseType { get; set; }
        public int? TempRatingF { get; set; }
        public double? DeflectorToCeilingIn { get; set; }
        public double? SidewallMaxAlongWallSpacingFt { get; set; }
        public double? SidewallMaxThrowFt { get; set; }
        public double? SidewallEndWallClearanceFt { get; set; }

        /// <summary>Compact one-line summary for the UI parameter card. Only lists values present.</summary>
        public string DisplayLabel
        {
            get
            {
                System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(SprinklerClass)) parts.Add(SprinklerClass);
                if (MaxCoverageAreaSqFt.HasValue) parts.Add("cov " + MaxCoverageAreaSqFt.Value + " ft²");
                if (MaxSpacingFt.HasValue) parts.Add("S→S " + MaxSpacingFt.Value + " ft");
                if (KFactor.HasValue) parts.Add("K" + KFactor.Value);
                if (!string.IsNullOrWhiteSpace(ResponseType)) parts.Add(ResponseType);
                if (TempRatingF.HasValue) parts.Add(TempRatingF.Value + "°F");
                if (SidewallMaxAlongWallSpacingFt.HasValue) parts.Add("wall S→S " + SidewallMaxAlongWallSpacingFt.Value + " ft");
                if (SidewallMaxThrowFt.HasValue) parts.Add("throw " + SidewallMaxThrowFt.Value + " ft");
                return parts.Count == 0 ? "No listed data (uses provisional defaults)" : string.Join(" / ", parts);
            }
        }
    }

    public sealed class NotificationApplianceCatalogEntry
    {
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string ApplianceType { get; set; }
        public int Candela { get; set; }
        public int NotificationDba { get; set; }

        public string DisplayLabel
        {
            get
            {
                if (string.IsNullOrEmpty(ApplianceType)) return TypeName ?? string.Empty;
                return ApplianceType + " " + Candela + "cd / " + NotificationDba + "dBA";
            }
        }
    }
}
