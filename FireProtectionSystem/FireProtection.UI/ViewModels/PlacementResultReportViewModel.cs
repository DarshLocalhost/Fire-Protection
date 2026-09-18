using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Common;

namespace FireProtection.UI.ViewModels
{
    public class PlacementResultReportViewModel : ObservableObject
    {
        private readonly PlacementRunReport _report;
        private readonly string _deviceDisplayName;

        public PlacementResultReportViewModel(PlacementRunReport report, string deviceDisplayName)
        {
            _report = report ?? new PlacementRunReport();
            _deviceDisplayName = deviceDisplayName ?? "Device";

            ReportTitle = _deviceDisplayName + " Placement Report";
            Summary = _report.Summary ?? "No summary available.";

            // Overall verdict + rule-set provenance - the "engineering-grade" honesty line the report
            // must carry: provisional/overridden rules are stated up front, never implied approved.
            string status = string.IsNullOrWhiteSpace(_report.OverallStatus)
                ? (_report.RoomsFailed > 0 ? "FAILURE" : _report.RoomsSkipped > 0 ? "PASS WITH SKIPS" : "PASS")
                : _report.OverallStatus;
            OverallStatusText = status.ToUpperInvariant();
            if (_report.IsProvisional)
                RulesLine = "PROVISIONAL RULES - engineering review required. "
                    + (_report.AppliedRulesSummary ?? string.Empty);
            else if (!string.IsNullOrWhiteSpace(_report.AppliedRulesSummary))
                RulesLine = "Rules: " + _report.AppliedRulesSummary;
            else
                RulesLine = "Rule set: not reported.";

            RoomsSucceeded = _report.RoomsSucceeded;
            RoomsFailed = _report.RoomsFailed;
            RoomsSkipped = _report.RoomsSkipped;
            ReviewRequiredCount = _report.ReviewRequiredCount;
            DevicesPlaced = _report.SprinklersPlaced;
            DevicesRequested = _report.SprinklersRequested;

            RoomReports = new ObservableCollection<PlacementRoomReportViewModel>();
            if (_report.RoomReports != null)
            {
                foreach (var room in _report.RoomReports)
                {
                    RoomReports.Add(new PlacementRoomReportViewModel(room));
                }
            }

            Issues = new ObservableCollection<IssueItemViewModel>();
            if (_report.FailedReasons != null)
            {
                foreach (var reason in _report.FailedReasons)
                {
                    Issues.Add(new IssueItemViewModel(reason, "Failure", "#FF4444"));
                }
            }
            if (_report.ReviewRequiredReasons != null)
            {
                foreach (var reason in _report.ReviewRequiredReasons)
                {
                    Issues.Add(new IssueItemViewModel(reason, "Review Required", "#FFA500"));
                }
            }
            // Skipped rooms are part of the issue list too - "why was nothing placed here" must be answerable.
            if (_report.RoomReports != null)
            {
                foreach (var room in _report.RoomReports)
                {
                    if (room != null && string.Equals(room.Status, "Skipped", StringComparison.OrdinalIgnoreCase))
                    {
                        Issues.Add(new IssueItemViewModel(
                            (room.RoomName ?? room.RoomId ?? "Room") + ": " + (room.Message ?? "Skipped."),
                            "Skipped", "#8A8F98"));
                    }
                }
            }
            if (_report.Warnings != null)
            {
                foreach (var warning in _report.Warnings)
                {
                    Issues.Add(new IssueItemViewModel(warning, "Warning", "#FFA500"));
                }
            }

            ExportJsonCommand = new RelayCommand(_ => ExportJson());
            ExportCsvCommand = new RelayCommand(_ => ExportCsv());
        }

        public string ReportTitle { get; }
        public string Summary { get; }
        public string OverallStatusText { get; }
        public string RulesLine { get; }
        public bool HasRulesLine => !string.IsNullOrWhiteSpace(RulesLine);
        public int RoomsSucceeded { get; }
        public int RoomsFailed { get; }
        public int RoomsSkipped { get; }
        public int ReviewRequiredCount { get; }
        public int DevicesPlaced { get; }
        public int DevicesRequested { get; }

        public ObservableCollection<PlacementRoomReportViewModel> RoomReports { get; }
        public ObservableCollection<IssueItemViewModel> Issues { get; }

        public ICommand ExportJsonCommand { get; }
        public ICommand ExportCsvCommand { get; }

        private void ExportJson()
        {
            try
            {
                string path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    _deviceDisplayName.Replace(" ", "_") + "_PlacementResult_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json");

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(_report, Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(path, json);
                MessageBox.Show("Exported to: " + path, "Export JSON", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed: " + ex.Message, "Export JSON", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportCsv()
        {
            try
            {
                string path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    _deviceDisplayName.Replace(" ", "_") + "_PlacementResult_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Room,Level,Status,Placed,Requested,Message");

                if (_report.RoomReports != null)
                {
                    foreach (var room in _report.RoomReports)
                    {
                        sb.AppendLine(string.Format("{0},{1},{2},{3},{4},{5}",
                            Escape(room.RoomName),
                            Escape(room.LevelName),
                            Escape(room.Status),
                            room.PointsPlaced,
                            room.PointsRequested,
                            Escape(room.Message)));
                    }
                }

                System.IO.File.WriteAllText(path, sb.ToString());
                MessageBox.Show("Exported to: " + path, "Export CSV", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed: " + ex.Message, "Export CSV", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }

    public class PlacementRoomReportViewModel
    {
        public PlacementRoomReportViewModel(PlacementRoomReport report)
        {
            RoomName = report.RoomName ?? report.RoomId ?? "Room";
            LevelName = report.LevelName ?? "";
            Status = report.Status ?? "Unknown";
            PointsPlaced = report.PointsPlaced;
            PointsRequested = report.PointsRequested;
            Message = report.Message ?? "";
        }

        public string RoomName { get; }
        public string LevelName { get; }
        public string Status { get; }
        public int PointsPlaced { get; }
        public int PointsRequested { get; }
        public string Message { get; }
    }

    public class IssueItemViewModel
    {
        public IssueItemViewModel(string message, string category, string severityColor)
        {
            Message = message;
            Category = category;
            SeverityColor = severityColor;
        }

        public string Message { get; }
        public string Category { get; }
        public string SeverityColor { get; }
    }
}
