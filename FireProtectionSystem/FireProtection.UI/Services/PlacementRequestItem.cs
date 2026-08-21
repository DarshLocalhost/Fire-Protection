using System.Collections.Generic;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// UI-side, Revit-free representation of one room the user asked to place.
    /// The executor is responsible for translating this into whatever the
    /// backend/Revit APIs need.
    /// </summary>
    public class PlacementRequestItem
    {
        public string LevelId { get; set; }
        public string LevelName { get; set; }
        public double LevelElevationFt { get; set; }

        public string RoomId { get; set; }
        public string RoomName { get; set; }
        public string RoomNumber { get; set; }
        public double AreaSqFt { get; set; }
        public double? CeilingHeightFt { get; set; }

        /// <summary>Polygon in host coordinates, [x,y] pairs (feet).</summary>
        public List<double[]> Polygon { get; set; }

        /// <summary>Effective (possibly user-overridden) hazard class string.</summary>
        public string EffectiveHazardClass { get; set; }

        /// <summary>
        /// User-selected sprinkler family name from the UI.
        /// Must identify the exact Revit Family.Name to resolve in the host document.
        /// </summary>
        public string SelectedSprinklerFamilyName { get; set; }

        /// <summary>
        /// User-selected sprinkler type name from the UI.
        /// Must identify the exact Revit FamilySymbol.Name within the selected family.
        /// </summary>
        public string SelectedSprinklerTypeName { get; set; }

        public PlacementRequestItem()
        {
            Polygon = new List<double[]>();
        }
    }
}