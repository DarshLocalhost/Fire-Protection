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
        /// <summary>
        /// Runs the device placement for the given rooms.
        /// </summary>
        /// <param name="progress">
        /// Optional progress + cancellation channel. Implementations report once per room and must honour
        /// cancellation at room boundaries only, rolling the whole run back so a cancelled run creates nothing.
        /// </param>
        /// <param name="existingDevicePolicy">
        /// What to do with rooms that already contain devices of this kind, i.e. what a second Place does.
        /// </param>
        PlacementRunReport ExecutePlacement(
            IReadOnlyList<DeviceRoomInputItem> items,
            IPlacementProgress progress = null,
            ExistingDevicePolicy existingDevicePolicy = ExistingDevicePolicy.SkipRoom);
    }
}
