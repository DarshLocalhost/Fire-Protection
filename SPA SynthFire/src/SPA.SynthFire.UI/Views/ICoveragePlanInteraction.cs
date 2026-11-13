namespace SPA.SynthFire.UI.Views;

/// <summary>
/// Editing hooks the coverage plan calls into when it is opened interactively. Implemented by
/// the Revit runner: each method queues its model op onto an ExternalEvent (a valid Revit API
/// context), performs the write/delete, then refreshes the affected room on the plan.
///
/// Manual placements are the source of truth: add / undo / redo / click-delete all mutate an
/// ordered list of manual heads and rebuild the room deterministically from its base snapshot,
/// so the plan and the model stay in step without ever re-running the solver.
/// </summary>
public interface ICoveragePlanInteraction
{
    /// <summary>Right-click a gap: place one device at the plan point inside the given room.</summary>
    void PlaceAt(string roomId, double x, double y);

    /// <summary>Left-click a manual head: remove the tracked device nearest the point (no-op on auto heads).</summary>
    void RemoveNear(string roomId, double x, double y);

    /// <summary>Undo the most recent manual placement (deletes its element from the model).</summary>
    void Undo();

    /// <summary>Re-place the most recently undone manual head.</summary>
    void Redo();
}
