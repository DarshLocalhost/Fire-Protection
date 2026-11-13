using System;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using SPA.SynthFire.Placement;

namespace SPA.SynthFire.UI.Views;

/// <summary>Read-only report of a completed placement run.</summary>
public partial class PlacementReportWindow : Window
{
    private readonly PlacementResult _result;
    private readonly string _deviceNoun;

    private PlacementReportWindow(PlacementResult result, string deviceNoun)
    {
        InitializeComponent();
        _result = result;
        _deviceNoun = deviceNoun;

        TitleText.Text = $"Placement Report — {result.StandardId} / {result.HazardClassId}";

        int rooms = result.Rooms.Count;
        int placed = result.Rooms.Count(r => r.Status != PlacementStatus.Skipped);
        int skipped = result.Rooms.Count(r => r.Status == PlacementStatus.Skipped);
        int levels = result.Rooms.Select(r => r.LevelName).Distinct().Count();

        var coverage = CoverageValidator.Validate(result)
            .ToDictionary(f => f.RoomId, f => f);
        int gaps = coverage.Values.Count(f => f.IsGap);

        SummaryText.Text =
            $"{result.TotalSprinklers} {deviceNoun} across {placed} of {rooms} rooms on {levels} level(s)" +
            (skipped > 0 ? $" ({skipped} skipped)" : "") +
            (gaps > 0 ? $" — {gaps} room(s) with coverage gaps." : ".");

        if (result.ReviewRequired || gaps > 0)
        {
            ReviewBanner.Visibility = Visibility.Visible;
            ReviewText.Text =
                (gaps > 0
                    ? $"{gaps} room(s) have coverage gaps (a spacing/area limit was exceeded or the room was skipped) — see the Coverage check column. "
                    : "") +
                "Engineering review required. Rule values are provisional (not FPE-verified) " +
                "and/or one or more rooms could not satisfy every spacing rule. Do not issue " +
                "this layout without a fire-protection engineer's sign-off.";
        }

        RoomsGrid.ItemsSource = result.Rooms
            .OrderBy(r => r.LevelName)
            .ThenBy(r => r.RoomName)
            .Select(r => new
            {
                r.LevelName,
                r.RoomName,
                Status = Describe(r.Status),
                r.Count,
                MaxSpacing = r.ActualMaxSpacing.ToString("0.##"),
                Coverage = r.ActualCoveragePerSprinkler.ToString("0.#"),
                CoverageCheck = coverage.TryGetValue(r.RoomId, out var f) ? f.Summary : "OK"
            }).ToList();
    }

    public static void ShowReport(PlacementResult result, IntPtr ownerHandle = default, string deviceNoun = "sprinklers")
    {
        var window = new PlacementReportWindow(result, deviceNoun);
        if (ownerHandle != IntPtr.Zero) new WindowInteropHelper(window).Owner = ownerHandle;
        window.ShowDialog();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export placement schedule",
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"SPA SynthFire {_deviceNoun} schedule.csv"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            System.IO.File.WriteAllText(
                dialog.FileName,
                PlacementScheduleExporter.ToCsv(_result, _deviceNoun),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            MessageBox.Show(this, $"Schedule exported to:\n{dialog.FileName}",
                "SPA SynthFire", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(this, "Could not export the schedule:\n" + ex.Message,
                "SPA SynthFire", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string Describe(PlacementStatus status) => status switch
    {
        PlacementStatus.Placed => "Placed",
        PlacementStatus.PlacedReviewRequired => "Review required",
        PlacementStatus.Skipped => "Skipped",
        _ => status.ToString()
    };
}
