using FireProtection.UI.ViewModels;
using FireProtection.UI.Views;

namespace FireProtection.UI.Services
{
    public static class UiLauncher
    {
        public static void Show(string json)
        {
            MainWindow window = new MainWindow(json);
            window.ShowDialog();
        }

        public static void Show(string json, IPlacementExecutor placementExecutor)
        {
            MainWindow window = new MainWindow(json, placementExecutor);
            window.ShowDialog();
        }

        public static void Show(
            string json,
            IPlacementExecutor placementExecutor,
            ISprinklerFamilySource sprinklerFamilySource)
        {
            MainWindow window = new MainWindow(json, placementExecutor, sprinklerFamilySource);
            window.ShowDialog();
        }
    }
}