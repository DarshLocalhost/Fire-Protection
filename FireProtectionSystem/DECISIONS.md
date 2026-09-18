# FireProtectionSystem — Decisions

> Decisions are extracted from actual source code, comments, `.csproj`, and `TextFile1.txt`. No
> decisions are invented. Where rationale cannot be established, it is marked **Undetermined**.

## Decision 001 — Extraction is fully separated from calculation, UI, and placement

### Status
Accepted

### Context
The project must read model data reliably without coupling to placement logic or WPF.

### Decision
Extraction lives in `FireProtection.Backend/Services/Model/*` and `Services/Extraction/*`, produces a
normalized `ModelSnapshot`, and never calls calculation or placement code.

### Why
Isolation keeps the read-only extraction testable and lets placement evolve independently.

### Evidence
`TextFile1.txt` "KEY DESIGN RULES": *"Extraction fully separated from placement calculation and UI."*
`FireProtectionExtractionService` has no reference to `BruteForceCalculationService`.

### Consequences
UI consumes a serialized snapshot, not extraction internals.

### Alternatives Considered
Not established from the repository.

---

## Decision 002 — All linked-model geometry is normalized to host-MEP coordinates at extraction time

### Status
Accepted

### Context
Rooms/ceilings/obstacles may originate in linked architectural models with their own coordinate systems.

### Decision
Extractors transform every linked point/bbox/vector to host coordinates via
`RevitModelContext.TransformPoint` / `TransformBoundingBox` / `TransformVector` during extraction.
Downstream calculation and placement use coordinates as-is (no second transform).

### Why
Prevents double-transform bugs and keeps the calculation/placement layers Revit-link-agnostic.

### Evidence
`RoomExtractor.cs` uses `RevitModelContext.TransformPoint` for locations, bounding boxes, and boundary
curves; `CeilingExtractor.cs` transforms face min/max and normals; `TextFile1.txt`: *"All linked
geometry transformed to host-MEP coordinates via link-instance transforms."*

### Consequences
`CalculatedSprinklerPoint.X/Y/Z` are already host-MEP feet; placement passes them directly to
`doc.Create.NewFamilyInstance`.

### Alternatives Considered
Transforming at placement time (rejected implicitly — would double-apply the link transform).

---

## Decision 003 — Backend references UI; UI must not reference Backend

### Status
Accepted

### Context
Concrete Revit services (placement, family source, exporter) live in Backend, but the UI must consume
them through interfaces.

### Decision
`FireProtection.Backend.csproj` has a `ProjectReference` to `FireProtection.UI`. The UI project has
**no** back-reference. Backend constructs concrete services and injects them through the
`UiLauncher → MainWindow → MainWindowViewModel → SprinklerBruteForceViewModel` constructor chain.
Service contracts (`ISprinklerPlacementService`, `ISprinklerFamilySource`, `IPlacementInputExporter`)
are defined in the UI project.

### Why
Keeps the Revit-API surface in Backend while letting the UI remain a pure WPF consumer of interfaces.

### Evidence
`FireProtection.Backend.csproj` `<ProjectReference Include="..\FireProtection.UI\..." />`; `UI.csproj`
has no `ProjectReference` to Backend; `FireProtectionCommand.cs` constructs
`RevitSprinklerPlacementService` / `RevitSprinklerFamilySource` / `PlacementInputJsonExporter` and
passes them to `UiLauncher.Show`.

### Consequences
Changing UI constructor signatures ripples into Backend's `UiLauncher`/`MainWindow`.

### Alternatives Considered
Not established.

---

## Decision 004 — Sprinkler spacing uses provisional 15 ft placeholder values

### Status
Accepted (intentional, not final)

### Context
A runnable calculation is needed before engineering-approved values exist.

### Decision
`DefaultHazardPlacementRules` returns `MaxSpacingFt = 15.0`, `CoverageRadiusFt = 7.5`,
`ObstacleClearanceFt = 1.0`, `BoundaryClearanceFt = 1.0`, `ExistingSprinklerSeparationFt = 7.5` for
**all** hazard classes, with `HasApprovedRules = false`. Every room is flagged `ReviewRequired`.

### Why
Provides a deterministic, non-engineering baseline so the engine and placement can be exercised; the
values are explicitly provisional.

### Evidence
`DefaultHazardPlacementRules.cs` comments: *"the spacing values below are PROVISIONAL placeholders
only... NOT NFPA13-2022 compliant."* `BruteForceCalculationService` sets `IsProvisional` and adds a
review warning.

### Consequences
No output is NFPA-compliant; human review is mandatory. This is by design, not a bug.

### Alternatives Considered
Not established.

---

## Decision 005 — Extraction is read-only and uses no transaction

### Status
Accepted

### Context
Extraction must never modify the user's model.

### Decision
The extraction pipeline performs no `Transaction`; it only reads element geometry/parameters.

### Why
Safety: a failed or partial extraction must not dirty the document.

### Evidence
`TextFile1.txt`: *"Zero-transaction, read-only extraction (model never dirtied)."* No `Transaction` in
`FireProtectionExtractionService` or the extractors.

### Consequences
Extraction cannot be aborted mid-way by a model-lock; per-room errors are caught and reported.

### Alternatives Considered
Not established.

---

## Decision 006 — The `.addin` manifest registers only the Application add-in

### Status
Accepted

### Context
Revit add-ins can register `Application` (ribbon) and/or `Command` entries.

### Decision
`DeployToRevit` writes a `FireProtectionSystem.addin` containing a single `Application` add-in pointing
to `FireProtection.Backend.Revit.FireProtectionApplication`. The command is launched from the ribbon
button's `PushButtonData` (which names `FireProtection.Backend.Commands.FireProtectionCommand`), so no
separate `<AddIn Type="Command">` entry is needed.

### Why
One registration point (the ribbon) keeps deployment simple and consistent across Revit versions.

### Evidence
`FireProtection.Backend.csproj` `DeployToRevit` target writes only an `Application` `<AddIn>`;
`FireProtectionApplication.cs` builds the button with `CommandClass =
"FireProtection.Backend.Commands.FireProtectionCommand"`.

### Consequences
The command is reachable only via the ribbon button, not via Revit's "Add-In Commands" list.

### Alternatives Considered
Not established.

---

## Decision 007 — Linked-model LevelIds cannot be used directly in the host document

### Status
Accepted

### Context
A room/level extracted from a linked model carries the **linked document's** `ElementId`, which is
meaningless in the host `Document`.

### Decision
`RevitSprinklerPlacementService.ResolveHostLevel(levelId, levelName)` first tries the host document;
if that fails it searches each `RevitLinkInstance`'s document, transforms the linked level's elevation
into host space, and matches by elevation (±0.01 ft) or by name to a real host `Level`. If unresolved,
it records a failed placement with diagnostics (no silent substitution).

### Why
Prevents placing sprinklers on a non-existent/incorrect host level.

### Evidence
`RevitSprinklerPlacementService.cs` `ResolveHostLevel` implementation; comment in `ARCHITECTURE.md`
(§14) documents the linked ceiling/level handling.

### Consequences
Placement of linked-room sprinklers depends on a matching host level existing.

### Alternatives Considered
Not established.

---

## Decision 008 — Calculated coordinates are already in host-MEP feet; placement applies no transform

### Status
Accepted

### Context
See Decision 002.

### Decision
`RevitSprinklerPlacementService` passes `new XYZ(point.X, point.Y, point.Z)` directly to
`NewFamilyInstance`; it does **not** apply any link transform.

