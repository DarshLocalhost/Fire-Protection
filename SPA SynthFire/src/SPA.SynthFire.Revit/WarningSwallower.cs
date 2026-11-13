using Autodesk.Revit.DB;

namespace SPA.SynthFire.Revit;

/// <summary>
/// Swallows non-fatal warnings raised during automated placement — notably Revit's
/// "None of the created elements are visible in view" warning that fires when a
/// hosted device's schedule level differs from the active view. Errors are left to
/// Revit's normal handling so real problems still surface.
/// </summary>
public sealed class WarningSwallower : IFailuresPreprocessor
{
    public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
    {
        foreach (FailureMessageAccessor failure in accessor.GetFailureMessages())
        {
            if (failure.GetSeverity() == FailureSeverity.Warning)
                accessor.DeleteWarning(failure);
        }
        return FailureProcessingResult.Continue;
    }

    /// <summary>Attaches the swallower to a transaction's failure handling options.</summary>
    public static void AttachTo(Transaction tx)
    {
        FailureHandlingOptions opts = tx.GetFailureHandlingOptions();
        opts.SetFailuresPreprocessor(new WarningSwallower());
        opts.SetClearAfterRollback(true);
        tx.SetFailureHandlingOptions(opts);
    }
}
