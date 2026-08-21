using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FireProtection.UI.Views.Sprinklers.BruteForce.Converters
{
    public class HazardClassToBrushConverter : IValueConverter
    {
        public Brush LightBackground { get; set; } = new SolidColorBrush(Color.FromRgb(0xDC, 0xFC, 0xE7));
        public Brush LightForeground { get; set; } = new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3D));

        public Brush OrdinaryBackground { get; set; } = new SolidColorBrush(Color.FromRgb(0xFF, 0xED, 0xD5));
        public Brush OrdinaryForeground { get; set; } = new SolidColorBrush(Color.FromRgb(0xC2, 0x41, 0x0C));

        public Brush ExtraBackground { get; set; } = new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2));
        public Brush ExtraForeground { get; set; } = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));

        public Brush DefaultBackground { get; set; } = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
        public Brush DefaultForeground { get; set; } = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));

        public bool ReturnForeground { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string s = (value as string)?.Trim().ToUpperInvariant() ?? string.Empty;

            if (s == "LIGHT" || s == "LH")
                return ReturnForeground ? LightForeground : LightBackground;

            if (s.StartsWith("OH") || s.StartsWith("ORDINARY"))
                return ReturnForeground ? OrdinaryForeground : OrdinaryBackground;

            if (s.StartsWith("EH") || s.StartsWith("EXTRA"))
                return ReturnForeground ? ExtraForeground : ExtraBackground;

            return ReturnForeground ? DefaultForeground : DefaultBackground;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}