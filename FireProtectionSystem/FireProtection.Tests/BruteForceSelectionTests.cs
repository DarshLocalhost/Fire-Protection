using System;
using System.Collections.Generic;
using System.Linq;
using FireProtection.UI.Models;
using FireProtection.UI.Models.Sprinklers.BruteForce;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Sprinklers.BruteForce;

namespace FireProtection.Tests
{
    /// <summary>
    /// Revit-free VM-level tests for the 3-state eligibility + selection contract
    /// (master prompt SPRINKLER_SELECTION_LINKED_MODEL_PRODUCTION_MASTER_PROMPT.md, §22 A-G, K).
    /// Linked-model scenarios H/I/J require the Revit-enabled Backend and are covered by
    /// architecture reuse + manual Revit acceptance (see SESSION_NOTES).
    /// </summary>
    internal sealed class FakePlacementService : ISprinklerPlacementService
    {
        private readonly Dictionary<string, PlacementEligibilityResult> _map =
            new Dictionary<string, PlacementEligibilityResult>(StringComparer.OrdinalIgnoreCase);

        public void Set(string roomId, PlacementEligibilityResult r) => _map[roomId] = r;

        // Phase 7 added the progress reporter and the duplicate-handling policy to the contract.
        // These tests cover the eligibility/selection contract only, so both are ignored here.
        public SprinklerPlacementResult PlaceSprinklers(
            string selectedFamilyName, string selectedTypeName, BruteForceCalculationResult calcResult,
            IPlacementProgress progress, ExistingDevicePolicy existingDevicePolicy) => null;

        public PlacementEligibilityResult EvaluateRoomEligibility(
            RoomUiData room, IReadOnlyList<CalculatedSprinklerPoint> candidates,
            string selectedFamilyName, string selectedTypeName) =>
            _map.TryGetValue(room?.RoomId ?? string.Empty, out var r)
                ? r
                : PlacementEligibilityResult.Undetermined("no-result", PlacementEligibilityStatusCodes.PreflightError);

        public void ClearEligibilityCache() { }

        // Decision 017 added this member to ISprinklerPlacementService. These tests cover the
        // eligibility/selection contract only, so the fake reports "no missing families".
        public IReadOnlyList<MissingFamilyEntry> ProbeMissingFamilies(BruteForceCalculationResult calcResult) =>
            new List<MissingFamilyEntry>();
    }

    public static class BruteForceSelectionTests
    {
        private static RoomUiData MakeRoom(string id, string name, string levelName, string hazard) => new RoomUiData
        {
            RoomId = id,
            Name = name,
            LevelName = levelName,
            Classification = new ClassificationUiData { HazardClass = hazard }
        };

        private static SprinklerBruteForceViewModel Build(FakePlacementService svc)
        {
            var a = MakeRoom("A", "Room A", "L1", "Light");
            var b = MakeRoom("B", "Room B", "L1", "Light");
            var c = MakeRoom("C", "Room C", "L1", "Light");
            var d = MakeRoom("D", "Room D", "L2", "Light");

            var l1 = new LevelUiData { LevelId = "L1", Name = "Level 1", Rooms = new List<RoomUiData> { a, b, c } };
            var l2 = new LevelUiData { LevelId = "L2", Name = "Level 2", Rooms = new List<RoomUiData> { d } };
            var data = new FireProtectionUiData
            {
                Project = new ProjectUiData { Name = "P" },
                Levels = new List<LevelUiData> { l1, l2 }
            };

            svc.Set("A", PlacementEligibilityResult.Eligible());
            svc.Set("B", PlacementEligibilityResult.Eligible());
            svc.Set("C", PlacementEligibilityResult.Blocked(PlacementEligibilityStatusCodes.NoUsableCeilingHost, "no ceiling"));
            svc.Set("D", PlacementEligibilityResult.Undetermined("why", PlacementEligibilityStatusCodes.CalculationFailed));

            var vm = new SprinklerBruteForceViewModel(data, null, null, svc);

            var fam = new SprinklerFamilyOption
            {
                FamilyName = "F",
                Types = new List<SprinklerTypeOption> { new SprinklerTypeOption { FamilyName = "F", TypeName = "T" } }
            };
            vm.SelectedSprinklerFamily = fam;
            vm.SelectedSprinklerType = new SprinklerTypeOption { FamilyName = "F", TypeName = "T" };
            return vm;
        }

