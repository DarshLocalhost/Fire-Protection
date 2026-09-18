using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Devices;

namespace FireProtection.Backend.Services.Placement.NotificationAppliances
{
    /// <summary>
    /// Revit-aware family source for notification appliances. Notification appliances share the
    /// <c>OST_FireAlarmDevices</c> category with smoke detectors, so family enumeration is identical; the only
    /// difference is the placement-behavior fallback. The notification catalog has no Mount column, so when a
    /// family's <c>FamilyPlacementType</c> does not itself dictate the behavior the fallback is ceiling
    /// (provisional) rather than a catalog-driven wall hint.
    /// </summary>
    public class RevitNotificationApplianceFamilySource : IDeviceFamilySource
    {
        private readonly Document _document;

        public RevitNotificationApplianceFamilySource(Document document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
        }

        public IReadOnlyList<DeviceFamilyOption> GetAvailableFamilies()
        {
            var collector = new FilteredElementCollector(_document)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_FireAlarmDevices)
                .Cast<FamilySymbol>();

            var results = new List<DeviceFamilyOption>();
            foreach (var group in collector.GroupBy(s => s.FamilyName, StringComparer.OrdinalIgnoreCase))
            {
                results.Add(new DeviceFamilyOption
                {
                    FamilyName = group.Key,
                    Types = group.Select(s => new DeviceTypeOption
                    {
                        FamilyName = group.Key,
                        TypeName = s.Name
                    }).OrderBy(t => t.TypeName, StringComparer.OrdinalIgnoreCase).ToList()
                });
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
            if (string.IsNullOrWhiteSpace(familyName) && string.IsNullOrWhiteSpace(typeName))
                return DevicePlacementBehavior.Unknown;

            FamilySymbol symbol = FindFamilySymbol(familyName, typeName);
            if (symbol == null || symbol.Family == null) return DevicePlacementBehavior.Unknown;

            // Check both family name AND type name for wall-mount keywords. Many manufacturer
            // wall-strobe families omit "wall" from the family name but include it in the type
            // (e.g. family "SystemSensor_H12SH", type "Wall Strobe 75cd").
            string combined = (familyName ?? string.Empty) + " " + (typeName ?? string.Empty);
            if (combined.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0)
                return DevicePlacementBehavior.WallSidewall;

            // Inspect the family's built-in FamilyPlacementType. FaceBased families whose name
            // does not contain "wall" are still ceiling-hosted; OneLevelBased families that carry
            // a "Mount" parameter set to "Wall" are wall-mounted.
            switch (symbol.Family.FamilyPlacementType)
            {
                case FamilyPlacementType.OneLevelBased:
                    // Check for a "Mount" or "Mounting" parameter that says "Wall".
                    if (HasWallMountParameter(symbol))
                        return DevicePlacementBehavior.WallSidewall;
                    return DevicePlacementBehavior.LevelHosted;

                case FamilyPlacementType.WorkPlaneBased:
                    if (HasWallMountParameter(symbol))
                        return DevicePlacementBehavior.WallSidewall;
                    return DevicePlacementBehavior.WorkPlaneDependent;

                case FamilyPlacementType.ViewBased:
                case FamilyPlacementType.CurveBasedDetail:
                    return DevicePlacementBehavior.Unsupported;

                default:
                    // No Mount column in the notification catalog: default to ceiling (provisional).
                    return DevicePlacementBehavior.CeilingOverhead;
            }
        }

        /// <summary>
        /// Checks whether the family symbol carries a "Mount" or "Mounting" type/instance parameter
        /// whose value contains "wall". This catches wall-strobe families that lack the keyword in
        /// their name but declare their mounting orientation as a parameter.
        /// </summary>
        private static bool HasWallMountParameter(FamilySymbol symbol)
        {
            if (symbol == null) return false;
            try
            {
                foreach (Parameter p in symbol.Parameters)
                {
                    if (p == null || p.Definition == null) continue;
                    string name = p.Definition.Name;
                    if (string.Equals(name, "Mount", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, "Mounting", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, "Mount Type", StringComparison.OrdinalIgnoreCase))
                    {
                        string val = p.AsString();
                        if (!string.IsNullOrWhiteSpace(val)
                            && val.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return true;
                        }
                    }
                }
            }
            catch { /* parameter read failure -> assume ceiling */ }
            return false;
        }

        private FamilySymbol FindFamilySymbol(string familyName, string typeName)
        {
            return new FilteredElementCollector(_document)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_FireAlarmDevices)
                .Cast<FamilySymbol>()
                .FirstOrDefault(s =>
                    string.Equals(s.FamilyName, familyName, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(typeName) || string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
