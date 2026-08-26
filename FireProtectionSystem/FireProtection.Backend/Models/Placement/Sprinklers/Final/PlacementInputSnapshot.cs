using System;
using System.Collections.Generic;
using FireProtection.Backend.Models.DTOs;
using Newtonsoft.Json;

namespace FireProtection.Backend.Models.Placement.Sprinklers.Final
{
    /// <summary>
    /// Root structured snapshot representing the entire input payload prepared for
    /// the future sprinkler placement calculation engine (Final/BruteForce).
    /// </summary>
    public class PlacementInputSnapshot
    {
        [JsonProperty("schemaVersion")]
        public string SchemaVersion { get; set; } = "1.0";

        [JsonProperty("timestampUtc")]
        public string TimestampUtc { get; set; } = DateTime.UtcNow.ToString("o");

        [JsonProperty("units")]
        public UnitsInfo Units { get; set; }

        [JsonProperty("coordinateSystem")]
        public CoordinateSystemInfo CoordinateSystem { get; set; }

        [JsonProperty("project")]
        public ProjectInfo Project { get; set; }

        [JsonProperty("sprinkler")]
        public SelectedSprinklerInfo Sprinkler { get; set; }

        [JsonProperty("totalRoomsSelected")]
        public int TotalRoomsSelected => Rooms != null ? Rooms.Count : 0;

        [JsonProperty("totalAreaSqFt")]
        public double TotalAreaSqFt { get; set; }

        [JsonProperty("rooms")]
        public List<PlacementRoomInput> Rooms { get; set; }

        public PlacementInputSnapshot()
        {
            Units = new UnitsInfo();
            CoordinateSystem = new CoordinateSystemInfo();
            Project = new ProjectInfo();
            Sprinkler = new SelectedSprinklerInfo();
            Rooms = new List<PlacementRoomInput>();
        }
    }
}
