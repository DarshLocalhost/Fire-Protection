using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using SPA.SynthFire.Core.Standards;

namespace SPA.SynthFire.UI.Views;

/// <summary>
/// Modal dialog to pick the design standard and hazard class before a run.
/// Populated from the <see cref="StandardRegistry"/>; the hazard list cascades
/// off the selected standard.
/// </summary>
public partial class PlacementOptionsWindow : Window
{
    /// <summary>The user's selection, or null if they cancelled.</summary>
    public PlacementOptions? Result { get; private set; }

    private readonly IReadOnlyDictionary<string, string>? _familyMap;

    private PlacementOptionsWindow(
        StandardRegistry registry, IReadOnlyList<FamilyTypeOption> families,
        string? defaultStandardId, IReadOnlyDictionary<string, string>? familyMap)
    {
        InitializeComponent();
        _familyMap = familyMap;

        foreach (var family in families)
            FamilyCombo.Items.Add(family);
        if (FamilyCombo.Items.Count > 0)
            FamilyCombo.SelectedIndex = 0;

        HazardCombo.SelectionChanged += (_, _) => PreselectMappedFamily();

        int selected = 0, i = 0;
        foreach (var standard in registry.All)
        {
            StandardCombo.Items.Add(standard);
            if (defaultStandardId is not null &&
                string.Equals(standard.Id, defaultStandardId, StringComparison.OrdinalIgnoreCase))
                selected = i;
            i++;
        }
        if (StandardCombo.Items.Count > 0)
            StandardCombo.SelectedIndex = selected;
    }

    /// <summary>Shows the dialog; returns the selection or null if cancelled.</summary>
    public static PlacementOptions? Prompt(
        StandardRegistry registry,
        IReadOnlyList<FamilyTypeOption> families,
        IntPtr ownerHandle = default,
        string? defaultStandardId = null,
        IReadOnlyDictionary<string, string>? familyMap = null)
    {
        var window = new PlacementOptionsWindow(registry, families, defaultStandardId, familyMap);
        if (ownerHandle != IntPtr.Zero) new WindowInteropHelper(window).Owner = ownerHandle;
        return window.ShowDialog() == true ? window.Result : null;
    }

    private void StandardCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        HazardCombo.Items.Clear();
        if (StandardCombo.SelectedItem is IDesignStandard standard)
        {
            foreach (var hazard in standard.SupportedHazardClasses)
                HazardCombo.Items.Add(hazard);
            if (HazardCombo.Items.Count > 0)
                HazardCombo.SelectedIndex = 0;
        }
    }

    /// <summary>If a family is mapped for the current standard+hazard, select it.</summary>
    private void PreselectMappedFamily()
    {
        if (_familyMap is null) return;
        if (StandardCombo.SelectedItem is not IDesignStandard standard) return;
        if (HazardCombo.SelectedItem is not HazardClassification hazard) return;

        string key = $"{standard.Id}|{hazard.Id}";
        if (!_familyMap.TryGetValue(key, out string? label) || string.IsNullOrEmpty(label)) return;

        foreach (var item in FamilyCombo.Items)
            if (item is FamilyTypeOption opt && opt.Label == label)
            {
                FamilyCombo.SelectedItem = item;
                return;
            }
    }

    private void Place_Click(object sender, RoutedEventArgs e)
    {
        if (FamilyCombo.SelectedItem is not FamilyTypeOption family)
        {
            MessageBox.Show(this, "Select a sprinkler family/type.", "SPA SynthFire");
            return;
        }
        if (StandardCombo.SelectedItem is not IDesignStandard standard ||
            HazardCombo.SelectedItem is not HazardClassification hazard)
        {
            MessageBox.Show(this, "Select a standard and a hazard class.", "SPA SynthFire");
            return;
        }

        Result = new PlacementOptions(standard.Id, hazard.Id, family.Id, family.Label, Mount: family.Mount);
        DialogResult = true;
    }
}
