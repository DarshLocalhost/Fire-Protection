using System;

namespace SPA.SynthFire.Catalog;

/// <summary>
/// Raised when a catalog workbook cannot be read or fails schema validation.
/// Loading is fail-fast: a bad catalog stops the run rather than silently
/// producing a partial device list.
/// </summary>
public sealed class CatalogLoadException : Exception
{
    public CatalogLoadException(string message) : base(message) { }
    public CatalogLoadException(string message, Exception inner) : base(message, inner) { }
}
