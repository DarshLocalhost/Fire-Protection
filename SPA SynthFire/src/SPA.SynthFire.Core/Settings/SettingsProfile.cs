namespace SPA.SynthFire.Core.Settings;

/// <summary>A named snapshot of <see cref="SynthFireSettings"/> (a saved profile).</summary>
public sealed class SettingsProfile
{
    public string Name { get; set; } = "";
    public SynthFireSettings Settings { get; set; } = new();
}
