using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using FireProtection.UI.Services;

namespace FireProtection.UI.Views.Common
{
    public partial class LevelSettingsPopover : Window
    {
        public string Field1KeyValue { get; set; }
        public string Field1LabelText { get; set; }
        public string Field1InitialValue { get; set; }
        public IReadOnlyList<string> Field1Options { get; set; }

        public string Field2KeyValue { get; set; }
        public string Field2LabelText { get; set; }
        public string Field2InitialValue { get; set; }
        public IReadOnlyList<string> Field2Options { get; set; }

        public string Field3KeyValue { get; set; }
        public string Field3LabelText { get; set; }
        public string Field3InitialValue { get; set; }
        public IReadOnlyList<string> Field3Options { get; set; }

        public string HeaderLevelName { get; set; }
        public string SubHeader { get; set; }

        public string NewField1Value { get; private set; }
        public string NewField2Value { get; private set; }
        public string NewField3Value { get; private set; }

        public LevelSettingsPopover()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        public static LevelSettingsResult Show(
            Window owner,
            string levelName,
            string subHeader,
            string field1Key, string field1Label, string field1Value, IReadOnlyList<string> field1Options,
            string field2Key, string field2Label, string field2Value, IReadOnlyList<string> field2Options,
            string field3Key, string field3Label, string field3Value, IReadOnlyList<string> field3Options)
        {
            var dlg = new LevelSettingsPopover
            {
                HeaderLevelName = levelName,
                SubHeader = subHeader,
                Field1KeyValue = field1Key,
                Field1LabelText = field1Label,
                Field1InitialValue = field1Value,
                Field1Options = field1Options,
                Field2KeyValue = field2Key,
                Field2LabelText = field2Label,
                Field2InitialValue = field2Value,
                Field2Options = field2Options,
                Field3KeyValue = field3Key,
                Field3LabelText = field3Label,
                Field3InitialValue = field3Value,
                Field3Options = field3Options
            };
            DialogOwner.Apply(dlg, owner);
            bool? ok = dlg.ShowDialog();
            if (ok != true) return null;
            return new LevelSettingsResult
            {
                Field1Key = field1Key,
                Field1Value = dlg.NewField1Value,
                Field2Key = field2Key,
                Field2Value = dlg.NewField2Value,
                Field3Key = field3Key,
                Field3Value = dlg.NewField3Value
            };
        }

        /// <summary>
        /// WPF only accepts a Window that has already been shown as an <see cref="Window.Owner"/>. Under
        /// Revit, <c>Application.Current.MainWindow</c> is a wrapper Revit never showed through WPF (its
        /// real main window is a native HWND), so assigning it throws InvalidOperationException.
        /// </summary>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(HeaderLevelName))
            {
                Title = "Level Settings \u2014 " + HeaderLevelName;
            }
            HeaderText.Text = (string.IsNullOrEmpty(HeaderLevelName) ? "LEVEL" : HeaderLevelName.ToUpperInvariant()) + " \u2014 SETTINGS";
            SubHeaderText.Text = SubHeader ?? "Planning only \u2014 backend pending. These values are recorded for documentation.";

            ConfigureField(Field1Container, Field1LabelText, Field1Combo, Field1InitialValue, Field1Options);
            ConfigureField(Field2Container, Field2LabelText, Field2Combo, Field2InitialValue, Field2Options);
            ConfigureField(Field3Container, Field3LabelText, Field3Combo, Field3InitialValue, Field3Options);
        }

        private void ConfigureField(Grid container, string label, ComboBox combo, string value, IReadOnlyList<string> options)
        {
            if (string.IsNullOrEmpty(label))
            {
                container.Visibility = Visibility.Collapsed;
                return;
            }

            TextBlock labelTb = (TextBlock)container.Children[0];
            labelTb.Text = label;

            combo.ItemsSource = options ?? new List<string>();
            combo.SelectedItem = value;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            NewField1Value = Field1Combo.SelectedItem as string;
            NewField2Value = Field2Combo.SelectedItem as string;
            NewField3Value = Field3Combo.SelectedItem as string;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class LevelSettingsResult
    {
        public string Field1Key { get; set; }
        public string Field1Value { get; set; }
        public string Field2Key { get; set; }
        public string Field2Value { get; set; }
        public string Field3Key { get; set; }
        public string Field3Value { get; set; }
    }
}
