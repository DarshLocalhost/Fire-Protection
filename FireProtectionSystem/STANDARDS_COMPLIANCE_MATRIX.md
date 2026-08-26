# FireProtectionSystem — Standards Compliance Matrix

> **Deliverable B** of the production rewrite (master prompt §5 / §45 Step 9).
> Each row: **requirement** → **current-code evidence** → **gap** → **planned change** → **validation type**.
> Validation types: `static` (provable by reading code / unit test), `runtime` (requires Revit + open
> model — **cannot be executed in this environment**, marked *pending*), `FPE` (requires a qualified fire
> protection engineer to supply approved values — **must not be invented**, hard rule 4).
>
> Two requirement sources:
> - **MP** = this project's master prompt mandates (§ numbers, hard rules) — the authoritative process/quality spec.
> - **NFPA** = NFPA-13 (sprinkler layout) / NFPA-72 (alarm) engineering values.

Related: [[ARCHITECTURE_AUDIT]] · [[MIGRATION_PLAN]] · [[RISK_REGISTER]] · [[DECISIONS]] · [[TODO]]

---

## Standards-source status (MP §1)

| Standard | File in repo | Ingestion status |
|---|---|---|
| NFPA-72 (alarm) — "72-19-PDF 1.pdf" | present at root | text-extractable; partially digested for alarm concepts. Alarm module not yet built. |
| Sprinkler layout textbook — "Layout, Detail, And Calculations of Fire Sprinkler Systems (1).pdf" | present at root | **image-only scan; no OCR tool available in this environment** → full numeric extraction infeasible here. NFPA-13 spacing tables are therefore **not** encoded. |

Consequence: no NFPA-13 numeric value is hard-coded from memory (hard rule 4). All spacing remains
**provisional** and every room is `ReviewRequired` until an FPE supplies approved tables. This is honest and
by design (Decision 004), not a defect.

---

## A. Architecture & isolation (MP §6, §7, §30; hard rules 12, 13)

| # | Requirement | Current evidence | Gap | Planned change | Validation |
|---|---|---|---|---|---|
| A1 | Domain/calc layer free of `Autodesk.Revit.DB` | calc engine + consumed DTOs verified Revit-free (grep clean; only doc-comments) | none | preserve invariant; add strategy classes in **Backend** only | static ✅ |
| A2 | One-way dependency (UI has no Backend ref) | `Backend.csproj` → UI; UI.csproj has no back-ref | none | preserve | static ✅ |
| A3 | No business logic in code-behind | all `*.xaml.cs` = `InitializeComponent` only | none (MessageBox in VM is a minor UI concern) | later: move dialogs behind a UI service (Phase 7) | static ✅ |
| A4 | Layered separation extraction→calc→placement→report | present (see [[ARCHITECTURE_AUDIT]] §1) | no dedicated `PlacementInstruction` type (§20) | introduce a Revit-free placement-instruction DTO when calc→placement contract is formalized (Phase 3/5) | static |

## B. Placement strategy & family compatibility (MP §12–§16; hard rules 5, 6, 7, 8, 19, 20)

| # | Requirement | Current evidence | Gap | Planned change | Validation |
|---|---|---|---|---|---|
| B1 | Detect placement type from **proven** `FamilyPlacementType`, not the name | `PlaceSprinklers` reads `symbol.Family.FamilyPlacementType` | none | keep; centralize in strategy selector | static ✅ |
| B2 | **No WorkPlaneBased→Level silent fallback** (hard rules 5,6,19; §13,§15) | ❌ `WorkPlaneLevelFallback` at `RevitSprinklerPlacementService.cs:254-263` | **VIOLATION** | replace with `IFamilyPlacementStrategy`; WorkPlaneBased with no face host → **`SketchPlane` overload honoring world XYZ**, else structured failure — never the level overload (Decision 011) | static (code) ✅ this session; **runtime pending** |
| B3 | FaceBased with no host → PLACEMENT_FAILED, not a fake instance (§13; hard rule 8) | already fails clearly (`:265-290`) | reason not machine-coded | keep behavior; add error code `REQUIRED_HOST_UNAVAILABLE` | static ✅ |
| B4 | Do not treat WorkPlaneBased as automatically FaceBased (hard rule 7) | current code routes WorkPlaneBased through the face branch first, then falls back | partial | strategy tries face host **then** work-plane (SketchPlane) — both valid for WorkPlaneBased; no Level substitution | static ✅ |
| B5 | Successful `NewFamilyInstance` ≠ correct placement (hard rule 8; §23) | ❌ no post-placement spatial check | **GAP** | add post-placement validation: compare actual vs requested XYZ, classify `PLACED_BUT_INVALID` on large delta / origin | static (logic) ✅; **runtime pending** |
| B6 | Correctness over count (hard rule 20; §47) | count-driven fallback present (B2) | contradicts §47 | strategy reports `26 calc / N valid / M placed+validated / K rejected w/ reasons` shape | static ✅ |

## C. Coordinates & links (MP §8, §9; hard rules 10, 11)

| # | Requirement | Current evidence | Gap | Planned change | Validation |
|---|---|---|---|---|---|
| C1 | Transform linked geometry exactly once | single transform at extraction; 8-corner bbox | none | preserve | static ✅ |
| C2 | Never mix link & host coordinates | placement `xyz` stays host-space; only search point → link space | none | preserve; document in strategy | static ✅ |
| C3 | Never apply transform twice | no double-transform found | invariant is convention-only | (optional, later) host-space wrapper type | static ✅ |
| C4 | Linked-level cannot be used directly | `ResolveHostLevel` maps via elevation/name (Decision 007) | none | keep | static ✅; runtime pending |

