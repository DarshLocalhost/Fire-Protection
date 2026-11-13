using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop; 
using SPA.SynthFire.Core.Standards;
using SPA.SynthFire.UI.ViewModels;

namespace SPA.SynthFire.UI.Views;

/// <summary>
/// Modeless sprinkler-design window. Because it is shown non-modally and runs
/// placement through an <see cref="IPlacementRunner"/> (ExternalEvent on the Revit
/// side), Revit stays fully interactive while it is open. A thin view over
/// <see cref="SprinklerDesignViewModel"/>, which owns the state and place command.
/// </summary>
public partial class SprinklerDesignWindow : Window
{
    private readonly SprinklerDesignViewModel _vm;

    public SprinklerDesignWindow(
        StandardRegistry registry,
        IReadOnlyList<FamilyTypeOption> families,
        string? defaultStandardId,
        IReadOnlyDictionary<string, string>? familyMap)
    {
        InitializeComponent();
        _vm = new SprinklerDesignViewModel(registry, families, defaultStandardId, familyMap);
        DataContext = _vm;
    }

    /// <summary>Set by the Revit side after construction (breaks the ctor cycle).</summary>
    public IPlacementRunner? Runner
    {
        get => _vm.Runner;
        set => _vm.Runner = value;
    }

    public bool ClearPrevious => _vm.ClearPrevious;

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
