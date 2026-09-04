using System.Windows;
using System.Windows.Controls;
using FireProtection.UI.ViewModels.Sprinklers.BruteForce;

namespace FireProtection.UI.Views.Sprinklers.BruteForce
{
    public partial class SprinklerBruteForceView : UserControl
    {
        public SprinklerBruteForceView()
        {
            InitializeComponent();
        }

        private void RoomResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is RoomItemViewModel room)
            {
                room.ResetFamilyAndTypeToDefault();
                room.ResetSpacingOverridesToDefault();
                room.ResetHazardClassToDefault();
            }
        }
    }
}
