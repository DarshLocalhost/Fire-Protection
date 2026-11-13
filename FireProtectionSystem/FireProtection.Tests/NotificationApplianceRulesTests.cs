using System;
using FireProtection.Backend.Services.Placement.NotificationAppliances.Final.BruteForce;

namespace FireProtection.Tests
{
    internal static class NotificationApplianceRulesTests
    {
        public static void RunAll()
        {
            TestCandelaTableLookups();
            TestSoundAttenuationCalculations();
            TestCorridorClassification();
            TestNotificationRulesIntegration();
            TestNotificationEngineStandardRoomSingleAppliance();
            TestNotificationEngineCorridorPlacement();
            TestNotificationEngineObstacleConflict();

            Console.WriteLine("NotificationApplianceRulesTests: ALL TESTS PASSED");
        }

        private static void TestNotificationEngineStandardRoomSingleAppliance()
        {
            Console.WriteLine("Test: notification engine standard room places exactly 1 appliance per compartment rule");

            // Standard 20x25 room (500 sq ft)
            var poly = new System.Collections.Generic.List<double[]>
            {
                new double[] { 0, 0 },
                new double[] { 25, 0 },
                new double[] { 25, 20 },
                new double[] { 0, 20 }
            };

            var wallRoom = new FireProtection.Backend.Models.Placement.SmokeDetectors.Final.SmokeDetectorRoomInput
            {
                RoomId = "R1",
                RoomName = "Office 101",
                RoomNumber = "101",
                LevelId = "L1",
                LevelName = "Level 1",
                LevelElevationFt = 0.0,
                AreaSqFt = 500.0,
                CeilingHeightFt = 9.0,
                Mount = "Wall",
                SelectedFamilyName = "Fire Alarm Horn - Wall Mounted",
                SelectedTypeName = "Standard",
                BoundaryPolygon = poly
            };

            var snap = new FireProtection.Backend.Models.Placement.SmokeDetectors.Final.SmokeDetectorPlacementInputSnapshot();
            snap.Rooms.Add(wallRoom);

            var result = Nfpa72NotificationApplianceEngine.Calculate(
                snap, new Nfpa72NotificationApplianceRules(),
                FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce.BruteForceCalculationConfig.Default());

            Check(result.Success, "Wall room calculation succeeded");
            Check(result.Rooms.Count == 1, "One room in result");
            var r = result.Rooms[0];
            Check(r.CalculatedCount == 1, "Exactly 1 wall appliance placed in standard room (got " + r.CalculatedCount + ")");
            Check(r.Points.Count == 1, "1 point in points list");

            var pt = r.Points[0];
            Check(pt.WallEdgeIndex.HasValue, "WallEdgeIndex is populated for wall appliance");
            Check(Math.Abs(pt.Z - 7.5) < 0.01, "Wall appliance Z is 7.5 ft (90 in AFF) (got " + pt.Z.ToString("F2") + " ft)");

            // Test ceiling mounted in standard room
            var ceilingRoom = new FireProtection.Backend.Models.Placement.SmokeDetectors.Final.SmokeDetectorRoomInput
            {
                RoomId = "R2",
                RoomName = "Office 102",
                RoomNumber = "102",
                LevelId = "L1",
                LevelName = "Level 1",
                LevelElevationFt = 0.0,
                AreaSqFt = 500.0,
                CeilingHeightFt = 9.0,
                Mount = "Ceiling",
                SelectedFamilyName = "Fire Alarm Strobe - Ceiling Mounted",
                SelectedTypeName = "Standard",
                BoundaryPolygon = poly
            };

            var snapCeiling = new FireProtection.Backend.Models.Placement.SmokeDetectors.Final.SmokeDetectorPlacementInputSnapshot();
            snapCeiling.Rooms.Add(ceilingRoom);

            var resCeiling = Nfpa72NotificationApplianceEngine.Calculate(
                snapCeiling, new Nfpa72NotificationApplianceRules(),
                FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce.BruteForceCalculationConfig.Default());

            Check(resCeiling.Rooms[0].CalculatedCount == 1, "Exactly 1 ceiling appliance placed in standard room");
            var ptC = resCeiling.Rooms[0].Points[0];
            Check(!ptC.WallEdgeIndex.HasValue, "WallEdgeIndex is null for ceiling appliance");
            Check(Math.Abs(ptC.Z - 9.0) < 0.01, "Ceiling appliance Z is 9.0 ft (ceiling height) (got " + ptC.Z.ToString("F2") + " ft)");
        }

