using System;
using System.Collections.Generic;
using System.Linq;
using SPA.SynthFire.Placement;
using SPA.SynthFire.Placement.Preview;
using SPA.SynthFire.UI.Controls;
using SPA.SynthFire.UI.Views;

namespace SPA.SynthFire.Revit;

/// <summary>
/// Builds the Revit-free coverage-plan models for EVERY level in a solve result and opens the
/// <see cref="CoveragePlanWindow"/> with a level picker. Shared by the sprinkler and detector
/// runners so both design windows get the same "View coverage plan" behaviour. Dry run only —
/// nothing is written to the model by the plan itself (manual click-to-place writes via the runner).
/// </summary>
internal static class CoveragePlanPresenter
{
    /// <summary>Read-only modal plan (no click-to-place).</summary>
    public static void Show(PlacementResult result, IntPtr owner, string deviceKind, Action<string> setStatus)
    {
        if (!TryBuild(result, deviceKind, setStatus, out var models, out int initial)) return;
        CoveragePlanWindow.ShowPlan(models, initial, owner);
    }

    /// <summary>
    /// Interactive MODELESS plan: right-clicking a gap / clicking a head raises the given
    /// <paramref name="interaction"/> so the runner can add / remove / undo / redo and refresh the
    /// affected room. Returns the open window (null if nothing to plot), so the caller can call
    /// <see cref="CoveragePlanWindow.RefreshRoom"/> after each edit.
    /// </summary>
    public static CoveragePlanWindow? ShowInteractive(
        PlacementResult result, IntPtr owner, string deviceKind, Action<string> setStatus,
        ICoveragePlanInteraction interaction)
    {
        if (!TryBuild(result, deviceKind, setStatus, out var models, out int initial)) return null;
        return CoveragePlanWindow.ShowInteractive(models, initial, owner, interaction);
    }

    private static bool TryBuild(
        PlacementResult result, string deviceKind, Action<string> setStatus,
        out List<LevelPreviewModel> models, out int initial)
    {
        models = new List<LevelPreviewModel>();
        initial = 0;

        // One group per level, listed alphabetically so the picker order is predictable.
        var byLevel = result.Rooms
            .GroupBy(r => string.IsNullOrWhiteSpace(r.LevelName) ? "Level" : r.LevelName)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (byLevel.Count == 0) { setStatus("No rooms to plot."); return false; }

        models = byLevel
            .Select(g => LevelPreviewBuilder.Build(
                g.Key,
                new PlacementResult
                {
                    Rooms = g.ToList(),
                    StandardId = result.StandardId,
                    HazardClassId = result.HazardClassId,
                    ReviewRequired = g.Any(r => r.ReviewRequired)
                },
                deviceKind))
            .ToList();

        // Default the picker to the level with the most rooms (usually what the user wants first).
        for (int i = 1; i < byLevel.Count; i++)
            if (byLevel[i].Count() > byLevel[initial].Count()) initial = i;

        var shown = models[initial];
        setStatus(
            $"Coverage plan — {shown.LevelName}: {shown.TotalDevices} device(s), " +
            $"{shown.TotalNonCompliances} non-compliance(s). " +
            (byLevel.Count > 1 ? $"{byLevel.Count} levels — pick one from the list. " : "") +
            "Right-click a gap to add a device. Provisional — review before issue.");
        return true;
    }
}
