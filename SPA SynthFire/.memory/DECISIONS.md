# SPA SynthFire — Architectural & Engineering Decision Records (ADRs)

> This document records key architectural, engineering, and design decisions made for **SPA SynthFire**. Any AI model or engineer reading this can instantly understand why the codebase is structured this way and the rationale behind critical rules.

---

## Decision 001 — Standalone Clean-Room Architecture (Zero Legacy Dependency)
- **Status**: Accepted
- **Context**: The repository had a sibling experimental project (`FireProtectionSystem`) with mixed concerns, legacy Windows Framework references, and complex tangled extraction/solvers.
- **Decision**: SPA SynthFire is developed completely standalone from scratch (`SPA.SynthFire.*`) targeting **.NET 8** and modern **Revit 2026/2025**. No code or binary references from `FireProtectionSystem` are imported.
- **Consequences**: Fast compile cycles, clean separation of concerns, zero legacy technical debt, and 100% control over design patterns.

---

## Decision 002 — Complete Revit-Decoupled Core and Placement Engine
- **Status**: Accepted
- **Context**: Revit API binaries (`RevitAPI.dll`, `RevitAPIUI.dll`) cannot be run in standard CI or headless test runners without a running Autodesk Revit process.
- **Decision**:
  - `SPA.SynthFire.Core` (domain, geometry, standards, settings) and `SPA.SynthFire.Placement` (solvers, layout math, clash shifting) have **zero reference** to Revit assemblies.
  - All room polygons, heights, and obstacles are represented as POCOs (`RoomInput`, `Rectangle2D`, `Polygon2D`, `ObstacleBox`).
- **Consequences**:
  - Entire suite of placement, standard compliance, and obstacle tests (158+ tests) executes headless in < 2 seconds via `dotnet test`.
  - Layout logic can be reused in web APIs, CLI tools, or cloud workers without Autodesk licenses.

---

## Decision 003 — Internal Unit Normalization to Decimal Feet (`ft` / `sq ft`)
- **Status**: Accepted
- **Context**: Revit's internal database stores all length coordinates as Imperial decimal feet. Standards like AS 2118 are metric ($m, m^2$), while NFPA 13 uses both ($ft$ and $m$).
- **Decision**:
  - Standard internal coordinate system for all calculation models is decimal **feet** and **square feet**.
  - All standard values (metric or imperial) are converted into decimal feet at standard definition time using `Units.cs`.
- **Consequences**:
  - Eliminates unit conversion bugs during element placement in Revit.
  - Math is consistent across AS 2118, NFPA 13, and NFPA 72.

---

## Decision 004 — Unified Grid & Obstacle Engine (`CeilingGridLayout`)
- **Status**: Accepted
- **Context**: Sprinkler heads (wet fire) and ceiling-mounted detectors (dry fire smoke/heat) share almost identical grid placement rules: maximum spacing, area-per-device limits, wall standoffs, and obstacle avoidance.
- **Decision**:
  - Created a single reusable layout engine in `CeilingGridLayout.cs`.
  - Both `SprinklerPlacementSolver` and `Nfpa72DetectionRules` delegate their grid generation to `CeilingGridLayout.Compute`.
- **Consequences**:
  - Bug fixes or improvements to grid math, 8-way obstacle nudging, or tile snapping instantly benefit both fire protection and fire alarm disciplines.

---

## Decision 005 — Rule Gating: Provisional Until Approved & One-Way Tightening
- **Status**: Accepted
- **Context**: Life safety code calculations must not be misrepresented as certified unless vetted by a licensed Fire Protection Engineer (FPE). Furthermore, custom user overrides must not violate building code minimums.
- **Decision**:
  - Every standard rule set carries `IsProvisional = true` until officially signed off.
  - `DesignCriteriaResolver`: User criteria overlays can **only tighten** the rules (reduce spacing or reduce area per head). Attempting to loosen/relax code requirements beyond the standard is blocked.
- **Consequences**:
  - Safe engineering posture. All reports clearly indicate if a calculation uses provisional rules.

---

## Decision 006 — Obstacle Avoidance via 8-Way Search with Review Gating
- **Status**: Accepted
- **Context**: MEP ductwork, cable trays, structural framing, and large pipes run across ceilings and clash with sprinkler spray patterns and smoke detector sensor fields.
- **Decision**:
  - `CeilingGridLayout` tests each nominal grid point against obstacle bounding boxes (expanded by a $0.5\text{ ft}$ clearance buffer).
  - If a clash occurs, the solver attempts an 8-way directional nudge (cardinal + diagonal) in search increments up to max step.
  - If a clear point inside the room standoff boundary is found, status is `ClashState.Nudged`.
  - If no clear location can be found, point remains at target coordinate and flags `ClashState.Unresolved` and `ReviewRequired = true`.
- **Consequences**:
  - Avoids losing heads or leaving unprotected pockets while preventing silent failures. Engineering review is explicitly requested.

---

## Decision 007 — Idempotent Redesign & Re-run Tracking via Comments Tag
- **Status**: Accepted
- **Context**: Architectural changes require frequent redesigns. Running placement multiple times previously resulted in stacked, duplicated heads.
- **Decision**:
  - All elements placed by SynthFire have their Revit parameter `BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS` set to `"SPA SynthFire"`.
  - When re-running placement on a room or level, the placer first identifies and removes prior heads matching this comment tag before writing new instances.
- **Consequences**:
  - "One-button redesign" is safe, clean, and avoids phantom duplicates.

---

## Decision 008 — Explicit Level & Elevation Parameter Enforcement (`PlacementLevels`)
- **Status**: Accepted
- **Context**: In Revit, creating unhosted family instances or sidewall heads on upper stories often defaulted their host level parameter to Level 1, causing elements to render at $Z=0$ or incorrect floor heights.
- **Decision**:
  - `PlacementLevels.Apply` explicitly commits both the `FAMILY_LEVEL_PARAM` and `INSTANCE_FREE_HOST_OFFSET_PARAM` (or `SCHEDULE_LEVEL_PARAM`) to match the room's native level elevation.
- **Consequences**:
  - Multi-story placement functions reliably without heads sinking to ground floor.

---

## Decision 009 — Architectural Ceiling Tile Center Snapping
- **Status**: Accepted
- **Context**: Commercial spaces with acoustic ceiling tile (ACT) grids require heads centered in tiles for aesthetic and constructability standards.
- **Decision**:
  - When ceiling grid data is detected, heads snap to tile center points via `SnapToTileCenter` $(Origin + (k + 0.5) \times Module)$.
  - Snapped points are rejected and fall back to pure grid if they violate the room boundary standoff ($4\text{ inches}$).
- **Consequences**:
  - Clean visual alignments in ceiling plan views while preserving edge clearance.
