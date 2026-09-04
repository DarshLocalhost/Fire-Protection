using System;
using System.Collections.Generic;
using FireProtection.UI.Models;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Sprinklers.BruteForce;

namespace FireProtection.Tests
{
    /// <summary>
    /// Revit-free regression tests for the Phase 7 additions that are easy to break silently:
    /// display-unit conversion/parsing, the inline validation on the numeric override cells
    /// (a bad entry must keep the last good value), and the duplicate-handling policy mapping.
    /// Signals failure by throwing, so it runs under Program.RunGuarded.
    /// </summary>
    public static class Phase7Tests
    {
        private static void Assert(bool cond, string msg)
        {
            if (!cond) throw new Exception("FAIL: " + msg);
        }

        public static void RunAll()
        {
            TestUnitDisplayFeetDefault();
            TestUnitDisplayMillimetres();
            TestCellValidation();
            TestExistingDevicePolicy();
            Console.WriteLine("  PASS: Phase7Tests (units, cell validation, duplicate policy)");
        }

        private static RoomItemViewModel NewRoomRow()
        {
            LevelUiData level = new LevelUiData
            {
                LevelId = "L1",
                Name = "Level 1",
                Rooms = new List<RoomUiData>
                {
                    new RoomUiData
                    {
                        RoomId = "R1",
                        Name = "Room 1",
                        LevelName = "Level 1",
                        Classification = new ClassificationUiData { HazardClass = "Light" }
                    }
                }
            };

            return new LevelItemViewModel(level).Rooms[0];
        }

        /// <summary>Feet is the fallback when the project unit could not be read; nothing must scale.</summary>
        private static void TestUnitDisplayFeetDefault()
        {
            UnitDisplay.Configure("ft", 1.0, 2);

            Assert(UnitDisplay.IsFeet, "feet: IsFeet true");
            Assert(UnitDisplay.HeaderSuffix == "(FT)", "feet: header suffix is (FT)");
            Assert(Math.Abs(UnitDisplay.FromFeet(12.0) - 12.0) < 1e-9, "feet: FromFeet is identity");
            Assert(UnitDisplay.Format(null) == "—", "feet: null formats as an em dash");

            double parsed;
            string error;
            Assert(UnitDisplay.TryParseToFeet("12.5", out parsed, out error) && Math.Abs(parsed - 12.5) < 1e-9,
                "feet: '12.5' parses to 12.5 ft");
            Assert(!UnitDisplay.TryParseToFeet("abc", out parsed, out error) && !string.IsNullOrEmpty(error),
                "feet: non-numeric text is refused with a message");
            Assert(!UnitDisplay.TryParseToFeet("-3", out parsed, out error), "feet: negative is refused");
            Assert(!UnitDisplay.TryParseToFeet("0", out parsed, out error), "feet: zero is refused");
        }

        /// <summary>A metric project shows and reads millimetres while storage stays in decimal feet.</summary>
        private static void TestUnitDisplayMillimetres()
        {
            UnitDisplay.Configure("mm", 304.8, 0);
            try
            {
                Assert(!UnitDisplay.IsFeet, "mm: IsFeet false");
                Assert(UnitDisplay.HeaderSuffix == "(MM)", "mm: header suffix is (MM)");
                Assert(Math.Abs(UnitDisplay.FromFeet(1.0) - 304.8) < 1e-6, "mm: 1 ft displays as 304.8 mm");
                Assert(Math.Abs(UnitDisplay.ToFeet(304.8) - 1.0) < 1e-9, "mm: 304.8 mm stores as 1 ft");

                double parsed;
                string error;
                Assert(UnitDisplay.TryParseToFeet("3048", out parsed, out error) && Math.Abs(parsed - 10.0) < 1e-9,
                    "mm: '3048' parses to 10 ft");
            }
            finally
            {
                // Static state: leave the harness in feet so test order cannot matter.
                UnitDisplay.Configure("ft", 1.0, 2);
            }
        }

