using System.Collections.Generic;
using System.Text;

namespace FireProtection.UI.Models.Sprinklers.BruteForce
{
    /// <summary>
    /// Root BruteForce calculation result covering every selected room.
    /// </summary>
    public class BruteForceCalculationResult
    {
        public bool Success { get; set; }

        public List<RoomCalculationResult> Rooms { get; set; }

        public int TotalCalculatedSprinklers { get; set; }

        public List<string> Warnings { get; set; }
        public List<string> Errors { get; set; }

        /// <summary>
        /// Human readable summary of the rule set actually applied (e.g. spacing source).
        /// </summary>
        public string AppliedRulesSummary { get; set; }

        /// <summary>
        /// True when the applied spacing/coverage values are provisional placeholders pending
        /// senior/project-approved NFPA13-2022 values (i.e. NOT fabricated engineering compliance).
        /// </summary>
        public bool IsProvisional { get; set; }

        public BruteForceCalculationResult()
        {
            Rooms = new List<RoomCalculationResult>();
            Warnings = new List<string>();
            Errors = new List<string>();
        }

        public string SummaryText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("BruteForce Sprinkler Calculation");
            sb.AppendLine("-------------------------------");
            sb.AppendLine($"Overall success : {Success}");
            sb.AppendLine($"Provisional rules: {IsProvisional}");
            sb.AppendLine($"Rooms processed : {Rooms.Count}");
            sb.AppendLine($"Total sprinklers: {TotalCalculatedSprinklers}");
            sb.AppendLine($"Warnings        : {Warnings.Count}");
            sb.AppendLine($"Errors          : {Errors.Count}");
            if (!string.IsNullOrEmpty(AppliedRulesSummary))
            {
                sb.AppendLine();
                sb.AppendLine("Applied rules:");
                sb.AppendLine("  " + AppliedRulesSummary);
            }

            sb.AppendLine();
            sb.AppendLine("Per-room:");
            foreach (RoomCalculationResult room in Rooms)
            {
                sb.AppendLine(
                    $"  [{room.Status}] {room.RoomName} ({room.RoomNumber}) " +
                    $"-> {room.CalculatedCount} sprinkler(s)");
                foreach (string warning in room.Warnings)
                {
                    sb.AppendLine($"      warn: {warning}");
                }
                foreach (string error in room.Errors)
                {
                    sb.AppendLine($"      err : {error}");
                }
            }

            return sb.ToString();
        }
    }
}
