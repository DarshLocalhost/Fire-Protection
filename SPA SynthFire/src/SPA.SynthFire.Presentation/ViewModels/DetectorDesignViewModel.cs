using System.Collections.Generic;
using System.Linq;
using SPA.SynthFire.Core.Catalog;
using SPA.SynthFire.Core.Devices;
using SPA.SynthFire.UI.Views;

namespace SPA.SynthFire.UI.ViewModels;

/// <summary>
/// View model for the fire-detection window: family/kind selection, redesign flag,
/// status text and the place command. Revit-free and unit-testable; the window binds
/// to it and forwards status updates raised from the Revit event handler.
/// </summary>
public sealed class DetectorDesignViewModel : ObservableObject
{
    private FamilyTypeOption? _selectedFamily;
    private DetectorKind _selectedKind = DetectorKind.Smoke;
    private bool _clearPrevious = true;
    private bool _previewOnly;
    private string _status = "Ready.";

    public DetectorDesignViewModel(IReadOnlyList<FamilyTypeOption> families)
    {
        Families = families;
        // Prefer a family that actually reads as a ceiling detector over a notification
        // appliance that happens to sort first in the OST_FireAlarmDevices list.
        _selectedFamily = families.FirstOrDefault(f => FireAlarmFamilyClassifier.IsDetector(f.Label))
                          ?? (families.Count > 0 ? families[0] : null);
        PlaceCommand = new RelayCommand(Place);
        ViewPlanCommand = new RelayCommand(ViewPlan);
        UpdateFamilyWarning();
    }

    public IReadOnlyList<FamilyTypeOption> Families { get; }

    public IReadOnlyList<DetectorKind> Kinds { get; } = new[] { DetectorKind.Smoke, DetectorKind.Heat };

    public FamilyTypeOption? SelectedFamily
    {
        get => _selectedFamily;
        set { if (SetProperty(ref _selectedFamily, value)) UpdateFamilyWarning(); }
    }

    public DetectorKind SelectedKind
    {
        get => _selectedKind;
        set => SetProperty(ref _selectedKind, value);
    }

    public bool ClearPrevious
    {
        get => _clearPrevious;
        set => SetProperty(ref _clearPrevious, value);
    }

    /// <summary>Solve + show the report without writing detectors into the model.</summary>
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
    public IDetectorRunner? Runner { get; set; }

    public RelayCommand PlaceCommand { get; }

    /// <summary>Open the read-only 2D coverage plan (dry run) for the selected detector kind.</summary>
    public RelayCommand ViewPlanCommand { get; }

    private void Place()
    {
        if (SelectedFamily is not { } family) { Status = "Select a detector family/type."; return; }
        if (Runner is null) { Status = "Not connected to Revit."; return; }

        Status = PreviewOnly ? "Previewing detectors…" : "Placing detectors…";
        Runner.RequestPlace(family.Id, SelectedKind, ClearPrevious, PreviewOnly, family.Mount);
    }

    // The coverage plan's grid depends only on the detector kind's spacing rules, not the family
    // symbol. The family is passed through only so a right-click on a gap can click-to-place that
    // family (a null family still opens the plan as a read-only viewer). Always a dry run for the grid.
    private void ViewPlan()
    {
        if (Runner is null) { Status = "Not connected to Revit."; return; }
        Status = "Building coverage plan…";
        Runner.RequestPlan(SelectedKind, SelectedFamily?.Mount ?? DeviceMount.Ceiling, SelectedFamily?.Id);
    }

    // A notification appliance (horn/strobe) picked for a detector run will be wall-hosted
    // by the placer regardless of the smoke/heat kind; warn but don't block — the label
    // heuristic can be wrong and the user may know better.
    private void UpdateFamilyWarning() =>
        Status = SelectedFamily is { } f && !FireAlarmFamilyClassifier.IsDetector(f.Label)
            ? "Heads up: this family looks like a notification appliance, not a ceiling detector."
            : "Ready.";
}
