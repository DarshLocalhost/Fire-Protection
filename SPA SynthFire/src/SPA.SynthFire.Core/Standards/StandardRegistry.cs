using System;
using System.Collections.Generic;
using System.Linq;

namespace SPA.SynthFire.Core.Standards;

/// <summary>
/// Runtime registry of available design standards. The UI populates its
/// standard picker from here and resolves the active <see cref="IDesignStandard"/>
/// by id. Ships with NFPA 13 and AS 2118; BS EN 12845 is added in a later phase.
/// </summary>
public sealed class StandardRegistry
{
    private readonly Dictionary<string, IDesignStandard> _standards;

    public StandardRegistry(IEnumerable<IDesignStandard> standards)
    {
        ArgumentNullException.ThrowIfNull(standards);
        _standards = standards.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The default set of standards shipped with SPA SynthFire.</summary>
    public static StandardRegistry CreateDefault() =>
        new(new IDesignStandard[] { new Nfpa13Standard(), new As2118Standard() });

    public IReadOnlyCollection<IDesignStandard> All => _standards.Values;

    public bool TryGet(string standardId, out IDesignStandard standard) =>
        _standards.TryGetValue(standardId, out standard!);

    public IDesignStandard Get(string standardId)
    {
        if (!_standards.TryGetValue(standardId, out var standard))
        {
            throw new ArgumentException(
                $"Unknown standard '{standardId}'. Registered: {string.Join(", ", _standards.Keys)}.",
                nameof(standardId));
        }
        return standard;
    }
}
