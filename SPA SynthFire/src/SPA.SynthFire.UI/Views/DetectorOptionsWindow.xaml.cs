using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using SPA.SynthFire.Core.Catalog;

namespace SPA.SynthFire.UI.Views;

/// <summary>User selections for a detector run: family/type and detector kind.</summary>
public sealed record DetectorOptions(string FamilySymbolId, DetectorKind Kind);

/// <summary>Modal dialog to pick the detector family/type and kind (smoke/heat).</summary>
public partial class DetectorOptionsWindow : Window
{
    public DetectorOptions? Result { get; private set; }

    private DetectorOptionsWindow(IReadOnlyList<FamilyTypeOption> families)
    {
        InitializeComponent();

        foreach (var family in families)
            FamilyCombo.Items.Add(family);
        if (FamilyCombo.Items.Count > 0)
            FamilyCombo.SelectedIndex = 0;

        KindCombo.Items.Add(DetectorKind.Smoke);
        KindCombo.Items.Add(DetectorKind.Heat);
        KindCombo.SelectedIndex = 0;
    }

    public static DetectorOptions? Prompt(
        IReadOnlyList<FamilyTypeOption> families, IntPtr ownerHandle = default)
    {
        var window = new DetectorOptionsWindow(families);
        if (ownerHandle != IntPtr.Zero) new WindowInteropHelper(window).Owner = ownerHandle;
        return window.ShowDialog() == true ? window.Result : null;
    }

    private void Place_Click(object sender, RoutedEventArgs e)
    {
        if (FamilyCombo.SelectedItem is not FamilyTypeOption family)
        {
            MessageBox.Show(this, "Select a detector family/type.", "SPA SynthFire");
            return;
        }
        if (KindCombo.SelectedItem is not DetectorKind kind)
        {
            MessageBox.Show(this, "Select a detector kind.", "SPA SynthFire");
            return;
        }

        Result = new DetectorOptions(family.Id, kind);
        DialogResult = true;
    }
}
