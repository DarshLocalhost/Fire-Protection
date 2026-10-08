using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FireProtection.UI.Converters
{
    /// <summary>
    /// Converts between a mutually-exclusive choice (the catalog source radio group) and a single
    /// enum-valued property.
    ///
    /// Required because a <c>RadioButton</c> binds its <c>IsChecked</c> to a bool while the source
    /// of truth is one enum.
    ///
    /// Used in ONE DIRECTION ONLY (enum -> bool, to reflect the current mode). The reverse direction
    /// is deliberately not used: <c>CatalogViewModel.SourceMode</c> has a private setter and is
    /// changed through <c>TrySetSourceMode</c>, which must be able to reject a switch and rebuild
    /// the catalog. Binding <c>IsChecked</c> with <c>Mode=TwoWay</c> throws
    /// <c>InvalidOperationException: A TwoWay or OneWayToSource binding cannot work on the read-only
    /// property 'SourceMode'</c> during <c>Window.Show()</c>, which aborts the command and lets Revit
    /// die in its own finalizer with an unrelated-looking SEHException. The radio's
    /// <c>Checked</c> handler drives the viewmodel instead.
    /// </summary>
    public class EnumToBoolConverter : IValueConverter
    {
        /// <summary>
        /// When true, a source value that is not the parameter converts to <c>false</c> rather than
        /// raising. Used for IsChecked binding so an unset source shows no selection instead of an
        /// exception being swallowed by WPF.
        /// </summary>
        public bool ReturnFalseForNull { get; set; } = true;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return ReturnFalseForNull ? (object)false : Binding.DoNothing;

            if (parameter == null) return Binding.DoNothing;

            Type enumType = value.GetType();
            if (!enumType.IsEnum) return ReturnFalseForNull ? (object)false : Binding.DoNothing;

            string valueName = Enum.GetName(enumType, value);
            string targetName = parameter as string;
            if (valueName == null || targetName == null) return false;

            return string.Equals(valueName, targetName, StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (targetType == null || !targetType.IsEnum) return Binding.DoNothing;

            bool isChecked;
            if (value is bool) isChecked = (bool)value;
            else if (value is string) bool.TryParse((string)value, out isChecked);
            else return Binding.DoNothing;

            if (!isChecked)
            {
                // Unchecking must NOT clear the source enum: WPF unchecks the previous radio
                // before checking the new one, which would otherwise wipe the value mid-switch.
                return Binding.DoNothing;
            }

            string targetName = parameter as string;
            if (string.IsNullOrEmpty(targetName)) return Binding.DoNothing;

            try
            {
                return Enum.Parse(targetType, targetName, true);
            }
            catch (ArgumentException)
            {
                return Binding.DoNothing;
            }
        }
    }
}