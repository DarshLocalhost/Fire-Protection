# FireProtectionSystem — TODO

> Priorities: **P0** critical, **P1** high, **P2** medium, **P3** low. Groomed from actual code gaps.
> Each item notes the affected area and verification type. No code changes are implied by this list.
>
> ⚠️ **PARTIALLY STALE (2026-08-25):** items that describe the (0,0,0) *fix* as an overload tweak inside
> `RevitSprinklerPlacementService` reflect the **superseded Decision 010**. The implemented fix is
> **Decision 011** (placement-strategy pattern; no WorkPlaneBased→Level fallback). The P0 runtime-verify
> task below is still valid — it now targets the Decision 011 code. **Current truth: `PROJECT_MEMORY.md`
> §7/§15 + `DECISIONS.md` 011.**
>
> ⚠️ **CATALOG / PER-ROW PLAN (2026-09-01):** Decisions 017–020 are now locked in `DECISIONS.md`. The
> P1/P2 items below tagged "catalog / per-row" cover that work. **P0 runtime-verify remains strictly
> first** — the catalog and per-row changes are static/UI plumbing and must not be runtime-verified
> before Decision 011/012 have been runtime-confirmed.

## P0 — Critical

- [ ] **Runtime-verify placement in Revit** (Decision 011 + 012 diagnostics).
  - Area: `RevitSprinklerPlacementService.PlaceSprinklers` + `…/Final/Strategies/*` + `CeilingHostResolver`.
  - Why: Decision 011 (strategy pattern) is static/build-verified only. The historic (0,0,0) hosted-sprinkler
    bug was runtime-PROVEN; the fix is **not yet runtime-re-verified**. This is the single highest-priority
    item — do not work P1–P3 while it is open.
  - Verification: **runtime** (Revit 2025 + host `02_FireProtection_Test.rvt` + link
    `01_Architectural_Test.rvt`, family `Sprinkler - Pendent - Hosted`, type `3/4" Pendent on Drop with
    Guard`). Delete any prior misplaced (0,0,0) instances before re-running.
  - **Diagnostics to return per placed entry** (now captured after the 2026-08-25 Decision 012 edit — one
    run yields complete evidence): `ResolvedFamilyPlacementType`; `RequestedX/Y/Z` vs actual `X/Y/Z`;
    `PlacementDeviationFt`; `IsSpatiallyValid`; `StatusCode`; `HostingStrategy`; `CeilingSource`;
    `HostCeilingElementId`; `LinkInstanceName`; `ActualHostElementId` / `ActualHostName`;
    `ActualInstanceLevelId` / `ActualInstanceLevelName`; `ActualScheduleLevelName`.
  - **Pass criterion:** actual ≈ (14.12, 31.95, 12) for Room 1235683, `PlacementDeviationFt` ≤ 0.5 ft,
    `StatusCode = PLACED_AND_VALID`, strategy `WorkPlaneCeilingFace` or `WorkPlaneSketchPlane` (never
    `LevelBased`), and no instance at (0,0,0).
  - **Triage if placed:** physically-correct-but-appears-in-wrong-floor-plan ⇒ view-range /
    `ActualScheduleLevelName` (level-association) issue — **do NOT move coordinates or hard-code Z**; fix is
    a level-association / view-range change (see `PROJECT_MEMORY.md` §15 tree, R-10). Otherwise (actual XYZ
    far from requested, host null, or strategy `LevelBased`) ⇒ genuine hosting defect — determine the actual
    family hosting semantics and make the smallest production-safe strategy fix. **No speculative changes.**

## P1 — High

- [ ] **Replace provisional 15 ft spacing with NFPA13-2022-approved hazard-specific values.**
  - Area: `DefaultHazardPlacementRules` / `IHazardPlacementRules` / `BruteForceCalculationService`.
  - Why: current spacing is explicitly not compliant; every room is `ReviewRequired`.
  - Verification: static + review by a qualified engineer.
  - Notes: keep `HasApprovedRules` flag; flip to true only when real tables are encoded.
- [ ] **Excel-driven catalog for sprinkler + device families/types (Decision 017 + 020).**
  - Area: new `FireProtection.Backend/Services/Catalog/CatalogLoader.cs` + `CatalogModels.cs` +
    `CatalogValidator.cs` (ClosedXML); new `FireProtection.UI/ViewModels/Catalog/CatalogViewModel.cs`
    + `Views/Catalog/CatalogBar.xaml`; one workbook, one sheet per category, `CatalogVersion` header.
  - Why: senior direction (2026-09-01). Replaces the implicit "Revit family listing" as the catalog
    source of truth; the Revit listing is **commented out, not deleted**, behind a `UseRevitFamilyListing`
    flag so it can be re-enabled for cross-checks.
  - Verification: static + a sample catalog round-trip in the Revit-free test harness.
  - Schema: `Sprinklers` (`Category, FamilyName, TypeName, HazardClass, Mount?, Notes?`);
    `SmokeDetectors` (`Category, FamilyName, TypeName, DetectorType, Mount, CeilingSlope, Notes?`);
    `NotificationAppliances` (`Category, FamilyName, TypeName, ApplianceType, Candela, NotificationDba, Notes?`).
  - Catalog `HazardClass` values fall back to `HazardClassOptions` if missing/invalid (Excel wins if
    present and valid).
  - User selects the file **each session** (no persisted path); "Reload" button hot-reloads.
