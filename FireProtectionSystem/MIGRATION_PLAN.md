# FireProtectionSystem — Migration Plan

> **Deliverable C** of the production rewrite (master prompt §5 / §39 / §45 Step 9).
> Ten controlled phases — **no god-level fix** (§39). Each phase: goal, concrete steps grounded in *this*
> repo, status, and validation. After every phase: build, test, inspect changed files, update tracking
> Markdown, record decisions.
>
> Status legend: ✅ done · 🔵 in progress (this session) · ⬜ planned · ⏸ **runtime-blocked** (no live Revit
> here — hard rule 18) · 🧑‍🔧 **FPE-gated** (needs approved engineering values — hard rule 4).

Related: [[ARCHITECTURE_AUDIT]] · [[STANDARDS_COMPLIANCE_MATRIX]] · [[RISK_REGISTER]] · [[DECISIONS]] ·
[[PROGRESS]] · [[TODO]]

---

## Guiding constraints

- The existing architecture already satisfies the big structural mandates (Revit-free calc/UI, one-way
  dependency, thin code-behind — see [[ARCHITECTURE_AUDIT]] §1.1). So this is **not** a re-layering; it is a
  **targeted** remediation centered on the placement adapter plus staged correctness fixes.
- The `ISprinklerPlacementService.PlaceSprinklers(string, string, BruteForceCalculationResult)` contract is
  **preserved** to avoid rippling through the whole injection chain (Decision 003).
- No NFPA-13 numeric value is invented (hard rule 4); no runtime result is fabricated (hard rule 18).

---

## Phase 1 — Discovery & baseline  ✅
**Goal:** understand the system as-built; establish a green baseline.
**Steps:** full read of the placement crux, calc engine, extraction, UI wiring, `.csproj`; two independent
verification passes; run build + tests.
**Status:** ✅ done — build 0 errors (828 benign `MSB3277`), 14/14 tests. Deliverables A/B/C/D/E produced.
**Validation:** static ✅.

## Phase 2 — Domain / application boundaries  ✅ (verified) + ⬜ (hardening)
**Goal:** calc responsibilities isolated from Revit.
**Finding:** **already satisfied** — calc engine + DTOs verified Revit-free.
**Remaining (⬜):** introduce an explicit Revit-free `PlacementInstruction` DTO (§20) when the calc→placement
contract is formalized (pairs with Phase 5). Low risk, additive.
**Validation:** static.

## Phase 3 — Extraction / normalization  ✅ (verified) + ⬜ (X-1 hardening)
**Goal:** formalize model + coordinate data; transform exactly once.
**Finding:** single-transform invariant holds; 8-corner bbox correct.
**Remaining (⬜, X-1):** `RoomExtractor.FindCeilingsForRoom` can associate a wrong-level ceiling (no level
filter, 20 ft fallback, first-not-nearest). Fix: add a level/elevation filter and sort matched ceilings so
the room's own (nearest-above) ceiling is primary. Test with a stacked-room fixture.
**Validation:** static + ⏸ runtime spot-check.

## Phase 4 — Ceiling / level / link resolution  ✅ (present) + ⬜ (refactor)
**Goal:** dedicated resolvers for level, ceiling, face, link.
**Finding:** `ResolveHostLevel`, `FindCeilingHost`, `FindCeilingFaceInDocument`, `FindHostFaceReference`
already implement this inside the placement service.
**Remaining (⬜):** extract these into a reusable `CeilingHostResolver` / level-resolver so the new
strategies (Phase 5) consume them cleanly and they become unit-targetable. (Done incrementally as part of
Phase 5 wiring.)
**Validation:** static; ⏸ runtime for link face-hosting reliability.

## Phase 5 — Family compatibility & placement strategies  🔵 **(this session — critical path)**
**Goal:** strategy-based placement keyed to the proven `FamilyPlacementType`; **remove the
WorkPlaneLevelFallback**.
**Steps:**
1. Add `PlacementStatusCodes` (§26 machine-readable constants).
2. Add `IFamilyPlacementStrategy` + `PlacementContext` + `PlacementOutcome` (Backend, Revit-facing).
3. Implement `FaceBasedPlacementStrategy` (host face required, else `REQUIRED_HOST_UNAVAILABLE`).
4. Implement `WorkPlaneBasedPlacementStrategy`: face host if available, else **`SketchPlane` overload at the
   requested world XYZ** (honors Z), else structured `WORKPLANE_UNAVAILABLE` — **never** the level overload.