### Why
Coordinates were already normalized during extraction (Decision 002).

### Evidence
`RevitSprinklerPlacementService.PlaceSprinklers` constructs `XYZ` from the point fields directly.

### Consequences
If extraction ever stopped normalizing, placement would be wrong — the invariant must be preserved.

### Alternatives Considered
Not established.

---

## Decision 009 — Per-room error isolation in extraction and calculation

### Status
Accepted

### Context
One bad room/level must not abort the whole operation.

### Decision
Extraction catches per-room issues into `ExtractionIssue` warnings/errors. `BruteForceCalculationService
.Calculate` wraps each room in a try/catch and records a `RoomCalculationResult` with `Failed` status
instead of throwing.

### Why
Robustness: partial models still produce usable output.

### Evidence
`TextFile1.txt`: *"Per-room error handling: bad rooms produce warnings but do not abort the whole
extraction."* `BruteForceCalculationService.Calculate` per-room try/catch.

### Consequences
Results may contain per-room failures that the UI must surface.

### Alternatives Considered
Not established.

---

## Decision 010 — WorkPlaneBased families must fall back to Level placement when no ceiling face host exists

### Status
**SUPERSEDED by [Decision 011](#decision-011--family-placement-uses-an-explicit-strategy-keyed-to-the-proven-familyplacementtype-no-workplanebasedlevel-silent-fallback) (2026-08-25).**
The `WorkPlaneLevelFallback` this decision introduced was proven to violate the master prompt (§13/§15 and
hard rules 5, 6, 8, 19) and to risk the (0,0,0) placement it was meant to avoid. Retained below **unedited**
for historical traceability (§4 / hard rule 16); do not act on it.

_Original status: Implemented (static), runtime verification pending._

### Context
The selected family `Sprinkler - Pendent - Hosted` (type `3/4" Pendent on Drop with Guard`) reports
`FamilyPlacementType = WorkPlaneBased`. In this project the ceilings live only in the **linked**
architectural model, and the host MEP model has no ceilings of its own.

### Decision
`RevitSprinklerPlacementService` distinguishes `FaceBased` / `WorkPlaneBased` / `OneLevelBased` by the
**proven** `symbol.Family.FamilyPlacementType` (never inferred from the family name). Placement strategy:
- `FaceBased` → requires a host face reference (host ceiling, or a linked ceiling converted via
  `Reference.CreateLinkReference`). If unavailable → clear placement failure (cannot place a FaceBased
  family without a host).
- `WorkPlaneBased` → if a ceiling face reference is found, host on it; **otherwise fall back to the
  Level-based overload** `NewFamilyInstance(xyz, symbol, level, NonStructural)`. A `WorkPlaneBased`
  family is validly placed on the Level's work plane at the already-normalized candidate XYZ (the ceiling
  elevation in host space). This fallback prevents the previous total failure where every sprinkler was
  rejected because `FindCeilingHost` returned null.
- `OneLevelBased` / unknown → Level-based overload.

### Why
The earlier fix (Decision implied by PROGRESS 2026-08-25) correctly detected `WorkPlaneBased` but routed
both `FaceBased` and `WorkPlaneBased` into the face-host branch. When no host ceiling face was available
(all ceilings in the linked model, where hosted placement on a linked face is not reliably supported by
the Revit API), `FindCeilingHost` returned null and **every** sprinkler failed. Treating `WorkPlaneBased`
as strictly face-hosted was the wrong mapping — `WorkPlaneBased` means "hosted on a work plane", which a
Level satisfies.

### Evidence
`RevitSprinklerPlacementService.cs`:
- Detection block captures and logs the real `FamilyPlacementType` (Phase 2/11 diagnostics).
- `PlaceSinglePoint` branches on `faceBased`/`workPlaneBased`; `WorkPlaneLevelFallback` strategy added.
- `FindHostFaceReference` now prefers the **downward** (−Z) ceiling underside face for a pendent, with an
  upward-face fallback, instead of only accepting +Z faces (which would host on the wrong side).
- `FindCeilingHost` returns a `CeilingHostInfo` (host / link / none, link instance name, ceiling
  ElementId) so failures are diagnosable.

### Consequences
Sprinklers place successfully even when ceilings are linked-only. Downward-face selection yields correct
pendant orientation/elevation. The linked-ceiling `CreateLinkReference` path is retained for host models
that DO contain ceilings.

### Alternatives Considered
- Forcing a host-model ceiling (e.g., Copy/Monitor) — out of scope for the placement service.
- Claiming a (0,0,0) origin reset — NOT assumed; `instance.Location` is recorded and reported for proof.

---

## Decision 011 — Family placement uses an explicit strategy keyed to the proven FamilyPlacementType; no WorkPlaneBased→Level silent fallback

