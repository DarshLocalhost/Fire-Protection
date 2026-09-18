using System;
using System.Collections.Generic;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Placement.SmokeDetectors.Final;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.UI.Services;
using Newtonsoft.Json;

namespace FireProtection.Backend.Services.Placement.SmokeDetectors
{
    public static class SmokeDetectorPlacementInputBuilder
    {
        public static SmokeDetectorPlacementInputSnapshot BuildSnapshot(
            List<DeviceRoomInputItem> roomItems,
            RevitSmokeDetectorFamilySource familySource)
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
                    DetectorType = item.DetectorType,
                    Mount = item.Mount,
                    CeilingSlope = item.CeilingSlope,
                    SelectedFamilyName = item.SelectedFamilyName,
                    SelectedTypeName = item.SelectedTypeName,
                    BoundaryPolygon = item.Polygon ?? new List<double[]>(),
                    DeviceKind = FireProtection.UI.Services.DeviceKind.SmokeDetector
                };

                if (familySource != null && !string.IsNullOrEmpty(item.SelectedFamilyName))
                {
                    roomInput.SelectedPlacementBehavior = familySource.GetPlacementBehavior(
                        item.SelectedFamilyName, item.SelectedTypeName);
                }

                if (item.FullRoomJson != null)
                {
                    HydrateFromRoomJson(roomInput, item.FullRoomJson.ToString());
                }

                snapshot.Rooms.Add(roomInput);
            }

            return snapshot;
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
            catch
            {
                // Fallback to basic geometry already populated
            }
        }
    }
}