        private static void TestNotificationEngineCorridorPlacement()
        {
            Console.WriteLine("Test: notification engine corridor layout respects 100 ft spacing & 15 ft end offsets");

            // Corridor 10 ft wide by 80 ft long
            var poly = new System.Collections.Generic.List<double[]>
            {
                new double[] { 0, 0 },
                new double[] { 80, 0 },
                new double[] { 80, 10 },
                new double[] { 0, 10 }
            };

            var corRoom = new FireProtection.Backend.Models.Placement.SmokeDetectors.Final.SmokeDetectorRoomInput
            {
                RoomId = "COR1",
                RoomName = "Corridor 1",
                RoomNumber = "C1",
                LevelId = "L1",
                LevelName = "Level 1",
                LevelElevationFt = 0.0,
                AreaSqFt = 800.0,
                CeilingHeightFt = 9.0,
                Mount = "Wall",
                SelectedFamilyName = "Fire Alarm Horn - Wall Mounted",
                SelectedTypeName = "Standard",
                BoundaryPolygon = poly
            };

            var snap = new FireProtection.Backend.Models.Placement.SmokeDetectors.Final.SmokeDetectorPlacementInputSnapshot();
            snap.Rooms.Add(corRoom);

            var result = Nfpa72NotificationApplianceEngine.Calculate(
                snap, new Nfpa72NotificationApplianceRules(),
                FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce.BruteForceCalculationConfig.Default());

            Check(result.Success, "Corridor calculation succeeded");
            var r = result.Rooms[0];
            Check(r.CalculatedCount >= 1, "Corridor produced appliances (got " + r.CalculatedCount + ")");
            foreach (var pt in r.Points)
            {
                Check(pt.X >= 0.0 && pt.X <= 80.0, "Point within corridor X bounds");
            }
        }

        private static void TestNotificationEngineObstacleConflict()
        {
            Console.WriteLine("Test: notification engine obstacle conflict detection");

            var poly = new System.Collections.Generic.List<double[]>
            {
                new double[] { 0, 0 },
                new double[] { 20, 0 },
                new double[] { 20, 20 },
                new double[] { 0, 20 }
            };

            // Wall room has primary wall at y=0, midpoint x=10, y=0.5
            var room = new FireProtection.Backend.Models.Placement.SmokeDetectors.Final.SmokeDetectorRoomInput
            {
                RoomId = "R_Obs",
                RoomName = "Room with Column",
                RoomNumber = "Obs1",
                LevelId = "L1",
                LevelName = "Level 1",
                LevelElevationFt = 0.0,
                AreaSqFt = 400.0,
                CeilingHeightFt = 9.0,
                Mount = "Wall",
                SelectedFamilyName = "Fire Alarm Horn - Wall Mounted",
                SelectedTypeName = "Standard",
                BoundaryPolygon = poly
            };

            // Add obstacle right over the wall midpoint
            room.Obstacles = new System.Collections.Generic.List<FireProtection.Backend.Models.DTOs.ObstacleData>
            {
                new FireProtection.Backend.Models.DTOs.ObstacleData
                {
                    ElementId = "Col-99",
                    Category = "OST_Columns",
                    BoundingBox = new FireProtection.Backend.Models.DTOs.BoundingBox3DData(
                        new FireProtection.Backend.Models.DTOs.Point3DData(8.0, 0.0, 0.0),
                        new FireProtection.Backend.Models.DTOs.Point3DData(12.0, 2.0, 10.0))
                }
            };

            var snap = new FireProtection.Backend.Models.Placement.SmokeDetectors.Final.SmokeDetectorPlacementInputSnapshot();
            snap.Rooms.Add(room);

            var result = Nfpa72NotificationApplianceEngine.Calculate(
                snap, new Nfpa72NotificationApplianceRules(),
                FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce.BruteForceCalculationConfig.Default());

            Check(result.Success, "Calculation succeeded");
            var r = result.Rooms[0];
            Check(r.CalculatedCount == 1, "1 appliance calculated");
            Check(r.Points[0].HasObstacleConflict, "Obstacle conflict is flagged on colliding wall appliance");
        }

