namespace FireProtection.UI.Services
{
    /// <summary>
    /// Tiny shared mutable reference to the current <see cref="ICatalog"/>.
    /// The Revit-aware family source constructs the resolver delegate at
    /// startup (BEFORE the user picks a catalog file), but the resolver
    /// needs the catalog's <c>Mount</c> lookup at CALL time — the user
    /// may load a catalog minutes after the add-in starts. The holder is
    /// a single-cell mutable box: the catalog viewmodel writes
    /// <c>Current</c> on every successful <c>TryLoad</c>, the family
    /// source's resolver reads it on every call.
    /// </summary>
    public sealed class CatalogHolder
    {
        public ICatalog Current { get; set; }
    }
}
