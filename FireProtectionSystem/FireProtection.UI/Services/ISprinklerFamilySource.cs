using FireProtection.UI.ViewModels.Sprinklers.BruteForce;
using System.Collections.Generic;

namespace FireProtection.UI.Services
{
    /// <summary>
    /// UI-side abstraction for obtaining loaded sprinkler families/types
    /// without leaking Revit API types into the UI project.
    /// </summary>
    public interface ISprinklerFamilySource
    {
        IReadOnlyList<SprinklerFamilyOption> GetAvailableFamilies();
    }
}