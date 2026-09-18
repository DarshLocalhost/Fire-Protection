using System.Collections.Generic;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Seam for device (smoke detector / notification appliance) placement.
    /// Implemented by the Revit-aware executors in the Backend; the UI depends only on this interface.
    /// Signature matches sprinkler-style progress + existing-device policy contracts in this folder.
    /// </summary>
    public interface IDevicePlacementExecutor
    {
        PlacementRunReport ExecutePlacement(
            IReadOnlyList<DeviceRoomInputItem> items,
            IPlacementProgress progress = null,
            ExistingDevicePolicy existingDevicePolicy = ExistingDevicePolicy.SkipRoom);

        /// <summary>
        /// Read-only pre-placement eligibility check for a batch of rooms, keyed by <see cref="DeviceRoomInputItem.RoomId"/>.
        /// Mirrors the sprinkler service's <c>EvaluateRoomEligibility</c>: it reuses the SAME calculation, family/level
        /// resolution and ceiling-host logic the real run uses, but creates no elements and opens no transaction, so a
        /// room can be shown ELIGIBLE / BLOCKED / UNDETERMINED in the grid BEFORE the user places anything. Rooms whose
        /// eligibility cannot be decided (e.g. the calculation could not run) are UNDETERMINED, never falsely blocked.
        /// </summary>
        IReadOnlyDictionary<string, PlacementEligibilityResult> EvaluateEligibility(
            IReadOnlyList<DeviceRoomInputItem> items);
    }
}