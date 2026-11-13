using Autodesk.Revit.DB;
using SPA.SynthFire.Core.Devices;

namespace SPA.SynthFire.Revit;

/// <summary>How a fire-alarm device mounts, deciding its placement strategy.</summary>
public enum FireAlarmMount
{
    /// <summary>Smoke/heat detector — hosts to the ceiling above a coverage grid point.</summary>
    Ceiling,

    /// <summary>Notification appliance (horn/strobe/sounder) — hosts to a wall at mounting height.</summary>
    Wall
}

/// <summary>
/// Classifies a fire-alarm family as a ceiling detector or a wall-mounted
/// notification appliance from its name.
///
/// An EXPLICIT mount phrase in the name wins over the device-type heuristic: a
/// "Horn Strobe - Ceiling Mounted" is a ceiling device even though horns/strobes
/// are usually wall appliances (this is exactly the family Revit's default library
/// ships, and hosting it to a wall is the bug we are avoiding). Only when the name
/// carries no explicit mount do we fall back to the device-type keywords —
/// horns/strobes/sounders default to walls, detectors to ceilings.
/// </summary>
public static class FireAlarmMountResolver
{
    public static FireAlarmMount Resolve(FamilySymbol symbol)
    {
        string name = ($"{symbol.FamilyName} {symbol.Name}").ToLowerInvariant();

        // 1) Explicit mount phrase in the name — highest priority.
        if (name.Contains("ceiling mounted") || name.Contains("ceiling-mounted") ||
            name.Contains("recessed") || name.Contains("pendent") || name.Contains("pendant"))
            return FireAlarmMount.Ceiling;
        if (name.Contains("wall mounted") || name.Contains("wall-mounted"))
            return FireAlarmMount.Wall;

        // 2) Device-type heuristic — notification appliances default to walls.
        string[] wallKeywords =
        {
            "horn", "strobe", "sounder", "bell", "chime", "speaker", "notification"
        };
        foreach (var k in wallKeywords)
            if (name.Contains(k)) return FireAlarmMount.Wall;

        // 3) Default: detectors mount to ceilings.
        return FireAlarmMount.Ceiling;
    }
}

/// <summary>Maps the fire-alarm mount enum to the Revit-free <see cref="DeviceMount"/>:
/// a wall appliance throws directionally like a sidewall; a ceiling detector is a circle.</summary>
public static class FireAlarmMountExtensions
{
    public static DeviceMount ToDeviceMount(this FireAlarmMount mount) =>
        mount == FireAlarmMount.Wall ? DeviceMount.Sidewall : DeviceMount.Ceiling;
}
