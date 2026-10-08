# FireProtectionSystem — Progress

## Catalog source switch: Model vs Catalog file (2026-10-07)

Static only. The Revit-document reading half is **UNVERIFIED at runtime**.

### What the user gets

A **SOURCE** radio group (Model | Catalog file) in the top bar, replacing the browse-only Catalog bar.
One group covers all three device tabs because the bar is hosted once at window level, outside the
`TabControl`.

| Mode | Sprinkler lists | Smoke / NA lists | Browse… | Reload |
|---|---|---|---|---|
| **Model** (default, new) | from `OST_Sprinklers` | from `OST_FireAlarmDevices`, **identical on both tabs** | hidden | re-reads the document |
| Catalog file (existing) | from workbook | from workbook, per sheet | shown | re-reads the workbook |

A single `InfoBanner` under the bar names the active source and states whether per-type values are
provisional.

### The design problem this had to solve

The two sources are **complementary, not interchangeable**:

- **Model** → what is actually loadable, plus `FamilyPlacementType` (authoritative for hosting).
  Carries **no** engineering values.
- **Workbook** → every per-type engineering value (hazard class, coverage area, spacing, coverage
  radius, K-factor, temperature, deflector, sidewall values, and the alarm attributes). Carries
  **no** placement-type information.

A naive switch was unsafe both ways. Blanking the workbook made `GetSprinklerEntry` return null, which
**silently** zeroed `TypeMaxSpacingFt` / `TypeMaxCoverageAreaSqFt` / `TypeMinSpacingFt` /
`TypeCoverageRadiusFt` / `SprinklerClass` on every room, and the engine then quietly used provisional
15 ft spacing with nothing shown to the user. Discarding the workbook lost the engineering data
entirely.

**Resolution:** `ModelBackedCatalog` takes **NAMES from the model** and **VALUES from the workbook**,
the workbook acting as a live overlay. `ISprinklerFamilySource` keeps only a last-resort fallback role.

### Files

- `Backend/Services/Catalog/ModelBackedCatalog.cs` — `ICatalog`: model lists + workbook overlay
- `Backend/Services/Catalog/ModelFamilyEnumerator.cs` — reads loaded `FamilySymbol`s per category
- `Backend/Services/Catalog/ModelFamilyType.cs` — Revit-free record (own file, deliberately)
- `Backend/Services/Catalog/DeviceCatalogOverlay.cs` — **cross-sheet** alarm lookups
- `UI/Services/CatalogSourceMode.cs`, `UI/Services/CatalogSettingsStore.cs`
- `UI/Converters/EnumToBoolConverter.cs`, `UI/Themes/Styles.xaml` (first RadioButton style)
- `UI/Views/Catalog/CatalogBar.xaml(.cs)`, `UI/Views/MainWindow.xaml`, `CatalogViewModel.cs`
- `Backend/Commands/FireProtectionCommand.cs` — two-holder wiring

### Decision 2 — shared device list, and the trap it created

Per the product decision, the Smoke Detectors and Notification Appliances tabs show the **same**
device list (both come from the single Revit category `OST_FireAlarmDevices`).

That exposed a real gap: the workbook stores alarms in **two separate sheets**, and every sheet-scoped
lookup searched only its own. A strobe catalogued under `NotificationAppliances` would appear on the
Smoke tab with **no** attributes, blanking the derived read-only fields and re-exposing **empty
editable pickers** — worse than today's read-only text. `DeviceCatalogOverlay` now searches **both**
sheets for every alarm-device lookup, keeping the shared list coherent. Attributes the other sheet
lacks are returned as `null` / `0` rather than guessed (`0` already means "no rating known"
downstream). This is the single most important behaviour to preserve here.

### Defects found and fixed along the way

- **Two holders, not one.** The model catalog must never read the shared `CatalogHolder`: in model
  mode it would find *itself* as its own overlay and recurse indefinitely
  (`GetSprinklerEntry` → `Overlay` → itself). A dedicated `workbookHolder` holds **only** the
  workbook, making that impossible by construction rather than by a fragile runtime guard.
- **`FireProtectionConfig.UseRevitFamilyListing` removed.** That gate is why the model listing looked
  dead. Two independent switches for one concept is how the earlier catalog/model inconsistency
  arose, so the flag was retired rather than kept alongside the radio.
- **Test-harness boundary.** `ModelBackedCatalog` contains **no** `Autodesk.Revit.DB` reference so the
  Revit-free harness can exercise it; the document read lives in `ModelFamilyEnumerator.LoadInto`.
- Entering Catalog-file mode with no remembered workbook now **clears** the catalog rather than
  leaving the previous source in place, which would have shown model families while the radio claimed
  "Catalog file".

### Verification

