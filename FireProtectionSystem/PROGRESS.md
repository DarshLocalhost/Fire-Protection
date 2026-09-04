# FireProtectionSystem — Progress

> Status legend:
> ✅ Implemented (static) — code exists and compiles; runtime not necessarily verified.
> ✅ Implemented & runtime-verified — confirmed against a live Revit model.
> 🟡 Partial / shell — UI or stubs present, logic not complete.
> 🔴 Not implemented — only referenced or absent.
> ❓ Uncertain — cannot confirm from repository.
>
> "Static" means provable by reading code. "Runtime-verified" requires Revit + an open model.
>
> ⚠️ **PARTIALLY STALE (2026-08-25):** any "In Progress" note describing the placement fix as
> `isFaceBased`/`WorkPlaneBased` detection with a Level *fallback* reflects the **superseded Decision 010**.
> The code now implements **Decision 011** (placement-strategy pattern; post-placement validation; no
> WorkPlaneBased→Level fallback). **Current truth: `PROJECT_MEMORY.md` + `DECISIONS.md` 011.**

## Legend for columns
- **Status**: one of the symbols above.
- **Verification**: `static` (code), `runtime` (run in Revit), `none`.

## Extraction (Phase 1)

| Capability | Status | Verification | Notes |
|---|---|---|---|
| Level extraction (host + links) | ✅ Implemented | static | `LevelExtractor`; elevations normalized. |
| Room extraction (loops, boundaries) | ✅ Implemented | static | `RoomExtractor`; outer/inner loops, curved boundaries. |
| Ceiling extraction & slope classification | ✅ Implemented | static | `CeilingExtractor`; FLAT/SLOPED/STEPPED/NONE. |
| Obstacle extraction | ✅ Implemented | static | `ObstacleExtractor`; columns/beams/walls/MEP curves as AABB. |
| Existing sprinkler extraction | ✅ Implemented | static | `ExistingSprinklerExtractor`. |
| Hazard classification (tagging) | ✅ Implemented | static | `HazardClassifier.ClassifyByName`; tagging only. |
| Linked-model coordinate normalization | ✅ Implemented | static | `RevitModelContext` transforms. |
| Model validation | ✅ Implemented | static | `ModelExtractionValidator`. |
| JSON snapshot export | ✅ Implemented | static | `JsonSnapshotExporter`. |
| Per-room error isolation | ✅ Implemented | static | warnings, non-aborting. |
| Read-only / no-transaction extraction | ✅ Implemented | static | Decision 005. |

## Sprinkler Placement (Phase 2 — BruteForce)

