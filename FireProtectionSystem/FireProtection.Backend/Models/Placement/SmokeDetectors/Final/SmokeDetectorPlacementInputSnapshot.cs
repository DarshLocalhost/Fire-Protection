using System.Collections.Generic;

namespace FireProtection.Backend.Models.Placement.SmokeDetectors.Final
{
    /// <summary>
    /// Revit-free input snapshot for the NFPA 72 smoke detector calculation engine.
    /// Built from the UI's <c>DeviceRoomInputItem</c> list by
    /// <see cref="Backend.Services.Placement.SmokeDetectors.SmokeDetectorPlacementInputBuilder"/>
    /// and consumed by <c>SmokeDetectorCalculationService.Calculate</c>. Mirrors the sprinkler
    /// <c>PlacementInputSnapshot</c> minus hazard fields.
    /// </summary>
    public class SmokeDetectorPlacementInputSnapshot
    {
        /// <summary>ISO-8601 ("o") UTC timestamp of when the snapshot was built.</summary>
        public string TimestampUtc { get; set; }

        public List<SmokeDetectorRoomInput> Rooms { get; set; }

        public SmokeDetectorPlacementInputSnapshot()
        {
            Rooms = new List<SmokeDetectorRoomInput>();
        }
    }
}
