using System;
using System.Collections.Generic;
using System.Linq;
using SPA.SynthFire.Core.Catalog;

namespace SPA.SynthFire.Catalog;

/// <summary>Outcome of validating a loaded catalog. Errors block use; warnings inform.</summary>
public sealed class CatalogValidationResult
{
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<string> Warnings { get; }

    public CatalogValidationResult(IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
    {
        Errors = errors;
        Warnings = warnings;
    }

    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Validates catalog content beyond per-cell parsing: duplicate products (hard
/// error), empty catalog and unknown standard ids (warnings). Kept separate from
/// the loader so callers can surface warnings without failing the load.
/// </summary>
public sealed class CatalogValidator
{
    private readonly ISet<string> _knownStandardIds;

    /// <param name="knownStandardIds">
    /// Standard ids registered at runtime; rows referencing others get a warning.
    /// Pass null to skip the standard-id check.
    /// </param>
    public CatalogValidator(IEnumerable<string>? knownStandardIds = null)
    {
        _knownStandardIds = knownStandardIds is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(knownStandardIds, StringComparer.OrdinalIgnoreCase);
    }

    public CatalogValidationResult Validate(Core.Catalog.Catalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var errors = new List<string>();
        var warnings = new List<string>();

        if (catalog.Sprinklers.Count == 0 && catalog.SmokeDetectors.Count == 0)
            warnings.Add("Catalog contains no products.");

        // Duplicate sprinkler family+type is a hard error (ambiguous selection).
        foreach (var dup in catalog.Sprinklers
                     .GroupBy(s => (s.Family.ToLowerInvariant(), s.Type.ToLowerInvariant()))
                     .Where(g => g.Count() > 1))
        {
            errors.Add($"Duplicate sprinkler '{dup.First().Family} / {dup.First().Type}' ({dup.Count()} rows).");
        }

        foreach (var dup in catalog.SmokeDetectors
                     .GroupBy(d => (d.Family.ToLowerInvariant(), d.Type.ToLowerInvariant()))
                     .Where(g => g.Count() > 1))
        {
            errors.Add($"Duplicate detector '{dup.First().Family} / {dup.First().Type}' ({dup.Count()} rows).");
        }

        if (_knownStandardIds.Count > 0)
        {
            foreach (var s in catalog.Sprinklers.Where(s => !_knownStandardIds.Contains(s.StandardId)))
                warnings.Add($"Sprinkler '{s.Family}/{s.Type}' references unregistered standard '{s.StandardId}'.");
        }

        return new CatalogValidationResult(errors, warnings);
    }

    public void ValidateAndThrow(Core.Catalog.Catalog catalog)
    {
        var result = Validate(catalog);
        if (!result.IsValid)
            throw new CatalogLoadException("Catalog validation failed: " + string.Join(" ", result.Errors));
    }
}