- [ ] **Per-row sprinkler family + type in the room list (Decision 017).**
  - Area: `FireProtection.UI/ViewModels/Sprinklers/BruteForce/RoomItemViewModel.cs` +
    `SprinklerBruteForceViewModel.cs` + `Views/Sprinklers/BruteForce/SprinklerBruteForceView.xaml`;
    `FireProtection.UI/Services/IPlacementInputExporter.cs` + `PlacementRoomInputItem`;
    `FireProtection.Backend/Models/Placement/Sprinklers/Final/PlacementRoomInput.cs`.
  - Why: senior direction (2026-09-01). One family per room, types filtered by family, "modified"
    indicator, "Reset to default" + "Apply to all eligible rows" per dropdown. Replaces the current
    universal `SelectedSprinklerFamily` / `SelectedSprinklerType` on the parent VM.
  - Verification: static.
  - Pair with: missing-in-model modal (see P2), `SkippedMissingFamilyCount` (P2).
- [ ] **Per-row `MaxSpacingFt` / `BoundaryClearanceFt` override threads through the BruteForce engine (Decision 018).**
  - Area: `PlacementRoomInput` (nullable `OverrideMaxSpacingFt`, `OverrideBoundaryClearanceFt`);
    `BruteForceCalculationService.Calculate` (per-room rule resolution; `IsProvisional` per-room
    when an override is in use); `RoomCalculationResult.Diagnostics` records "Override applied"
    lines; `RoomItemViewModel` gains the two new columns + range-hint tooltip.
  - Why: senior direction (2026-09-01). The override column is a hazard if it doesn't reach the
    engine (Decision 018). NFPA 13 hard limits clamp + warn.
  - Verification: static + targeted unit tests.
  - Out of scope (v1): `ObstacleClearanceFt`, `ExistingSprinklerSeparationFt`, `CoverageRadiusFt`.

- [ ] **Handle sloped / unsupported ceilings robustly.**
  - Area: `BruteForceCalculationService` Z resolution.
  - Why: sloped/stepped/missing ceilings fall back to `LevelElevation + CeilingHeight` and are flagged.
  - Verification: static + runtime spot-check.
- [ ] **Expand Revit-free test coverage.**
  - Area: `FireProtection.Tests`.
  - Why: only 14 tests; extraction, placement-input building, obstacle logic, catalog loader, and
    per-room rule override are largely untested.
  - Verification: static (automated).

## P2 — Medium

- [ ] **Missing-in-model modal + `SkippedMissingFamilyCount` (Decision 017).**
  - Area: new `FireProtection.UI/Views/Common/MissingFamiliesModal.xaml`; per-row family-availability
    probe (debounced) on dropdown change AND on Place; auto-deselect level when all its rooms fail;
    `SprinklerPlacementResult.SkippedMissingFamilyCount`; "Export missing list to CSV" button.
  - Why: senior direction (2026-09-01).
  - Verification: static + runtime.
- [ ] **Per-level Smoke/Notification declarative metadata (Decision 019).**
  - Area: new `FireProtection.UI/Views/Common/LevelSettingsPopover.xaml` ("⋯" button per level);
    per-level `SelectedDetectorType` / `SelectedMount` / `SelectedCeilingSlope` (Smoke);
    per-level `SelectedApplianceType` / `SelectedCandelaDba` (Notification, composite pair);
    per-room override mirroring today's hazard pattern.
  - Why: senior direction (2026-09-01). Declarative metadata only for v1 (no algorithmic effect
    until device placement lands). `CeilingSlope` = detector-rated-for, not room-actual.
  - Verification: static.
- [ ] **Implement Collision workflow logic.**
  - Area: `SprinklerCollisionViewModel` + extraction/calculation/placement for collisions.
  - Why: UI tab is an empty shell.
  - Verification: static + runtime.
- [ ] **Implement Smoke Detector extraction & placement.**
  - Area: new `SmokeDetectorExtractor` + UI VM + placement service.
  - Why: only an empty-shell ViewModel exists; no extractor.
  - Verification: static + runtime.
- [ ] **Implement Notification Appliance extraction & placement.**
  - Area: new extractor + `NotificationApplianceViewModel` + placement.
  - Why: only an empty-shell ViewModel exists.
  - Verification: static + runtime.
