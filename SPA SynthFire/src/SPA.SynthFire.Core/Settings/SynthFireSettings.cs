using SPA.SynthFire.Core.Standards;

namespace SPA.SynthFire.Core.Settings;

/// <summary>
/// User-configurable settings persisted between sessions (the "Settings" dialog).
/// Kept Revit-free and serializable.
/// </summary>
public sealed class SynthFireSettings
{
    /// <summary>Active design standard id (see <see cref="StandardIds"/>).</summary>
    public string StandardId { get; set; } = StandardIds.Nfpa13;

    /// <summary>Display unit system for the settings UI.</summary>
    public UnitSystem Units { get; set; } = UnitSystem.Imperial;

    /// <summary>Sprinkler standoff below a solid (non-tile) ceiling, in inches.</summary>
    public double CeilingOffsetInches { get; set; } = 4.0;

    /// <summary>Wall-mounted notification appliance mounting height, in inches AFF.</summary>
    public double NotificationMountingHeightInches { get; set; } = 80.0;

    /// <summary>
    /// Visual (strobe) effective intensity in candela used to derive wall-appliance
    /// spacing from NFPA 72 Table 18.5.5.5.1(a). Default 75 cd (a common wall rating,
    /// ≤45 ft square coverage). Provisional — verify against the room and listing.
    /// </summary>
    public double NotificationCandela { get; set; } = 75.0;

    /// <summary>
    /// Snap auto-placed ceiling devices (sprinklers / detectors) to the ceiling tile
    /// grid centres, like Tandm's "Tile Position". Off = place on the even coverage grid.
    /// </summary>
    public bool SnapToCeilingTile { get; set; }

    /// <summary>Ceiling tile module in the X (east-west) direction, in inches. Default 24" (2 ft lay-in).</summary>
    public double CeilingTileModuleXInches { get; set; } = 24.0;

    /// <summary>Ceiling tile module in the Y (north-south) direction, in inches. Default 24" (2 ft lay-in).</summary>
    public double CeilingTileModuleYInches { get; set; } = 24.0;

    /// <summary>Model X (feet-as-inches) of one tile's grid origin, so the snap grid aligns to the real ceiling.</summary>
    public double CeilingTileOriginXInches { get; set; }

    /// <summary>Model Y (feet-as-inches) of one tile's grid origin, so the snap grid aligns to the real ceiling.</summary>
    public double CeilingTileOriginYInches { get; set; }

    /// <summary>
    /// Per-hazard edits to the design-standard tables, keyed by
    /// <see cref="HazardRuleOverride.Key"/> ("standardId|hazardId").
    /// </summary>
    public System.Collections.Generic.Dictionary<string, HazardRuleOverride> Overrides { get; set; } = new();

    /// <summary>
    /// Preferred sprinkler family for each hazard class, keyed "standardId|hazardId"
    /// → family/type label (e.g. "Std Pendent : SP-1"). Used to auto-select the
    /// family for a run. Stored by label (name) so it is portable across models.
    /// </summary>
    public System.Collections.Generic.Dictionary<string, string> FamilyMap { get; set; } = new();

    /// <summary>
    /// Deep copy via a JSON round-trip, so the copy shares no dictionaries with the original.
    /// Used by <see cref="ProfileBook"/> to snapshot settings into a profile and back.
    /// </summary>
    public static SynthFireSettings Clone(SynthFireSettings source)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(source);
        return System.Text.Json.JsonSerializer.Deserialize<SynthFireSettings>(json) ?? new SynthFireSettings();
    }
}
