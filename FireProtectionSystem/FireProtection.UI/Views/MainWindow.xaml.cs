using System.Windows;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels;
using FireProtection.UI.ViewModels.Catalog;

namespace FireProtection.UI.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            FitToWorkArea();
            DataContext = new MainWindowViewModel();
        }

        public MainWindow(
            string json,
            IPlacementInputExporter placementInputExporter,
            ISprinklerFamilySource sprinklerFamilySource,
            ISprinklerPlacementService sprinklerPlacementService)
        {
            InitializeComponent();
            FitToWorkArea();
            DataContext = new MainWindowViewModel(json, placementInputExporter, sprinklerFamilySource, sprinklerPlacementService);
        }
        public MainWindow(
            string json,
            IPlacementInputExporter placementInputExporter,
            ISprinklerFamilySource sprinklerFamilySource,
            ISprinklerPlacementService sprinklerPlacementService,
            CatalogViewModel catalog)
        {
            InitializeComponent();
            FitToWorkArea();
            DataContext = new MainWindowViewModel(
                json, placementInputExporter, sprinklerFamilySource, sprinklerPlacementService, catalog);
        }

        /// <summary>
        /// Keeps the 1400x900 design size from overflowing a smaller screen (laptops, scaled displays):
        /// the window opens at most as large as the working area, and never below its MinWidth/MinHeight.
        /// </summary>
        private void FitToWorkArea()
        {
            double availableWidth = SystemParameters.WorkArea.Width;
            double availableHeight = SystemParameters.WorkArea.Height;

            if (availableWidth > 0 && Width > availableWidth)
                Width = System.Math.Max(MinWidth, availableWidth);

            if (availableHeight > 0 && Height > availableHeight)
                Height = System.Math.Max(MinHeight, availableHeight);
        }
    }
}