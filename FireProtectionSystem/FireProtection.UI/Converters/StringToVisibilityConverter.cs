using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FireProtection.UI.Converters
{
    /// <summary>
    /// Shows an element only when the bound string carries content, so an explanatory message can
    /// replace a blank line instead of always reserving vertical space.
    ///
    /// Null, empty, and whitespace-only all collapse the element. Without this, status messages that
    /// are <c>null</c> in the normal case (for example "why is this dropdown empty" — null when the
    /// dropdown has data) would leave a permanent empty gap in the layout.
    /// </summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        /// <summary>When true (the default) an empty string hides the element; set false to invert.</summary>
        public bool CollapseWhenEmpty { get; set; } = true;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool hasContent = !string.IsNullOrWhiteSpace(value as string);

            if (value != null && !(value is string))
            {
                // Fall back to ToString so a non-string value (e.g. a formatted number) still works.
                hasContent = !string.IsNullOrWhiteSpace(value.ToString());
            }

            bool visible = CollapseWhenEmpty ? hasContent : !hasContent;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // One-way only. Visibility is never a meaningful source for a text binding.
            return Binding.DoNothing;
        }
    }
}