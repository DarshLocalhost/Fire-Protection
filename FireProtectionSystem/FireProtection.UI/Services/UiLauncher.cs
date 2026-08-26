using FireProtection.UI.Views;

namespace FireProtection.UI.Services
{
    public static class UiLauncher
    {
        public static void Show(
            string json,
            IPlacementInputExporter placementInputExporter,
            ISprinklerFamilySource sprinklerFamilySource,
            ISprinklerPlacementService sprinklerPlacementService)
        {
            MainWindow window = new MainWindow(json, placementInputExporter, sprinklerFamilySource, sprinklerPlacementService);
            window.ShowDialog();
        }
    }
}