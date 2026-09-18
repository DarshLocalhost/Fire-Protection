using System;
using Autodesk.Revit.DB;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;

namespace FireProtection.Backend.Services.Placement
{
    /// <summary>
    /// Plain, Revit-aware conversion from a Revit's <c>FamilyPlacementType</c> value to the
    /// Revit-free <see cref="DevicePlacementBehavior"/> enum.
    ///
    /// <para>
    /// This is the ONLY place where the mapping between Revit's <c>FamilyPlacementType</c>
    /// and the calculation-layer enum lives. The mapping is performed at the Revit-aware
    /// boundary (per Step 2 rule: "actual Revit <c>FamilyPlacementType</c> is resolved only
    /// in the Revit-aware boundary") and the result is a plain enum value that can cross
    /// into the calculation engine without dragging any <c>Autodesk.Revit.DB</c> types.
    /// </para>
    ///
    /// <para>
    /// Mapping (intentionally narrow; see Step 1 audit G-02 / G-16):
    /// </para>
    /// <list type="bullet">
    /// <item><c>FaceBased</c>          → <see cref="DevicePlacementBehavior.FaceHosted"/></item>
    /// <item><c>WorkPlaneBased</c>     → <see cref="DevicePlacementBehavior.WorkPlaneDependent"/></item>
    ///   (the audit confirmed: a WorkPlaneBased family can be a pendent on a ceiling face OR
    ///    a wall-sidewall mounted on a work plane. The fine-grained subdivision between
    ///    <see cref="DevicePlacementBehavior.CeilingOverhead"/> /
    ///    <see cref="DevicePlacementBehavior.WallSidewall"/> is performed by
    ///    <see cref="FromResolvedFamily"/> using the catalog's <c>Mount</c> column.)
    /// <item><c>OneLevelBased</c>      → <see cref="DevicePlacementBehavior.LevelHosted"/></item>
    /// <item>anything else / null     → <see cref="DevicePlacementBehavior.Unsupported"/></item>
    /// </list>
    /// </summary>
    internal static class DevicePlacementBehaviorResolver
    {
        /// <summary>
        /// Maps a Revit's <c>FamilyPlacementType</c> (read from the symbol on the Revit
        /// boundary) to the plain <see cref="DevicePlacementBehavior"/> consumed by the
        /// calculation layer. The mapping never throws; an unknown / null placement type
        /// becomes <see cref="DevicePlacementBehavior.Unsupported"/> (never silently
        /// downgraded to a default — per Step 2 error-handling rule).
        /// </summary>
        public static DevicePlacementBehavior FromFamilyPlacementType(FamilyPlacementType? placementType)
        {
            if (!placementType.HasValue) return DevicePlacementBehavior.Unsupported;
            return FromPlacementTypeString(placementType.Value.ToString());
        }

        /// <summary>
        /// String-keyed overload. The string is what is actually stored on the
        /// Revit-free DTO (<see cref="SelectedSprinklerInfo.FamilyPlacementType"/> and
        /// <see cref="PlacementRoomInput.SelectedSprinklerFamilyPlacementType"/>).
        /// </summary>
        public static DevicePlacementBehavior FromPlacementTypeString(string placementTypeName)
        {
            if (string.IsNullOrWhiteSpace(placementTypeName)) return DevicePlacementBehavior.Unsupported;

            // OrdinalIgnoreCase matches how RevitSprinklerPlacementService.cs and the
            // strategies already compare ("FaceBased", "WorkPlaneBased", "OneLevelBased").
            if (string.Equals(placementTypeName, "FaceBased", StringComparison.OrdinalIgnoreCase))
                return DevicePlacementBehavior.FaceHosted;

            if (string.Equals(placementTypeName, "WorkPlaneBased", StringComparison.OrdinalIgnoreCase))
                return DevicePlacementBehavior.WorkPlaneDependent;

            if (string.Equals(placementTypeName, "OneLevelBased", StringComparison.OrdinalIgnoreCase))
                return DevicePlacementBehavior.LevelHosted;

            return DevicePlacementBehavior.Unsupported;
        }

        /// <summary>
        /// Mount-aware refinement of the family-level bucket into the mount-specific
        /// bucket. A <c>WorkPlaneBased</c> family can host a pendent on a ceiling face
        /// OR a sidewall on a wall work plane; the catalog's <c>Mount</c> column
        /// disambiguates. Industry-standard mapping:
        ///
        /// <list type="bullet">
        /// <item><c>Mount</c> contains <c>"sidewall"</c>   → <see cref="DevicePlacementBehavior.WallSidewall"/></item>
        /// <item><c>Mount</c> contains <c>"pendent"</c> or <c>"upright"</c> → <see cref="DevicePlacementBehavior.CeilingOverhead"/></item>
        /// <item><c>Mount</c> empty / unknown / null → behavior is returned unchanged
        ///   (preserves the family-level bucket).</item>
        /// </list>
        ///
        /// The <paramref name="behavior"/> values that are NOT mount-dependent
        /// (<see cref="DevicePlacementBehavior.FaceHosted"/>,
        /// <see cref="DevicePlacementBehavior.LevelHosted"/>,
        /// <see cref="DevicePlacementBehavior.Unsupported"/>) are returned unchanged
        /// even when the mount signal is present — only
        /// <see cref="DevicePlacementBehavior.WorkPlaneDependent"/> is refined.
        /// </summary>
        public static DevicePlacementBehavior FromResolvedFamily(DevicePlacementBehavior behavior, string mount)
        {
            if (behavior != DevicePlacementBehavior.WorkPlaneDependent) return behavior;
            if (string.IsNullOrWhiteSpace(mount)) return behavior;

            string m = mount.Trim();
            if (m.IndexOf("sidewall", StringComparison.OrdinalIgnoreCase) >= 0)
                return DevicePlacementBehavior.WallSidewall;
            if (m.IndexOf("pendent", StringComparison.OrdinalIgnoreCase) >= 0)
                return DevicePlacementBehavior.CeilingOverhead;
            if (m.IndexOf("upright", StringComparison.OrdinalIgnoreCase) >= 0)
                return DevicePlacementBehavior.CeilingOverhead;

            // Unknown mount: keep the family-level bucket (WorkPlaneDependent).
            return behavior;
        }
    }
}
