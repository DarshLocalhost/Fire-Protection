using Autodesk.Revit.DB;
using FireProtection.UI.ViewModels.Sprinklers.BruteForce;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FireProtection.Backend.Services.Placement
{
    /// <summary>
    /// Resolves sprinkler FamilySymbols from the current host document.
    /// The placement path must resolve the exact user-selected family/type.
    /// No silent fallback is allowed.
    /// </summary>
    public class SprinklerFamilyResolver
    {
        public FamilySymbol Resolve(
            Document hostDocument,
            string familyName,
            string typeName)
        {
            if (hostDocument == null) throw new ArgumentNullException(nameof(hostDocument));
            if (string.IsNullOrWhiteSpace(familyName)) return null;
            if (string.IsNullOrWhiteSpace(typeName)) return null;

            FilteredElementCollector collector = new FilteredElementCollector(hostDocument)
                .OfCategory(BuiltInCategory.OST_Sprinklers)
                .OfClass(typeof(FamilySymbol));

            foreach (Element element in collector)
            {
                FamilySymbol symbol = element as FamilySymbol;
                if (symbol == null) continue;

                string candidateFamilyName = symbol.Family != null ? symbol.Family.Name : null;
                string candidateTypeName = symbol.Name;

                if (string.Equals(candidateFamilyName, familyName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidateTypeName, typeName, StringComparison.OrdinalIgnoreCase))
                {
                    return symbol;
                }
            }

            return null;
        }

        public IReadOnlyList<SprinklerFamilyOption> GetAvailableFamilies(Document hostDocument)
        {
            if (hostDocument == null) throw new ArgumentNullException(nameof(hostDocument));

            List<FamilySymbol> symbols = new List<FamilySymbol>();

            FilteredElementCollector collector = new FilteredElementCollector(hostDocument)
                .OfCategory(BuiltInCategory.OST_Sprinklers)
                .OfClass(typeof(FamilySymbol));

            foreach (Element element in collector)
            {
                FamilySymbol symbol = element as FamilySymbol;
                if (symbol != null)
                {
                    symbols.Add(symbol);
                }
            }

            List<SprinklerFamilyOption> result = symbols
                .GroupBy(s => s.Family != null ? s.Family.Name : string.Empty, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new SprinklerFamilyOption
                {
                    FamilyName = g.Key,
                    Types = g
                        .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(s => new SprinklerTypeOption
                        {
                            FamilyName = g.Key,
                            TypeName = s.Name
                        })
                        .ToList()
                })
                .ToList();

            return result;
        }
    }
}