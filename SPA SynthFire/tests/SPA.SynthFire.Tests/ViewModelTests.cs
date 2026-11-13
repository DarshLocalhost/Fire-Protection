using System.Collections.Generic;
using System.Linq;
using SPA.SynthFire.Core.Catalog;
using SPA.SynthFire.Core.Devices;
using SPA.SynthFire.Core.Standards;
using SPA.SynthFire.UI;
using SPA.SynthFire.UI.ViewModels;
using SPA.SynthFire.UI.Views;
using Xunit;

namespace SPA.SynthFire.Tests;

public class ViewModelTests
{
    private sealed class FakeDetectorRunner : IDetectorRunner
    {
        public string? FamilyId;
        public DetectorKind Kind;
        public bool ClearPrevious;
        public bool PreviewOnly;
        public DeviceMount Mount;
        public int Calls;
        public DetectorKind PlanKind;
        public DeviceMount PlanMount;
        public string? PlanFamilyId;
        public int PlanCalls;

        public void RequestPlace(string familySymbolId, DetectorKind kind, bool clearPrevious, bool previewOnly,
            DeviceMount mount = DeviceMount.Ceiling)
        {
            FamilyId = familySymbolId; Kind = kind; ClearPrevious = clearPrevious; PreviewOnly = previewOnly; Mount = mount; Calls++;
        }

        public void RequestPlan(DetectorKind kind, DeviceMount mount = DeviceMount.Ceiling, string? familySymbolId = null)
        { PlanKind = kind; PlanMount = mount; PlanFamilyId = familySymbolId; PlanCalls++; }
    }

    private sealed class FakePlacementRunner : IPlacementRunner
    {
        public PlacementOptions? Options;
        public int Calls;
        public PlacementOptions? PlanOptions;
        public int PlanCalls;
        public void RequestPlace(PlacementOptions options) { Options = options; Calls++; }
        public void RequestPlan(PlacementOptions options) { PlanOptions = options; PlanCalls++; }
    }

    private static readonly FamilyTypeOption Fam1 = new("id-1", "Pendent 155");
    private static readonly FamilyTypeOption Fam2 = new("id-2", "Upright 200");

    // ---- DetectorDesignViewModel ----

    [Fact]
    public void Detector_Ctor_PreselectsFirstFamilyAndSmokeAndClearsByDefault()
    {
        var vm = new DetectorDesignViewModel(new[] { Fam1, Fam2 });
        Assert.Equal(Fam1, vm.SelectedFamily);
        Assert.Equal(DetectorKind.Smoke, vm.SelectedKind);
        Assert.True(vm.ClearPrevious);
    }

    [Fact]
    public void Detector_Place_WithoutRunner_SetsNotConnected()
    {
        var vm = new DetectorDesignViewModel(new[] { Fam1 });
        vm.PlaceCommand.Execute(null);
        Assert.Equal("Not connected to Revit.", vm.Status);
    }

    [Fact]
    public void Detector_Place_WithoutFamily_AsksForSelection()
    {
        var vm = new DetectorDesignViewModel(System.Array.Empty<FamilyTypeOption>()) { Runner = new FakeDetectorRunner() };
        vm.PlaceCommand.Execute(null);
        Assert.Equal("Select a detector family/type.", vm.Status);
    }

    [Fact]
    public void Detector_Place_ForwardsSelectionToRunner()
    {
        var runner = new FakeDetectorRunner();
        var vm = new DetectorDesignViewModel(new[] { Fam1, Fam2 })
        {
            Runner = runner,
            SelectedFamily = Fam2,
            SelectedKind = DetectorKind.Heat,
            ClearPrevious = false
        };

        vm.PlaceCommand.Execute(null);

        Assert.Equal(1, runner.Calls);
        Assert.Equal("id-2", runner.FamilyId);
        Assert.Equal(DetectorKind.Heat, runner.Kind);
        Assert.False(runner.ClearPrevious);
        Assert.Equal("Placing detectors…", vm.Status);
    }

    [Fact]
    public void Detector_ViewPlan_RequestsPlanForSelectedKind()
    {
        var runner = new FakeDetectorRunner();
        var vm = new DetectorDesignViewModel(new[] { Fam1 }) { Runner = runner, SelectedKind = DetectorKind.Heat };

        vm.ViewPlanCommand.Execute(null);

        Assert.Equal(1, runner.PlanCalls);
        Assert.Equal(0, runner.Calls);
        Assert.Equal(DetectorKind.Heat, runner.PlanKind);
    }

    [Fact]
    public void Detector_ViewPlan_WithoutRunner_SetsNotConnected()
    {
        var vm = new DetectorDesignViewModel(new[] { Fam1 });
        vm.ViewPlanCommand.Execute(null);
        Assert.Equal("Not connected to Revit.", vm.Status);
    }

    // ---- FireAlarmFamilyClassifier + detector family warnings ----

    [Theory]
    [InlineData("Smoke Detector : Standard", true)]
    [InlineData("Heat Detector : 135F", true)]
    [InlineData("Horn Strobe - Ceiling Mounted : Red", true)] // explicit ceiling phrase wins
    [InlineData("Fire Alarm Horn : Wall", false)]
    [InlineData("Strobe : 75cd", false)]
    [InlineData("Speaker Strobe : White", false)]
    public void Classifier_ClassifiesByLabel(string label, bool isDetector) =>
        Assert.Equal(isDetector, FireAlarmFamilyClassifier.IsDetector(label));

    [Fact]
    public void Detector_Ctor_PrefersDetectorFamilyOverLeadingAppliance()
    {
        var horn = new FamilyTypeOption("h", "Fire Alarm Horn : Wall");
        var smoke = new FamilyTypeOption("s", "Smoke Detector : Standard");
        var vm = new DetectorDesignViewModel(new[] { horn, smoke });
        Assert.Equal(smoke, vm.SelectedFamily);
    }

