using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Minimal rolling file log under %APPDATA%\FireProtection\logs. Debug.WriteLine is invisible in a
    /// real Revit install, so support tickets had nothing to attach; every entry here survives the session.
    /// Never throws: logging failures must not break placement.
    /// </summary>
    public static class FireProtectionLog
    {
        private const long MaxBytes = 5L * 1024 * 1024;
        private static readonly object Gate = new object();
        private static string _path;

        /// <summary>Full path of today's log file (created on first write).</summary>
        public static string LogFilePath
        {
            get
            {
                EnsurePath();
                return _path;
            }
        }

        public static string LogFolder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FireProtection",
                    "logs");
            }
        }

        public static void Info(string message)
        {
            Write("INFO ", message, null);
        }

        public static void Warn(string message)
        {
            Write("WARN ", message, null);
        }

        public static void Error(string message, Exception ex = null)
        {
            Write("ERROR", message, ex);
        }

        private static void EnsurePath()
        {
            if (_path != null) return;

            string folder = LogFolder;
            Directory.CreateDirectory(folder);
            _path = Path.Combine(
                folder,
                "fireprotection-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
        }

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                lock (Gate)
                {
                    EnsurePath();
                    Roll();

                    StringBuilder line = new StringBuilder();
                    line.Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
                    line.Append("  ").Append(level).Append("  ").Append(message ?? string.Empty);
                    if (ex != null) line.AppendLine().Append(ex);

                    File.AppendAllText(_path, line.ToString() + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // A log that breaks the tool is worse than no log.
            }
        }

        /// <summary>Keeps one previous generation so a long session cannot fill the disk.</summary>
        private static void Roll()
        {
            FileInfo info = new FileInfo(_path);
            if (!info.Exists || info.Length < MaxBytes) return;

            string previous = _path + ".1";
            if (File.Exists(previous)) File.Delete(previous);
            File.Move(_path, previous);
        }
    }
}
