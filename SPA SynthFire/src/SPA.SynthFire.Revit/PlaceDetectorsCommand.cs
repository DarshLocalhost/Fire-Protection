using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SPA.SynthFire.Revit;

/// <summary>
/// Opens the modeless Fire Detection window (via <see cref="SynthFireLauncher"/>, shared
/// with the dockable panel). Placement runs through an ExternalEvent
/// (<see cref="DetectorPlacementRunner"/>).
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class PlaceDetectorsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => SynthFireLauncher.LaunchDetectors(commandData.Application, ref message);
}
