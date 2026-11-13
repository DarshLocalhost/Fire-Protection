using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using SPA.SynthFire.Core.Devices;
using SPA.SynthFire.Core.Geometry;
using SPA.SynthFire.Placement;
using SPA.SynthFire.Placement.Preview;

// Alias to disambiguate from SPA.SynthFire.Core.Geometry.Point3D.
using WpfPoint3D = System.Windows.Media.Media3D.Point3D;

namespace SPA.SynthFire.UI.Controls;

/// <summary>
/// Helix-based 3D viewport that renders a whole-level coverage plan.
/// Coordinate mapping: Revit (X, Y, Z) → WPF 3D (X, Z, Y).
/// </summary>
public sealed class CoveragePlanViewport3D : HelixViewport3D
{
    // Brighter, higher-alpha palette so SoftwareOnly / weak lighting still reads.
    private static readonly Color UncoveredColor = Color.FromArgb(220, 230, 70, 70);
    private static readonly Color SingleColor = Color.FromArgb(200, 90, 200, 110);
    private static readonly Color OverlapColor = Color.FromArgb(220, 50, 180, 90);
    private static readonly Color ObstructedColor = Color.FromArgb(200, 160, 160, 165);
    private static readonly Color WallColor = Color.FromArgb(110, 186, 196, 210);
    private static readonly Color FloorSlabColor = Color.FromArgb(90, 55, 62, 72);
    private static readonly Color DeviceColor = Color.FromRgb(80, 170, 255);
    private static readonly Color DeviceReviewColor = Color.FromRgb(255, 170, 50);
    private static readonly Color ObstacleColor = Color.FromArgb(180, 110, 118, 130);
    private static readonly Color CoverageVolumeColor = Color.FromArgb(45, 100, 210, 140);

    private LevelPreviewModel? _model;
    private bool _showCoverageVolumes;
    private bool _sceneDirty;
    private bool _preserveCameraOnNextBuild;

