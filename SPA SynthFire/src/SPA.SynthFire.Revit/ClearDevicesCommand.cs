using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SPA.SynthFire.Revit;

/// <summary>
/// Removes every device SPA SynthFire placed — sprinklers, detectors and notification
/// appliances — identified by the "SPA SynthFire" tag in each instance's Comments.
/// Prompts for confirmation first (via <see cref="SynthFireLauncher"/>, shared with the panel).
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class ClearDevicesCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => SynthFireLauncher.ClearDevices(commandData.Application, ref message);
}
