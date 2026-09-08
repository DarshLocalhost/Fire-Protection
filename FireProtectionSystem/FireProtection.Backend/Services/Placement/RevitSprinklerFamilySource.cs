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

        /// <summary>
        /// Backward-compatible constructor: the family source works exactly as before
        /// when no <paramref name="catalogAccessor"/> is supplied — the mount signal
        /// is simply absent and the resolver falls back to the family-level bucket
        /// (<see cref="DevicePlacementBehavior.WorkPlaneDependent"/>,
        /// <see cref="DevicePlacementBehavior.FaceHosted"/>, etc.).
        /// </summary>
        public RevitSprinklerFamilySource(Document hostDocument)
            : this(hostDocument, null)
        {
        }

        /// <summary>
        /// Mount-aware constructor. <paramref name="catalogAccessor"/> is a
        /// delegate that returns the CURRENT <see cref="ICatalog"/> at call time
        /// (it may return <c>null</c> when the user has not yet picked a catalog
        /// file). <see cref="ResolveDeviceContext"/> reads the delegate on every
        /// call so the mount signal is fresh — the catalog is loaded AFTER the
        /// add-in starts, and the resolver must not capture a stale or
        /// <c>null</c> catalog at construction time.
        ///
        /// When the delegate returns a non-null catalog, the resolver looks up
        /// the <c>Mount</c> column for the resolved (family, type) and refines
        /// <see cref="DevicePlacementBehavior.WorkPlaneDependent"/> into either
        /// <see cref="DevicePlacementBehavior.CeilingOverhead"/> (pendent/upright)
        /// or <see cref="DevicePlacementBehavior.WallSidewall"/> (sidewall). This
        /// is the cross-layer signal that activates the sidewall candidate branch
        /// in <see cref="FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce.BruteForceCalculationService"/>.
        /// </summary>
        public RevitSprinklerFamilySource(Document hostDocument, Func<ICatalog> catalogAccessor)
        {
            _hostDocument = hostDocument ?? throw new ArgumentNullException(nameof(hostDocument));
            _catalogAccessor = catalogAccessor; // may be null
        }

        // -----------------------------------------------------------------------------------
        // Step 2 — plain device / mount context resolution (Revit-aware boundary only)
        // -----------------------------------------------------------------------------------
        // The calculation layer must remain completely Revit-free. The actual Revit
        // FamilySymbol lookup + FamilyPlacementType read happen HERE, and only the plain
        // (Revit-free) values are returned. The plain context is then carried by the
        // existing input pipeline into PlacementInputBuilder → PlacementRoomInput.
        //
        // Step 2 does NOT change candidate generation; it only resolves and carries the
        // context so future steps can use it.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Step 2 — exposes the Backend-internal <see cref="DeviceContextResolver"/>
        /// delegate that <see cref="PlacementInputBuilder"/> consumes. The delegate
        /// wraps <see cref="ResolveDeviceContext"/> so callers do not have to
        /// depend on the concrete <c>RevitSprinklerFamilySource</c> class.
        /// </summary>
        public DeviceContextResolver GetDeviceContextResolver()
        {
            return ResolveDeviceContext;
        }

        /// <summary>
        /// Resolves the actual Revit's <c>FamilyPlacementType</c> for the given
        /// (family, type) pair and converts it to a plain, Revit-free
        /// <see cref="DevicePlacementBehavior"/> via <see cref="DevicePlacementBehaviorResolver"/>.
        /// Never throws; an unresolvable family/type returns
        /// <see cref="DevicePlacementBehavior.Unsupported"/> with a null placement type
        /// (no silent default — per Step 2 error-handling rule).
        /// </summary>
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
                context.PlacementBehavior = familyLevel;

                // Mount-aware refinement: when a catalog accessor is wired, read the
                // CURRENT catalog at call time (the user may have loaded it after the
                // family source was constructed) and look up the Mount column for this
                // (family, type). Refines WorkPlaneDependent into CeilingOverhead /
                // WallSidewall. A catalog miss (no catalog loaded, or no row for this
                // (family, type)) preserves the family-level bucket — pre-mount
                // behavior, but a warning is logged so a sidewall family that
                // ends up on the ceiling-grid branch is traceable.
                if (_catalogAccessor != null)
                {
                    ICatalog currentCatalog = null;
                    try { currentCatalog = _catalogAccessor(); } catch { currentCatalog = null; }
                    if (currentCatalog == null)
                    {
                        FireProtectionLog.Warn(
                            "RevitSprinklerFamilySource: no catalog is loaded — " +
                            "mount signal unavailable for '" + familyName + ":" + typeName + "'. " +
                            "Open the catalog file via the top bar before placement so sidewall / pendent " +
                            "families use the correct candidate branch.");
                    }
                    else
                    {
                        string mount = currentCatalog.GetSprinklerMount(familyName, typeName);
                        context.Mount = mount;
                        if (string.IsNullOrWhiteSpace(mount))
                        {
                            // Family found in the Revit document, but the catalog has
                            // no row for it (or the row's Mount column is blank). The
                            // family-level bucket applies; the sidewall candidate
                            // branch will NOT fire for a sidewall family whose name
                            // does not exactly match a catalog row.
                            FireProtectionLog.Warn(
                                "RevitSprinklerFamilySource: catalog has no row for '" +
                                familyName + ":" + typeName + "'. " +
                                "Mount signal is empty — placement will use the family-level bucket " +
                                "(WorkPlaneDependent), not the catalog's intended mount. " +
                                "Verify the (FamilyName, TypeName) pair in the Excel catalog matches the " +
                                "Revit family exactly (case-insensitive, trimmed).");
                        }
                        else
                        {
                            DevicePlacementBehavior refined = DevicePlacementBehaviorResolver.FromResolvedFamily(familyLevel, mount);
                            if (refined != familyLevel) context.PlacementBehavior = refined;
                        }
                    }
                }

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

        /// <summary>
        /// Walks the host document's sprinkler FamilySymbols and returns the first one
        /// whose (FamilyName, Type Name) matches the supplied pair (case-insensitive).
        /// This is the SAME matcher used by
        /// <see cref="RevitSprinklerPlacementService.ResolveSymbol"/>; it is reproduced
        /// here so the Step 2 resolver is self-contained and does not change placement
        /// behavior. Returns <c>null</c> when no match is found.
        /// </summary>
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

        public IReadOnlyList<SprinklerFamilyOption> GetAvailableFamilies()
        {
            List<SprinklerFamilyOption> result = new List<SprinklerFamilyOption>();

            if (_hostDocument == null) return result;

            // Decision 017 (2026-09-01): the Excel catalog is the primary source of truth for
            // sprinkler families + types. The Revit family listing below is COMMENTED OUT, not
            // deleted, and gated by FireProtectionConfig.UseRevitFamilyListing (default false).
            // Re-enable for verification / cross-checks; do not enable as a production default.
            if (!FireProtectionConfig.UseRevitFamilyListing)
            {
                return result;
            }

            // ===================================================================================
            // LEGACY REVIT-DOCUMENT FAMILY LISTING (commented out per Decision 017)
            // ===================================================================================
            //try
            //{
            //    FilteredElementCollector collector = new FilteredElementCollector(_hostDocument)
            //        .OfCategory(BuiltInCategory.OST_Sprinklers)
            //        .OfClass(typeof(FamilySymbol));
            //
            //    Dictionary<string, List<string>> familyMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            //
            //    foreach (Element element in collector)
            //    {
            //        if (element is FamilySymbol symbol)
            //        {
            //            string familyName = symbol.FamilyName ?? symbol.Family?.Name;
            //            string typeName = symbol.Name;
            //
            //            if (string.IsNullOrWhiteSpace(familyName)) continue;
            //
            //            if (!familyMap.ContainsKey(familyName))
            //            {
            //                familyMap[familyName] = new List<string>();
            //            }
            //
            //            if (!string.IsNullOrWhiteSpace(typeName) && !familyMap[familyName].Contains(typeName))
            //            {
            //                familyMap[familyName].Add(typeName);
            //            }
            //        }
            //    }
            //
            //    foreach (KeyValuePair<string, List<string>> kvp in familyMap.OrderBy(k => k.Key))
            //    {
            //        List<SprinklerTypeOption> typeOptions = kvp.Value
            //            .OrderBy(t => t)
            //            .Select(t => new SprinklerTypeOption
            //            {
            //                FamilyName = kvp.Key,
            //                TypeName = t
            //            })
            //            .ToList();
            //
            //        result.Add(new SprinklerFamilyOption
            //        {
            //            FamilyName = kvp.Key,
            //            Types = typeOptions
            //        });
            //    }
            //}
            //catch
            //{
            //    // Fallback to empty list if symbol collection fails
            //}
            // ===================================================================================
            // END LEGACY BLOCK
            // ===================================================================================

            return result;
        }
    }
}
