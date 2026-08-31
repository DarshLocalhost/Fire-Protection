using System.Collections.Generic;
using FireProtection.UI.ViewModels.Devices;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// UI-side abstraction for obtaining loaded device families/types (sprinkler / smoke detector /
    /// notification appliance) without leaking Revit API types into the UI project. Device-agnostic
    /// counterpart of <c>ISprinklerFamilySource</c>.
    /// </summary>
    public interface IDeviceFamilySource
    {
        IReadOnlyList<DeviceFamilyOption> GetAvailableFamilies();
    }
}