    [Fact]
    public void Detector_SelectingApplianceRaisesWarning_ThenClearsOnDetector()
    {
        var horn = new FamilyTypeOption("h", "Fire Alarm Horn : Wall");
        var smoke = new FamilyTypeOption("s", "Smoke Detector : Standard");
        var vm = new DetectorDesignViewModel(new[] { smoke, horn });

        Assert.Equal("Ready.", vm.Status);
        vm.SelectedFamily = horn;
        Assert.Contains("notification appliance", vm.Status);
        vm.SelectedFamily = smoke;
        Assert.Equal("Ready.", vm.Status);
    }

    // ---- SprinklerDesignViewModel ----

    private static SprinklerDesignViewModel MakeSprinklerVm(
        IReadOnlyList<FamilyTypeOption> families, IReadOnlyDictionary<string, string>? map = null) =>
        new(StandardRegistry.CreateDefault(), families, StandardIds.Nfpa13, map);

    [Fact]
    public void Sprinkler_Ctor_SelectsDefaultStandardAndPopulatesHazards()
    {
        var vm = MakeSprinklerVm(new[] { Fam1 });
        Assert.NotNull(vm.SelectedStandard);
        Assert.Equal(StandardIds.Nfpa13, vm.SelectedStandard!.Id);
        Assert.NotEmpty(vm.Hazards);
        Assert.Equal(vm.Hazards[0], vm.SelectedHazard);
    }

    [Fact]
    public void Sprinkler_ChangingStandard_RebuildsHazards()
    {
        var vm = MakeSprinklerVm(new[] { Fam1 });
        var as2118 = vm.Standards.First(s => s.Id == StandardIds.As2118);

        vm.SelectedStandard = as2118;

        Assert.Equal(as2118.SupportedHazardClasses.Count, vm.Hazards.Count);
        Assert.Equal(as2118.SupportedHazardClasses[0].Id, vm.SelectedHazard!.Id);
    }

    [Fact]
    public void Sprinkler_PreselectsMappedFamily_WhenHazardMatchesSavedMap()
    {
        var registry = StandardRegistry.CreateDefault();
        string hazardId = registry.Get(StandardIds.Nfpa13).SupportedHazardClasses[0].Id;
        var map = new Dictionary<string, string> { [$"{StandardIds.Nfpa13}|{hazardId}"] = Fam2.Label };

        var vm = new SprinklerDesignViewModel(registry, new[] { Fam1, Fam2 }, StandardIds.Nfpa13, map);

        Assert.Equal(Fam2, vm.SelectedFamily);
    }

    [Fact]
    public void Sprinkler_Place_ForwardsOptionsToRunner()
    {
        var runner = new FakePlacementRunner();
        var vm = MakeSprinklerVm(new[] { Fam1 });
        vm.Runner = runner;
        vm.SelectedFamily = Fam1;

        vm.PlaceCommand.Execute(null);

        Assert.Equal(1, runner.Calls);
        Assert.Equal(StandardIds.Nfpa13, runner.Options!.StandardId);
        Assert.Equal(vm.SelectedHazard!.Id, runner.Options.HazardClassId);
        Assert.Equal("id-1", runner.Options.FamilySymbolId);
        Assert.Equal("Placing sprinklers…", vm.Status);
    }

    [Fact]
    public void Sprinkler_Place_WithoutRunner_SetsNotConnected()
    {
        var vm = MakeSprinklerVm(new[] { Fam1 });
        vm.PlaceCommand.Execute(null);
        Assert.Equal("Not connected to Revit.", vm.Status);
    }

    [Fact]
    public void Sprinkler_ViewPlan_RequestsPlanAsDryRun()
    {
        var runner = new FakePlacementRunner();
        var vm = MakeSprinklerVm(new[] { Fam1 });
        vm.Runner = runner;
        vm.SelectedFamily = Fam1;
        vm.PreviewOnly = false; // ViewPlan must force a dry run regardless

        vm.ViewPlanCommand.Execute(null);

        Assert.Equal(1, runner.PlanCalls);
        Assert.Equal(0, runner.Calls);
        Assert.True(runner.PlanOptions!.PreviewOnly);
        Assert.Equal("id-1", runner.PlanOptions.FamilySymbolId);
    }

    // ---- Preview-only (dry run) ----

    [Fact]
    public void Detector_PreviewOnly_ForwardsFlagAndSetsPreviewStatus()
    {
        var runner = new FakeDetectorRunner();
        var vm = new DetectorDesignViewModel(new[] { Fam1 }) { Runner = runner, PreviewOnly = true };

        vm.PlaceCommand.Execute(null);

        Assert.True(runner.PreviewOnly);
        Assert.Equal("Previewing detectors…", vm.Status);
    }

    [Fact]
    public void Detector_PreviewOnly_DefaultsFalse()
    {
        var runner = new FakeDetectorRunner();
        var vm = new DetectorDesignViewModel(new[] { Fam1 }) { Runner = runner };
        vm.PlaceCommand.Execute(null);
        Assert.False(runner.PreviewOnly);
    }

    [Fact]
    public void Sprinkler_PreviewOnly_ForwardsFlagAndSetsPreviewStatus()
    {
        var runner = new FakePlacementRunner();
        var vm = MakeSprinklerVm(new[] { Fam1 });
        vm.Runner = runner;
        vm.SelectedFamily = Fam1;
        vm.PreviewOnly = true;

        vm.PlaceCommand.Execute(null);

        Assert.True(runner.Options!.PreviewOnly);
        Assert.Equal("Previewing sprinklers…", vm.Status);
    }
}
