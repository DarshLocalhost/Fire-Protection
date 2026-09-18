using System.Windows;

namespace FireProtection.UI.Views.Common
{
    public partial class PlacementResultReportWindow : Window
    {
        public PlacementResultReportWindow(PlacementResultReportView reportView)
        {
            InitializeComponent();
            Content = reportView;
            Title = "Placement Report";
            Width = 1000;
            Height = 700;
            MinWidth = 800;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }
}
