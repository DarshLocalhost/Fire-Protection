using System;
using System.Collections.Generic;
using System.Linq;

namespace SPA.SynthFire.Core.Standards;

/// <summary>Canonical hazard tier, light → severe, independent of any one standard.</summary>
public enum HazardTier { Light = 0, OH1 = 1, OH2 = 2, EH1 = 3, EH2 = 4 }

/// <summary>
/// The auto-classification of a room from its name: the suggested hazard tier, the
/// keyword(s) that matched, and whether a human should review it (multiple matches,
/// or a high-consequence occupancy such as storage/server that always needs a look).
/// </summary>
public sealed record RoomHazardSuggestion(HazardTier Tier, string MatchedKeywords, bool ReviewRequired)
{
    public bool Matched => MatchedKeywords.Length > 0;
}

/// <summary>
/// Suggests a DEFAULT hazard tier for a room from its name, by keyword. This is a
/// convenience starting point only — the user can override any room's hazard before
/// it is applied, and every classification remains review-required (the underlying
/// spacing rules are still provisional; see the standard's HasApprovedRules).
///
/// Ported from the FireProtectionSystem reference classifier; the keyword map and
/// the "always review" list are kept verbatim so behaviour matches that tool.
/// </summary>
public static class RoomHazardClassifier
{
    // Keyword → tier. Longer / more specific phrases should precede generic ones so
    // the most specific occupancy wins; on ties the highest (most severe) tier wins.
    private static readonly (string Keyword, HazardTier Tier)[] Map =
    {
        // LIGHT
        ("office", HazardTier.Light), ("school", HazardTier.Light), ("classroom", HazardTier.Light),
        ("hospital", HazardTier.Light), ("ward", HazardTier.Light), ("clinic", HazardTier.Light),
        ("church", HazardTier.Light), ("temple", HazardTier.Light), ("hotel room", HazardTier.Light),
        ("guest room", HazardTier.Light), ("lobby", HazardTier.Light), ("corridor", HazardTier.Light),
        ("toilet", HazardTier.Light), ("washroom", HazardTier.Light), ("conference", HazardTier.Light),
        ("meeting", HazardTier.Light), ("dining", HazardTier.Light), ("seating", HazardTier.Light),
        ("theatre", HazardTier.Light), ("theater", HazardTier.Light), ("museum", HazardTier.Light),
        ("library reading", HazardTier.Light),

        // OH1
        ("parking", HazardTier.OH1), ("garage", HazardTier.OH1), ("laundry", HazardTier.OH1),
        ("kitchen", HazardTier.OH1), ("canteen service", HazardTier.OH1), ("restaurant service", HazardTier.OH1),
        ("cannery", HazardTier.OH1), ("bakery", HazardTier.OH1), ("beverage", HazardTier.OH1),
        ("electronic", HazardTier.OH1), ("mechanical room", HazardTier.OH1), ("electrical room", HazardTier.OH1),
        ("pump room", HazardTier.OH1),

        // OH2
        ("machine shop", HazardTier.OH2), ("workshop", HazardTier.OH2), ("repair", HazardTier.OH2),
        ("mercantile", HazardTier.OH2), ("retail", HazardTier.OH2), ("shop floor", HazardTier.OH2),
        ("dry clean", HazardTier.OH2), ("wood assembly", HazardTier.OH2), ("carpentry", HazardTier.OH2),
        ("paper", HazardTier.OH2), ("post office", HazardTier.OH2), ("stack room", HazardTier.OH2),
        ("stationery", HazardTier.OH2), ("stockroom", HazardTier.OH2), ("store room", HazardTier.OH2),
        ("storage", HazardTier.OH2), ("warehouse", HazardTier.OH2),

        // EH1
        ("sawmill", HazardTier.EH1), ("die cast", HazardTier.EH1), ("printing", HazardTier.EH1),
        ("plywood", HazardTier.EH1), ("textile", HazardTier.EH1), ("upholster", HazardTier.EH1),
        ("foam", HazardTier.EH1), ("rubber", HazardTier.EH1),

        // EH2
        ("paint", HazardTier.EH2), ("spray booth", HazardTier.EH2), ("solvent", HazardTier.EH2),
        ("varnish", HazardTier.EH2), ("asphalt", HazardTier.EH2), ("flammable", HazardTier.EH2),
        ("fuel", HazardTier.EH2), ("oil quench", HazardTier.EH2), ("chemical", HazardTier.EH2),
        ("plastics processing", HazardTier.EH2),
    };

    // High-consequence occupancies that always warrant a manual look even on a single match.
    private static readonly string[] AlwaysReview =
    {
        "storage", "warehouse", "rack", "high bay", "godown", "server", "data"
    };

    /// <summary>Suggests a default hazard tier from a room name (Light + review when unknown).</summary>
    public static RoomHazardSuggestion Classify(string? roomName)
    {
        if (string.IsNullOrWhiteSpace(roomName))
            return new RoomHazardSuggestion(HazardTier.Light, "", ReviewRequired: false);

        string name = roomName.Trim().ToLowerInvariant();

        HazardTier best = HazardTier.Light;
        var hits = new List<string>();
        foreach (var (keyword, tier) in Map)
        {
            if (!name.Contains(keyword)) continue;
            hits.Add(keyword);
            if (tier > best) best = tier;
        }

        bool review = hits.Count > 0 && (hits.Count > 1 || AlwaysReview.Any(name.Contains));
        return new RoomHazardSuggestion(best, string.Join("+", hits), review);
    }

    /// <summary>
    /// Maps a canonical tier onto a hazard class id the given standard actually
    /// supports. NFPA 13 uses the same ids as the tiers; other standards (AS 2118)
    /// have a different, sometimes shorter, ladder, so the tier is clamped onto the
    /// standard's own ordered class list by position.
    /// </summary>
    public static string MapToStandardHazardId(IDesignStandard standard, HazardTier tier)
    {
        var classes = standard.SupportedHazardClasses;
        if (classes.Count == 0) return "";

        // Exact-id match first (NFPA 13: LH/OH1/OH2/EH1/EH2 line up with the tiers).
        string canonicalId = tier == HazardTier.Light ? "LH" : tier.ToString();
        var exact = classes.FirstOrDefault(c => string.Equals(c.Id, canonicalId, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.Id;

        // Otherwise clamp the tier's ordinal onto this standard's class ladder.
        int idx = Math.Min((int)tier, classes.Count - 1);
        return classes[idx].Id;
    }
}
