using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using SPA.SynthFire.UI;

namespace SPA.SynthFire.Revit;

/// <summary>
/// Lists fire-alarm / detector family types loaded in the model (smoke, heat and
/// notification devices all sit under the Fire Alarm Devices category) so the user
/// can pick which to place.
/// </summary>
public static class DetectorFamilyReader
{
    public static readonly BuiltInCategory Category = BuiltInCategory.OST_FireAlarmDevices;

    public static IReadOnlyList<FamilyTypeOption> ReadDetectorTypes(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfCategory(Category)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .OrderBy(s => s.FamilyName)
            .ThenBy(s => s.Name)
            .Select(s => new FamilyTypeOption(s.Id.Value.ToString(), $"{s.FamilyName} : {s.Name}")
            {
                Mount = FireAlarmMountResolver.Resolve(s).ToDeviceMount()
            })
            .ToList();
    }
}
