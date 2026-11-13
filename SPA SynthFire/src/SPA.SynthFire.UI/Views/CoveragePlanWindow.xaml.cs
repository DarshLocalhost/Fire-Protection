using SPA.SynthFire.Placement.Preview;
using SPA.SynthFire.UI.Controls;
using SPA.SynthFire.UI.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;

namespace SPA.SynthFire.UI.Views;

/// <summary>
/// Whole-level coverage plan: hosts the reusable <c>CoveragePlanCanvas</c> with a
/// details/legend side panel and a level picker. Opened from a design window (via a preview
/// solve) so the user can see rooms as true polygons with the green/red/overlap coverage
/// heatmap before placing, and switch between levels.
///
/// Read-only by default (a viewer). When opened with a place callback it becomes an
/// interactive click-to-place surface: right-clicking a gap raises the callback, and
/// <see cref="RefreshRoom"/> updates a single room in place after the device is written.
/// </summary>
public partial class CoveragePlanWindow : Window
{
    static CoveragePlanWindow()
    {
        AssemblyResolver.EnsureRegistered();
    }
    private readonly List<LevelPreviewModel> _models;
    private ICoveragePlanInteraction? _interaction;
    private CoveragePlan3DWindow? _plan3D;

    private CoveragePlanWindow(IReadOnlyList<LevelPreviewModel> models, int initialIndex)
    {
        InitializeComponent();
        _models = models.ToList();

        Plan.RoomSelected += (_, room) => ShowRoom(room);
        Plan.ViewChanged += (_, _) => ZoomText.Text = $"{Plan.ZoomPercent:0}%";
        Plan.PlacePointRequested += (_, req) => _interaction?.PlaceAt(req.Room.RoomId, req.X, req.Y);
        Plan.DeviceRemoveRequested += (_, req) => _interaction?.RemoveNear(req.Room.RoomId, req.X, req.Y);
        ZoomInBtn.Click += (_, _) => Plan.ZoomIn();
        ZoomOutBtn.Click += (_, _) => Plan.ZoomOut();
        ResetBtn.Click += (_, _) => Plan.ResetView();
        UndoBtn.Click += (_, _) => _interaction?.Undo();
        RedoBtn.Click += (_, _) => _interaction?.Redo();
        OkBtn.Click += (_, _) => Close();
        CloseBtn.Click += (_, _) => Close();
        View3DBtn.Click += (_, _) => Open3DView();

        LevelSelector.SelectionChanged += (_, _) =>
        {
            ShowLevel(LevelSelector.SelectedItem as LevelPreviewModel);
            Sync3DLevel();
        };
        LevelSelector.ItemsSource = _models;
        LevelSelector.Visibility = _models.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        LevelSelector.SelectedIndex = _models.Count == 0 ? -1
            : Math.Min(Math.Max(initialIndex, 0), _models.Count - 1);
    }

    private void ShowLevel(LevelPreviewModel? model)
    {
        Plan.Model = model;
        LevelNameText.Text = model?.LevelName ?? "—";
        TotalDevicesText.Text = $"Total devices: {model?.TotalDevices ?? 0}";
        TotalNonCompText.Text = $"Non-compliances: {model?.TotalNonCompliances ?? 0}";
        ShowRoom(null);
        Plan.ResetView();
        ZoomText.Text = $"{Plan.ZoomPercent:0}%";
    }

    private void ShowRoom(RoomPreview? r)
    {
        SelName.Text = r?.Name ?? "(click a room)";
        SelArea.Text = r is null ? "Floor area: —" : $"Floor area: {r.AreaSqFt:0} sq ft";
        SelDevices.Text = r is null ? "Devices: —" : $"Devices: {r.DeviceCount}";
        SelCoverage.Text = r is null ? "Coverage: —" : $"Coverage: {r.CoveragePercent:0.0}%";
        SelNonComp.Text = r is null ? "Non-compliances: —" : $"Non-compliances: {r.NonCompliances}";
    }

    /// <summary>The room currently shown, matched by id — for the caller to overlay a manual head.</summary>
    public RoomPreview? FindRoom(string roomId) =>
        (Plan.Model ?? _models.ElementAtOrDefault(LevelSelector.SelectedIndex))?
            .Rooms.FirstOrDefault(r => r.RoomId == roomId);

