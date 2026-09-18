using System;

namespace FireProtection.Backend.Services.Placement.LocationPoints
{
    public enum DeviceLocationPointStrategy
    {
        SprinklerCeilingGrid,
        SprinklerSidewall,
        SmokeDetectorCeilingGrid,
        SmokeDetectorWallMounted,
        NotificationApplianceCeilingGrid,
        NotificationApplianceWallMounted
    }

    public sealed class DeviceLocationPointProfile
    {
        public string DeviceKind { get; set; }
        public string Mount { get; set; }
        public string StrategyName { get; set; }
        public DeviceLocationPointStrategy Strategy { get; set; }
        public double GridResolutionFt { get; set; }
        public double MinSpacingFt { get; set; }
        public double CoverageRadiusFt { get; set; }
        public double PlacementZ { get; set; }
        public string SelectionBasis { get; set; }
    }

    public static class DeviceLocationPointIdentifier
    {
        public static DeviceLocationPointProfile ResolveSprinklerProfile(
            string placementBehavior,
            string orientation,
            double maxSpacingFt,
            double boundaryClearanceFt,
            double coverageRadiusFt,
            double placementZ,
            string roomId = null)
        {
            bool isSidewall = string.Equals(placementBehavior, "WallSidewall", StringComparison.OrdinalIgnoreCase)
                || string.Equals(orientation, "sidewall", StringComparison.OrdinalIgnoreCase);

            double resolvedSpacing = maxSpacingFt > 0 ? maxSpacingFt : 15.0;
            double resolvedCoverage = coverageRadiusFt > 0 ? coverageRadiusFt : Math.Max(7.5, resolvedSpacing / 1.75);
            double resolvedMinSpacing = Math.Max(5.0, Math.Min(resolvedSpacing, resolvedCoverage));

            var profile = new DeviceLocationPointProfile
            {
                DeviceKind = "Sprinkler",
                Mount = isSidewall ? "Wall" : "Ceiling",
                Strategy = isSidewall ? DeviceLocationPointStrategy.SprinklerSidewall : DeviceLocationPointStrategy.SprinklerCeilingGrid,
                StrategyName = isSidewall ? "sprinkler-sidewall-edge-grid" : "sprinkler-ceiling-grid",
                GridResolutionFt = Math.Max(1.0, Math.Min(resolvedSpacing, resolvedCoverage)),
                MinSpacingFt = resolvedMinSpacing,
                CoverageRadiusFt = resolvedCoverage,
                PlacementZ = placementZ,
                SelectionBasis = isSidewall
                    ? "Wall-sidewall candidate path selected from room perimeter; perimeter edge spacing controls location point selection."
                    : "Ceiling candidate grid selected from room interior; interior spacing and boundary clearance drive location point selection."
            };

            if (!string.IsNullOrWhiteSpace(roomId))
            {
                profile.SelectionBasis += " RoomId=" + roomId + ".";
            }

            return profile;
        }

        public static DeviceLocationPointProfile ResolveSmokeDetectorProfile(
            string detectorType,
            string mount,
            string ceilingSlope,
            double maxSpacingFt,
            double minSpacingFt,
            double coverageRadiusFt,
            double placementZ,
            string roomId = null)
        {
            bool isWallMount = string.Equals(mount, "Wall", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mount, "WallSidewall", StringComparison.OrdinalIgnoreCase);
            double resolvedSpacing = maxSpacingFt > 0 ? maxSpacingFt : 30.0;
            double resolvedMinSpacing = minSpacingFt > 0 ? minSpacingFt : 10.0;
            double resolvedCoverage = coverageRadiusFt > 0 ? coverageRadiusFt : Math.Max(6.0, resolvedSpacing / Math.Sqrt(2.0));

            var profile = new DeviceLocationPointProfile
            {
                DeviceKind = "Smoke detector",
                Mount = isWallMount ? "Wall" : "Ceiling",
                Strategy = isWallMount ? DeviceLocationPointStrategy.SmokeDetectorWallMounted : DeviceLocationPointStrategy.SmokeDetectorCeilingGrid,
                StrategyName = isWallMount ? "smoke-detector-wall-mounted" : "smoke-detector-ceiling-grid",
                GridResolutionFt = Math.Max(1.0, Math.Min(resolvedSpacing, resolvedCoverage)),
                MinSpacingFt = resolvedMinSpacing,
                CoverageRadiusFt = resolvedCoverage,
                PlacementZ = placementZ,
                SelectionBasis = isWallMount
                    ? "Wall-mounted smoke detector location points are selected from the room perimeter with wall offset and clearance checks."
                    : "Ceiling-mounted smoke detector location points are selected from the room interior with NFPA 72 spacing, boundary, and obstacle checks."
            };

            if (!string.IsNullOrWhiteSpace(ceilingSlope))
            {
                profile.SelectionBasis += " CeilingSlope=" + ceilingSlope.Trim() + ".";
            }

            if (!string.IsNullOrWhiteSpace(detectorType))
            {
                profile.SelectionBasis += " DetectorType=" + detectorType.Trim() + ".";
            }

            if (!string.IsNullOrWhiteSpace(roomId))
            {
                profile.SelectionBasis += " RoomId=" + roomId + ".";
            }

            return profile;
        }

        public static DeviceLocationPointProfile ResolveNotificationApplianceProfile(
            string applianceType,
            string mount,
            double maxSpacingFt,
            double minSpacingFt,
            double coverageRadiusFt,
            double placementZ,
            string roomId = null)
        {
            bool isWallMount = string.Equals(mount, "Wall", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mount, "WallSidewall", StringComparison.OrdinalIgnoreCase);
            double resolvedSpacing = maxSpacingFt > 0 ? maxSpacingFt : 15.0;
            double resolvedMinSpacing = minSpacingFt > 0 ? minSpacingFt : Math.Max(5.0, resolvedSpacing * 0.35);
            double resolvedCoverage = coverageRadiusFt > 0 ? coverageRadiusFt : Math.Max(5.0, resolvedSpacing * 0.75);

            var profile = new DeviceLocationPointProfile
            {
                DeviceKind = "Notification appliance",
                Mount = isWallMount ? "Wall" : "Ceiling",
                Strategy = isWallMount ? DeviceLocationPointStrategy.NotificationApplianceWallMounted : DeviceLocationPointStrategy.NotificationApplianceCeilingGrid,
                StrategyName = isWallMount ? "notification-appliance-wall-mounted" : "notification-appliance-ceiling-grid",
                GridResolutionFt = Math.Max(1.0, Math.Min(resolvedSpacing, resolvedSpacing / 3.0)),
                MinSpacingFt = resolvedMinSpacing,
                CoverageRadiusFt = resolvedCoverage,
                PlacementZ = placementZ,
                SelectionBasis = isWallMount
                    ? "Wall-mounted notification appliance location points are selected from the wall edge using visible/audible spacing and wall clearance constraints."
                    : "Ceiling notification appliance location points are selected from the room grid using the stricter of visible and audible coverage rules."
            };

            if (!string.IsNullOrWhiteSpace(applianceType))
            {
                profile.SelectionBasis += " ApplianceType=" + applianceType.Trim() + ".";
            }

            if (!string.IsNullOrWhiteSpace(roomId))
            {
                profile.SelectionBasis += " RoomId=" + roomId + ".";
            }

            return profile;
        }
    }
}
