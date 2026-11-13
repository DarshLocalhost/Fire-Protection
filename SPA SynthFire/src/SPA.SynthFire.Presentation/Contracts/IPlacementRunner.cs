namespace SPA.SynthFire.UI;

/// <summary>
/// Bridge the modeless design window uses to run a placement without blocking Revit.
/// The Revit side implements this by raising an ExternalEvent, so the actual API
/// work happens in a valid Revit context while the window stays open and Revit
/// remains fully interactive.
/// </summary>
public interface IPlacementRunner
{
    /// <summary>Queue a placement run for the given options (non-blocking).</summary>
    void RequestPlace(PlacementOptions options);

    /// <summary>
    /// Queue a dry-run solve and open the 2D coverage plan for the given options
    /// (non-blocking, no model writes). The window shows rooms as true polygons with the
    /// green/red/overlap coverage heatmap.
    /// </summary>
    void RequestPlan(PlacementOptions options);
}
