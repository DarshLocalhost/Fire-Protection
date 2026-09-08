using System;
using System.Collections.Generic;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;

namespace FireProtection.Backend.Services.Placement.Sprinklers.Final
{
    public class PlacementRoomSelection
    {
        public string LevelId { get; set; }
        public string LevelName { get; set; }
        public double LevelElevationFt { get; set; }

        public string RoomId { get; set; }
        public string RoomName { get; set; }
        public string RoomNumber { get; set; }

        public double AreaSqFt { get; set; }
        public double? VolumeCuFt { get; set; }
        public string EffectiveHazardClass { get; set; }

        public double? CeilingHeightFt { get; set; }
        public string CeilingType { get; set; }
        public List<double[]> Polygon { get; set; }

        public List<CeilingData> Ceilings { get; set; }
        public List<ObstacleData> Obstacles { get; set; }
        public List<ExistingSprinklerData> ExistingSprinklers { get; set; }
        public SourceReferenceData Source { get; set; }

        // Per-row overrides (Decisions 017, 018). When non-null these override the
        // universal selection passed to PlacementInputBuilder.Build for THIS room only.
        public string SelectedSprinklerFamilyName { get; set; }
        public string SelectedSprinklerTypeName { get; set; }
        public double? OverrideMaxSpacingFt { get; set; }
        public double? OverrideBoundaryClearanceFt { get; set; }
    }

    /// <summary>
    /// Step 2 — small Backend-internal delegate that resolves a (family, type) pair
    /// to a plain, Revit-free <see cref="DevicePlacementContext"/>. The resolution
    /// happens at the Revit-aware boundary (the <c>RevitSprinklerFamilySource</c>);
    /// only the plain context crosses into the input pipeline.
    /// </summary>
    public delegate DevicePlacementContext DeviceContextResolver(string familyName, string typeName);

    public static class PlacementInputBuilder
    {
        /// <summary>
        /// Original Step 1 overload. Preserved for full backward compatibility
        /// (test harness, any current caller). The per-row device context is left
        /// at its default (<see cref="DevicePlacementBehavior.Unknown"/>,
        /// <c>null</c> placement type) — i.e. identical to pre-Step-2 behavior.
        /// </summary>
        public static PlacementInputSnapshot Build(
            string projectName,
            string selectedFamilyName,
            string selectedTypeName,
            IEnumerable<PlacementRoomSelection> roomSelections)
        {
            return Build(projectName, selectedFamilyName, selectedTypeName, roomSelections, null);
        }

