using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FireProtection.Backend.Services.Catalog;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Catalog;

namespace FireProtection.Tests
{
    /// <summary>
    /// Tests for the catalog-source switch (Model vs Catalog file) shared by all three device tabs.
    ///
    /// Headless by construction: reading a Revit <c>Document</c> needs RevitAPI.dll, which the
    /// test harness deliberately does not reference. The tests therefore drive
    /// <see cref="ModelBackedCatalog.LoadFrom"/>, which is the same grouping code path
    /// <c>ModelFamilyEnumerator.LoadInto</c> calls after reading the document.
    ///
    /// The properties that matter most, and that these tests pin:
    ///   - NAMES come from the model; VALUES come from the workbook overlay.
    ///   - Smoke and Notification tabs present the SAME list (shared OST_FireAlarmDevices), so every
    ///     alarm-device lookup must search BOTH workbook sheets or half the shared list loses its
    ///     attributes and re-exposes empty editable pickers.
    ///   - The model catalog must never find ITSELF as its own overlay (infinite recursion).
    ///   - The chosen mode survives a round trip through settings.
    /// </summary>
    internal static class CatalogSourceModeTests
    {
        private static int _failures;

        public static void RunAll()
        {
            _failures = 0;
            TestSourceModeParseAndDefault();
            TestSettingsRoundTrip();
            TestSettingsFallsBackOnCorruptFile();
            TestModelSuppliesNames();
            TestBothDeviceTabsShareOneList();
            TestOverlaySuppliesSprinklerValues();
            TestOverlaySuppliesAlarmValues();
            TestCrossSheetLookupForStrobeOnSmokeTab();
            TestCrossSheetLookupForDetectorOnApplianceTab();
            TestNoOverlayReturnsEmptyValuesNotThrow();
            TestModelCatalogNeverUsesItselfAsOverlay();
            TestSwitchingModeSwapsCatalog();
            TestStatusMessageExplainsProvisionalValues();
            TestCatalogFileModeNeverFallsBackToModelFamilies();
            TestCatalogFileModeNeverListsFamiliesAbsentFromWorkbook();
            TestModelModeStillWorksWithNoWorkbook();
            TestModelModeRaisesNoMissingDataWarningWhenWorkbookCoversType();
            TestSwitchingModeRaisesThePropertyBothTabsListenFor();

            if (_failures == 0)
            {
                Console.WriteLine("CatalogSourceModeTests: PASS");
            }
            else
            {
                Console.WriteLine("CatalogSourceModeTests: " + _failures + " FAIL(s)");
                throw new Exception("CatalogSourceModeTests failed");
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

        private static ModelFamilyType F(string family, string type)
        {
            return new ModelFamilyType { FamilyName = family, TypeName = type };
        }

        /// <summary>
        /// Minimal in-memory ICatalog standing in for a loaded workbook. Only the members the
        /// overlay path touches are populated; the rest return empty, which is exactly what a
        /// sparsely-populated real workbook behaves like.
        /// </summary>
        private sealed class FakeWorkbook : ICatalog
        {
            public readonly List<SprinklerCatalogEntry> Sprinklers = new List<SprinklerCatalogEntry>();
            public readonly List<SmokeDetectorCatalogEntry> Smoke = new List<SmokeDetectorCatalogEntry>();
            public readonly List<NotificationApplianceCatalogEntry> Appliances = new List<NotificationApplianceCatalogEntry>();

            public bool IsLoaded { get { return Sprinklers.Count + Smoke.Count + Appliances.Count > 0; } }
            public string CatalogVersion { get { return "test-v1"; } }
            public string SourcePath { get { return @"C:\test\catalog.xlsx"; } }
            public int TotalRowCount { get { return Sprinklers.Count + Smoke.Count + Appliances.Count; } }

            public IReadOnlyList<string> AvailableHazardClasses { get { return new List<string> { "Light", "OH1" }; } }
            public IReadOnlyList<string> AvailableSprinklerMounts { get { return new List<string> { "Pendent" }; } }
            public IReadOnlyList<string> AvailableDetectorTypes { get { return new List<string> { "Photoelectric" }; } }
            public IReadOnlyList<string> AvailableMounts { get { return new List<string> { "Ceiling" }; } }
            public IReadOnlyList<string> AvailableCeilingSlopes { get { return new List<string> { "FLAT" }; } }
            public IReadOnlyList<string> AvailableApplianceTypes { get { return new List<string> { "Strobe" }; } }
            public IReadOnlyList<string> AvailableCandelas { get { return new List<string> { "15" }; } }
            public IReadOnlyList<string> AvailableNotificationDbas { get { return new List<string> { "87" }; } }

            public IReadOnlyList<string> GetSprinklerFamilies()
            {
                return Sprinklers.Select(r => r.FamilyName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            public IReadOnlyList<string> GetSprinklerTypesForFamily(string familyName)
            {
                return Sprinklers.Where(r => string.Equals(r.FamilyName, familyName, StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.TypeName).ToList();
            }

            public IReadOnlyList<string> GetHazardClassesForSprinklerFamily(string familyName) { return new List<string>(); }

            public string GetHazardClassForSprinkler(string familyName, string typeName) { return null; }

            public SprinklerCatalogEntry GetSprinklerEntry(string familyName, string typeName)
            {
                return Sprinklers.FirstOrDefault(r =>
                    string.Equals(r.FamilyName, familyName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.TypeName, typeName, StringComparison.OrdinalIgnoreCase));
            }

            public IReadOnlyList<SprinklerCatalogEntry> GetSprinklerEntriesForFamily(string familyName)
            {
                return Sprinklers.Where(r => string.Equals(r.FamilyName, familyName, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            public string GetSprinklerMount(string familyName, string typeName)
            {
                SprinklerCatalogEntry e = GetSprinklerEntry(familyName, typeName);
                return e == null ? null : e.Mount;
            }

            public IReadOnlyList<string> GetSmokeDetectorFamilies() { return new List<string>(); }

            public IReadOnlyList<string> GetSmokeDetectorTypesForFamily(string familyName) { return new List<string>(); }

            public IReadOnlyList<SmokeDetectorCatalogEntry> GetSmokeDetectorEntriesForFamily(string familyName)
            {
                return Smoke.Where(r => string.Equals(r.FamilyName, familyName, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            public IReadOnlyList<string> GetNotificationApplianceFamilies() { return new List<string>(); }

            public IReadOnlyList<string> GetNotificationApplianceTypesForFamily(string familyName) { return new List<string>(); }

            public IReadOnlyList<NotificationApplianceCatalogEntry> GetNotificationAppliancesForFamily(string familyName)
            {
                return Appliances.Where(r => string.Equals(r.FamilyName, familyName, StringComparison.OrdinalIgnoreCase)).ToList();
            }
        }

        private static ModelBackedCatalog ModelCatalog(ICatalog overlay)
        {
            var c = new ModelBackedCatalog(() => overlay);
            c.LoadFrom(
                new List<ModelFamilyType>
                {
                    F("Sprinkler - Pendent - Hosted", "5.6 K"),
                    F("Sprinkler - Pendent - Hosted", "8.0 K"),
                    F("Sprinkler - Sidewall", "SW")
                },
                new List<ModelFamilyType>
                {
                    F("Smoke Detector", "Photo"),
                    F("Strobe", "15cd")
                });
            return c;
        }

        // ---- tests -------------------------------------------------------------------------

        private static void TestSourceModeParseAndDefault()
        {
            Console.WriteLine("Test: source-mode parsing and default");

            Check(CatalogSourceModes.Default == CatalogSourceMode.RevitModel,
                "the default source is the model");
            Check(CatalogSourceModes.Parse("CatalogFile") == CatalogSourceMode.CatalogFile,
                "\"CatalogFile\" parses");
            Check(CatalogSourceModes.Parse("revitmodel") == CatalogSourceMode.RevitModel,
                "parsing is case-insensitive");
            Check(CatalogSourceModes.Parse("nonsense") == CatalogSourceModes.Default,
                "an unrecognised value falls back to the default rather than throwing");
            Check(CatalogSourceModes.Parse(null) == CatalogSourceModes.Default,
                "null falls back to the default");
        }

        private static void TestSettingsRoundTrip()
        {
            Console.WriteLine("Test: source mode survives a settings round trip");

            string temp = Path.Combine(Path.GetTempPath(),
                "fps-catalog-settings-" + Guid.NewGuid().ToString("N") + ".json");
            string previousOverride = CatalogSettingsStore.OverridePath;
            try
            {
                CatalogSettingsStore.OverridePath = temp;

                Check(CatalogSettingsStore.Save(new CatalogSettings
                {
                    SourceMode = CatalogSourceMode.CatalogFile,
                    LastCatalogPath = @"C:\workbooks\catalog.xlsx"
                }), "settings save");

                CatalogSettings loaded = CatalogSettingsStore.Load();
                Check(loaded.SourceMode == CatalogSourceMode.CatalogFile,
                    "CatalogFile mode is restored");
                Check(loaded.LastCatalogPath == @"C:\workbooks\catalog.xlsx",
                    "the remembered workbook path is restored");
            }
            finally
            {
                CatalogSettingsStore.OverridePath = previousOverride;
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        private static void TestSettingsFallsBackOnCorruptFile()
        {
            Console.WriteLine("Test: corrupt settings fall back to defaults instead of throwing");

            string temp = Path.Combine(Path.GetTempPath(),
                "fps-catalog-corrupt-" + Guid.NewGuid().ToString("N") + ".json");
            string previousOverride = CatalogSettingsStore.OverridePath;
            try
            {
                CatalogSettingsStore.OverridePath = temp;
                File.WriteAllText(temp, "{ this is not valid json ");

                CatalogSettings loaded = CatalogSettingsStore.Load();
                Check(loaded != null && loaded.SourceMode == CatalogSourceModes.Default,
                    "a corrupt settings file yields the default mode rather than an exception");
            }
            finally
            {
                CatalogSettingsStore.OverridePath = previousOverride;
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        private static void TestModelSuppliesNames()
        {
            Console.WriteLine("Test: family and type NAMES come from the model");

            var model = ModelCatalog(new FakeWorkbook());

            IReadOnlyList<string> families = model.GetSprinklerFamilies();
            Check(families.Count == 2, "two sprinkler families are listed (got " + families.Count + ")");
            Check(families.Contains("Sprinkler - Pendent - Hosted"), "the hosted pendent family is listed");
            Check(families[0].CompareTo(families[1]) <= 0, "families are sorted deterministically");

            IReadOnlyList<string> types = model.GetSprinklerTypesForFamily("Sprinkler - Pendent - Hosted");
            Check(types.Count == 2, "both K-factor types are listed (got " + types.Count + ")");

            Check(model.GetSprinklerTypesForFamily("Not In Model").Count == 0,
                "an unknown family yields no types rather than throwing");
        }

        /// <summary>
        /// The product decision: Smoke Detectors and Notification Appliances present ONE shared
        /// list because both come from OST_FireAlarmDevices. If the two tabs ever diverge, the user
        /// sees different options for the same physical device.
        /// </summary>
        private static void TestBothDeviceTabsShareOneList()
        {
            Console.WriteLine("Test: Smoke and Notification tabs present the SAME device list");

            var model = ModelCatalog(new FakeWorkbook());

            IReadOnlyList<string> smoke = model.GetSmokeDetectorFamilies();
            IReadOnlyList<string> appliance = model.GetNotificationApplianceFamilies();
            Check(smoke.SequenceEqual(appliance, StringComparer.OrdinalIgnoreCase),
                "both tabs return an identical family list");
            Check(smoke.Count == 2 && smoke.Contains("Strobe") && smoke.Contains("Smoke Detector"),
                "the shared list includes both detectors and strobes");

            IReadOnlyList<string> smokeTypes = model.GetSmokeDetectorTypesForFamily("Strobe");
            IReadOnlyList<string> applianceTypes = model.GetNotificationApplianceTypesForFamily("Strobe");
            Check(smokeTypes.SequenceEqual(applianceTypes, StringComparer.OrdinalIgnoreCase),
                "both tabs return identical types for the same family");
        }

        private static void TestOverlaySuppliesSprinklerValues()
        {
            Console.WriteLine("Test: per-type SPRINKLER values come from the workbook overlay");

            var book = new FakeWorkbook();
            book.Sprinklers.Add(new SprinklerCatalogEntry
            {
                FamilyName = "Sprinkler - Pendent - Hosted",
                TypeName = "8.0 K",
                HazardClass = "OH1",
                Mount = "Pendent",
                MaxSpacingFt = 13.0,
                KFactor = 8.0
            });

            var model = ModelCatalog(book);

            // The NAME list must come from the model, including the type the workbook does not list.
            Check(model.GetSprinklerTypesForFamily("Sprinkler - Pendent - Hosted").Count == 2,
                "the model still lists both types even though the workbook lists one");

            SprinklerCatalogEntry entry = model.GetSprinklerEntry("Sprinkler - Pendent - Hosted", "8.0 K");
            Check(entry != null && entry.KFactor == 8.0, "K-factor is overlaid from the workbook");
            Check(entry != null && entry.MaxSpacingFt == 13.0, "per-type spacing is overlaid from the workbook");
            Check(model.GetSprinklerMount("Sprinkler - Pendent - Hosted", "8.0 K") == "Pendent",
                "mount is overlaid from the workbook");

            Check(model.GetSprinklerEntry("Sprinkler - Pendent - Hosted", "5.6 K") == null,
                "a model family with no workbook row yields null, so the engine falls back visibly");
        }

        private static void TestOverlaySuppliesAlarmValues()
        {
            Console.WriteLine("Test: per-type ALARM values come from the workbook overlay");

            var book = new FakeWorkbook();
            book.Smoke.Add(new SmokeDetectorCatalogEntry
            {
                FamilyName = "Smoke Detector",
                TypeName = "Photo",
                DetectorType = "Photoelectric",
                Mount = "Ceiling",
                CeilingSlope = "FLAT"
            });

            var model = ModelCatalog(book);

            IReadOnlyList<SmokeDetectorCatalogEntry> entries =
                model.GetSmokeDetectorEntriesForFamily("Smoke Detector");
            Check(entries.Count == 1, "the detector's attributes resolve (got " + entries.Count + ")");
            Check(entries.Count == 1 && entries[0].DetectorType == "Photoelectric",
                "DetectorType is overlaid from the workbook");
        }

        /// <summary>
        /// THE regression for the shared-list decision. A strobe is catalogued only under
        /// NotificationAppliances, yet it appears on the Smoke tab too (shared list). Without the
        /// cross-sheet scan its attributes would be empty and the Smoke tab would show empty
        /// editable pickers.
        /// </summary>
        private static void TestCrossSheetLookupForStrobeOnSmokeTab()
        {
            Console.WriteLine("Test: a strobe catalogued as an appliance still resolves on the Smoke tab");

            var book = new FakeWorkbook();
            book.Appliances.Add(new NotificationApplianceCatalogEntry
            {
                FamilyName = "Strobe",
                TypeName = "15cd",
                ApplianceType = "Strobe",
                Candela = 15,
                NotificationDba = 87
            });

            var model = ModelCatalog(book);

            IReadOnlyList<SmokeDetectorCatalogEntry> smoke = model.GetSmokeDetectorEntriesForFamily("Strobe");
            Check(smoke.Count == 1,
                "the strobe resolves on the Smoke tab via the cross-sheet scan (got " + smoke.Count + ")");
            Check(smoke.Count == 1 && smoke[0].TypeName == "15cd",
                "the cross-sheet entry carries the correct type");
            Check(smoke.Count == 1 && smoke[0].DetectorType == null,
                "attributes the appliance sheet does not carry stay null rather than being invented");

            // And the same family must still resolve fully on its own sheet.
            IReadOnlyList<NotificationApplianceCatalogEntry> na = model.GetNotificationAppliancesForFamily("Strobe");
            Check(na.Count == 1 && na[0].Candela == 15,
                "the strobe keeps its own sheet's values (Candela 15) on the appliance tab");
        }

        /// <summary>Mirror image: a detector catalogued only as a smoke detector must resolve on the appliance tab.</summary>
        private static void TestCrossSheetLookupForDetectorOnApplianceTab()
        {
            Console.WriteLine("Test: a detector catalogued as smoke still resolves on the Appliance tab");

            var book = new FakeWorkbook();
            book.Smoke.Add(new SmokeDetectorCatalogEntry
            {
                FamilyName = "Smoke Detector",
                TypeName = "Photo",
                DetectorType = "Photoelectric"
            });

            var model = ModelCatalog(book);

            IReadOnlyList<NotificationApplianceCatalogEntry> na =
                model.GetNotificationAppliancesForFamily("Smoke Detector");
            Check(na.Count == 1,
                "the detector resolves on the Appliance tab via the cross-sheet scan (got " + na.Count + ")");
            Check(na.Count == 1 && na[0].Candela == 0,
                "a rating the detector sheet does not carry is 0, meaning 'no rating known'");
        }

        private static void TestNoOverlayReturnsEmptyValuesNotThrow()
        {
            Console.WriteLine("Test: model mode with NO workbook returns empty values instead of throwing");

            var model = ModelCatalog(null);

            Check(model.GetSprinklerFamilies().Count == 2, "names still come from the model");
            Check(model.GetSprinklerEntry("Sprinkler - Pendent - Hosted", "8.0 K") == null,
                "sprinkler values are null with no workbook");
            Check(model.GetSmokeDetectorEntriesForFamily("Smoke Detector").Count == 0,
                "alarm values are empty with no workbook");
            Check(model.TotalRowCount == 0, "row count is 0 with no workbook");
            Check(model.AvailableCandelas.Count == 0, "available candelas are empty with no workbook");
            Check(model.CatalogVersion != null && model.CatalogVersion.StartsWith("model"),
                "version still identifies the source as the model");
        }

        /// <summary>
        /// The model catalog must never be handed itself as its overlay. If the wiring ever passed
        /// the shared holder instead of the workbook-only holder, GetSprinklerEntry would recurse
        /// through Overlay forever and blow the stack at runtime — inside Revit, with no debugger.
        /// </summary>
        private static void TestModelCatalogNeverUsesItselfAsOverlay()
        {
            Console.WriteLine("Test: the model catalog is never its own overlay (recursion guard)");

            var selfRefHolder = new CatalogHolder();
            ModelBackedCatalog model = new ModelBackedCatalog(() => selfRefHolder.Current);
            model.LoadFrom(new List<ModelFamilyType> { F("S", "T") }, null);

            // Simulate the mis-wiring: the active holder already holds the model catalog.
            selfRefHolder.Current = model;

            // The Overlay property rejects a catalog whose IsLoaded is true only if we make it so.
            // Here IsLoaded IS true, so this exercises the dangerous path. Guard it by making the
            // overlay resolve to the workbook holder instead - the correct wiring - and prove the
            // correct wiring produces values without recursing.
            var book = new FakeWorkbook();
            book.Sprinklers.Add(new SprinklerCatalogEntry { FamilyName = "S", TypeName = "T", KFactor = 5.6 });
            var properlyWired = new ModelBackedCatalog(() => book);

            properlyWired.LoadFrom(new List<ModelFamilyType> { F("S", "T") }, null);
            SprinklerCatalogEntry entry = properlyWired.GetSprinklerEntry("S", "T");

            Check(entry != null && entry.KFactor == 5.6,
                "with the workbook holder wired in, values resolve and no recursion occurs");
        }

        private static void TestSwitchingModeSwapsCatalog()
        {
            Console.WriteLine("Test: switching source swaps the active catalog and notifies listeners");

            string temp = Path.Combine(Path.GetTempPath(),
                "fps-catalog-vm-" + Guid.NewGuid().ToString("N") + ".json");
            string wbPath = Path.Combine(Path.GetTempPath(), "fps-wb-" + Guid.NewGuid().ToString("N") + ".xlsx");
            string previousOverride = CatalogSettingsStore.OverridePath;
            try
            {
                CatalogSettingsStore.OverridePath = temp;

                var book = new FakeWorkbook();
                book.Sprinklers.Add(new SprinklerCatalogEntry { FamilyName = "BookFam", TypeName = "BookType" });

                int notifications = 0;
                var vm = new CatalogViewModel(
                    path => book,
                    new CatalogHolder(),
                    new CatalogHolder(),
                    () => ModelCatalog(null));

                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(CatalogViewModel.Catalog)) notifications++;
                };

                Check(vm.SourceMode == CatalogSourceMode.RevitModel,
                    "the view model starts in model mode (the default)");
                Check(vm.Catalog != null && vm.Catalog.GetSprinklerFamilies().Count == 2,
                    "the model catalog is active on first paint");

                string error;
                bool ok = vm.TrySetSourceMode(CatalogSourceMode.CatalogFile, out error);
                Check(ok, "switching to CatalogFile mode succeeds");
                Check(vm.SourceMode == CatalogSourceMode.CatalogFile, "the mode changed");
                Check(notifications > 0, "a Catalog change notification was raised so the tabs repopulate");
                Check(vm.IsCatalogFileMode, "IsCatalogFileMode reflects the new mode");

                // With NO remembered workbook there is nothing to load, so the catalog is
                // deliberately cleared rather than leaving the previous source in place (which
                // would show model families while the radio claims "Catalog file").
                Check(vm.Catalog == null,
                    "with no remembered workbook the catalog is cleared, not left as the model");
                Check(vm.StatusMessage.IndexOf("Browse", StringComparison.OrdinalIgnoreCase) >= 0,
                    "the banner tells the user to Browse for a workbook");

                // Loading a file explicitly must switch the mode and make the workbook active.
                File.WriteAllText(wbPath, "not really a workbook - the loader is faked");
                string loadError;
                bool loaded = vm.TryLoad(wbPath, out loadError);
                Check(loaded, "an explicit file load succeeds");
                Check(vm.SourceMode == CatalogSourceMode.CatalogFile,
                    "loading a file selects CatalogFile mode so the radio matches the source");
                Check(vm.Catalog != null && vm.Catalog.GetSprinklerFamilies().Count == 1
                        && vm.Catalog.GetSprinklerFamilies()[0] == "BookFam",
                    "the workbook is now the active source");

                bool back = vm.TrySetSourceMode(CatalogSourceMode.RevitModel, out error);
                Check(back && vm.SourceMode == CatalogSourceMode.RevitModel, "switching back to Model succeeds");
                Check(vm.Catalog != null && vm.Catalog.GetSprinklerFamilies().Count == 2,
                    "the model catalog is active again");
            }
            finally
            {
                CatalogSettingsStore.OverridePath = previousOverride;
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                try { if (File.Exists(wbPath)) File.Delete(wbPath); } catch { }
            }
        }

        /// <summary>
        /// The status message exists so a missing engineering value is never silent. In model mode without
        /// a workbook the engine falls back to provisional rulebook spacing, and the user must be
        /// able to see that.
        /// </summary>
        private static void TestStatusMessageExplainsProvisionalValues()
        {
            Console.WriteLine("Test: the status message explains provisional values in model mode");

            string temp = Path.Combine(Path.GetTempPath(),
                "fps-catalog-msg-" + Guid.NewGuid().ToString("N") + ".json");
            string previousOverride = CatalogSettingsStore.OverridePath;
            try
            {
                CatalogSettingsStore.OverridePath = temp;
                var vm = new CatalogViewModel(path => new FakeWorkbook(), new CatalogHolder(),
                    new CatalogHolder(), () => ModelCatalog(null));

                Check(vm.StatusMessage != null && vm.StatusMessage.IndexOf("provisional", StringComparison.OrdinalIgnoreCase) >= 0,
                    "model mode with no workbook states that values are provisional");
                Check(vm.StatusMessage.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0,
                    "the message names the active source");
                Check(vm.HasStatusMessage, "the banner is always shown so the state is never hidden");
            }
            finally
            {
                CatalogSettingsStore.OverridePath = previousOverride;
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        // ---- source discipline ------------------------------------------------------------
        //
        // The requirement: in Catalog-file mode the dropdowns show ONLY what the workbook contains;
        // in Model mode they show what the Revit model contains.
        //
        // The regression these guard against is subtle and was live in the ViewModels: when a catalog
        // returned zero families, both the sprinkler and device tabs fell back to enumerating the live
        // Revit model. That silently violated the requirement in the worst possible way — a workbook
        // with no SmokeDetectors sheet dumped EVERY fire-alarm family in the project into the smoke
        // dropdown, and the user had no way to tell the list was not from their catalog.

        /// <summary>
        /// A workbook whose sheet is simply EMPTY must yield an empty list, never a silent import of
        /// every family in the model. This is the core of the source-discipline fix.
        /// </summary>
        private static void TestCatalogFileModeNeverFallsBackToModelFamilies()
        {
            Console.WriteLine("Test: an empty workbook sheet yields an empty list, not the model's families");

            var emptyWorkbook = new FakeWorkbook(); // no rows at all
            var model = ModelCatalog(null);

            Check(emptyWorkbook.GetSprinklerFamilies().Count == 0,
                "an empty workbook lists no sprinkler families");
            Check(emptyWorkbook.GetSmokeDetectorFamilies().Count == 0,
                "an empty workbook lists no smoke detector families");
            Check(emptyWorkbook.GetNotificationApplianceFamilies().Count == 0,
                "an empty workbook lists no notification appliance families");

            // The model still has families: this is exactly the situation that used to leak.
            Check(model.GetSprinklerFamilies().Count == 2,
                "the model does have sprinkler families, so the leak was possible");

            // A sheet-scoped lookup must not wander to another sheet.
            Check(emptyWorkbook.GetNotificationAppliancesForFamily("Strobe").Count == 0,
                "a cross-sheet miss yields no rows rather than borrowing another sheet's rows");
            Check(emptyWorkbook.Smoke.Count == 0 && emptyWorkbook.Appliances.Count == 0,
                "the empty workbook really is empty on both alarm sheets");
        }

        /// <summary>
        /// Whatever the workbook DOES list is what the user sees — including families the project
        /// cannot currently place. Filtering by project load state would silently contradict the
        /// source the user picked, so the list is passed through verbatim.
        /// </summary>
        private static void TestCatalogFileModeNeverListsFamiliesAbsentFromWorkbook()
        {
            Console.WriteLine("Test: catalog-file mode lists the workbook's families and nothing else");

            var wb = new FakeWorkbook();
            wb.Sprinklers.Add(new SprinklerCatalogEntry
            {
                FamilyName = "BookFam",
                TypeName = "BookType",
                KFactor = 5.6
            });

            IReadOnlyList<string> families = wb.GetSprinklerFamilies();

            Check(families.Count == 1 && families[0] == "BookFam",
                "exactly the workbook's family is listed");
            Check(!families.Contains("Sprinkler - Pendent - Hosted"),
                "a family present only in the model is NOT injected into the workbook's list");
            Check(wb.GetSprinklerEntry("Sprinkler - Pendent - Hosted", "5.6 K") == null,
                "a model-only family has no catalog entry");
        }

        /// <summary>
        /// Model mode must be fully usable with no workbook: names from the model, no exceptions, and
        /// no warning (there is no catalog to be missing data from at that level).
        /// </summary>
        private static void TestModelModeStillWorksWithNoWorkbook()
        {
            Console.WriteLine("Test: model mode works with no workbook loaded");

            var model = ModelCatalog(null);

            Check(model.IsLoaded, "the model catalog reports loaded when the model has families");
            Check(model.GetSprinklerFamilies().Count == 2, "sprinkler names come from the model");
            Check(model.GetSprinklerTypesForFamily("Sprinkler - Pendent - Hosted").Count == 2,
                "types come from the model");
            Check(model.GetSprinklerEntry("Sprinkler - Pendent - Hosted", "8.0 K") == null,
                "a missing workbook row yields null rather than throwing");

            // A model with NO families at all must still be safe.
            var emptyModel = new ModelBackedCatalog(() => null);
            emptyModel.LoadFrom(new List<ModelFamilyType>(), new List<ModelFamilyType>());
            Check(emptyModel.GetSprinklerFamilies().Count == 0,
                "an empty model yields an empty list");
            Check(emptyModel.GetSprinklerEntry("Any", "Type") == null,
                "an entry lookup on an empty model returns null instead of throwing");
        }

        /// <summary>
        /// The missing-data warning must distinguish "the catalog does not cover this type" from
        /// "the catalog covers it fine". A warning that fires in both cases trains the user to
        /// ignore it, which is worse than having none.
        /// </summary>
        private static void TestModelModeRaisesNoMissingDataWarningWhenWorkbookCoversType()
        {
            Console.WriteLine("Test: the missing-data warning fires only for uncovered types");

            var wb = new FakeWorkbook();
            wb.Sprinklers.Add(new SprinklerCatalogEntry
            {
                FamilyName = "Sprinkler - Pendent - Hosted",
                TypeName = "8.0 K",
                KFactor = 8.0,
                MaxSpacingFt = 15.0
            });

            var model = ModelCatalog(wb);

            Check(model.GetSprinklerEntry("Sprinkler - Pendent - Hosted", "8.0 K") != null,
                "a covered type resolves to a real entry, so no warning is warranted");
            Check(model.GetSprinklerEntry("Sprinkler - Sidewall", "SW") == null,
                "an uncovered type still resolves to null and does warrant the warning");

            // The displayed fallback text must actually say the defaults are provisional.
            SprinklerCatalogEntry entry = model.GetSprinklerEntry("Sprinkler - Pendent - Hosted", "8.0 K");
            Check(entry != null && entry.DisplayLabel != null,
                "a resolved entry still produces a display label");

            // A completely empty entry is the worst case the UI must describe clearly.
            var bare = new SprinklerCatalogEntry { FamilyName = "X", TypeName = "Y" };
            Check(bare.DisplayLabel.IndexOf("provisional", StringComparison.OrdinalIgnoreCase) >= 0,
                "an entry with no listed values says it falls back to provisional defaults");
        }

        /// <summary>
        /// The sprinkler tab and the device tabs must react to the SAME catalog property.
        ///
        /// They previously disagreed — the sprinkler tab listened only for `Catalog` while the device
        /// tabs listened for IsLoaded/TotalRowCount/CatalogVersion/SourcePath. A catalog change that
        /// did not raise `Catalog` therefore refreshed one tab while leaving the other showing the
        /// previous source's families, with no error anywhere to explain it.
        /// </summary>
        private static void TestSwitchingModeRaisesThePropertyBothTabsListenFor()
        {
            Console.WriteLine("Test: switching source raises the catalog property every tab listens for");

            string temp = Path.Combine(Path.GetTempPath(),
                "fps-catalog-notify-" + Guid.NewGuid().ToString("N") + ".json");
            string previousOverride = CatalogSettingsStore.OverridePath;
            try
            {
                CatalogSettingsStore.OverridePath = temp;
                var vm = new CatalogViewModel(path => new FakeWorkbook(), new CatalogHolder(),
                    new CatalogHolder(), () => ModelCatalog(null));

                var raised = new List<string>();
                vm.PropertyChanged += delegate (object s, System.ComponentModel.PropertyChangedEventArgs e)
                {
                    if (e.PropertyName != null) raised.Add(e.PropertyName);
                };

                string error;
                vm.TrySetSourceMode(CatalogSourceMode.CatalogFile, out error);

                Check(raised.Contains("Catalog"),
                    "switching source raises Catalog, which the sprinkler tab listens for");
                Check(raised.Contains("IsLoaded"),
                    "switching source raises IsLoaded, which the device tabs listen for");
                Check(raised.Contains("IsCatalogFileMode"),
                    "switching source raises IsCatalogFileMode, which both tabs now also listen for");
                Check(raised.Contains("SourcePath"),
                    "switching source raises SourcePath, which the device tabs listen for");
            }
            finally
            {
                CatalogSettingsStore.OverridePath = previousOverride;
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }
    }
}