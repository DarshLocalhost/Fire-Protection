using System.Collections.Generic;

namespace SPA.SynthFire.Core.Catalog;

/// <summary>
/// Revit-free seam over a loaded catalog. The UI binds device dropdowns to this
/// and the placement layer resolves products through it, so neither depends on
/// how the catalog was loaded (Excel today) or on the Revit API.
/// </summary>
public interface ICatalog
{
    string Version { get; }

    IReadOnlyList<SprinklerCatalogRow> Sprinklers { get; }
    IReadOnlyList<SmokeDetectorCatalogRow> SmokeDetectors { get; }

    /// <summary>Sprinklers listed for the given standard + hazard class.</summary>
    IReadOnlyList<SprinklerCatalogRow> GetSprinklers(string standardId, string hazardClassId);
}
