# Candidate Location Point Identification — All Three Devices (In Depth)

> **Source of truth:** the C# source in this repository. Where older docs disagree with code, **code wins** (see [§9 Doc discrepancies](#9-doc-vs-code-discrepancies-known)).
>
> **Status legend used throughout:**
> - `[IMPLEMENTED]` — logic exists in source and is structurally provable by reading it.
> - `[PROVISIONAL]` — rule **values** are placeholders (`HasApprovedRules = false` / `IsProvisional = true`); every run is forced `ReviewRequired`. Not AHJ/FPE-signed.
> - `[STATIC-ONLY]` — compiles and passes headless tests; **not** runtime-verified against a live Revit host in this environment.
>
> This document covers the **full candidate-location pipeline** for each device: rule resolution → height/Z → candidate generation → filters → final selection → post-checks → edge cases. Revit element creation is covered only where it feeds back into identification (existing-device policy, kind scoping, duplicate/outside-room guards).

---

## Table of contents

1. [Shared foundation (all 3 devices)](#1-shared-foundation-all-3-devices)
2. [Sprinklers](#2-sprinklers)
3. [Smoke detectors](#3-smoke-detectors)
4. [Notification appliances](#4-notification-appliances)
5. [Side-by-side comparison](#5-side-by-side-comparison)
6. [Family / type / mount influence matrix](#6-family--type--mount-influence-matrix)
7. [Edge-case catalogue (all devices)](#7-edge-case-catalogue-all-devices)
8. [Class & method index](#8-class--method-index)
9. [Doc vs code discrepancies (known)](#9-doc-vs-code-discrepancies-known)

---

## 1. Shared foundation (all 3 devices)

### 1.1 Plain-English overview of the shared idea

Every device in this project finds its spots the same way a person would tape out a room:

1. **Draw the room** on paper (the exact wall outline, including holes for columns).
2. **Decide how far apart** devices must be (that number comes from a rules table, different for sprinklers vs detectors vs alarms).
3. **Decide the height** (usually the ceiling underside).
4. **Lay a fine mesh of possible dots** across the floor plan (or walk the walls for wall-mounted devices).
5. **Throw away bad dots** — outside the room, too close to a wall, inside a beam/column/duct, on top of an existing device.
6. **Keep only the dots you need** — start from a sensible order, skip dots already “covered” by a neighbor, never put two devices closer than the minimum.
7. **Audit the result** — max gap, max wall distance, coverage holes, and flag anything uncertain for human review.

Sprinklers, smoke detectors, and notification appliances each bring a **different rules table and a slightly different step 4/6**, but steps 1, 3, 5, and 7 are the same machinery.

### 1.2 Shared pipeline (code view) `[IMPLEMENTED]`

```
UI selection (rooms, family/type, overrides)
        │
        ▼
Placement input builder          PlacementInputBuilder.Build          (sprinklers)
                                 SmokeDetectorPlacementInputBuilder   (smoke)
                                 NotificationAppliancePlacementInputBuilder (NA)
        │  per-room: boundary polygon, ceilings, obstacles,
        │  existing devices, hazard/mount/spacing fields
        ▼
Rule provider.GetRules(...)      → HazardPlacementRuleSet
                                  SmokeDetectorPlacementRuleSet
        │  + per-room overrides (ApplyPerRoomOverrides)
        ▼
SelectPrimaryCeiling → placementZ
        │
        ▼
DeviceLocationPointIdentifier.Resolve*Profile   → strategy label + profile (diagnostics)
        │
        ▼
Candidate generation             ceiling grid  OR  wall/perimeter walk
        │
        ▼
Filters (inside room, boundary clearance, obstacle+Z, existing separation)
        │
        ▼
Final selection                 centered grid | greedy | single-device center | sidewall solver
        │
        ▼
Post-checks + status            ReviewRequired / Failed / MissingCeiling / …
```

### 1.3 Shared geometry helpers

| Concern | Class | Method | File |
|---|---|---|---|
| Room shape (outer + holes) | `RoomGeometry` | ctor, `IsPointInsideRoom`, `DistanceToOuterBoundary`, `IsDegenerate`, `MinX/MaxX/MinY/MaxY` | `FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/RoomGeometry.cs` |
| Point-in-polygon (even-odd ray cast + edge tolerance) | `GeometryMath` | `PointInPolygon` | `…/BruteForce/GeometryMath.cs` |
| Distance point→segment | `GeometryMath` | `Distance`, `DistancePointToSegmentSquared` | same |
| Expanded obstacle box test | `GeometryMath` | `InsideExpandedBox` | same |
| Obstacle Z-span guard | `ObstacleBox` (device engine) / sprinkler obstacle box | `SpansZ` | `…/SmokeDetectors/Final/BruteForce/ObstacleBox.cs` |
| Ceiling pick | both engines | `SelectPrimaryCeiling` | `BruteForceCalculationService.cs`, `SmokeDetectorCalculationService.cs` |
| Per-room override clone + clamp | both engines | `ApplyPerRoomOverrides` | same two files |
| Fine-grid step | both engines | `ComputeGridResolution` | same two files |
| Per-room isolation | both engines | `Calculate` wraps each room in try/catch → `Failed` (Decision 009) | same |

**Coordinate rule:** all polygons/points are already **host-MEP feet**, normalized once at extraction (`RevitModelContext.TransformPoint`). Calculation and placement apply **no second transform** (Decisions 002/008).

### 1.4 The location-point strategy layer (all 3) `[IMPLEMENTED]`

**File:** `FireProtection.Backend/Services/Placement/LocationPoints/DeviceLocationPointIdentifier.cs`

This class **labels** which identification strategy a room will use and freezes a diagnostic profile (strategy name, mount, grid step, min spacing, coverage radius, placement Z, human-readable `SelectionBasis`). It is called from:

| Caller | Method | Strategy chosen |
|---|---|---|
| `BruteForceCalculationService.CalculateRoom` (~line 373) | `ResolveSprinklerProfile` | `sprinkler-ceiling-grid` or `sprinkler-sidewall-edge-grid` |
| `SmokeDetectorCalculationService.CalculateRoom` (~line 252) | `ResolveSmokeDetectorProfile` | `smoke-detector-ceiling-grid` or `smoke-detector-wall-mounted` |
| `SmokeDetectorCalculationService.CalculateRoom` (~line 244) | `ResolveNotificationApplianceProfile` | `notification-appliance-ceiling-grid` or `notification-appliance-wall-mounted` |

Branch predicates:

```csharp
// Sprinkler — sidewall if behavior OR orientation says wall
bool isSidewall =
    string.Equals(placementBehavior, "WallSidewall", StringComparison.OrdinalIgnoreCase)
    || string.Equals(orientation, "sidewall", StringComparison.OrdinalIgnoreCase);

// Smoke / Notification — wall if mount says Wall (or WallSidewall)
bool isWallMount =
    string.Equals(mount, "Wall", StringComparison.OrdinalIgnoreCase)
    || string.Equals(mount, "WallSidewall", StringComparison.OrdinalIgnoreCase);
```

**Important:** the profile is written into `RoomCalculationResult.Diagnostics` but **does not itself branch the generation code**. The engines re-derive the same `isSidewall` / `isWallMount` / `deviceMode` booleans locally and call the corresponding `Generate*` method. Treat `DeviceLocationPointIdentifier` as the **named strategy registry + audit trail**, not a separate dispatcher.

Enum values (`DeviceLocationPointStrategy`): `SprinklerCeilingGrid`, `SprinklerSidewall`, `SmokeDetectorCeilingGrid`, `SmokeDetectorWallMounted`, `NotificationApplianceCeilingGrid`, `NotificationApplianceWallMounted`.

### 1.5 Shared config knobs

`BruteForceCalculationConfig` (`…/BruteForce/BruteForceCalculationConfig.cs`):

| Field | Default | Role |
|---|---|---|
| `ToleranceFt` | `1e-6` | floating-point slack on all inside/boundary/clearance tests |
| `GridResolutionFt` | `1.0` | starting fine-grid step (both engines adapt it) |
| `MaxCandidatePoints` | `4000` | hard cap on generated candidates per room |
| `MaxSearchIterations` | `500000` | greedy selection loop cap |

---

## 2. Sprinklers

### 2.1 Plain-English explanation (non-technical)

**What we are doing:** deciding where every sprinkler head in a selected room should sit.

**How, in everyday terms:**

1. **Read the room’s hazard class** (Light, OH1, OH2, EH1, EH2 — from the room name or user override). That class looks up a spacing table: how far apart heads may be, how much floor one head covers, how far from walls, how close to beams. *Today those numbers are provisional placeholders, so every result is marked “needs review.”*
2. **Pick the ceiling** that truly belongs to this room (flat preferred over sloped over stepped; must be on the same floor; never a slab below the floor). The **height of that ceiling** becomes the Z (height) of every candidate point. If there is no ceiling, we fall back to “floor + ceiling height,” or just the floor, and flag the room.
3. **Shrink the allowed spacing** when the room is tall or the ceiling is sloped (Light Hazard height table; slope factors). Sidewall heads also get a tighter spacing and half the circular coverage (they spray a half-circle).
4. **Choose a layout style from the family/type you picked:**
   - **Ceiling heads (pendent/upright):** lay a fine net of possible points over the room, then place heads on a **centered rectangular array** whose spacing equals the allowed S→S value (this is what the UI “S→S” box controls).
   - **Sidewall heads:** do **not** use the ceiling net. Walk each wall, step heads along the wall at the allowed spacing, aim the spray into the room, and add more walls only if one wall cannot reach across.
5. **Reject bad points:** outside the room outline, too close to a wall (min clearance), inside or too close to a beam/column/duct **at ceiling height** (a low beam does not block a ceiling head), or sitting on top of an existing sprinkler.
6. **Enforce minimum head-to-head spacing** so heads never bunch.
7. **Audit:** largest gap between heads, any head too far from a wall, average area per head, coverage holes on a re-scan, grid cut off by the safety cap. Anything wrong → **ReviewRequired**, not silent success.
8. **Honesty:** if the family is unsupported, or the room has no ceiling and the family needs one, we **refuse** to invent points (`InvalidInput` / `MissingCeiling`) instead of emitting doomed coordinates.

**What actually decides the final X/Y/Z:** hazard rules (after height/slope/orientation/override adjustments) → room outline → ceiling Z → centered grid (or sidewall solver) → filters → min-spacing → post-audit.

### 2.2 Technical deep-dive

**Engine:** `BruteForceCalculationService` (static, Revit-free)  
**File:** `FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/BruteForceCalculationService.cs`  
**Entry:** `Calculate(PlacementInputSnapshot, IHazardPlacementRules, BruteForceCalculationConfig)` → per-room `CalculateRoom`.

#### 2.2.1 Rule resolution (per room)

```csharp
HazardClass hazardClass = ParseHazardClass(room.EffectiveHazardClass, result);
HazardPlacementRuleSet baseRuleSet = rules.GetRules(hazardClass);          // DefaultHazardPlacementRules
HazardPlacementRuleSet catalogRuleSet = MergeTypeRuleValues(baseRuleSet, room, result); // optional per-type Excel fields
HazardPlacementRuleSet ruleSet = ApplyPerRoomOverrides(catalogRuleSet, room, result);
if (ReferenceEquals(ruleSet, baseRuleSet)) ruleSet = baseRuleSet.Clone();
```

- **Provider:** `DefaultHazardPlacementRules.GetRules` — `[PROVISIONAL]` `HasApprovedRules => false`; every set `IsProvisional = true`.

| Hazard | MaxSpacingFt | MinSpacingFt | CoverageRadiusFt | MaxCoverageAreaSqFt | BoundaryClearanceFt | ExistingSeparationFt | MaxDistanceFromWallsFt |
|---|---|---|---|---|---|---|---|
| Light | 15 | 6 | 7.5 | 225 | 1.0 | 7.5 | 7.5 |
| OH1 | 12 | 6 | 6.0 | 130 | 1.0 | 6.0 | 6.0 |
| OH2 | 12 | 5 | 5.0 | 100 | 1.0 | 5.0 | 5.0 |
| EH1 | 10 | 4.5 | 4.5 | 90 | 1.5 | 4.5 | 4.5 |
| EH2 | 10 | 4.5 | 4.5 | 90 | 2.0 | 4.5 | 4.5 |

- **Unrecognized / missing hazard** → default Light + warning + `ReviewRequired` (`ParseHazardClass`).
- **ESFR/CMSA or EH without listed coverage** → forced `ReviewRequired` (storage design note).
- **Per-room overrides** (`OverrideMaxSpacingFt`, `OverrideBoundaryClearanceFt`): `ApplyPerRoomOverrides` clones the base rule, clamps spacing to the provisional ceiling, marks `IsProvisional`, adds a diagnostic (Decision 018).

#### 2.2.2 Ceiling selection and Z

`SelectPrimaryCeiling(room, out reason, result)` priority:

1. Candidates = ceilings with `BottomElevationFt` **above floor** (`> LevelElevationFt + 0.05`).
2. Pass 0 (level match) then pass 1 (any level): slope order **FLAT → SLOPED → STEPPED**; within a bucket, highest `BottomElevationFt`.
3. Last resort: highest ceiling of any slope. Never a ceiling at/below floor (that is the slab below).

Z:

| Case | `placementZ` |
|---|---|
| FLAT ceiling | `BottomElevationFt` |
| SLOPED ceiling | `(Bottom + Top) / 2` (centroid height; diagnostic logged) |
| No ceiling, `CeilingHeightFt` present | `LevelElevationFt + CeilingHeightFt` + provisional note |
| No ceiling, no height | `LevelElevationFt` + provisional note |

Then **hosting block:** if no ceiling at all **and** `RequiresCeilingHost(behavior)` (CeilingOverhead / FaceHosted / WorkPlaneDependent / Unknown / Unsupported) → **hard block** `MissingCeiling`, zero points (level-hosted and sidewall exempt).

#### 2.2.3 Spacing adjustments before generation

```csharp
// Light Hazard height table (NFPA 13 §11.1 style) — only MaxSpacingFt reduced
height > 25 → 0.80;  >20 → 0.85;  >15 → 0.90;  >12 → 0.95;  else 1.0

// Slope (FLAT 1.0, SLOPED 0.9, STEPPED 0.85) × class CeilingHeightAdjustmentFactor
combinedFactor = heightFactor * slopeFactor * ruleSet.CeilingHeightAdjustmentFactor;
if (combinedFactor < 1.0) ruleSet.MaxSpacingFt *= combinedFactor;

// Orientation (pendent/upright 1.0; sidewall 0.85 on MaxSpacingFt)
// Sidewall also: CoverageRadiusFt /= √2  (half-circle pattern)
```

#### 2.2.4 Branch: ceiling grid vs sidewall

```csharp
bool isSidewall =
    room.SelectedSprinklerPlacementBehavior == DevicePlacementBehavior.WallSidewall
    || string.Equals(room.SelectedSprinklerOrientation, "sidewall", StringComparison.OrdinalIgnoreCase);

var sprinklerLocationProfile = DeviceLocationPointIdentifier.ResolveSprinklerProfile(...);
// diagnostics: "Location-point strategy=sprinkler-ceiling-grid|sprinkler-sidewall-edge-grid, …"

if (isSidewall)
    return SelectSidewallDirectional(...);   // no fine-grid path
```

`DevicePlacementBehavior` comes from `PlacementInputBuilder` / `DevicePlacementBehaviorResolver` (Revit `FamilyPlacementType` + catalog `Mount`): `FaceBased→FaceHosted`, `WorkPlaneBased→WorkPlaneDependent` (refined by mount to `WallSidewall` or `CeilingOverhead`), `OneLevelBased→LevelHosted`, else `Unsupported`.  
`Unsupported` → fail-fast `InvalidInput` before any candidates.

#### 2.2.5 Ceiling path — fine grid → centered array

**Step A — fine grid (`ComputeGridResolution` + sweep):**

```csharp
double res = config.GridResolutionFt;                    // 1.0
if (res > ruleSet.CoverageRadiusFt) res = CoverageRadiusFt;
// floor step at CoverageRadius/2 when room is ≥ 2×floor in BOTH axes
// double res while estimate > MaxCandidatePoints (≤64 times), cap CoverageRadius*4 / 25 ft

for (double y = geometry.MinY; y <= geometry.MaxY + tol; y += gridRes)
  for (double x = geometry.MinX; x <= geometry.MaxX + tol; x += gridRes) { … }
```

**Filters per point (in order):**

1. `!geometry.IsPointInsideRoom(x, y, tol)` → reject `outside`
2. `DistanceToOuterBoundary < BoundaryClearanceFt - tol` → reject `boundary`
3. `InsideExpandedBox(…, box.ClearanceFt)` **and** `box.SpansZ(placementZ, tol)` → reject `obstacle`  
   (per-category clearance via `ObstacleSpecificClearances`: beam/column/duct; no Z info ⇒ `SpansZ` true = legacy behavior)
4. `Distance to any existing sprinkler ≤ ExistingSprinklerSeparationFt - tol` → reject `existing`

**Truncation honesty (C-E guard):** if `generated >= MaxCandidatePoints` **and** full sweep would have been larger → warning + `ReviewRequired` (“part of the room was never sampled”).

**Zero valid candidates** → `NoValidCandidates` + error.

**Step B — `SelectCenteredGrid` (industry array, not pure greedy):**

```csharp
// n = ceil(dim / S); step = dim / n (≤ S); first line at step/2 from min edge
int nx = width  <= spacing ? 1 : ceil(width  / spacing);
int ny = height <= spacing ? 1 : ceil(height / spacing);
double stepX = width / nx, stepY = height / ny;

for each cell center (Min + step*(i+0.5)):
    if IsPlacementValid(target) → place exact
    else snap to nearest UNUSED fine-grid candidate within captureRadius = max(step/2, gridRes)
    if none reachable → skip cell
    enforce MinSpacingFt against already-selected → else skip
```

`IsPlacementValid` re-checks inside-room, boundary clearance, obstacles+Z, existing separation.  
Empty selection after a non-empty candidate set → **hard `Failed`** (honest, not soft review).

**Post-checks — `FinalizeSelection` (shared with sidewall):**

| Check | Trigger | Status |
|---|---|---|
| Pairwise max spacing | any pair `> MaxSpacingFt` | `ReviewRequired` |
| Max distance from wall | any head `> MaxDistanceFromWallsFt` | `ReviewRequired` |
| Per-head coverage area | `room.Area / count > MaxCoverageAreaSqFt` | `ReviewRequired` |
| Coverage-gap re-sample | coarse grid; uncovered % reported; `>5%` | `ReviewRequired` |
| Provisional rules | `ruleSet.IsProvisional` | always `ReviewRequired` |
| Ceiling unsupported note | fallback Z | warning (status may already be Missing/Unsupported ceiling) |

`RequiredCount` (estimate only) = `ceil(room.Area / (π r²))`.  
`CalculatedCount` = actual selected points.

#### 2.2.6 Sidewall path — `SelectSidewallDirectional`

Two stages:

1. **Legacy perimeter walk `GenerateSidewallCandidates`** (still in source; the production branch now calls the directional solver first): each outer edge → inward normal (winding + interior probe at 0.1/0.25/0.5 ft) → step along edge with end clearance → project **0.5 ft inboard** (`standOff`) → filters: inside room, obstacle+Z, existing separation. **Boundary-clearance lower bound intentionally NOT applied** (heads belong near the wall). Each point keeps `WallEdgeIndex`.
2. **Directional solver `SelectSidewallDirectional`:**
   - Along-wall spacing `S` = adjusted `MaxSpacingFt`.
   - Throw depth `D` = listed `TypeMaxCoverageAreaSqFt / S` (clamped to hazard ceiling), else **assumed square `D = S`** (flagged).
   - End-wall max = `S/2`; short edges get a single centered head; min-spacing collapses over-tight steps to one centered head.
   - Head = edge sample + 0.5 ft inward; reject outside room; reject inside obstacle solid; **clearance only for obstacles in the throw path** (wall behind head does not reject).
   - Sample interior on a coarse grid; existing sprinklers pre-cover within `D` (circular approximation).
   - **Greedy by wall-row gain:** repeatedly take the unused wall row that covers the most still-uncovered samples until no gain.
   - Always `ReviewRequired` (+ notes for assumed-square throw, residual interior gap, provisional values).

#### 2.2.7 Family / type influence on identification `[IMPLEMENTED]`

| Input | Effect on candidate identification |
|---|---|
| Catalog `Mount` (Pendent/Upright/Sidewall/Recessed) | Via `PlacementInputBuilder` → `SelectedSprinklerOrientation`; sidewall forces `WallSidewall` branch |
| Revit `FamilyPlacementType` | → `DevicePlacementBehavior`; `Unsupported` kills the room; hosted behaviors require ceiling for eligibility/blocked calc |
| Catalog optional per-type numbers (`MaxSpacingFt`, `MaxCoverageAreaSqFt`, sidewall listing fields, …) | `MergeTypeRuleValues` merges before per-room overrides; still bounded by hazard ceiling |
| Per-row family/type | Carried on `PlacementRoomInput.SelectedSprinklerFamilyName/TypeName`; missing family handled at placement probe (`ProbeMissingFamilies`), not during point generation |
| Per-row spacing overrides | Enter engine as `OverrideMaxSpacingFt` / `OverrideBoundaryClearanceFt` → `ApplyPerRoomOverrides` |

Any family/type the engine cannot resolve to a supported behavior never reaches candidate generation.

#### 2.2.8 Sprinkler edge cases (explicit)

| Edge case | Behavior | Where |
|---|---|---|
| Degenerate boundary (<3 pts) | `InvalidRoomGeometry` | `CalculateRoom` |
| Missing/unrecognized hazard | Light + `ReviewRequired` | `ParseHazardClass` |
| Unsupported family behavior | `InvalidInput`, 0 points | early return |
| No ceiling + hosted family | `MissingCeiling` blocked | `RequiresCeilingHost` block |
| No ceiling + level-hosted / sidewall | provisional Z + review | ceilingUnsupported block |
| Sloped ceiling | Z averaged; slope factor shrinks S | Z + combinedFactor |
| Room too large for 4000-point cap | truncation warning + `ReviewRequired` | C-E guard |
| All candidates rejected | `NoValidCandidates` | after sweep |
| Centered grid places nothing | hard `Failed` | `SelectCenteredGrid` |
| Obstacle without Z | treated as spanning (blocks) | `SpansZ` default |
| Low beam under ceiling | does **not** block ceiling head | `SpansZ` false |
| Existing sprinkler nearby | reject during generation; also coverage-skip during greedy | separation + coverage radius |
| Concave / L-shaped / holes | polygon tests handle | `RoomGeometry` |
| Sidewall room too deep for any wall | residual gap % warning + `ReviewRequired` | directional solver |
| Grid coarsened by cap | honest truncation warning | C-E guard |
| ESFR/CMSA / EH unlisted | `ReviewRequired` storage note | early in `CalculateRoom` |
| Per-room override out of range | clamped + diagnostic | `ApplyPerRoomOverrides` |
| Linked-model room | coords already host-MEP; no second transform | Decision 002 |

---

## 3. Smoke detectors

### 3.1 Plain-English explanation (non-technical)

**What we are doing:** deciding where smoke detectors go in each room so smoke is caught early, without stacking detectors or ignoring beams, slopes, and airflow.

**How, in everyday terms:**

1. **The rules are not about room “hazard” like sprinklers.** They depend on **what kind of detector** (photoelectric, ionization, heat, beam, …), **how it mounts** (ceiling or wall), **how the ceiling slopes**, and **how much the air moves** (air changes per hour).
2. **Default smooth-ceiling numbers (provisional):** about **30 ft** max between detectors, **10 ft** minimum, **900 sq ft** max per detector, never closer than **~4 in** to a wall, and no more than **15 ft** from a wall.
3. **Airflow shrinks coverage:** the more the room is ventilated, the smaller the protected area per detector (900 → down toward 125 sq ft at very high air change rates). Spacing becomes √(area).
4. **Sloped/stepped ceilings shrink spacing** (about ×0.90 / ×0.85). **Wall mount** shrinks spacing again (×0.90) and allows the detector almost flush to the wall (0.05 ft), sitting about **0.5 ft below the ceiling**.
5. **Height (Z):** same ceiling picker as sprinklers — flat ceiling underside, average of a slope, or floor+height fallback with a review flag.
6. **Where points are allowed:**
   - **Ceiling mount:** fine mesh over the room, then **one detector at the room center** if the room is small enough to need only one; otherwise pick candidates **nearest the center first**, then spread by coverage/min-spacing rules.
   - **Wall mount:** walk the walls, step at the allowed spacing, nudge **0.15 ft off the wall**, Z dropped below ceiling.
   - **Sloped ceiling extra row:** also generate candidates within **3 ft of the high (peak) zone** at `peak − 3 ft` (smoke pools at the high side).
7. **Same bad-point rejects as everyone:** outside room, wall clearance, obstacle at that height (HVAC supply grilles get extra 3 ft clearance), existing detectors too close.
8. **Beam detectors are special:** if the room’s longest dimension is shorter than the minimum listed beam path (min spacing, e.g. 15 ft), we **refuse** the room and tell the user to pick a point-type detector — we do not fake a beam layout.
9. **Small-room fast path:** if one detector’s coverage area already covers the room and the room’s longest side is short, place **exactly one** detector as close to the center as the valid mesh allows.
10. **Audit / honesty:** provisional rules ⇒ every room needs review; empty mesh ⇒ explicit failure; unsupported family behavior ⇒ explicit refusal.

**What actually decides the final X/Y/Z:** detector type + mount + slope + airflow rules → ceiling Z → (ceiling mesh **or** wall walk **or** peak row) → filters → single-center or center-first greedy → counts.

### 3.2 Technical deep-dive

**Engine:** `SmokeDetectorCalculationService` (also drives notification appliances via mode flag)  
**File:** `FireProtection.Backend/Services/Placement/SmokeDetectors/Final/BruteForce/SmokeDetectorCalculationService.cs`  
**Entry:** `Calculate(SmokeDetectorPlacementInputSnapshot, ISmokeDetectorPlacementRules, BruteForceCalculationConfig)`  
**Rules:** `Nfpa72SmokeDetectorRules` — `[PROVISIONAL]` `HasApprovedRules => false`; every set `IsProvisional = true`.

#### 3.2.1 Rule resolution

```csharp
string effectiveCeilingSlope = room.CeilingSlope
    ?? bestCeiling?.SlopeType;   // room field wins; else primary ceiling

SmokeDetectorPlacementRuleSet baseRuleSet = rules.GetRules(
    room.DetectorType, room.Mount, effectiveCeilingSlope, room.AirChangesPerHour);

SmokeDetectorPlacementRuleSet ruleSet = ApplyPerRoomOverrides(baseRuleSet, room, result);
```

`Nfpa72SmokeDetectorRules.GetRules` order of operations:

1. Defaults: type `Photoelectric`, mount `Ceiling`, slope `FLAT`.
2. **Base smooth ceiling:** `MaxSpacing=30`, `MinSpacing=10`, `MaxCoverageArea=900`, `CoverageRadius=30/√2≈21.213`, `MaxDistanceFromWalls=15`, `MinBoundaryClearance=0.333`.
3. **Beam type** (name contains “Beam”): `60 / 15 / 3600 / radius 30 / wall 30 / boundary 0.5`.
4. **Airflow** `ApplyAirflowAdjustment` (only if ACH > 7.5): table → area; then `MaxSpacing=√area`, `CoverageRadius=√(area/2)`, `MaxDistanceFromWalls=spacing/2`.

   | ACH ≥ | Area sq ft |
   |---|---|
   | 60 | 125 |
   | 30 | 250 |
   | 20 | 375 |
   | 15 | 500 |
   | 12 | 625 |
   | 10 | 750 |
   | 8.6 | 875 |
   | (else >7.5) | 900 |

5. **Slope** `ApplySlopeAdjustment`: SLOPED/PEAKED → spacing ×0.90, area ×0.81 (floor 125); STEPPED → ×0.85, area ×0.7225 (floor 125); recompute radius/wall distance.
6. **Wall mount last:** `MinBoundaryClearance=0.05`, spacing ×0.90, area `max(125, ×0.85)`, radius `spacing/√2`, wall distance `spacing/2`.

Per-room override (`ApplyPerRoomOverrides`, ~line 1373): clone; clamp spacing to **[5, 30]**; recompute radius/wall/coverage; boundary ≥ 0.333; force `IsProvisional` + `ReviewRequired` + warning.

#### 3.2.2 Ceiling / Z / early exits

- `SelectPrimaryCeiling`: same pattern as sprinklers but slope priority includes **PEAKED**: `FLAT → SLOPED → PEAKED → STEPPED`.
- Z: flat bottom / sloped average / `level+height` / last resort **`level + 10 ft`** (device engine default differs from sprinkler’s floor-level fallback) → `ceilingUnsupported` + review note.
- **Unsupported placement behavior** → `InvalidInput`, 0 points (mirrors sprinklers).
- **Beam path-length check (NFPA 72 §17.7.3.7):** if type/family name contains “Beam” and `max(room W, room H) < MinSpacingFt (e.g. 15)` → `InvalidInput` with explicit “choose a point-type detector” error.

#### 3.2.3 Strategy resolve + branch

```csharp
bool isWallMount =
    string.Equals(ruleSet.Mount, "Wall", StringComparison.OrdinalIgnoreCase)
    || room.SelectedPlacementBehavior == DevicePlacementBehavior.WallSidewall;

DevicePlacementMode deviceMode =
    room.DeviceKind == DeviceKind.NotificationAppliance
        ? DevicePlacementMode.NotificationAppliance
        : DevicePlacementMode.SmokeDetector;

// smoke-only extra: ApplyBeamAndSlopeAdjustments when ceiling-mounted smoke
var locationProfile = deviceMode == NotificationAppliance
    ? DeviceLocationPointIdentifier.ResolveNotificationApplianceProfile(...)
    : DeviceLocationPointIdentifier.ResolveSmokeDetectorProfile(...);
// diagnostics: "Device placement mode=…, detected strategy=smoke-detector-ceiling-grid|…"
```

Then:

| Branch | Method |
|---|---|
| Notification mode | `GenerateNotificationApplianceCandidates` (own grid or wall) |
| Smoke + wall | `GenerateWallMountCandidates` at `ComputeNfpa72WallMountZ` |
| Smoke + ceiling | `GenerateCeilingCandidates` **+** `GenerateSlopedCeilingPeakRowCandidates` (appended) |

#### 3.2.4 Ceiling mesh — `GenerateCeilingCandidates`

Fine grid via `ComputeGridResolution` (same CoverageRadius/2 floor idea as sprinklers; **no** 4000-point doubling loop here — hard `break` at `MaxCandidatePoints`).

Filters:

1. outside room  
2. `DistanceToOuterBoundary < MinBoundaryClearanceFt - tol`  
3. obstacle expanded box + `SpansZ(placementZ)` (duct terminals use `max(box clearance, HvacSupplyRegisterClearanceFt=3.0)` in `BuildObstacleBoxes`)  
4. existing detector within `ExistingDetectorSeparationFt` (base default **15 ft** on the rule set; NA overrides this in its own rule provider)

Diagnostics counters mirror sprinklers. **Asymmetry:** smoke/NA engines `break` at the cap **without** the sprinkler-style truncation warning (known gap, §9).

#### 3.2.5 Peak-row extras — `GenerateSlopedCeilingPeakRowCandidates`

Only if a non-FLAT/non-NONE ceiling has bottom+top elevations.  
`peakRowZ = max(Top − 3, Bottom)`.  
Candidates kept only if within **3 ft of a bounding-box edge currently flagged as peak** (`peakIsMaxX` defaults true → high-X edge) **or** within 3 ft of the outer boundary; then same boundary/obstacle(Z at peakRow)/existing filters.  
Merged into the main candidate list before selection.

#### 3.2.6 Wall mount — `GenerateWallMountCandidates`

```csharp
const double wallStandoffFt = 0.15;
double step = ruleSet.MaxSpacingFt > 0 ? ruleSet.MaxSpacingFt : 15.0;
// per edge: inward normal via midpoint probe at 0.5 ft
// n = ceil(edgeLen / step); k = 0..n inclusive
// point = lerp(edge) + inward * 0.15; Z = wallZ
```

`wallZ` for smoke: `ComputeNfpa72WallMountZ(placementZ, level, WallMountDropFromCeilingFt=0.5, isNotification:false)` — drop below ceiling.  
Filters: inside room, obstacle+`SpansZ(wallZ)`, existing separation. Point carries `WallEdgeIndex`.

#### 3.2.7 Selection — `SelectFromCandidates` (device engine)

```csharp
double minSpacingFt = smoke
    ? (ruleSet.MinSpacingFt > 0 ? ruleSet.MinSpacingFt : ruleSet.CoverageRadiusFt)
    : max(ruleSet.MinSpacingFt or 5.0, MaxSpacingFt * 0.35);   // notification

double coverageRadiusFt = smoke
    ? (ruleSet.CoverageRadiusFt > 0 ? CoverageRadius : 21.213)
    : (CoverageRadius > 0 ? CoverageRadius : MaxSpacing / √2);
```

**Single-device centering fast path** (ceiling only):  
if `!isWallMount && room.Area ≤ MaxCoverageArea && maxDim ≤ coverageRadius * 1.8`  
→ `FindBestCentroidCandidate` (closest valid candidate to polygon centroid) → **exactly one point**, `Success`.

**Otherwise multi-device greedy:**

1. Sort candidates by **distance to centroid ascending** (center-out, unlike sprinkler’s Y-then-X sort).
2. Skip if within `coverageRadius` of an existing or already-selected device.
3. Skip if closer than `minSpacingFt` to an already-selected device.
4. Emit `CalculatedSmokeDetectorPoint` with `Mount = "Wall"|"Ceiling"` and `WallEdgeIndex`.

`RequiredCount` (estimate) = `ceil(room.Area / MaxCoverageAreaSqFt)` (fallback 900).  
**No** sprinkler-style `FinalizeSelection` pairwise/wall-distance/gap suite on this path — post-audit is thinner (known asymmetry, §9).

Empty candidate list before selection → `NoValidCandidates`.

#### 3.2.8 Family / type / catalog influence

| Input | Source | Effect |
|---|---|---|
| `DetectorType` | Catalog `SmokeDetectors` sheet / row derive | Beam branch, rule notes, profile label |
| `Mount` | Catalog (Ceiling/Wall/Floor) | wall vs ceiling generation + wall rule adjustments |
| `CeilingSlope` | Catalog “detector-rated-for” + room `CeilingType`/ceiling host | slope factors; peak-row eligibility |
| `AirChangesPerHour` | `SmokeDetectorRoomInput` | airflow table |
| Family/type names | UI selection | unsupported behavior block; beam name sniffing |
| Per-room spacing overrides | UI | clamped [5,30] via `ApplyPerRoomOverrides` |
| `DeviceKind` | stamped by tab | switches smoke vs NA profile + selection constants |

#### 3.2.9 Smoke edge cases (explicit)

| Edge case | Behavior |
|---|---|
| Degenerate polygon | `InvalidRoomGeometry` |
| Unsupported behavior | `InvalidInput` |
| Beam detector, room shorter than min path | `InvalidInput` + NFPA 72 §17.7.3.7 message |
| No ceiling | provisional Z (incl. +10 ft default) + `ReviewRequired` |
| Sloped ceiling | averaged Z + spacing factors + peak-row candidates |
| High airflow | smaller area → tighter mesh spacing |
| Wall mount | 0.15 ft standoff, 0.5 ft drop, wall clearance 0.05 |
| HVAC supply terminal obstacle | clearance raised to 3 ft |
| Room fits one detector | forced center point, `Success` |
| All candidates rejected | `NoValidCandidates` |
| 4000-point cap | silent break (no truncation warning yet) |
| Existing detector nearby | generation reject + coverage skip |
| Per-room override | clamp + provisional review |
| Kind unclassifiable at placement | never skipped/deleted as “other kind” (Decision 021) |

---

## 4. Notification appliances

### 4.1 Plain-English explanation (non-technical)

**What we are doing:** deciding where horns, strobes, horn-strobes, speakers, etc., go so that **everyone can see the flash** and **everyone can hear the sound**.

**How, in everyday terms:**

1. **The appliance’s rating drives the spacing — not the room’s hazard class.**
   - **Brighter strobe (more candela) → can sit farther apart.** Roughly: 15 cd ≈ 30 ft, up to >110 cd ≈ 50 ft.
   - **Louder horn (more dBA) → can sit farther apart.** Roughly: under 85 dBA ≈ 20 ft, 95+ ≈ 35 ft.
2. **If one device does both (horn-strobe), the stricter of the two wins** — if sound only reaches 25 ft but light reaches 40 ft, we design for **25 ft**.
3. **Ceiling slope and wall mounting tighten the spacing further** (slope ×0.80/×0.75; wall ×0.85).
4. **Height (Z):** same ceiling rules as the other devices; wall appliances also drop about 0.5 ft below the ceiling; wall-mounted notification height is additionally clamped into the NFPA 72 wall band (about **6.67–8.0 ft** above floor when applied in the placement core path for notification wall Z).
5. **Where points go:**
   - **Ceiling mode:** a **finer mesh** than smoke detectors (`max(2, spacing/3)` step, at least 1 ft), same four rejects (outside room, wall clearance, obstacle, existing appliance).
   - **Wall mode:** same wall-walk as smoke detectors (0.15 ft off the wall, stepped at spacing, Z = wall height).
6. **Selection:** same center-out greedy as smoke detectors — one center point if the room fits in one appliance’s coverage; otherwise cover from the middle outward while respecting min spacing and coverage radius derived from the effective spacing (`radius = spacing/√2`, min spacing ≈ `max(5, 0.35×spacing)`).
7. **Honesty:** ratings and tables are **provisional** until the project design basis is signed off; every room is review-required. Visible and audible both considered; the stricter one is what you see in the notes on the rule set.

**What actually decides the final X/Y/Z:** candela + dBA (+ mount + slope) → effective spacing → ceiling Z → fine mesh or wall walk → filters → single-center or center-out greedy.

### 4.2 Technical deep-dive

**Same engine as smoke:** `SmokeDetectorCalculationService` with `DevicePlacementMode.NotificationAppliance`.  
**Rules:** `Nfpa72NotificationApplianceRules` (`…/NotificationAppliances/Final/BruteForce/Nfpa72NotificationApplianceRules.cs`) — implements `ISmokeDetectorPlacementRules`; `[PROVISIONAL]` `HasApprovedRules => false`.

#### 4.2.1 Descriptor → rules

Input “appliance type” on the room is a **descriptor string** built by `NotificationAppliancePlacementInputBuilder.BuildRuleDescriptor`:

```
HornStrobe|candela=75|dba=89
```

`ParseDescriptor` splits on `|` then `key=value` → `type`, `candela`, `dba` (ints ≥ 0).

```csharp
double visibleSpacing = VisibleSpacing(candela);  // ≤15→30, ≤30→35, ≤75→40, ≤110→45, else 50
double audibleSpacing = AudibleSpacing(dba);      // ≤0→∞, <85→20, <90→25, <95→30, else 35
double spacing = Math.Min(visibleSpacing, audibleSpacing);
if (candela <= 0) spacing = audibleSpacing;       // audible-only appliance
if (dba <= 0)     spacing = visibleSpacing;       // visible-only appliance
if (invalid/≤0)   spacing = 15.0;                // hard fallback

spacing *= SlopeFactor(slope);   // SLOPED/PEAKED 0.80, STEPPED 0.75, else 1.0
if (mount == Wall) spacing *= 0.85;
```

Derived rule set:

| Field | Formula |
|---|---|
| `MaxSpacingFt` | effective spacing (above) |
| `MinSpacingFt` | `max(5, spacing × 0.35)` |
| `MaxCoverageAreaSqFt` | `max(225, spacing²)` |
| `CoverageRadiusFt` | `spacing / √2` |
| `MaxDistanceFromWallsFt` | `spacing / 2` |
| `MinBoundaryClearanceFt` | 0.05 wall / 0.333 ceiling |
| `ExistingDetectorSeparationFt` | `max(5, spacing × 0.35)` |
| `WallMountDropFromCeilingFt` | 0.5 |
| `HvacSupplyRegisterClearanceFt` | 3.0 |
| `ObstacleClearanceFt` | 1.5 |
| `IsProvisional` | **true** |
| `Notes` | records appliance/candela/dBA/mount/slope + “stricter of visible and audible governs” |

#### 4.2.2 Candidate generation — `GenerateNotificationApplianceCandidates`

```csharp
double baseGridRes = ComputeGridResolution(geometry, ruleSet, config);
double notificationGridRes = Math.Max(1.0,
    Math.Min(baseGridRes, Math.Max(2.0, ruleSet.MaxSpacingFt / 3.0)));

if (isWallMount)
    return GenerateWallMountCandidates(..., wallZ: placementZ - WallMountDropFromCeilingFt, ...);

// ceiling: same four filters as smoke, with notification-specific floors
// boundary: max(MinBoundaryClearanceFt, 0.333)
// obstacle: max(box.ClearanceFt, ruleSet.ObstacleClearanceFt)
// existing: max(ExistingDetectorSeparationFt, MinSpacingFt)
```

Fallback profile (if `locationProfile` were null): strategy names `notification-wall-mount` / `notification-ceiling-grid` — **note:** these differ slightly from `DeviceLocationPointIdentifier`’s `notification-appliance-*` strings (§9).

Diagnostics:  
`"Notification appliance branch selected: visible/audible coverage spacing is used instead of smoke-detector coverage geometry."`  
plus `"Notification-appliance candidates generated=… at grid step …"`.

#### 4.2.3 Selection differences vs smoke

Inside shared `SelectFromCandidates` when `room.DeviceKind == NotificationAppliance`:

- `minSpacingFt = max(ruleSet.MinSpacingFt or 5, MaxSpacing × 0.35)`
- `coverageRadiusFt = ruleSet.CoverageRadiusFt or MaxSpacing/√2`
- Same single-device centering test and centroid-first greedy.
- Points stamped `Mount` wall/ceiling; `DeviceKind` remains NotificationAppliance on the room input.

#### 4.2.4 Wall Z for notification

When generating wall candidates from the smoke branch helper, `ComputeNfpa72WallMountZ(..., isNotification: true)` applies the NFPA 72 §18.5.4.3.1 band clamp (**6.67–8.0 ft AFF**) in addition to the ceiling drop logic (see `SmokeDetectorCalculationService` ~1417–1439). The direct `GenerateNotificationApplianceCandidates` wall path uses `placementZ − 0.5` before calling the shared wall generator — both paths are ceiling-relative; confirm desired band behavior at runtime when wall mode is exercised. `[STATIC-ONLY]`

#### 4.2.5 Edge cases (notification-specific + shared)

| Edge case | Behavior |
|---|---|
| Candela only (no dBA) | visible spacing governs |
| dBA only (no candela) | audible spacing governs |
| Both present | `min(visible, audible)` |
| Neither / invalid | fallback 15 ft spacing |
| HornStrobe 15 cd / 87 dBA (test) | audible 25 ft governs (see `SmokeDetectorCalculationTests`) |
| Sloped ceiling | ×0.80 (stepped ×0.75) before wall factor |
| Wall mount | additional ×0.85; boundary 0.05 |
| Small room | single center appliance fast path |
| Existing smoke detector in room | **kind-scoped**: does not count as “own kind” for skip/replace/duplicate (Decision 021); `DeviceKindResolver.TryResolve` must positively match notification keywords |
| Unclassifiable existing family name | never treated as own-kind → never skipped/deleted |
| Cross-kind coexistence | smoke run then NA run in same room: neither skips nor deletes the other (runtime acceptance item) |
| Missing candela on strobe row | catalog validator ERROR at load (fail-fast) |
| `AudibleCoverageEngine` | **tests only** — not in production candidate path |

---

## 5. Side-by-side comparison

| Aspect | Sprinklers | Smoke detectors | Notification appliances |
|---|---|---|---|
| Engine class | `BruteForceCalculationService` | `SmokeDetectorCalculationService` | **Same** smoke engine, `DevicePlacementMode.NotificationAppliance` |
| Rule provider | `DefaultHazardPlacementRules` | `Nfpa72SmokeDetectorRules` | `Nfpa72NotificationApplianceRules` |
| Rules driven by | Hazard class (+ catalog type fields + overrides) | Detector type, mount, slope, ACH (+ overrides) | Candela, dBA, mount, slope (+ overrides) |
| `HasApprovedRules` | false `[PROVISIONAL]` | false `[PROVISIONAL]` | false `[PROVISIONAL]` |
| Default max spacing | 15 ft (Light) … 10 ft (EH) | 30 ft smooth ceiling | 15–50 ft from ratings |
| Strategy labels | `sprinkler-ceiling-grid` / `sprinkler-sidewall-edge-grid` | `smoke-detector-ceiling-grid` / `smoke-detector-wall-mounted` | `notification-appliance-ceiling-grid` / `notification-appliance-wall-mounted` |
| Ceiling layout | **Centered rectangular array** (`SelectCenteredGrid`) | Center-out greedy (+ single-center fast path) | Same as smoke |
| Wall layout | Sidewall **directional solver** (rows + throw depth) | Simple wall walk 0.15 ft standoff | Same wall walk |
| Extra candidates | — | Sloped **peak row** (within 3 ft, Z=peak−3) | — |
| Height adjustments | Light height table + slope + class factor + orientation | Airflow table + slope + wall | Candela/dBA tables + slope + wall |
| Obstacle rule | Per-hazard beam/column/duct clearances | Category clearances; **HVAC terminal 3 ft** | Default obstacle clearance 1.5; HVAC terminal 3 ft |
| Existing-device filter | Existing sprinkler separation | Existing detector separation (15 base) | Own-kind only (kind-scoped) |
| Min boundary | `BoundaryClearanceFt` (1–2 ft) | `MinBoundaryClearanceFt` (0.333 / 0.05 wall) | same as smoke |
| Truncation warning at 4000 cap | **Yes** | **No** (silent break) | **No** |
| Rich post-checks (`FinalizeSelection`) | **Yes** (max spacing, max wall, area, gap %) | Partial (counts + provisional flag; no full gap suite) | Same as smoke |
| Beam path refusal | n/a | **Yes** | n/a |
| Sort for greedy | Y then X (sidewall) / centered grid order | Distance to centroid | Distance to centroid |
| Output point type | `CalculatedSprinklerPoint` | `CalculatedSmokeDetectorPoint` | `CalculatedSmokeDetectorPoint` |
| Placement core | `RevitSprinklerPlacementService` + strategies | `FireAlarmDevicePlacementCore` via smoke executor | Same core via NA executor |
| Existing-device policy default | `SkipRoom` | `SkipRoom` | `SkipRoom` (kind-scoped) |
| Duplicate guard | 0.25 ft proximity | 0.25 ft | 0.25 ft |
| Outside-room guard at place | `OUTSIDE_ROOM_BOUNDARY` | `Point falls outside host room boundary` | same |
| Z validation window | 0.5 ft XY tolerance | 0.5 ft XY + 6 ft Z window | same |

---

## 6. Family / type / mount influence matrix

| Factor | Sprinkler identification | Smoke identification | Notification identification |
|---|---|---|---|
| Family name | Unsupported→block; drives behavior/strategy | Unsupported→block; “Beam” name→beam rules/path check | Unsupported→block; kind keywords for skip policy |
| Type name | Per-row; catalog merge of listed numbers; missing→placement probe | Per-row; catalog DetectorType/Mount/Slope | Per-row; catalog ApplianceType/Candela/dBA → descriptor |
| Mount / orientation | Sidewall → perimeter solver, 0.85 spacing, √2 coverage | Wall → wall walk + wall rules | Wall → wall walk + ×0.85 |
| `FamilyPlacementType` | Strategy at place; eligibility preflight host requirements | Strategy at place (`FireAlarmDevicePlacementCore`) | same |
| Hazard class | **Primary** rule key | Not used | Not used |
| Candela / dBA | — | — | **Primary** spacing key (stricter wins) |
| Air changes/hr | — | **Primary** area key | Ignored (passed but NA tables don’t use ACH) |
| Ceiling slope | Factor on S | Factor + peak row | Factor on S |
| Per-room S/wall overrides | Yes (clamp to provisional ceiling) | Yes (clamp 5–30) | Yes (same smoke override path) |

Catalog sheets (`CatalogLoader` / `CatalogValidator`):  
`Sprinklers`, `SmokeDetectors`, `NotificationAppliances` — fail-fast on schema/duplicates; missing required NA rating fields → load ERROR.

---

## 7. Edge-case catalogue (all devices)

Cross-device, ordered by pipeline stage:

**Input / geometry**

- No rooms in snapshot → overall `Success=false` + error.
- Boundary missing / <3 vertices → `InvalidRoomGeometry`.
- Inner loops (holes) excluded from interior via even-odd tests.
- Concave polygons supported.
- Linked rooms: already host-MEP feet; placement never re-transforms.

**Rules**

- Missing hazard (sprinklers) → Light + review.
- Unrecognized hazard → Light + review.
- Provisional rules everywhere → global review warning on result.
- Per-room override applied → clone + provisional + review diagnostic.
- Catalog merge before override; hazard ceiling still bounds sprinkler S.

**Ceiling / Z**

- Ceiling below floor ignored (slab of level below).
- No ceiling → fallback Z + review; hosted sprinkler family → hard block.
- Sloped → averaged Z; stepped/unsupported sprinkler ceilings → `UnsupportedCeiling` status path.
- Device engines default Z to floor+10 when height also missing (sprinkler uses floor).

**Family / behavior**

- `DevicePlacementBehavior.Unsupported` → `InvalidInput`, zero points (all three).
- Sidewall / Wall mount → non-grid generation.
- Beam detector short room → `InvalidInput`.

**Generation**

- Outside polygon → reject.
- Boundary clearance → reject (except sidewall lower bound intentionally skipped).
- Obstacle XY+Z at placement plane → reject; missing Z → conservative block.
- Existing same-purpose device within separation → reject.
- `MaxCandidatePoints` 4000: sprinklers warn + ReviewRequired; smoke/NA silent break.
- Notification grid step forced ≥ 2 ft and ≤ spacing/3 (min 1 ft).

**Selection**

- Sprinkler ceiling: centered array + snap to fine candidates; min spacing; empty → Failed.
- Sprinkler sidewall: wall-row greedy by uncovered samples; residual gap → ReviewRequired.
- Smoke/NA: single-center fast path; else centroid-first greedy (coverage skip + min spacing).
- Greedy iteration cap `MaxSearchIterations`.

**Post-checks**

- Sprinklers: max pair spacing, max wall distance, per-head area, coverage gap %, provisional flag.
- Devices: provisional flag; calc `ReviewRequired` survives into run report (Decision 021 — not clobbered to Success).

**Placement feedback into “has devices / duplicates” (identification-adjacent)**

- Kind-scoped existing collection (`DeviceKindResolver.TryResolve`); unknown names never own-kind.
- `SkipRoom` / `ReplaceExisting` / `AddAnyway` policies.
- Duplicate proximity 0.25 ft; outside-room refusal; post-place XY/Z tolerance.
- Missing family → skip room / `SkippedMissingFamilyCount` (sprinklers).

**Reporting honesty**

- Cancelled runs → `Cancelled`, not zeroed Success.
- Spatially invalid placements → ReviewRequired with deviation evidence.
- Provisional summary text forced into report.

---

## 8. Class & method index

### Location-point layer

| Symbol | File |
|---|---|
| `DeviceLocationPointIdentifier.ResolveSprinklerProfile` | `FireProtection.Backend/Services/Placement/LocationPoints/DeviceLocationPointIdentifier.cs` |
| `DeviceLocationPointIdentifier.ResolveSmokeDetectorProfile` | same |
| `DeviceLocationPointIdentifier.ResolveNotificationApplianceProfile` | same |
| `DeviceLocationPointStrategy`, `DeviceLocationPointProfile` | same |

### Sprinkler engine

| Symbol | Role |
|---|---|
| `BruteForceCalculationService.Calculate` | snapshot loop, provisional summary |
| `…CalculateRoom` | full per-room pipeline |
| `…ParseHazardClass` | hazard parse/default |
| `…MergeTypeRuleValues` | catalog per-type merge |
| `…ApplyPerRoomOverrides` | override clone/clamp |
| `…SelectPrimaryCeiling` | ceiling pick |
| `…RequiresCeilingHost` | hosted-family ceiling requirement |
| `…ComputeGridResolution` / `EstimateGridPoints` | fine-grid step |
| grid sweep inside `CalculateRoom` | 4 filters + counters + C-E truncation guard |
| `…SelectCenteredGrid` / `…IsPlacementValid` | ceiling array selection |
| `…SelectFromCandidates` | greedy (sidewall-adjacent / shared) |
| `…FinalizeSelection` | post-checks + RequiredCount |
| `…SelectSidewallDirectional` / `…SampleCoveredByRow` | sidewall solver |
| `…GenerateSidewallCandidates` / `…CleanPolygonLoop` / `…ComputeSignedArea` | perimeter candidates |
| `DefaultHazardPlacementRules.GetRules` | hazard tables |
| `HazardPlacementRuleSet` / `IHazardPlacementRules` | rule contract |
| `PlacementInputBuilder.Build` | UI→snapshot, orientation/behavior |
| `DevicePlacementBehaviorResolver` | FamilyPlacementType+mount → behavior |
| `RoomGeometry`, `GeometryMath`, `BruteForceCalculationConfig` | shared math/config |

### Smoke / notification engine

| Symbol | Role |
|---|---|
| `SmokeDetectorCalculationService.Calculate` | entry, provisional summary |
| `…CalculateRoom` | shared per-room pipeline + mode branch |
| `…GenerateCeilingCandidates` | smoke ceiling mesh |
| `…GenerateSlopedCeilingPeakRowCandidates` | peak row |
| `…GenerateWallMountCandidates` | wall walk (smoke + NA) |
| `…GenerateNotificationApplianceCandidates` | NA ceiling/wall branch |
| `…SelectFromCandidates` | single-center + centroid greedy |
| `…FindBestCentroidCandidate` / `…ComputePolygonCentroid` | centering |
| `…ComputeGridResolution` | device fine-grid step |
| `…SelectPrimaryCeiling` | ceiling pick (incl. PEAKED) |
| `…BuildObstacleBoxes` / `…BuildExistingDetectorXy` | obstacle/existing prep |
| `…ApplyPerRoomOverrides` | override clamp [5,30] |
| `…ApplyBeamAndSlopeAdjustments` | smoke ceiling extras |
| `…ComputeNfpa72WallMountZ` | wall Z (+ NA band) |
| `Nfpa72SmokeDetectorRules.GetRules` | NFPA 72 Ch.17 tables |
| `Nfpa72NotificationApplianceRules.GetRules` / `ParseDescriptor` / `VisibleSpacing` / `AudibleSpacing` / `SlopeFactor` | Ch.18 rating tables |
| `SmokeDetectorPlacementRuleSet` / `ISmokeDetectorPlacementRules` | rule contract |
| `DevicePlacementMode` | smoke vs NA |
| `SmokeDetectorPlacementInputBuilder` / `NotificationAppliancePlacementInputBuilder` | UI→room input |

### Placement-side (feeds identification via existing/duplicate/policy)

| Symbol | Role |
|---|---|
| `RevitSprinklerPlacementService.PlaceSprinklers` / `PlaceSinglePoint` / `EvaluateRoomEligibility` / `ProbeMissingFamilies` | sprinkler place + preflight |
| `IFamilyPlacementStrategy` + Face/WorkPlane/Level/Wall strategies + `CeilingHostResolver` | host resolution |
| `FireAlarmDevicePlacementCore.RunPlacement` / `PlaceSinglePoint` / `DevicesInRoom` | device place + kind-scoped skip |
| `DeviceKindResolver.TryResolve` | smoke vs notification attribution |
| `ExistingDevicePolicy` / `ExistingDevicePolicyOptions` | SkipRoom default |
| `RevitSmokeDetectorPlacementExecutor` / `RevitNotificationAppliancePlacementExecutor` | inject rules into core |
| `DevicePlacementViewModelBase` / `SmokeDetectorViewModel` / `NotificationApplianceViewModel` / `SprinklerBruteForceViewModel` | UI selection + triggers |
| `ICatalog` / `CatalogLoader` / `CatalogService` | family/type/mount/rating source |

### Tests touching identification

| File | Covers |
|---|---|
| `FireProtection.Tests/SmokeDetectorCalculationTests.cs` | NFPA72 smoke + NA rules, audible engine, peak rows |
| `FireProtection.Tests/NotificationApplianceRulesTests.cs` | descriptor, visible/audible, slope/wall |
| `FireProtection.Tests/DeviceReportAndKindTests.cs` | report verdicts + kind separation |
| `FireProtection.Tests/BruteForceSelectionTests.cs` | eligibility/selection contract |
| `FireProtection.Tests/CenteredGridPlacementTests.cs`, `SidewallPlacementTests.cs`, `SidewallDirectionalSolverTests.cs` | sprinkler layouts |
| `FireProtection.Tests/BruteForceOverrideTests.cs`, `PerTypeCatalogMergeTests.cs`, `CatalogLoaderTests.cs` | overrides + catalog |

---

## 9. Doc vs code discrepancies (known)

Recorded so this file does not silently inherit stale claims. **Code is authoritative.**

1. **`TARGET_DEVICE_PLACEMENT_LOGIC.md` “divide-and-centre” shared method** — describes a divide-room-by-count layout as the shared method. **Actual ceiling sprinkler path** is a fine-grid sweep + `SelectCenteredGrid` array (n=ceil(dim/S)); smoke/NA use centroid-first greedy, not divide-and-count. Sidewall uses a directional wall-row solver.
2. **Older notes that smoke/notification tabs are empty shells** (`PROJECT_CONTEXT` §13, early `DECISIONS` Undetermined) — **stale.** Engines, rules, executors, and kind-scoped placement core now exist (`[STATIC-ONLY]`).
3. **`Explanation.md` Part 7 “reuses SmokeDetectorCalculationService”** — still true; this file adds the mode-specific NA generator, descriptor parsing, and selection constants that the shorter summary omits.
4. **Strategy name strings** — `DeviceLocationPointIdentifier` emits `notification-appliance-ceiling-grid` / `notification-appliance-wall-mounted`; the NA generator’s null-profile fallback emits `notification-ceiling-grid` / `notification-wall-mount`. Both appear in diagnostics; prefer the Identifier strings as canonical.
5. **Truncation honesty** — sprinkler engine warns on `MaxCandidatePoints` truncation; smoke/NA engines do not (silent `break`). Documented here as a real asymmetry, not a doc error.
6. **Post-check depth** — sprinklers run the full `FinalizeSelection` suite; smoke/NA selection returns after greedy without the same pairwise/gap suite.
7. **Default Z when no ceiling** — sprinkler: floor level; smoke/NA: floor+10 ft if height also missing.
8. **`AudibleCoverageEngine`** — referenced in tests only; **not** part of production notification candidate identification (audible influence is via `AudibleSpacing` inside the rule provider).
9. **`DeviceLocationPointIdentifier`** — pure labeling/diagnostics; generation branches are re-derived inside each engine. Do not describe it as the runtime dispatcher.
10. **`TARGET_DEVICE_PLACEMENT_LOGIC.md` audible “different loudness method”** — production code does **not** run a room-by-room loudness simulation; audible coverage enters only as the dBA→spacing table (plus unused test-only `AudibleCoverageEngine`).
11. **PROGRESS “smoke/notification placement disabled / backend deferred”** older rows — superseded by Decision 021-era wiring; treat latest PROGRESS/DECISIONS bullets as current.
12. **All three `HasApprovedRules` are false** — any doc claiming approved NFPA output is wrong; every run is provisional/`ReviewRequired` by design.

---

## Related documentation

- [[PROJECT_CONTEXT]]
- [[ARCHITECTURE]]
- [[DECISIONS]] (esp. 004, 009, 011, 016–021)
- [[PROGRESS]]
- [[TODO]]
- [[SESSION_NOTES]]
- [[SPRINKLER_POINT_CALCULATION_EXPLAINED]] (sprinkler X/Y/Z deep-dive)
- `Explanation.md` (broader project walkthrough)
- `TARGET_DEVICE_PLACEMENT_LOGIC.md` (intent/tables — verify against code per §9)
