using System;
using System.Collections.Generic;
using System.Linq;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce;
using FireProtection.UI.Models.Sprinklers.BruteForce;

namespace FireProtection.Tests
{
    /// <summary>
    /// WS-B: verifies that per-type catalog numbers (carried on <see cref="PlacementRoomInput"/> as
    /// TypeMaxSpacingFt / TypeMinSpacingFt / TypeCoverageRadiusFt / TypeMaxCoverageAreaSqFt) merge over
    /// the hazard baseline, clamped to the NFPA-13 hazard ceiling, and that ESFR/CMSA classes raise a
    /// review flag without fabricating storage numbers. Blank per-type values must reproduce the
    /// baseline layout exactly.
    /// </summary>
    internal static class PerTypeCatalogMergeTests
    {
        private static int _failures;

        public static void RunAll()
        {
            _failures = 0;
            TestTighterTypeSpacingDrivesMoreHeads();
            TestTypeSpacingClampedToHazardCeiling();
            TestBlankTypeValuesReproduceBaseline();
            TestMergeRecordsCatalogSourceDiagnostic();
            TestEsfrClassFlagsReview();

            if (_failures == 0)
            {
                Console.WriteLine("PerTypeCatalogMergeTests: PASS");
            }
            else
            {
                Console.WriteLine("PerTypeCatalogMergeTests: " + _failures + " FAIL(s)");
                throw new Exception("PerTypeCatalogMergeTests failed");
            }
        }

        private static void Check(bool condition, string message)
        {
            if (condition) Console.WriteLine("  PASS: " + message);
            else
            {
                Console.WriteLine("  FAIL: " + message);
                _failures++;
            }
        }

        private static List<double[]> Rect(double x0, double y0, double x1, double y1)
        {
            return new List<double[]>
            {
                new double[] { x0, y0 },
                new double[] { x1, y0 },
                new double[] { x1, y1 },
                new double[] { x0, y1 }
            };
        }

        private static CeilingData FlatCeiling(double bottomElevationFt)
        {
            return new CeilingData
            {
                SlopeType = "FLAT",
                BottomElevationFt = bottomElevationFt,
                Source = new SourceReferenceData()
            };
        }

        private static PlacementRoomInput MakeRoom(
            string id,
            List<double[]> polygon,
            string hazard = "Light",
            double? typeMaxSpacing = null,
            double? typeMinSpacing = null,
            double? typeCoverageRadius = null,
            double? typeMaxCoverageArea = null,
            string sprinklerClass = null,
            string typeName = null)
        {
            PlacementRoomInput room = new PlacementRoomInput
            {
                RoomId = id,
                RoomName = id,
                RoomNumber = id,
                LevelId = "L1",
                LevelElevationFt = 0.0,
                AreaSqFt = 600.0,
                EffectiveHazardClass = hazard,
                CeilingHeightFt = 9.0,
                Ceilings = new List<CeilingData> { FlatCeiling(9.0) },
                Obstacles = new List<ObstacleData>(),
                ExistingSprinklers = new List<ExistingSprinklerData>()
            };
            room.BoundaryPolygon = polygon;
            room.Boundary = new BoundaryData
            {
                OuterLoop = new BoundaryLoopData { IsOuter = true, Polygon = polygon }
            };
            room.TypeMaxSpacingFt = typeMaxSpacing;
            room.TypeMinSpacingFt = typeMinSpacing;
            room.TypeCoverageRadiusFt = typeCoverageRadius;
            room.TypeMaxCoverageAreaSqFt = typeMaxCoverageArea;
            room.SprinklerClass = sprinklerClass;
            room.SelectedSprinklerTypeName = typeName;
            return room;
        }

        private static BruteForceCalculationResult Calc(PlacementRoomInput room)
        {
            PlacementInputSnapshot snap = new PlacementInputSnapshot();
            snap.Rooms.Add(room);
            return BruteForceCalculationService.Calculate(
                snap, new DefaultHazardPlacementRules(), BruteForceCalculationConfig.Default());
        }

