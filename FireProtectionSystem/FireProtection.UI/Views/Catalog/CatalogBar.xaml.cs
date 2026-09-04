using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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

            if (!string.IsNullOrEmpty(ViewModel.SourcePath))
            {
                string dir = Path.GetDirectoryName(ViewModel.SourcePath);
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
