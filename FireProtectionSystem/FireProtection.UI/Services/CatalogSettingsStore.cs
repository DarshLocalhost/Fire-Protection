using System;
using System.IO;
using Newtonsoft.Json;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Persists the user's catalog-source choice and the last workbook path between sessions.
    ///
    /// Storage is a single small JSON file under %APPDATA%\FireProtection, matching the folder
    /// convention already used by <see cref="FireProtectionLog.LogFolder"/>. Persisting the mode
    /// matters because the user has to pick a source on every single session otherwise — which
    /// for a daily-use tool is friction they will work around by accident.
    ///
    /// EVERY method is failure-tolerant: settings are a convenience, never a dependency. A corrupt
    /// or unwritable file falls back to defaults rather than blocking the add-in.
    /// </summary>
    public sealed class CatalogSettings
    {
        public CatalogSourceMode SourceMode { get; set; }

        /// <summary>Last workbook the user chose, so CatalogFile mode can re-open it.</summary>
        public string LastCatalogPath { get; set; }
    }

    public static class CatalogSettingsStore
    {
        private const string FileName = "catalog-settings.json";

        /// <summary>
        /// Overridable for tests; defaults to %APPDATA%\FireProtection\catalog-settings.json.
        /// </summary>
        public static string OverridePath { get; set; }

        public static string SettingsPath
        {
            get
            {
                if (!string.IsNullOrEmpty(OverridePath)) return OverridePath;
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FireProtection",
                    FileName);
            }
        }

        /// <summary>Reads persisted settings. Returns defaults when absent or unreadable.</summary>
        public static CatalogSettings Load()
        {
            try
            {
                string path = SettingsPath;
                if (!File.Exists(path)) return new CatalogSettings { SourceMode = CatalogSourceModes.Default };

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return new CatalogSettings { SourceMode = CatalogSourceModes.Default };

                CatalogSettings loaded = JsonConvert.DeserializeObject<CatalogSettings>(json);
                if (loaded == null) return new CatalogSettings { SourceMode = CatalogSourceModes.Default };

                // Guard against a hand-edited or stale file carrying an undefined enum value.
                if (!System.Enum.IsDefined(typeof(CatalogSourceMode), loaded.SourceMode))
                    loaded.SourceMode = CatalogSourceModes.Default;

                return loaded;
            }
            catch (Exception ex)
            {
                FireProtectionLog.Warn("Catalog settings could not be read; using defaults: " + ex.Message);
                return new CatalogSettings { SourceMode = CatalogSourceModes.Default };
            }
        }

        /// <summary>Writes settings. Returns false when the write failed; never throws.</summary>
        public static bool Save(CatalogSettings settings)
        {
            if (settings == null) return false;
            try
            {
                string path = SettingsPath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(path, JsonConvert.SerializeObject(settings, Formatting.Indented));
                return true;
            }
            catch (Exception ex)
            {
                FireProtectionLog.Warn("Catalog settings could not be written: " + ex.Message);
                return false;
            }
        }
    }
}