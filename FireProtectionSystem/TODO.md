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
- Runtime validation: **still blocked** — requires a user session in a live Revit host.

## 2026-10-07 update — sprinklers focused, test gate now real

Scope decision: **sprinklers only for now**, internal team first, external sale later if the internal team
is satisfied. Pipe / hydraulics / fire alarm circuits / extra device types are explicitly out of scope.

### Done

- [x] `FireProtection.Tests` added to the solution (previously the solution built **no tests at all**).
- [x] Three orphan suites registered — `HazardRuleValuesTests`, `SidewallDirectionalSolverTests`,
      `PerTypeCatalogMergeTests` — they had never run while the harness printed `ALL TESTS PASSED`.
- [x] `ReportSuiteCoverage()` fails loud when a suite exists but is not registered (verified negatively).
- [x] 2 failing tests fixed; `CatalogLoaderTests` moved under `RunGuarded`.
- [x] Pre-existing solution bug: `-c Revit2024` failed to build (UI mapped to `Debug` while Backend built
      `net48`). Fixed. All 3 Revit configurations now build 0 errors and all tests pass.
- [x] Sprinkler point identification: bounded tile snapping, array extent, corrected coverage radius
      (`S/sqrt2`), advisory-only coverage, nearest-neighbour max-spacing check, single grid-maths owner,
      split skip counters, corrected mounting-plane (Z) selection. See `PROGRESS.md` for detail.
- [x] Dead code removed / made honest: coverage-pattern table (zero callers), `MinKFactor` documented as
      not enforced.

**Baseline: 388 checks, 17 suites, green on Revit2024 / 2025 / 2026; 0 build errors on all three.**

### 2026-10-07 (later) — rulebook-driven sprinkler rules

The two supplied rulebooks are now the **declared design basis**. Sprinkler rules were implemented from
them and every value is cited in
`FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/Nfpa13RulebookRules.cs`.
Added `Nfpa13RulebookRulesTests` (16 tests / 59 checks).

Implemented: min wall distance 4 in · non-90° corner 0.75 S · deflector drop 1–12 in unobstructed /
1–6 in obstructed · unobstructed-vs-obstructed classification (3.7.2) · sloped peak rule 3 ft · S × L
per-head coverage · Small Room Rule (4 conditions, 9 ft, averaging) · Ch.20 18 in zone plane ·
Three Times rule (3 × **maximum** dimension, capped 24 in) + opposing-sprinkler exception ·
permanent fixture > 4 ft wide below the zone plane.

**Known gaps — rulebook does not contain the values, nothing was invented:**
- [ ] **Beam rule (NFPA 13 Table 8.6.5.1.2)** — the rulebook references this table but does not
      reproduce it. Obstruction clear distance vs. deflector height is therefore unimplemented and
      recorded as `BeamRuleTableAvailable = false`. This is the single most significant missing rule.
- [ ] Sprinklers in every beam pocket (Ch.19 p.226) and the concrete-tee exception.
- [ ] Composite wood joist firestopping (Ch.19 p.228); concealed-space / attic rules (Ch.19 p.230).
- [ ] High-piled storage / rack storage commodity tables (Ch.37, Ch.38).
- [ ] Extended-coverage (Ch.21) and ESFR geometry — ESFR is still a review-only flag.
- [ ] Non-90° corner limit is transcribed but corner *detection* is still advisory (polygon-shape
      dependent).
- [ ] Obstruction geometry is still axis-aligned bounding boxes; curved/diagonal members are approximated.

### 2026-10-07 (latest) — catalog source switch (Model | Catalog file)

- [x] **SOURCE radio group** in the top bar, one group for all three device tabs. Model is the
      default; the choice persists between sessions along with the last workbook path.
- [x] `ModelBackedCatalog` takes family/type **names** from the model and per-type **values** from the
      workbook overlay, because the two sources are complementary, not interchangeable.
- [x] **Shared device list** across the Smoke and Notification tabs (single Revit category
      `OST_FireAlarmDevices`), with `DeviceCatalogOverlay` searching **both** workbook sheets so a
      family catalogued on one sheet still resolves on the other.
- [x] Persistent `InfoBanner` stating the active source and whether values are provisional — so a
      missing per-type value is never silent.
- [x] `FireProtectionConfig.UseRevitFamilyListing` retired; the radio is now the single source switch.
- [x] `CatalogSourceModeTests` (12 tests / 53 checks).

**Not verified:** the Revit-document reading half (`ModelFamilyEnumerator`, `FamilySymbol`
enumeration) needs one live run in Revit.

- [ ] Verify in Revit: switch Model → Catalog file → Model; confirm the dropdowns change source and
      the banner text updates.
- [ ] Confirm the shared device list looks right on both alarm tabs for a real project.
- [ ] Consider persisting the *remembered workbook* separately from the mode, so a user who works
      mostly from the model can still keep a workbook one click away.

### Next — P0, blocked on the user

- [ ] **Phase 3: live Revit verification.** Nothing is runtime-proven. The historical (0,0,0) origin-snap
      defect must be re-checked. Checklist in `PROGRESS.md`.
- [ ] Confirm placed Z lands on the intended ceiling and not the slab above (new mounting-plane ranking
      is static-only until observed in Revit).

### Still open (unchanged priorities)

- [ ] NFPA13-2022 compliant spacing — needs approved values from a fire protection engineer. The rule
      values are NFPA 13 **2002** via a client textbook and are **not** FPE-signed-off. Do not invent them.
      A rules-from-file mechanism (Phase 8) is the planned route.
- [ ] Collision tab ("Final") is an empty shell — build it or hide it.
- [ ] Report must surface reason codes (`StatusCode`) and populate `PlacementDiagnostics`.
- [ ] Cancelled run returns before `RoomReports` is populated.
- [ ] Tag placed elements with a run ID; then add "remove devices I placed" and safe re-runs.
- [ ] 2D plan preview before placing.
- [ ] 6 hard-coded Revit `HintPath`s — build only works on this machine. No `Revit2027` configuration.
- [ ] Spatial index for the free-grid candidate search — needs a real large model to validate.
- [ ] Catalog provenance: record the source/page for each value (values currently also come from
      "memory", which is a correctness risk for a life-safety tool).

### Parked — known-wrong code that must not be used until fixed

Sprinklers are the focus for now, so these are parked rather than fixed. They are small, well-understood
fixes; leaving them is only safe while the features stay off.

- [ ] Smoke/notification obstacles never reach the device calculation (`SmokeDetectorRoomInput.Obstacles`
      is never assigned; `RoomData` has no `Obstacles`). Beam clearance, obstacle spacing and the whole
      ACH branch are dead in production.
- [ ] ACH airflow table is wrong: `Nfpa72SmokeDetectorRules.cs` thresholds are roughly an order of
      magnitude off NFPA 72 Table 17.7.6.3.3.2 (actual: 2→125, 3→250, 4→375, 5→500, 6→625, 7→875,
      8+→900 ft²) and invent a 750 ft² row that does not exist.
- [ ] `AudibleCoverageEngine` has exactly one caller in the repo and it is a test — audible coverage is
      never computed. Visible coverage has no implementation at all.
- [ ] Wall-mounted strobes place nothing: `RevitNotificationAppliancePlacementExecutor` passes
      `fallbackStrategy = null`, so `OneLevelBasedHosted` families fail with
      "No strategy supports placement type".
- [ ] Provisional rules do not flag device rooms — a clean room reports `Success` on unapproved values.
