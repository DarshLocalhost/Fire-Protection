using System;
using System.Windows;
using System.Windows.Controls;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Devices;

namespace FireProtection.UI.Views.Devices
{
    public partial class DevicePlacementView : UserControl
    {
        public DevicePlacementView()
        {
            InitializeComponent();
        }

        private void LevelSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DeviceLevelItemViewModel level)
            {
                if (DataContext is DevicePlacementViewModelBase vm)
                {
                    // This runs inside Revit: an exception escaping a click handler takes Revit down with it.
                    try
                    {
                        vm.OpenLevelSettings(level);
                    }
                    catch (Exception ex)
                    {
                        FireProtectionLog.Error("Level settings dialog failed for level '" + level.Name + "'.", ex);
                        Dialogs.Show("Could not open the level settings dialog.\n\n" + ex.Message, "Level Settings");
                    }
                }
            }
        }
    }
}
