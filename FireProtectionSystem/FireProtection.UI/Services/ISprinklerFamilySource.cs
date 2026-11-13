using FireProtection.UI.ViewModels.Sprinklers.BruteForce;
using System.Collections.Generic;

namespace FireProtection.UI.Services
{
    /// <summary>Outcome of loading a single .rfa into the active model.</summary>
    public enum FamilyLoadOutcome
    {
        /// <summary>The family was newly added to the model.</summary>
        Loaded,
        /// <summary>A family of this name was already in the model; nothing new was added.</summary>
        AlreadyPresent,
        /// <summary>The file could not be loaded as a family (bad path, corrupt/not an .rfa, no document).</summary>
        Failed
    }

    /// <summary>
    /// UI-side abstraction for obtaining loaded sprinkler families/types
    /// without leaking Revit API types into the UI project.
    /// </summary>
    public interface ISprinklerFamilySource
    {
        IReadOnlyList<SprinklerFamilyOption> GetAvailableFamilies();

        /// <summary>Loads a selected .rfa into the active model. Reports whether it was newly loaded,
        /// was already present, or failed (with <paramref name="error"/> set for the failure case).</summary>
        FamilyLoadOutcome TryLoadFamily(string familyFilePath, out string error);
    }
}