| Capability | Status | Verification | Notes |
|---|---|---|---|
| Placement input builder (in-memory) | ✅ Implemented | static | `PlacementInputBuilder.Build`. |
| BruteForce calculation engine | ✅ Implemented | static | `BruteForceCalculationService`; 1 ft grid, 15 ft greedy select. |
| X/Y grid + greedy selection | ✅ Implemented | static | See [[SPRINKLER_POINT_CALCULATION_EXPLAINED]]. |
| Z resolution (ceiling/level) | ✅ Implemented | static | fallback to level+height when ceiling missing/sloped. |
| Obstacle & boundary clearance | ✅ Implemented | static | 1 ft placeholder clearances. |
| Placement input JSON export | ✅ Implemented | static | `PlacementInputJsonExporter.ExportInput`. |
| Placement result JSON export | ✅ Implemented | static | `ExportPlacementResult`. |
| Revit `FamilyInstance` placement | ✅ Implemented | static | `RevitSprinklerPlacementService.PlaceSprinklers`. |
| Linked-level resolution | ✅ Implemented | static | `ResolveHostLevel`. |
| Ceiling-host detection for placement | ✅ Implemented | static | `CeilingHostResolver.FindCeilingHost` searches host + linked ceilings, returns a downward-face `Reference` (link refs via `CreateLinkReference`). **Decision 011:** `WorkPlaneBased` uses that ceiling face, else a `SketchPlane` honoring world Z — it **never** uses the Level overload (no WorkPlaneBased→Level fallback). ~~Earlier note claimed a Level fallback (Decision 010, superseded).~~ Runtime pending. |
| Actual host/level read-back diagnostics | ✅ Implemented | static | **Decision 012 (2026-08-25):** success path records `ActualHostElementId/Name`, `ActualInstanceLevelId/Name`, `ActualScheduleLevelName` read back from the created instance (best-effort, read-only) + `FamilyPlacementType`/`HostCeilingElementId`/`LinkInstanceName`. Diagnostics-only; no placement semantics changed. Distinguishes a hosting defect from a view-range/level-association symptom in one run. Runtime pending. |
| NFPA13-2022 compliant spacing | 🔴 Not implemented | none | provisional 15 ft only; `HasApprovedRules=false`. |
| Per-row sprinkler family/type (universal → per-room) | ✅ Implemented | static | **Decision 017 (2026-09-01).** Per-row family + type dropdowns in the room list, types filtered by family, "modified" indicator, "Reset to default" + "Apply to all eligible rows". Backed by `ICatalog` (Excel). Missing-family modal flow wired in the placement path. |
| Per-row `MaxSpacingFt` / `BoundaryClearanceFt` override | ✅ Implemented | static | **Decision 018 (2026-09-01).** Nullable fields on `PlacementRoomInput`; `BruteForceCalculationService.ApplyPerRoomOverrides` clones the rule returned by `IHazardPlacementRules.GetRules(hazardClass)` and applies the per-row overrides when present. Per-room `IsProvisional` is set when an override is in use; the preflight continues to use the un-overridden rule set so the NFPA compliance check is preserved. Out-of-range `MaxSpacingFt` is clamped to the provisional ceiling (15 ft) and a diagnostic line is recorded. Obstacle + existing-sprinkler separation NOT overridable in v1. |
| Excel-driven catalog (Sprinkler/Smoke/Notification) | ✅ Implemented | static | **Decision 020 (2026-09-01).** One workbook, one sheet per category, `CatalogVersion` header, fail-fast `CatalogLoader` (ClosedXML, Backend). User-selectable path each session + "Reload" button (no persistence). Catalog version in top bar. 8 catalog tests + 5 BruteForce override tests PASS in the headless standalone runner (`FireProtection.CatalogStandalone`). |
| Missing-in-model modal (interactive Proceed/Cancel + CSV export) | ✅ Implemented | static | **Decision 017 (2026-09-01).** `MissingFamiliesModal` shows `(Room, Family, Type)` for missing entries; "Proceed with available" / "Cancel" + "Export missing list to CSV" button. `ISprinklerPlacementService.ProbeMissingFamilies(calcResult)` reads the per-row family/type from `RoomCalculationResult` and probes the live Revit document. `SprinklerPlacementResult.SkippedMissingFamilyCount` tracks the per-row skip count; `RevitSprinklerPlacementService.PlaceSprinklers` now resolves symbols **per room** (Decision 017) and groups activations. |
| Per-level Smoke/Notification declarative metadata (popover "⋯") | ✅ Implemented | static | **Decision 019 (2026-09-01).** UI-only. `LevelSettingsPopover` ("⋯" button per level) drives `DeviceLevelItemViewModel` (smoke: DetectorType/Mount/CeilingSlope; notification: ApplianceType + composite Candela+dBA). Level value propagates as default to rooms via `DeviceRoomItemViewModel.GetOverride/SetOverride`; rooms can override. "Planning only — backend pending" badge. No algorithmic effect until device placement lands. |

## Collision Workflow

| Capability | Status | Verification | Notes |
|---|---|---|---|
| UI tab (Collision) | 🟡 Shell | static | `SprinklerCollisionViewModel` exists, empty. |
| Collision extraction/calculation/placement | 🔴 Not implemented | none | no logic observed. |

## Smoke Detector Workflow

| Capability | Status | Verification | Notes |
|---|---|---|---|
| UI tab (Smoke Detectors) | ✅ Implemented (shared base) | static | `SmokeDetectorViewModel` inherits `DevicePlacementViewModelBase`; hosts `DevicePlacementView` + NFPA-72 params (detector type / mount / ceiling slope). Placement disabled (backend deferred). |
| Extraction / placement logic | 🔴 Not implemented | none | no `SmokeDetectorExtractor`; not in extraction pipeline. Backend device placement deferred (UI-first slice). |

## Notification Appliance Workflow

| Capability | Status | Verification | Notes |
|---|---|---|---|
| UI tab (Notification Appliances) | ✅ Implemented (shared base) | static | `NotificationApplianceViewModel` inherits `DevicePlacementViewModelBase`; hosts `DevicePlacementView` + NFPA-72 params (appliance type / candela / dBA). Placement disabled (backend deferred). |
| Extraction / placement logic | 🔴 Not implemented | none | not present. Backend device placement deferred (UI-first slice). |