        /// <summary>
        /// Step 2 overload. When <paramref name="resolver"/> is supplied, the
        /// builder resolves the per-row Revit's <c>FamilyPlacementType</c> at the
        /// Revit-aware boundary and stamps the plain, Revit-free device context
        /// onto every <see cref="PlacementRoomInput"/>. The per-row family is the
        /// source of truth (Decision 017); the universal family is only a fallback
        /// when the row has no per-row value.
        ///
        /// When <paramref name="resolver"/> is <c>null</c> the builder behaves
        /// exactly like the original Step 1 overload.
        /// </summary>
        public static PlacementInputSnapshot Build(
            string projectName,
            string selectedFamilyName,
            string selectedTypeName,
            IEnumerable<PlacementRoomSelection> roomSelections,
            DeviceContextResolver resolver)
        {
            PlacementInputSnapshot snapshot = new PlacementInputSnapshot
            {
                SchemaVersion = "1.0",
                TimestampUtc = DateTime.UtcNow.ToString("o"),
                Units = new UnitsInfo
                {
                    Length = "ft",
                    Area = "sq_ft",
                    Volume = "cu_ft",
                    Angle = "degrees"
                },
                CoordinateSystem = new CoordinateSystemInfo
                {
                    Canonical = "host_mep_model",
                    LengthUnit = "feet"
                },
                Project = new ProjectInfo
                {
                    Name = projectName ?? "RevitModel",
                    Standard = "NFPA13-2022",
                    TimestampUtc = DateTime.UtcNow.ToString("o")
                },
                Sprinkler = new SelectedSprinklerInfo(selectedFamilyName, selectedTypeName)
            };

            // Step 2 — resolve the universal (top-level) device context once, when
            // a resolver is available. The result also seeds the snapshot-level
            // SelectedSprinklerInfo so existing diagnostic paths (which already
            // read snapshot.Sprinkler) keep working unchanged.
            if (resolver != null && !string.IsNullOrWhiteSpace(selectedFamilyName) && !string.IsNullOrWhiteSpace(selectedTypeName))
            {
                DevicePlacementContext universal = SafeResolve(resolver, selectedFamilyName, selectedTypeName);
                snapshot.Sprinkler.FamilyPlacementType = universal.FamilyPlacementType;
                snapshot.Sprinkler.PlacementBehavior = universal.PlacementBehavior;
            }

            double totalArea = 0.0;

            if (roomSelections != null)
            {
                foreach (PlacementRoomSelection sel in roomSelections)
                {
                    if (sel == null) continue;

                    totalArea += sel.AreaSqFt;

                    List<double[]> polyCopy = new List<double[]>();
                    if (sel.Polygon != null)
                    {
                        foreach (double[] pt in sel.Polygon)
                        {
                            if (pt != null && pt.Length >= 2)
                            {
                                polyCopy.Add(new double[] { pt[0], pt[1] });
                            }
                        }
                    }

                    BoundaryData boundary = new BoundaryData
                    {
                        Polygon = polyCopy,
                        OuterLoop = new BoundaryLoopData
                        {
                            IsOuter = true,
                            Polygon = polyCopy
                        }
                    };

                    // Per-row family is the source of truth (Decision 017). Fall
                    // back to the universal selection only when the row is empty.
                    string rowFamily = !string.IsNullOrEmpty(sel.SelectedSprinklerFamilyName)
                        ? sel.SelectedSprinklerFamilyName
                        : selectedFamilyName;
                    string rowType = !string.IsNullOrEmpty(sel.SelectedSprinklerTypeName)
                        ? sel.SelectedSprinklerTypeName
                        : selectedTypeName;

                    DevicePlacementContext rowContext = new DevicePlacementContext
                    {
                        FamilyName = rowFamily,
                        TypeName = rowType,
                        FamilyPlacementType = null,
                        PlacementBehavior = DevicePlacementBehavior.Unknown,
                        Resolved = false,
                        FailureReason = null
                    };

                    // Step 2 — resolve the per-row device context only at the
                    // Revit-aware boundary. Without a resolver, the context
                    // stays at the Step 1 default (Unknown / null) — preserving
                    // current candidate behavior byte-for-byte.
                    if (resolver != null && !string.IsNullOrWhiteSpace(rowFamily) && !string.IsNullOrWhiteSpace(rowType))
                    {
                        rowContext = SafeResolve(resolver, rowFamily, rowType);
                    }

                    // Step 2 (sidewall) — derive the per-row orientation string the
                    // calculation engine reads on the room input. Order of
                    // precedence:
                    //   1. The resolved behavior, when it is mount-specific
                    //      (WallSidewall -> "sidewall", CeilingOverhead ->
                    //      "pendent"). This keeps legacy snapshots that already
                    //      have a resolver wired working.
                    //   2. The catalog's Mount string (e.g. "Sidewall",
                    //      "Pendent", "Upright"), lower-cased. This is the
                    //      production path when the resolver carries the
                    //      family-level bucket but the catalog has the row.
                    //   3. null (no orientation set) — preserves pre-Step-2
                    //      behavior byte-for-byte.
                    string orientation = null;
                    if (rowContext.PlacementBehavior == DevicePlacementBehavior.WallSidewall)
                    {
                        orientation = "sidewall";
                    }
                    else if (rowContext.PlacementBehavior == DevicePlacementBehavior.CeilingOverhead)
                    {
                        orientation = "pendent";
                    }
                    else if (!string.IsNullOrWhiteSpace(rowContext.Mount))
                    {
                        string m = rowContext.Mount.Trim();
                        if (m.IndexOf("sidewall", StringComparison.OrdinalIgnoreCase) >= 0)
                            orientation = "sidewall";
                        else if (m.IndexOf("pendent", StringComparison.OrdinalIgnoreCase) >= 0)
                            orientation = "pendent";
                        else if (m.IndexOf("upright", StringComparison.OrdinalIgnoreCase) >= 0)
                            orientation = "upright";
                    }

                    PlacementRoomInput roomInput = new PlacementRoomInput
                    {
                        LevelId = sel.LevelId,
                        LevelName = sel.LevelName,
                        LevelElevationFt = sel.LevelElevationFt,
                        RoomId = sel.RoomId,
                        RoomName = sel.RoomName,
                        RoomNumber = sel.RoomNumber,
                        AreaSqFt = sel.AreaSqFt,
                        VolumeCuFt = sel.VolumeCuFt,
                        EffectiveHazardClass = sel.EffectiveHazardClass,
                        CeilingHeightFt = sel.CeilingHeightFt,
                        CeilingType = sel.CeilingType ?? "NONE",
                        BoundaryPolygon = polyCopy,
                        Boundary = boundary,
                        Ceilings = sel.Ceilings ?? new List<CeilingData>(),
                        Obstacles = sel.Obstacles ?? new List<ObstacleData>(),
                        ExistingSprinklers = sel.ExistingSprinklers ?? new List<ExistingSprinklerData>(),
                        Source = sel.Source ?? new SourceReferenceData(),
                        SelectedSprinklerFamilyName = sel.SelectedSprinklerFamilyName,
                        SelectedSprinklerTypeName = sel.SelectedSprinklerTypeName,
                        // Step 2 — plain, Revit-free device context carried on the row.
                        // Calculation engine does not read these in Step 2 (intentional).
                        SelectedSprinklerFamilyPlacementType = rowContext.FamilyPlacementType,
                        SelectedSprinklerPlacementBehavior = rowContext.PlacementBehavior,
                        // Step 2 (sidewall) — derived from the resolved behavior + the
                        // catalog's Mount signal. Activates the 0.85 sidewall factor
                        // in HazardPlacementRuleSet.GetOrientationAdjustment and the
                        // WallSidewall candidate branch in BruteForceCalculationService.
                        SelectedSprinklerOrientation = orientation,
                        OverrideMaxSpacingFt = sel.OverrideMaxSpacingFt,
                        OverrideBoundaryClearanceFt = sel.OverrideBoundaryClearanceFt
                    };

                    snapshot.Rooms.Add(roomInput);
                }
            }

            snapshot.TotalAreaSqFt = totalArea;
            return snapshot;
        }

        /// <summary>
        /// Step 2 safety wrapper around the resolver: any unexpected exception in
        /// the resolver MUST NOT abort the snapshot build. An exception becomes
        /// an explicit <see cref="DevicePlacementBehavior.Unsupported"/> context
        /// with a populated <see cref="DevicePlacementContext.FailureReason"/>
        /// — per the Step 2 error-handling rule (never silently downgraded).
        /// </summary>
        private static DevicePlacementContext SafeResolve(
            DeviceContextResolver resolver, string familyName, string typeName)
        {
            try
            {
                DevicePlacementContext ctx = resolver(familyName, typeName);
                return ctx ?? new DevicePlacementContext
                {
                    FamilyName = familyName,
                    TypeName = typeName,
                    PlacementBehavior = DevicePlacementBehavior.Unsupported,
                    Resolved = false,
                    FailureReason = "Resolver returned null."
                };
            }
            catch (Exception ex)
            {
                return new DevicePlacementContext
                {
                    FamilyName = familyName,
                    TypeName = typeName,
                    PlacementBehavior = DevicePlacementBehavior.Unsupported,
                    Resolved = false,
                    FailureReason = "Resolver threw: " + ex.Message
                };
            }
        }
    }
}