    public CoveragePlanViewport3D()
    {
        Background = new SolidColorBrush(Color.FromRgb(28, 32, 38));

        // Camera / interaction — explicit so orbit/zoom/pan always work.
        ZoomExtentsWhenLoaded = false;
        ShowCoordinateSystem = false;
        ShowViewCube = true;
        ViewCubeOpacity = 0.85;
        IsInertiaEnabled = true;
        IsRotationEnabled = true;
        IsZoomEnabled = true;
        IsPanEnabled = true;
        IsMoveEnabled = true;
        RotateAroundMouseDownPoint = true;
        ZoomAroundMouseDownPoint = true;
        ShowCameraInfo = false;
        ShowFrameRate = false;
        Orthographic = false;

        Camera = new PerspectiveCamera
        {
            Position = new WpfPoint3D(40, 60, 40),
            LookDirection = new Vector3D(-40, -50, -40),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 50,
            NearPlaneDistance = 0.05,
            FarPlaneDistance = 10000
        };

        // Stronger lighting than DefaultLights alone (SoftwareOnly is dim).
        Children.Add(new DefaultLights());
        Children.Add(new ModelVisual3D
        {
            Content = new AmbientLight(Color.FromRgb(80, 80, 85))
        });
        Children.Add(new ModelVisual3D
        {
            Content = new DirectionalLight(Color.FromRgb(255, 255, 250), new Vector3D(-0.6, -1.0, -0.4))
        });
        Children.Add(new ModelVisual3D
        {
            Content = new DirectionalLight(Color.FromRgb(140, 150, 170), new Vector3D(0.5, -0.2, 0.7))
        });

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_sceneDirty)
            RebuildSceneCore();
    }

    public LevelPreviewModel? Model
    {
        get => _model;
        set
        {
            _model = value;
            _preserveCameraOnNextBuild = false;
            ScheduleRebuild();
        }
    }

    public bool ShowCoverageVolumes
    {
        get => _showCoverageVolumes;
        set
        {
            if (_showCoverageVolumes == value) return;
            _showCoverageVolumes = value;
            _preserveCameraOnNextBuild = true;
            ScheduleRebuild();
        }
    }

    public void UpdateModelPreservingView(LevelPreviewModel? model)
    {
        _model = model;
        _preserveCameraOnNextBuild = true;
        ScheduleRebuild();
    }

    public void ResetCamera()
    {
        if (!IsLoaded) return;
        try
        {
            ZoomExtents();
        }
        catch
        {
            // Helix can throw on degenerate bounds
        }
    }

    // Revit (X, Y, Z) → WPF 3D (X, Z-up→Y, Y→Z).
    private static WpfPoint3D To3D(double rx, double ry, double rz) => new(rx, rz, ry);

    private void ScheduleRebuild()
    {
        _sceneDirty = true;
        if (!IsLoaded) return;
        RebuildSceneCore();
    }

    private void RebuildSceneCore()
    {
        _sceneDirty = false;

        try
        {
            // Keep lights; remove previous geometry only.
            foreach (var m in Children.OfType<ModelVisual3D>()
                         .Where(v => v.Content is Model3DGroup)
                         .ToList())
            {
                Children.Remove(m);
            }

            if (_model is null || _model.Rooms.Count == 0)
                return;

            var root = new Model3DGroup();
            foreach (var room in _model.Rooms)
                AddRoom(root, room);

            Children.Add(new ModelVisual3D { Content = root });

            if (!_preserveCameraOnNextBuild)
            {
                // Let layout settle, then frame the model.
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    try { ZoomExtents(); }
                    catch { /* ignore */ }
                }));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CoveragePlanViewport3D] Rebuild failed: {ex}");
        }
        finally
        {
            _preserveCameraOnNextBuild = false;
        }
    }

    private void AddRoom(Model3DGroup root, RoomPreview room)
    {
        double floorZ = room.FloorElevation;
        double ceilZ = room.CeilingElevation;
        if (double.IsNaN(floorZ) || double.IsInfinity(floorZ)) floorZ = 0;
        if (double.IsNaN(ceilZ) || double.IsInfinity(ceilZ) || ceilZ <= floorZ)
            ceilZ = floorZ + 9.0;

        AddFloorSlab(root, room, floorZ);
        AddCoverageTiles(root, room, floorZ);
        AddWalls(root, room, floorZ, ceilZ);
        AddDevices(root, room);
        AddObstructions(root, room, floorZ, ceilZ);

        if (_showCoverageVolumes)
            AddCoverageVolumes(root, room);
    }

    // ── Subtle floor slab under heatmap (gives rooms a solid base) ──────────

    private static void AddFloorSlab(Model3DGroup root, RoomPreview room, double floorZ)
    {
        var f = room.Footprint;
        if (f.Width <= 0 || f.Height <= 0) return;

        var builder = new MeshBuilder(false, false);
        var centre = To3D(
            (f.MinX + f.MaxX) / 2,
            (f.MinY + f.MaxY) / 2,
            floorZ - 0.05);
        builder.AddBox(centre, f.Width, 0.08, f.Height);
        AddMesh(root, builder, FloorSlabColor);
    }

    // ── Coverage heatmap (floor tiles) ──────────────────────────────────────

    private static void AddCoverageTiles(Model3DGroup root, RoomPreview room, double floorZ)
    {
        var builders = new Dictionary<CoverageCellKind, MeshBuilder>
        {
            [CoverageCellKind.Uncovered] = new MeshBuilder(false, false),
            [CoverageCellKind.Single] = new MeshBuilder(false, false),
            [CoverageCellKind.Overlap] = new MeshBuilder(false, false),
            [CoverageCellKind.Obstructed] = new MeshBuilder(false, false)
        };

        const double tileH = 0.06;
        // Slight inset so tiles don't z-fight each other / walls.
        const double inset = 0.02;

        foreach (var c in room.Cells)
        {
            if (!builders.TryGetValue(c.Kind, out var b)) continue;
            if (c.Size <= 0 || double.IsNaN(c.Size) ||
                double.IsNaN(c.CenterX) || double.IsNaN(c.CenterY))
                continue;

            double s = Math.Max(c.Size - inset, 0.05);
            var centre = To3D(c.CenterX, c.CenterY, floorZ + tileH / 2);
            b.AddBox(centre, s, tileH, s);
        }

        AddMesh(root, builders[CoverageCellKind.Uncovered], UncoveredColor, emissive: true);
        AddMesh(root, builders[CoverageCellKind.Single], SingleColor, emissive: true);
        AddMesh(root, builders[CoverageCellKind.Overlap], OverlapColor, emissive: true);
        AddMesh(root, builders[CoverageCellKind.Obstructed], ObstructedColor, emissive: true);
    }

    // ── Room walls (extruded boundary) ──────────────────────────────────────

    private static void AddWalls(Model3DGroup root, RoomPreview room, double floorZ, double ceilZ)
    {
        IReadOnlyList<Point2D> verts;
        if (room.Boundary is { } poly && poly.Vertices.Count >= 3)
            verts = poly.Vertices;
        else
        {
            var f = room.Footprint;
            if (f.Width <= 0 || f.Height <= 0) return;
            verts = new[]
            {
                new Point2D(f.MinX, f.MinY), new Point2D(f.MaxX, f.MinY),
                new Point2D(f.MaxX, f.MaxY), new Point2D(f.MinX, f.MaxY)
            };
        }

        var builder = new MeshBuilder(false, false);
        for (int i = 0; i < verts.Count; i++)
        {
            var a = verts[i];
            var b = verts[(i + 1) % verts.Count];
            var p0 = To3D(a.X, a.Y, floorZ);
            var p1 = To3D(b.X, b.Y, floorZ);
            var p2 = To3D(b.X, b.Y, ceilZ);
            var p3 = To3D(a.X, a.Y, ceilZ);
            builder.AddQuad(p0, p1, p2, p3);
        }

        AddMesh(root, builder, WallColor, doubleSided: true);
    }

    // ── Device markers ──────────────────────────────────────────────────────

    private static void AddDevices(Model3DGroup root, RoomPreview room)
    {
        var normalBuilder = new MeshBuilder(false, false);
        var reviewBuilder = new MeshBuilder(false, false);
        const double r = 0.55; // larger so they read while orbiting

        foreach (var d in room.Devices)
        {
            double z = d.Z > 0 ? d.Z : room.CeilingElevation;
            if (double.IsNaN(z) || double.IsInfinity(z)) z = room.FloorElevation + 8;
            var centre = To3D(d.X, d.Y, z);
            var target = room.Status == RoomPreviewStatus.ReviewRequired ? reviewBuilder : normalBuilder;
            target.AddSphere(centre, r, 12, 10);

            // Small stem down to floor so height is obvious.
            var floorPt = To3D(d.X, d.Y, room.FloorElevation);
            target.AddCylinder(floorPt, centre, 0.06, 6);

            if (d.Mount == DeviceMount.Sidewall && d.OrientationRadians is { } theta)
            {
                double throwLen = 3.0;
                var tip = To3D(
                    d.X + Math.Cos(theta) * throwLen,
                    d.Y + Math.Sin(theta) * throwLen,
                    z);
                target.AddCone(centre, tip, r * 0.7, true, 10);
            }
        }

        AddMesh(root, normalBuilder, DeviceColor, emissive: true);
        AddMesh(root, reviewBuilder, DeviceReviewColor, emissive: true);
    }

    // ── Obstruction boxes ───────────────────────────────────────────────────

    private static void AddObstructions(Model3DGroup root, RoomPreview room, double floorZ, double ceilZ)
    {
        if (room.Obstacles.Count == 0) return;
        var builder = new MeshBuilder(false, false);

        foreach (var o in room.Obstacles)
        {
            double minZ = o.MinZ > 0 ? o.MinZ : floorZ;
            double maxZ = o.MaxZ > 0 ? o.MaxZ : ceilZ;
            double h = maxZ - minZ;
            if (h <= 0) h = 1.0;
            if (o.Bounds.Width <= 0 || o.Bounds.Height <= 0) continue;

            var centre = To3D(
                (o.Bounds.MinX + o.Bounds.MaxX) / 2,
                (o.Bounds.MinY + o.Bounds.MaxY) / 2,
                (minZ + maxZ) / 2);
            builder.AddBox(centre, o.Bounds.Width, h, o.Bounds.Height);
        }

        AddMesh(root, builder, ObstacleColor);
    }

    // ── Coverage volumes (optional) ─────────────────────────────────────────

    private static void AddCoverageVolumes(Model3DGroup root, RoomPreview room)
    {
        var builder = new MeshBuilder(false, false);

        foreach (var d in room.Devices)
        {
            double z = d.Z > 0 ? d.Z : room.CeilingElevation;
            var centre = To3D(d.X, d.Y, z);

            if (d.Mount == DeviceMount.Sidewall && d.OrientationRadians is { } theta)
            {
                double throwFt = room.ThrowFt > 0 ? room.ThrowFt : 10;
                double halfW = room.HalfWidthFt > 0 ? room.HalfWidthFt : throwFt / 2;
                double midX = d.X + Math.Cos(theta) * throwFt / 2;
                double midY = d.Y + Math.Sin(theta) * throwFt / 2;
                var mid = To3D(midX, midY, z);
                builder.AddBox(mid, throwFt, 0.6, halfW * 2);
            }
            else
            {
                double radius = room.CoverageRadiusFt > 0 ? room.CoverageRadiusFt : 6;
                // Hemisphere-ish: full sphere is fine for read-only viz
                builder.AddSphere(centre, radius, 16, 12);
            }
        }

        AddMesh(root, builder, CoverageVolumeColor, doubleSided: true);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void AddMesh(
        Model3DGroup root,
        MeshBuilder builder,
        Color color,
        bool doubleSided = false,
        bool emissive = false)
    {
        if (builder.Positions.Count == 0) return;

        var mesh = builder.ToMesh();
        if (mesh.Positions.Count == 0) return;
        mesh.Freeze();

        var material = CreateMaterial(color, emissive);
        var geo = new GeometryModel3D(mesh, material)
        {
            BackMaterial = doubleSided ? material : null
        };
        geo.Freeze();
        root.Children.Add(geo);
    }

    /// <summary>
    /// Diffuse + soft emissive so colours stay readable under SoftwareOnly lighting.
    /// </summary>
    private static Material CreateMaterial(Color color, bool emissive)
    {
        var group = new MaterialGroup();

        var diffuseBrush = new SolidColorBrush(color);
        diffuseBrush.Freeze();
        group.Children.Add(new DiffuseMaterial(diffuseBrush));

        if (emissive)
        {
            // Pull colour through even when lights are weak.
            byte ea = (byte)Math.Max(40, color.A / 3);
            var emBrush = new SolidColorBrush(Color.FromArgb(ea, color.R, color.G, color.B));
            emBrush.Freeze();
            group.Children.Add(new EmissiveMaterial(emBrush));
        }

        // Mild specular highlight helps depth perception while orbiting.
        group.Children.Add(new SpecularMaterial(
            new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 40));

        group.Freeze();
        return group;
    }
}