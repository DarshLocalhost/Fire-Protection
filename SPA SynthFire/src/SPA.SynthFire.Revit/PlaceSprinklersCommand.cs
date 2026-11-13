using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SPA.SynthFire.Revit;

/// <summary>
/// Opens the modeless Sprinkler Design window (via <see cref="SynthFireLauncher"/>, so
/// the ribbon and the dockable panel share one window instance). Placement runs through
/// an ExternalEvent (<see cref="SprinklerPlacementRunner"/>).
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class PlaceSprinklersCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => SynthFireLauncher.LaunchSprinklers(commandData.Application, ref message);
}