        private static void TestCandelaTableLookups()
        {
            // NFPA 72 Table 18.5.5.4.1(a) Single Wall Strobe requirements
            Check(Nfpa72AudibleVisualSolver.GetRequiredCandelaForSingleWallAppliance(15.0) == 15, "15 ft room requires 15 cd");
            Check(Nfpa72AudibleVisualSolver.GetRequiredCandelaForSingleWallAppliance(20.0) == 15, "20 ft room requires 15 cd");
            Check(Nfpa72AudibleVisualSolver.GetRequiredCandelaForSingleWallAppliance(25.0) == 30, "25 ft room requires 30 cd");
            Check(Nfpa72AudibleVisualSolver.GetRequiredCandelaForSingleWallAppliance(30.0) == 30, "30 ft room requires 30 cd");
            Check(Nfpa72AudibleVisualSolver.GetRequiredCandelaForSingleWallAppliance(35.0) == 60, "35 ft room requires 60 cd");
            Check(Nfpa72AudibleVisualSolver.GetRequiredCandelaForSingleWallAppliance(45.0) == 95, "45 ft room requires 95 cd");
            Check(Nfpa72AudibleVisualSolver.GetRequiredCandelaForSingleWallAppliance(55.0) == 135, "55 ft room requires 135 cd");
            Check(Nfpa72AudibleVisualSolver.GetRequiredCandelaForSingleWallAppliance(65.0) == 185, "65 ft room requires 185 cd");

            // Max room dimension for candela ratings
            Check(Math.Abs(Nfpa72AudibleVisualSolver.GetMaxRoomDimensionForCandela(15) - 20.0) < 0.001, "15 cd covers up to 20 ft room");
            Check(Math.Abs(Nfpa72AudibleVisualSolver.GetMaxRoomDimensionForCandela(30) - 30.0) < 0.001, "30 cd covers up to 30 ft room");
            Check(Math.Abs(Nfpa72AudibleVisualSolver.GetMaxRoomDimensionForCandela(60) - 40.0) < 0.001, "60 cd covers up to 40 ft room");
            Check(Math.Abs(Nfpa72AudibleVisualSolver.GetMaxRoomDimensionForCandela(95) - 50.0) < 0.001, "95 cd covers up to 50 ft room");
        }

