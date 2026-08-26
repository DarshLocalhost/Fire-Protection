using System.Windows;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels;

namespace FireProtection.UI.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();
        }

        public MainWindow(
            string json,
            IPlacementInputExporter placementInputExporter,
            ISprinklerFamilySource sprinklerFamilySource,
            ISprinklerPlacementService sprinklerPlacementService)
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel(json, placementInputExporter, sprinklerFamilySource, sprinklerPlacementService);
        }
    }
}