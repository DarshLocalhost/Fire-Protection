namespace SPA.SynthFire.Catalog;

/// <summary>
/// The Excel catalog layout: one worksheet per device category, row 1 = catalog
/// version, row 2 = column headers, row 3+ = data. Loader and template writer
/// share these constants so the format stays in lock-step.
/// </summary>
public static class CatalogSchema
{
    public const string CurrentVersion = "1.0";

    public const int VersionRow = 1;
    public const int HeaderRow = 2;
    public const int FirstDataRow = 3;

    public const string SprinklersSheet = "Sprinklers";
    public const string SmokeDetectorsSheet = "SmokeDetectors";

    public static readonly string[] SprinklerHeaders =
    {
        "Family", "Type", "KFactor", "Orientation", "HazardClassId", "StandardId", "TemperatureRating"
    };

    public static readonly string[] SmokeDetectorHeaders =
    {
        "Family", "Type", "Kind", "StandardId"
    };
}