### Status
Implemented (static), **runtime verification pending** (no live Revit host in this environment; hard rule 18).
Supersedes [Decision 010](#decision-010--workplanebased-families-must-fall-back-to-level-placement-when-no-ceiling-face-host-exists).

### Context
`RevitSprinklerPlacementService.PlaceSinglePoint` routed `WorkPlaneBased` families through a face-host
search and, when no ceiling face was found (ceilings live only in the linked architectural model), fell
back to the **level** overload `NewFamilyInstance(xyz, symbol, level, NonStructural)` (Decision 010's
`WorkPlaneLevelFallback`, `RevitSprinklerPlacementService.cs:254-263`).

Runtime evidence (`SPRINKLER_ACTUAL_Z_DIAGNOSTIC.md`) proved that for the selected family
`Sprinkler - Pendent - Hosted` (`FamilyPlacementType = WorkPlaneBased`) this overload produces an instance
at the **project origin (0,0,0)** — Candidate Z=12, Actual Z=0, link transform identity. The fallback thus
both (a) violated the master prompt's explicit prohibitions and (b) risked re-introducing the very bug it
was meant to fix, while a missing post-placement check would have reported it as success.

### Decision
Introduce an explicit placement-strategy boundary (master prompt §13), Revit-facing, in the Backend adapter
(strategies use Revit types, so they must **not** live in the domain — hard rule 12):

- `IFamilyPlacementStrategy` selected from the **proven** `symbol.Family.FamilyPlacementType` (never the
  family name — preserved from prior work).
- `FaceBasedPlacementStrategy` — requires a host face (host ceiling, or a linked ceiling via
  `Reference.CreateLinkReference`). No host → **`PLACEMENT_FAILED` / `REQUIRED_HOST_UNAVAILABLE`**; never a
  fabricated instance (hard rule 8).
- `WorkPlaneBasedPlacementStrategy`:
  1. if a ceiling face host is available → host on it (best: the downward/underside face for a pendant);
  2. else place on a **`SketchPlane` through the requested world XYZ** via
     `NewFamilyInstance(SketchPlane, XYZ, XYZ, FamilySymbol)` — this honors the full world coordinate
     **including Z**, which is the correct work-plane placement for a WorkPlaneBased family;
  3. if that fails → **`PLACEMENT_FAILED` / `WORKPLANE_UNAVAILABLE`** with full diagnostics.
  **The level overload is never used for a WorkPlaneBased family.**
- `LevelBasedPlacementStrategy` — used only for genuinely `OneLevelBased` families.
- **Post-placement validation** (§23): after creation, read `instance.Location`, compute the
  actual-vs-requested delta, and classify `PLACED_AND_VALID` or `PLACED_BUT_INVALID` (large delta / origin
  snap). A returned `ElementId` is **not** treated as success on its own (hard rule 8).
- **Structured status/error codes** (§26) carried on the Revit-free UI result DTOs alongside the existing
  human-readable `Reason`.

### Why
- `WorkPlaneBased` means "hosted on a work plane." A `SketchPlane` at the candidate point **is** such a work
  plane and preserves world XYZ; the `Level` overload discards the candidate position and (for this hosted
  family) collapses to the origin. Choosing the SketchPlane overload is the semantically correct,
  non-count-driven fix (hard rules 5, 6, 19, 20; §13, §15).
- Failing explicitly (with a code) is required over a silent incompatible substitution (§13; hard rule 5).
- Post-placement validation is what actually catches the (0,0,0) class of bug (hard rule 8; §23).

### Evidence
- Violation: `RevitSprinklerPlacementService.cs:254-263` (removed).
- Runtime proof of the (0,0,0) failure: `SPRINKLER_ACTUAL_Z_DIAGNOSTIC.md`.
- Overload availability: `SketchPlane.Create`, `Plane.CreateByNormalAndOrigin`,
  `NewFamilyInstance(SketchPlane, XYZ, XYZ, FamilySymbol)`, and `Reference.CreateLinkReference(RevitLinkInstance)`
  all compile across Revit 2024/2025/2026 (build verification after implementation).
- Analysis: [[ARCHITECTURE_AUDIT]] §3, [[STANDARDS_COMPLIANCE_MATRIX]] rows B2/B5, [[RISK_REGISTER]] R-01/R-03.

### Consequences
- WorkPlaneBased sprinklers place at the correct world XYZ whether or not a ceiling face host exists, with
  no origin collapse.
- A genuinely unplaceable point yields a structured failure code, not a misleading instance.
- New side effect: the SketchPlane path may create sketch planes in the model (R-15) — to be minimized and
  confirmed at runtime.
- **Runtime confirmation still required**: actual instance XYZ ≈ (14.12, 31.95, 12) for Room 1235683 with
  `02_FireProtection_Test` (host) + `01_Architectural_Test` (link); and that no (0,0,0) instances are
  created. Marked pending until executed in Revit (hard rule 18).

### Alternatives Considered
- **Keep the level fallback** (Decision 010) — rejected: proven to place at origin; violates §13/§15 and
  hard rules 5, 6, 8, 19.
- **Move the misplaced instance to XYZ after a level-overload placement** — rejected: still uses the wrong
  overload, fragile, and masks the semantic mismatch.
- **Require a host-model ceiling (Copy/Monitor)** — out of scope for the placement service.
- **Fail all WorkPlaneBased placements without a face host** — rejected: the SketchPlane overload is a valid,
  correct work-plane placement, so failing would needlessly reject placeable sprinklers (but it remains the
  explicit outcome if the SketchPlane overload itself fails).

---

## Decision 012 — Placement records the ACTUAL host/level read back from the created instance (diagnostics-only)

### Status
Implemented (static + build-verified: Revit2025 0 errors; 14/14 logic tests pass, 2026-08-25).
**Runtime verification pending** (same run as [Decision 011](#decision-011--family-placement-uses-an-explicit-strategy-keyed-to-the-proven-familyplacementtype-no-workplanebased-level-silent-fallback); hard rule 18). Does not change placement behavior.

### Context
The post-placement diagnostics recorded the requested vs actual XYZ (§23) and the *resolved* host level, but
never read back the host/level relationship the created `FamilyInstance` actually ended up with. The
documented runtime symptom — "sprinkler calculated for an upper floor appears in the wrong floor plan" — has
two very different root causes that requested-vs-actual XYZ alone cannot separate:
1. a genuine **hosting defect** (wrong/absent host, wrong level association), versus
2. a pure **view-range / level-association (visibility)** issue where the instance is physically correct but
   its Schedule Level / view range makes it show in another plan.

### Decision
On the success path only, capture read-only, best-effort diagnostics onto `PlacedSprinklerEntry`
(Revit-free DTO, strings): `FamilyPlacementType`, `HostCeilingElementId`, `LinkInstanceName`, and the values
read **from the created instance** — `ActualHostElementId`, `ActualHostName`, `ActualInstanceLevelId`,
`ActualInstanceLevelName`, and `ActualScheduleLevelName` (`BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM`).
Each read is wrapped in try/catch and returns null on failure; a diagnostic read can **never** affect placement.

### Why
- A single Revit run must yield the evidence needed to triage the symptom (hard rules 8, 18; §23/§24) rather
  than requiring another round-trip.
- The Schedule Level is precisely the association that drives plan-view visibility, so it is the key datum
  distinguishing a hosting defect from a view-range symptom (§24 "never move coordinates to fix a visibility
  symptom").

### Evidence
- Implementation: `RevitSprinklerPlacementService.cs` (`ReadActual*` helpers + success-path population);
  `SprinklerPlacementResult.cs` (`PlacedSprinklerEntry` new fields). Read-back idiom matches
  `ExistingSprinklerExtractor.cs` (`instance.Host` / `instance.LevelId`).
- Build: Revit2025 `dotnet build` 0 errors (2 benign MSB3277-class warnings), 2026-08-25.
- Tests: `FireProtection.Tests` all PASS, 2026-08-25 (logic-only; do not exercise Revit placement).

### Consequences
- The placement JSON now tells the truth about what Revit created (actual host + actual level + schedule
  level), enabling correct runtime triage.
- No behavioral change to placement; no coordinate/calc/NFPA change. Serialization is automatic (public
  properties on the already-exported DTO).
- **Runtime confirmation still required** to read these fields against a live model.

### Alternatives Considered
- **Add the fields but populate only on failure** — rejected: the "wrong floor plan" symptom occurs on
  *successful* placements, so the success path is exactly where the evidence is needed.
- **Write requested coords into the actual fields to make the export look clean** — rejected outright (hard
  rule: the export must tell the truth about what Revit created; §24, first-response constraint #7).
- **Defer all diagnostics until after a Revit run** — rejected: would force a second run to add the missing
  fields; the master prompt explicitly permits closing a diagnostics gap so one run suffices.

---

## Decision 013 - Placement eligibility is authoritative (real calculation + real placement probe), not heuristic

**Context:** A user could select rooms in the BruteForce UI that the production placement pipeline
provably cannot place, resulting in silently empty placement. The original UI used a geometry-only "eligibility"
(pre polygon ≥ 3 + `CeilingHeightFt`). A first-pass Backend preflight (2026-08-26) improved this by reusing
the placement pipeline's symbol/level/host resolution, but it was still **not authoritative**: it only tested a
single representative (centroid) point and treated `WorkPlaneBased` as *always* eligible, without running the
real BruteForce calculation or a real placement probe. **Runtime proved it false** — rooms reported eligible but
actual placement produced `Placed=0, INVALID=N, Failed=N` (candidates placed spatially-invalid, e.g. the (0,0,0)
origin snap). The centroid heuristic never exercised the actual candidate set, so it could not observe the
invalid placement.

**Decision:** Eligibility is now **authoritative**, computed by the Backend as a single source of truth:
- The UI computes each room's **real candidate points** with the exact `BruteForceCalculationService`
  (`IPlacementInputExporter.CalculateBruteForce`) that placement consumes.
- `ISprinklerPlacementService.EvaluateRoomEligibility(RoomUiData, IReadOnlyList<CalculatedSprinklerPoint> candidates, familyName, typeName)`
  probes **real placement** of each candidate through the same `strategy.Place(...)` / `ResolveHostLevel` /
  `CeilingHostResolver` path as `PlaceSprinklers`, inside a **rolled-back `Transaction`**. A room is eligible
  **only if ≥1 candidate creates a `FamilyInstance` whose ACTUAL location is spatially valid** (deviation ≤
  `PlacementValidationToleranceFt`) — the identical success criterion used by real placement. No model changes
  survive. Results are cached per (family/type/room) and invalidated via `ClearEligibilityCache()`.
- The UI consumes the returned Revit-free `PlacementEligibilityResult` to disable/block rooms before the user
  executes placement, and auto-deselects any room that becomes blocked on a family/type change. The UI does
  **not** re-implement or duplicate Revit hosting rules.

**Rationale:**
- Keeps a single source of truth: the *same* code path (real calculation + real placement validation) decides
  "can this be placed" at preflight and at placement, so the two cannot drift — directly fixing the
  `INVALID=N` blind spot.
- `FireProtection.UI` stays Revit-free; only the Backend touches the Revit API.
- Fail-closed: any failure (unresolved family/level, no candidates, no placeable candidate, no *valid*
  candidate, probe exception) blocks the room rather than silently treating it as eligible.

**Alternatives considered:**
- *Representative-point + strategy.CanHandle heuristic (first pass)* — rejected after runtime evidence: it
  reported eligible for rooms that actually placed `INVALID`, because it never ran the real calculation or a real
  placement probe over the candidate set.
- *UI-local heuristics mirroring Revit rules* — rejected: guaranteed to drift.
- *Compute eligibility inside `BruteForceCalculationService`* — rejected: the placement service already owns
  symbol/level/host resolution and the rolled-back-transaction probe; it is the natural home. (No new DI
  threading: the VM already injects `ISprinklerPlacementService`.)

---

## Decision 014 - Eligibility is a 4-state model (ELIGIBLE / BLOCKED / PLACEMENT_ERROR / UNKNOWN); placement defects are never hidden as BLOCKED — **SUPERSEDED by Decision 016 (3-state)**

**Context (2026-08-26, production-grade fix):** Decision 013 made eligibility authoritative via a real placement
probe, which fixed the `INVALID=N` blind spot. But its fail-closed rule collapsed *every* non-eligible outcome into
**BLOCKED** — including genuine placement defects (strategy threw, `RevitCreationFailed`, an instance created but
spatially invalid) and insufficient-evidence cases (family not selected, candidate calculation could not run). That
collapsing created a **circular dependency**: if the placement path itself were broken, the preflight would mark every
room `BLOCKED`, hiding the real defect behind a "you chose bad rooms" message and making the defect invisible/unfixable
through the UI. The user explicitly required: Error ≠ Blocked; Unknown ≠ Eligible/Blocked; and breaking the circular
hide.

**Decision:** `PlacementEligibilityResult` now carries a four-value `EligibilityState` and the UI/VM/Backend classify
rigorously:
- **ELIGIBLE** — `≥1` candidate placed and spatially valid (deviation ≤ `PlacementValidationToleranceFt`). Selectable.
- **BLOCKED** — deterministic inability ONLY: missing room geometry, unsupported family/placement type, no candidate
  points per the calc contract, host level unresolvable, or a *required deterministic host* provably absent
  (e.g. FaceBased with `REQUIRED_HOST_UNAVAILABLE` on every candidate). Not selectable.
- **PLACEMENT_ERROR** — a candidate existed and placement was attempted, but an unexpected API/runtime failure occurred
  OR an instance was created but is spatially invalid (`CREATED_BUT_INVALID`). This is a real defect and is surfaced
  distinctly (red outline, dedicated count, `INVALID`/`ProbeException` detail in the reason). **Never** reported as
  BLOCKED. Not selectable.
- **UNKNOWN** — insufficient evidence to decide (family/type not selected, or the candidate calculation itself could
  not run). Not selectable; visually amber. Never treated as BLOCKED.

The probe distinguishes these by inspecting `PlacementOutcome.ErrorCode` and by whether the failure is deterministic vs
a runtime defect; any *exception thrown by the probe harness or the strategy* is `PLACEMENT_ERROR` (`ProbeException`),
not BLOCKED. `RefreshEligibility` sets **UNKNOWN for every room** when the candidate calculation throws (instead of
probing with a null list and reporting BLOCKED), and auto-deselects any room that is no longer ELIGIBLE. "Show eligible
rooms only" filters to `IsEligible`, so BLOCKED/ERROR/UNKNOWN are all hidden equally rather than masking a defect.
Per-candidate structured diagnostics (`[ROOM-CANDIDATE-DIAGNOSTIC]`: requested vs actual XYZ, delta, distance,
validation status, exception detail) are emitted via `Debug` to pinpoint the exact defect.

**Rationale:** Preserves a single source of truth (same code path decides preflight and placement) **and** makes the UI
prove, not hide, placement defects. A broken placement path now shows `PLACEMENT_ERROR` (red, countable, detailed)
rather than sweeping every room into `BLOCKED`. This directly satisfies: Error ≠ Blocked; Unknown ≠ Eligible/Blocked;
break the circular hide.

**Important runtime caveat (not yet verified):** The current production placement path (post Decisions 011/012 — no
Level-overload `(0,0,0)` snap; all three strategies place at the requested ceiling-height point; `CeilingHostResolver`
handles linked ceilings correctly) is correct **by code inspection**. If, in the user's Revit session, real placement
still produces `INVALID`, the 4-state model will now report those rooms as `PLACEMENT_ERROR` (with the requested-vs-
actual deviation in `EligibilityReason`) instead of `BLOCKED` — surfacing, not hiding, the residual defect. The
`CREATED_BUT_INVALID` signal specifically indicates a coordinate/hosting mismatch (e.g. requested Z derived from
`CeilingHeightFt` vs the actual ceiling face elevation) that remains to be confirmed at runtime; it must be fixed at
the placement/coordinate source, **not** by enlarging the validation tolerance.

**Alternatives considered:**
- *Keep fail-closed → everything BLOCKED* — rejected: causes the circular hide the user explicitly forbade.
- *Enlarge `PlacementValidationToleranceFt` to absorb invalid placements* — rejected: masks real defects; the user
  required the defect be fixed, not tolerated.
- *Treat created-but-invalid as ELIGIBLE anyway* — rejected: contradicts the hard rule that a returned instance is
  not success on its own (§23/§24).

---

## Decision 015 - Room eligibility is a DETERMINISTIC feasibility preflight (no live placement); supersedes the live-probe mechanism of 013/014, keeps the 4-state contract — **SUPERSEDED by Decision 016 (3-state + WorkPlaneBased requires ceiling)**

**Context (2026-08-26, "0 eligible rooms" production bug):** Decisions 013/014 made `EvaluateRoomEligibility` decide
eligibility by performing a **live** `strategy.Place(...)` for each candidate inside a rolled-back `Transaction` and
requiring the *created* `FamilyInstance` to be spatially valid before a room could be ELIGIBLE. That hard-coupled
*room eligibility* to the *placement executor's runtime behaviour*. Consequence observed in the field: whenever the
placement path had any runtime imperfection (the `(0,0,0)`/hosting path is still RUNTIME-UNVERIFIED), **every** room
collapsed to `PLACEMENT_ERROR`/`BLOCKED`, so the UI reported **"0 rooms shown / 0 eligible"** even for perfectly
normal rooms (Room 100/101/102/1035). Master prompt §37: *all rooms unexpectedly blocked is a SYSTEM-ERROR signal to
diagnose, not valid output*; §12: *prefer a deterministic preflight that mirrors placement over hundreds of temporary
Revit placements*; §5/§10: *do not equate a placement-probe failure with room non-eligibility.*

**Decision:** `EvaluateRoomEligibility` now runs a **deterministic feasibility preflight** that reuses the **same**
resolvers as real placement — `ResolveFamily` (symbol + proven `FamilyPlacementType` + strategy), `ResolveHostLevel`,
and `CeilingHostResolver.FindCeilingHost` — but **creates no instance and opens no transaction**. Feasibility by
proven placement type:
- **FaceBased** (placement type contains "Face") — requires a real ceiling/host **face** for ≥1 candidate; none found
  anywhere ⇒ deterministic **BLOCKED** (`NO_CEILING_HOST`).
- **WorkPlaneBased** — a ceiling face when present, else a `SketchPlane` at the requested Z (always constructible from
  a point + resolved level) ⇒ **ELIGIBLE** once any candidate resolves a host level.
- **OneLevelBased** — needs only a resolvable level ⇒ **ELIGIBLE** once any candidate resolves a level.
- No candidate resolves a host level ⇒ deterministic **BLOCKED** (`MISSING_HOST_LEVEL`).
- Any exception in the preflight ⇒ **PLACEMENT_ERROR** (`PREFLIGHT_ERROR`) via the outer catch — never BLOCKED.

The pre-preflight guards are unchanged (geometry < 3 pts → BLOCKED `MISSING_ROOM_GEOMETRY`; symbol/strategy
unresolved → BLOCKED; null candidates → **UNKNOWN**; zero candidates → BLOCKED `NO_CANDIDATE_POINTS`). The per-room
result cache and `ClearEligibilityCache()` are unchanged.

**What this supersedes / preserves:** it **supersedes the live rolled-back-`Transaction` placement-probe mechanism**
of Decisions 013 and 014 (no `strategy.Place`, no `FamilyInstance` creation, no deviation check during eligibility).
It **preserves** Decision 014's four-state contract and its governing principle — ELIGIBLE / BLOCKED /
PLACEMENT_ERROR / UNKNOWN, defects are *never* hidden as BLOCKED — and all UI wiring (grid `IsEnabled=IsEligible`,
Select-All / default-selection / `CollectSelectedRooms` / `CanExecutePlaceSprinklers` all gate on ELIGIBLE, backend
`PlaceSprinklers` still independently rejects non-eligible rooms per §23).

**Rationale:** (1) **Decouples eligibility from the placement runtime defect** — a broken executor can no longer make
every room non-eligible; eligibility answers "can this family be placed here *in principle*", which the shared
resolvers determine deterministically. (2) **Faithful** — same family/level/host resolution that placement uses, so
eligibility mirrors real preconditions without inventing any fire-protection rule. (3) **Fast (§13)** — O(rooms)
read-only lookups instead of hundreds of temporary Revit placements. Actual placement correctness (exact coordinate /
spatial validity) remains a separate concern, re-checked at placement time and guarded again in the Place command.

**Alternatives considered:**
- *Force all rooms eligible / all blocked / delete the calc* — rejected (master prompt §2): hides the real cause.
- *Keep the live probe but widen tolerance* — rejected: re-introduces the coupling and masks defects (§24).
- *Keep the live probe for correctness* — rejected: it is precisely the coupling that produced "0 eligible"; a
  deterministic preflight that reuses the same resolvers is both correct-in-principle and defect-independent.

**Status (2026-08-26):** Backend **builds clean under Revit2025 (0 errors)**; Revit-free test harness **all tests
pass**. The only warnings are environmental `MSB3277` (`Microsoft.VisualBasic` version conflict from `RevitAPI.dll`),
not from this change. Runtime Revit verification **pending** (no live Revit host in this environment).

---

## Decision 016 - Eligibility is a 3-state deterministic preflight (ELIGIBLE / BLOCKED / UNDETERMINED); supersedes the 4-state PLACEMENT_ERROR/UNKNOWN split of 014/015 and the WorkPlaneBased SketchPlane assumption

**Context (2026-08-26, continuation master prompt):** Decision 015's deterministic preflight still carried a
PLACEMENT_ERROR state (it had removed the live probe but kept 4 states) and, critically, treated **WorkPlaneBased** as
ELIGIBLE whenever a candidate resolved a host level — relying on a `SketchPlane` work-plane fallback. Production room
`yfbxcv 1234` (WorkPlaneBased, no usable ceiling face, the SketchPlane overload rejected by Revit) placed `Calculated:2,
Placed:0, Failed:2`, proving the fallback is unreliable. The new master prompt mandates a **3-state** model
(ELIGIBLE/BLOCKED/UNDETERMINED), explicitly forbids any live `strategy.Place()` probe, requires a **no-ceiling
WorkPlaneBased room to be BLOCKED before placement**, and requires HazardClass to participate in eligibility.

**Decision:**
- **Three states only:** ELIGIBLE, BLOCKED, UNDETERMINED. There is no placement-error state — the preflight never
  places, so there is no placement runtime to error on. PLACEMENT_ERROR and UNKNOWN from Decisions 014/015 are
  collapsed into UNDETERMINED (config/infra/insufficient-evidence). BLOCKED remains a deterministic, reason+code outcome.
- **No live probe:** `EvaluateRoomEligibility` performs only **read-only** queries (`ResolveFamily`, `ResolveHostLevel`,
  `CeilingHostResolver.FindCeilingHost`). No `Transaction`, no `FamilyInstance` creation, no `strategy.Place()`.
- **Host requirement by proven `FamilyPlacementType`:** `FaceBased` **and** `WorkPlaneBased` both REQUIRE a usable
  ceiling/host **face** on ≥1 candidate. `WorkPlaneBased`'s `SketchPlane` fallback is NOT trusted (the `yfbxcv 1234`
  failure). `OneLevelBased` requires only a resolvable level. A numeric `CeilingHeightFt` is never proof of a host.
- **Classification:** geometry incomplete → BLOCKED `MISSING_ROOM_GEOMETRY`; family/type not resolved → UNDETERMINED
  `UNSUPPORTED_FAMILY_PLACEMENT` (a config error, NOT "every room blocked" — master prompt §19); strategy null → BLOCKED
  `UNSUPPORTED_FAMILY_PLACEMENT_TYPE`; candidates null → UNDETERMINED `CALCULATION_FAILED`; candidates empty → BLOCKED
  `NO_CANDIDATE_POINTS`; no resolvable level → BLOCKED `MISSING_HOST_LEVEL`; ceiling-required & no host face → BLOCKED
  `NO_USABLE_CEILING_HOST`; ≥1 valid candidate → ELIGIBLE; unexpected exception → UNDETERMINED `PREFLIGHT_ERROR`.
- **HazardClass participates:** the eligibility cache key includes `HazardClass` and `RefreshEligibility` re-runs on a
  per-room hazard edit, because candidate generation (and therefore feasibility) is hazard-dependent.

**What this supersedes:** Decision 015's 4-state contract and its WorkPlaneBased-ELIGIBLE-on-level-resolution rule; the
PLACEMENT_ERROR/UNKNOWN split of Decision 014 (both become UNDETERMINED). Decision 013's authoritative-single-source
principle is preserved.

**Rationale:** The 3-state model with a read-only preflight is the minimal contract the master prompt requires; folding
PLACEMENT_ERROR into UNDETERMINED removes a state that could never occur without a live probe. Requiring a real ceiling
host for WorkPlaneBased closes the `yfbxcv 1234` regression (a room that provably cannot be placed is BLOCKED, not
silently ELIGIBLE).

**Status (2026-08-26):** UI builds clean (0 errors). Backend not buildable in the headless CLI (Revit API `CS0246`,
environmental); type-correctness verified by inspection. Runtime Revit verification **pending**.

---

## Undetermined Decisions

The repository does not currently provide enough evidence to determine the rationale behind:

- **NFPA13-2022 compliance strategy** — the project references NFPA13-2022 in metadata/comments, but the
  actual spacing values are provisional placeholders; the intended path to approved values is not in code.
- **Snowdon integration** — generic prompts mention Snowdon providers, but no such code exists; the name
  is only sample data. Intent is unestablished.
- **Collision / Smoke Detector / Notification Appliance scope** — UI tabs exist as empty shells; whether
  full features are planned, deferred, or out of scope is not documented in the repository.
- **Multi-link / multi-host coordination beyond level resolution** — only linked-level resolution is
  implemented; broader linked-placement strategy is unestablished.

---

## Decision 017 — Per-room sprinkler family/type selection (replaces universal combo) + Excel-driven catalog

### Status
Accepted (plan locked, 2026-09-01). Implementation pending — not yet coded. See [[TODO]] P1 (catalog + per-row).

### Context
Today the SprinklerBruteForce UI exposes a single universal sprinkler family + type combo (`SelectedSprinklerFamily`,
`SelectedSprinklerType` on `SprinklerBruteForceViewModel`) that drives all selected rooms. Seniors requested:
(a) the family/type selector moves **per-room** as a new column in the room list; (b) the catalog of
available families and types is sourced from a **master Excel/CSV file** (not directly from the Revit model);
(c) if a chosen family/type does not exist in the live Revit document, a warning popup lists the missing
entries and placement proceeds for the available rooms; (d) the Revit family listing logic is **commented
out (not deleted)** behind a config flag so it can be re-enabled for verification against the catalog. The
same Excel pattern is required for Smoke Detectors (per-level DetectorType / Mount / CeilingSlope) and
Notification Appliances (per-level ApplianceType / Candela + dBA as a composite pair).

### Decision
- **Per-row sprinkler family/type**: each `RoomItemViewModel` carries `SelectedFamily` and `SelectedType`
  (editable, with "modified" indicator mirroring today's `IsHazardClassOverridden`). Types are filtered
  by the row's selected family. A default value (propagated from a level default) is seeded; a
  "Reset to default" button is provided per row. A bulk "Apply to all eligible rows" button is provided
  per dropdown.
- **Catalog source = Excel (one workbook, one sheet per category)**, with a `CatalogVersion` metadata row.
  Proposed schema (locked):
  - `Sprinklers` sheet: `Category, FamilyName, TypeName, HazardClass, Mount?, Notes?`
  - `SmokeDetectors` sheet: `Category, FamilyName, TypeName, DetectorType, Mount, CeilingSlope, Notes?`
  - `NotificationAppliances` sheet: `Category, FamilyName, TypeName, ApplianceType, Candela, NotificationDba, Notes?`
  (Candela + dBA are presented as a composite pair in the UI; one row = one valid combo.)
- **Excel is read-only**; the add-in never writes back. The user selects the file **each session** via a
  file picker (no fixed path). A "Reload catalog" button hot-reloads from disk without restarting Revit.
- **One catalog serves all projects for v1** (multi-catalog per project deferred).
- **Revit family listing is commented out, not deleted.** A new config flag `UseRevitFamilyListing`
  (default `false`) gates it so the catalog is the primary source but the Revit listing can be re-enabled
  for verification.
- **Missing-in-model check fires on both** the per-row dropdown change (debounced) AND on Place. Failures
  surface in an **interactive modal** with "Proceed with available" / "Cancel" and an "Export missing
  list to CSV" button. Missing-only rooms are skipped; if all rooms on a level fail, the level is
  auto-deselected. `SprinklerPlacementResult` gains a new `SkippedMissingFamilyCount` counter.
- **Per-row sprinklers also get two editable override columns**: `MaxSpacingFt` (= "sprinkler-to-sprinkler
  space") and `BoundaryClearanceFt` (= "wall space"). These thread into the BruteForce engine per room
  (see [Decision 018](#decision-018--per-room-spacingclearance-override-threads-through-the-bruteforce-engine)).
- **Smoke Detector / Notification per-level fields** are declarative metadata only for v1 (no algorithmic
  effect). `DetectorType` / `Mount` / `CeilingSlope` (Smoke) and `ApplianceType` / `Candela + dBA`
  (Notification) are exposed via a "⋯" popover per level. Level value propagates as a default to rooms
  on the level; rooms can override.
- **Smoke `CeilingSlope` semantic = detector-rated-for** (a property of the detector type, not the room;
  the room's actual slope is already in `CeilingType ∈ {FLAT, SLOPED, STEPPED, NONE}` from extraction).
- **Catalog column names are English-only and fixed** for v1 (no localization).
- **Catalog hazard classes**: Excel + hardcoded fallback; Excel wins if present.
- **Device placement logic is still deferred** (TODO P2). The metadata is recorded on results but does
  not drive placement yet.

### Why
- Per-row selection matches the engineering reality (one room can use a pendent, another a sidewall, in
  the same model) and unblocks the seniors' direction.
- Excel-as-catalog is the senior-specified source of truth and is easier to maintain than hardcoded lists;
  the Revit model becomes a runtime availability check, not a catalog.
- Commenting (not deleting) the Revit listing preserves the ability to cross-check Excel entries against
  what is actually loaded in the model.
- Per-room spacing overrides turn the existing `MaxSpacingFt` / `BoundaryClearanceFt` placeholder values
  into a true engineer-controllable surface — closing the biggest gap in the current "all-15-ft" output.

### Evidence
- User-direction message: 2026-09-01.
- Today: `SprinklerBruteForceViewModel.SelectedSprinklerFamily` (single, universal);
  `RoomItemViewModel.HazardClassOptionsList` (per-row pattern to mirror); `ISprinklerFamilySource`
  (Revit-backed listing, to be commented out behind `UseRevitFamilyListing`).
- `DefaultHazardPlacementRules` returns uniform 15 ft placeholders — the values the per-row override
  will replace.

### Consequences
- `RoomItemViewModel` gains 4 new properties (`SelectedFamily`, `SelectedType`, `MaxSpacingFtOverride?`,
  `BoundaryClearanceFtOverride?`) plus per-row "modified"/availability indicators and a reset command.
- New `CatalogViewModel`, `CatalogBar` view, `LevelSettingsPopover`, `MissingFamiliesModal` view, and
  an `ICatalog`/`CatalogLoader` service (Backend) using ClosedXML.
- `PlacementRoomInputItem` gains `SelectedSprinklerFamily`/`SelectedSprinklerType` and the two override
  fields; `PlacementRoomInput` (Backend) mirrors them.
- `SprinklerPlacementResult` gains `SkippedMissingFamilyCount`.
- New package dependency: `ClosedXML` (Backend only).
- Catalog version is shown in the top bar; load failures (bad schema, missing required fields, unknown
  enums, duplicate `(Family, Type)`) are surfaced as a single blocking dialog at load time.

### Alternatives Considered
- *Delete the Revit family listing outright* — rejected: the runtime preflight still needs the Revit
  model to know if a family is loadable; also defeats the "verify Excel against Revit" use case.
- *One catalog per project* (forced) — rejected for v1: seniors said "I guess but not sure"; one shared
  catalog is simpler and can be split later.
- *Keep spacing overrides display-only* (no engine change) — rejected: the override would be a lie
  (Decision 018 closes this gap).
- *Localize the catalog column names* — rejected for v1: schema-stability outweighs the small UX win.

---

## Decision 018 — Per-room spacing/clearance override threads through the BruteForce engine

### Status
Accepted (plan locked, 2026-09-01). Implementation pending — not yet coded. See [[TODO]] P1 (per-room override).

### Context
[Decision 017](#decision-017--per-room-sprinkler-familytype-selection-replaces-universal-combo--excel-driven-catalog) introduced
per-row overrides for `MaxSpacingFt` (sprinkler-to-sprinkler space) and `BoundaryClearanceFt` (wall space).
The existing `BruteForceCalculationService.Calculate(snapshot, rules, config)` consumes a single
`IHazardPlacementRules` instance for the whole run; `PlacementRoomInput` carries no override fields. A
display-only override that doesn't reach the engine would be misleading and unsafe.

### Decision
- `PlacementRoomInput` (Backend) gains two nullable fields: `OverrideMaxSpacingFt`, `OverrideBoundaryClearanceFt`.
- `PlacementRoomInputItem` (UI) mirrors them.
- `BruteForceCalculationService.Calculate` now resolves the effective `HazardPlacementRuleSet` per room by
  copying the rule returned by `rules.GetRules(hazardClass)` and applying the overrides when present.
  The override is rejected (clamped + warning) if it exceeds the NFPA 13 hard-limit range for the
  hazard class; the room's result `IsProvisional` is `true` if either override was applied.
- The preflight (`EvaluateRoomEligibility`) continues to use the un-overridden rule set (NFPA compliance
  check); placement is gated on the per-room effective rule set, surfaced in `RoomCalculationResult.Diagnostics`.
- Obstacle clearance, existing-sprinkler separation, coverage radius, and any future rule values are
  **not** overridden in v1 (explicit seniors' decision; TODO P3).

### Why
- A per-row UI override that does not change the engine output is a hazard, not a feature. The seniors'
  request only makes sense if the override actually moves the calculation.
- Threading overrides through `PlacementRoomInput` (rather than a new `Func<>` parameter) is the
  minimal change to `BruteForceCalculationService` and keeps the engine signature stable.
- Keeping the preflight on the **un-overridden** rule set preserves the NFPA compliance check as the
  authoritative gate; the override is recorded explicitly per room so the engineer can see what they
  did.

### Evidence
- Today: `HazardPlacementRuleSet` (Backend, `BruteForce/`) — `MaxSpacingFt`, `BoundaryClearanceFt`,
  `ObstacleClearanceFt`, `CoverageRadiusFt`, `ExistingSprinklerSeparationFt`, `IsProvisional`.
- `DefaultHazardPlacementRules` (uniform 15 ft placeholder, `HasApprovedRules=false`).
- `BruteForceCalculationService.Calculate(PlacementInputSnapshot, IHazardPlacementRules, BruteForceCalculationConfig)`
  — one rule instance for the whole snapshot.

### Consequences
- `BruteForceCalculationConfig` and `IHazardPlacementRules` signatures are unchanged.
- The per-room rule resolution is internal to `Calculate`; callers do not need to change.
- `RoomCalculationResult.Diagnostics` gains "Override applied: MaxSpacingFt=12 (rule=15)" entries so the
  result JSON tells the truth about what spacing was used.
- `BruteForceCalculationResult.AppliedRulesSummary` becomes a per-hazard summary (existing logic) plus
  a per-room summary when overrides are in use.

### Alternatives Considered
- *Keep the override display-only* — rejected: would make the UI column a lie.
- *Add a `Func<PlacementRoomInput, HazardPlacementRuleSet>` parameter* — rejected as primary: more
  invasive than nullable fields on `PlacementRoomInput`, and the nullable fields already carry the
  information the engine needs. (A `Func<>` may still be layered on top later for fully custom rules.)
- *Allow overriding `ObstacleClearanceFt` and `ExistingSprinklerSeparationFt` too* — rejected for v1
  (per seniors' Q26 answer).
- *Make the override global (whole-run) instead of per-room* — rejected: contradicts the per-row UX
  Decision 017 mandates.

---

## Decision 019 — Per-level device metadata (Smoke Detector + Notification Appliance) is declarative-only for v1

### Status
Accepted (plan locked, 2026-09-01). Implementation pending — not yet coded.

### Context
[Decision 017](#decision-017--per-room-sprinkler-familytype-selection-replaces-universal-combo--excel-driven-catalog) introduced
per-level popover controls for `DetectorType`/`Mount`/`CeilingSlope` (Smoke) and `ApplianceType`/
`Candela` + `dBA` (Notification). The current backend has no device placement logic; only the UI shells
exist (`SmokeDetectorViewModel`, `NotificationApplianceViewModel`, `DevicePlacementViewModelBase`).

### Decision
- The new per-level fields are **recorded on the result DTO only** for v1 — no algorithmic effect on
  placement. They surface in the UI for the engineer's planning benefit and are written into the
  placement result JSON when those workflows are eventually implemented.
- `DetectorType` semantic (locked) = **detector-rated-for** (what slopes this detector is rated for),
  not the room's actual slope. The room's actual slope is already on `RoomUiData.Geometry.CeilingType`.
- `ApplianceType` + `Candela` + `dBA` are presented as a single composite enum entry in the dropdown
  (e.g., "Wall Horn-Strobe 75cd / 89dBA") to prevent impossible combinations.
- Level value **propagates as the default** to rooms on that level; rooms can override.
- Catalog source for these fields is the same universal Excel workbook; hazard classes are
  Excel + hardcoded fallback (Excel wins if present).
- Full NFPA-72 device placement logic remains TODO P2 (smoke + notification). When implemented, the
  per-level metadata from this decision will become its inputs.

### Why
- The seniors' request is forward-looking: it builds the metadata surface that the eventual device
  placement pipeline will consume. Fabricating algorithmic effect today would mean inventing NFPA-72
  rules, which is explicitly forbidden (`STANDARDS_MEMORY` rules 1–4 + Decision 004).
- The semantic distinction (detector-rated vs room-actual) prevents a redundant dropdown that would
  always show the same value the room already has.
- Composite (Candela, dBA) prevents invalid states without requiring a runtime cross-check.

### Evidence
- Today: `DevicePlacementViewModelBase` exists (`FireProtection.UI/ViewModels/Devices/`); the per-level
  parameters are not yet exposed.
- `RoomUiData.Geometry.CeilingType` (extraction) is the authoritative room-actual ceiling slope.

### Consequences
- New `LevelItemViewModel` properties on the device tabs: `SelectedDetectorType`, `SelectedMount`,
  `SelectedCeilingSlope`, `SelectedApplianceType`, `SelectedCandelaDba`.
- New per-row fields on the device room VM: the same with per-row overrides.
- No backend change in v1; DTOs gain optional fields for when the device placement logic lands.
- UI shows a "Planning only — backend pending" badge so the engineer doesn't expect algorithmic effect.

### Alternatives Considered
- *Drive smoke-detector / notification placement now* — rejected: no NFPA-72 rule source, no extractor,
  no placement service. Implementation would fabricate engineering values.
- *Treat `CeilingSlope` as the room's actual slope* — rejected: redundant with `CeilingType` from
  extraction; creates two sources of truth for the same fact.
- *Allow Candela and dBA as independent dropdowns* — rejected: enables impossible combinations and
  diverges from how real devices are specified.

---

## Decision 020 — Catalog version is shown in the top bar; load is fail-fast with structured errors

### Status
Accepted (plan locked, 2026-09-01). Implementation pending.

### Context
The Excel catalog is the source of truth for families, types, and per-level device metadata. Bad data
at load time (duplicate `(Family, Type)`, missing required fields, unknown enum values, missing
`CatalogVersion` header row) would otherwise crash later during the eligibility probe — the worst
possible place to surface catalog errors.

### Decision
- `CatalogLoader.Load(path)` is fail-fast: any validation error throws `CatalogLoadException` carrying a
  list of `CatalogIssue { Sheet, Row, Column, Code, Message }` suitable for display in a single dialog.
- The top bar shows "Catalog: `<file-name>` v`<CatalogVersion>`" when loaded; "(no catalog loaded)"
  otherwise.
- A "Reload" button next to the file name re-runs `Load` on the same path without restarting Revit.
- A "Browse..." button opens a file picker; the path is **session-scoped** (not persisted) per the
  seniors' Q7 answer.
- "Apply" of a new catalog triggers a full re-run of `RefreshEligibility` and re-binds every per-row
  family/type combo (resetting rows whose selection is no longer in the catalog and falling back to
  the new default for that row).
- Catalog `HazardClass` values fall back to the hardcoded `HazardClassOptions` list if the catalog
  omits or invalidates a value (Excel wins if present and valid).

### Why
- Fail-fast at load avoids leaking catalog errors into the eligibility probe where the root cause
  is hard to trace.
- Showing the version in the top bar is cheap; it answers "which catalog did this run use?" without a
  second file open.
- Re-binds on reload keep the UI state consistent with the source of truth.

### Evidence
- Today: no catalog; `ISprinklerFamilySource` (Revit-backed) is the only family source.

### Consequences
- New `FireProtection.Backend/Services/Catalog/CatalogLoader.cs`, `CatalogModels.cs`, `CatalogValidator.cs`.
- New UI: `FireProtection.UI/ViewModels/Catalog/CatalogViewModel.cs`, `Views/Catalog/CatalogBar.xaml`.
- A new package dependency: `ClosedXML` (Backend only; UI references the loaded catalog through the
  service interface).

### Alternatives Considered
- *Best-effort load (skip bad rows, log warnings)* — rejected: hides engineering risk; senior-signed
  direction is "fail-fast and show a popup" (Q12 answer).
- *Persist the catalog path across sessions* — rejected for v1: seniors said user selects each session
  (Q7); persistence adds a settings layer not in scope.

---

## Decision 021 — Device-kind-scoped existing-device policy + catalog-derived device attributes (2026-09-11)

### Context
Three defects found together while wiring the smoke / notification placement legs:
1. **Cross-device skip/fail.** Smoke detectors and notification appliances both live in
   `OST_FireAlarmDevices`. `FireAlarmDevicePlacementCore` collected *every* fire-alarm device as
   "existing", so a smoke run treated a room holding notification appliances as "already has devices"
   and skipped it (and `Replace` would DELETE the other kind's devices; the 0.25 ft duplicate guard
   counted the other kind too).
2. **Device attributes were user-owned.** Detector Type / Mount / Ceiling Slope and Appliance Type /
   Candela / dBA were tab+level+row dropdowns, but the catalog already carries them *per
   (family, type)* row — they are facts about the device, not choices.
3. **Sprinklers bypassed the run report.** The BruteForce tab ended with a plain text dialog while
   smoke/notification opened the new `PlacementResultReportWindow`.

### Decision
- **Kind-scoped existing devices.** `FireAlarmDevicePlacementCore` derives its own kind from the
  input items (`DeviceRoomInputItem.DeviceKind`, stamped by each tab) and, in
  `CollectExistingDevices`, attributes every found `FamilyInstance` to a kind via
  `DeviceKindResolver.TryResolve(familyName, typeName)`. **Only own-kind devices** drive the
  skip/replace policy and the duplicate guard. An *unclassifiable* name (no keyword) is never
  own-kind — conservative: a run can never skip or delete a device it cannot positively identify.
  Skip messages now name the kind ("Room already has N smoke detector(s) — other device kinds in
  this room do not count").
- **Catalog-derived attributes.** `DevicePlacementViewModelBase` gains `DeriveAttribute(key, family,
  type)` (virtual, default null) and `OnUniversalFamilyTypeChanged()`; `BuildRoomInputItem` prefers
  the value derived from **that row's own** family/type over the row-override/level-default chain.
  `SmokeDetectorViewModel` / `NotificationApplianceViewModel` implement it against
  `GetSmokeDetectorEntriesForFamily` / `GetNotificationAppliancesForFamily`. The XAML shows the
  derived value as read-only text and only keeps a picker for a field the catalog cannot derive
  (`Show*Picker` + `BoolToVis`/`InverseBoolToVis`). The per-level popover for these attributes is
  retired (`OpenLevelSettings` → base virtual no-op, `HasLevelSettings` gates the ⋯ button off).
- **Sprinkler run report.** New `SprinklerPlacementReportMapper` (UI, Revit-free) folds
  `SprinklerPlacementResult` + `BruteForceCalculationResult` into the shared `PlacementRunReport`;
  the BruteForce tab opens the same `PlacementResultReportWindow`. Verdicts follow device semantics
  (Failed / ReviewRequired / Skipped / Success); duplicates, outside-room refusals and spatially
  INVALID instances surface in the issue list; provisional rules force a ReviewRequired verdict.
- **Honesty guards restored.** `DefaultHazardPlacementRules` had its uncommitted
  `HasApprovedRules`/`IsProvisional` flipped to "approved" with no FPE sign-off on record anywhere in
  the repo — reverted to `false`/`true` (the values are kept, only the *claim* is fixed; notes now
  read "provisional … NOT yet AHJ/FPE-verified"). Device core: calc-level `ReviewRequired` is no
  longer clobbered to "Success" when placements happen to be spatially valid; a cancelled run reports
  `OverallStatus="Cancelled"` with a real summary instead of a zeroed "Success".
- **Two missing placement guards ported** from the sprinkler service into the device core: the
  outside-room coordinate-space guard (`OUTSIDE_ROOM_BOUNDARY`-equivalent refusal before creating an
  element) and, sprinkler-side, a C-E grid-truncation warning (a room whose candidate sweep hits the
  `MaxCandidatePoints` cap now flags ReviewRequired instead of silently leaving the rest of the room
  unsampled).

### Evidence
- BUILD-VERIFIED: solution Revit2026 + Revit2025, 0 errors.
- TEST-VERIFIED: `FireProtection.Tests` all green, incl. new `DeviceReportAndKindTests` (mapper
  verdicts + smoke-vs-notification kind separation).
- RUNTIME-UNVERIFIED as ever — no Revit host here. The kind attribution is *name-based* because
  Revit's category cannot distinguish the two; the live test is "place smoke → place notification in
  the same room → neither skips nor deletes the other".

### Consequences
- A mis-named family (no "smoke"/"strobe"/"horn"/… in family or type name) is invisible to the
  policy: it will not trigger SkipRoom and will not be replaced. That is deliberate (never destroy
  what you cannot identify); the message path still reports the room's outcome.
- `DeviceKindResolver` is now load-bearing (was dead code); its `Resolve()` fallback-to-smoke remains
  for diagnostics, `TryResolve` is the policy-safe entry point.
