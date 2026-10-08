using System;
using Autodesk.Revit.DB;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;

namespace FireProtection.Backend.Services.Placement
{
    /// <summary>
    /// Plain, Revit-aware conversion from Revit's <c>FamilyPlacementType</c> value to the
    /// Revit-free <see cref="DevicePlacementBehavior"/> enum.
    /// </summary>
    internal static class DevicePlacementBehaviorResolver
    {
        public static DevicePlacementBehavior FromFamilyPlacementType(FamilyPlacementType? placementType)
        {
            if (!placementType.HasValue) return DevicePlacementBehavior.Unsupported;
            return FromPlacementTypeString(placementType.Value.ToString());
        }

        public static DevicePlacementBehavior FromPlacementTypeString(string placementTypeName)
        {
            if (string.IsNullOrWhiteSpace(placementTypeName)) return DevicePlacementBehavior.Unsupported;

            if (string.Equals(placementTypeName, "FaceBased", StringComparison.OrdinalIgnoreCase))
                return DevicePlacementBehavior.FaceHosted;

            if (string.Equals(placementTypeName, "WorkPlaneBased", StringComparison.OrdinalIgnoreCase))
                return DevicePlacementBehavior.WorkPlaneDependent;

            if (string.Equals(placementTypeName, "OneLevelBased", StringComparison.OrdinalIgnoreCase))
                return DevicePlacementBehavior.LevelHosted;

            return DevicePlacementBehavior.Unsupported;
        }

        /// <summary>
        /// Mount-aware refinement of the family-level bucket into the mount-specific bucket.
        /// Refines FaceHosted, WorkPlaneDependent, and LevelHosted into WallSidewall or CeilingOverhead.
        /// </summary>
        public static DevicePlacementBehavior FromResolvedFamily(DevicePlacementBehavior behavior, string mount)
        {
            if (behavior == DevicePlacementBehavior.Unsupported) return behavior;

            if (string.IsNullOrWhiteSpace(mount)) return behavior;

            string m = mount.Trim();
            if (m.IndexOf("sidewall", StringComparison.OrdinalIgnoreCase) >= 0)
                return DevicePlacementBehavior.WallSidewall;

            if (m.IndexOf("pendent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("upright", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return DevicePlacementBehavior.CeilingOverhead;
            }

            return behavior;
        }

        /// <summary>
        /// Fallback mount inference directly from family and type name strings when catalog is not loaded.
        /// </summary>
        public static string InferMountFromNames(string familyName, string typeName)
        {
            string combined = ((familyName ?? "") + " " + (typeName ?? "")).Trim();
            if (string.IsNullOrWhiteSpace(combined)) return null;

            if (combined.IndexOf("sidewall", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Sidewall";
            if (combined.IndexOf("pendent", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Pendent";
            if (combined.IndexOf("upright", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Upright";

            return null;
        }
    }
}