5. Implement `LevelBasedPlacementStrategy` (genuine `OneLevelBased`).
6. Rewire `RevitSprinklerPlacementService` to select + invoke a strategy per point; keep the transaction,
   symbol resolution, level resolution, duplicate check, and requested-vs-actual recording.
7. Add machine-readable `StatusCode`/`ErrorCode` to the UI DTOs (additive, Revit-free).
**Status:** 🔵 implementing now.
**Validation:** static (build across 2024/2025/2026 + 14/14 tests) ✅ target; **⏸ runtime**: confirm actual
XYZ ≈ requested (Room 1235683 ≈ (14.12, 31.95, 12)); no (0,0,0). See [[DECISIONS]] Decision 011.

## Phase 6 — Validation & diagnostics  🔵 (partial this session) + ⬜/⏸
**Goal:** structured execution result + post-placement validation (§23, §24).
**This session (🔵):** post-placement spatial check (actual vs requested delta → `PLACED_AND_VALID` /
`PLACED_BUT_INVALID`); structured status/error codes; stop `Warnings` pollution (A-1).
**Remaining (⬜):** richer candidate pre-validation (C-C inner-loop clearance, C-H obstacle Z-gating, C-F
NaN guard) with unit tests. **⏸ runtime:** the `PLACED_BUT_NOT_VISIBLE_IN_VIEW` diagnostic — investigate
view range/discipline/phase; **never move coordinates for visibility** (§24).
**Validation:** static + ⏸ runtime.

## Phase 7 — UI workflow  ⬜
**Goal:** Preview → Commit → Results → Diagnostics cleanly; surface status codes + PLACED_BUT_INVALID.
**Steps:** show per-point status/error codes and the requested-vs-actual delta in the results view; move
`MessageBox` behind a UI notification service; a dry-run "Preview" before the committing "Commit".
**Status:** ⬜ planned.
**Validation:** static; ⏸ runtime UX check.

## Phase 8 — Idempotency / audit  ⬜ (needs runtime)
**Goal:** safe repeated runs (§27, §28).
**Steps:** stamp each created instance with a `GenerationId` (and source room+point id) via a shared
parameter / extensible storage; on re-run, detect and update/replace only application-generated instances;
never touch user-created sprinklers. Add a `GenerationId` + timestamp to the result/audit JSON.
**Status:** ⬜ planned; **⏸** the tagging + re-run behavior needs Revit to verify.
**Validation:** static (design) + ⏸ runtime.

## Phase 9 — Tests / regression  ⬜
**Goal:** comprehensive Revit-free coverage.
**Steps:** add fixtures for X-1 (stacked-room ceiling association), C-A (multiple FLAT ceilings), C-C
(inner-loop clearance), C-G (ceiling-failure not masked), C-F (NaN input), and a strategy-selection test
(WorkPlaneBased never selects the level overload — assert via a seam/fake, since placement itself needs
Revit). Grow beyond the current 14.
**Status:** ⬜ planned.
**Validation:** static (automated).

## Phase 10 — Cleanup  ⬜
**Goal:** remove obsolete code only after proving it is unused (§39; hard rule 15).
**Steps:** the working tree already deleted the legacy `Services/Placement/*` and `Models/Placement/*`
(old `RevitSprinklerPlacer`, `SprinklerPlacementService`, `SnowdonPlacementProvider`, …) — verify nothing
references them, then commit the deletions. Remove dead calc enum values (C-D) once statuses are wired.
**Status:** ⬜ planned (deletions staged in git working tree; not yet committed).
**Validation:** static (build + grep for references).

---

## Environment-capability summary

| Category | Phases | Note |
|---|---|---|
| Achievable here (static) | 1, 2✅, 3✅-part, 4✅-part, **5**, 6-part, 9, 10 | build + 14 tests are the gate |
| Runtime-blocked (⏸) | 5-verify, 6-visibility, 8-tagging, F2/F3 | needs Revit + `02_FireProtection_Test` host + `01_Architectural_Test` link + `Sprinkler - Pendent - Hosted` |
| FPE-gated (🧑‍🔧) | E1/E2/E4 spacing, C-B/C-D coverage model | approved NFPA-13 tables must be supplied — not invented |

## Definition of done per phase
build (2024/2025/2026) = 0 errors · tests green · changed files inspected · tracking docs updated
([[PROGRESS]], [[TODO]], [[SESSION_NOTES]], [[DECISIONS]]) · runtime items explicitly marked **pending**.