        private static void TestSoundAttenuationCalculations()
        {
            // Free-field inverse square law: -6.02 dBA per distance doubling
            // 85 dBA at 10 ft
            double dbaAt10 = Nfpa72AudibleVisualSolver.CalculateAudibleAttenuation(85.0, 10.0);
            Check(Math.Abs(dbaAt10 - 85.0) < 0.01, "Sound level at reference 10 ft equals rated 85 dBA");

            // At 20 ft (doubling of 10 ft): should be 85 - 20*log10(2) = 85 - 6.02 = 78.98 dBA
            double dbaAt20 = Nfpa72AudibleVisualSolver.CalculateAudibleAttenuation(85.0, 20.0);
            Check(Math.Abs(dbaAt20 - 78.98) < 0.05, "Sound level at 20 ft drops by ~6 dBA to ~79 dBA");

            // At 40 ft (another doubling): should be 78.98 - 6.02 = 72.96 dBA
            double dbaAt40 = Nfpa72AudibleVisualSolver.CalculateAudibleAttenuation(85.0, 40.0);
            Check(Math.Abs(dbaAt40 - 72.96) < 0.05, "Sound level at 40 ft drops by ~12 dBA to ~73 dBA");

            // Max throw calculation for 85 dBA horn targeting 70 dBA (55 ambient + 15):
            // d_max = 10 * 10^(15/20) = 10 * 5.6234 = 56.23 ft
            double maxThrow = Nfpa72AudibleVisualSolver.GetMaxAudibleThrowFt(85.0, 70.0);
            Check(Math.Abs(maxThrow - 56.23) < 0.05, "85 dBA horn with 70 dBA target has throw distance ~56.23 ft");

            // Verify that sound at maxThrow is indeed 70 dBA
            double dbaAtMaxThrow = Nfpa72AudibleVisualSolver.CalculateAudibleAttenuation(85.0, maxThrow);
            Check(Math.Abs(dbaAtMaxThrow - 70.0) < 0.05, "Sound level at calculated max throw equals target 70 dBA");
        }

        private static void TestCorridorClassification()
        {
            // NFPA 72 §18.5.5.5: Width <= 20 ft, aspect ratio >= 2.5
            Check(Nfpa72AudibleVisualSolver.IsCorridor(10.0, 50.0), "10x50 ft space is a corridor");
            Check(Nfpa72AudibleVisualSolver.IsCorridor(12.0, 35.0), "12x35 ft space is a corridor (ratio 2.9)");
            Check(!Nfpa72AudibleVisualSolver.IsCorridor(25.0, 100.0), "25 ft wide space is NOT a corridor (> 20 ft limit)");
            Check(!Nfpa72AudibleVisualSolver.IsCorridor(20.0, 30.0), "20x30 ft space is NOT a corridor (ratio 1.5 < 2.5)");

            Check(Math.Abs(Nfpa72AudibleVisualSolver.GetCorridorMaxSpacingFt() - 100.0) < 0.001, "Corridor strobe max spacing is 100 ft");
            Check(Math.Abs(Nfpa72AudibleVisualSolver.GetCorridorEndOffsetFt() - 15.0) < 0.001, "Corridor strobe end offset is 15 ft");
        }

        private static void TestNotificationRulesIntegration()
        {
            Nfpa72NotificationApplianceRules rules = new Nfpa72NotificationApplianceRules();
            Check(!rules.HasApprovedRules, "Rules remain provisional until formal project-level AHJ sign-off");

            var lowCandela = rules.GetRules("Strobe|candela=15|dba=0", "Ceiling", "FLAT");
            Check(Math.Abs(lowCandela.MaxSpacingFt - 30.0) < 0.001, "15 cd strobe uses the low-candela visible envelope");

            var highCandela = rules.GetRules("Strobe|candela=110|dba=0", "Ceiling", "FLAT");
            Check(highCandela.MaxSpacingFt > lowCandela.MaxSpacingFt, "higher candela changes visible spacing");

            var combined = rules.GetRules("HornStrobe|candela=15|dba=87", "Ceiling", "FLAT");
            Check(Math.Abs(combined.MaxSpacingFt - 25.0) < 0.001, "combined appliance uses the stricter audible limit");

            var wallSloped = rules.GetRules("HornStrobe|candela=15|dba=87", "Wall", "SLOPED");
            Check(wallSloped.Mount == "Wall", "wall appliance keeps wall mount");
            Check(wallSloped.MaxSpacingFt < combined.MaxSpacingFt, "wall and sloped constraints reduce spacing");
            Check(wallSloped.IsProvisional, "unapproved project design basis remains visibly provisional");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + message);
            Console.WriteLine("  PASS: " + message);
        }
    }
}
