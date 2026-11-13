using System;
using System.IO;
using System.Text.Json;

namespace SPA.SynthFire.Core.Settings;

/// <summary>
/// Loads and saves the <see cref="ProfileBook"/> as JSON under
/// %APPDATA%\SPA SynthFire\profiles.json. Never throws to callers — a missing or corrupt
/// file yields an empty book so the tool always runs.
/// </summary>
public static class SettingsProfileStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string ProfilesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SPA SynthFire", "profiles.json");

    public static ProfileBook Load()
    {
        try
        {
            if (File.Exists(ProfilesPath))
            {
                string json = File.ReadAllText(ProfilesPath);
                var book = JsonSerializer.Deserialize<ProfileBook>(json, Options);
                if (book is not null) return book;
            }
        }
        catch
        {
            // fall through to an empty book
        }
        return new ProfileBook();
    }

    public static void Save(ProfileBook book)
    {
        try
        {
            string dir = Path.GetDirectoryName(ProfilesPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(ProfilesPath, JsonSerializer.Serialize(book, Options));
        }
        catch
        {
            // best-effort; profiles are non-critical
        }
    }
}
