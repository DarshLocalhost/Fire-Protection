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

        public bool Proceed { get; private set; }

        public MissingFamiliesModal(IEnumerable<MissingEntry> entries)
        {
            InitializeComponent();
            _entries = entries != null ? new List<MissingEntry>(entries) : new List<MissingEntry>();
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
                    "{0} room{1} will be SKIPPED. Proceeding will place sprinklers only in rooms whose family/type is loaded in Revit.",
                    _entries.Count, _entries.Count == 1 ? string.Empty : "s");
            }
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
