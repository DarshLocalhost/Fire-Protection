using System;
using System.Collections.Generic;
using System.Globalization;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Placement.SmokeDetectors.Final;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.UI.Services;
using Newtonsoft.Json;

namespace FireProtection.Backend.Services.Placement.NotificationAppliances
{
    public static class NotificationAppliancePlacementInputBuilder
    {
        public static SmokeDetectorPlacementInputSnapshot BuildSnapshot(
            List<DeviceRoomInputItem> roomItems,
            RevitNotificationApplianceFamilySource familySource)
        {
            var snapshot = new SmokeDetectorPlacementInputSnapshot
            {
                TimestampUtc = DateTime.UtcNow.ToString("o")
            };

            if (roomItems == null) return snapshot;

            foreach (DeviceRoomInputItem item in roomItems)
            {
                if (item == null) continue;

                var roomInput = new SmokeDetectorRoomInput
                {
                    LevelId = item.LevelId,
                    LevelName = item.LevelName,
                    LevelElevationFt = item.LevelElevationFt,
                    RoomId = item.RoomId,
                    RoomName = item.RoomName,
                    RoomNumber = item.RoomNumber,
                    AreaSqFt = item.AreaSqFt,
                    CeilingHeightFt = item.CeilingHeightFt,
                    CeilingType = item.CeilingType,
                    DetectorType = BuildRuleDescriptor(item),
                    RuleDescriptor = item.CandelaDba,
                    Mount = ResolveMount(item),
                    CeilingSlope = item.CeilingSlope,
                    SelectedFamilyName = item.SelectedFamilyName,
                    SelectedTypeName = item.SelectedTypeName,
                    BoundaryPolygon = item.Polygon ?? new List<double[]>(),
                    DeviceKind = FireProtection.UI.Services.DeviceKind.NotificationAppliance
                };

                if (familySource != null && !string.IsNullOrEmpty(item.SelectedFamilyName))
                {
                    roomInput.SelectedPlacementBehavior = familySource.GetPlacementBehavior(
                        item.SelectedFamilyName, item.SelectedTypeName);

                    if (string.Equals(roomInput.Mount, "Wall", StringComparison.OrdinalIgnoreCase) ||
                        roomInput.SelectedPlacementBehavior == DevicePlacementBehavior.WallSidewall)
                    {
                        roomInput.Mount = "Wall";
                        roomInput.SelectedPlacementBehavior = DevicePlacementBehavior.WallSidewall;
                    }
                }

                if (item.FullRoomJson != null)
                {
                    HydrateFromRoomJson(roomInput, item.FullRoomJson.ToString());
                }

                snapshot.Rooms.Add(roomInput);
            }

            return snapshot;
        }

        private static string BuildRuleDescriptor(DeviceRoomInputItem item)
        {
            string appliance = string.IsNullOrWhiteSpace(item.ApplianceType)
                ? "Notification Appliance" : item.ApplianceType.Trim();
            int candela = 0;
            int dba = 0;
            if (!string.IsNullOrWhiteSpace(item.CandelaDba))
            {
                string[] parts = item.CandelaDba.Split('/');
                if (parts.Length > 0) int.TryParse(parts[0].Replace("cd", string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out candela);
                if (parts.Length > 1) int.TryParse(parts[1].Replace("dBA", string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out dba);
            }
            return appliance + "|candela=" + candela.ToString(CultureInfo.InvariantCulture)
                + "|dba=" + dba.ToString(CultureInfo.InvariantCulture);
        }

        private static string ResolveMount(DeviceRoomInputItem item)
        {
            string family = item.SelectedFamilyName ?? string.Empty;
            string type = item.SelectedTypeName ?? string.Empty;
            string appType = item.ApplianceType ?? string.Empty;
            string mountOverride = item.Mount ?? string.Empty;

            // Naming indicators for wall notification devices (e.g. Horn, Strobe, Chime, Wall)
            if (family.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0
                || type.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0
                || appType.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0
                || mountOverride.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0
                || family.IndexOf("horn", StringComparison.OrdinalIgnoreCase) >= 0
                || type.IndexOf("horn", StringComparison.OrdinalIgnoreCase) >= 0
                || appType.IndexOf("horn", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Wall";
            }

            return "Ceiling";
        }

        private static void HydrateFromRoomJson(SmokeDetectorRoomInput roomInput, string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                var roomData = JsonConvert.DeserializeObject<RoomData>(json);
                if (roomData == null) return;

                if (roomData.Boundary != null)
                {
                    roomInput.Boundary = roomData.Boundary;
                    if ((roomInput.BoundaryPolygon == null || roomInput.BoundaryPolygon.Count < 3)
                        && roomData.Boundary.Polygon != null)
                    {
                        roomInput.BoundaryPolygon = roomData.Boundary.Polygon;
                    }
                }

                if (roomData.Ceilings != null && roomData.Ceilings.Count > 0)
                {
                    roomInput.Ceilings = roomData.Ceilings;
                }
            }
            catch { }
        }
    }
}