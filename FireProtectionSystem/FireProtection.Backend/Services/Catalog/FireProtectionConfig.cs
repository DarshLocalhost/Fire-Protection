namespace FireProtection.Backend.Services.Catalog
{
    /// <summary>
    /// Process-wide configuration flags for the FireProtection add-in. The flag
    /// <see cref="UseRevitFamilyListing"/> gates the legacy Revit-document family
    /// listing (<see cref="FireProtection.Backend.Services.Placement.RevitSprinklerFamilySource"/>).
    /// Decision 017 (2026-09-01) makes the Excel catalog the primary source of truth;
    /// the Revit listing is kept behind this flag (default false) for verification /
    /// cross-checks only, not deleted.
    /// </summary>
    public static class FireProtectionConfig
    {
        public const bool UseRevitFamilyListingDefault = false;

        private static bool _useRevitFamilyListing = UseRevitFamilyListingDefault;

        public static bool UseRevitFamilyListing
        {
            get { return _useRevitFamilyListing; }
            set { _useRevitFamilyListing = value; }
        }
    }
}
