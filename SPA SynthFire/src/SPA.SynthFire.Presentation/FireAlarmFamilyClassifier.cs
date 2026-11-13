namespace SPA.SynthFire.UI;

/// <summary>How a fire-alarm family is expected to mount, inferred from its label.</summary>
public enum FireAlarmRole
{
    /// <summary>Smoke/heat detector — a ceiling device the detection window places.</summary>
    Detector,

    /// <summary>Notification appliance (horn/strobe/sounder) — a wall device.</summary>
    NotificationAppliance
}

/// <summary>
/// Name-based classifier that tells whether a fire-alarm family option is a ceiling
/// detector or a wall notification appliance. Presentation-layer mirror of the Revit
/// <c>FireAlarmMountResolver</c> (which works on the live FamilySymbol); this one works
/// on the display label so the detection view model can preselect a real detector and
/// warn when the user picks an appliance for a detector run. Keep the keyword lists in
/// sync with FireAlarmMountResolver.
/// </summary>
public static class FireAlarmFamilyClassifier
{
    private static readonly string[] NotificationKeywords =
    {
        "horn", "strobe", "sounder", "bell", "chime", "speaker", "notification"
    };

    private static readonly string[] CeilingPhrases =
    {
        "ceiling mounted", "ceiling-mounted", "recessed", "pendent", "pendant"
    };

    public static FireAlarmRole Classify(string? label)
    {
        string name = (label ?? string.Empty).ToLowerInvariant();

        // An explicit ceiling-mount phrase wins over the appliance keywords, matching
        // FireAlarmMountResolver (e.g. a "Horn Strobe - Ceiling Mounted" is a ceiling device).
        foreach (var p in CeilingPhrases)
            if (name.Contains(p)) return FireAlarmRole.Detector;

        if (name.Contains("wall mounted") || name.Contains("wall-mounted"))
            return FireAlarmRole.NotificationAppliance;

        foreach (var k in NotificationKeywords)
            if (name.Contains(k)) return FireAlarmRole.NotificationAppliance;

        // Default: detectors mount to ceilings.
        return FireAlarmRole.Detector;
    }

    public static bool IsDetector(string? label) => Classify(label) == FireAlarmRole.Detector;
}
