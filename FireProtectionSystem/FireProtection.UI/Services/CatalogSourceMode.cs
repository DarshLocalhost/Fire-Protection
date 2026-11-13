using System;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Where the family/type lists shown in the three device tabs come from.
    ///
    /// These are NOT interchangeable sources. The model is authoritative for what is loadable and
    /// for <c>FamilyPlacementType</c>; the workbook is the only carrier of per-type engineering
    /// values. In <see cref="RevitModel"/> the workbook is still consulted as an overlay when one is
    /// loaded, so a mode switch changes where NAMES come from, not whether engineering values exist.
    /// </summary>
    public enum CatalogSourceMode
    {
        /// <summary>Family/type lists read from the open Revit document. Default.</summary>
        RevitModel = 0,

        /// <summary>Family/type lists read from the user-selected Excel workbook.</summary>
        CatalogFile = 1
    }

    public static class CatalogSourceModes
    {
        /// <summary>
        /// Default when nothing has been persisted yet. The model is the default because it is the
        /// only source that cannot name a family the project cannot actually place, and because it
        /// avoids making every user maintain a workbook before the tool is usable.
        /// </summary>
        public const CatalogSourceMode Default = CatalogSourceMode.RevitModel;

        public static CatalogSourceMode Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Default;
            CatalogSourceMode parsed;
            if (System.Enum.TryParse(text.Trim(), true, out parsed)) return parsed;
            return Default;
        }

        public static string Label(CatalogSourceMode mode)
        {
            return mode == CatalogSourceMode.CatalogFile ? "Catalog file" : "Model";
        }
    }
}