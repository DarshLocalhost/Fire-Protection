using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SPA.SynthFire.Revit;

/// <summary>Opens the modeless Room Creation window (via <see cref="SynthFireLauncher"/>).</summary>
[Transaction(TransactionMode.Manual)]
public sealed class RoomCreationCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => SynthFireLauncher.LaunchRoomCreation(commandData.Application, ref message);
}