    /// <summary>Every room across every level, as first built (no manual heads).</summary>
    public IReadOnlyList<RoomPreview> AllRooms => _models.SelectMany(m => m.Rooms).ToList();

    /// <summary>Enables/disables the Undo/Redo toolbar buttons.</summary>
    public void SetHistoryState(bool canUndo, bool canRedo)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => SetHistoryState(canUndo, canRedo)); return; }
        UndoBtn.IsEnabled = canUndo;
        RedoBtn.IsEnabled = canRedo;
    }

    /// <summary>Replaces one room in the current level with an updated copy and re-renders.</summary>
    public void RefreshRoom(RoomPreview updated)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => RefreshRoom(updated)); return; }
        if (!IsLoaded) return;

        int i = LevelSelector.SelectedIndex;
        if (i < 0 || i >= _models.Count) return;
        _models[i] = _models[i].WithRoom(updated);
        Plan.UpdateModelPreservingView(_models[i]);
        Plan.UpdateLayout();
        TotalDevicesText.Text = $"Total devices: {_models[i].TotalDevices}";
        TotalNonCompText.Text = $"Non-compliances: {_models[i].TotalNonCompliances}";
        ShowRoom(_models[i].Rooms.FirstOrDefault(r => r.RoomId == updated.RoomId));
        Sync3D(updated);
    }

    // ── 3D view sync ────────────────────────────────────────────────────────

    private void Open3DView()
    {
        try
        {
            AssemblyResolver.EnsureRegistered();

            if (_plan3D is { IsLoaded: true })
            {
                _plan3D.Activate();
                return;
            }

            int i = LevelSelector.SelectedIndex;
            if (i < 0 || i >= _models.Count) return;

            _plan3D = new CoveragePlan3DWindow(_models, i);
            _plan3D.Closed += (_, _) => _plan3D = null;
            var owner = new WindowInteropHelper(this).Owner;
            if (owner != IntPtr.Zero) new WindowInteropHelper(_plan3D).Owner = owner;
            _plan3D.Show();
        }
        catch (Exception ex)
        {
            _plan3D = null;
            MessageBox.Show(
                this,
                "Unable to open the 3D coverage view.\n\n" + ex.Message,
                "SPA SynthFire",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Sync3D(RoomPreview updated) => _plan3D?.RefreshRoom(updated);

    private void Sync3DLevel()
    {
        if (_plan3D is null) return;
        int i = LevelSelector.SelectedIndex;
        if (i >= 0 && i < _models.Count) _plan3D.ShowLevel(_models[i], i);
    }

    // ── Static openers ──────────────────────────────────────────────────────

    /// <summary>Shows a set of levels modally with a level picker.</summary>
    public static void ShowPlan(IReadOnlyList<LevelPreviewModel> models, int initialIndex = 0, IntPtr ownerHandle = default)
    {
        if (models.Count == 0) return;
        var window = new CoveragePlanWindow(models, initialIndex);
        if (ownerHandle != IntPtr.Zero) new WindowInteropHelper(window).Owner = ownerHandle;
        window.ShowDialog();
    }

    /// <summary>Shows a single level modally.</summary>
    public static void ShowPlan(LevelPreviewModel model, IntPtr ownerHandle = default) =>
        ShowPlan(new[] { model }, 0, ownerHandle);

    /// <summary>Shows the plan MODELESS with click-to-place/edit enabled.</summary>
    public static CoveragePlanWindow ShowInteractive(
        IReadOnlyList<LevelPreviewModel> models, int initialIndex,
        IntPtr ownerHandle, ICoveragePlanInteraction interaction)
    {
        var window = new CoveragePlanWindow(models, initialIndex);
        window._interaction = interaction;
        window.Plan.PlacementEnabled = true;
        window.PlaceHint.Visibility = Visibility.Visible;
        window.UndoBtn.Visibility = Visibility.Visible;
        window.RedoBtn.Visibility = Visibility.Visible;
        if (ownerHandle != IntPtr.Zero) new WindowInteropHelper(window).Owner = ownerHandle;
        window.Show();
        return window;
    }
}