using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FireProtection.UI.Converters
{
    public class RunLogLevelToBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush ErrorBrush = new SolidColorBrush(Color.FromRgb(192, 57, 43));
        private static readonly SolidColorBrush WarningBrush = new SolidColorBrush(Color.FromRgb(185, 119, 14));
        private static readonly SolidColorBrush InfoBrush = new SolidColorBrush(Color.FromRgb(90, 99, 112));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string level = value as string;
            if (level == "ERROR")
            {
                return ErrorBrush;
            }

            if (level == "WARN")
            {
                return WarningBrush;
            }

            return InfoBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
