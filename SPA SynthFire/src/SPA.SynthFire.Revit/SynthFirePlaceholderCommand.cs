using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SPA.SynthFire.Revit;

/// <summary>
/// Phase 0 placeholder command. Confirms the ribbon button is wired and the
/// add-in is live inside Revit. Replaced by real placement commands in Phase 2.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class SynthFirePlaceholderCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        TaskDialog.Show(
            "SPA SynthFire",
            "SPA SynthFire add-in loaded successfully.\n\n" +
            "This is the Phase 0 stub. Sprinkler placement, detection, clash " +
            "avoidance and the estimator arrive in later phases.");

        return Result.Succeeded;
    }
}
