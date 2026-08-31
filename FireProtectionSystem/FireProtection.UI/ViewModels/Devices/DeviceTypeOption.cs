namespace FireProtection.UI.ViewModels.Devices
{
    /// <summary>
    /// Revit-free UI representation of a single device type. Device-agnostic counterpart of
    /// <c>SprinklerTypeOption</c>.
    /// </summary>
    public class DeviceTypeOption
    {
        public string FamilyName { get; set; }
        public string TypeName { get; set; }

        public override string ToString()
        {
            return TypeName ?? string.Empty;
        }
    }
}
