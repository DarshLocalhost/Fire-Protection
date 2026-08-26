# FireProtectionSystem — Architecture Audit (AS-IS)

> **Deliverable A** of the production rewrite (master prompt §5 / §45 Step 9).
> Purpose: a forensic, source-grounded description of the system **as it actually is** — not as
> documentation claims. Every claim is tied to a `file:line` or a build/test artifact.
> Rule applied: *"Do not trust existing documentation until verified against source code."*
>
> Method: full read of the placement crux file, the calc engine, the extraction layer, the UI wiring,
> the `.csproj` targets, plus two independent verification passes (calc-layer isolation + boundaries /
> wiring / transforms). Findings below are the reconciled result.

Related: [[ARCHITECTURE]] (design description) · [[STANDARDS_COMPLIANCE_MATRIX]] · [[MIGRATION_PLAN]] ·
[[RISK_REGISTER]] · [[DECISIONS]] · [[PROGRESS]] · [[TODO]]

---

## 0. Baseline (master prompt §3)

| Item | Result | Evidence |
|---|---|---|
| Build Revit2024 (net48, x64) | 0 errors | prior session; re-confirmed this session — see [[PROGRESS]] |
| Build Revit2025 (net8.0-windows) | 0 errors, 828 warnings | `.analysis/baseline_build_revit2025.log` — all warnings are `MSB3277` (Microsoft.VisualBasic 10.0↔10.1, System.Drawing 4.0↔8.0) from multi-targeting net8 against Revit DLLs. **Environmental, not defects.** "2 Warning(s), 0 Error(s)", EXIT=0. |
| Build Revit2026 | 0 errors | prior session |
| Revit-free unit tests | 14/14 PASS | `.analysis/baseline_tests.log`, EXIT=0 |
| Revit runtime placement | **NOT run** | No live Revit host in this environment. All runtime acceptance criteria are marked **pending**, never fabricated (hard rule 18). |

The 828 `MSB3277` warnings pre-exist this work and are an environmental Revit-reference artifact. They must
**not** be relabeled as newly introduced defects (master prompt §3).

---

## 1. Layer map (as built)

```
FireProtection.UI        (WPF, Revit-FREE)      — Views, ViewModels, UI DTOs, service interfaces
        ▲
        │ ProjectReference (Backend → UI only)
        │
FireProtection.Backend   (Revit-heavy)          — extraction, calc engine, placement adapter, command
FireProtection.Tests     (Revit-FREE)           — logic-only harness (14 tests)
```

**Conceptual layering the master prompt §6 asks for is largely already present**, just not named the
same way:

| §6 layer | Where it lives today | Revit-dependent? |
|---|---|---|
| Extraction | `Backend/Services/Model/*`, `Backend/Services/Extraction/*` | Yes (correct) |
| Normalization | inside extractors via `RevitModelContext.Transform*` | Yes (correct) |
| Domain model | `Backend/Models/**` + `UI/Models/**` (POCO/Newtonsoft) | **No — verified clean** |
| Engineering/rules | `…/Final/BruteForce/DefaultHazardPlacementRules.cs` | No |
| Candidate calculation | `…/Final/BruteForce/BruteForceCalculationService.cs` | **No — verified clean** |
| Geometric validation | inside the calc service (inline filters) | No |
| Placement instruction | *implicit* — `BruteForceCalculationResult` is passed directly to placement | No (but not a dedicated `PlacementInstruction` type — see Gap 6) |
| Revit placement adapter | `…/Final/RevitSprinklerPlacementService.cs` | Yes (correct) |
| Post-placement validation | **minimal / absent** (see Gap 4) | — |
| Reporting/audit | `…/Final/PlacementInputJsonExporter.cs` + timestamped JSON | partial |

### 1.1 Isolation — VERIFIED COMPLIANT (hard rules 12, 13; §7, §30)

