using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;

namespace SPA.SynthFire.UI.Views;

/// <summary>Bridge for creating rooms off the UI thread (ExternalEvent on the Revit side).</summary>
public interface IRoomCreationRunner
{
    void RequestCreate(IReadOnlyList<string> levelIds);
}

/// <summary>Modeless room-creation window; Revit stays interactive while it is open.</summary>
public partial class RoomCreationWindow : Window
{
    public IRoomCreationRunner? Runner { get; set; }

    public RoomCreationWindow(IReadOnlyList<NamedOption> levels)
    {
        InitializeComponent();
        foreach (var l in levels) LevelList.Items.Add(l);
        if (LevelList.Items.Count > 0) LevelList.SelectedIndex = 0;
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

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var ids = LevelList.SelectedItems.OfType<NamedOption>().Select(o => o.Id).ToList();
        if (ids.Count == 0) { SetStatus("Select at least one level."); return; }
        if (Runner is null) { SetStatus("Not connected to Revit."); return; }

        SetStatus("Creating rooms…");
        Runner.RequestCreate(ids);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
