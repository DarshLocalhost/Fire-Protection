using System.Collections.Generic;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Abstraction implemented outside the UI project. Given a batch of user-selected rooms, runs the
    /// device placement calculation and creates device instances in the active Revit host document.
    ///
    /// The UI project must remain Revit-free; this interface is the seam. The concrete implementation
    /// (device backend) is deferred in the UI-first slice; the UI wires this seam and leaves the
    /// placement action disabled until the implementation lands.
    /// </summary>
    public interface IDevicePlacementExecutor
    {
        PlacementRunReport ExecutePlacement(IReadOnlyList<DeviceRoomInputItem> items);
    }
}