- **Calc engine is 100% Revit-free.** All calc files + consumed DTOs (`CeilingData`, `PlacementRoomInput`,
  `PlacementInputSnapshot`, `HazardClass`, `ObstacleData`, …) contain **no** `using Autodesk.Revit`; the
  only textual hits are XML doc-comments. Revit `ElementId`s are carried as `string`.
- **UI is 100% Revit-free.** Grep of `FireProtection.UI/**` for `Autodesk.Revit` returns only two
  doc-comment hits (`ISprinklerPlacementService.cs:8`, `SprinklerPlacementResult.cs:6`). Interfaces expose
  only primitives + UI DTOs.
- **Dependency direction correct.** `FireProtection.Backend.csproj` references `FireProtection.UI`; the UI
  project has **no** back-reference (`UI.csproj` deps = Newtonsoft.Json only).
- **Code-behind is thin.** All six `Views/**/*.xaml.cs` contain only `InitializeComponent()` / `DataContext`
  assignment. No business logic. (Placement orchestration + `MessageBox` live in the VM — acceptable, but
  the dialogs are a UI concern to relocate later.)

**Conclusion:** the master prompt's most structural mandates (Revit-free domain, one-way dependency, thin
code-behind) are **already satisfied**. The rewrite is therefore **targeted at the placement adapter and a
set of correctness defects**, not a from-scratch re-layering. This materially lowers risk and is the single
most important finding of this audit.

---

## 2. Sprinkler pipeline trace, end-to-end (§45 Step 4)

| Stage | Entry point | Notes |
|---|---|---|
| UI command | `Commands/FireProtectionCommand.Execute` | constructs `FireProtectionExtractionService`, `RevitSprinklerFamilySource`, `PlacementInputJsonExporter`, `RevitSprinklerPlacementService`; injects UI interfaces |
| Extraction | `Services/Extraction/FireProtectionExtractionService` → `Services/Model/*Extractor` | read-only, zero-transaction (Decision 005); links transformed once via `RevitModelContext` |
| Snapshot → UI | `ModelSnapshot` serialized to JSON → `UiLauncher.Show(json, …)` → `MainWindow` → `MainWindowViewModel` → `SprinklerViewModel` → `SprinklerBruteForceViewModel` | constructor injection throughout |
| Placement input | `PlacementInputBuilder.Build` (in-memory) → `IPlacementInputExporter.CalculateBruteForce` | |
| Calculation | `BruteForceCalculationService.Calculate` | 1 ft grid, greedy ~15 ft packing, Z from ceiling/level |
| Level resolution | `RevitSprinklerPlacementService.ResolveHostLevel` | host id → linked-level elevation/name mapping (Decision 007) |
| Family resolution | `RevitSprinklerPlacementService.ResolveSymbol` | by name over `OST_Sprinklers` (never hard-coded id) |
| Placement | `RevitSprinklerPlacementService.PlaceSinglePoint` | **contains the violation — §3 below** |
| Result | `SprinklerPlacementResult` (+ per-room, per-point) | requested vs actual XYZ recorded |
| JSON | `PlacementInputJsonExporter.ExportPlacementResult` → timestamped file | |
| UI | `SprinklerBruteForceViewModel.ExecutePlaceSprinklers` | drives `MessageBox`, re-exports |

---

## 3. Placement-overload inventory & the core violation (§45 Step 5)

`RevitSprinklerPlacementService.PlaceSinglePoint` selects a strategy from a **string** compare of
`symbol.Family.FamilyPlacementType` (proven, not name-inferred — good) and then uses one of three overloads:

| Branch | Condition | Overload | Verdict |
|---|---|---|---|
| Face host | face found (host or linked ceiling) | `NewFamilyInstance(Reference, XYZ, XYZ, FamilySymbol)` | **correct** |
| **WorkPlaneLevelFallback** | `WorkPlaneBased` **and no face found** | `NewFamilyInstance(XYZ, FamilySymbol, Level, NonStructural)` | ❌ **VIOLATION** — `RevitSprinklerPlacementService.cs:254-263` |
| LevelBased | `OneLevelBased`/unknown | `NewFamilyInstance(XYZ, FamilySymbol, Level, NonStructural)` | correct for genuinely level-based families |

