using System;
using System.Collections.Generic;
using System.Linq;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Sprinklers.BruteForce;

namespace FireProtection.Backend.Services.Catalog
{
    /// <summary>
    /// Revit-free catalog view. Wraps a loaded <see cref="Catalog"/> and exposes the
    /// UI-facing <see cref="ICatalog"/> lookups (families, types, per-row options).
    /// Construct via <see cref="LoadFromFile"/> or <see cref="FromCatalog"/>.
    /// </summary>
    public sealed class CatalogService : ICatalog
    {
        private readonly Catalog _catalog;
        private readonly List<string> _sprinklerFamilies;
        private readonly Dictionary<string, List<SprinklerCatalogRow>> _sprinklerByFamily;
        private readonly List<string> _smokeFamilies;
        private readonly Dictionary<string, List<SmokeDetectorCatalogRow>> _smokeByFamily;
        private readonly List<string> _naFamilies;
        private readonly Dictionary<string, List<NotificationApplianceCatalogRow>> _naByFamily;

        private CatalogService(Catalog catalog)
        {
            _catalog = catalog ?? new Catalog();

            _sprinklerByFamily = _catalog.Sprinklers
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.FamilyName))
                .GroupBy(r => r.FamilyName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            _sprinklerFamilies = _sprinklerByFamily.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

            _smokeByFamily = _catalog.SmokeDetectors
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.FamilyName))
                .GroupBy(r => r.FamilyName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            _smokeFamilies = _smokeByFamily.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

            _naByFamily = _catalog.NotificationAppliances
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.FamilyName))
                .GroupBy(r => r.FamilyName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            _naFamilies = _naByFamily.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static CatalogService LoadFromFile(string path)
        {
            Catalog catalog = CatalogLoader.Load(path);
            return new CatalogService(catalog);
        }

        public static CatalogService FromCatalog(Catalog catalog)
        {
            return new CatalogService(catalog);
        }

        public static CatalogService Empty()
        {
            return new CatalogService(new Catalog());
        }

        public Catalog RawCatalog { get { return _catalog; } }

        public bool IsLoaded { get { return _catalog != null && _catalog.TotalRowCount > 0; } }

        public string CatalogVersion { get { return _catalog?.CatalogVersion; } }

        public string SourcePath { get { return _catalog?.SourcePath; } }

        public int TotalRowCount { get { return _catalog?.TotalRowCount ?? 0; } }

        public IReadOnlyList<string> AvailableHazardClasses
        {
            get
            {
                HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (_catalog != null && _catalog.Sprinklers != null)
                {
                    foreach (SprinklerCatalogRow r in _catalog.Sprinklers)
                    {
                        if (r == null) continue;
                        if (!string.IsNullOrWhiteSpace(r.HazardClass)) set.Add(r.HazardClass.Trim());
                    }
                }
                // Hazard class is deliberately NOT a workbook-driven attribute: the shipped
                // catalog leaves the column blank and the room's hazard class is chosen per room
                // in the UI from HazardClassOptions.All. The union stays here so this list is
                // never empty. Every other Available* list below is workbook-only.
                foreach (string h in HazardClassOptions.All)
                {
                    set.Add(h);
                }
                return set.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public IReadOnlyList<string> AvailableSprinklerMounts
        {
            get
            {
                HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (_catalog != null && _catalog.Sprinklers != null)
                {
                    foreach (SprinklerCatalogRow r in _catalog.Sprinklers)
                    {
                        if (r == null) continue;
                        if (!string.IsNullOrWhiteSpace(r.Mount)) set.Add(r.Mount.Trim());
                    }
                }
                return set.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public IReadOnlyList<string> AvailableDetectorTypes
        {
            get
            {
                HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (_catalog != null && _catalog.SmokeDetectors != null)
                {
                    foreach (SmokeDetectorCatalogRow r in _catalog.SmokeDetectors)
                    {
                        if (r == null) continue;
                        if (!string.IsNullOrWhiteSpace(r.DetectorType)) set.Add(r.DetectorType.Trim());
                    }
                }
                return set.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public IReadOnlyList<string> AvailableMounts
        {
            get
            {
                HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (_catalog != null && _catalog.SmokeDetectors != null)
                {
                    foreach (SmokeDetectorCatalogRow r in _catalog.SmokeDetectors)
                    {
                        if (r == null) continue;
                        if (!string.IsNullOrWhiteSpace(r.Mount)) set.Add(r.Mount.Trim());
                    }
                }
                return set.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public IReadOnlyList<string> AvailableCeilingSlopes
        {
            get
            {
                HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (_catalog != null && _catalog.SmokeDetectors != null)
                {
                    foreach (SmokeDetectorCatalogRow r in _catalog.SmokeDetectors)
                    {
                        if (r == null) continue;
                        if (!string.IsNullOrWhiteSpace(r.CeilingSlope)) set.Add(r.CeilingSlope.Trim());
                    }
                }
                return set.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public IReadOnlyList<string> AvailableApplianceTypes
        {
            get
            {
                HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (_catalog != null && _catalog.NotificationAppliances != null)
                {
                    foreach (NotificationApplianceCatalogRow r in _catalog.NotificationAppliances)
                    {
                        if (r == null) continue;
                        if (!string.IsNullOrWhiteSpace(r.ApplianceType)) set.Add(r.ApplianceType.Trim());
                    }
                }
                return set.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public IReadOnlyList<string> AvailableCandelas
        {
            get
            {
                HashSet<int> set = new HashSet<int>();
                if (_catalog != null && _catalog.NotificationAppliances != null)
                {
                    foreach (NotificationApplianceCatalogRow r in _catalog.NotificationAppliances)
                    {
                        if (r == null || r.Candela <= 0) continue;
                        set.Add(r.Candela);
                    }
                }
                return set.OrderBy(v => v)
                    .Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .ToList();
            }
        }

        public IReadOnlyList<string> AvailableNotificationDbas
        {
            get
            {
                HashSet<int> set = new HashSet<int>();
                if (_catalog != null && _catalog.NotificationAppliances != null)
                {
                    foreach (NotificationApplianceCatalogRow r in _catalog.NotificationAppliances)
                    {
                        if (r == null || r.NotificationDba <= 0) continue;
                        set.Add(r.NotificationDba);
                    }
                }
                return set.OrderBy(v => v)
                    .Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .ToList();
            }
        }

        public IReadOnlyList<string> GetSprinklerFamilies()
        {
            return _sprinklerFamilies.ToList();
        }

        public IReadOnlyList<string> GetSprinklerTypesForFamily(string familyName)
        {
            if (string.IsNullOrWhiteSpace(familyName)) return new List<string>();
            if (!_sprinklerByFamily.TryGetValue(familyName.Trim(), out List<SprinklerCatalogRow> rows)) return new List<string>();
            return rows
                .Where(r => !string.IsNullOrWhiteSpace(r.TypeName))
                .Select(r => r.TypeName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public IReadOnlyList<string> GetHazardClassesForSprinklerFamily(string familyName)
        {
            if (string.IsNullOrWhiteSpace(familyName)) return new List<string>();
            if (!_sprinklerByFamily.TryGetValue(familyName.Trim(), out List<SprinklerCatalogRow> rows)) return new List<string>();
            return rows
                .Where(r => !string.IsNullOrWhiteSpace(r.HazardClass))
                .Select(r => r.HazardClass.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public string GetHazardClassForSprinkler(string familyName, string typeName)
        {
            if (string.IsNullOrWhiteSpace(familyName) || string.IsNullOrWhiteSpace(typeName)) return null;
            if (!_sprinklerByFamily.TryGetValue(familyName.Trim(), out List<SprinklerCatalogRow> rows)) return null;
            SprinklerCatalogRow match = rows.FirstOrDefault(r =>
                string.Equals(r.TypeName, typeName, StringComparison.OrdinalIgnoreCase));
            if (match == null) return null;
            return string.IsNullOrWhiteSpace(match.HazardClass) ? null : match.HazardClass.Trim();
        }

        public IReadOnlyList<string> GetSmokeDetectorFamilies()
        {
            return _smokeFamilies.ToList();
        }

        public IReadOnlyList<string> GetSmokeDetectorTypesForFamily(string familyName)
        {
            if (string.IsNullOrWhiteSpace(familyName)) return new List<string>();
            if (!_smokeByFamily.TryGetValue(familyName.Trim(), out List<SmokeDetectorCatalogRow> rows)) return new List<string>();
            return rows
                .Where(r => !string.IsNullOrWhiteSpace(r.TypeName))
                .Select(r => r.TypeName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public IReadOnlyList<SmokeDetectorCatalogEntry> GetSmokeDetectorEntriesForFamily(string familyName)
        {
            if (string.IsNullOrWhiteSpace(familyName)) return new List<SmokeDetectorCatalogEntry>();
            if (!_smokeByFamily.TryGetValue(familyName.Trim(), out List<SmokeDetectorCatalogRow> rows)) return new List<SmokeDetectorCatalogEntry>();
            return rows
                .Where(r => !string.IsNullOrWhiteSpace(r.TypeName))
                .Select(r => new SmokeDetectorCatalogEntry
                {
                    FamilyName = r.FamilyName,
                    TypeName = r.TypeName,
                    DetectorType = r.DetectorType,
                    Mount = r.Mount,
                    CeilingSlope = r.CeilingSlope
                })
                .ToList();
        }

        public IReadOnlyList<string> GetNotificationApplianceFamilies()
        {
            return _naFamilies.ToList();
        }

        public IReadOnlyList<string> GetNotificationApplianceTypesForFamily(string familyName)
        {
            if (string.IsNullOrWhiteSpace(familyName)) return new List<string>();
            if (!_naByFamily.TryGetValue(familyName.Trim(), out List<NotificationApplianceCatalogRow> rows)) return new List<string>();
            return rows
                .Where(r => !string.IsNullOrWhiteSpace(r.TypeName))
                .Select(r => r.TypeName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public IReadOnlyList<NotificationApplianceCatalogEntry> GetNotificationAppliancesForFamily(string familyName)
        {
            if (string.IsNullOrWhiteSpace(familyName)) return new List<NotificationApplianceCatalogEntry>();
            if (!_naByFamily.TryGetValue(familyName.Trim(), out List<NotificationApplianceCatalogRow> rows)) return new List<NotificationApplianceCatalogEntry>();
            return rows
                .Where(r => !string.IsNullOrWhiteSpace(r.TypeName))
                .Select(r => new NotificationApplianceCatalogEntry
                {
                    FamilyName = r.FamilyName,
                    TypeName = r.TypeName,
                    ApplianceType = r.ApplianceType,
                    Candela = r.Candela,
                    NotificationDba = r.NotificationDba
                })
                .OrderBy(e => e.ApplianceType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Candela)
                .ToList();
        }
    }
}
