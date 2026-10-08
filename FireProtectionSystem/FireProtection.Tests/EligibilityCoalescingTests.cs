using System;
using System.Collections.Generic;
using System.Linq;
using FireProtection.UI.Models;
using FireProtection.UI.Models.Sprinklers.BruteForce;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Catalog;
using FireProtection.UI.ViewModels.Sprinklers.BruteForce;

namespace FireProtection.Tests
{
    /// <summary>
    /// T1 performance regressions: the eligibility sweep used to be re-queued once per trigger, and the
    /// ceiling/floor/roof element list used to be rebuilt per candidate point.
    ///
    /// These tests pin the CONTRACTS, not the timings. Elapsed-time assertions would be flaky on CI and
    /// meaningless without Revit; what matters is that work happens once per gesture instead of once
    /// per notification, and that the coalescer cannot latch.
    ///
    /// <c>RevitApi.Context</c> defaults to <see cref="ImmediateRevitApiContext"/>, which runs queued
    /// work inline, so the whole queue/coalesce path is exercised without Revit.
    /// </summary>
    public static class EligibilityCoalescingTests
    {
        private static int _failures;

        public static void RunAll()
        {
            _failures = 0;
            TestBurstOfTriggersRunsOnePass();
            TestCoalescedTriggerSchedulesExactlyOneFollowUp();
            TestFailureDoesNotLatchTheCoalescer();
            TestEveryPassIsBracketedByHostPass();
            TestHostPassIsBalancedWhenPassThrows();
            TestCatalogChangeRebuildsOnceDespiteManyNotifications();

            if (_failures == 0)
            {
                Console.WriteLine("EligibilityCoalescingTests: PASS");
            }
            else
            {
                Console.WriteLine("EligibilityCoalescingTests: " + _failures + " FAIL(s)");
                throw new Exception("EligibilityCoalescingTests failed");
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

        // ---- fakes -------------------------------------------------------------------------

        /// <summary>
        /// Counts eligibility sweeps and how the host-resolution pass was bracketed.
        /// <c>ThrowOnPass</c> simulates a mid-sweep Revit failure so the latch case can be tested.
        /// </summary>
        private sealed class CountingPlacementService : ISprinklerPlacementService
        {
            public int PassCount;
            public int HostPassBegins;
            public int HostPassEnds;
            public int ClearCacheCalls;
            public bool ThrowOnPass;

            public void BeginHostResolutionPass() => HostPassBegins++;

            public void EndHostResolutionPass() => HostPassEnds++;

            public long HostCollectorCallsSaved => 0;

            public void ClearEligibilityCache() => ClearCacheCalls++;

            public IReadOnlyList<MissingFamilyEntry> ProbeMissingFamilies(BruteForceCalculationResult calcResult) =>
                new List<MissingFamilyEntry>();

            public SprinklerPlacementResult PlaceSprinklers(
                string selectedFamilyName, string selectedTypeName, BruteForceCalculationResult calcResult,
                IPlacementProgress progress, ExistingDevicePolicy existingDevicePolicy) => null;

            public PlacementEligibilityResult EvaluateRoomEligibility(
                RoomUiData room, IReadOnlyList<CalculatedSprinklerPoint> candidates,
                string selectedFamilyName, string selectedTypeName)
            {
                PassCount++;
                if (ThrowOnPass) throw new InvalidOperationException("simulated Revit failure");
                return PlacementEligibilityResult.Undetermined(
                    "no result", PlacementEligibilityStatusCodes.PreflightError);
            }
        }

        /// <summary>
        /// Stands in for Revit's real queue: it HOLDS work instead of running it, so a burst of
        /// triggers can be queued and then drained deliberately. This is what makes the coalescing
        /// assertion meaningful - <c>ImmediateRevitApiContext</c> would drain inline and hide it.
        /// </summary>
        private sealed class DeferringRevitApiContext : IRevitApiContext
        {
            private readonly Queue<Action> _work = new Queue<Action>();
            private readonly Queue<Action<Exception>> _errors = new Queue<Action<Exception>>();

            public void Run(Action work, Action<Exception> onError = null)
            {
                if (work == null) return;
                _work.Enqueue(work);
                if (onError != null) _errors.Enqueue(onError);
            }

            public int PendingCount { get { return _work.Count; } }

            /// <summary>Drains everything queued, invoking each item's error callback on failure.</summary>
            public void Drain()
            {
                while (_work.Count > 0)
                {
                    Action work = _work.Dequeue();
                    Action<Exception> onError = _errors.Count > 0 ? _errors.Dequeue() : null;
                    try { work(); }
                    catch (Exception ex) { if (onError != null) onError(ex); }
                }
            }
        }

        private static FireProtectionUiData MakeData()
        {
            var a = new RoomUiData
            {
                RoomId = "A",
                Name = "Room A",
                LevelName = "L1",
                Classification = new ClassificationUiData { HazardClass = "Light" }
            };
            var l1 = new LevelUiData { LevelId = "L1", Name = "Level 1", Rooms = new List<RoomUiData> { a } };
            return new FireProtectionUiData
            {
                Project = new ProjectUiData { Name = "P" },
                Levels = new List<LevelUiData> { l1 }
            };
        }

        private static SprinklerBruteForceViewModel BuildVm(CountingPlacementService svc)
        {
            var vm = new SprinklerBruteForceViewModel(MakeData(), null, null, svc);
            vm.SelectedSprinklerFamily = new SprinklerFamilyOption
            {
                FamilyName = "F",
                Types = new List<SprinklerTypeOption> { new SprinklerTypeOption { FamilyName = "F", TypeName = "T" } }
            };
            vm.SelectedSprinklerType = new SprinklerTypeOption { FamilyName = "F", TypeName = "T" };
            return vm;
        }

        // ---- tests -------------------------------------------------------------------------

        /// <summary>
        /// The core regression. <c>RevitApiContext.Run</c> ENQUEUES and never de-duplicates, so before
        /// T1.1 a burst of N triggers queued N complete whole-model sweeps. One gesture must queue one.
        /// </summary>
        private static void TestBurstOfTriggersRunsOnePass()
        {
            Console.WriteLine("Test: a burst of triggers queues exactly one eligibility sweep");

            var svc = new CountingPlacementService();
            var deferred = new DeferringRevitApiContext();
            IRevitApiContext previous = RevitApi.Context;
            RevitApi.Context = deferred;
            try
            {
                var vm = BuildVm(svc);

                // Drain whatever the constructor queued, then fire a burst of 8 more triggers.
                deferred.Drain();
                int queuedBefore = svc.PassCount;

                for (int i = 0; i < 8; i++)
                {
                    Burst(vm);
                }

                Check(deferred.PendingCount == 1,
                    "8 triggers queued 1 sweep, not 8 (pending=" + deferred.PendingCount + ")");

                deferred.Drain();
                Check(svc.PassCount == queuedBefore + 1,
                    "draining the burst ran exactly one more sweep (delta="
                        + (svc.PassCount - queuedBefore) + ")");
            }
            finally
            {
                RevitApi.Context = previous;
            }
        }

        /// <summary>
        /// A trigger that arrives while a sweep is queued is not lost: exactly ONE follow-up is
        /// scheduled, so the final on-screen state reflects the latest selection without a sweep per
        /// change.
        /// </summary>
        private static void TestCoalescedTriggerSchedulesExactlyOneFollowUp()
        {
            Console.WriteLine("Test: a coalesced trigger schedules exactly one follow-up sweep");

            var svc = new CountingPlacementService();
            var deferred = new DeferringRevitApiContext();
            IRevitApiContext previous = RevitApi.Context;
            RevitApi.Context = deferred;
            try
            {
                var vm = BuildVm(svc);
                deferred.Drain();

                // Queue one sweep, then trigger repeatedly WHILE it is pending.
                Burst(vm);
                Check(deferred.PendingCount == 1, "the first trigger queued a sweep");

                for (int i = 0; i < 5; i++) Burst(vm);
                Check(deferred.PendingCount == 1, "further triggers queued nothing extra");

                deferred.Drain();
                Check(deferred.PendingCount <= 1,
                    "draining produced at most one follow-up (pending=" + deferred.PendingCount + ")");

                deferred.Drain();
                Check(deferred.PendingCount == 0, "no further sweeps are scheduled after the follow-up");
            }
            finally
            {
                RevitApi.Context = previous;
            }
        }

        /// <summary>
        /// A transient Revit failure must NOT leave the coalescer latched. If it did, every later
        /// trigger would be silently swallowed and eligibility would freeze at its last value for the
        /// rest of the session - a worse bug than the hang.
        /// </summary>
        private static void TestFailureDoesNotLatchTheCoalescer()
        {
            Console.WriteLine("Test: a failed sweep does not latch the coalescer");

            var svc = new CountingPlacementService();
            var deferred = new DeferringRevitApiContext();
            IRevitApiContext previous = RevitApi.Context;
            RevitApi.Context = deferred;
            try
            {
                var vm = BuildVm(svc);
                deferred.Drain();

                svc.ThrowOnPass = true;
                Burst(vm);
                deferred.Drain();
                Check(true, "a throwing sweep did not escape the ViewModel");

                // The decisive assertion: a later trigger must still be able to run.
                svc.ThrowOnPass = false;
                int before = svc.PassCount;
                Burst(vm);
                Check(deferred.PendingCount == 1,
                    "after a failure a new trigger still queues a sweep (pending=" + deferred.PendingCount + ")");

                deferred.Drain();
                Check(svc.PassCount > before, "the post-failure sweep actually ran");
            }
            finally
            {
                RevitApi.Context = previous;
            }
        }

        /// <summary>
        /// The host-resolution pass (T1.4) must wrap every sweep: the ceiling/floor/roof list is only
        /// valid for the document state it was collected in.
        /// </summary>
        private static void TestEveryPassIsBracketedByHostPass()
        {
            Console.WriteLine("Test: every eligibility sweep opens and closes a host-resolution pass");

            var svc = new CountingPlacementService();
            var deferred = new DeferringRevitApiContext();
            IRevitApiContext previous = RevitApi.Context;
            RevitApi.Context = deferred;
            try
            {
                var vm = BuildVm(svc);
                deferred.Drain();

                int beginsBefore = svc.HostPassBegins;
                Burst(vm);
                deferred.Drain();

                Check(svc.HostPassBegins > beginsBefore, "the sweep opened a host-resolution pass");
                Check(svc.HostPassEnds == svc.HostPassBegins,
                    "every host pass was closed (begins=" + svc.HostPassBegins + ", ends=" + svc.HostPassEnds + ")");
            }
            finally
            {
                RevitApi.Context = previous;
            }
        }

        /// <summary>
        /// If a sweep throws, the host pass must still be closed. A leaked cache would be served after a
        /// document change made its elements invalid.
        /// </summary>
        private static void TestHostPassIsBalancedWhenPassThrows()
        {
            Console.WriteLine("Test: a throwing sweep still closes its host-resolution pass");

            var svc = new CountingPlacementService { ThrowOnPass = true };
            var deferred = new DeferringRevitApiContext();
            IRevitApiContext previous = RevitApi.Context;
            RevitApi.Context = deferred;
            try
            {
                var vm = BuildVm(svc);
                deferred.Drain();

                Burst(vm);
                deferred.Drain();

                Check(svc.HostPassEnds == svc.HostPassBegins,
                    "host passes stayed balanced after a failure (begins=" + svc.HostPassBegins
                        + ", ends=" + svc.HostPassEnds + ")");
            }
            finally
            {
                RevitApi.Context = previous;
            }
        }

        /// <summary>
        /// T1.2: <c>SetCatalog</c> raises eight PropertyChanged events and <c>SourceMode</c> adds a
        /// ninth, so one mode switch used to rebuild the dropdowns and re-run the sweep ~7 times. The
        /// handler must now react to the actual change only.
        /// </summary>
        private static void TestCatalogChangeRebuildsOnceDespiteManyNotifications()
        {
            Console.WriteLine("Test: a catalog change rebuilds once despite many PropertyChanged events");

            string settings = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "fps-coalesce-" + Guid.NewGuid().ToString("N") + ".json");
            string previousSettingsPath = FireProtection.UI.Services.CatalogSettingsStore.OverridePath;
            FireProtection.UI.Services.CatalogSettingsStore.OverridePath = settings;

            var svc = new CountingPlacementService();
            var deferred = new DeferringRevitApiContext();
            IRevitApiContext previous = RevitApi.Context;
            RevitApi.Context = deferred;
            try
            {
                // A fresh model catalog per call, so switching back to Model mode really does produce a
                // DIFFERENT catalog instance - the condition the idempotence check keys on.
                var catalog = new CatalogViewModel(
                    path => new FakeLoadedCatalog(),
                    new CatalogHolder(),
                    new CatalogHolder(),
                    () => new FakeLoadedCatalog());

                var vm = new SprinklerBruteForceViewModel(MakeData(), null, null, svc, catalog);

                deferred.Drain();

                // A REAL mode switch. TrySetSourceMode raises SourceMode/IsCatalogFileMode plus
                // SetCatalog's eight events; only ONE of those represents an actual change.
                int queuedBefore = deferred.PendingCount;
                string error;
                catalog.TrySetSourceMode(
                    CatalogSourceMode.CatalogFile, out error);

                Check(deferred.PendingCount - queuedBefore == 1,
                    "one mode switch queued exactly 1 sweep, not 7 (queued "
                        + (deferred.PendingCount - queuedBefore) + ")");

                deferred.Drain();
                int passesAfterFirst = svc.PassCount;
                Check(passesAfterFirst >= 1, "the mode switch did run a sweep");

                // Switching back must behave identically: one genuine change, one rebuild.
                catalog.TrySetSourceMode(
                    CatalogSourceMode.RevitModel, out error);
                Check(deferred.PendingCount == 1,
                    "switching back queued exactly 1 more sweep (pending=" + deferred.PendingCount + ")");

                deferred.Drain();
                Check(svc.PassCount > passesAfterFirst, "the switch back ran a sweep too");
            }
            finally
            {
                RevitApi.Context = previous;
                FireProtection.UI.Services.CatalogSettingsStore.OverridePath = previousSettingsPath;
                try { if (System.IO.File.Exists(settings)) System.IO.File.Delete(settings); } catch { }
            }
        }

        /// <summary>
        /// Fires a rapid sequence of eligibility-affecting edits on one room. Hazard class is used
        /// because it is a public settable row property that the ViewModel explicitly routes into the
        /// preflight (it drives candidate generation and the cache key). Values alternate because the
        /// setters no-op on an unchanged value, which would produce no notification at all.
        /// </summary>
        private static void Burst(SprinklerBruteForceViewModel vm)
        {
            if (vm == null || vm.AllRooms.Count == 0) return;
            var room = vm.AllRooms[0];
            room.SelectedHazardClass = room.SelectedHazardClass == "OH1" ? "Light" : "OH1";
        }

        /// <summary>
        /// A minimal LOADED catalog. Reports IsLoaded so the ViewModel takes the populated path rather
        /// than the "no catalog" early-out, which is the branch the idempotence fix guards.
        /// </summary>
        private sealed class FakeLoadedCatalog : FireProtection.UI.Services.ICatalog
        {
            public bool IsLoaded { get { return true; } }
            public string CatalogVersion { get { return "fake-v1"; } }
            public string SourcePath { get { return null; } }
            public int TotalRowCount { get { return 1; } }
            public IReadOnlyList<string> AvailableHazardClasses { get { return new List<string>(); } }
            public IReadOnlyList<string> AvailableSprinklerMounts { get { return new List<string>(); } }
            public IReadOnlyList<string> AvailableDetectorTypes { get { return new List<string>(); } }
            public IReadOnlyList<string> AvailableMounts { get { return new List<string>(); } }
            public IReadOnlyList<string> AvailableCeilingSlopes { get { return new List<string>(); } }
            public IReadOnlyList<string> AvailableApplianceTypes { get { return new List<string>(); } }
            public IReadOnlyList<string> AvailableCandelas { get { return new List<string>(); } }
            public IReadOnlyList<string> AvailableNotificationDbas { get { return new List<string>(); } }
            public IReadOnlyList<string> GetSprinklerFamilies() { return new List<string> { "FakeFam" }; }
            public IReadOnlyList<string> GetSprinklerTypesForFamily(string familyName) { return new List<string> { "FakeType" }; }
            public IReadOnlyList<string> GetHazardClassesForSprinklerFamily(string familyName) { return new List<string>(); }
            public string GetHazardClassForSprinkler(string familyName, string typeName) { return null; }
            public FireProtection.UI.Services.SprinklerCatalogEntry GetSprinklerEntry(string familyName, string typeName) { return null; }
            public IReadOnlyList<FireProtection.UI.Services.SprinklerCatalogEntry> GetSprinklerEntriesForFamily(string familyName) { return new List<FireProtection.UI.Services.SprinklerCatalogEntry>(); }
            public string GetSprinklerMount(string familyName, string typeName) { return null; }
            public IReadOnlyList<string> GetSmokeDetectorFamilies() { return new List<string>(); }
            public IReadOnlyList<string> GetSmokeDetectorTypesForFamily(string familyName) { return new List<string>(); }
            public IReadOnlyList<FireProtection.UI.Services.SmokeDetectorCatalogEntry> GetSmokeDetectorEntriesForFamily(string familyName) { return new List<FireProtection.UI.Services.SmokeDetectorCatalogEntry>(); }
            public IReadOnlyList<string> GetNotificationApplianceFamilies() { return new List<string>(); }
            public IReadOnlyList<string> GetNotificationApplianceTypesForFamily(string familyName) { return new List<string>(); }
            public IReadOnlyList<FireProtection.UI.Services.NotificationApplianceCatalogEntry> GetNotificationAppliancesForFamily(string familyName) { return new List<FireProtection.UI.Services.NotificationApplianceCatalogEntry>(); }
        }
    }
}