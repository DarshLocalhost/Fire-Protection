using System;

namespace FireProtection.UI.ViewModels.Devices
{
    public class RunLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
        public string Level { get; set; }

        public string FormattedTime => Timestamp.ToString("HH:mm:ss");
        public string FormattedEntry => $"[{FormattedTime}] [{Level}] {Message}";
    }
}