**Why the fallback is a violation.** The runtime diagnostic (`SPRINKLER_ACTUAL_Z_DIAGNOSTIC.md`) proved
that the selected family `Sprinkler - Pendent - Hosted` reports `FamilyPlacementType = WorkPlaneBased`, and
that placing it through the **level** overload produced an instance at **(0,0,0)** — Candidate Z=12,
Actual Z=0, link transform identity. The fallback therefore:

- treats WorkPlaneBased as effectively level-based → **hard rule 6**;
- silently substitutes an incompatible placement strategy to increase the placed count →
  **hard rules 5, 19** and **§13/§15**;
- would report the (0,0,0) instance as a success because there is no post-placement spatial check →
  **hard rule 8** and **§23**.

This is the primary defect the rewrite must remove. The compliant replacement is an explicit
`IFamilyPlacementStrategy` that, for a WorkPlaneBased family with no face host, uses the **work-plane
(`SketchPlane`) overload honoring world XYZ** — or fails with a structured code — but **never** the level
overload. See [[DECISIONS]] Decision 011.

---

## 4. Coordinate-transform trace (§45 Step 6)

- `RevitModelContext.TransformPoint/TransformVector/TransformBoundingBox` guard on
  `transform == null || IsIdentity`; the bbox variant transforms **all 8 corners** and rebuilds min/max
  (correct — avoids the naive 2-corner AABB bug).
- Host extraction passes `Transform.Identity`; links pass `link.TotalTransform ?? link.Transform`.
- Each raw Revit value is transformed **exactly once** at extraction, then stored in host-space DTOs;
  downstream consumers do not re-transform. **No double-transform found** (§45 Step 6 clean; hard rules
  10, 11 held).
- At placement time, transforms act on **freshly fetched raw link data**, not on DTOs: linked-level
  elevation transformed once (`ResolveHostLevel`), and the host-space search point mapped into link space
  via `.Inverse` **only for the ceiling-face search** while the placement `xyz` is passed to
  `NewFamilyInstance` unchanged.
- **Caveat (Risk):** the single-transform invariant is enforced **by convention**, not by a type-level
  host-space wrapper. Nothing structurally prevents a future caller from re-transforming a DTO.

---

## 5. Ceiling discovery & face-reference creation (§45 Step 7)

- `FindCeilingHost` searches host ceilings first (level-filtered), then linked ceilings; for a linked
  ceiling it maps the host point into link space, finds a face, and returns a host-usable reference via
  `linkFaceRef.CreateLinkReference(linkInstance)` (instance method — the 2-arg static overload is absent in
  this API build).
- `FindHostFaceReference` prefers a **downward** face (`FaceNormal.Z < -0.5`, pendant underside) and falls
  back to `bestDownward ?? bestAny`.
- **Extraction-side association weakness (verified, HIGH):** `RoomExtractor.FindCeilingsForRoom` matches
  ceilings by XY-overlap within a Z band `[floorZ − 0.5, roomTopZ + 5.0]`, with a **20 ft** height fallback
  and **no level-id filter**; `matchedCeilings[0]` (collection order, *not* nearest) becomes the primary
  ceiling. Stacked rooms can therefore inherit a wrong-level slab's height/type. Compounded downstream by
  `BruteForceCalculationService` picking the FLAT ceiling with an unsorted `FirstOrDefault`.

---

## 6. Confirmed defects (reconciled from both verification passes)