## User Interface (WPF)

| Capability | Status | Verification | Notes |
|---|---|---|---|
| Ribbon / entry point | ✅ Implemented | static | `FireProtectionApplication` + `FireProtectionCommand`. |
| Main window & MVVM base | ✅ Implemented | static | `MainWindow`, `ObservableObject`, `RelayCommand`. |
| Room selection / data binding | ✅ Implemented | static | `MainWindowViewModel`, `FireProtectionUiData`. |
| BruteForce UI (run, review, export) | ✅ Implemented | static | `SprinklerBruteForceViewModel`. |
| Room eligibility (3-state deterministic preflight) | ✅ Implemented | static | `RoomItemViewModel.IsEligible/IsBlocked/IsUndetermined/EligibilityReason`; `RevitSprinklerPlacementService.EvaluateRoomEligibility` read-only preflight (no live probe); FaceBased & WorkPlaneBased require a usable ceiling host, OneLevelBased a resolvable level; `yfbxcv 1234` regression fixed (no-ceiling WorkPlaneBased ⇒ BLOCKED). |
| Smart Level Select All / Clear All toggle | ✅ Implemented | static | single `ToggleSelectAllLevelsCommand`; label derived from `AreAllSelectableLevelsSelected` (levels with rooms only). |
| Smart Room Select All / Clear All toggle | ✅ Implemented | static | single `ToggleSelectAllRoomsCommand`; label derived from `AreAllSelectableRoomsSelected` (ELIGIBLE rooms only); manual changes synced via `PropertyChanged`. |
| Blocked/undetermined-room exclusion from selection/placement | ✅ Implemented | static | `ToggleSelectAllRooms` selects only ELIGIBLE; room CheckBox `IsEnabled=IsEligible`; non-eligible auto-deselected. |
| "Show eligible rooms only" / "Show levels with rooms only" toggles | ✅ Implemented | static | visibility-only via `ICollectionView.Refresh()`; source/selection preserved. |
| Default selection (eligible selected, blocked not) | ✅ Implemented | static | `ApplyDefaultSelection()` in ctor + `Reset`. |
| Preliminary/Final tab swap (UI labels + order) | ✅ Implemented | static | BruteForce→"Preliminary" (first), Collision→"Final"; `SprinklerViewModel` order + `SprinklerView.xaml` headers. |
| Hazard color coding | ✅ Implemented | static | `HazardClassToBrushConverter`. |

## Hazard Classification

| Capability | Status | Verification | Notes |
|---|---|---|---|
| Name-based classification | ✅ Implemented | static | `HazardClassifier`. |
| Hazard-specific engineering rules | 🔴 Not implemented | none | all classes use 15 ft placeholder. |

## Build & Test

| Capability | Status | Verification | Notes |
|---|---|---|---|
| Build Revit2024/2025/2026 | ✅ Implemented | static | 0 errors across configs. |
| Revit-free calculation tests | ✅ Implemented | static | 14/14 pass (`FireProtection.Tests`). |
| Runtime Revit placement test | 🔴 Not implemented | none | needs live Revit host; not run in dev env. |

## Deployment

| Capability | Status | Verification | Notes |
|---|---|---|---|
| Auto-deploy `.addin` + DLL copy | ✅ Implemented | static | `DeployToRevit` target; Application-only add-in. |
| Multi-version support | ✅ Implemented | static | 2024/2025/2026 configs. |

## Recent Significant Work

- **Code cleanup** — removed dead exporters/overloads (`ExportCalculationResult`,
  `GetDefaultCalculationExportPath`, `Extract`, `ExtractFromHostModel`, 3 `UiLauncher.Show` overloads,
  MainWindow 1/2/3-arg constructors) and unused `using`s. Builds clean; 14/14 tests pass.
  See [[SESSION_NOTES]].
- **Sprinkler placement Z bug fix (2026-08-25, first pass)** — Root cause (proven at runtime): hosted
  ceiling sprinklers (`FamilyPlacementType` = `WorkPlaneBased`, not `FaceBased`) were placed via the
  level-based overload and landed at the project origin (0,0,0). Fixed `RevitSprinklerPlacementService`:
  detect `WorkPlaneBased`+`FaceBased` as hosted, make `FindCeilingHost` search linked models and return a
  host reference via `Reference.CreateLinkReference`, and record the ACTUAL `instance.Location`.
  Compiles clean on Revit2024/2026; Revit2025 blocked only by a VS/Revit file lock. See
  [[SPRINKLER_ACTUAL_Z_DIAGNOSTIC]].
