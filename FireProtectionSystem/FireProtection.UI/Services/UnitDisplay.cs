using System;
using System.Globalization;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Length display units for the UI only. Every stored value, every calculation and every Revit call stays
    /// in decimal feet (Revit's internal unit); this converts at the presentation boundary so a metric project
    /// sees mm instead of ft. Configured once by the Backend from the project's own unit settings; defaults to
    /// feet so tests, the designer and the standalone catalog tool keep working unchanged.
    /// <para>Areas are NOT converted — the AREA column stays ft² and is labelled as such.</para>
    /// </summary>
    public static class UnitDisplay
    {
        private static string _suffix = "ft";
        private static double _feetToDisplay = 1.0;
        private static int _decimals = 2;

        /// <summary>Short unit label, e.g. "ft" or "mm".</summary>
        public static string Suffix { get { return _suffix; } }

        /// <summary>True when the project displays feet, so no conversion happens.</summary>
        public static bool IsFeet { get { return Math.Abs(_feetToDisplay - 1.0) < 1e-9; } }

        /// <summary>Upper-case bracketed label for column headers, e.g. "(FT)".</summary>
        public static string HeaderSuffix
        {
            get { return "(" + _suffix.ToUpperInvariant() + ")"; }
        }

        /// <summary>
        /// Sets the display unit. <paramref name="feetToDisplay"/> is how many display units one foot is
        /// (304.8 for mm). Invalid or non-finite factors are ignored, leaving feet in place.
        /// </summary>
        public static void Configure(string suffix, double feetToDisplay, int decimals = 2)
        {
            if (string.IsNullOrWhiteSpace(suffix)) return;
            if (double.IsNaN(feetToDisplay) || double.IsInfinity(feetToDisplay) || feetToDisplay <= 0) return;

            _suffix = suffix.Trim();
            _feetToDisplay = feetToDisplay;
            _decimals = decimals < 0 ? 0 : (decimals > 4 ? 4 : decimals);
        }

        public static double FromFeet(double feet)
        {
            return feet * _feetToDisplay;
        }

        public static double ToFeet(double displayValue)
        {
            return displayValue / _feetToDisplay;
        }

        /// <summary>Formats a stored feet value for display, or an em dash when there is no value.</summary>
        public static string Format(double? feet)
        {
            if (!feet.HasValue) return "—";
            return FromFeet(feet.Value).ToString("F" + _decimals.ToString(CultureInfo.InvariantCulture),
                CultureInfo.CurrentCulture);
        }

        /// <summary>Formats a stored feet value with its unit, e.g. "15.00 ft".</summary>
        public static string FormatWithUnit(double? feet)
        {
            if (!feet.HasValue) return "—";
            return Format(feet) + " " + _suffix;
        }

        /// <summary>
        /// Parses user input in display units into feet. Returns false for blank, non-numeric,
        /// negative, zero and non-finite input — the caller shows the error next to the field.
        /// </summary>
        public static bool TryParseToFeet(string text, out double feet, out string error)
        {
            feet = 0;
            error = null;

            if (string.IsNullOrWhiteSpace(text) || text.Trim() == "—")
            {
                error = "empty";
                return false;
            }

            double parsed;
            if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out parsed)
                && !double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                error = "Enter a number, e.g. " + Format(15.0) + ".";
                return false;
            }

            if (double.IsNaN(parsed) || double.IsInfinity(parsed))
            {
                error = "Enter a finite number.";
                return false;
            }

            if (parsed <= 0)
            {
                error = "Must be greater than 0 " + _suffix + ".";
                return false;
            }

            feet = ToFeet(parsed);
            return true;
        }
    }
}
