using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SPA.SynthFire.Revit;

/// <summary>Opens the SPA SynthFire settings window modelessly (via <see cref="SynthFireLauncher"/>).</summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class SettingsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => SynthFireLauncher.LaunchSettings(commandData.Application);
}