- **Sprinkler "all failed" fix (2026-08-25, second pass)** — The first-pass fix still routed
  `WorkPlaneBased` into the face-host-only branch; when no ceiling face was found (ceilings are
  linked-only, where hosted placement on a linked face is not reliably supported by the Revit API),
  `FindCeilingHost` returned null and **every** sprinkler failed. Refined fix: `WorkPlaneBased` now falls
  back to the Level-based overload (valid — `WorkPlaneBased` = hosted on a work plane, which a Level
  satisfies) and places at the already-normalized candidate XYZ; `FaceBased` still requires a host face;
  pendent mounting now selects the **downward** ceiling underside face. Added Phase 2/11 diagnostics
  (real `FamilyPlacementType`, hosting strategy, ceiling source, link instance, actual vs requested XYZ,
  full exception detail). Runtime verification in Revit still pending.
- **Revit placement layer** — `RevitSprinklerPlacementService` implemented incl. linked-level
  resolution, replacing the prior external-process approach.
- **BruteForce UI selection/eligibility (2026-08-26)** — UI-only: rooms flagged eligible/blocked from
  existing authoritative `RoomUiData.Geometry` (polygon ≥ 3 points + `CeilingHeightFt`); blocked rooms shown
  disabled with tooltip and excluded from Select All / default selection / placement. Added "Show eligible
  rooms only" and "Show levels with rooms only" visibility toggles (source collections + selection preserved
  via `ICollectionView.Refresh()`); `ApplyDefaultSelection()` seeds eligible levels + eligible rooms. Swapped
  the Preliminary/Final tab labels + order (BruteForce = "Preliminary", Collision = "Final") — presentation
  only; no calculation/placement/NFPA logic touched. UI builds clean (0 errors). See [[SESSION_NOTES]].
- **BruteForce engine** — X/Y/Z calculation completed and documented.
- **Placement Preflight / Eligibility (2026-08-26)** — Implemented a reusable, authoritative pre-placement
  eligibility layer so the UI blocks rooms the production pipeline provably cannot place. `ISprinklerPlacementService`
  gained `EvaluateRoomEligibility(RoomUiData, family, type)`; the Backend `RevitSprinklerPlacementService`
  implements it by **reusing the exact** `ResolveSymbol` + `_strategies.CanHandle` + `ResolveHostLevel` +
  `CeilingHostResolver.FindCeilingHost` logic used by real placement (single source of truth — UI does NOT
  re-derive Revit hosting rules). Result DTO `PlacementEligibilityResult` (UI, Revit-free) carries
  `IsEligible/Reason/StatusCode/FamilyPlacementType/HostingStrategy/CeilingSource/LinkInstanceName/HostLevel*`.
  UI `RoomItemViewModel.SetEligibility` applies it; `SprinklerBruteForceViewModel.RefreshEligibility()` runs it on
  ctor/Reset/family+type change, auto-deselects any room that becomes blocked, and the room grid + single
   Select-All toggle already disable blocked rooms. `CollectSelectedRooms` now guards against blocked rooms.
   **Status: UI builds clean (0 errors). Backend not buildable in headless CLI (Revit API CS0246 — environmental);
   type-correctness verified by inspection. Runtime Revit verification pending.** See [[SESSION_NOTES]], [[DECISIONS]] (013).
- **Placement Preflight → AUTHORITATIVE probe (2026-08-26, supersedes first-pass preflight above)** — The
  first-pass preflight was **runtime-insufficient**: it tested only a representative centroid point and treated
  `WorkPlaneBased` as always eligible, so rooms reported eligible while actual placement produced
  `Placed=0, INVALID=N, Failed=N`. Rewrote `EvaluateRoomEligibility` to be authoritative: the UI computes each
  room's **real candidate points** via the exact `BruteForceCalculationService` (`CalculateBruteForce`), then the
  Backend probes **real placement** of every candidate through the same `strategy.Place` / `ResolveHostLevel` /
  `CeilingHostResolver` path used by `PlaceSprinklers`, inside a **rolled-back `Transaction`**. A room is eligible
  only if ≥1 candidate creates a `FamilyInstance` that is spatially valid (deviation ≤ tolerance) — the identical
  success criterion as real placement, so the `INVALID` blind spot is eliminated. Eligibility is fail-closed
  (`NO_CANDIDATE_POINTS` / `NO_PLACEABLE_CANDIDATE` / `NO_VALID_CANDIDATE` / `PREFLIGHT_ERROR`). Added
  `CollectAllVisibleRooms()`, `ClearEligibilityCache()`, `SelectedVisibleEligibleRoomCount` gating for
  `CanExecutePlaceSprinklers`, and `[ROOM-ELIGIBILITY]` / `[ROOM-SELECTION-GUARD]` `Debug` diagnostics (§17).
  **Status: UI builds clean (0 errors). Backend not buildable in headless CLI (Revit API CS0246 — environmental);
  type-correctness verified by inspection. Runtime Revit verification pending — the probe cannot be exercised without
  a live Revit host, but its logic is the same code path as placement, which is the fix's whole point.** See
   [[SESSION_NOTES]], [[DECISIONS]] (013), PROJECT_MEMORY §8b.