        private static RoomItemViewModel Find(SprinklerBruteForceViewModel vm, string id) =>
            vm.AllRooms.First(r => r.Room?.RoomId == id);

        private static void Assert(bool cond, string msg)
        {
            if (!cond) throw new Exception("FAIL: " + msg);
        }

        public static void RunAll()
        {
            // A — default selection (all ELIGIBLE selected; BLOCKED/UNDETERMINED unchecked)
            var svc = new FakePlacementService();
            var vm = Build(svc);
            Assert(Find(vm, "A").IsSelected, "A eligible -> selected by default");
            Assert(Find(vm, "B").IsSelected, "B eligible -> selected by default");
            Assert(!Find(vm, "C").IsSelected && !Find(vm, "C").IsEligible, "C blocked -> unchecked");
            Assert(!Find(vm, "D").IsSelected && !Find(vm, "D").IsEligible, "D undetermined -> unchecked");

            // B / C — the single Room toggle is a real toggle: `select = !AreAllSelectableRoomsSelected`.
            // Block A just established that the eligible rooms are ALREADY selected by default
            // (ApplyDefaultSelection), so the first press is the "Clear All" half and the second is the
            // "Select All" half. Both halves must leave BLOCKED/UNDETERMINED rooms untouched.
            Assert(vm.AreAllSelectableRoomsSelected, "toggle starts in the 'Clear All' state");

            vm.ToggleSelectAllRoomsCommand.Execute(null);
            Assert(!Find(vm, "A").IsSelected && !Find(vm, "B").IsSelected, "Clear All deselects eligible A/B");
            Assert(!Find(vm, "C").IsSelected && !Find(vm, "D").IsSelected, "Clear All leaves non-eligible unchecked");

            vm.ToggleSelectAllRoomsCommand.Execute(null);
            Assert(Find(vm, "A").IsSelected && Find(vm, "B").IsSelected, "Select All selects eligible A/B");
            Assert(!Find(vm, "C").IsSelected && !Find(vm, "D").IsSelected, "Select All skips non-eligible C/D");

            // D / E — level selection synchronizes only its eligible rooms (A,B are selected here).
            var l1 = vm.Levels[0];
            l1.IsSelected = false;
            Assert(!Find(vm, "A").IsSelected && !Find(vm, "B").IsSelected, "Clearing L1 deselects its eligible rooms");
            l1.IsSelected = true;
            Assert(Find(vm, "A").IsSelected && Find(vm, "B").IsSelected, "Selecting L1 selects its eligible rooms");
            Assert(!Find(vm, "D").IsSelected, "Other selected level (L2) rooms unaffected by L1 toggle");

            // F — hazard change invalidates + recalculates the affected room
            svc.Set("A", PlacementEligibilityResult.Blocked(PlacementEligibilityStatusCodes.NoUsableCeilingHost, "now blocked"));
            Find(vm, "A").SelectedHazardClass = "EH2";
            Assert(!Find(vm, "A").IsEligible && !Find(vm, "A").IsSelected, "Hazard change recalculates; A now blocked+unchecked");

            // G — family/type change invalidates + recalculates
            svc.Set("B", PlacementEligibilityResult.Blocked(PlacementEligibilityStatusCodes.NoUsableCeilingHost, "now blocked"));
            var fam2 = new SprinklerFamilyOption
            {
                FamilyName = "F2",
                Types = new List<SprinklerTypeOption> { new SprinklerTypeOption { FamilyName = "F2", TypeName = "T" } }
            };
            vm.SelectedSprinklerFamily = fam2;
            vm.SelectedSprinklerType = new SprinklerTypeOption { FamilyName = "F2", TypeName = "T" };
            Assert(!Find(vm, "B").IsEligible && !Find(vm, "B").IsSelected, "Family/type change recalculates; B now blocked");

            // K — invariant: no non-eligible room is ever selected
            foreach (var r in vm.AllRooms)
                Assert(r.IsEligible || !r.IsSelected, "Invariant violated for room " + (r.Room?.RoomId ?? "?"));

            Console.WriteLine("BruteForceSelectionTests: PASS");
        }
    }
}
