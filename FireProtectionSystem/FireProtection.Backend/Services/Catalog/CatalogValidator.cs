using System;
using System.Collections.Generic;
using FireProtection.UI.ViewModels.Sprinklers.BruteForce;

namespace FireProtection.Backend.Services.Catalog
{
    public static class CatalogOptions
    {
        public static readonly IReadOnlyList<string> HazardClasses = new List<string>
        {
            HazardClassOptions.Light,
            HazardClassOptions.OH1,
            HazardClassOptions.OH2,
            HazardClassOptions.EH1,
            HazardClassOptions.EH2
        };

        public static readonly IReadOnlyList<string> SprinklerMounts = new List<string>
        {
            "Pendent",
            "Upright",
            "Sidewall",
            "Recessed"
        };

        public static readonly IReadOnlyList<string> DetectorTypes = new List<string>
        {
            "Ionization",
            "Photoelectric",
            "Heat",
            "CO",
            "MultiCriteria",
            "Aspirating"
        };

        public static readonly IReadOnlyList<string> Mounts = new List<string>
        {
            "Ceiling",
            "Wall",
            "Floor"
        };

        public static readonly IReadOnlyList<string> CeilingSlopes = new List<string>
        {
            "Flat",
            "Sloped",
            "Stepped"
        };

        public static readonly IReadOnlyList<string> ApplianceTypes = new List<string>
        {
            "Horn",
            "Strobe",
            "HornStrobe",
            "Speaker",
            "SpeakerStrobe",
            "Chime",
            "ChimeStrobe"
        };
    }

    public sealed class CatalogValidationResult
    {
        public List<CatalogIssue> Issues { get; }

