using System;
using System.Collections.Generic;
using FireProtection.UI.Models;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Devices;
using FireProtection.UI.ViewModels.Sprinklers.BruteForce;

namespace FireProtection.Tests
{
    /// <summary>
    /// Revit-free regression tests for three UI contracts that are easy to break silently: the short
    /// eligibility labels shown in the sprinkler room grid, the "a family change selects that family's
    /// first type immediately" cascade on both room-row ViewModels, and the device row's level-default
    /// vs row-override resolution (Decision 019).
    /// Signals failure by throwing, so it runs under Program.RunGuarded.
    /// </summary>
    public static class UiDefaultsTests
    {
        private static void Assert(bool cond, string msg)
        {
            if (!cond) throw new Exception("FAIL: " + msg);
        }

        private static readonly Dictionary<string, IReadOnlyList<string>> TypesByFamily =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "F1", new List<string> { "F1-A", "Shared" } },
                { "F2", new List<string> { "F2-A", "Shared" } },
                { "F3", new List<string>() }
            };

        private static IReadOnlyList<string> Resolve(string family)
        {
            IReadOnlyList<string> types;
            return family != null && TypesByFamily.TryGetValue(family, out types)
                ? types
                : new List<string>();
        }

        private static LevelUiData OneRoomLevel() => new LevelUiData
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

        public static void RunAll()
        {
            // --- Short eligibility labels -------------------------------------------------
            Assert(EligibilityShortText.For(PlacementEligibilityStatusCodes.NoUsableCeilingHost, "anything")
                   == "No ceiling host", "NO_USABLE_CEILING_HOST -> 'No ceiling host'");
            Assert(EligibilityShortText.For(PlacementEligibilityStatusCodes.PendingFamilySelection, null)
                   == "Pick family/type", "PENDING_FAMILY_SELECTION -> 'Pick family/type'");
            Assert(EligibilityShortText.For(PlacementEligibilityStatusCodes.NoCandidatePoints, null)
                   == "No candidate points", "NO_CANDIDATE_POINTS -> 'No candidate points'");

            // An unmapped code degrades to the first sentence of the reason, never to blank.
            Assert(EligibilityShortText.For("SOME_FUTURE_CODE", "Short reason. Second sentence.")
                   == "Short reason", "unmapped code -> first sentence of the reason");
            string longLabel = EligibilityShortText.For(
                "SOME_FUTURE_CODE",
                "This reason has no period so it must be truncated to stay inside the column");
            Assert(longLabel.Length <= 35 && longLabel.EndsWith("…"),
                "unmapped code -> long reason truncated with an ellipsis");
            Assert(EligibilityShortText.For("SOME_FUTURE_CODE", null) == string.Empty,
                "unmapped code with no reason -> empty, not null");

            // --- Sprinkler room row: family change re-points Type at the family's first type ---
            var sprinklerRoom = new LevelItemViewModel(OneRoomLevel()).Rooms[0];
            sprinklerRoom.SetTypesResolver(Resolve);
            sprinklerRoom.AvailableFamilies = new List<string> { "F1", "F2", "F3" };

            sprinklerRoom.SelectedFamily = "F1";
            Assert(sprinklerRoom.SelectedType == "F1-A", "sprinkler row: F1 -> first type F1-A");

            // "Shared" exists under F2 as well, so only a forced cascade moves off it.
            sprinklerRoom.SelectedType = "Shared";
            sprinklerRoom.SelectedFamily = "F2";
            Assert(sprinklerRoom.SelectedType == "F2-A",
                "sprinkler row: family change forces first type even when the old type still exists");

            // Re-seeding the list (startup / catalog reload) must NOT discard a still-valid pick.
            sprinklerRoom.SelectedType = "Shared";
            sprinklerRoom.RefreshAvailableTypesForSelectedFamily();
            Assert(sprinklerRoom.SelectedType == "Shared",
                "sprinkler row: re-seed keeps a type the family still offers");

            sprinklerRoom.SelectedFamily = "F3";
            Assert(sprinklerRoom.SelectedType == null, "sprinkler row: family with no types -> null type");

            sprinklerRoom.SetEligibility(PlacementEligibilityResult.Blocked(
                PlacementEligibilityStatusCodes.MissingRoomGeometry,
                "The room has no closed boundary loop in the model."));
            Assert(sprinklerRoom.EligibilityShortReason == "No room geometry",
                "sprinkler row: short reason follows the status code");
            Assert(sprinklerRoom.EligibilityReason.Contains("closed boundary"),
                "sprinkler row: full reason kept for the tooltip");

            // --- Device room row: same cascade ------------------------------------------------
            var deviceRoom = new DeviceLevelItemViewModel(OneRoomLevel()).Rooms[0];
            deviceRoom.SetTypesResolver(Resolve);
            deviceRoom.AvailableFamilies = new List<string> { "F1", "F2" };

            deviceRoom.SelectedFamily = "F1";
            Assert(deviceRoom.SelectedType == "F1-A", "device row: F1 -> first type F1-A");

            deviceRoom.SelectedType = "Shared";
            deviceRoom.SelectedFamily = "F2";
            Assert(deviceRoom.SelectedType == "F2-A",
                "device row: family change forces first type even when the old type still exists");

            deviceRoom.SelectedType = "Shared";
            deviceRoom.RefreshAvailableTypesForSelectedFamily();
            Assert(deviceRoom.SelectedType == "Shared",
                "device row: re-seed keeps a type the family still offers");

            // --- Device room row: level default vs row override -------------------------------
            // The device tabs have no eligibility column; what decides a placement value is this
            // two-bag resolution, so it is what the regression test guards.
            deviceRoom.SetLevelDefault("DetectorType", "Photoelectric");
            Assert(deviceRoom.GetEffective("DetectorType") == "Photoelectric",
                "device row: level default applies when the row has no override");
            Assert(!deviceRoom.IsOverridden("DetectorType"),
                "device row: a level default is not an override");

            deviceRoom.SetOverride("DetectorType", "Ionization");
            Assert(deviceRoom.GetEffective("DetectorType") == "Ionization",
                "device row: row override beats the level default");
            Assert(deviceRoom.IsOverridden("DetectorType"), "device row: override is reported as one");

            // A later level-default change must not silently overwrite the user's row override.
            deviceRoom.SetLevelDefault("DetectorType", "Beam");
            Assert(deviceRoom.GetEffective("DetectorType") == "Ionization",
                "device row: override survives a level-default change");

            deviceRoom.ClearAllOverrides();
            Assert(deviceRoom.GetEffective("DetectorType") == "Beam",
                "device row: clearing overrides falls back to the current level default");

            Console.WriteLine("  PASS: UiDefaultsTests (short labels + family -> first type cascade)");
        }
    }
}