- **Eligibility 4-state model — ELIGIBLE/BLOCKED/PLACEMENT_ERROR/UNKNOWN (2026-08-26, Decision 014)** — Split the
  fail-closed eligibility outcome so real placement defects are never hidden as BLOCKED. `PlacementEligibilityResult`
  now carries an `EligibilityState` with derived `IsEligible/IsBlocked/IsPlacementError/IsUnknown`. `RevitSprinklerPlacementService
  .EvaluateRoomEligibility` classifies: ELIGIBLE (≥1 candidate placed + spatially valid); BLOCKED (deterministic
  inability only — including FaceBased `REQUIRED_HOST_UNAVAILABLE`); PLACEMENT_ERROR (`CREATED_BUT_INVALID`,
  `PROBE_EXCEPTION`, or any unexpected API/runtime failure while a candidate existed and placement was attempted);
  UNKNOWN (family/type not selected, or candidate calculation could not run). The probe inspects `PlacementOutcome
  .ErrorCode` and distinguishes deterministic vs defect; any exception in the probe harness/strategy is `PLACEMENT_ERROR`,
  not BLOCKED — **this breaks the circular hide** (a broken placement path now shows red, countable `PLACEMENT_ERROR`
  rooms instead of a blanket `BLOCKED`). `RefreshEligibility` sets UNKNOWN for every room when the candidate calculation
  throws (instead of probing a null list → BLOCKED) and auto-deselects any non-ELIGIBLE room. Only ELIGIBLE rooms are
  selectable (grid `IsEnabled=IsEligible`, single Select-All selects only `IsEligible`, "Show eligible only" filters
  to `IsEligible`). Added per-candidate `[ROOM-CANDIDATE-DIAGNOSTIC]` `Debug` output (requested vs actual XYZ, delta,
  distance, validation status, exception detail) to pinpoint the exact defect. XAML mutes all non-eligible rooms and
  gives PLACEMENT_ERROR a red outline, UNKNOWN an amber outline. **Status: UI builds clean (0 errors). Backend not
  buildable headless (Revit API CS0246 — environmental); type-correct by inspection. Runtime Revit verification pending.**
  The `(0,0,0)` origin-snap defect is already fixed by Decisions 011/012; if a residual `CREATED_BUT_INVALID` appears at
  runtime it will surface as `PLACEMENT_ERROR` (with requested-vs-actual deviation in `Reason`), to be fixed at the
  placement/coordinate source — **not** by enlarging `PlacementValidationToleranceFt`. See [[SESSION_NOTES]], [[DECISIONS]] (014), PROJECT_MEMORY §8b.