        public CatalogValidationResult()
        {
            Issues = new List<CatalogIssue>();
        }

        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < Issues.Count; i++)
                {
                    if (Issues[i] == null) continue;
                    if (string.Equals(Issues[i].Code, "ERROR", StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
        }

        public IReadOnlyList<CatalogIssue> Errors
        {
            get
            {
                List<CatalogIssue> errs = new List<CatalogIssue>();
                for (int i = 0; i < Issues.Count; i++)
                {
                    if (Issues[i] == null) continue;
                    if (string.Equals(Issues[i].Code, "ERROR", StringComparison.OrdinalIgnoreCase)) errs.Add(Issues[i]);
                }
                return errs;
            }
        }
    }

    public static class CatalogValidator
    {
        public const string SheetSprinklers = "Sprinklers";
        public const string SheetSmokeDetectors = "SmokeDetectors";
        public const string SheetNotificationAppliances = "NotificationAppliances";

        public const string SeverityError = "ERROR";
        public const string SeverityWarning = "WARNING";

        public static CatalogValidationResult Validate(Catalog catalog)
        {
            CatalogValidationResult result = new CatalogValidationResult();
            if (catalog == null)
            {
                result.Issues.Add(new CatalogIssue("(catalog)", 0, "", SeverityError, "Catalog is null."));
                return result;
            }

            if (string.IsNullOrWhiteSpace(catalog.CatalogVersion))
            {
                result.Issues.Add(new CatalogIssue("(header)", 0, "CatalogVersion", SeverityError,
                    "CatalogVersion header is missing. The first row of any sheet must be 'CatalogVersion,<value>'."));
            }

            HashSet<string> sprinklerKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (catalog.Sprinklers != null)
            {
                for (int i = 0; i < catalog.Sprinklers.Count; i++)
                {
                    SprinklerCatalogRow row = catalog.Sprinklers[i];
                    int sheetRow = i + 2;
                    if (row == null)
                    {
                        result.Issues.Add(new CatalogIssue(SheetSprinklers, sheetRow, "", SeverityError, "Row is empty."));
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(row.FamilyName))
                        result.Issues.Add(new CatalogIssue(SheetSprinklers, sheetRow, "FamilyName", SeverityError, "FamilyName is required."));
                    if (string.IsNullOrWhiteSpace(row.TypeName))
                        result.Issues.Add(new CatalogIssue(SheetSprinklers, sheetRow, "TypeName", SeverityError, "TypeName is required."));
                    if (!string.IsNullOrWhiteSpace(row.HazardClass) && !IsKnown(HazardClassesForValidation, row.HazardClass))
                    {
                        result.Issues.Add(new CatalogIssue(SheetSprinklers, sheetRow, "HazardClass", SeverityWarning,
                            "HazardClass '" + row.HazardClass + "' is not in the known list; falling back to the hardcoded set."));
                    }
                    if (!string.IsNullOrWhiteSpace(row.Mount) && !IsKnown(CatalogOptions.SprinklerMounts, row.Mount))
                    {
                        result.Issues.Add(new CatalogIssue(SheetSprinklers, sheetRow, "Mount", SeverityWarning,
                            "Mount '" + row.Mount + "' is not in the known list."));
                    }
                    if (!string.IsNullOrWhiteSpace(row.FamilyName) && !string.IsNullOrWhiteSpace(row.TypeName))
                    {
                        string key = row.FamilyName.Trim() + "|" + row.TypeName.Trim();
                        if (!sprinklerKeys.Add(key))
                        {
                            result.Issues.Add(new CatalogIssue(SheetSprinklers, sheetRow, "FamilyName/TypeName", SeverityError,
                                "Duplicate (FamilyName, TypeName) '" + row.FamilyName + " / " + row.TypeName + "'."));
                        }
                    }
                }
            }

            HashSet<string> smokeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (catalog.SmokeDetectors != null)
            {
                for (int i = 0; i < catalog.SmokeDetectors.Count; i++)
                {
                    SmokeDetectorCatalogRow row = catalog.SmokeDetectors[i];
                    int sheetRow = i + 2;
                    if (row == null)
                    {
                        result.Issues.Add(new CatalogIssue(SheetSmokeDetectors, sheetRow, "", SeverityError, "Row is empty."));
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(row.FamilyName))
                        result.Issues.Add(new CatalogIssue(SheetSmokeDetectors, sheetRow, "FamilyName", SeverityError, "FamilyName is required."));
                    if (string.IsNullOrWhiteSpace(row.TypeName))
                        result.Issues.Add(new CatalogIssue(SheetSmokeDetectors, sheetRow, "TypeName", SeverityError, "TypeName is required."));
                    if (string.IsNullOrWhiteSpace(row.DetectorType))
                        result.Issues.Add(new CatalogIssue(SheetSmokeDetectors, sheetRow, "DetectorType", SeverityError, "DetectorType is required."));
                    if (string.IsNullOrWhiteSpace(row.Mount))
                        result.Issues.Add(new CatalogIssue(SheetSmokeDetectors, sheetRow, "Mount", SeverityError, "Mount is required."));
                    if (string.IsNullOrWhiteSpace(row.CeilingSlope))
                        result.Issues.Add(new CatalogIssue(SheetSmokeDetectors, sheetRow, "CeilingSlope", SeverityError, "CeilingSlope is required."));
                    if (!string.IsNullOrWhiteSpace(row.DetectorType) && !IsKnown(CatalogOptions.DetectorTypes, row.DetectorType))
                        result.Issues.Add(new CatalogIssue(SheetSmokeDetectors, sheetRow, "DetectorType", SeverityWarning,
                            "DetectorType '" + row.DetectorType + "' is not in the known list."));
                    if (!string.IsNullOrWhiteSpace(row.Mount) && !IsKnown(CatalogOptions.Mounts, row.Mount))
                        result.Issues.Add(new CatalogIssue(SheetSmokeDetectors, sheetRow, "Mount", SeverityWarning,
                            "Mount '" + row.Mount + "' is not in the known list."));
                    if (!string.IsNullOrWhiteSpace(row.CeilingSlope) && !IsKnown(CatalogOptions.CeilingSlopes, row.CeilingSlope))
                        result.Issues.Add(new CatalogIssue(SheetSmokeDetectors, sheetRow, "CeilingSlope", SeverityWarning,
                            "CeilingSlope '" + row.CeilingSlope + "' is not in the known list."));
                    if (!string.IsNullOrWhiteSpace(row.FamilyName) && !string.IsNullOrWhiteSpace(row.TypeName))
                    {
                        string key = row.FamilyName.Trim() + "|" + row.TypeName.Trim();
                        if (!smokeKeys.Add(key))
                        {
                            result.Issues.Add(new CatalogIssue(SheetSmokeDetectors, sheetRow, "FamilyName/TypeName", SeverityError,
                                "Duplicate (FamilyName, TypeName) '" + row.FamilyName + " / " + row.TypeName + "'."));
                        }
                    }
                }
            }

            HashSet<string> naKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (catalog.NotificationAppliances != null)
            {
                for (int i = 0; i < catalog.NotificationAppliances.Count; i++)
                {
                    NotificationApplianceCatalogRow row = catalog.NotificationAppliances[i];
                    int sheetRow = i + 2;
                    if (row == null)
                    {
                        result.Issues.Add(new CatalogIssue(SheetNotificationAppliances, sheetRow, "", SeverityError, "Row is empty."));
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(row.FamilyName))
                        result.Issues.Add(new CatalogIssue(SheetNotificationAppliances, sheetRow, "FamilyName", SeverityError, "FamilyName is required."));
                    if (string.IsNullOrWhiteSpace(row.TypeName))
                        result.Issues.Add(new CatalogIssue(SheetNotificationAppliances, sheetRow, "TypeName", SeverityError, "TypeName is required."));
                    if (string.IsNullOrWhiteSpace(row.ApplianceType))
                        result.Issues.Add(new CatalogIssue(SheetNotificationAppliances, sheetRow, "ApplianceType", SeverityError, "ApplianceType is required."));
                    // Candela is the visible rating and NotificationDba the audible one. Neither is
                    // required on every row - a plain Horn / Speaker / Chime has no candela, and a
                    // strobe-only appliance has no dBA - but a row with neither carries no placeable
                    // device data at all. So each rating is required only for the types that have one,
                    // and a row must carry at least one of the two. Requiring Candela unconditionally
                    // would make Horn, Speaker and Chime unusable despite being in CatalogOptions.
                    bool isVisibleType = !string.IsNullOrWhiteSpace(row.ApplianceType)
                        && row.ApplianceType.IndexOf("Strobe", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isAudibleType = !string.IsNullOrWhiteSpace(row.ApplianceType)
                        && (row.ApplianceType.IndexOf("Horn", StringComparison.OrdinalIgnoreCase) >= 0
                            || row.ApplianceType.IndexOf("Speaker", StringComparison.OrdinalIgnoreCase) >= 0
                            || row.ApplianceType.IndexOf("Chime", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (row.Candela < 0 || (isVisibleType && row.Candela <= 0))
                    {
                        result.Issues.Add(new CatalogIssue(SheetNotificationAppliances, sheetRow, "Candela", SeverityError,
                            "Candela must be a positive integer for visible appliance types (any Strobe)."));
                    }
                    if (row.NotificationDba < 0 || (isAudibleType && row.NotificationDba <= 0))
                    {
                        result.Issues.Add(new CatalogIssue(SheetNotificationAppliances, sheetRow, "NotificationDba", SeverityError,
                            "NotificationDba must be a positive integer for audible appliance types (Horn / Speaker / Chime)."));
                    }
                    if (row.Candela <= 0 && row.NotificationDba <= 0)
                    {
                        result.Issues.Add(new CatalogIssue(SheetNotificationAppliances, sheetRow, "Candela/NotificationDba", SeverityError,
                            "Row has neither a Candela nor a NotificationDba rating - at least one is required."));
                    }
                    if (!string.IsNullOrWhiteSpace(row.ApplianceType) && !IsKnown(CatalogOptions.ApplianceTypes, row.ApplianceType))
                        result.Issues.Add(new CatalogIssue(SheetNotificationAppliances, sheetRow, "ApplianceType", SeverityWarning,
                            "ApplianceType '" + row.ApplianceType + "' is not in the known list."));
                    if (!string.IsNullOrWhiteSpace(row.FamilyName) && !string.IsNullOrWhiteSpace(row.TypeName))
                    {
                        string key = row.FamilyName.Trim() + "|" + row.TypeName.Trim();
                        if (!naKeys.Add(key))
                        {
                            result.Issues.Add(new CatalogIssue(SheetNotificationAppliances, sheetRow, "FamilyName/TypeName", SeverityError,
                                "Duplicate (FamilyName, TypeName) '" + row.FamilyName + " / " + row.TypeName + "'."));
                        }
                    }
                }
            }

            return result;
        }

        private static readonly IReadOnlyList<string> HazardClassesForValidation = new List<string>
        {
            HazardClassOptions.Light,
            HazardClassOptions.OH1,
            HazardClassOptions.OH2,
            HazardClassOptions.EH1,
            HazardClassOptions.EH2
        };

        private static bool IsKnown(IReadOnlyList<string> allowed, string value)
        {
            if (allowed == null || string.IsNullOrWhiteSpace(value)) return false;
            for (int i = 0; i < allowed.Count; i++)
            {
                if (string.Equals(allowed[i], value.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
