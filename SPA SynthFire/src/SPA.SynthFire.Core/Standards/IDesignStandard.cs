using System.Collections.Generic;

namespace SPA.SynthFire.Core.Standards;

/// <summary>
/// A swappable fire-protection design standard (AS 2118, NFPA 13, BS EN 12845, ...).
/// SPA SynthFire is standard-agnostic: solvers ask the active standard for the
/// spacing/coverage rule set of a chosen hazard class rather than hard-coding any
/// one code. Standards are configurable at runtime via the <see cref="StandardRegistry"/>.
/// </summary>
public interface IDesignStandard
{
    /// <summary>Stable identifier, e.g. "NFPA13", "AS2118" (see <see cref="StandardIds"/>).</summary>
    string Id { get; }

    /// <summary>Human-readable name shown in the UI.</summary>
    string DisplayName { get; }

    /// <summary>Unit system this standard's published values are expressed in.</summary>
    UnitSystem Units { get; }

    /// <summary>
    /// False until the encoded values have been reviewed and signed off by a
    /// fire-protection engineer. While false, every generated layout must be
    /// flagged provisional / review-required.
    /// </summary>
    bool HasApprovedRules { get; }

    /// <summary>Hazard classifications this standard supports.</summary>
    IReadOnlyList<HazardClassification> SupportedHazardClasses { get; }

    /// <summary>
    /// The sprinkler spacing/coverage rule set for the given hazard class.
    /// Throws <see cref="System.ArgumentException"/> if the class is unknown.
    /// </summary>
    SprinklerRuleSet GetSprinklerRules(string hazardClassId);
}

/// <summary>Well-known standard identifiers.</summary>
public static class StandardIds
{
    public const string Nfpa13 = "NFPA13";
    public const string As2118 = "AS2118";
    public const string BsEn12845 = "BSEN12845";
}
