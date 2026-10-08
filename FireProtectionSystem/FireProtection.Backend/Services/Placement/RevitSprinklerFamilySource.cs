using Autodesk.Revit.DB;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.Backend.Services.Catalog;
using FireProtection.Backend.Services.Placement.Sprinklers.Final;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Sprinklers.BruteForce;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FireProtection.Backend.Services.Placement
{
    public class RevitSprinklerFamilySource : ISprinklerFamilySource
    {
        private readonly Document _hostDocument;
        private readonly Func<ICatalog> _catalogAccessor;

        public RevitSprinklerFamilySource(Document hostDocument)
            : this(hostDocument, null)
        {
        }

        public RevitSprinklerFamilySource(Document hostDocument, Func<ICatalog> catalogAccessor)
        {
            _hostDocument = hostDocument ?? throw new ArgumentNullException(nameof(hostDocument));
            _catalogAccessor = catalogAccessor;
        }

        public DeviceContextResolver GetDeviceContextResolver()
        {
            return ResolveDeviceContext;
        }

        public DevicePlacementContext ResolveDeviceContext(string familyName, string typeName)
        {
            DevicePlacementContext context = new DevicePlacementContext
            {
                FamilyName = familyName,
                TypeName = typeName,
                FamilyPlacementType = null,
                PlacementBehavior = DevicePlacementBehavior.Unsupported,
                Resolved = false,
                FailureReason = null
            };

            if (string.IsNullOrWhiteSpace(familyName) || string.IsNullOrWhiteSpace(typeName))
            {
                context.FailureReason = "Family name or type name is empty.";
                return context;
            }

            if (_hostDocument == null)
            {
                context.FailureReason = "Host document is unavailable.";
                return context;
            }

            try
            {
                FamilySymbol symbol = FindSymbolByName(familyName, typeName);
                if (symbol == null)
                {
                    context.FailureReason =
                        $"Sprinkler family/type '{familyName}:{typeName}' was not found in the active document.";
                    return context;
                }

                FamilyPlacementType? placementType = null;
                try
                {
                    placementType = symbol.Family?.FamilyPlacementType;
                }
                catch
                {
                    placementType = null;
                }

                context.FamilyPlacementType = placementType?.ToString();
                DevicePlacementBehavior familyLevel = DevicePlacementBehaviorResolver.FromFamilyPlacementType(placementType);

                string mount = null;
                if (_catalogAccessor != null)
                {
                    ICatalog currentCatalog = null;
                    try { currentCatalog = _catalogAccessor(); } catch { currentCatalog = null; }
                    if (currentCatalog != null)
                    {
                        mount = currentCatalog.GetSprinklerMount(familyName, typeName);
                    }
                }

                // Fallback to name-based mount inference if catalog did not supply a mount
                if (string.IsNullOrWhiteSpace(mount))
                {
                    mount = DevicePlacementBehaviorResolver.InferMountFromNames(familyName, typeName);
                }

                context.Mount = mount;
                DevicePlacementBehavior refined = DevicePlacementBehaviorResolver.FromResolvedFamily(familyLevel, mount);
                context.PlacementBehavior = refined;

                context.Resolved = context.PlacementBehavior != DevicePlacementBehavior.Unsupported;
                if (!context.Resolved && string.IsNullOrEmpty(context.FailureReason))
                {
                    context.FailureReason = placementType.HasValue
                        ? $"FamilyPlacementType '{placementType.Value}' is not supported by the current candidate pipeline."
                        : "FamilyPlacementType could not be read from the family.";
                }
                return context;
            }
            catch (Exception ex)
            {
                context.FailureReason = "Family lookup failed: " + ex.Message;
                return context;
            }
        }

        private FamilySymbol FindSymbolByName(string familyName, string typeName)
        {
            if (_hostDocument == null) return null;

            FilteredElementCollector collector = new FilteredElementCollector(_hostDocument)
                .OfCategory(BuiltInCategory.OST_Sprinklers)
                .OfClass(typeof(FamilySymbol));

            foreach (Element element in collector)
            {
                if (element is FamilySymbol sym)
                {
                    string fam = sym.FamilyName ?? sym.Family?.Name;
                    if (string.Equals(fam, familyName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(sym.Name, typeName, StringComparison.OrdinalIgnoreCase))
                    {
                        return sym;
                    }
                }
            }
            return null;
        }

/// <summary>
        /// Sprinkler families loaded in the open document.
        ///
        /// NO LONGER GATED. This used to return an empty list unless
        /// <c>FireProtectionConfig.UseRevitFamilyListing</c> was enabled, because the workbook used
        /// to be the only catalog of record. The catalog-source switch now makes the model an
        /// explicit, user-visible choice, so the flag would only be a second switch for one concept
        /// — and two switches is how the previous catalog/model inconsistency arose. The flag has
        /// been removed.
        ///
        /// Still used as the last-resort fallback by the tab view models when the active
        /// <see cref="ICatalog"/> is unavailable.
        /// </summary>
        public IReadOnlyList<SprinklerFamilyOption> GetAvailableFamilies()
        {
            List<SprinklerFamilyOption> result = new List<SprinklerFamilyOption>();
            if (_hostDocument == null) return result;

            FilteredElementCollector collector = new FilteredElementCollector(_hostDocument)
                .OfCategory(BuiltInCategory.OST_Sprinklers)
                .OfClass(typeof(FamilySymbol));

            foreach (Element element in collector)
            {
                if (!(element is FamilySymbol symbol)) continue;
                string familyName = symbol.FamilyName ?? symbol.Family?.Name;
                if (string.IsNullOrWhiteSpace(familyName)) continue;

                SprinklerFamilyOption family = result.Find(
                    item => string.Equals(item.FamilyName, familyName, StringComparison.OrdinalIgnoreCase));
                if (family == null)
                {
                    family = new SprinklerFamilyOption { FamilyName = familyName };
                    result.Add(family);
                }

                List<SprinklerTypeOption> types = new List<SprinklerTypeOption>(family.Types);
                types.Add(new SprinklerTypeOption { FamilyName = familyName, TypeName = symbol.Name });
                family.Types = types;
            }

            result.Sort((a, b) => string.Compare(a.FamilyName, b.FamilyName, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        // LoadFamily opens its own sub-transaction, so it must be called with NO transaction
        // open - the UI button that invokes this runs on Revit's idle UI thread, which is safe.
        // The shared helper reloads/overwrites an already-present family instead of failing.
        public FamilyLoadOutcome TryLoadFamily(string familyFilePath, out string error)
        {
            return FamilyLoadHelper.Load(_hostDocument, familyFilePath, out error);
        }
    }
}
