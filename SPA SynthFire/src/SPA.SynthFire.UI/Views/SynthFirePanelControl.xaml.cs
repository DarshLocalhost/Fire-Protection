using System;
using System.Windows;
using System.Windows.Controls;

namespace SPA.SynthFire.UI.Views;

/// <summary>
/// Dockable-panel home for SPA SynthFire: quick-launch buttons for every tool plus a
/// status line. It raises <see cref="ToolRequested"/> when a button is clicked; the
/// Revit host runs the matching tool inside a valid API context. Keeping the launch
/// off this control means the panel stays Revit-free and the modeless windows are
/// unchanged.
/// </summary>
public partial class SynthFirePanelControl : UserControl
{
    /// <summary>Raised with the chosen tool when the user clicks a launch button.</summary>
    public event Action<SynthFireTool>? ToolRequested;

    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(string), typeof(SynthFirePanelControl),
        new PropertyMetadata("Ready."));

    /// <summary>Status text shown in the panel's card (set by the host after a launch).</summary>
    public string Status
    {
        get => (string)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public SynthFirePanelControl() => InitializeComponent();

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse(tag, out SynthFireTool tool))
            ToolRequested?.Invoke(tool);
    }
}
