using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using FireProtection.UI.Services;
using Microsoft.Win32;

namespace FireProtection.UI.Views.Common
{
    public partial class MissingFamiliesModal : Window
    {
        public class MissingEntry
        {
            public string RoomName { get; set; }
            public string FamilyName { get; set; }
            public string TypeName { get; set; }
        }

        private readonly List<MissingEntry> _entries;
        private readonly Func<string, string> _loadFamily;

        public bool Proceed { get; private set; }

        public MissingFamiliesModal(IEnumerable<MissingEntry> entries, Func<string, string> loadFamily = null)
        {
            InitializeComponent();
            _entries = entries != null ? new List<MissingEntry>(entries) : new List<MissingEntry>();
            _loadFamily = loadFamily;
            MissingList.ItemsSource = _entries;
            UpdateSummary();
        }

        public static bool ShowDialog(Window owner, IEnumerable<MissingEntry> entries)
        {
            MissingFamiliesModal dlg = new MissingFamiliesModal(entries);
            DialogOwner.Apply(dlg, owner);
            bool? result = dlg.ShowDialog();
            return result == true && dlg.Proceed;
        }

        public static bool ShowDialog(Window owner, IEnumerable<MissingEntry> entries, Func<string, string> loadFamily)
        {
            MissingFamiliesModal dlg = new MissingFamiliesModal(entries, loadFamily);
            DialogOwner.Apply(dlg, owner);
            bool? result = dlg.ShowDialog();
            return result == true && dlg.Proceed;
        }

        private void UpdateSummary()
        {
            if (_entries.Count == 0)
            {
                HeaderText.Text = "No families or types are missing in the current Revit model.";
                SummaryText.Text = "Click Proceed with available to continue.";
            }
            else
            {
                SummaryText.Text = string.Format(CultureInfo.InvariantCulture,
                    "{0} room{1} reference a family/type that is not loaded. Load the .rfa, then proceed; unresolved rows will fail with a report.",
                    _entries.Count, _entries.Count == 1 ? string.Empty : "s");
            }
        }

        private void LoadFamilyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_loadFamily == null) return;

            OpenFileDialog dlg = new OpenFileDialog
            {
                Title = "Select Revit family to load",
                Filter = "Revit families (*.rfa)|*.rfa|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dlg.ShowDialog(this) != true) return;

            string error = _loadFamily(dlg.FileName);
            MessageBox.Show(this,
                error == null ? "Family loaded into the active model. Select Proceed to continue placement." : "Family could not be loaded:\n\n" + error,
                "Load family",
                MessageBoxButton.OK,
                error == null ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private void ProceedButton_Click(object sender, RoutedEventArgs e)
        {
            Proceed = true;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Proceed = false;
            DialogResult = false;
            Close();
        }

        private void ExportCsvButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dlg = new SaveFileDialog
            {
                Title = "Export missing family list",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = "MissingFamilies_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv",
                AddExtension = true
            };

            bool? ok = dlg.ShowDialog();
            if (ok != true) return;

            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("RoomName,FamilyName,TypeName");
                foreach (MissingEntry entry in _entries)
                {
                    if (entry == null) continue;
                    sb.AppendLine(CsvEscape(entry.RoomName) + "," + CsvEscape(entry.FamilyName) + "," + CsvEscape(entry.TypeName));
                }
                File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                MessageBox.Show(this,
                    "Missing family list exported to:\n" + dlg.FileName,
                    "Export missing list",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Failed to export missing list:\n\n" + ex.Message,
                    "Export missing list",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private static string CsvEscape(string s)
        {
            if (s == null) return string.Empty;
            if (s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
