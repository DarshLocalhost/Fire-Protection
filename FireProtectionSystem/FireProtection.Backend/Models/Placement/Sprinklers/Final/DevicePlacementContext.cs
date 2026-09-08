using Newtonsoft.Json;

namespace FireProtection.Backend.Models.Placement.Sprinklers.Final
{
    /// <summary>
    /// Plain, Revit-free result of resolving a sprinkler (family, type) at the
    /// Revit-aware boundary. This is the small DTO returned by
    /// <see cref="FireProtection.Backend.Services.Placement.RevitSprinklerFamilySource.ResolveDeviceContext"/>
    /// and copied onto the per-row <see cref="PlacementRoomInput"/> by
    /// <see cref="FireProtection.Backend.Services.Placement.Sprinklers.Final.PlacementInputBuilder"/>.
    ///
    /// <para>
    /// It contains NO <c>Autodesk.Revit.DB</c> types — only strings, the plain
    /// <see cref="DevicePlacementBehavior"/> enum, and a boolean status flag.
    /// The calculation layer can read it without ever touching the Revit API.
    /// </para>
    /// </summary>
    public class DevicePlacementContext
    {
        [JsonProperty("familyName")]
        public string FamilyName { get; set; }

        [JsonProperty("typeName")]
        public string TypeName { get; set; }

        /// <summary>
        /// The PROVEN Revit's <c>FamilyPlacementType</c> value as a string
        /// (<c>"FaceBased"</c> / <c>"WorkPlaneBased"</c> / <c>"OneLevelBased"</c>).
        /// <c>null</c> when the family/type could not be resolved.
        /// </summary>
        [JsonProperty("familyPlacementType")]
        public string FamilyPlacementType { get; set; }

        /// <summary>
        /// Plain, Revit-free classification of the placement behavior required by the
        /// resolved family/type. Default <see cref="DevicePlacementBehavior.Unknown"/>
        /// when <see cref="Resolved"/> is <c>false</c>.
        /// </summary>
        [JsonProperty("placementBehavior")]
        public DevicePlacementBehavior PlacementBehavior { get; set; } = DevicePlacementBehavior.Unknown;

        /// <summary>
        /// <c>true</c> when the family/type was resolved and the resulting placement
        /// behavior is one of the supported values (i.e. not
        /// <see cref="DevicePlacementBehavior.Unsupported"/>).
        /// </summary>
        [JsonProperty("resolved")]
        public bool Resolved { get; set; }

        /// <summary>
        /// Human-readable failure reason when <see cref="Resolved"/> is <c>false</c>;
        /// <c>null</c> otherwise.
        /// </summary>
        [JsonProperty("failureReason")]
        public string FailureReason { get; set; }

        /// <summary>
        /// Mount signal read from the catalog's <c>Mount</c> column
        /// (e.g. <c>"Pendent"</c> / <c>"Sidewall"</c> / <c>"Upright"</c>). The
        /// resolver refines <see cref="PlacementBehavior"/> from the family-level
        /// bucket (FaceBased/WorkPlaneBased/OneLevelBased) into the mount-specific
        /// bucket (CeilingOverhead / WallSidewall) using this value. <c>null</c>
        /// when the catalog has no row for the (family, type) pair or no
        /// <c>Mount</c> value — in which case the resolver falls back to the
        /// family-level mapping.
        /// </summary>
        [JsonProperty("mount")]
        public string Mount { get; set; }
    }
}
