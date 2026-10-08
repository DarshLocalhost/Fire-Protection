using System;
using System.Collections.Generic;

namespace FireProtection.Backend.Services.Catalog
{
    public enum CatalogCategory
    {
        Sprinkler = 1,
        SmokeDetector = 2,
        NotificationAppliance = 3
    }

    public sealed class SprinklerCatalogRow
    {
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string HazardClass { get; set; }
        public string Mount { get; set; }
        public string Notes { get; set; }

        // Optional per-type listed data (columns 7+). All nullable: a blank cell means
        // "no listed value — fall back to the provisional hazard-class default in the engine".
        // These are the client's listing/manufacturer numbers, NOT hardcoded in code.
        /// <summary>StandardSpray / ExtendedCoverage / Residential / Sidewall / ESFR / CMSA.</summary>
        public string SprinklerClass { get; set; }
        /// <summary>Listed maximum coverage area per sprinkler (sq ft).</summary>
        public double? MaxCoverageAreaSqFt { get; set; }
        /// <summary>Listed maximum center-to-center spacing (ft).</summary>
        public double? MaxSpacingFt { get; set; }
        /// <summary>Listed minimum center-to-center spacing (ft).</summary>
        public double? MinSpacingFt { get; set; }
        /// <summary>Listed coverage radius attributed to one sprinkler (ft).</summary>
        public double? CoverageRadiusFt { get; set; }
        /// <summary>Nominal K-factor.</summary>
        public double? KFactor { get; set; }
        /// <summary>Response type (QR / SR). Display metadata.</summary>
        public string ResponseType { get; set; }
        /// <summary>Temperature rating (deg F). Display metadata.</summary>
        public int? TempRatingF { get; set; }
        /// <summary>Deflector-to-ceiling distance (inches). Display metadata.</summary>
        public double? DeflectorToCeilingIn { get; set; }
        /// <summary>Listed maximum separation of sidewall heads along their host wall (feet).</summary>
        public double? SidewallMaxAlongWallSpacingFt { get; set; }
        /// <summary>Listed maximum directional throw perpendicular to the host wall (feet).</summary>
        public double? SidewallMaxThrowFt { get; set; }
        /// <summary>Listed end-wall/corner clearance limit for sidewall heads (feet).</summary>
        public double? SidewallEndWallClearanceFt { get; set; }
    }

    public sealed class SmokeDetectorCatalogRow
    {
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string DetectorType { get; set; }
        public string Mount { get; set; }
        public string CeilingSlope { get; set; }
        public string Notes { get; set; }
    }

    public sealed class NotificationApplianceCatalogRow
    {
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string ApplianceType { get; set; }
        public int Candela { get; set; }
        public int NotificationDba { get; set; }
        public string Notes { get; set; }
    }

    public sealed class Catalog
    {
        public string CatalogVersion { get; set; }
        public string SourcePath { get; set; }
        public List<SprinklerCatalogRow> Sprinklers { get; set; }
        public List<SmokeDetectorCatalogRow> SmokeDetectors { get; set; }
        public List<NotificationApplianceCatalogRow> NotificationAppliances { get; set; }

        public Catalog()
        {
            Sprinklers = new List<SprinklerCatalogRow>();
            SmokeDetectors = new List<SmokeDetectorCatalogRow>();
            NotificationAppliances = new List<NotificationApplianceCatalogRow>();
        }

        public int TotalRowCount
        {
            get
            {
                int n = 0;
                if (Sprinklers != null) n += Sprinklers.Count;
                if (SmokeDetectors != null) n += SmokeDetectors.Count;
                if (NotificationAppliances != null) n += NotificationAppliances.Count;
                return n;
            }
        }

        public string DisplayHeader
        {
            get
            {
                if (string.IsNullOrWhiteSpace(CatalogVersion)) return "Catalog (no version)";
                return "Catalog v" + CatalogVersion.Trim();
            }
        }
    }

    public sealed class CatalogIssue
    {
        public string Sheet { get; set; }
        public int Row { get; set; }
        public string Column { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }

        public CatalogIssue()
        {
        }

        public CatalogIssue(string sheet, int row, string column, string code, string message)
        {
            Sheet = sheet ?? string.Empty;
            Row = row;
            Column = column ?? string.Empty;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public override string ToString()
        {
            string loc = string.IsNullOrEmpty(Sheet) ? "?" : Sheet;
            if (Row > 0) loc = loc + " row " + Row;
            if (!string.IsNullOrEmpty(Column)) loc = loc + " col " + Column;
            return "[" + Code + "] " + loc + " - " + Message;
        }
    }

    [Serializable]
    public class CatalogLoadException : Exception
    {
        public IReadOnlyList<CatalogIssue> Issues { get; }

        public CatalogLoadException(string message)
            : base(message)
        {
            Issues = new List<CatalogIssue>();
        }

        public CatalogLoadException(string message, IReadOnlyList<CatalogIssue> issues)
            : base(BuildMessage(message, issues))
        {
            Issues = issues ?? new List<CatalogIssue>();
        }

        public CatalogLoadException(string message, Exception inner)
            : base(message, inner)
        {
            Issues = new List<CatalogIssue>();
        }

        private static string BuildMessage(string message, IReadOnlyList<CatalogIssue> issues)
        {
            if (issues == null || issues.Count == 0) return message ?? "Catalog load failed.";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine(message ?? "Catalog load failed with " + issues.Count + " issue(s).");
            sb.AppendLine();
            int max = Math.Min(issues.Count, 20);
            for (int i = 0; i < max; i++)
            {
                CatalogIssue issue = issues[i];
                if (issue == null) continue;
                sb.AppendLine("  - " + issue.ToString());
            }
            if (issues.Count > max)
            {
                sb.AppendLine("  ... and " + (issues.Count - max) + " more.");
            }
            return sb.ToString();
        }
    }
}
