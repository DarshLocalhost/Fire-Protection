using System;
using System.Collections.Generic;

namespace SPA.SynthFire.Core.Settings;

/// <summary>
/// The set of saved settings profiles plus which one is currently active. Pure and
/// serializable (no file IO — that is <see cref="SettingsProfileStore"/>'s job). All
/// name lookups are case-insensitive; stored settings are deep-copied on the way in and out
/// so a profile never shares mutable state with the live settings.
/// </summary>
public sealed class ProfileBook
{
    public List<SettingsProfile> Profiles { get; set; } = new();

    /// <summary>Name of the profile last saved or applied, if any.</summary>
    public string? ActiveProfileName { get; set; }

    public SettingsProfile? Find(string? name) =>
        name is null ? null
        : Profiles.Find(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Inserts or replaces the profile called <paramref name="name"/> with a deep copy of
    /// <paramref name="settings"/> and marks it active. Blank names are rejected (returns null).
    /// </summary>
    public SettingsProfile? Save(string name, SynthFireSettings settings)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();

        var existing = Find(name);
        if (existing is not null)
        {
            existing.Settings = SynthFireSettings.Clone(settings);
            ActiveProfileName = existing.Name;
            return existing;
        }

        var profile = new SettingsProfile { Name = name, Settings = SynthFireSettings.Clone(settings) };
        Profiles.Add(profile);
        ActiveProfileName = name;
        return profile;
    }

    /// <summary>Removes the named profile. Clears the active marker if it pointed there.</summary>
    public bool Remove(string? name)
    {
        var existing = Find(name);
        if (existing is null) return false;
        Profiles.Remove(existing);
        if (string.Equals(ActiveProfileName, existing.Name, StringComparison.OrdinalIgnoreCase))
            ActiveProfileName = null;
        return true;
    }

    /// <summary>
    /// Returns a deep copy of the named profile's settings and marks it active,
    /// or null if there is no such profile.
    /// </summary>
    public SynthFireSettings? Apply(string? name)
    {
        var existing = Find(name);
        if (existing is null) return null;
        ActiveProfileName = existing.Name;
        return SynthFireSettings.Clone(existing.Settings);
    }
}
