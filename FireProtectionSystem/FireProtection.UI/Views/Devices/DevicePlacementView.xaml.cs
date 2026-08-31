using System.Windows.Controls;

namespace FireProtection.UI.Views.Devices
{
    /// <summary>
    /// Shared device-placement UI (level/room selection + family/type + actions). Hosted by each device
    /// tab (smoke detector, notification appliance). Bound to <c>DevicePlacementViewModelBase</c> and its
    /// subclasses; contains no device-specific logic.
    /// </summary>
    public partial class DevicePlacementView : UserControl
    {
        public DevicePlacementView()
        {
            InitializeComponent();
        }
    }
}
