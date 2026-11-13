using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using SPA.SynthFire.Placement.Preview;
using SPA.SynthFire.UI.Controls;
using SPA.SynthFire.UI.Helpers;

namespace SPA.SynthFire.UI.Views;

/// <summary>
/// Read-only 3D coverage-plan viewer. Opened from the 2D <see cref="CoveragePlanWindow"/>
/// via the "3D View" button. Shares the same <see cref="LevelPreviewModel"/> data and
/// stays in sync when the 2D plan refreshes a room after a manual placement.
/// </summary>
public partial class CoveragePlan3DWindow : Window
{
    private readonly List<LevelPreviewModel> _models;
    private bool _ready;

    public CoveragePlanViewport3D Viewport { get; }

    static CoveragePlan3DWindow()
    {
        AssemblyResolver.EnsureRegistered();
    }

    public CoveragePlan3DWindow(IReadOnlyList<LevelPreviewModel> models, int initialIndex)
    {
        AssemblyResolver.EnsureRegistered();

        SourceInitialized += (_, _) =>
        {
            try
            {
                if (PresentationSource.FromVisual(this) is HwndSource src &&
                    src.CompositionTarget is HwndTarget target)
                {
                    target.RenderMode = RenderMode.SoftwareOnly;
                }
            }
            catch
            {
                // Non-fatal — continue with default render mode.
            }
        };

        InitializeComponent();

        // Dynamically instantiate the 3D Viewport AFTER assembly resolver is registered
        Viewport = new CoveragePlanViewport3D();
        ViewportHost.Children.Add(Viewport);

        _models = models.ToList();

        VolumesCheck.Checked += (_, _) => Viewport.ShowCoverageVolumes = true;
        VolumesCheck.Unchecked += (_, _) => Viewport.ShowCoverageVolumes = false;
        ResetCamBtn.Click += (_, _) => Viewport.ResetCamera();
        CloseBtn.Click += (_, _) => Close();

        LevelSelector.SelectionChanged += OnLevelSelectionChanged;
        LevelSelector.ItemsSource = _models;
        LevelSelector.Visibility = _models.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        int idx = _models.Count == 0 ? -1
            : Math.Min(Math.Max(initialIndex, 0), _models.Count - 1);
        LevelSelector.SelectedIndex = idx;

        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (!IsLoaded) return;
                _ready = true;
                if (LevelSelector.SelectedItem is LevelPreviewModel m)
                    ShowLevel(m, LevelSelector.SelectedIndex);
            }));
        };
    }

    public void SetRevitOwner(IntPtr ownerHandle)
    {
        if (ownerHandle == IntPtr.Zero) return;
        new WindowInteropHelper(this).Owner = ownerHandle;
    }

    private void OnLevelSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (LevelSelector.SelectedItem is LevelPreviewModel m)
            ShowLevel(m, LevelSelector.SelectedIndex);
    }

    internal void ShowLevel(LevelPreviewModel model, int index)
    {
        try
        {
            Viewport.Model = model;
            LevelNameText.Text = model.LevelName;
            TotalDevicesText.Text = $"Total devices: {model.TotalDevices}";
            TotalNonCompText.Text = $"Non-compliances: {model.TotalNonCompliances}";
            ShowRoom(null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CoveragePlan3DWindow] ShowLevel: {ex}");
        }
    }

    private void ShowRoom(RoomPreview? r)
    {
        SelName.Text = r?.Name ?? "(orbit to explore)";
        SelArea.Text = r is null ? "Floor area: —" : $"Floor area: {r.AreaSqFt:0} sq ft";
        SelDevices.Text = r is null ? "Devices: —" : $"Devices: {r.DeviceCount}";
        SelCoverage.Text = r is null ? "Coverage: —" : $"Coverage: {r.CoveragePercent:0.0}%";
        SelNonComp.Text = r is null ? "Non-compliances: —" : $"Non-compliances: {r.NonCompliances}";
    }

    internal void RefreshRoom(RoomPreview updated)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => RefreshRoom(updated));
            return;
        }
        if (!IsLoaded || !_ready) return;

        int i = LevelSelector.SelectedIndex;
        if (i < 0 || i >= _models.Count) return;

        _models[i] = _models[i].WithRoom(updated);
        Viewport.UpdateModelPreservingView(_models[i]);
        TotalDevicesText.Text = $"Total devices: {_models[i].TotalDevices}";
        TotalNonCompText.Text = $"Non-compliances: {_models[i].TotalNonCompliances}";
        ShowRoom(_models[i].Rooms.FirstOrDefault(r => r.RoomId == updated.RoomId));
    }
}