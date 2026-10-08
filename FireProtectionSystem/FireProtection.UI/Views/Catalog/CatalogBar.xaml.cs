using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Catalog;
using Microsoft.Win32;

namespace FireProtection.UI.Views.Catalog
{
    public partial class CatalogBar : UserControl
    {
        public CatalogBar()
        {
            InitializeComponent();
        }

        public CatalogViewModel ViewModel
        {
            get { return (CatalogViewModel)DataContext; }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            OpenFileDialog dlg = new OpenFileDialog
            {
                Title = "Select catalog workbook",
                Filter = "Excel workbooks (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            // Start the dialog where the last-used workbook lives, falling back to the remembered
            // path when the current catalog is not the file (model mode shows no SourcePath).
            string startingFrom = !string.IsNullOrEmpty(ViewModel.SourcePath)
                ? ViewModel.SourcePath
                : ViewModel.RememberedCatalogPath;
            if (!string.IsNullOrEmpty(startingFrom))
            {
                string dir = Path.GetDirectoryName(startingFrom);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    dlg.InitialDirectory = dir;
                }
            }

            bool? ok = dlg.ShowDialog();
            if (ok != true) return;

            string error;
            if (!ViewModel.TryLoad(dlg.FileName, out error))
            {
                MessageBox.Show(
                    "Failed to load catalog:\n\n" + (error ?? "<unknown error>"),
                    "Catalog",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Raised when the user picks the other source. The radio IsChecked binding updates the
        /// enum but cannot itself rebuild the catalog (a converter must not run I/O), so the
        /// viewmodel is told here to swap the active ICatalog and raise its change notifications,
        /// which is what the three tabs listen to in order to repopulate their dropdowns.
        /// </summary>
        private void SourceRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            RadioButton radio = sender as RadioButton;
            if (radio == null || radio.IsChecked != true) return;

            // During x:aml load no DataContext exists yet; the constructor already applied the
            // persisted mode, so there is nothing to do.
            CatalogSourceMode requested = CatalogSourceModes.Parse(radio.Tag as string);
            if (requested == ViewModel.SourceMode) return;

            string error;
            if (!ViewModel.TrySetSourceMode(requested, out error))
            {
                MessageBox.Show(
                    "Could not switch source:\n\n" + (error ?? "<unknown error>"),
                    "Catalog",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            string error;
            if (!ViewModel.TryReload(out error))
            {
                MessageBox.Show(
                    "Failed to reload catalog:\n\n" + (error ?? "<unknown error>"),
                    "Catalog",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
