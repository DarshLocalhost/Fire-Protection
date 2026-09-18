using System;
using FireProtection.UI.Services;

namespace FireProtection.Backend.Services.Placement.Devices
{
    /// <summary>
    /// Classifies a fire-alarm device by family/type keywords. Device kinds are NOT distinguishable by Revit
    /// category — smoke detectors and notification appliances both live in <c>OST_FireAlarmDevices</c> — so
    /// the family/type name is the discriminator used to scope existing-device detection (skip / replace /
    /// duplicate guard) to one device kind.
    /// </summary>
    public static class DeviceKindResolver
    {
        public static DeviceKind Resolve(string familyName, string typeName, string descriptor, string mount)
        {
            if (string.IsNullOrWhiteSpace(familyName) && string.IsNullOrWhiteSpace(typeName)
                && string.IsNullOrWhiteSpace(descriptor) && string.IsNullOrWhiteSpace(mount))
            {
                return DeviceKind.SmokeDetector;
            }

            string combined = (familyName ?? string.Empty) + " " + (typeName ?? string.Empty) + " "
                + (descriptor ?? string.Empty) + " " + (mount ?? string.Empty);

            if (combined.IndexOf("notification", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("strobe", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("horn", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("speaker", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("audible", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("visible", StringComparison.OrdinalIgnoreCase) >= 0
                || (combined.IndexOf("candela", StringComparison.OrdinalIgnoreCase) >= 0
                    && combined.IndexOf("dba", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return DeviceKind.NotificationAppliance;
            }

            if (combined.IndexOf("sprinkler", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("pendent", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("upright", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("sidewall", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return DeviceKind.Sprinkler;
            }

            if (combined.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("detector", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("photoelectric", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("ionization", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return DeviceKind.SmokeDetector;
            }

            return DeviceKind.SmokeDetector;
        }

        /// <summary>
        /// Keyword-only classification: returns false when no explicit keyword matched (instead of guessing).
        /// Used to scope existing-device detection to one device kind — an unclassifiable fire-alarm device is
        /// treated as "not ours": it never triggers the room skip/replace policy and never blocks a new point
        /// as a duplicate, so a run of THIS kind can never skip or delete a device of any other kind.
        /// </summary>
        public static bool TryResolve(string familyName, string typeName, out DeviceKind kind)
        {
            kind = DeviceKind.SmokeDetector;
            string combined = (familyName ?? string.Empty) + " " + (typeName ?? string.Empty);

            if (combined.IndexOf("notification", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("strobe", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("horn", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("speaker", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("audible", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = DeviceKind.NotificationAppliance;
                return true;
            }

            if (combined.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("photoelectric", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("ionization", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("heat detector", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = DeviceKind.SmokeDetector;
                return true;
            }

            return false;
        }
    }
}