- [x] **FIX (proven): hosted-sprinkler placement uses wrong overload → instance at (0,0,0).**
  - Area: `RevitSprinklerPlacementService.PlaceSinglePoint` (`isFaceBased` detection) + `FindCeilingHost`.
  - Why: `Sprinkler - Pendent - Hosted` is `WorkPlaneBased`, but detection only matches `"FaceBased"`,
    so the level-based `NewFamilyInstance(xyz, symbol, level, NonStructural)` overload is used; a hosted
    family placed without a host lands at the project origin (0,0,0). Runtime-proven: Candidate/Passed
    Z = 12 but Actual Instance Z = 0 (see `SPRINKLER_ACTUAL_Z_DIAGNOSTIC.md`).
  - Fix: detect `WorkPlaneBased`+`FaceBased` as hosted; use face-based `NewFamilyInstance(hostRef, xyz,
    dir, symbol)`; make `FindCeilingHost` also search linked models and return a linked `Reference`
    (`Reference.CreateLinkReference`). Also record ACTUAL `instance.Location` in `PlacedSprinklerEntry`.
  - Status: root cause proven at runtime (2026-08-25); **fixed in code (2026-08-25)**; runtime verification
    pending (re-run add-in in Revit).
  - Verification: runtime (Revit) — confirm Actual Instance XYZ ≈ (14.12, 31.95, 12).
- [x] **Improve linked-ceiling hosting.**
  - Area: `RevitSprinklerPlacementService.FindCeilingHost`.
  - Why: previously searched host document only; linked ceilings fell back to level-based placement.
  - Status: resolved by the 2026-08-25 placement fix — `FindCeilingHost` now searches linked models and
    returns a host reference via `Reference.CreateLinkReference`.
  - Verification: static + runtime.
- [ ] **Harden ceiling-to-room association against wrong-floor ceiling selection.**
  - Area: `RoomExtractor.FindCeilingsForRoom` + `BruteForceCalculationService` `FirstOrDefault(FLAT)`.
  - Why: the Z-overlap window (`cBox.Min.Z <= roomTopZ + 5.0 && cBox.Max.Z >= floorZ - 0.5`) can
    include the structural slab of the level below (top at the room's floor elevation). The room's
    `Ceilings` list is consumed in iteration order, so a lower ceiling can become the placement plane,
    yielding `placementZ` at the floor instead of the true ceiling.
  - Fix sketch (deliberate, not symptom-driven): sort matched ceilings so the room's own (highest)
    ceiling is primary, and/or tighten the upward Z tolerance.
  - Status: identified during the Level/Z placement diagnostic (2026-08-25); NOT applied because it
    does not cause the reported "Ground Floor plan" symptom (which is a Revit View Range issue). See
    `SPRINKLER_LEVEL_Z_PLACEMENT_FIX_REPORT.md`.
  - Verification: static + runtime.

## P3 — Low / Technical Debt

- [ ] **Consolidate duplicate export-path helpers.**
  - Area: `PlacementInputJsonExporter.GetExportPath` vs `JsonSnapshotExporter` path logic;
    `PlacementInputJsonExporter` still has a `using Newtonsoft.Json.Linq;` only for `JObject`.
  - Why: minor duplication; the `JObject` usage is unnecessary for the current flow.
  - Verification: static.
- [ ] **Remove or quarantine stray sample/notes artifacts from the build root.**
  - Area: `extractTest.json`, `TextFile1.txt`, `*_Master_Prompt.md` files.
  - Why: `TextFile1.txt` is already excluded from the build, but these clutter the root. Do not delete
    `extractTest.json` without preserving a sample for tests if any test relies on it (currently the
    test harness builds its own data in code, not from the file).
  - Verification: static.
- [ ] **Document the `Snowdon` non-integration for future sessions.**
  - Why: generic prompts keep referencing Snowdon; it is only sample data. This is recorded in
    [[PROJECT_CONTEXT]] and [[DECISIONS]]; keep reinforcing to avoid fabricated work.
  - Verification: n/a.
- [ ] **Extend per-row override to other rule fields (Decision 018, deferred).**
  - Area: `ObstacleClearanceFt`, `ExistingSprinklerSeparationFt`, `CoverageRadiusFt`.
  - Why: senior said "not considering obstacles now"; revisit when obstacle placement is built.
  - Verification: static.

## Deferred / Uncertain (do not act without clarification)

- [ ] **NFPA13-2022 compliance strategy end-to-end** — intent not established in the repo. (P1
      covers the engine + per-row override plumbing; the actual approved values still need a
      qualified engineer.)
- [ ] **Broader multi-link placement beyond level resolution** — not designed yet.
- [ ] **Snowdon integration** — does not exist; treat as out of scope unless explicitly requested.
- [ ] **Multi-catalog per project** — locked to one shared catalog for v1 (Decision 020).

## Verification Notes

- `static`: provable by reading code / running the test harness. The harness is run with
  `dotnet run --project FireProtection.Tests/FireProtection.Tests.csproj -c Revit2025` (14/14 pass).
- `runtime`: requires Revit, an open host model, linked models, specific families, and Revit API
  behavior. Not performed in the current environment.

## Related Documentation

- [[PROJECT_CONTEXT]]
- [[ARCHITECTURE]]
- [[DECISIONS]]
- [[PROGRESS]]
- [[SESSION_NOTES]]
- [[SPRINKLER_POINT_CALCULATION_EXPLAINED]]
