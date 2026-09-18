using System.Collections.Generic;
using FireProtection.UI.ViewModels.Devices;

namespace FireProtection.UI.Services
{
    public interface IDeviceFamilySource
    {
        IReadOnlyList<DeviceFamilyOption> GetAvailableFamilies();

        /// <summary>Loads a selected .rfa into the active model and returns null on success.</summary>
        bool TryLoadFamily(string familyFilePath, out string error);
    }
}