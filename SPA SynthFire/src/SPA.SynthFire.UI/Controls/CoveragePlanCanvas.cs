using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SPA.SynthFire.Core.Devices;
using SPA.SynthFire.Core.Geometry;
using SPA.SynthFire.Placement;
using SPA.SynthFire.Placement.Preview;

namespace SPA.SynthFire.UI.Controls;

/// <summary>
/// Reusable, self-contained WPF control that renders a whole-level coverage plan
/// (Tandm-style): room footprints, obstruction boxes, the green/red/overlap coverage
/// field and device dots, with mouse-wheel zoom, drag-pan and click-to-select. Driven
/// purely by a Revit-free <see cref="LevelPreviewModel"/>, so it renders identically in
/// the standalone preview lab and inside the add-in's design window.
///
/// Coverage colours follow the requested scheme (functional data colours, not the app
/// theme): red = uncovered gap, light green (~0.5 alpha) = covered by one device,
/// stronger green (~0.8 alpha) = covered by two or more, grey = obstructed floor.
/// </summary>
public sealed class CoveragePlanCanvas : FrameworkElement
{
    private static readonly Brush UncoveredBrush   = Frozen(Color.FromArgb(95, 220, 55, 55));
    private static readonly Brush SingleBrush       = Frozen(Color.FromArgb(128, 120, 200, 120));
    private static readonly Brush OverlapBrush       = Frozen(Color.FromArgb(204, 70, 170, 80));
    private static readonly Brush ObstructedBrush   = Frozen(Color.FromArgb(150, 150, 150, 150));
    private static readonly Brush BackgroundBrush   = Frozen(Color.FromRgb(30, 34, 40));
    private static readonly Brush ObstacleBrush      = Frozen(Color.FromArgb(190, 96, 102, 112));
    private static readonly Brush DeviceBrush         = Frozen(Color.FromRgb(70, 140, 235));
    private static readonly Brush DeviceReviewBrush = Frozen(Color.FromRgb(240, 150, 40));
    private static readonly Pen RoomPen       = FrozenPen(Color.FromRgb(210, 214, 220), 1.4);
    private static readonly Pen ObstaclePen   = FrozenPen(Color.FromRgb(55, 60, 66), 1.0);
    private static readonly Pen SelectedPen   = FrozenPen(Color.FromRgb(90, 170, 255), 2.6);
    private static readonly Pen DevicePen     = FrozenPen(Colors.White, 0.6);

    private LevelPreviewModel? _model;
    private double _zoom = 1.0;      // user zoom multiplier on top of the fit-to-view scale
    private Vector _pan;             // screen-space pan offset (px)
    private bool _dragging;
    private bool _moved;
    private Point _lastMouse;
    private RoomPreview? _selected;

    public event EventHandler<RoomPreview?>? RoomSelected;
    public event EventHandler? ViewChanged;

    /// <summary>Raised when the user right-clicks inside a room while <see cref="PlacementEnabled"/>
    /// is on, asking for a device to be placed at that plan point (Phase 3 click-to-place).</summary>
    public event EventHandler<PlaceRequest>? PlacePointRequested;

    /// <summary>Raised when the user left-clicks (no drag) directly on a device dot while
    /// <see cref="PlacementEnabled"/> is on, asking for that device to be removed. Carries the
    /// device's plan point; the runner only removes it if it is a tracked manual head.</summary>
    public event EventHandler<PlaceRequest>? DeviceRemoveRequested;

    /// <summary>When true, right-clicking inside a room raises <see cref="PlacePointRequested"/>.</summary>
    public bool PlacementEnabled { get; set; }

    /// <summary>A request to place a device at a plan point inside a room.</summary>
    public sealed record PlaceRequest(RoomPreview Room, double X, double Y);

    public CoveragePlanCanvas() { Focusable = true; ClipToBounds = true; }

    public LevelPreviewModel? Model
    {
        get => _model;
        set { _model = value; _selected = null; ResetView(); RaiseSelected(); }
    }

    /// <summary>Swaps the model but keeps the current zoom/pan and re-resolves the selected room
    /// by id — used to refresh after a manual placement without yanking the view.</summary>
    public void UpdateModelPreservingView(LevelPreviewModel? model)
    {
        string? selId = _selected?.RoomId;
        _model = model;
        _selected = selId is null || model is null ? null
            : model.Rooms.FirstOrDefault(r => r.RoomId == selId);
        InvalidateVisual();
        RaiseSelected();
    }

    public RoomPreview? SelectedRoom => _selected;
    public double ZoomPercent => _zoom * 100.0;

