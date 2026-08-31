using System.Collections.Generic;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Placeholder implementation of <see cref="IDevicePlacementExecutor"/> used by the UI-first slice.
    /// The real Revit placement backend for devices is deferred; this seam exists so the UI compiles and
    /// is fully wired, with the placement action disabled (or returning a "pending" report) until the
    /// device backend is implemented.
    /// </summary>
    public class NotImplementedDeviceExecutor : IDevicePlacementExecutor
    {
        public PlacementRunReport ExecutePlacement(IReadOnlyList<DeviceRoomInputItem> items)
        {
            return new PlacementRunReport
            {
                RoomsProcessed = items?.Count ?? 0
            };
        }
    }
}
