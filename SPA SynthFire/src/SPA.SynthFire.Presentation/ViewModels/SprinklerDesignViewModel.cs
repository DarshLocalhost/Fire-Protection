using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SPA.SynthFire.Core.Standards;

namespace SPA.SynthFire.UI.ViewModels;

/// <summary>
/// View model for the sprinkler-design window: family selection, design standard and
/// default hazard (with the standard → hazard cascade), a family auto-preselect driven
/// by the saved family map, the redesign flag, status text and the place command.
/// Revit-free and unit-testable; the window binds to it.
/// </summary>
public sealed class SprinklerDesignViewModel : ObservableObject
{
    private readonly IReadOnlyDictionary<string, string>? _familyMap;

    private FamilyTypeOption? _selectedFamily;
    private IDesignStandard? _selectedStandard;
    private HazardClassification? _selectedHazard;
    private bool _clearPrevious = true;
    private bool _previewOnly;
    private string _status = "Ready.";

    public SprinklerDesignViewModel(
        StandardRegistry registry,
        IReadOnlyList<FamilyTypeOption> families,
        string? defaultStandardId,
        IReadOnlyDictionary<string, string>? familyMap)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _familyMap = familyMap;

        Families = families;
        _selectedFamily = families.Count > 0 ? families[0] : null;
        Standards = registry.All.ToList();
        PlaceCommand = new RelayCommand(Place);
        ViewPlanCommand = new RelayCommand(ViewPlan);

        // Selecting the standard cascades into the hazard list (see the setter).
        SelectedStandard = Standards.FirstOrDefault(s =>
                               defaultStandardId is not null &&
                               string.Equals(s.Id, defaultStandardId, StringComparison.OrdinalIgnoreCase))
                           ?? Standards.FirstOrDefault();
    }

    public IReadOnlyList<FamilyTypeOption> Families { get; }

    public IReadOnlyList<IDesignStandard> Standards { get; }

    public ObservableCollection<HazardClassification> Hazards { get; } = new();

    public FamilyTypeOption? SelectedFamily
    {
        get => _selectedFamily;
        set => SetProperty(ref _selectedFamily, value);
    }

    public IDesignStandard? SelectedStandard
    {
        get => _selectedStandard;
        set
        {
            if (!SetProperty(ref _selectedStandard, value)) return;
            RebuildHazards();
        }
    }

    public HazardClassification? SelectedHazard
    {
        get => _selectedHazard;
        set
        {
            if (!SetProperty(ref _selectedHazard, value)) return;
            PreselectMappedFamily();
        }
    }

    public bool ClearPrevious
    {
        get => _clearPrevious;
        set => SetProperty(ref _clearPrevious, value);
    }

    /// <summary>Solve + show the report without writing sprinklers into the model.</summary>
    public bool PreviewOnly
    {
        get => _previewOnly;
        set => SetProperty(ref _previewOnly, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    /// <summary>Set by the Revit side after construction (breaks the ctor cycle).</summary>
    public IPlacementRunner? Runner { get; set; }

    public RelayCommand PlaceCommand { get; }

    /// <summary>Solve (dry run) and open the 2D coverage plan without writing to the model.</summary>
    public RelayCommand ViewPlanCommand { get; }

    private void RebuildHazards()
    {
        Hazards.Clear();
        if (_selectedStandard is null) { SelectedHazard = null; return; }
        foreach (var h in _selectedStandard.SupportedHazardClasses) Hazards.Add(h);
        SelectedHazard = Hazards.Count > 0 ? Hazards[0] : null;
    }

    /// <summary>Selects the family the user last mapped to this standard+hazard, if any.</summary>
    private void PreselectMappedFamily()
    {
        if (_familyMap is null || _selectedStandard is null || _selectedHazard is null) return;
        if (!_familyMap.TryGetValue($"{_selectedStandard.Id}|{_selectedHazard.Id}", out string? label)
            || string.IsNullOrEmpty(label)) return;

        var match = Families.FirstOrDefault(f => f.Label == label);
        if (match is not null) SelectedFamily = match;
    }

    private void Place()
    {
        if (SelectedFamily is not { } family) { Status = "Select a sprinkler family/type."; return; }
        if (SelectedStandard is not { } standard || SelectedHazard is not { } hazard)
        {
            Status = "Select a standard and hazard class."; return;
        }
        if (Runner is null) { Status = "Not connected to Revit."; return; }

        Status = PreviewOnly ? "Previewing sprinklers…" : "Placing sprinklers…";
        Runner.RequestPlace(new PlacementOptions(standard.Id, hazard.Id, family.Id, family.Label, PreviewOnly, family.Mount));
    }

    private void ViewPlan()
    {
        if (SelectedFamily is not { } family) { Status = "Select a sprinkler family/type."; return; }
        if (SelectedStandard is not { } standard || SelectedHazard is not { } hazard)
        {
            Status = "Select a standard and hazard class."; return;
        }
        if (Runner is null) { Status = "Not connected to Revit."; return; }

        Status = "Building coverage plan…";
        Runner.RequestPlan(new PlacementOptions(standard.Id, hazard.Id, family.Id, family.Label, PreviewOnly: true, Mount: family.Mount));
    }
}
