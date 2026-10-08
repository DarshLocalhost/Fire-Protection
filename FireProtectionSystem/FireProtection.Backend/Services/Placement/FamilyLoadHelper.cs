using Autodesk.Revit.DB;
using FireProtection.UI.Services;
using System;
using System.IO;

namespace FireProtection.Backend.Services.Placement
{
    /// <summary>
    /// Shared, production-grade Revit family loading for the device "Load families" flows.
    /// <para>
    /// <c>Document.LoadFamily</c> is an unreliable success signal on its own: the 2-arg overload
    /// returns <c>false</c> whenever a same-named family already exists, and even the
    /// <see cref="IFamilyLoadOptions"/> overload returns <c>false</c> when the incoming family is
    /// byte-for-byte identical to the one in the model (nothing to overwrite). Neither is an error.
    /// Rather than trust that bool, we snapshot the model's family set before and after the call and
    /// decide from what actually changed: a new family name appeared =&gt; loaded; nothing changed
    /// =&gt; it was already present; an exception =&gt; the file is not a loadable family. This is
    /// independent of whether the .rfa's internal family name matches its file name.
    /// </para>
    /// <para>
    /// LoadFamily opens its own sub-transaction, so the caller must invoke this with NO transaction
    /// open (i.e. from a Revit API context on the idle UI thread), never from inside an open
    /// <see cref="Transaction"/>.
    /// </para>
    /// </summary>
    public static class FamilyLoadHelper
    {
        /// <summary>
        /// Loads <paramref name="familyFilePath"/> and reports whether it was newly loaded, was
        /// already present in the model, or could not be loaded (with <paramref name="error"/> set).
        /// </summary>
        public static FamilyLoadOutcome Load(Document document, string familyFilePath, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(familyFilePath)) { error = "No family file was selected."; return FamilyLoadOutcome.Failed; }
            if (!File.Exists(familyFilePath)) { error = "The selected file no longer exists."; return FamilyLoadOutcome.Failed; }
            if (document == null) { error = "The active Revit document is unavailable."; return FamilyLoadOutcome.Failed; }

            Document familyDocument = null;
            try
            {
                // Opening the source through the running Revit application is the same upgrade
                // boundary used by Revit's family import workflow. Revit upgrades older family
                // documents (for example 2019 -> 2025) in memory before loading them into the project.
                familyDocument = document.Application.OpenDocumentFile(familyFilePath);
                if (familyDocument == null || !familyDocument.IsFamilyDocument)
                {
                    error = "Revit could not open the selected file as a family document.";
                    return FamilyLoadOutcome.Failed;
                }

                Family sourceFamily = familyDocument.OwnerFamily;
                string sourceFamilyName = sourceFamily != null ? sourceFamily.Name : null;
                bool wasPresent = !string.IsNullOrWhiteSpace(sourceFamilyName)
                    && FindFamily(document, sourceFamilyName) != null;

                Family loadedFamily = familyDocument.LoadFamily(document, new OverwriteFamilyLoadOptions());
                if (loadedFamily != null)
                    return wasPresent ? FamilyLoadOutcome.AlreadyPresent : FamilyLoadOutcome.Loaded;

                // Revit can return null when the same internal family already exists and no update
                // was needed. This check uses the family name read from the opened RFA, never its
                // filename, so an unrelated/missing family cannot be reported as already present.
                if (wasPresent && FindFamily(document, sourceFamilyName) != null)
                    return FamilyLoadOutcome.AlreadyPresent;

                error = "Revit opened the family but did not load it into the active project. "
                    + "Check the family category, conflicts, and compatibility with the running Revit version.";
                return FamilyLoadOutcome.Failed;
            }
            catch (Exception ex)
            {
                error = "Revit could not upgrade or load this family for the running version: " + ex.Message;
                return FamilyLoadOutcome.Failed;
            }
            finally
            {
                if (familyDocument != null && familyDocument.IsValidObject)
                {
                    try { familyDocument.Close(false); }
                    catch { /* Preserve the load result; source document was opened read-only for import. */ }
                }
            }
        }

        /// <summary>
        /// Bool convenience wrapper for the placement pre-flight "Load family" buttons, where a family
        /// that is already present counts as success (it is in the model, which is all that matters).
        /// </summary>
        public static bool TryLoad(Document document, string familyFilePath, out string error)
        {
            return Load(document, familyFilePath, out error) != FamilyLoadOutcome.Failed;
        }

        private static Family FindFamily(Document document, string familyName)
        {
            if (document == null || string.IsNullOrWhiteSpace(familyName)) return null;
            foreach (Element element in new FilteredElementCollector(document).OfClass(typeof(Family)))
            {
                if (element is Family family
                    && string.Equals(family.Name, familyName, StringComparison.OrdinalIgnoreCase))
                    return family;
            }
            return null;
        }

        // When the family (or a shared nested family) is already present, reload it and refresh its
        // parameter values rather than aborting. This is what lets re-selecting the same .rfa succeed
        // instead of throwing; the before/after snapshot in Load() then classifies the result.
        private sealed class OverwriteFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(
                Family sharedFamily,
                bool familyInUse,
                out FamilySource source,
                out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }
    }
}
