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
    }

    public static class PlacementInputBuilder
    {
        public static PlacementInputSnapshot Build(
            string projectName,
            string selectedFamilyName,
            string selectedTypeName,
            IEnumerable<PlacementRoomSelection> roomSelections)
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
                        Source = sel.Source ?? new SourceReferenceData()
                    };

                    snapshot.Rooms.Add(roomInput);
                }
            }

            snapshot.TotalAreaSqFt = totalArea;
            return snapshot;
        }
    }
}
