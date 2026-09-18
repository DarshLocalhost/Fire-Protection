# FireProtectionSystem — Production TODO

Priority order: P0 = critical, P1 = high, P2 = medium, P3 = low.

## Environment status

- [ ] Restore a valid Revit runtime/build environment on this machine before claiming runtime verification.
- [ ] Ensure all required Autodesk Revit DLL references exist for the active build configuration.
- [ ] Keep the build/test gate explicit: static code review is not runtime proof.

## P0 — Must finish before client delivery

- [ ] Verify the sprinkler placement path in live Revit and remove all zero-origin, wrong-host, or invalid Z placements.
  - Area: [FireProtection.Backend/Services/Placement/Sprinklers/Final/RevitSprinklerPlacementService.cs](FireProtection.Backend/Services/Placement/Sprinklers/Final/RevitSprinklerPlacementService.cs) and [FireProtection.Backend/Services/Placement/Sprinklers/Final/Strategies](FireProtection.Backend/Services/Placement/Sprinklers/Final/Strategies)
  - Requirement: placed family instances must match the calculated candidate within tolerance and use the correct host/level.

- [ ] Replace the provisional sprinkler spacing with code-edition-approved NFPA rules.
  - Area: [FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/DefaultHazardPlacementRules.cs](FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/DefaultHazardPlacementRules.cs)
  - Requirement: no placeholder 15 ft logic remains in the production path.

- [ ] Finalize the shared device-placement framework but keep device rules independent.
  - Area: [FireProtection.Backend/Services/Placement](FireProtection.Backend/Services/Placement)
  - Requirement: sprinklers, smoke detectors, and notification appliances must each resolve their own spacing, hosting, and validation logic.

- [ ] Add a single engineering-grade result report for every placement/run.
  - Requirement: count placed, failed, skipped, review-required, reasons, host status, rule set used, and actual-vs-requested coordinates must be visible in the UI and export/report model.

- [ ] Implement a strict success/failure UI report for all device categories.
  - Requirement: user can see run summary, room-level outcomes, warning details, and a click-through issue list for failed placements.

## P1 — High-value production work

- [ ] Implement standards-based smoke detector placement logic.
  - Area: [FireProtection.Backend/Services/Placement/SmokeDetectors](FireProtection.Backend/Services/Placement/SmokeDetectors), [FireProtection.UI/ViewModels/SmokeDetectors](FireProtection.UI/ViewModels/SmokeDetectors)
  - Requirement: detector spacing, ceiling mount, slope handling, wall offset, and valid-host checks must be independent from sprinkler rules.

- [ ] Implement standards-based notification appliance placement logic.
  - Area: [FireProtection.Backend/Services/Placement/NotificationAppliances](FireProtection.Backend/Services/Placement/NotificationAppliances), [FireProtection.UI/ViewModels/NotificationAppliances](FireProtection.UI/ViewModels/NotificationAppliances)
  - Requirement: use NFPA-72 logic, not a sprinkler copy; visible and audible coverage must both be considered.

- [ ] Complete catalog validation and missing-family handling.
  - Area: [FireProtection.Backend/Services/Catalog](FireProtection.Backend/Services/Catalog), [FireProtection.UI/ViewModels/Catalog](FireProtection.UI/ViewModels/Catalog)
  - Requirement: fail-fast validation, workbook schema checks, and actionable missing-device reporting must be in place.

- [ ] Harden room/level selection for production use.
  - Area: [FireProtection.UI/ViewModels/Devices/DevicePlacementViewModelBase.cs](FireProtection.UI/ViewModels/Devices/DevicePlacementViewModelBase.cs)
  - Requirement: only eligible rooms must be selectable, and non-eligible states must explain the reason clearly.

- [ ] Add strict validation before element creation.
  - Requirement: every candidate must pass host, room, clearance, and family validity checks before a new Revit instance is attempted.

- [ ] Keep override handling traceable and bounded.
  - Requirement: every override must be visible, limited, and included in the final placement report.

- [ ] Add a device-specific preflight result model for smoke and notification devices.
  - Requirement: these flows must distinguish ELIGIBLE, BLOCKED, and UNDETERMINED with reason codes and host diagnostics, not a single boolean.

## P2 — UI and operations polish

- [ ] Standardize the UI palette and layout across all tabs.
  - Area: [FireProtection.UI/Views](FireProtection.UI/Views), [FireProtection.UI/Themes](FireProtection.UI/Themes)
  - Requirement: consistent spacing, colors, chips, and status cards across sprinkler, smoke, notification, and results views.

- [ ] Add a success summary panel for every run.
  - Requirement: show placed, failed, skipped, review-required counts and a clickable issue list with the underlying reason.

- [ ] Add a failure detail panel for every run.
  - Requirement: list every failed device/room with host issue, room issue, rule issue, or duplicate issue.

- [ ] Add export/report actions for placement results and logs.
  - Requirement: CSV and JSON exports for engineering review and client sharing.

- [ ] Implement collision workflow logic.
  - Area: [FireProtection.UI/ViewModels/Sprinklers/Collision](FireProtection.UI/ViewModels/Sprinklers/Collision)
  - Requirement: no empty shell remains.

- [ ] Harden ceiling and room-association logic.
  - Area: [FireProtection.Backend/Services/Model](FireProtection.Backend/Services/Model)
  - Requirement: correct room/level selection and no false ceiling picks.

## P3 — Cleanup and quality

- [ ] Remove dead code and unused files after the production engine stabilizes.
- [ ] Consolidate duplicate export and path logic.
- [ ] Add failing tests before changing placement logic.
  - Area: [FireProtection.Tests](FireProtection.Tests)
- [ ] Final build verification across supported configs.
- [ ] Confirm no stale sample or placeholder values remain in production code paths.

## Non-negotiable product rules

- Do not use the sprinkler rule model as the default for smoke or notification devices.
- Keep extraction, geometry normalization, calculation, and placement separate.
- Do not apply a second coordinate transform during placement.
- UI must act as a thin review layer; engineering logic stays in backend services.
- Every placement must be explainable: why it was placed, what host was chosen, and why it failed if it did not.
- Success/failure reporting is part of the product contract, not an optional add-on.

## Verification gates before client delivery

- Static build check
- Revit runtime validation for representative families
- Smoke detector validation against the active design basis
- Notification appliance validation against the active design basis
- Real placement report with success/failure reasons
- UI review for all tabs, including status and failure detail panels
- Cleanup pass for dead code and sample artifacts

## Immediate next step

1. Restore/add the Revit dependencies required for a valid build in this environment.
2. Finalize the per-device rule separation for sprinklers, smoke detectors, and notification appliances.
3. Finish the success/failure reporting UI and result export actions.
4. Validate runtime placement in Revit and lock the final engineering report.

## Current implementation status

- Shared device placement framework: implemented and separated by device type in the backend calculation layer.
- Device rule separation: improved and now device-specific for smoke detectors and notification appliances instead of a shared sprinkler-style pattern.
- Smoke detector and notification logic: implemented at the calculation layer with room-by-room candidate generation, spacing, and selection logic separated by device; production runtime validation remains pending.
- Reporting UI: planned and required before production deployment.
- Runtime validation: blocked by missing Autodesk Revit references in this environment; backend build continues to fail with CS0246 until the Revit assemblies are restored.
