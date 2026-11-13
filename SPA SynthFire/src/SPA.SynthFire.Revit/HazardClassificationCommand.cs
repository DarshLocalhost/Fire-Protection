using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SPA.SynthFire.Revit;

/// <summary>Opens the modeless Hazard Classification window (via <see cref="SynthFireLauncher"/>).</summary>
[Transaction(TransactionMode.Manual)]
public sealed class HazardClassificationCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => SynthFireLauncher.LaunchHazard(commandData.Application, ref message);
}