New suite `CatalogSourceModeTests` (12 tests / 53 checks): both-sheet lookups in both directions,
overlay precedence, shared-list equality, no-overlay behaviour, settings round-trip, corrupt-settings
fallback, mode switching, and the provisional-values banner.

**388 checks, 17 suites, green on Revit2024 / 2025 / 2026; 0 build errors on all three.**

## Sprinkler Obstacle Avoidance Recovery & Nearest-Point Relocation (2026-10-07)

**Industry-grade placement logic enhancement:** Fixed silent dropping of sprinkler heads when ideal grid cell targets hit physical obstructions or structural members, ensuring sprinklers snap to the nearest valid candidate position up to `MaxSpacing / 2.0` (the mathematical maximum preserving NFPA 13 spacing limits).

### Fixed Issues

1. **`IsPlacementValid` Alignment with `TryAcceptPoint`**:
   - `IsPlacementValid` previously lacked the **Three Times Rule** (NFPA 13 Ch.20), **Beam Rule** footprint rejection, and the **4 in wall clearance floor**.
   - As a result, grid-ideal positions directly adjacent to columns or beams passed `IsPlacementValid` without any snap, violating NFPA 13 clearance.
   - `IsPlacementValid` now evaluates the complete set of structural member rules and wall clearance constraints.