- **Eligibility 3-state deterministic preflight (2026-08-26, Decision 016) — SUPERSEDES the authoritative-probe & 4-state bullets above.** The continuation master prompt replaced the live rolled-back-`Transaction` probe AND the 4-state (PLACEMENT_ERROR/UNKNOWN) model with a **3-state** model (ELIGIBLE/BLOCKED/UNDETERMINED) and **forbids any live `strategy.Place()` probe**. `RevitSprinklerPlacementService.EvaluateRoomEligibility` now performs **read-only** queries only (`ResolveFamily`, `ResolveHostLevel`, `CeilingHostResolver.FindCeilingHost`): FaceBased **and** WorkPlaneBased REQUIRE a usable ceiling/host face (the `yfbxcv 1234` regression — a no-ceiling WorkPlaneBased room is now BLOCKED, not ELIGIBLE); OneLevelBased requires only a resolvable level; a numeric `CeilingHeightFt` is never host proof. Family/type not resolved ⇒ UNDETERMINED (config error, not "all blocked"); candidates null ⇒ UNDETERMINED `CALCULATION_FAILED`; unexpected exception ⇒ UNDETERMINED `PREFLIGHT_ERROR`. `RoomItemViewModel` carries `IsEligible/IsBlocked/IsUndetermined`; the room grid mutes non-eligible, red-outlines BLOCKED, amber-outlines UNDETERMINED, and shows an inline status line + tooltip. `RefreshEligibility` runs on ctor/Reset/family+type change/per-room hazard edit and auto-deselects non-eligible rooms.   **Status: UI builds clean (0 errors). Backend not buildable headless (Revit API CS0246 — environmental); type-correct by inspection. Runtime Revit verification pending.** See [[DECISIONS]] (016), PROJECT_MEMORY §8b.

- **Selection contract + level↔room sync (2026-08-26, master prompt: SPRINKLER_SELECTION_LINKED_MODEL_PRODUCTION)** — Built on the 3-state preflight. All ELIGIBLE rooms are selected by default; BLOCKED/UNDETERMINED are frozen (`IsEnabled=IsEligible`), unchecked, and never selectable. `RefreshEligibility` now normalizes selection: non-eligible always deselected; a room that *just became* ELIGIBLE is selected by default; already-eligible keeps the user's manual choice. Level selection selects/deselects only that level's ELIGIBLE rooms (other levels unaffected). Room Select All/Clear All act on visible ELIGIBLE only. `CollectSelectedRooms` rejects `!IsEligible` (defensive final-placement guard). Linked-model eligibility cache key now includes room `Name`+`LevelName` to avoid cross-link staleness. Added `FireProtection.Tests/BruteForceSelectionTests.cs` (Revit-free, fake `ISprinklerPlacementService`) covering §22 A–G + K. **Status: UI builds clean. `FireProtection.Tests` NOT executed headless (Backend Revit API CS0246) — tests ADDED, run under Revit-enabled build.** See [[SESSION_NOTES]].


## In Progress

- Active fix awaiting **runtime verification in Revit**: confirm placed sprinkler `instance.Location`
  ≈ (14.12, 31.95, 12) for `02_FireProtection_Test` (host) + `01_Architectural_Test` (link). Then remove
  any stray misplaced (0,0,0) instances from prior runs. **P0 — must complete before any of the
  catalog / per-row / device-popover work is runtime-verified.**
- Next verification gap after that: **NFPA-compliant spacing** (replacing provisional 15 ft values).

## Completed Milestones

- Extraction pipeline (Phase 1) — implemented.
- BruteForce sprinkler calculation engine — implemented.
- Revit placement service (element creation) — implemented.
- End-to-end documentation (`SPRINKLER_POINT_CALCULATION_EXPLAINED.md`) — written.
- Dead-code cleanup — completed and verified.
- Plan for catalog + per-row family/type + per-row spacing override (Decisions 017–020) — locked.
- **Catalog + per-row family/type + per-row spacing override + device-popover implementation
  (Decisions 017, 018, 019, 020) — implemented (static, 2026-09-01).** Backend
  `CatalogLoader` / `CatalogService` (ClosedXML), `ICatalog` (UI), `CatalogViewModel` +
  `CatalogBar` (top bar with Browse/Reload/version), per-row `SelectedFamily` / `SelectedType` /
  `MaxSpacingFtOverride` / `BoundaryClearanceFtOverride` + bulk-apply commands, per-row
  `MissingFamiliesModal` (Proceed/Cancel + "Export missing list to CSV") +
  `SkippedMissingFamilyCount`, per-row symbol resolution in `PlaceSprinklers`,
  `BruteForceCalculationService.ApplyPerRoomOverrides` (NFPA clamp + per-room `IsProvisional`),
  `LevelSettingsPopover` (⋯ per level) for Smoke Detector / Notification Appliance declarative
  metadata. 22/22 standalone tests pass (`FireProtection.CatalogStandalone`).
  **Runtime verification in Revit is still P0-first** — none of this has been live-Revit-verified.

## Related Documentation

- [[PROJECT_CONTEXT]]
- [[ARCHITECTURE]]
- [[DECISIONS]]
- [[TODO]]
- [[SESSION_NOTES]]
- [[SPRINKLER_POINT_CALCULATION_EXPLAINED]]