        private static void TestTighterTypeSpacingDrivesMoreHeads()
        {
            Console.WriteLine("Test: a tighter per-type MaxSpacing places more heads than the hazard baseline");
            List<double[]> poly = Rect(0, 0, 30, 20);

            int baseline = Calc(MakeRoom("BASE", poly)).Rooms[0].CalculatedCount;
            // 8 ft is below the 15 ft Light ceiling, so it survives the clamp and densifies the grid.
            int tight = Calc(MakeRoom("TIGHT", poly, typeMaxSpacing: 8.0)).Rooms[0].CalculatedCount;

            Check(baseline > 0, "baseline room produced heads (got " + baseline + ")");
            Check(tight > baseline,
                "per-type 8 ft spacing packs more heads than the 15 ft baseline (baseline=" + baseline + ", tight=" + tight + ")");
        }

        private static void TestTypeSpacingClampedToHazardCeiling()
        {
            Console.WriteLine("Test: an over-max per-type spacing is clamped to the NFPA hazard ceiling");
            List<double[]> poly = Rect(0, 0, 30, 20);

            // An EC type listing 20 ft on a Light room: the clamp holds it at 15 ft, so the layout
            // matches the 15 ft baseline (never a wider, non-compliant grid).
            int baseline = Calc(MakeRoom("BASE", poly)).Rooms[0].CalculatedCount;
            RoomCalculationResult ec = Calc(MakeRoom("EC", poly, typeMaxSpacing: 20.0,
                sprinklerClass: "ExtendedCoverage", typeName: "EC-20")).Rooms[0];

            Check(ec.CalculatedCount == baseline,
                "20 ft EC spacing clamps to 15 ft Light ceiling -> same head count as baseline (baseline="
                + baseline + ", ec=" + ec.CalculatedCount + ")");
            // The applied spacing carried onto the result must be the clamped 15 ft, not 20 ft.
            Check(ec.AppliedMaxSpacingFt.HasValue && Math.Abs(ec.AppliedMaxSpacingFt.Value - 15.0) < 1e-6,
                "applied max spacing is the 15 ft ceiling, not the listed 20 ft (got "
                + (ec.AppliedMaxSpacingFt.HasValue ? ec.AppliedMaxSpacingFt.Value.ToString("F2") : "null") + ")");
        }

        private static void TestBlankTypeValuesReproduceBaseline()
        {
            Console.WriteLine("Test: blank per-type values reproduce the hazard baseline exactly");
            List<double[]> poly = Rect(0, 0, 30, 20);

            RoomCalculationResult a = Calc(MakeRoom("A", poly)).Rooms[0];
            RoomCalculationResult b = Calc(MakeRoom("B", poly)).Rooms[0];

            Check(a.CalculatedCount == b.CalculatedCount,
                "two blank-per-type rooms produce the same head count (" + a.CalculatedCount + " vs " + b.CalculatedCount + ")");
            bool noCatalogNote = a.Diagnostics.All(d => d == null || d.IndexOf("from catalog", StringComparison.OrdinalIgnoreCase) < 0);
            Check(noCatalogNote, "blank per-type values record no 'from catalog' merge diagnostic");
        }

        private static void TestMergeRecordsCatalogSourceDiagnostic()
        {
            Console.WriteLine("Test: a merged per-type value records a catalog-source diagnostic naming the type");
            List<double[]> poly = Rect(0, 0, 30, 20);
            RoomCalculationResult r = Calc(MakeRoom("D", poly, typeMaxSpacing: 10.0, typeName: "SS-10")).Rooms[0];

            bool hasSourceNote = r.Diagnostics.Any(d => d != null
                && d.IndexOf("from catalog", StringComparison.OrdinalIgnoreCase) >= 0
                && d.IndexOf("SS-10", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(hasSourceNote, "diagnostic names the catalog type as the source of the spacing (got "
                + r.Diagnostics.Count + " diagnostics)");
        }

        private static void TestEsfrClassFlagsReview()
        {
            Console.WriteLine("Test: an ESFR class raises the storage-review warning without inventing numbers");
            List<double[]> poly = Rect(0, 0, 30, 20);
            RoomCalculationResult r = Calc(MakeRoom("ESFR", poly, typeMaxSpacing: 12.0,
                sprinklerClass: "ESFR", typeName: "ESFR-K25")).Rooms[0];

            Check(r.Status == CalculationStatus.ReviewRequired,
                "ESFR class flags ReviewRequired (got " + r.Status + ")");
            bool hasStorageNote = r.Warnings.Any(w => w != null
                && w.IndexOf("storage-specific", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(hasStorageNote, "ESFR review warning names the storage-specific design requirement");
        }
    }
}
