using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FireProtection.Backend.Services.Catalog
{
    /// <summary>
    /// Reads the families that are actually LOADED in the open document, which is what a placement
    /// run can create. Enumerates <see cref="FamilySymbol"/> rather than
    /// <see cref="FamilyInstance"/>: a loaded family can be placed even when no instance of it
    /// exists yet, and listing only instantiated families would hide every family the user is
    /// about to place for the first time.
    /// </summary>
    public static class ModelFamilyEnumerator
    {
        /// <summary>Sprinkler family types loaded in the document (OST_Sprinklers).</summary>
        public static IReadOnlyList<ModelFamilyType> EnumerateSprinklerTypes(Document document)
        {
            return Enumerate(document, BuiltInCategory.OST_Sprinklers);
        }

        /// <summary>
        /// Enumerates the document and loads both category lists into a <see cref="ModelBackedCatalog"/>.
        ///
        /// This bridge lives here, in the Revit-aware file, rather than on the catalog itself:
        /// <see cref="ModelBackedCatalog"/> must contain NO reference to <c>Autodesk.Revit.DB</c> so the
        /// Revit-free test harness can construct and exercise it without RevitAPI.dll.
        /// </summary>
        public static void LoadInto(ModelBackedCatalog catalog, Document document)
        {
            if (catalog == null) return;
            catalog.LoadFrom(EnumerateSprinklerTypes(document), EnumerateFireAlarmTypes(document));
        }

        /// <summary>
        /// Fire-alarm device family types loaded in the document (OST_FireAlarmDevices).
        ///
        /// NOTE: smoke detectors and notification appliances share this single Revit category, so
        /// this list is the SAME for both. That is deliberate — the product decision is that the
        /// Smoke Detector and Notification Appliance tabs present one shared device list rather
        /// than attempting to split families by name, which would misclassify anything the
        /// keyword rules did not anticipate.
        /// </summary>
        public static IReadOnlyList<ModelFamilyType> EnumerateFireAlarmTypes(Document document)
        {
            return Enumerate(document, BuiltInCategory.OST_FireAlarmDevices);
        }

        private static IReadOnlyList<ModelFamilyType> Enumerate(Document document, BuiltInCategory category)
        {
            var results = new List<ModelFamilyType>();
            if (document == null) return results;

            FilteredElementCollector collector;
            try
            {
                collector = new FilteredElementCollector(document)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(category);
            }
            catch (Exception ex)
            {
                // Never let a listing failure take down the add-in. The caller treats an empty
                // list as "no families from the model" and the UI shows its own message.
                FireProtection.UI.Services.FireProtectionLog.Warn(
                    "Model family enumeration failed for " + category + ": " + ex.Message);
                return results;
            }

            try
            {
                foreach (Element element in collector)
                {
                    FamilySymbol symbol = element as FamilySymbol;
                    if (symbol == null) continue;

                    string familyName = symbol.FamilyName;
                    if (string.IsNullOrWhiteSpace(familyName))
                    {
                        Family fam = symbol.Family;
                        if (fam != null) familyName = fam.Name;
                    }
                    if (string.IsNullOrWhiteSpace(familyName)) continue;
                    if (string.IsNullOrWhiteSpace(symbol.Name)) continue;

                    string placementType = null;
                    try
                    {
                        // FamilyPlacementType is a plain (non-nullable) enum, so the only guard
                        // needed is a null Family. Mirrors RevitSprinklerFamilySource.
                        Family fam = symbol.Family;
                        if (fam != null) placementType = fam.FamilyPlacementType.ToString();
                    }
                    catch
                    {
                        // Not every family exposes this; absence simply means "unknown".
                        placementType = null;
                    }

                    results.Add(new ModelFamilyType
                    {
                        FamilyName = familyName.Trim(),
                        TypeName = symbol.Name.Trim(),
                        FamilyPlacementType = placementType
                    });
                }
            }
            catch (Exception ex)
            {
                FireProtection.UI.Services.FireProtectionLog.Warn(
                    "Model family enumeration failed while reading " + category + ": " + ex.Message);
            }

            // Stable, deterministic order so the dropdown does not reshuffle between reads.
            return results
                .OrderBy(f => f.FamilyName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}