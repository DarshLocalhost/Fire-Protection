using System.Collections.Generic;
using System.Text;
using FireProtection.UI.Models.Sprinklers.BruteForce;

namespace FireProtection.Backend.Models.Placement.SmokeDetectors.Final
{
    /// <summary>
    /// Root NFPA 72 smoke detector calculation result covering every selected room.
    /// </summary>
    public class SmokeDetectorCalculationResult
    {
        public bool Success { get; set; }

        public List<SmokeDetectorRoomCalculationResult> Rooms { get; set; }

        public int TotalCalculatedDetectors { get; set; }

        public List<string> Warnings { get; set; }

        public List<string> Errors { get; set; }

        public string AppliedRulesSummary { get; set; }

        public bool IsProvisional { get; set; }

        public SmokeDetectorCalculationResult()
        {
            Rooms = new List<SmokeDetectorRoomCalculationResult>();
            Warnings = new List<string>();
            Errors = new List<string>();
        }

        public string SummaryText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("NFPA 72 Smoke Detector Calculation");
            sb.AppendLine("----------------------------------");
            sb.AppendLine("Overall success : " + Success);
            sb.AppendLine("Provisional rules: " + IsProvisional);
            sb.AppendLine("Rooms processed : " + Rooms.Count);
            sb.AppendLine("Total detectors : " + TotalCalculatedDetectors);
            sb.AppendLine("Warnings        : " + Warnings.Count);
            sb.AppendLine("Errors          : " + Errors.Count);
            if (!string.IsNullOrEmpty(AppliedRulesSummary))
            {
                sb.AppendLine();
                sb.AppendLine("Applied rules:");
                sb.AppendLine("  " + AppliedRulesSummary);
            }

            sb.AppendLine();
            sb.AppendLine("Per-room:");
            foreach (SmokeDetectorRoomCalculationResult room in Rooms)
            {
                sb.AppendLine(
                    "  [" + room.Status + "] " + room.RoomName + " (" + room.RoomNumber + ") " +
                    "-> " + room.CalculatedCount + " detector(s)");
                foreach (string warning in room.Warnings)
                {
                    sb.AppendLine("      warn: " + warning);
                }
                foreach (string error in room.Errors)
                {
                    sb.AppendLine("      err : " + error);
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Per-room smoke detector calculation outcome.
    /// </summary>
    public class SmokeDetectorRoomCalculationResult
    {
        public string RoomId { get; set; }
        public string RoomName { get; set; }
        public string RoomNumber { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }

        public CalculationStatus Status { get; set; }

        public bool IsSuccessful =>
            Status == CalculationStatus.Success || Status == CalculationStatus.ReviewRequired;

        public int CalculatedCount { get; set; }
        public int RequiredCount { get; set; }

        public double AppliedMaxSpacingFt { get; set; }
        public double AppliedBoundaryClearanceFt { get; set; }

        public List<CalculatedSmokeDetectorPoint> Points { get; set; }
        public List<double[]> Polygon { get; set; }

        public List<string> Warnings { get; set; }
        public List<string> Errors { get; set; }
        public List<string> Diagnostics { get; set; }

        public SmokeDetectorRoomCalculationResult()
        {
            Status = CalculationStatus.Success;
            Points = new List<CalculatedSmokeDetectorPoint>();
            Polygon = new List<double[]>();
            Warnings = new List<string>();
            Errors = new List<string>();
            Diagnostics = new List<string>();
        }
    }

    /// <summary>
    /// A selected smoke detector placement point in host MEP coordinates (feet).
    /// </summary>
    public class CalculatedSmokeDetectorPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public string RoomId { get; set; }
        public string LevelId { get; set; }
        public string LevelName { get; set; }

        /// <summary>Ceiling or Wall.</summary>
        public string Mount { get; set; }

        /// <summary>Polygon edge index for wall-mounted points; null for ceiling grid.</summary>
        public int? WallEdgeIndex { get; set; }

        public CalculatedSmokeDetectorPoint()
        {
            Mount = "Ceiling";
        }

        public override string ToString()
        {
            return "(" + X.ToString("F3") + ", " + Y.ToString("F3") + ", " + Z.ToString("F3") + ") [" + Mount + "]";
        }
    }

    /// <summary>
    /// Result of the simplified audible coverage audit for a notification appliance run.
    /// </summary>
    public class AudibleCoverageResult
    {
        public string Status { get; set; }
        public string Message { get; set; }
        public double RequiredDb { get; set; }
        public double AmbientDb { get; set; }
        public double MaxSustainedDb { get; set; }
        public bool IsSleepingArea { get; set; }
        public int SamplesChecked { get; set; }
        public int UncoveredSamples { get; set; }
        public double WorstDeficitDb { get; set; }
        public CalculatedSmokeDetectorPoint WorstPoint { get; set; }
        public double CoveragePercentage { get; set; }
    }
}