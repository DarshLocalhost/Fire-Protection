using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SPA.SynthFire.Revit;

/// <summary>Shows (and focuses) the dockable SPA SynthFire panel.</summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class SynthFirePanelCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            DockablePane pane = commandData.Application.GetDockablePane(SynthFireDockablePane.PaneId);
            pane.Show();
            return Result.Succeeded;
        }
        catch (System.Exception ex)
        {
            message = "The SPA SynthFire panel is unavailable: " + ex.Message;
            return Result.Failed;
        }
    }
}
