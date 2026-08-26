# FireProtectionSystem — TODO

> Priorities: **P0** critical, **P1** high, **P2** medium, **P3** low. Groomed from actual code gaps.
> Each item notes the affected area and verification type. No code changes are implied by this list.
>
> ⚠️ **PARTIALLY STALE (2026-08-25):** items that describe the (0,0,0) *fix* as an overload tweak inside
> `RevitSprinklerPlacementService` reflect the **superseded Decision 010**. The implemented fix is
> **Decision 011** (placement-strategy pattern; no WorkPlaneBased→Level fallback). The P0 runtime-verify
> task below is still valid — it now targets the Decision 011 code. **Current truth: `PROJECT_MEMORY.md`
> §7/§15 + `DECISIONS.md` 011.**

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
- [ ] **Handle sloped / unsupported ceilings robustly.**
  - Area: `BruteForceCalculationService` Z resolution.
  - Why: sloped/stepped/missing ceilings fall back to `LevelElevation + CeilingHeight` and are flagged.
  - Verification: static + runtime spot-check.
- [ ] **Expand Revit-free test coverage.**
  - Area: `FireProtection.Tests`.
  - Why: only 14 tests; extraction, placement-input building, and obstacle logic are largely untested.
  - Verification: static (automated).

## P2 — Medium

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

## Deferred / Uncertain (do not act without clarification)

- [ ] **NFPA13-2022 compliance strategy end-to-end** — intent not established in the repo.
- [ ] **Broader multi-link placement beyond level resolution** — not designed yet.
- [ ] **Snowdon integration** — does not exist; treat as out of scope unless explicitly requested.

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
