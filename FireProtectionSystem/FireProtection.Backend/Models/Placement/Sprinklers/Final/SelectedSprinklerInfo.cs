using Newtonsoft.Json;

namespace FireProtection.Backend.Models.Placement.Sprinklers.Final
{
    /// <summary>
    /// Captures the user-selected sprinkler family and type, plus the
    /// Revit-free plain device placement context required by the future
    /// candidate generation (Step 2).
    ///
    /// <para>
    /// <see cref="FamilyPlacementType"/> stores the PROVEN Revit's
    /// <c>Autodesk.Revit.DB.FamilyPlacementType.ToString()</c> value (e.g.
    /// <c>"FaceBased"</c>, <c>"WorkPlaneBased"</c>, <c>"OneLevelBased"</c>).
    /// It is a string so the calculation engine (which is Revit-free) can
    /// inspect it without taking a Revit dependency.
    /// </para>
    ///
    /// <para>
    /// <see cref="PlacementBehavior"/> is the normalized, plain classification
    /// the calculation engine will eventually consume. The mapping from the
    /// Revit <c>FamilyPlacementType</c> string to this enum is performed once,
    /// at the Revit-aware boundary, in
    /// <see cref="FireProtection.Backend.Services.Placement.RevitSprinklerFamilySource"/>
    /// (see <c>ResolveDevicePlacementBehavior</c>).
    /// </para>
    ///
    /// <para>
    /// Both fields default to <c>null</c> / <see cref="DevicePlacementBehavior.Unknown"/>
    /// to preserve full backward compatibility with every caller that constructs
    /// this DTO with the existing 0/1/2-arg constructors (per Step 2 backward-compat rule).
    /// </para>
    /// </summary>
    public class SelectedSprinklerInfo
    {
        [JsonProperty("familyName")]
        public string FamilyName { get; set; }

        [JsonProperty("typeName")]
        public string TypeName { get; set; }

        /// <summary>
        /// The proven Revit's <c>FamilyPlacementType</c> string (e.g.
        /// <c>"FaceBased"</c> / <c>"WorkPlaneBased"</c> / <c>"OneLevelBased"</c>).
        /// <c>null</c> when the family/type was not resolved.
        /// </summary>
        [JsonProperty("familyPlacementType")]
        public string FamilyPlacementType { get; set; }

        /// <summary>
        /// Plain, Revit-free classification of the placement behavior required by the
        /// selected family/type. Default <see cref="DevicePlacementBehavior.Unknown"/>.
        /// The mapping from <see cref="FamilyPlacementType"/> to this enum is fixed
        /// and lives in
        /// <see cref="FireProtection.Backend.Services.Placement.RevitSprinklerFamilySource"/>.
        /// </summary>
        [JsonProperty("placementBehavior")]
        public DevicePlacementBehavior PlacementBehavior { get; set; } = DevicePlacementBehavior.Unknown;

        public SelectedSprinklerInfo() { }

        public SelectedSprinklerInfo(string familyName, string typeName)
        {
            FamilyName = familyName;
            TypeName = typeName;
        }
    }
}
