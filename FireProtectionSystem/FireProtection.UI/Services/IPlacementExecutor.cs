using System.Collections.Generic;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Abstraction implemented outside the UI project. Given a batch of
    /// user-selected rooms, runs the placement calculation and creates
    /// sprinkler instances in the active Revit host document.
    ///
    /// The UI project must remain Revit-free; this interface is the seam.
    /// </summary>
    public interface IPlacementExecutor
    {
        PlacementRunReport ExecutePlacement(IReadOnlyList<PlacementRequestItem> items);
    }
}