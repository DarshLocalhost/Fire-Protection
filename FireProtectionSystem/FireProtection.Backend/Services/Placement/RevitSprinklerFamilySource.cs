using Autodesk.Revit.DB;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Sprinklers.BruteForce;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FireProtection.Backend.Services.Placement
{
    public class RevitSprinklerFamilySource : ISprinklerFamilySource
    {
        private readonly Document _hostDocument;

        public RevitSprinklerFamilySource(Document hostDocument)
        {
            _hostDocument = hostDocument ?? throw new ArgumentNullException(nameof(hostDocument));
        }

        public IReadOnlyList<SprinklerFamilyOption> GetAvailableFamilies()
        {
            List<SprinklerFamilyOption> result = new List<SprinklerFamilyOption>();

            if (_hostDocument == null) return result;

            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(_hostDocument)
                    .OfCategory(BuiltInCategory.OST_Sprinklers)
                    .OfClass(typeof(FamilySymbol));

                Dictionary<string, List<string>> familyMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                foreach (Element element in collector)
                {
                    if (element is FamilySymbol symbol)
                    {
                        string familyName = symbol.FamilyName ?? symbol.Family?.Name;
                        string typeName = symbol.Name;

                        if (string.IsNullOrWhiteSpace(familyName)) continue;

                        if (!familyMap.ContainsKey(familyName))
                        {
                            familyMap[familyName] = new List<string>();
                        }

                        if (!string.IsNullOrWhiteSpace(typeName) && !familyMap[familyName].Contains(typeName))
                        {
                            familyMap[familyName].Add(typeName);
                        }
                    }
                }

                foreach (KeyValuePair<string, List<string>> kvp in familyMap.OrderBy(k => k.Key))
                {
                    List<SprinklerTypeOption> typeOptions = kvp.Value
                        .OrderBy(t => t)
                        .Select(t => new SprinklerTypeOption
                        {
                            FamilyName = kvp.Key,
                            TypeName = t
                        })
                        .ToList();

                    result.Add(new SprinklerFamilyOption
                    {
                        FamilyName = kvp.Key,
                        Types = typeOptions
                    });
                }
            }
            catch
            {
                // Fallback to empty list if symbol collection fails
            }

            return result;
        }
    }
}