    public void ResetView()
    {
        _zoom = 1.0;
        _pan = new Vector(0, 0);
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomIn() => ApplyZoom(1.25, new Point(ActualWidth / 2, ActualHeight / 2));
    public void ZoomOut() => ApplyZoom(1 / 1.25, new Point(ActualWidth / 2, ActualHeight / 2));

    private void RaiseSelected() => RoomSelected?.Invoke(this, _selected);

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Pen FrozenPen(Color c, double w)
    {
        var p = new Pen(Frozen(c), w);
        p.Freeze();
        return p;
    }

    private bool TryTransform(out double scale, out double ox, out double oy, out Rectangle2D b)
    {
        scale = 1; ox = 0; oy = 0; b = default;
        if (_model is null) return false;
        b = _model.Bounds;
        if (b.Width <= 0 || b.Height <= 0) return false;
        const double margin = 24;
        double fit = Math.Min(Math.Max(1, ActualWidth - margin * 2) / b.Width,
                              Math.Max(1, ActualHeight - margin * 2) / b.Height);
        scale = fit * _zoom;
        ox = (ActualWidth - b.Width * scale) / 2 + _pan.X;
        oy = (ActualHeight - b.Height * scale) / 2 + _pan.Y;
        return true;
    }

    private static Point W2S(double wx, double wy, double scale, double ox, double oy, Rectangle2D b)
        => new(ox + (wx - b.MinX) * scale, oy + (b.MaxY - wy) * scale);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (!TryTransform(out double scale, out double ox, out double oy, out var b)) return;

        foreach (var room in _model!.Rooms)
        {
            foreach (var c in room.Cells)
            {
                Brush? brush = c.Kind switch
                {
                    CoverageCellKind.Uncovered => UncoveredBrush,
                    CoverageCellKind.Single => SingleBrush,
                    CoverageCellKind.Overlap => OverlapBrush,
                    CoverageCellKind.Obstructed => ObstructedBrush,
                    _ => null
                };
                if (brush is null) continue;
                var tl = W2S(c.CenterX - c.Size / 2, c.CenterY + c.Size / 2, scale, ox, oy, b);
                double s = c.Size * scale + 0.6;
                dc.DrawRectangle(brush, null, new Rect(tl.X, tl.Y, s, s));
            }
            DrawRoomOverlay(dc, room, scale, ox, oy, b);
        }
    }

    private void DrawRoomOverlay(DrawingContext dc, RoomPreview room, double scale, double ox, double oy, Rectangle2D b)
    {
        var pen = ReferenceEquals(room, _selected) ? SelectedPen : RoomPen;
        if (room.Boundary is { } poly && poly.Vertices.Count >= 3)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                var v0 = poly.Vertices[0];
                g.BeginFigure(W2S(v0.X, v0.Y, scale, ox, oy, b), isFilled: false, isClosed: true);
                for (int i = 1; i < poly.Vertices.Count; i++)
                {
                    var v = poly.Vertices[i];
                    g.LineTo(W2S(v.X, v.Y, scale, ox, oy, b), isStroked: true, isSmoothJoin: false);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }
        else
        {
            var f = room.Footprint;
            if (f.Width > 0 && f.Height > 0)
            {
                var tl = W2S(f.MinX, f.MaxY, scale, ox, oy, b);
                dc.DrawRectangle(null, pen, new Rect(tl.X, tl.Y, f.Width * scale, f.Height * scale));
            }
        }
        foreach (var o in room.Obstacles)
        {
            var ob = o.Bounds;
            var tl = W2S(ob.MinX, ob.MaxY, scale, ox, oy, b);
            dc.DrawRectangle(ObstacleBrush, ObstaclePen, new Rect(tl.X, tl.Y, ob.Width * scale, ob.Height * scale));
        }
        var dot = room.Status == RoomPreviewStatus.ReviewRequired ? DeviceReviewBrush : DeviceBrush;
        foreach (var d in room.Devices)
        {
            var c = W2S(d.X, d.Y, scale, ox, oy, b);
            // Draw a sidewall head as a wedge pointing the way it throws across the room, so the
            // plan reads like the directional device it is; symmetric mounts stay a plain dot.
            if (d.Mount == DeviceMount.Sidewall && d.OrientationRadians is { } theta)
                DrawThrowWedge(dc, c, theta, dot);
            else
                dc.DrawEllipse(dot, DevicePen, c, 3.2, 3.2);
        }
    }

