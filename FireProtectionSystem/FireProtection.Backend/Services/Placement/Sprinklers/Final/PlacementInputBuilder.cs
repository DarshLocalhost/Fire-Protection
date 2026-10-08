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

        public string SelectedSprinklerFamilyName { get; set; }
        public string SelectedSprinklerTypeName { get; set; }
        public double? OverrideMaxSpacingFt { get; set; }
        public double? OverrideBoundaryClearanceFt { get; set; }
        public double? OverrideMaxDistanceToWallFt { get; set; }
        public double? OverrideCeilingTileUFt { get; set; }
        public double? OverrideCeilingTileVFt { get; set; }
        public string SelectedSprinklerOrientation { get; set; }
        public double? TypeMaxCoverageAreaSqFt { get; set; }
        public double? TypeMaxSpacingFt { get; set; }
        public double? TypeMinSpacingFt { get; set; }
        public double? TypeCoverageRadiusFt { get; set; }
        public string SprinklerClass { get; set; }
    }

    public delegate DevicePlacementContext DeviceContextResolver(string familyName, string typeName);

    public static class PlacementInputBuilder
    {
        public static PlacementInputSnapshot Build(
            string projectName,
            string selectedFamilyName,
            string selectedTypeName,
            IEnumerable<PlacementRoomSelection> roomSelections)
        {
            return Build(projectName, selectedFamilyName, selectedTypeName, roomSelections, null);
        }

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
                Units = new UnitsInfo { Length = "ft", Area = "sq_ft", Volume = "cu_ft", Angle = "degrees" },
                CoordinateSystem = new CoordinateSystemInfo { Canonical = "host_mep_model", LengthUnit = "feet" },
                Project = new ProjectInfo { Name = projectName ?? "RevitModel", Standard = "NFPA13-2022", TimestampUtc = DateTime.UtcNow.ToString("o") },
                Sprinkler = new SelectedSprinklerInfo(selectedFamilyName, selectedTypeName)
            };

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
                                polyCopy.Add(new double[] { pt[0], pt[1] });
                        }
                    }

                    BoundaryData boundary = new BoundaryData
                    {
                        Polygon = polyCopy,
                        OuterLoop = new BoundaryLoopData { IsOuter = true, Polygon = polyCopy }
                    };

                    string rowFamily = !string.IsNullOrEmpty(sel.SelectedSprinklerFamilyName) ? sel.SelectedSprinklerFamilyName : selectedFamilyName;
                    string rowType = !string.IsNullOrEmpty(sel.SelectedSprinklerTypeName) ? sel.SelectedSprinklerTypeName : selectedTypeName;

                    DevicePlacementContext rowContext = new DevicePlacementContext
                    {
                        FamilyName = rowFamily,
                        TypeName = rowType,
                        FamilyPlacementType = null,
                        PlacementBehavior = DevicePlacementBehavior.Unknown,
                        Resolved = false,
                        FailureReason = null
                    };

                    if (resolver != null && !string.IsNullOrWhiteSpace(rowFamily) && !string.IsNullOrWhiteSpace(rowType))
                    {
                        rowContext = SafeResolve(resolver, rowFamily, rowType);
                    }

                    // Derive orientation with robust fallback
                    string orientation = null;
                    if (!string.IsNullOrWhiteSpace(sel.SelectedSprinklerOrientation) &&
                        !string.Equals(sel.SelectedSprinklerOrientation.Trim(), "(auto)", StringComparison.OrdinalIgnoreCase))
                    {
                        orientation = sel.SelectedSprinklerOrientation.Trim().ToLowerInvariant();
                    }
                    else if (rowContext.PlacementBehavior == DevicePlacementBehavior.WallSidewall)
                    {
                        orientation = "sidewall";
                    }
                    else if (rowContext.PlacementBehavior == DevicePlacementBehavior.CeilingOverhead)
                    {
                        orientation = "pendent";
                    }
                    else if (!string.IsNullOrWhiteSpace(rowContext.Mount))
                    {
                        string m = rowContext.Mount.Trim().ToLowerInvariant();
                        if (m.Contains("sidewall")) orientation = "sidewall";
                        else if (m.Contains("pendent")) orientation = "pendent";
                        else if (m.Contains("upright")) orientation = "upright";
                    }

                    if (string.IsNullOrWhiteSpace(orientation))
                    {
                        string combined = ((rowFamily ?? "") + " " + (rowType ?? "")).ToLowerInvariant();
                        if (combined.Contains("sidewall")) orientation = "sidewall";
                        else if (combined.Contains("pendent")) orientation = "pendent";
                        else if (combined.Contains("upright")) orientation = "upright";
                    }

                    if (string.Equals(orientation, "sidewall", StringComparison.OrdinalIgnoreCase) &&
                        rowContext.PlacementBehavior != DevicePlacementBehavior.WallSidewall)
                    {
                        rowContext.PlacementBehavior = DevicePlacementBehavior.WallSidewall;
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
                        SelectedSprinklerFamilyPlacementType = rowContext.FamilyPlacementType,
                        SelectedSprinklerPlacementBehavior = rowContext.PlacementBehavior,
                        SelectedSprinklerOrientation = orientation,
                        TypeMaxCoverageAreaSqFt = sel.TypeMaxCoverageAreaSqFt,
                        TypeMaxSpacingFt = sel.TypeMaxSpacingFt,
                        TypeMinSpacingFt = sel.TypeMinSpacingFt,
                        TypeCoverageRadiusFt = sel.TypeCoverageRadiusFt,
                        SprinklerClass = sel.SprinklerClass,
                        OverrideMaxSpacingFt = sel.OverrideMaxSpacingFt,
                        OverrideBoundaryClearanceFt = sel.OverrideBoundaryClearanceFt,
                        OverrideMaxDistanceToWallFt = sel.OverrideMaxDistanceToWallFt,
                        OverrideCeilingTileUFt = sel.OverrideCeilingTileUFt,
                        OverrideCeilingTileVFt = sel.OverrideCeilingTileVFt
                    };

                    snapshot.Rooms.Add(roomInput);
                }
            }

            snapshot.TotalAreaSqFt = totalArea;
            return snapshot;
        }

        private static DevicePlacementContext SafeResolve(DeviceContextResolver resolver, string familyName, string typeName)
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