2. **Free-Grid Obstacle-Recovery Expanded Search**:
   - When a physical obstacle (column, beam, duct, wall) blocks an ideal grid target and the initial tight `captureRadius` snap finds no candidate, the algorithm now expands its recovery search up to `spacing / 2.0`.
   - `spacing / 2.0` is the theoretical maximum displacement that maintains NFPA 13 `MaxSpacingFt` compliance (moving a head by <= S/2 keeps its gap to the adjacent array position <= 1.5S, which is then flagged by `FinalizeSelection`'s nearest-neighbour check).
   - If an existing sprinkler occupies a target grid cell, recovery snap is bypassed for that cell (since the existing sprinkler already satisfies the grid position).

3. **Tile-Grid Obstacle-Recovery Expanded Search**:
   - For suspended tile ceilings, when an 8-immediate-neighbour snap budget fails to find a clear tile, the search expands up to `spacing / 2.0` across the tile grid (`extRange = max(tileStepU, tileStepV) + 1` tiles).
   - `placedObstacleRecovered` is tracked and reported separately in diagnostic outputs to distinguish 8-neighbour tile snaps from expanded recovery snaps.

### Verification

- Build clean on **Revit2026 / Revit2025 / Revit2024** (0 errors).
- All **16 test suites** (over 335 test checks) pass green.

## Rulebook-driven sprinkler rules (2026-10-07)

**Design basis decision (from the project owner):** the two supplied rulebooks are the declared design
basis for this tool, and the tool is to be developed against them. They are old editions (NFPA 13 2002
via the NFSA textbook; NFPA 72 2019 for detectors/appliances) and that is accepted — but every rule must
be traceable to them, and nothing may be invented.

Scope of this pass: **sprinklers only.**

### Where the rules now live

`FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/Nfpa13RulebookRules.cs` — every
value transcribed from the rulebook, each carrying the chapter, page and a verbatim quote. The calc
engine references these constants; it holds no bare engineering numbers of its own.

### Rules implemented

| Rule | Rulebook source | Notes |
|---|---|---|
| Min spacing 6 ft (cold soldering) | Ch.19 p.218 | already correct; now sourced to the constant |
| Max spacing 15 / 12 ft | Ch.19 p.218 | already correct |
| Max wall distance = ½ S | Ch.19 p.219 | already correct |
| **Min wall distance 4 in** | Ch.19 p.220 | **new.** Enforced as a floor — a room or rule set can never go below it |
| **Non-90° corner limit = 0.75 S** | Ch.19 p.220 | value recorded; corner detection is polygon-shape dependent and still advisory |
| **Deflector drop 1–12 in unobstructed** | Ch.19 p.225 | **new and previously a real defect.** Heads sat exactly ON the ceiling plane (zero drop), violating the 1 in minimum. Now applied |
| **Deflector drop 1–6 in obstructed** | Ch.19 p.225-226 | **new.** Band selected by construction class |
| **Unobstructed / obstructed classification** | NFPA 13 3.7.2 via Ch.19 p.216 | **new.** Members > 7.5 ft o.c. = unobstructed regardless of solidity; < 3 ft = necessarily obstructed; between 3 and 7.5 ft falls to the **conservative OBSTRUCTED** branch because the >70 % open-cross-section test cannot be measured from extracted AABBs |
| **Sloped peak rule (3 ft)** | Ch.19 p.229 | **new.** Highest head must be within 3 ft of the peak; flagged when not |
| **S × L coverage rule** | Ch.19 p.222 | **new.** Per-head coverage is now `S × L` where each dimension is the greater of the neighbour distance and **twice** the wall distance. Replaced a room-level `area / headCount` estimate that could not see a head overloaded next to a wall |
| **Small Room Rule** | Ch.19 p.223-224 | **new.** Light hazard + < 800 ft² + unobstructed + enclosed → wall limit relaxed to 9 ft **and** coverage switches to the averaging technique the rulebook requires. Never loosens a limit already tighter than S/2 |
| **Ch.20 zone plane (18 in)** | Ch.20 p.246 | **new.** Splits the room into discharge-development and below-sprinkler zones |
| **"Three Times" rule** | Ch.20 p.248 | **new.** ≥ 3 × the obstruction's **maximum** dimension from its near edge, capped at 24 in, measured to the deflector centreline. Includes the opposing-sprinkler exception (within 0.5 S of the centreline) |
| **Permanent fixture > 4 ft wide** | Ch.20 p.249 | **new.** Reported for obstructions below the 18 in plane; the tool cannot place a head *under* a fixture, so this is raised for manual layout |

### Deliberately NOT implemented — and why

`Nfpa13RulebookRules.BeamRuleTableAvailable = false` plus `Nfpa13RulebookRules.KnownGaps` record these
so they cannot quietly become invented numbers later:

- **Beam rule (NFPA 13 Table 8.6.5.1.2).** The rulebook *references* this table but does not reproduce
  it — Ch.20 p.246 only says "See Table 8.6.5.1.2 and Figure 8.6.5.1.2(a) of NFPA 13 for more
  information on exactly how far away sprinklers need to be". The clear distance depends on the distance
  from the obstruction bottom to the deflector, and those numbers are simply absent from the design
  basis. **No values were invented.** Heads are rejected only when inside a member's own footprint, and
  such rooms are flagged.
- Sprinklers in every beam pocket (Ch.19 p.226) and the concrete-tee exception — need per-member pocket
  geometry the AABB model cannot represent.
- Composite wood joist firestopping (Ch.19 p.228) and concealed-space/attic rules (Ch.19 p.230) —
  extraction does not identify these assemblies.
- High-piled storage and rack storage commodity tables (Ch.37, Ch.38).
- Extended-coverage and ESFR geometry (Ch.21) — ESFR remains a review-only flag.
- Density/area hydraulics — out of scope for a placement tool.

### Defects found and fixed along the way

- **Zero deflector drop.** Heads were placed exactly on the mounting plane. Ch.19 requires 1–12 in.
  This changed every room's Z, so four existing tests asserting "Z == ceiling elevation" were
  **correctly** failing afterwards and were updated to assert ceiling-minus-one-drop.
- **The Small Room Rule initially loosened caller-supplied limits.** Relaxing a wall limit the rule set
  had deliberately set *tighter* than S/2 is wrong; caught by an existing test that pinned a 1.0 ft
  limit. Now the relaxation only applies when the caller is actually relying on the standard S/2 limit.
- **Per-category obstacle clearances never matched.** `GetObstacleClearance` was keyed on
  `"beam"/"column"/"duct"` while the extractor emits Revit categories
  (`OST_StructuralFraming`, `OST_StructuralColumns`, `OST_DuctCurves`, …), so every lookup silently
  fell through to the flat default. Structural members now use the Chapter 20 dimension rule instead of
  a fabricated flat clearance.

### Verification

New suite `Nfpa13RulebookRulesTests` (16 tests / 59 checks) covering every rule above, including an
explicit test that the Beam rule table remains declared unavailable.

**335 checks, 16 suites, all green on Revit2024 / 2025 / 2026; 0 build errors on all three.**

All static. Runtime Revit placement remains **UNVERIFIED**.

## Sprinkler point-identification corrections + test safety net (2026-10-07)

Static only — **runtime Revit placement remains UNVERIFIED**. See "Phase 3 — live Revit verification" below.

### Test safety net (was silently broken)

- `FireProtection.Tests` is now in `FireProtectionSystem.slnx`. Previously the solution contained
  only Backend + UI, so a plain `dotnet build` compiled **no tests at all** and nothing gated a change.
- `FireProtection.Tests.csproj` gained the same `Configurations`/per-config TFM matrix as Backend and
  UI (Revit2024 → `net48`, Revit2025/2026 → `net8.0-windows`), because it references Backend whose TFM
  follows the Revit configuration.
- **Pre-existing solution bug fixed:** `.slnx` mapped `Revit2024|Any CPU` → `Debug` for the UI project,
  rebuilding UI as `net8.0-windows` while Backend built `net48` in the same build. That combination is
  an unsatisfiable project reference, so `dotnet build -c Revit2024` failed outright on the solution
  while each project built correctly on its own. The obsolete mapping was removed.
- Three complete suites existed but were **never registered** in `Program.RunAll()` and so never ran
  while the harness printed `ALL TESTS PASSED`: `HazardRuleValuesTests` (the entire hazard-spacing
  regression lock), `SidewallDirectionalSolverTests` (7 tests, NFPA 13 §11.3 directional sidewall
  solver), `PerTypeCatalogMergeTests` (5 tests, catalog-override merge + ESFR/CMSA review flag). All
  three are now registered and pass.
- `CatalogLoaderTests` was invoked directly rather than through `RunGuarded`; it signals failure by
  throwing, so one failure aborted the whole run. Now guarded like every other suite.
- New `Program.ReportSuiteCoverage()` discovers every `*Tests.cs` declaring `RunAll` and **fails loud**
  if any is not in `_registeredSuites`. Verified negatively: removing a registration makes the run
  exit non-zero with `MISSING : <name>`.
- Fixed 2 genuinely failing tests (`Program.cs` unsupported-behaviour and hosted-family/no-ceiling).
  Root cause: the engine had moved blocking messages from `Diagnostics` to `Errors`; the tests still
  scanned `Diagnostics`. Tests now assert the real contract via `HasMessageContaining`, which searches
  every user-facing channel — moving a message between channels is presentation, not behaviour.
- Baseline: **276 checks, 15 suites, all green on Revit2024/2025/2026; 0 build errors on all three.**

### Point-identification corrections (sprinkler calc engine)

- **Bounded tile snapping.** `SelectCenteredGridOnTiles` searched ±`tileStep` tiles in *both* axes with
  **no distance limit** and took the nearest valid tile, so a head whose array target was blocked could
  be dragged arbitrarily far, producing a pair beyond `MaxSpacingFt`. The placer was relying on the
  post-hoc pairwise checker to report the placer's own violation. The search is now limited to the 8
  immediate neighbours AND bounded by the slack between the array pitch and the permitted spacing
  (capped at half a tile). A cell with no in-budget neighbour is **skipped and reported**, never dragged.
- **Array extent.** `tileStep` is a `Floor` of spacing/pitch, so a centred array left the room ends
  uncovered whenever `(tiles-1)` was not a multiple of the step (4 ft tile + 15 ft spacing → 12 ft step).
  The uncovered tail is what made the unbounded search fire in practice. The array now covers the
  extent and appends the far-edge index when the stride cannot reach it.
- **Coverage radius corrected.** `CoverageRadiusFt` was `MaxSpacingFt / 2` for every hazard class, which
  is valid only along an array EDGE. The true worst case for an array is `S / sqrt(2)` ≈ `0.707 × S`
  at a corner/array centre. Added `HazardPlacementRuleSet.EffectiveCoverageRadiusFt` (= `S/sqrt2`) and
  `RemoteDistanceLimitFt` (= `0.7 S`). `CoverageRadiusFt` is retained as the head's *listing* radius;
  coverage *radius* and coverage *area* (`MaxCoverageAreaSqFt`) are now separate quantities.
- **Coverage is advisory only** (product decision). The previous `if (pct > 5.0)` escalation is removed —
  it reported a gap with no location and no action, training users to ignore it. Now: one concise warning
  with the worst-gap point, plus a diagnostic carrying the 0.7 S limit and the listing radius. It never
  changes room status. Sidewall rooms skip the 2D sampler entirely (a half-disc spray cannot satisfy a
  full-room radius test) with an explicit "skipped" diagnostic rather than a false gap.
- **Max-spacing check was a false positive on virtually every room.** `FinalizeSelection` compared
  **every pair** of heads against `MaxSpacingFt`. Diagonally adjacent heads in an array are always
  further apart than the pitch (15 × 15 ft array → 21.2 ft diagonal against a 15 ft limit), so any room
  with a 2×2-or-larger array emitted a spurious "exceeding MaxSpacingFt" warning and was flagged
  `ReviewRequired` regardless of quality. Replaced with a per-head **nearest-neighbour** test, which is
  what max spacing actually governs and which still catches a dragged or orphaned head.
- **Single owner for grid maths.** `SelectCenteredGridOnTiles` carried local `WorldToIndex` /
  `IndexToWorldX/Y` duplicating `CeilingGridMath.SnapToTileCenter` — they had already drifted once (the
  `// Re-added +0.5 shift` comment). Deleted the duplicates; `CeilingGridMath` now exposes
  `WorldToIndex`, `IndexToWorld`, `IndexKey`, `IndexDistance`.
- **Trap avoided and documented:** caching `cos/sin` in the `CeilingGrid` struct created an invalid
  state — the struct has public settable fields, so a struct initializer left the cached rotation at
  (0,0) and produced a snapped point on a tile *corner*. Caught by the existing `CeilingGridSnapTests`.
  `AngleRad` is now the single source of truth and cannot go stale.
- **Split skip counters.** "too close to another new head" and "too close to a pre-existing sprinkler"
  were merged into one `skippedMin` bucket, telling the user nothing actionable. Now reported
  separately as `noValidNeighbour` / `minSpacing` / `existingSprinkler`.
- **Mounting-plane (Z) selection.** `SelectPrimaryCeiling`'s fallback ranked by **highest** bottom
  elevation, so in a stacked building a room with no level-tagged ceiling selected the ceiling of the
  level **above** — an ~12 ft Z error across every head in the room. Now ranks: own level first, then
  FLAT before SLOPED/STEPPED, then **closest to the room's nominal ceiling height**. Candidates more
  than 5 ft above nominal are excluded as soffit/plenum/slab, and a non-level-matched or
  height-mismatched pick raises an explicit warning instead of silently substituting.
- Dead code removed: `CoveragePatternAdjustments` + `GetCoveragePatternAdjustment` had **zero callers**
  repo-wide — an unvalidated-looking table a future reader could mistake for an implemented rule.
  `MinKFactor` is documented as **not enforced** (it is a hydraulic-stage property; this tool performs
  no hydraulics) rather than being silently present.
- New regression tests: unobstructed grid never exceeds `MaxSpacingFt`; obstructed room serves both
  sides and **reports** the resulting gap rather than hiding it; blocked target with no near neighbour
  is skipped, not dragged; array reaches room extent with a coarse 4 ft tile; level-above slab is not
  used as the mounting plane; own-level ceiling preferred; out-of-band plenum deck rejected + reported.

### Not done in this pass (deliberate)

- **Spatial index for candidate search (P1F).** The free-grid nearest-candidate search remains
  O(cells × candidates). Whole suite runs in 2.6 s, so there is no evidence of a problem at test scale,
  and the fix cannot be validated without a real large linked model. Deliberately deferred rather than
  optimised blind.
- `FireProtection.CatalogStandalone` still duplicates 26 sources via `<Compile Include>` and is not in
  the solution.

### Phase 3 — live Revit verification (OUTSTANDING, requires the user)

Nothing below is proven. The historical (0,0,0) origin-snap defect has never been re-verified.

1. Open a host model + architectural link with a real ceiling family.
2. Place into 2–3 rooms, including one with beams/ducts and one with a suspended tile ceiling.
3. Confirm placed `instance.Location` ≈ calculated X/Y/Z for every head; **none at (0,0,0)**.
4. Confirm Z is on the intended ceiling (not the slab above).
5. Read the run report: reason codes, coverage advisory, bounded-snap skip counts.
6. Re-run to confirm the duplicate guard behaves; remove any stray misplaced instances from prior runs.

## ELI5 location-point explainer (sim_expl_2.md) — written (2026-09-22)

- Documentation only (no code change): `sim_expl_2.md` in the project root is a plain-language
  (ELI5) companion covering location-point logic only for sprinklers, smoke detectors, and
  notification appliances — rules → grid → filters → selection → review flags — plus a side-by-side
  comparison table.
- Companion to the more technical `explanation_2.md`; both flag provisional rules
  (`HasApprovedRules=false`). See [[SESSION_NOTES]] (2026-09-22).

## Candidate-location explainer (explanation_2.md) — written (2026-09-22)

- Documentation only (no code change): `explanation_2.md` in the project root documents candidate-point
  identification for sprinklers, smoke detectors, and notification appliances (plain-English then technical:
  rules, ceiling/Z, generation, filters, selection, post-checks, family/type coverage, edge cases, class &
  method index).
- Flags provisional rule sets (`HasApprovedRules=false`), static-only status, and 12 known doc-vs-code
  discrepancies (code is source of truth). See `explanation_2.md` §9 and [[SESSION_NOTES]] (2026-09-22).

## Catalog-driven sprinkler rules — implemented, static verification (2026-09-21)

- Implemented: optional per-type sprinkler catalog fields, warning-only validation, catalog accessors,
  template examples, a read-only selected-type card, per-room orientation, and ceiling-type display.
- Implemented: listed type values travel through the UI/export contract to the Revit-free engine,
  merge before per-room overrides, and remain bounded by the hazard-class spacing ceiling.
- Implemented: ESFR/CMSA and unlisted storage coverage are ReviewRequired; no structural rule,
  K-factor, pressure, or minimum-head requirement is fabricated.
- Static verification: Revit2026 test-project build completed with 0 errors and the console harness
  reported `ALL TESTS PASSED`. Runtime Revit verification remains outstanding.

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
| Extraction / placement logic | ✅ Implemented | static | Device-specific smoke-detector coverage-grid logic and rule set are now separated from sprinkler logic; live Revit verification remains pending because the backend cannot build without Revit assemblies. |

## Notification Appliance Workflow

| Capability | Status | Verification | Notes |
|---|---|---|---|
| UI tab (Notification Appliances) | ✅ Implemented (shared base) | static | `NotificationApplianceViewModel` inherits `DevicePlacementViewModelBase`; hosts `DevicePlacementView` + NFPA-72 params (appliance type / candela / dBA). Placement disabled (backend deferred). |
| Extraction / placement logic | ✅ Implemented | static | Notification-appliance logic now carries its own candidate-generation and spacing decision path, using visible/audible coverage selection rather than the sprinkler-style generic path; live Revit verification is still blocked by missing Autodesk DLLs. |

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
- **Device-specific location-point split (2026-09-10)** — Added a dedicated location-point identifier layer
  that resolves distinct strategies for sprinklers, smoke detectors, and notification appliances before
  candidate generation. The project now encodes explicit device-family logic:
  `sprinkler-ceiling-grid`, `sprinkler-sidewall-edge-grid`, `smoke-detector-ceiling-grid`,
  `smoke-detector-wall-mounted`, `notification-appliance-ceiling-grid`, and
  `notification-appliance-wall-mounted`. This is a source-level fix only; runtime verification remains
  blocked because the backend cannot compile without the Autodesk Revit DLLs in the current environment.
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
- New runtime-verification items from Decision 021: (a) smoke run then NA run over the same rooms with
  policy Skip — neither may skip or delete the other's devices; (b) derived detector/appliance attributes
  follow per-row family changes into the calculation; (c) the sprinkler report window renders themed and
  truthful on a live run.
- Next verification gap after that: **NFPA-compliant spacing** (replacing provisional values — an
  uncommitted "approved" flip in `DefaultHazardPlacementRules` was reverted 2026-09-11: still needs real
  FPE sign-off, the values themselves are plausible NFPA-13 hazard tables but unverified here).
- Production task list has been finalized in [TODO.md](TODO.md): the repository now tracks the required device-specific rule separation, engineering-grade reporting, UI success/failure summaries, and runtime validation gates.
- Build validation note (2026-09-11): the earlier "environment-blocked CS0246" entry is **stale** — the
  solution now builds 0 errors in Revit2025 and Revit2026 (Revit 2026 assemblies present on this machine),
  and the full console test harness passes headless. Only *runtime* Revit verification stays blocked.

## Completed Milestones

- **Family import classification correction (static 2026-10-07).** `FamilyLoadHelper` no longer labels
  every no-count-change load as "already present." It opens the selected RFA as a family document in
  the running Revit application, reads its internal family name, imports that document with load
  conflict options, and reports already-present only when that actual family name exists. This lets
  Revit's current-version family-document open/import path upgrade supported older families. Standard
  pendent/upright point selection was reviewed against the project NFSA/NFPA 13 rule implementation:
  centered max-spacing layout, clearance/obstacle screening, and per-head S x L checks are present;
  no change made to it or to sidewall/wall-based placement. Revit-free suite passes; live RFA upgrade
  behavior still needs Revit runtime verification.

- **Linked-model and family-load reliability (static 2026-10-07).** Link discovery now traverses loaded
  nested Revit links and composes each instance transform into host coordinates while preserving
  nested-link metadata. The top-bar family loader now reports after the Revit API callback, the shared
  loader classifies loads by model family-count changes instead of assuming the file name equals the
  internal family name, and sprinkler-family enumeration returns the actual Revit symbols when enabled.
  Revit2024/2025/2026 builds succeed. Live validation with representative linked models and RFA files is
  still required; no such project model was available during this change.

- **Device pipeline hardening + sprinkler run report (Decision 021, static 2026-09-11).** Kind-scoped
  existing-device policy (fixes cross-device skip/fail), catalog-derived read-only device attributes
  replacing the smoke/NA dropdowns, the BruteForce tab now opens the shared `PlacementResultReportWindow`
  (via `SprinklerPlacementReportMapper`), calc-review + cancelled-run honesty in the device core,
  outside-room guard ported to devices, C-E grid-truncation warning in the sprinkler calc, sprinkler
  S→S/Wall columns showing real applied values with bounded editability, reverted `HasApprovedRules`
  flip. BUILD 0 errors (R2025+R2026), tests all pass incl. `DeviceReportAndKindTests`. RUNTIME-UNVERIFIED.
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

## Catalog Source Discipline Fixes (2026-10-07)

### Implemented
- **Host stability.** Added `FireProtection.UI/Services/WpfHost.cs` (creates + statically pins a WPF
  `Application` with `ShutdownMode.OnExplicitShutdown`; exposes `UiDispatcher` and `RunOnUi`). Called
  from `UiLauncher.Show` before the first `Window` is constructed. Removed the
  `Dispatcher.CurrentDispatcher` fallback from `Dialogs.PumpUi`; replaced the two
  `Application.Current.Dispatcher` dereferences in `MainWindowViewModel.LoadFamilies` (which would
  have thrown `NullReferenceException`) with `WpfHost.RunOnUi`.
- **Crash diagnostics.** `FireProtectionApplication.InstallCrashDiagnostics()` logs unhandled
  exceptions via `AppDomain.CurrentDomain.UnhandledException`; `WpfHost` attaches
  `Dispatcher.UnhandledException`. This is how the real fault behind the `SEHException` was found.
- **Fixed the launch crash.** `SourceMode` (private setter) was bound with `Mode=TwoWay`, throwing
  inside `Window.Show()` and aborting the command; Revit then crashed in its own finalizer. Both radio
  bindings are now `Mode=OneWay`; the `Checked` handler drives `TrySetSourceMode`.
- **Browse button.** `CatalogBar.xaml` used `InverseBoolToVis` on `IsCatalogFileMode`, so Browse showed
  in Model mode and hid in Catalog-file mode. Now `BoolToVis`.
- **Source discipline.** `CanFallBackToModelFamilies()` on both
  `SprinklerBruteForceViewModel` and `DevicePlacementViewModelBase` prevents the silent import of
  every Revit family when the active catalog is empty in Catalog-file mode.
- **Status messages.** Mode-aware `CatalogStatusMessage` on all three tabs, distinguishing no catalog /
  empty sheet / no families in the model. Added `CatalogSheetName` overrides naming the exact sheet.
  New `StringToVisibilityConverter` and `WarningText` style for collapsed explanatory text.
- **Missing-data warnings.** `MissingCatalogDataMessage` on all three tabs; null when the catalog
  covers the selected type.
- **Load-family wiring.** The sprinkler tab now passes a `TryLoadFamily` callback to
  `MissingFamiliesModal`; previously the button was inert. Both tabs report
  `PlacementEligibilityStatusCodes.FamilyNotLoaded`. A per-row family/type change now re-runs the
  eligibility preflight on the sprinkler tab. Placement-skip warnings name the remedy.
- **Notification contract.** All three tab ViewModels react to the same catalog property set,
  including `IsCatalogFileMode`.
- **Documentation.** Corrected the `FamilyAvailability` claim in `SESSION_NOTES.md`; no such member
  exists, and load state is reported through eligibility.

### Not done / open
- Nullable Candela/dBA readers were considered and rejected; see [[DECISIONS]].
- Hazard-class dropdown remains hardcoded (`LIGHT`, `OH1`, `OH2`, `EH1`, `EH2`) by explicit user
  choice. `ICatalog.GetHazardClassesForSprinklerFamily` / `GetHazardClassForSprinkler` remain
  implemented-but-unused.
- Several parsed workbook columns are still read by nothing outside the four type values the engine
  merges: `TempRatingF`, `ResponseType`, `DeflectorToCeilingIn`, `SidewallEndWallClearanceFt`,
  `CoverageRadiusFt`, `MinSpacingFt`.

### Verification status
Static only. Builds clean on Revit2024/2025/2026; 17 test suites pass. **The crash fix and all loading
behaviour are unverified in a live Revit session** and remain P0 to confirm.
## UI Freeze Work, Tier 1 (2026-10-07)

### Implemented
- **`CeilingHostResolver`** now caches the Ceiling/Floor/RoofBase and `RevitLinkInstance` element lists
  per `Document`, bracketed by `BeginPass()`/`EndPass()`. Previously these were rebuilt for EVERY
  candidate point via `4 + 3L` whole-document collectors - the single largest source of the freeze.
  Exposed as `ISprinklerPlacementService.BeginHostResolutionPass` / `EndHostResolutionPass` /
  `HostCollectorCallsSaved`, and bracketed around both the eligibility sweep and the placement run.
  Geometry is still computed per candidate; only the collection is cached.
- **Eligibility refresh is coalesced** in `SprinklerBruteForceViewModel` and
  `DevicePlacementViewModelBase`: a pending flag plus an input version bumped at the decline point, so a
  burst of triggers runs one sweep plus at most one follow-up. Previously every trigger queued a full
  sweep, because `RevitApiContext.Run` enqueues without de-duplicating.
- **Catalog-change handlers are idempotent** in both tab VMs. One mode switch used to queue ~7 sweeps
  (8 `SetCatalog` notifications + `SourceMode`, of which the wide filter matched 6); now 1. This
  corrects a regression introduced in the previous session's "unify the notification contract" change.
- **Eligibility cache no longer cleared unconditionally.** The key already covers room, level name,
  hazard, family and type. Invalidation now happens after a successful `TryLoadFamily`
  (`MissingFamiliesModal` callback and `MainWindowViewModel.LoadFamiliesCore`).
- **Per-sweep instrumentation**: `FireProtectionLog.Info` records elapsed ms, room count, selected
  family and collectors avoided. Read it at `%APPDATA%\FireProtection\logs\`.
- **New suite `EligibilityCoalescingTests`** (6 tests) pinning: 8 triggers -> 1 sweep, a coalesced
  trigger -> exactly one follow-up, a failure does not latch the coalescer, host passes stay balanced
  even when a sweep throws, one mode switch -> 1 sweep.
- `FakePlacementService` updated for the new interface members.

### Open / not done
See [[DECISIONS]] for the Tier 2+ backlog: per-keystroke filters and counts (250 ms debounce approved
but not implemented), `FilteredElementCollector` on the WPF thread from `CatalogBar` (API violation),
modal dialogs blocking Revit's main thread, and O(n^2) backend extraction.

### Verification status
Builds clean on Revit2024/2025/2026. 18 test suites pass. **Not runtime-verified** - the measured
speed-up is unknown until the tool runs in Revit and the new log line is read.
## Tile-Centric Ceiling Placement (2026-10-08)

### Implemented
- **New grid source (Revit 2025.3+).** `CeilingExtractor.TryReadGridFromCeilingGridLines` calls
  `Ceiling.GetCeilingGridLines` through a cached `MethodInfo` so one codebase compiles for 2024/2025/2026.
  Groups lines into two direction families, requires near-perpendicularity, takes the module as the
  median gap between offsets, and sets the origin to the intersection of two real lines -> EXACT phase.
  Exposed as `IsCeilingGridLinesApiAvailable`.
- **Phase is now reported, not assumed.** `CeilingData.GridOriginSource` +
  `GridOriginSources` + `HasExactGridPhase`; mirrored onto `CeilingGrid.PhaseIsExact` /
  `PhaseSource`; `CeilingGridMath.DescribePhase` for reports. A provisional phase now logs a WARNING
  that names the problem and how to fix it.
- **Orthogonality check** added to the material-pattern reader (`OrthogonalityToleranceRad`). The second
  grid angle is no longer silently discarded.
- **New `TileCentricPatternGenerator`** (Backend, Revit-free, pure math):
  - `TryGenerate` - count-driven X-2X-X search with irregular-footprint support, for a caller that has
    a required device count.
  - `TrySolveBalancedLayout` - the API the placement engines use; takes a MAXIMUM whole-tile pitch,
    derives the count, and solves the balanced start tile. This is what replaced the anchored array.
  - `SnapPositionsToTiles` - safety net that moves positions onto free tile blocks without ever
    dropping a device or letting two share a tile.
- **Sprinklers wired in.** `BruteForceCalculationService.SelectCenteredGridOnTiles` now uses the solved
  balanced layout for `uIndices`/`vIndices` when one exists, keeps the existing bounded snap / obstacle
  recovery / `FinalizeSelection`, and falls back to the old anchor-and-append otherwise. Adds a
  diagnostic line describing the solved pattern and wall gaps.
- **Smoke and notification wired in.** New `AddTileCentricCandidates` in
  `SmokeDetectorCalculationService` replaces "enumerate every tile centre" with a balanced
  whole-tile-pitch pattern, still filtered by each tab's `TryAccept`, with the old enumeration as
  fallback.
- **`CeilingGrid` made public** (it was private nested in `CeilingGridMath`) so the generator can use it.
- **New `TileCentricPatternTests`** (13 tests) registered; suite count 19.

### Verified
Builds clean on Revit2024/2025/2026; 19/19 suites pass. Worst deviation from a true tile centre is
0.000E+000 ft on both axis-aligned and 30-degree lattices. The worked example (20 ft room, 2 ft tiles,
15 ft spacing) now yields a 10 ft pitch with 2 columns and 5 ft wall gaps, replacing the previous
1 ft / 14 ft / 4 ft spread.

### Open / not runtime-verified
See [[DECISIONS]] for the full list of deferred items (no view-export fallback for pre-2025.3,
family rotation not aligned to the grid angle, one lattice per multi-ceiling room, and the two engines'
differing ceiling ranking). Nothing here has been confirmed in a live Revit session yet; the new
`CeilingGrid` extraction diagnostic is how to confirm detection and phase.