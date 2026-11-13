using System;
using System.Collections.Generic;
using System.Linq;
using SPA.SynthFire.Core.Catalog;

namespace SPA.SynthFire.Catalog;

/// <summary>
/// <see cref="ICatalog"/> over an in-memory <see cref="Core.Catalog.Catalog"/>.
/// Build via <see cref="CatalogLoader.LoadFromFile"/> then <see cref="FromCatalog"/>.
/// </summary>
public sealed class CatalogService : ICatalog
{
    private readonly Core.Catalog.Catalog _catalog;
    private CatalogService(Core.Catalog.Catalog catalog) => _catalog = catalog;

    public static CatalogService FromCatalog(Core.Catalog.Catalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return new CatalogService(catalog);
    }
    public string Version => _catalog.Version; 
    public IReadOnlyList<SprinklerCatalogRow> Sprinklers => _catalog.Sprinklers;
    public IReadOnlyList<SmokeDetectorCatalogRow> SmokeDetectors => _catalog.SmokeDetectors;

    public IReadOnlyList<SprinklerCatalogRow> GetSprinklers(string standardId, string hazardClassId)
    {
        ArgumentNullException.ThrowIfNull(standardId);
        ArgumentNullException.ThrowIfNull(hazardClassId);

        return _catalog.Sprinklers
            .Where(s =>
                string.Equals(s.StandardId, standardId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(s.HazardClassId, hazardClassId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
