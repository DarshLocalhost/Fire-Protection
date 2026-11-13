using System;
using Autodesk.Revit.UI;
using SPA.SynthFire.UI;
using SPA.SynthFire.UI.Views;

namespace SPA.SynthFire.Revit;

/// <summary>
/// Dockable "SPA SynthFire" panel: a persistent home docked in the Revit window that
/// launches every tool, alongside the existing ribbon buttons. Button clicks are
/// marshalled through an <see cref="ExternalEvent"/> so each tool opens inside a valid
/// Revit API context; the modeless design windows themselves are unchanged.
/// </summary>
public sealed class SynthFireDockablePane : IDockablePaneProvider
{
    /// <summary>Stable id for the pane (must never change once shipped).</summary>
    public static readonly DockablePaneId PaneId =
        new(new Guid("b7e1d3a2-6c44-4f1e-9a2b-3c5d7e9f0a11"));

    private readonly SynthFirePanelControl _control;
    private readonly ExternalEvent _event;
    private readonly LaunchHandler _handler;

    public SynthFireDockablePane()
    {
        _control = new SynthFirePanelControl();
        _handler = new LaunchHandler(_control);
        _event = ExternalEvent.Create(_handler);
        _control.ToolRequested += tool =>
        {
            _handler.Requested = tool;
            _event.Raise();
        };
    }

    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = _control;
        data.InitialState = new DockablePaneState
        {
            DockPosition = DockPosition.Right
        };
    }

    // Runs the requested tool in a valid API context, then reflects any message on the panel.
    private sealed class LaunchHandler : IExternalEventHandler
    {
        private readonly SynthFirePanelControl _control;
        public SynthFireTool? Requested;

        public LaunchHandler(SynthFirePanelControl control) => _control = control;

        public string GetName() => "SPA SynthFire — Launch Tool";

        public void Execute(UIApplication app)
        {
            if (Requested is not { } tool) return;
            string message = "";
            Result result = SynthFireLauncher.Launch(tool, app, ref message);
            string status = result == Result.Cancelled && message.Length > 0
                ? message
                : $"Opened: {tool}.";
            _control.Dispatcher.Invoke(() => _control.Status = status);
        }
    }
}
