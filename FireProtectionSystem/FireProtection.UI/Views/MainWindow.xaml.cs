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

        public MainWindow(string json)
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel(json);
        }

        public MainWindow(string json, IPlacementExecutor placementExecutor)
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel(json, placementExecutor);
        }

        public MainWindow(
            string json,
            IPlacementExecutor placementExecutor,
            ISprinklerFamilySource sprinklerFamilySource)
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel(json, placementExecutor, sprinklerFamilySource);
        }
    }
}