        /// <summary>The override cells must refuse bad input at the cell and keep the last good override.</summary>
        private static void TestCellValidation()
        {
            RoomItemViewModel row = NewRoomRow();

            Assert(row.MaxSpacingInput == string.Empty, "cell: no override starts blank, not '—'");
            Assert(!row.HasMaxSpacingError, "cell: no error before anything is typed");

            row.MaxSpacingInput = "12";
            Assert(row.MaxSpacingFtOverride.HasValue && Math.Abs(row.MaxSpacingFtOverride.Value - 12.0) < 1e-9,
                "cell: '12' sets a 12 ft override");
            Assert(!row.HasMaxSpacingError, "cell: a valid entry clears the error");

            row.MaxSpacingInput = "abc";
            Assert(row.HasMaxSpacingError, "cell: non-numeric text raises the error flag");
            Assert(row.MaxSpacingFtOverride.HasValue && Math.Abs(row.MaxSpacingFtOverride.Value - 12.0) < 1e-9,
                "cell: a rejected entry keeps the last good override");
            Assert(row.MaxSpacingInput == "abc", "cell: the rejected text stays visible for correction");
            Assert(row.MaxSpacingTooltip == row.MaxSpacingError, "cell: tooltip shows the error while invalid");

            row.MaxSpacingInput = "-4";
            Assert(row.HasMaxSpacingError, "cell: negative is refused");
            row.MaxSpacingInput = "0";
            Assert(row.HasMaxSpacingError, "cell: zero is refused");

            row.MaxSpacingInput = string.Empty;
            Assert(!row.MaxSpacingFtOverride.HasValue, "cell: blank clears the override");
            Assert(!row.HasMaxSpacingError, "cell: blank is not an error");
            Assert(row.MaxSpacingTooltip != null && row.MaxSpacingTooltip.Contains("Leave blank"),
                "cell: tooltip falls back to the help text when valid");

            // The wall-clearance cell is the same contract on its own backing field.
            row.BoundaryClearanceInput = "1.5";
            Assert(row.BoundaryClearanceFtOverride.HasValue
                && Math.Abs(row.BoundaryClearanceFtOverride.Value - 1.5) < 1e-9,
                "clearance cell: '1.5' sets a 1.5 ft override");
            row.BoundaryClearanceInput = "x";
            Assert(row.HasBoundaryClearanceError, "clearance cell: non-numeric refused");
            Assert(Math.Abs(row.BoundaryClearanceFtOverride.Value - 1.5) < 1e-9,
                "clearance cell: last good value kept");
            Assert(!row.HasMaxSpacingError, "clearance cell: its error does not leak into the spacing cell");

            // Reset must also refresh the visible text, not just the backing field.
            row.MaxSpacingInput = "11";
            row.ResetSpacingOverridesToDefault();
            Assert(!row.MaxSpacingFtOverride.HasValue, "reset: clears the spacing override");
            Assert(row.MaxSpacingInput == string.Empty, "reset: the cell text follows the reset");
        }

        /// <summary>Skip is the default so a second Place run never silently doubles up devices.</summary>
        private static void TestExistingDevicePolicy()
        {
            Assert(ExistingDevicePolicyOptions.Labels.Length == 3, "policy: three options offered");
            Assert(ExistingDevicePolicyOptions.Labels[0] == ExistingDevicePolicyOptions.SkipRoomLabel,
                "policy: Skip is listed first (the default selection)");

            Assert(ExistingDevicePolicyOptions.Parse(ExistingDevicePolicyOptions.SkipRoomLabel)
                == ExistingDevicePolicy.SkipRoom, "policy: Skip label round-trips");
            Assert(ExistingDevicePolicyOptions.Parse(ExistingDevicePolicyOptions.AddAnywayLabel)
                == ExistingDevicePolicy.AddAnyway, "policy: Add-anyway label round-trips");
            Assert(ExistingDevicePolicyOptions.Parse(ExistingDevicePolicyOptions.ReplaceExistingLabel)
                == ExistingDevicePolicy.ReplaceExisting, "policy: Replace label round-trips");

            Assert(ExistingDevicePolicyOptions.Parse(null) == ExistingDevicePolicy.SkipRoom,
                "policy: null falls back to Skip");
            Assert(ExistingDevicePolicyOptions.Parse("something else") == ExistingDevicePolicy.SkipRoom,
                "policy: an unknown label falls back to Skip");

            Assert(!NullPlacementProgress.Instance.IsCancellationRequested,
                "progress: the null reporter never reports a cancellation");
        }
    }
}
