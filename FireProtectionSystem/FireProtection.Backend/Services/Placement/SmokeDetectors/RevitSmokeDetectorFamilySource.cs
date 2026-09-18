using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Devices;

namespace FireProtection.Backend.Services.Placement.SmokeDetectors
{
    public class RevitSmokeDetectorFamilySource : IDeviceFamilySource
    {
        private readonly Document _document;
        private readonly Func<ICatalog> _catalogAccessor;

        /// <param name="catalogAccessor">Lazy accessor for the CURRENT catalog. The family source is built
        /// before the user picks a workbook, so it must read the catalog on every call (mirrors the
        /// sprinkler <c>RevitSprinklerFamilySource</c> resolver). Null =&gt; no catalog-driven Mount hint.</param>
        public RevitSmokeDetectorFamilySource(Document document, Func<ICatalog> catalogAccessor = null)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _catalogAccessor = catalogAccessor;
        }

        public IReadOnlyList<DeviceFamilyOption> GetAvailableFamilies()
        {
            var results = new List<DeviceFamilyOption>();

            var collector = new FilteredElementCollector(_document)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_FireAlarmDevices)
                .Cast<FamilySymbol>();

            var familiesGroup = collector.GroupBy(s => s.FamilyName, StringComparer.OrdinalIgnoreCase);

            foreach (var group in familiesGroup)
            {
                var option = new DeviceFamilyOption
                {
                    FamilyName = group.Key,
                    Types = group.Select(s => new DeviceTypeOption
                    {
                        FamilyName = group.Key,
                        TypeName = s.Name
                    }).OrderBy(t => t.TypeName, StringComparer.OrdinalIgnoreCase).ToList()
                };

                results.Add(option);
            }

            return results.OrderBy(f => f.FamilyName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public bool TryLoadFamily(string familyFilePath, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(familyFilePath)) { error = "No family file was selected."; return false; }
            try
            {
                Family family;
                if (!_document.LoadFamily(familyFilePath, out family))
                {
                    error = "Revit did not load the family file.";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public DevicePlacementBehavior GetPlacementBehavior(string familyName, string typeName)
        {
            if (string.IsNullOrWhiteSpace(familyName)) return DevicePlacementBehavior.Unknown;

            FamilySymbol symbol = FindFamilySymbol(familyName, typeName);
            if (symbol == null || symbol.Family == null) return DevicePlacementBehavior.Unknown;

            Family family = symbol.Family;
            FamilyPlacementType placementType = family.FamilyPlacementType;

            ICatalog catalog = _catalogAccessor?.Invoke();
            if (catalog != null)
            {
                var entries = catalog.GetSmokeDetectorEntriesForFamily(familyName);
                var entry = entries?.FirstOrDefault(e => string.Equals(e.TypeName, typeName, StringComparison.OrdinalIgnoreCase));
                if (entry != null && string.Equals(entry.Mount, "Wall", StringComparison.OrdinalIgnoreCase))
                    return DevicePlacementBehavior.WallSidewall;
            }

            switch (placementType)
            {
                case FamilyPlacementType.OneLevelBased:
                    return DevicePlacementBehavior.LevelHosted;

                case FamilyPlacementType.WorkPlaneBased:
                    return DevicePlacementBehavior.WorkPlaneDependent;

                case FamilyPlacementType.ViewBased:
                case FamilyPlacementType.CurveBasedDetail:
                    return DevicePlacementBehavior.Unsupported;

                default:
                    return DevicePlacementBehavior.CeilingOverhead;
            }
        }

        private FamilySymbol FindFamilySymbol(string familyName, string typeName)
        {
            var collector = new FilteredElementCollector(_document)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_FireAlarmDevices)
                .Cast<FamilySymbol>();

            return collector.FirstOrDefault(s =>
                string.Equals(s.FamilyName, familyName, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(typeName) || string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase)));
        }
    }
}