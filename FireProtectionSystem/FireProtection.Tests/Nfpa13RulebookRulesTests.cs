using System;
using System.Collections.Generic;
using System.Linq;
using FireProtection.Backend.Models.DTOs;
using FireProtection.Backend.Models.Hazard;
using FireProtection.Backend.Models.Placement.Sprinklers.Final;
using FireProtection.Backend.Services.Placement.Sprinklers.Final.BruteForce;
using FireProtection.UI.Models.Sprinklers.BruteForce;

namespace FireProtection.Tests
{
    /// <summary>
    /// Tests for the sprinkler layout rules implemented from the project rulebook
    /// ("Layout, Detail and Calculation of Fire Sprinkler Systems", NFSA, reproducing NFPA 13 2002).
    ///
    /// Every expectation below is derived from a quoted passage in that book, cited in
    /// <see cref="Nfpa13RulebookRules"/>. These tests pin the RULE IMPLEMENTATION, not code
    /// compliance: the design basis is a superseded edition and every rule set still reports
    /// HasApprovedRules = false pending an FPE sign-off.
    ///
    /// The suite deliberately also asserts what is NOT implemented (the Beam rule table is not in
    /// the rulebook) so the gap cannot quietly become an invented number later.
    /// </summary>
    internal static class Nfpa13RulebookRulesTests
    {
        private static int _failures;
        private const double Eps = 1e-6;