| ID | Severity | Area | Defect | Anchor |
|---|---|---|---|---|
| **P-1** | **Critical** | placement | WorkPlaneBased→Level silent fallback → (0,0,0) risk; violates §13/§15/hard rules 5,6,8,19 | `RevitSprinklerPlacementService.cs:254-263` |
| **P-2** | High | placement | No post-placement spatial validation; a created-but-wrong instance reports success (§23) | `…PlacementService.cs:322-342` |
| **X-1** | High | extraction | Ceiling→room association matches wrong-level slab (no level filter, 20 ft fallback, first-not-nearest) | `RoomExtractor.FindCeilingsForRoom` |
| **C-A** | High | calc | FLAT-ceiling `FirstOrDefault` unsorted → arbitrary Z | `BruteForceCalculationService.cs:114-127` |
| **C-B** | High | calc | `MaxSpacing` used as **minimum** separation → coverage gaps (never verifies coverage) | `…Service.cs:295-305` |
| **C-E** | High | calc | Adaptive grid coarsening to 30 ft can exceed max spacing; candidate cap can leave high-Y region unsprinklered | `…Service.cs:402-422,179` |
| **C-G** | High | calc | `MissingCeiling`/`UnsupportedCeiling` clobbered to `ReviewRequired` by provisional flag → ceiling failures masked as success | `…Service.cs:149-157,327` |
| **C-C** | Medium | calc | Boundary clearance ignores inner loops (shafts/atria) | `RoomGeometry.cs:66-83` |
| **C-D** | Medium | calc | `RequiredCount` (πr²) inconsistent with placement (~15×15); `InsufficientCoverage`/`InvalidInput` are dead enum values | `…Service.cs:321-325` |
| **C-H** | Medium | calc | Obstacle rejection is XY-only, no Z/height gating → over-rejection under low furniture | `…Service.cs:434-487` |
| **I-1** | Medium | placement | No true idempotency: re-runs rely on a 0.25 ft proximity check; instances not tagged with a generation id (§27) | `…PlacementService.cs:192`, config `:779` |
| **A-1** | Low | audit | `[DIAGNOSTIC]`/`[PLACED]` strings pushed into `result.Warnings` — debug noise in the audit payload (§28) | `…PlacementService.cs:95,345` |
| **C-F** | Low | calc | Calc layer does not guard NaN/finite input coords → 0 candidates misclassified `NoValidCandidates` | `…Service.cs` |

Design note: `RoomCalculationResult.IsSuccessful` treats `ReviewRequired` as success, and
`HasApprovedRules` is hard-wired `false` with a single 15 ft placeholder for **all** hazard classes — so
today every run is "provisional-but-successful," and C-G means no ceiling defect fails a room until real
approved rules are injected. The provisional spacing itself is **intentional** (Decision 004; hard rule 4 —
values must not be invented), but the masking (C-G) and the min-separation logic (C-B) are genuine bugs.

---

## 7. What is compliant today (do not "fix" these)

- Revit-free calc engine and UI; one-way Backend→UI dependency; thin code-behind (§6/§7/§30, hard rules 12/13).
- Single-transform normalization with correct 8-corner bbox mapping (hard rules 10, 11).
- Family symbol resolved by name, never hard-coded id.
- `FamilyPlacementType` proven from `symbol.Family`, not inferred from the family name (hard rule addressed).
- Requested vs actual XYZ already recorded separately (§25) — honest; **keep**.
- Provisional spacing openly flagged `IsProvisional`/`ReviewRequired` (§18; Decision 004).
- Read-only, zero-transaction extraction (Decision 005).

---

## 8. Audit conclusions → rewrite scope

1. **Phase 5 (placement strategies)** is the critical path: replace P-1 with `IFamilyPlacementStrategy`,
   add P-2 post-placement validation, structured status/error codes (§26). **This session.**
2. Calc defects (C-*) and the extraction association weakness (X-1) are **real but out of the Phase-5
   blast radius**; they are staged as later, test-backed phases (no god-level fix — §39). C-B and the
   spacing model interact with engineering values and must not be "fixed" by inventing NFPA numbers
   (hard rule 4) — they need an approved rule set (FPE).
3. Idempotency (I-1) and audit hygiene (A-1) are Phase 8.
4. All runtime-only acceptance (actual XYZ ≈ requested; visibility) remains **pending** — cannot be
   executed here (hard rule 18).
