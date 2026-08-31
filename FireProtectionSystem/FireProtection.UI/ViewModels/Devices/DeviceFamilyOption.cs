using System.Collections.Generic;

namespace FireProtection.UI.ViewModels.Devices
{
    /// <summary>
    /// Revit-free UI representation of a device family (sprinkler / smoke detector / notification
    /// appliance) and its available types. Device-agnostic counterpart of
    /// <c>SprinklerFamilyOption</c>; shared by every device placement tab.
    /// </summary>
    public class DeviceFamilyOption
    {
        public string FamilyName { get; set; }

        public IReadOnlyList<DeviceTypeOption> Types { get; set; }

        public DeviceFamilyOption()
        {
            Types = new List<DeviceTypeOption>();
        }

        public override string ToString()
        {
            return FamilyName ?? string.Empty;
        }
    }
}
