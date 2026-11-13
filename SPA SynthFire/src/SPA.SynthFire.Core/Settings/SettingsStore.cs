using System;
using System.IO;
using System.Text.Json;

namespace SPA.SynthFire.Core.Settings;

/// <summary>
/// Loads and saves <see cref="SynthFireSettings"/> as JSON under
/// %APPDATA%\SPA SynthFire\settings.json. Never throws to callers — a missing or
/// corrupt file yields defaults so the tool always runs.
/// </summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SPA SynthFire", "settings.json");

    public static SynthFireSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<SynthFireSettings>(json, Options);
                if (settings is not null) return settings;
            }
        }
        catch
        {
            // fall through to defaults
        }
        return new SynthFireSettings();
    }

    public static void Save(SynthFireSettings settings)
    {
        try
        {
            string dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Options));
        }
        catch
        {
            // best-effort; settings are non-critical
        }
    }
}
