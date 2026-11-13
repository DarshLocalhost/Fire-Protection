using System;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// Contract to re-run extraction on the active Revit document and refresh the UI data in-place.
    /// Implemented in FireProtection.Backend.
    /// </summary>
    public interface IModelRefreshService
    {
        /// <summary>
        /// Re-extracts model data from the active document and returns updated JSON snapshot string.
        /// Must be called within a valid Revit API context.
        /// </summary>
        string RefreshModelSnapshot();
    }
}
