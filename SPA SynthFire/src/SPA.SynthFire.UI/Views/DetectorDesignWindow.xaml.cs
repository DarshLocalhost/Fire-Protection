using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using SPA.SynthFire.UI.ViewModels;

namespace SPA.SynthFire.UI.Views;

/// <summary>
/// Modeless fire-detection window; Revit stays interactive while it is open.
/// A thin view over <see cref="DetectorDesignViewModel"/> (state + place command live
/// in the view model); this code-behind only owns window-specific concerns.
/// </summary>
public partial class DetectorDesignWindow : Window
{
    private readonly DetectorDesignViewModel _vm;

    public DetectorDesignWindow(IReadOnlyList<FamilyTypeOption> families)
    {
        InitializeComponent();
        _vm = new DetectorDesignViewModel(families);
        DataContext = _vm;
    }

    /// <summary>Set by the Revit side after construction (breaks the ctor cycle).</summary>
    public IDetectorRunner? Runner
    {
        get => _vm.Runner;
        set => _vm.Runner = value;
    }

    /// <summary>Thread-safe status update (called from the Revit event handler).</summary>
    public void SetStatus(string message)
    {
        if (Dispatcher.CheckAccess()) _vm.Status = message;
        else Dispatcher.Invoke(() => _vm.Status = message);
    }

    public void SetOwner(IntPtr revitHandle)
    {
        if (revitHandle != IntPtr.Zero) new WindowInteropHelper(this).Owner = revitHandle;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