## D. Results, diagnostics, audit (MP §25, §26, §28)

| # | Requirement | Current evidence | Gap | Planned change | Validation |
|---|---|---|---|---|---|
| D1 | RequestedXYZ + ActualXYZ recorded | `PlacedSprinklerEntry.Requested*` + actual `X/Y/Z` | none | keep | static ✅ |
| D2 | Stable machine-readable status/error codes (§26) | ❌ `Reason` is free text; no code field | **GAP** | add `PlacementStatusCodes` constants + `StatusCode`/`ErrorCode` fields on UI DTOs | static ✅ this session |
| D3 | Separate CALCULATION_FAILED / PLACEMENT_FAILED / PLACED_BUT_INVALID / PLACED_AND_VALID / NOT_VISIBLE (§24) | partial (placed vs failed only) | **GAP** | add `PLACED_BUT_INVALID`; NOT_VISIBLE stays a **runtime** diagnostic (pending) | static (first 4) ✅; visibility **runtime pending** |
| D4 | Audit trail not UI-text-only (§28) | timestamped input/result JSON exist | debug strings pollute `Warnings` (A-1) | move family-type info to a structured field; stop per-point `[PLACED]` spam | static ✅ |
| D5 | Generation id / element tagging (§27, §28) | ❌ none; 0.25 ft proximity only | **GAP** | Phase 8: tag instances with a generation id parameter for safe re-runs | static + runtime (Phase 8) |

## E. Engineering rules (MP §17, §18; hard rule 4) — **FPE-gated**

| # | Requirement | Current evidence | Gap | Planned change | Validation |
|---|---|---|---|---|---|
| E1 | Do not invent NFPA values | uniform 15 ft placeholder, `HasApprovedRules=false`, `IsProvisional=true` | intentional placeholder (Decision 004) | keep provisional; flip `HasApprovedRules` only when FPE tables are supplied | **FPE** |
| E2 | Distinguish approved / configured / provisional / missing | `IsProvisional` flag + review warnings | value states not fully enumerated | add explicit value-state enum when real rules land | static + FPE |
| E3 | Never present provisional as final compliance | UI shows provisional warnings; every room `ReviewRequired` | C-G masks *ceiling* failures under `ReviewRequired` | fix C-G so ceiling failures are not laundered into success | static |
| E4 | Coverage actually verified (§19) | ❌ C-B: max-spacing used as min-separation; coverage never checked | **GAP (engineering)** | redesign selection to verify coverage against approved coverage radius — **needs approved values** | static (algorithm) + FPE (values) |

## F. Validation, transactions, visibility (MP §19, §22, §23, §24)

| # | Requirement | Current evidence | Gap | Planned change | Validation |
|---|---|---|---|---|---|
| F1 | Validate every candidate before Revit (§19) | inline filters (inside-room, boundary, obstacle, existing) | C-C (inner loops), C-H (Z gating), C-F (NaN) | harden in a later calc phase with tests | static |
| F2 | Deliberate transaction boundaries; no unknown state on failure (§22) | single batch transaction, rollback on exception | batch-level only; document policy | document one-transaction-per-generation + rollback; consider per-room subtransactions | static ✅; runtime pending |
| F3 | Post-placement validation (§23) | ❌ absent | **GAP** | B5 change | static ✅; runtime pending |
| F4 | Visibility diagnosed separately from placement (§24) | not implemented | **GAP** | runtime-only diagnostic checklist; **never move coordinates for visibility** | **runtime pending** |

## G. Process discipline (MP §4, §39, §43–§47)

| # | Requirement | Status |
|---|---|---|
| G1 | Read standards before major code change (hard rule 1) | done as feasible (NFPA-13 PDF image-only — E1) ✅ |
| G2 | Inspect whole repo before redesign (hard rule 2) | done — full crux read + 2 verification passes ✅ |
| G3 | Deliverables exist before destructive rewrite (§5) | this matrix + [[ARCHITECTURE_AUDIT]] + [[MIGRATION_PLAN]] + [[RISK_REGISTER]] + Decision 011 ✅ |
| G4 | No god-level fix; phased (§39) | Phase 5 only this session; C-*/X-1/I-1 staged ✅ |
| G5 | Never claim tests/runtime not run (hard rules 17, 18) | build/tests re-run; **all runtime marked pending** ✅ |
| G6 | Do not remove historical tracking info (§4, hard rule 16) | Decision 010 kept + marked *Superseded*; docs appended not overwritten ✅ |

---

## Scoreboard

- **Compliant now (static):** A1–A3, B1, B3, B4, C1–C4, D1, E-flags honest, G1–G6.
- **Fixed this session (static; runtime pending):** B2, B5, B6, D2, D3 (4 of 5 states), D4.
- **Staged, later phases (static + tests):** A4, D5, F1, F2, F3-runtime, X-1, C-A, C-C, C-E, C-F, C-G, C-H, A-1, I-1.
- **FPE-gated (must not invent — hard rule 4):** E1, E2, E4 (values), C-B/C-D (coverage model values).
- **Runtime-only, pending (no live Revit here — hard rule 18):** B2/B5 confirmation, C4 confirmation, F2/F3 runtime, F4 visibility, the (0,0,0) re-verification.
