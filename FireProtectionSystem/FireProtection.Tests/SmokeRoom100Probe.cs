using System;
using System.Collections.Generic;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Placement.SmokeDetectors.Final;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.Backend.Services.Placement.NotificationAppliances.Final.BruteForce;
using FireProtection.Backend.Services.Placement.SmokeDetectors.Final.BruteForce;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce;

namespace FireProtection.Tests
{
    /// <summary>
    /// Temporary probe: runs the smoke detector engine for the real client Room 100
    /// (49'-4" x 19'-4") under Ceiling mount and Wall mount and prints counts,
    /// points, applied rules, and diagnostics. Not part of the regression suite.
    /// </summary>
    internal static class SmokeRoom100Probe
    {
        public static void Run()
        {
            List<double[]> poly = new List<double[]>
            {
                new double[] { 62.456, 49.290 },
                new double[] { 13.123, 49.290 },
                new double[] { 13.123, 29.956 },
                new double[] { 62.456, 29.956 }
            };

            RunCase("CEILING", "Ceiling", DevicePlacementBehavior.CeilingOverhead, poly);
            RunCase("WALL", "Wall", DevicePlacementBehavior.WallSidewall, poly);
        }

        private static void RunCase(string label, string mount, DevicePlacementBehavior behavior, List<double[]> poly)
        {
            Console.WriteLine("==== ROOM 100 SMOKE PROBE: " + label + " MOUNT ====");

            SmokeDetectorRoomInput room = new SmokeDetectorRoomInput
            {
                RoomId = "100",
                RoomName = "Room 100",
                RoomNumber = "100",
                LevelId = "L1",
                LevelElevationFt = 0.0,
                AreaSqFt = 953.75,
                CeilingHeightFt = 9.0,
                Mount = mount,
                CeilingSlope = "FLAT",
                DetectorType = "Photoelectric",
                SelectedPlacementBehavior = behavior,
                BoundaryPolygon = poly,
                Obstacles = new List<ObstacleData>(),
                Ceilings = new List<CeilingData>
                {
                    new CeilingData { SlopeType = "FLAT", BottomElevationFt = 9.0, Source = new SourceReferenceData() }
                }
            };
            room.Boundary = new BoundaryData
            {
                OuterLoop = new BoundaryLoopData { IsOuter = true, Polygon = poly }
            };

            var rules = new Nfpa72SmokeDetectorRules();
            SmokeDetectorPlacementRuleSet rs = rules.GetRules(room.DetectorType, mount, "FLAT", null);
            Console.WriteLine("Rules: S=" + rs.MaxSpacingFt.ToString("F3")
                + " ft, MinS=" + rs.MinSpacingFt.ToString("F2")
                + " ft, Coverage=" + rs.MaxCoverageAreaSqFt.ToString("F1")
                + " sqft, Radius=" + rs.CoverageRadiusFt.ToString("F3")
                + " ft, BoundaryClear=" + rs.MinBoundaryClearanceFt.ToString("F3")
                + " ft, MaxWallDist=" + rs.MaxDistanceFromWallsFt.ToString("F2")
                + " ft, provisional=" + rs.IsProvisional);

            var snapshot = new SmokeDetectorPlacementInputSnapshot();
            snapshot.Rooms.Add(room);

            SmokeDetectorCalculationResult result = SmokeDetectorCalculationService.Calculate(
                snapshot, rules, BruteForceCalculationConfig.Default());

            SmokeDetectorRoomCalculationResult rr = result.Rooms[0];
            Console.WriteLine("Status=" + rr.Status
                + ", CalculatedCount=" + rr.CalculatedCount
                + ", RequiredCount=" + rr.RequiredCount
                + ", AppliedMaxSpacingFt=" + rr.AppliedMaxSpacingFt.ToString("F3")
                + ", AppliedBoundaryClearanceFt=" + rr.AppliedBoundaryClearanceFt.ToString("F3"));

            int i = 1;
            foreach (CalculatedSmokeDetectorPoint p in rr.Points)
            {
                Console.WriteLine("  #" + i
                    + ": X=" + p.X.ToString("F3")
                    + ", Y=" + p.Y.ToString("F3")
                    + ", Z=" + p.Z.ToString("F3")
                    + ", Mount=" + p.Mount
                    + ", WallEdgeIndex=" + p.WallEdgeIndex);
                i++;
            }

            if (rr.Warnings.Count > 0)
            {
                foreach (string w in rr.Warnings) Console.WriteLine("  WARN: " + w);
            }
            if (rr.Errors.Count > 0)
            {
                foreach (string e in rr.Errors) Console.WriteLine("  ERR: " + e);
            }
            foreach (string d in rr.Diagnostics)
            {
                Console.WriteLine("  DIAG: " + d);
            }
            Console.WriteLine();
        }
    }
}
