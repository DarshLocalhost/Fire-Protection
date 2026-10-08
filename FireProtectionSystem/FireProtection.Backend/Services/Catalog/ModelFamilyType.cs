using System;

namespace FireProtection.Backend.Services.Catalog
{
    /// <summary>
    /// A family/type pair read from the open model, plus the model-only placement-type string.
    ///
    /// Deliberately in a Revit-FREE file. The test harness references this type directly and must
    /// not need RevitAPI.dll, so it cannot live in a file that pulls in the Revit namespaces.
    /// </summary>
    public sealed class ModelFamilyType
    {
        public string FamilyName { get; set; }
        public string TypeName { get; set; }

        /// <summary>
        /// <c>FamilyPlacementType</c> as a string, or null when it could not be read. MODEL-ONLY
        /// information the Excel catalog cannot carry, and the value that decides how a device is
        /// hosted (face / work-plane / level).
        ///
        /// Exposed for reporting only. It is deliberately NOT surfaced through ICatalog: the Revit
        /// family sources already resolve it authoritatively at placement time, and duplicating
        /// that resolution here would create a second, divergent source of truth.
        /// </summary>
        public string FamilyPlacementType { get; set; }
    }
}