    // A small filled triangle whose apex points along the throw direction (world +X = 0, +Y up;
    // screen Y is inverted, so the y-component is negated).
    private static void DrawThrowWedge(DrawingContext dc, Point c, double theta, Brush fill)
    {
        double fx = Math.Cos(theta), fy = -Math.Sin(theta);
        double px = -fy, py = fx;
        const double len = 9.0, half = 4.5;
        var apex = new Point(c.X + fx * len, c.Y + fy * len);
        var b1 = new Point(c.X + px * half, c.Y + py * half);
        var b2 = new Point(c.X - px * half, c.Y - py * half);
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(apex, isFilled: true, isClosed: true);
            g.LineTo(b1, isStroked: true, isSmoothJoin: false);
            g.LineTo(b2, isStroked: true, isSmoothJoin: false);
        }
        geo.Freeze();
        dc.DrawGeometry(fill, DevicePen, geo);
    }

    private void ApplyZoom(double factor, Point center)
    {
        if (!TryTransform(out double scale, out double ox, out double oy, out var b)) return;
        double wx = b.MinX + (center.X - ox) / scale;
        double wy = b.MaxY - (center.Y - oy) / scale;
        double oldZoom = _zoom;
        _zoom = Math.Clamp(_zoom * factor, 0.1, 40.0);
        double newScale = scale / oldZoom * _zoom;
        double obx = (ActualWidth - b.Width * newScale) / 2;
        double oby = (ActualHeight - b.Height * newScale) / 2;
        _pan = new Vector(center.X - obx - (wx - b.MinX) * newScale,
                          center.Y - oby - (b.MaxY - wy) * newScale);
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ApplyZoom(e.Delta > 0 ? 1.15 : 1 / 1.15, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragging = true;
        _moved = false;
        _lastMouse = e.GetPosition(this);
        CaptureMouse();
        Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(this);
        var delta = p - _lastMouse;
        _lastMouse = p;
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) > 2) _moved = true;
        _pan += delta;
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragging) { _dragging = false; ReleaseMouseCapture(); }
        if (_moved) return;

        // In edit mode a click directly on a device dot asks to remove it (the runner ignores
        // it unless it is a tracked manual head); otherwise fall back to room selection.
        var pos = e.GetPosition(this);
        if (PlacementEnabled && TryHitDevice(pos, out var room, out double dx, out double dy))
        {
            DeviceRemoveRequested?.Invoke(this, new PlaceRequest(room!, dx, dy));
            return;
        }
        SelectAt(pos);
    }

    // Nearest device dot within ~6 px of the click, in screen space. Returns its room and the
    // device's PLAN point (so the runner can match it against its tracked manual heads).
    private bool TryHitDevice(Point screen, out RoomPreview? room, out double x, out double y)
    {
        room = null; x = 0; y = 0;
        if (_model is null || !TryTransform(out double scale, out double ox, out double oy, out var b)) return false;
        const double tolPx = 6.0;
        double bestD2 = tolPx * tolPx;
        bool found = false;
        foreach (var r in _model.Rooms)
            foreach (var d in r.Devices)
            {
                var s = W2S(d.X, d.Y, scale, ox, oy, b);
                double ddx = s.X - screen.X, ddy = s.Y - screen.Y;
                double d2 = ddx * ddx + ddy * ddy;
                if (d2 <= bestD2) { bestD2 = d2; room = r; x = d.X; y = d.Y; found = true; }
            }
        return found;
    }

    private void SelectAt(Point screen)
    {
        if (_model is null || !TryTransform(out double scale, out double ox, out double oy, out var b)) return;
        double wx = b.MinX + (screen.X - ox) / scale;
        double wy = b.MaxY - (screen.Y - oy) / scale;
        RoomPreview? hit = null;
        foreach (var r in _model.Rooms)
        {
            bool inside = r.Boundary is { } poly ? poly.Contains(wx, wy) : r.Footprint.Contains(wx, wy);
            if (inside) { hit = r; break; }
        }
        if (!ReferenceEquals(hit, _selected)) { _selected = hit; InvalidateVisual(); RaiseSelected(); }
    }

    // Right-click asks to place a device at the clicked plan point (when enabled). The point is
    // resolved to the room it falls in so the Revit side can host to that room's ceiling/wall.
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (!PlacementEnabled || _model is null) return;
        if (!TryTransform(out double scale, out double ox, out double oy, out var b)) return;

        double wx = b.MinX + (e.GetPosition(this).X - ox) / scale;
        double wy = b.MaxY - (e.GetPosition(this).Y - oy) / scale;
        foreach (var r in _model.Rooms)
        {
            bool inside = r.Boundary is { } poly ? poly.Contains(wx, wy) : r.Footprint.Contains(wx, wy);
            if (inside)
            {
                PlacePointRequested?.Invoke(this, new PlaceRequest(r, wx, wy));
                e.Handled = true;
                return;
            }
        }
    }
}