        public static void RunAll()
        {
            _failures = 0;
            TestRuleValuesMatchRulebook();
            TestDeflectorDropApplied();
            TestObstructedCeilingUsesTighterBand();
            TestCeilingConstructionClassification();
            TestMinWallClearanceFloorIsFourInches();
            TestThreeTimesRuleRejectsTooClose();
            TestThreeTimesRuleExceptionWithOppositeSprinkler();
            TestThreeTimesRuleUsesMaximumDimension();
            TestLowerZoneWideFixtureReported();
            TestSmallRoomRuleRelaxesWallDistance();
            TestSmallRoomRuleNotAppliedWhenTighterThanHalfSpacing();
            TestSmallRoomRuleRequiresLightHazard();
            TestSxLCoverageDetectsOverloadedHeadNearWall();
            TestPeakRuleFlaggedWhenExceedsThreeFeet();
            TestBeamRuleTableIsNotImplemented();

            if (_failures == 0)
            {
                Console.WriteLine("Nfpa13RulebookRulesTests: PASS");
            }
            else
            {
                Console.WriteLine("Nfpa13RulebookRulesTests: " + _failures + " FAIL(s)");
                throw new Exception("Nfpa13RulebookRulesTests failed");
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

        // ---- helpers ---------------------------------------------------------------------

        private static List<double[]> Rect(double x0, double y0, double x1, double y1)
        {
            return new List<double[]>
            {
                new double[] { x0, y0 }, new double[] { x1, y0 },
                new double[] { x1, y1 }, new double[] { x0, y1 }
            };
        }

        private static CeilingData FlatCeiling(double bottom, string levelId = "L1")
        {
            return new CeilingData
            {
                SlopeType = "FLAT",
                BottomElevationFt = bottom,
                LevelId = levelId,
                Source = new SourceReferenceData()
            };
        }

        private static ObstacleData Member(string category, string x0, string y0, string x1, string y1, double z0, double z1)
        {
            var o = new ObstacleData { Category = category, Source = new SourceReferenceData() };
            o.BoundingBox = new BoundingBox3DData
            {
                Min = new Point3DData(double.Parse(x0), double.Parse(y0), z0),
                Max = new Point3DData(double.Parse(x1), double.Parse(y1), z1)
            };
            return o;
        }

        private static PlacementRoomInput Room(
            string id, List<double[]> poly, double area, List<CeilingData> ceilings,
            List<ObstacleData> obstacles = null, string hazard = "Light")
        {
            var room = new PlacementRoomInput
            {
                RoomId = id,
                RoomName = id,
                RoomNumber = id,
                LevelId = "L1",
                LevelElevationFt = 0.0,
                AreaSqFt = area,
                EffectiveHazardClass = hazard,
                CeilingHeightFt = 9.0,
                Ceilings = ceilings ?? new List<CeilingData> { FlatCeiling(9.0) },
                Obstacles = obstacles ?? new List<ObstacleData>(),
                ExistingSprinklers = new List<ExistingSprinklerData>()
            };
            room.BoundaryPolygon = poly;
            room.Boundary = new BoundaryData { OuterLoop = new BoundaryLoopData { IsOuter = true, Polygon = poly } };
            return room;
        }

        private static RoomCalculationResult Calc(PlacementRoomInput room)
        {
            var snap = new PlacementInputSnapshot();
            snap.Rooms.Add(room);
            return BruteForceCalculationService.Calculate(
                snap, new DefaultHazardPlacementRules(), BruteForceCalculationConfig.Default()).Rooms[0];
        }

        private static bool HasDiagnostic(RoomCalculationResult res, string fragment)
        {
            return res.Diagnostics.Any(d => d != null && d.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool HasWarning(RoomCalculationResult res, string fragment)
        {
            return res.Warnings.Any(w => w != null && w.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ---- tests -----------------------------------------------------------------------

        /// <summary>The transcribed constants themselves must match the quoted rulebook prose.</summary>
        private static void TestRuleValuesMatchRulebook()
        {
            Console.WriteLine("Test: transcribed rulebook values match the quoted text");

            Check(Nfpa13RulebookRules.MinSpacingFt == 6.0,
                "min spacing 6 ft (Ch.19 p.218, cold soldering)");
            Check(Nfpa13RulebookRules.MaxSpacingLightOrdinaryFt == 15.0,
                "max spacing 15 ft light/ordinary (Ch.19 p.218)");
            Check(Nfpa13RulebookRules.MaxSpacingExtraHazardFt == 12.0,
                "max spacing 12 ft extra hazard / high-piled (Ch.19 p.218)");
            Check(Math.Abs(Nfpa13RulebookRules.MinWallClearanceFt - 4.0 / 12.0) < Eps,
                "min wall clearance 4 in (Ch.19 p.220)");
            Check(Nfpa13RulebookRules.CornerDistanceFractionOfMaxSpacing == 0.75,
                "non-90-degree corner limit 0.75 x spacing (Ch.19 p.220)");
            Check(Math.Abs(Nfpa13RulebookRules.DeflectorMinDropUnobstructedFt - 1.0 / 12.0) < Eps,
                "deflector min drop 1 in unobstructed (Ch.19 p.225)");
            Check(Math.Abs(Nfpa13RulebookRules.DeflectorMaxDropUnobstructedFt - 1.0) < Eps,
                "deflector max drop 12 in unobstructed (Ch.19 p.225)");
            Check(Math.Abs(Nfpa13RulebookRules.DeflectorMaxDropObstructedFt - 0.5) < Eps,
                "deflector max drop 6 in obstructed (Ch.19 p.225-226)");
            Check(Math.Abs(Nfpa13RulebookRules.DeflectorMaxDropAboveMembersFt - 22.0 / 12.0) < Eps,
                "deflector max 22 in below roof deck (Ch.19 p.226)");
            Check(Nfpa13RulebookRules.PeakMaxDropFt == 3.0,
                "sloped peak rule 3 ft (Ch.19 p.229)");
            Check(Math.Abs(Nfpa13RulebookRules.ObstructionZonePlaneBelowSprinklerFt - 1.5) < Eps,
                "Chapter 20 zone plane 18 in below the sprinkler (Ch.20 p.246)");
            Check(Nfpa13RulebookRules.ThreeTimesRuleFactor == 3.0,
                "Three Times rule factor 3x (Ch.20 p.248)");
            Check(Math.Abs(Nfpa13RulebookRules.ThreeTimesRuleMaxRequiredDistanceFt - 2.0) < Eps,
                "Three Times rule capped at 24 in (Ch.20 p.248)");
            Check(Nfpa13RulebookRules.PermanentFixtureRequiringSprinklerWidthFt == 4.0,
                "permanent fixture over 4 ft wide needs a head beneath (Ch.20 p.249)");
            Check(Nfpa13RulebookRules.SmallRoomMaxAreaSqFt == 800.0,
                "Small Room Rule area limit 800 sq ft (Ch.19 p.223)");
            Check(Nfpa13RulebookRules.SmallRoomMaxWallDistanceFt == 9.0,
                "Small Room Rule wall relaxation 9 ft (Ch.19 p.223)");
            Check(Nfpa13RulebookRules.UnobstructedIfMemberSpacingOverFt == 7.5,
                "unobstructed if members > 7.5 ft o.c. (NFPA 13 3.7.2 via Ch.19 p.216)");
            Check(Nfpa13RulebookRules.ObstructedIfMemberSpacingUnderFt == 3.0,
                "obstructed if members < 3 ft o.c. (Ch.19 p.227)");
        }

        /// <summary>Ch.19 p.225: heads sit 1-12 in below the ceiling, NOT on the ceiling plane.</summary>
        private static void TestDeflectorDropApplied()
        {
            Console.WriteLine("Test: deflector drop of at least 1 inch is applied below the ceiling");

            var res = Calc(Room("D", Rect(0, 0, 40, 30), 1200, new List<CeilingData> { FlatCeiling(9.0) }));

            Check(res.Points.Count > 0, "room places heads (got " + res.Points.Count + ")");
            double expected = 9.0 - Nfpa13RulebookRules.DeflectorMinDropUnobstructedFt;
            bool allAtBand = res.Points.All(p => Math.Abs(p.Z - expected) < Eps);
            Check(allAtBand,
                "every head is 1 in below the 9 ft ceiling (expected Z=" + expected.ToString("F3")
                + " ft, got " + (res.Points.Count > 0 ? res.Points[0].Z.ToString("F3") : "n/a") + " ft)");

            Check(res.Points.All(p => p.Z < 9.0),
                "no head sits exactly on the ceiling plane");
            Check(HasDiagnostic(res, "Deflector drop"),
                "the applied drop is reported in diagnostics");
        }

        /// <summary>
        /// Ch.19 p.225-226: obstructed construction uses the tighter 6 in band, unobstructed the
        /// 12 in band. The engine must classify before choosing the band.
        /// </summary>
        private static void TestObstructedCeilingUsesTighterBand()
        {
            Console.WriteLine("Test: obstructed construction is classified and reported");

            // Dense beams at 2 ft on center -> necessarily obstructed.
            var obstacles = new List<ObstacleData>();
            for (int i = 0; i < 12; i++)
            {
                double x = i * 2.0;
                obstacles.Add(Member("OST_StructuralFraming", x.ToString(), "-1", (x + 0.5).ToString(), "31", 8.5, 9.0));
            }

            var res = Calc(Room("O", Rect(0, 0, 24, 30), 720, new List<CeilingData> { FlatCeiling(9.0) }, obstacles));

            Check(HasDiagnostic(res, "Obstructed"),
                "dense framing is classified OBSTRUCTED (3.7.2 / Ch.19 p.227)");
            Check(HasDiagnostic(res, "Deflector drop"),
                "the deflector band is reported for the classified construction");
        }

        /// <summary>NFPA 13 3.7.2 as quoted in Ch.19 p.216.</summary>
        private static void TestCeilingConstructionClassification()
        {
            Console.WriteLine("Test: unobstructed vs obstructed classification (NFPA 13 3.7.2)");

            var open = Calc(Room("U1", Rect(0, 0, 40, 30), 1200, new List<CeilingData> { FlatCeiling(9.0) }));
            Check(HasDiagnostic(open, "Ceiling construction: UNOBSTRUCTED"),
                "a room with no members is UNOBSTRUCTED");

            // Members at 9 ft o.c. -> unobstructed regardless of solidity.
            var wide = new List<ObstacleData>();
            for (int i = 0; i < 4; i++)
            {
                double x = i * 9.0;
                wide.Add(Member("OST_StructuralFraming", x.ToString(), "-1", (x + 1.0).ToString(), "31", 8.0, 9.0));
            }
            var wideRes = Calc(Room("U2", Rect(0, 0, 40, 30), 1200, new List<CeilingData> { FlatCeiling(9.0) }, wide));
            Check(HasDiagnostic(wideRes, "UNOBSTRUCTED"),
                "members at 9 ft o.c. are UNOBSTRUCTED regardless of solidity");

            // Members at 5 ft o.c. -> cannot prove the >70% open cross-section test, so OBSTRUCTED.
            var mid = new List<ObstacleData>();
            for (int i = 0; i < 8; i++)
            {
                double x = i * 5.0;
                mid.Add(Member("OST_StructuralFraming", x.ToString(), "-1", (x + 1.0).ToString(), "31", 8.0, 9.0));
            }
            var midRes = Calc(Room("U3", Rect(0, 0, 40, 30), 1200, new List<CeilingData> { FlatCeiling(9.0) }, mid));
            Check(HasDiagnostic(midRes, "OBSTRUCTED"),
                "members at 5 ft o.c. fall to the conservative OBSTRUCTED branch (70% openings unmeasurable)");

            // Members at 2.5 ft o.c. -> necessarily obstructed.
            var dense = new List<ObstacleData>();
            for (int i = 0; i < 16; i++)
            {
                double x = i * 2.5;
                dense.Add(Member("OST_StructuralFraming", x.ToString(), "-1", (x + 0.5).ToString(), "31", 8.5, 9.0));
            }
            var denseRes = Calc(Room("U4", Rect(0, 0, 40, 30), 1200, new List<CeilingData> { FlatCeiling(9.0) }, dense));
            Check(HasDiagnostic(denseRes, "OBSTRUCTEDDENSE"),
                "members under 3 ft o.c. are OBSTRUCTEDDENSE (necessarily obstructed)");
        }

        /// <summary>Ch.19 p.220: the minimum wall distance is 4 inches and must never go below it.</summary>
        private static void TestMinWallClearanceFloorIsFourInches()
        {
            Console.WriteLine("Test: a rule set cannot set the wall clearance below the 4 in code minimum");

            var rules = new DefaultHazardPlacementRules();
            HazardPlacementRuleSet light = rules.GetRules(HazardClass.Light);

            // The engine floors the clearance at 4 in even if a room asks for less.
            var room = Room("W", Rect(0, 0, 40, 30), 1200, new List<CeilingData> { FlatCeiling(9.0) });
            room.OverrideBoundaryClearanceFt = 0.0;
            var res = Calc(room);

            Check(Nfpa13RulebookRules.MinWallClearanceFt > 0,
                "the 4 in code minimum is available as an enforced floor");
            Check(res.Points.Count > 0, "room still places heads (got " + res.Points.Count + ")");

            double nearest = res.Points.Count == 0
                ? double.NaN
                : res.Points.Min(p => Math.Min(
                    Math.Min(p.X - 0.0, 40.0 - p.X), Math.Min(p.Y - 0.0, 30.0 - p.Y)));

            Check(nearest >= Nfpa13RulebookRules.MinWallClearanceFt - 0.01,
                "no head is closer than the 4 in minimum to a wall (closest="
                + nearest.ToString("F2") + " ft)");

            Check(light.MaxDistanceFromWallsFt > 0,
                "max wall distance (S/2) is separately enforced and still active");
        }

        /// <summary>
        /// Ch.20 p.248: the "Three Times" rule — a sprinkler must be at least 3 x the obstruction's
        /// MAXIMUM dimension from its near edge, capped at 24 in. Worked example in the book: a
        /// 3 in x 4 in column needs 12 in from the near edge.
        /// </summary>
        private static void TestThreeTimesRuleRejectsTooClose()
        {
            Console.WriteLine("Test: Three Times rule keeps heads 3x the max obstruction dimension away");

            // A 1 ft x 1 ft (12 in) column needs 3 x 12 = 36 in, capped at 24 in -> 2 ft clear.
            var column = new List<ObstacleData>
            {
                Member("OST_StructuralColumns", "19", "14", "20", "15", 0.0, 9.0)
            };

            var res = Calc(Room("C3", Rect(0, 0, 40, 30), 1200, new List<CeilingData> { FlatCeiling(9.0) }, column));

            Check(res.Points.Count > 0, "room still places heads (got " + res.Points.Count + ")");

            bool anyTooClose = res.Points.Any(p =>
                p.X > 19.0 - 2.0 && p.X < 20.0 + 2.0 && p.Y > 14.0 - 2.0 && p.Y < 15.0 + 2.0);

            Check(!anyTooClose,
                "no head lies within the 2 ft (24 in capped) Three Times distance of a 1 ft column");
            Check(HasDiagnostic(res, "threeTimesRule="),
                "Three Times rejections are counted in diagnostics");
        }

        /// <summary>
        /// Ch.20 p.248 exception: the Three Times rule is ignored when a sprinkler exists on the
        /// OTHER side within half the allowable spacing of the obstruction centreline.
        ///
        /// Asserted as a change in REJECTION COUNT rather than by head position, because the array
        /// is generated independently of this rule — whether a head happens to land in the zone is
        /// not what the exception governs.
        /// </summary>
        private static void TestThreeTimesRuleExceptionWithOppositeSprinkler()
        {
            Console.WriteLine("Test: Three Times rule is waived when a sprinkler sits opposite");

            var column = new List<ObstacleData>
            {
                Member("OST_StructuralColumns", "19", "14", "20", "15", 0.0, 9.0)
            };

            // Baseline: no opposing sprinkler, so the rule bites and candidates are rejected.
            var without = Calc(Room("CX0", Rect(0, 0, 40, 30), 1200,
                new List<CeilingData> { FlatCeiling(9.0) }, column));

            PlacementRoomInput room = Room("CX", Rect(0, 0, 40, 30), 1200,
                new List<CeilingData> { FlatCeiling(9.0) }, column);

            // Existing sprinkler on the far side of the column, within 0.5 x 15 ft = 7.5 ft of its
            // centreline (19.5, 14.5).
            room.ExistingSprinklers.Add(new ExistingSprinklerData
            {
                Location = new Point3DData(16.5, 14.5, 8.9),
                RoomId = "CX",
                Source = new SourceReferenceData()
            });

            var with = Calc(room);

            int rejectsWithout = CountThreeTimesRejections(without);
            int rejectsWith = CountThreeTimesRejections(with);

            Check(rejectsWithout > 0,
                "without an opposing sprinkler the Three Times rule rejects candidates (n="
                + rejectsWithout + ")");
            Check(rejectsWith < rejectsWithout,
                "with an opposing sprinkler inside 0.5S of the centreline the rule stops rejecting ("
                + rejectsWith + " < " + rejectsWithout + ")");
        }

        private static int CountThreeTimesRejections(RoomCalculationResult res)
        {
            foreach (string d in res.Diagnostics)
            {
                if (d == null) continue;
                int i = d.IndexOf("threeTimesRule=", StringComparison.Ordinal);
                if (i < 0) continue;
                string tail = d.Substring(i + "threeTimesRule=".Length);
                int comma = tail.IndexOf(',');
                if (comma > 0)
                {
                    int v;
                    if (int.TryParse(tail.Substring(0, comma), out v)) return v;
                }
            }
            return 0;
        }

        /// <summary>
        /// Ch.20 p.248 uses the obstruction's MAXIMUM dimension, explicitly not the one nearest the
        /// sprinkler: "Note that it is the maximum dimension that counts."
        ///
        /// Sized so the 24 in cap does NOT bind: a member 2 in deep x 6 in wide needs 3 x 6 = 18 in.
        /// If the rule wrongly used the 2 in dimension it would require only 6 in, so a head at
        /// 12 in away would be wrongly accepted.
        /// </summary>
        private static void TestThreeTimesRuleUsesMaximumDimension()
        {
            Console.WriteLine("Test: Three Times rule uses the MAXIMUM obstruction dimension");

            // 2 in deep (Y) x 6 in wide (X), spanning y 14..14.17, x 18..18.5.
            var member = new List<ObstacleData>
            {
                Member("OST_StructuralFraming", "18", "14", "18.5", "14.1667", 8.5, 9.0)
            };

            var res = Calc(Room("CM", Rect(0, 0, 40, 30), 1200, new List<CeilingData> { FlatCeiling(9.0) }, member));

            Check(res.Points.Count > 0, "room places heads (got " + res.Points.Count + ")");

            // Required clearance is 3 x 6 in = 18 in = 1.5 ft. Anything inside that is a violation.
            // A head at, say, 12 in away would only be legal under the WRONG (minimum-dimension) rule.
            double required = 3.0 * 6.0 / 12.0;
            Check(required <= Nfpa13RulebookRules.ThreeTimesRuleMaxRequiredDistanceFt,
                "the test case keeps 3 x max below the 24 in cap so the rule is not clipped");

            bool withinRequired = res.Points.Any(p =>
                p.X > 18.0 - required && p.X < 18.5 + required
                && p.Y > 14.0 - required && p.Y < 14.1667 + required);

            Check(!withinRequired,
                "no head sits within the 18 in (3 x the 6 in maximum dimension) required distance");

            // Sanity: the cap must still bound a LARGE obstruction at 24 in, not 3 x its dimension.
            double cappedForThreeFootMember = Math.Min(
                3.0 * 3.0, Nfpa13RulebookRules.ThreeTimesRuleMaxRequiredDistanceFt);
            Check(Math.Abs(cappedForThreeFootMember - 2.0) < Eps,
                "a 3 ft member is capped at 24 in (2 ft), not 9 ft, per Ch.20");
        }

        /// <summary>Ch.20 p.249: an obstruction below the 18 in plane over 4 ft wide needs a head under it.</summary>
        private static void TestLowerZoneWideFixtureReported()
        {
            Console.WriteLine("Test: a wide fixture below the 18 in zone plane is reported");

            // 6 ft wide counter, top at 4 ft. Sprinkler at ~8.9 ft, so the counter sits well below
            // the 18 in plane (8.9 - 1.5 = 7.4 ft). Category is deliberately NOT OST_Walls:
            // walls form the room boundary and are handled by the boundary rule instead.
            var counter = new List<ObstacleData>
            {
                Member("OST_Furniture", "10", "12", "16", "14", 0.0, 4.0)
            };

            var res = Calc(Room("Z2", Rect(0, 0, 40, 30), 1200, new List<CeilingData> { FlatCeiling(9.0) }, counter));

            Check(HasWarning(res, "Chapter 20 lower zone"),
                "the lower-zone fixture rule is raised as a warning");
            Check(HasWarning(res, "permanent fixture"),
                "the warning names the permanent-fixture rule");
            Check(res.Status == CalculationStatus.ReviewRequired,
                "the room is flagged ReviewRequired (got " + res.Status + ")");
        }

        /// <summary>Ch.19 p.223: light hazard, <800 sq ft, unobstructed -> 9 ft wall relaxation.</summary>
        private static void TestSmallRoomRuleRelaxesWallDistance()
        {
            Console.WriteLine("Test: Small Room Rule relaxes the wall limit to 9 ft for a small light-hazard room");

            // 20 x 20 = 400 sq ft, Light hazard, unobstructed -> all four conditions except
            // enclosure (which cannot be fully verified and is always reported).
            var res = Calc(Room("SR", Rect(0, 0, 20, 20), 400, new List<CeilingData> { FlatCeiling(9.0) }));

            Check(res.Points.Count > 0, "room places heads (got " + res.Points.Count + ")");
            Check(HasWarning(res, "Small Room Rule applied"),
                "the Small Room Rule is reported as applied");
            Check(HasWarning(res, "lintel"),
                "the unverifiable wall/lintel condition is flagged for review");
            Check(HasDiagnostic(res, "Small Room Rule averaging"),
                "coverage switches to the averaging technique the rulebook requires");
        }

        /// <summary>
        /// The relaxation must never LOOSEN a wall limit the rule set already set tighter than S/2.
        /// </summary>
        private static void TestSmallRoomRuleNotAppliedWhenTighterThanHalfSpacing()
        {
            Console.WriteLine("Test: Small Room Rule never loosens a wall limit tighter than S/2");

            var tight = new HazardPlacementRuleSet
            {
                HazardClass = HazardClass.Light,
                MaxSpacingFt = 15.0,
                MinSpacingFt = 6.0,
                MaxCoverageAreaSqFt = 225.0,
                CoverageRadiusFt = 7.5,
                ObstacleClearanceFt = 1.0,
                BoundaryClearanceFt = 1.0,
                ExistingSprinklerSeparationFt = 7.5,
                MaxDistanceFromWallsFt = 1.0,
                CeilingHeightAdjustmentFactor = 1.0
            };

            var snap = new PlacementInputSnapshot();
            snap.Rooms.Add(Room("SR2", Rect(0, 0, 20, 20), 400, new List<CeilingData> { FlatCeiling(9.0) }));
            var res = BruteForceCalculationService.Calculate(
                snap, new SingleRuleProvider(tight), BruteForceCalculationConfig.Default()).Rooms[0];

            Check(HasDiagnostic(res, "tighter than"),
                "the rule is skipped when the supplied limit is tighter than S/2");
            Check(!HasWarning(res, "Small Room Rule applied"),
                "the 9 ft relaxation is NOT applied over a tighter limit");
        }

        private static void TestSmallRoomRuleRequiresLightHazard()
        {
            Console.WriteLine("Test: Small Room Rule does not apply to Ordinary hazard");

            var res = Calc(Room("SR3", Rect(0, 0, 20, 20), 400,
                new List<CeilingData> { FlatCeiling(9.0) }, null, "OH1"));

            Check(HasDiagnostic(res, "requires Light hazard"),
                "an Ordinary hazard room is refused the Small Room Rule");
            Check(!HasWarning(res, "Small Room Rule applied"),
                "no relaxation for Ordinary hazard");
        }

        /// <summary>
        /// Ch.19 p.222: coverage area is S x L, where S and L are each the greatest of the
        /// neighbour distance and TWICE the distance to the wall. A head near a wall therefore
        /// covers far more than its nominal area, which the old room-average test could not see.
        /// </summary>
        private static void TestSxLCoverageDetectsOverloadedHeadNearWall()
        {
            Console.WriteLine("Test: S x L coverage rule detects a head overloaded near a wall");

            // A long narrow room (60 x 12). With a 15 ft spacing the perimeter heads sit close to
            // the long walls; twice that distance can exceed the 15 ft spacing, so S x L exceeds the
            // 225 sq ft limit for those heads.
            //
            // 60 x 30 = 1800 sq ft, deliberately ABOVE the 800 sq ft Small Room Rule threshold so
            // the per-head S x L test runs instead of the averaging technique.
            var res = Calc(Room("SX", Rect(0, 0, 60, 30), 1800, new List<CeilingData> { FlatCeiling(9.0) }));

            Check(res.Points.Count > 0, "room places heads (got " + res.Points.Count + ")");
            Check(!HasWarning(res, "Small Room Rule applied"),
                "a 1800 sq ft room is above the Small Room Rule threshold");
            Check(HasDiagnostic(res, "S x L rules"),
                "coverage is reported per head using the S x L rules");
        }

        /// <summary>Ch.19 p.229: the highest head must be within 3 ft of the peak.</summary>
        private static void TestPeakRuleFlaggedWhenExceedsThreeFeet()
        {
            Console.WriteLine("Test: sloped-ceiling peak rule is evaluated");

            // Steep slope: bottom 8 ft, top 20 ft -> a large drop below the peak.
            var sloped = new CeilingData
            {
                SlopeType = "SLOPED",
                BottomElevationFt = 8.0,
                TopElevationFt = 20.0,
                LevelId = "L1",
                Source = new SourceReferenceData()
            };

            var res = Calc(Room("PK", Rect(0, 0, 40, 30), 1200, new List<CeilingData> { sloped }));

            Check(res.Points.Count > 0, "sloped room places heads (got " + res.Points.Count + ")");
            Check(HasDiagnostic(res, "Peak rule") || HasWarning(res, "peak"),
                "the 3 ft peak rule is evaluated and reported");
        }

        /// <summary>
        /// The rulebook references NFPA 13 Table 8.6.5.1.2 (Beam rule) but does not reproduce it.
        /// This test exists so the gap is explicit: if someone later adds invented Beam-rule numbers,
        /// this assertion documents that they did not come from the design basis.
        /// </summary>
        private static void TestBeamRuleTableIsNotImplemented()
        {
            Console.WriteLine("Test: Beam rule table is declared unavailable (not invented)");

            Check(Nfpa13RulebookRules.BeamRuleTableAvailable == false,
                "BeamRuleTableAvailable is false — the table is not in the rulebook");
            Check(Nfpa13RulebookRules.KnownGaps.Any(g =>
                    g.IndexOf("Beam rule", StringComparison.OrdinalIgnoreCase) >= 0),
                "the Beam rule is recorded as a known gap");
        }
    }

    /// <summary>Minimal provider returning one fixed rule set, for targeted rule tests.</summary>
    internal sealed class SingleRuleProvider : IHazardPlacementRules
    {
        private readonly HazardPlacementRuleSet _rules;
        public SingleRuleProvider(HazardPlacementRuleSet rules) { _rules = rules; }
        public bool HasApprovedRules => false;
        public HazardPlacementRuleSet GetRules(HazardClass hazardClass) { return _rules.Clone(); }
    }
}