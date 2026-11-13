using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using SPA.SynthFire.UI;

namespace SPA.SynthFire.Revit;

/// <summary>
/// Lists the sprinkler family types loaded in the model so the user can pick
/// which one to place (Tandm lets you choose the family/type). Ids are the
/// FamilySymbol ElementId as a string; the placer resolves them back.
/// </summary>
public static class SprinklerFamilyReader
{
    public static IReadOnlyList<FamilyTypeOption> ReadSprinklerTypes(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Sprinklers)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .OrderBy(s => s.FamilyName)
            .ThenBy(s => s.Name)
            .Select(s => new FamilyTypeOption(
                Id: s.Id.Value.ToString(),
                Label: $"{s.FamilyName} : {s.Name}")
            {
                Mount = SprinklerMountResolver.Resolve(s).ToDeviceMount()
            })
            .ToList();
    }
}
