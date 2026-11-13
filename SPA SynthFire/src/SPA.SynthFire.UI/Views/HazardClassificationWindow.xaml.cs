using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using SPA.SynthFire.Core.Standards;

namespace SPA.SynthFire.UI.Views;

/// <summary>Bridge for tagging rooms with per-room hazard classes off the UI thread.</summary>
public interface IHazardRunner
{
    /// <summary>Writes each room's chosen hazard class (roomId → hazardId) under the given standard.</summary>
    void RequestApply(string standardId, IReadOnlyDictionary<string, string> roomHazards);
}

/// <summary>
/// Modeless hazard-classification window. Every placed room is listed with an
/// auto-suggested hazard (from its name; no match → Light) that the user can
/// override per row before applying. Revit stays interactive while open.
/// </summary>
public partial class HazardClassificationWindow : Window
{
    private readonly StandardRegistry _registry;
    private readonly IReadOnlyList<HazardRoomSeed> _seeds;
    private readonly ObservableCollection<HazardRoomRow> _rows = new();

    public IHazardRunner? Runner { get; set; }

    public HazardClassificationWindow(
        StandardRegistry registry, string? defaultStandardId, IReadOnlyList<HazardRoomSeed> seeds)
    {
        InitializeComponent();
        _registry = registry;
        _seeds = seeds;
        RoomGrid.ItemsSource = _rows;

        int selected = 0, i = 0;
        foreach (var s in registry.All)
        {
            StandardCombo.Items.Add(s);
            if (defaultStandardId is not null &&
                string.Equals(s.Id, defaultStandardId, StringComparison.OrdinalIgnoreCase)) selected = i;
            i++;
        }
        if (StandardCombo.Items.Count > 0) StandardCombo.SelectedIndex = selected; // triggers row build

        if (_seeds.Count == 0)
            SetStatus("No placed rooms found. Create or open rooms first, then reopen this window.");
    }

    public void SetStatus(string message)
    {
        if (Dispatcher.CheckAccess()) StatusText.Text = message;
        else Dispatcher.Invoke(() => StatusText.Text = message);
    }

    public void SetOwner(IntPtr revitHandle)
    {
        if (revitHandle != IntPtr.Zero) new WindowInteropHelper(this).Owner = revitHandle;
    }

    private void StandardCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => BuildRows();

    /// <summary>Rebuilds every row against the selected standard, defaulting each to its auto-suggestion.</summary>
    private void BuildRows()
    {
        _rows.Clear();
        if (StandardCombo.SelectedItem is not IDesignStandard standard) return;

        var options = standard.SupportedHazardClasses;
        foreach (var seed in _seeds)
        {
            string suggestedId = RoomHazardClassifier.MapToStandardHazardId(standard, seed.SuggestedTier);
            HazardClassification suggested =
                options.FirstOrDefault(h => h.Id == suggestedId) ?? options.First();
            HazardClassification? existing = seed.ExistingHazardId is null ? null
                : options.FirstOrDefault(h => string.Equals(h.Id, seed.ExistingHazardId, StringComparison.OrdinalIgnoreCase));

            _rows.Add(new HazardRoomRow(seed, options, suggested, existing));
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        BuildRows();
        SetStatus("Reset every room to its auto-suggested hazard.");
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (StandardCombo.SelectedItem is not IDesignStandard standard)
        {
            SetStatus("Select a standard."); return;
        }
        if (Runner is null) { SetStatus("Not connected to Revit."); return; }
        if (_rows.Count == 0) { SetStatus("No rooms to classify."); return; }

        RoomGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RoomGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var map = _rows
            .Where(r => r.Assigned is not null)
            .ToDictionary(r => r.RoomId, r => r.Assigned.Id);

        SetStatus($"Applying hazard classification to {map.Count} room(s)…");
        Runner.RequestApply(standard.Id, map);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
