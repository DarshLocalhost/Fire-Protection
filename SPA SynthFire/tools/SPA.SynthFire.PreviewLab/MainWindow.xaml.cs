using System;
using System.Collections.Generic;
using System.Windows;
using SPA.SynthFire.Core.Geometry;
using SPA.SynthFire.Placement;
using SPA.SynthFire.Placement.Preview;

namespace SPA.SynthFire.PreviewLab;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Plan.RoomSelected += (_, room) => ShowRoom(room);
        Plan.ViewChanged += (_, _) => ZoomText.Text = $"{Plan.ZoomPercent:0}%";
        ZoomInBtn.Click += (_, _) => Plan.ZoomIn();
        ZoomOutBtn.Click += (_, _) => Plan.ZoomOut();
        ResetBtn.Click += (_, _) => Plan.ResetView();
        CancelBtn.Click += (_, _) => Close();
        PlaceBtn.Click += (_, _) =>
            MessageBox.Show(this, "In the add-in this commits the previewed layout to Revit.",
                "Preview Lab", MessageBoxButton.OK, MessageBoxImage.Information);

        var level = SampleLevel();
        Plan.Model = level;
        LevelNameText.Text = level.LevelName;
        TotalDevicesText.Text = $"Total devices: {level.TotalDevices}";
        TotalNonCompText.Text = $"Non-compliances: {level.TotalNonCompliances}";
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

    // Canned floor: one well-gridded room, one under-provisioned (red gaps), one with a
    // beam obstruction (grey band), one flagged review-required (orange dots), and one true
    // L-shaped polygon room so the non-rectangular rendering + clipping is visible on screen.
    private static LevelPreviewModel SampleLevel()
    {
        // L-shaped room (feet): a 30x30 block with the top-right 15x15 quadrant removed.
        var lShape = new Polygon2D(new[]
        {
            new Point2D(60,  0), new Point2D(90,  0), new Point2D(90, 15),
            new Point2D(75, 15), new Point2D(75, 30), new Point2D(60, 30)
        });

        var rooms = new List<RoomPlacementResult>
        {
            Room("A", "Open Office",  new Rectangle2D(0, 0, 30, 24),  12, PlacementStatus.Placed,
                 Array.Empty<ObstacleBox>(), Array.Empty<string>()),
            Room("B", "Store Room",   new Rectangle2D(31, 0, 58, 24), 12, PlacementStatus.Placed,
                 Array.Empty<ObstacleBox>(), Array.Empty<string>(), single: true),
            Room("C", "Corridor",     new Rectangle2D(0, 25, 30, 49), 12, PlacementStatus.Placed,
                 new[] { new ObstacleBox(new Rectangle2D(12, 25, 18, 49), "Beam") }, Array.Empty<string>()),
            Room("D", "Plant Room",   new Rectangle2D(31, 25, 58, 49), 9, PlacementStatus.PlacedReviewRequired,
                 Array.Empty<ObstacleBox>(),
                 new[] { "Y spacing 9 ft is below the 10 ft minimum; review required." }),
            Room("E", "L-Shaped Hall", new Rectangle2D(60, 0, 90, 30), 12, PlacementStatus.Placed,
                 Array.Empty<ObstacleBox>(), Array.Empty<string>(), boundary: lShape),
        };

        var result = new PlacementResult
        {
            Rooms = rooms,
            StandardId = "NFPA13",
            HazardClassId = "LightHazard",
            ReviewRequired = true
        };
        return LevelPreviewBuilder.Build("GROUND FLOOR", result);
    }

    private static RoomPlacementResult Room(
        string id, string name, Rectangle2D f, double spacing, PlacementStatus status,
        IReadOnlyList<ObstacleBox> obstacles, IReadOnlyList<string> notes,
        bool single = false, Polygon2D? boundary = null)
    {
        var pts = single
            ? new List<SprinklerPoint> { new(f.CenterX, f.CenterY, 9) }
            : GridPoints(f, spacing);
        if (boundary is not null)
            pts = pts.FindAll(p => boundary.Contains(p.X, p.Y));
        return new RoomPlacementResult
        {
            RoomId = id,
            RoomName = name,
            Status = status,
            Sprinklers = pts,
            ActualMaxSpacing = spacing,
            Footprint = f,
            Boundary = boundary,
            Obstacles = obstacles,
            Notes = notes
        };
    }

    private static List<SprinklerPoint> GridPoints(Rectangle2D f, double spacing)
    {
        int nx = Math.Max(1, (int)Math.Round(f.Width / spacing, MidpointRounding.AwayFromZero));
        int ny = Math.Max(1, (int)Math.Round(f.Height / spacing, MidpointRounding.AwayFromZero));
        double sx = f.Width / nx, sy = f.Height / ny;
        var pts = new List<SprinklerPoint>(nx * ny);
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
                pts.Add(new SprinklerPoint(f.MinX + sx * (i + 0.5), f.MinY + sy * (j + 0.5), 9));
        return pts;
    }
}
