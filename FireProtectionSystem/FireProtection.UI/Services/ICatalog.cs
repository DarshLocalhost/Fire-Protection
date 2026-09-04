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
