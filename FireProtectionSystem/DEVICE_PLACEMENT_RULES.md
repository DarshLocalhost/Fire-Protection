# Device Placement Rules — Current Project Logic

> Reflects the actual implemented engine (source of truth), not target/aspirational rules.
> **Every rule set is provisional:** `HasApprovedRules => false`, each result `IsProvisional`,
> and every populated room is returned `ReviewRequired`. Values are traced to a source but are
> NOT FPE/AHJ-verified. No number is fabricated; anything derived from geometry is flagged.

---

## 1. Sprinklers
`DefaultHazardPlacementRules` + `BruteForceCalculationService`

### 1. Know the rules for this room
Resolved per room from its hazard class (fallback = design-window default). All values NFPA 13
(2002) via the NFSA textbook, provisional:

| Hazard | MaxSpacing | MinSpacing | MaxCoverage | CoverageRadius | BoundaryClearance | MaxDistFromWall | ExistingSep |
|--------|-----------:|-----------:|------------:|---------------:|------------------:|----------------:|------------:|
| Light  | 15 ft | 6 ft | 225 ft² | 7.5 ft | 1.0 ft | 7.5 ft | 6 ft |
| OH1    | 15 ft | 6 ft | 130 ft² | 7.5 ft | 1.0 ft | 7.5 ft | 6 ft |
| OH2    | 15 ft | 6 ft | 130 ft² | 7.5 ft | 1.0 ft | 7.5 ft | 6 ft |
| EH1    | 12 ft | 6 ft | 100 ft² | 6.0 ft | 1.5 ft | 6.0 ft | 6 ft |
| EH2    | 12 ft | 6 ft | 100 ft² | 6.0 ft | 2.0 ft | 6.0 ft | 6 ft |

- `MaxDistFromWall = ½ MaxSpacing`. Per-obstacle clearances differ by class (beam/column/duct).
- Adjustments applied in order: per-room override (clamped) → ceiling-height factor → NFPA-13
  max-spacing ceiling clamp (15 ft light/ordinary, 12 ft extra) on any catalog/override value →
  orientation: **sidewall** reduces MaxSpacing by the orientation factor and halves the coverage
  radius (÷√2, half-circle spray).

### 2. Make a grid of possible dots
Fine grid over the room bounding box, step = configured grid resolution (~1 ft) capped at the
coverage radius; swept ascending Y then X. A `MaxCandidatePoints` safety cap flags the room
`ReviewRequired` if the grid is truncated.

### 3. Deletion of non-eligible dots
A dot is deleted if:
1. Outside the room outline or inside a hole.
2. Closer than `BoundaryClearanceFt` to a wall.
3. Inside an obstacle box + its per-category clearance **and** that obstacle spans the placement
   Z (a low beam/duct below the ceiling plane is ignored).
4. Within `ExistingSprinklerSeparationFt` of a sprinkler already in the model.

### 4. Pick the final positions
- **Pendent / upright (ceiling) → centered rectangular grid:** `nx = ceil(W/S)`, `ny = ceil(H/S)`,
  actual step = dim/n (≤ S), first line at step/2 (centered, not jammed to a wall). A blocked grid
  target snaps to the nearest unused valid fine-grid dot within ½ step; if nothing is reachable the
  cell is skipped. Min-spacing guard prevents two heads closer than `MinSpacingFt`.
- **Sidewall → directional solver** (throw one way across the room, not a circle):
  along-wall spacing `S` = adjusted MaxSpacing; `throwDepth = min(TypeMaxCoverageArea / S, hazard
  ceiling)`, or `= S` when no listed area (flagged); `endWallMax = S/2`; head hangs `standOff` 0.5 ft
  off the wall face. Place a head row per wall (first/last ≤ endWallMax from corners; a wall only
  marginally longer than `2·endWallMax` gets ONE centered head, no stacking). Seed the longest wall,
  then measure the room's perpendicular depth from it — **if depth > throwDepth, seed the opposing
  (anti-parallel) wall unconditionally** (geometry-driven, obstacle-independent → identical layout
  floor-to-floor). Coverage counts back to the wall face; a head is never rejected by the clearance
  of its own host wall (only throw-path obstacles count). A room too deep for any wall pair → residual
  "coverage gap" warning, never silent.
- **Post-selection (both paths):** verify max center-to-center spacing, wall distance ≤ S/2,
  per-head coverage area, and re-sample for coverage gaps (>5% uncovered → `ReviewRequired`).
  Provisional rules always force `ReviewRequired`.

---

## 2. Smoke Detectors
`Nfpa72SmokeDetectorRules` + `SmokeDetectorCalculationService`

### 1. Know the rules for this room
Spot-type default (NFPA 72 Ch. 17, provisional):
- `MaxSpacingFt` = 30 ft
- `MinSpacingFt` = 10 ft (beam detector: 15 ft minimum listed path length)
- `MaxCoverageAreaSqFt` = 900 ft²
- `CoverageRadiusFt` = 30/√2 ≈ 21.213 ft
- `MaxDistanceFromWallsFt` = 15 ft
- `MinBoundaryClearanceFt` = 0.333 ft (4 in), ceiling mount
- `WallMountDropFromCeilingFt` = 0.5 ft, `HvacSupplyRegisterClearanceFt` = 3 ft

Adjustments (compounding):
- **Beam type** → spacing 60 ft, min path 15 ft, coverage 3600 ft², wall dist 30 ft.
- **High airflow** (ACH > 7.5): coverage steps down by band (875→125 ft² from 8.6→60 ACH);
  `MaxSpacing = √area`.
- **Slope**: SLOPED/PEAKED ×0.90 spacing (×0.81 area); STEPPED ×0.85 (×0.7225 area).
- **Wall mount**: boundary 0.05 ft, spacing ×0.90, area ×0.85 (floor 125 ft²).
- **Beams at ceiling** (structural): d/H < 0.1 → smooth; ≥ 0.4H → beam-pocket spacing ×0.5;
  room ≤ 900 ft² or corridor ≤ 15 ft → smooth-ceiling 30 ft restored.

### 2. Make a grid of possible dots
Grid step = configured resolution (~1 ft) capped at coverage radius, floored at coverage radius/2
for rooms larger than that. A sloped ceiling adds an extra peak-row candidate set within 3 ft of the
peak (Z = peak − 3 ft) — smoke rises there.

### 3. Deletion of non-eligible dots
A dot is deleted if:
1. Outside the room outline or inside a hole.
2. Closer than `MinBoundaryClearanceFt` to a wall (0.333 ft ceiling / 0.05 ft wall).
3. Inside an obstacle box + clearance **and** the obstacle spans Z; an HVAC supply register
   (`DuctTerminal`) uses `max(clearance, 3 ft)` keep-away.
4. Within `ExistingDetectorSeparationFt` of a detector already in the model.

### 4. Pick the final positions
- **Small room** (`area ≤ MaxCoverageArea` **and** `maxDim ≤ 1.8 × CoverageRadius`) → one detector
  at the valid dot nearest the room centroid.
- **Bigger room** → sort valid dots by distance to centroid (center first); skip if already within
  `CoverageRadius` of a chosen/existing detector; skip if closer than `MinSpacing`; otherwise keep.
- **Wall mount** → walk each wall, step = `MaxSpacing`, dot 0.15 ft off the wall face,
  Z = ceiling − 0.5 ft; same four filters, then same center-out pick.
- `RequiredCount = ceil(area / MaxCoverageArea)`.

---

## 3. Notification Appliances (horn / strobe / speaker)
`Nfpa72NotificationApplianceRules` (reuses the smoke-detector grid/selection engine)

### 1. Know the rules for this room
Spacing comes from the type rating descriptor `Type|candela=NN|dba=NN` (NFPA 72 Ch. 18, provisional):
- **Visible (strobe) by candela**: ≤15 → 30 ft | ≤30 → 35 ft | ≤75 → 40 ft | ≤110 → 45 ft | >110 → 50 ft
- **Audible (horn) by dBA**: <85 → 20 ft | <90 → 25 ft | <95 → 30 ft | ≥95 → 35 ft
- `spacing = min(visible, audible)` — the stricter governs; if only one rating is present it wins;
  fallback 15 ft.
- Slope ×0.80 (SLOPED/PEAKED), ×0.75 (STEPPED); Wall mount ×0.85.
- Derived: `MaxCoverageArea = max(225, spacing²)`, `MinSpacing = max(5, spacing×0.35)`,
  `CoverageRadius = spacing/√2`, `MaxDistFromWall = spacing/2`, boundary 0.05 ft wall / 0.333 ft
  ceiling, `ExistingSeparation = max(5, spacing×0.35)`, wall drop 0.5 ft, HVAC 3 ft, obstacle 1.5 ft.

### 2. Make a grid of possible dots
Grid step = `clamp(max(2, MaxSpacing/3))`, not coarser than the base grid, floor 1 ft — finer than
smoke because audible/visible spacing is tighter.

### 3. Deletion of non-eligible dots
Same four filters as smoke detectors (boundary uses `max(MinBoundary, 0.333)`; existing uses
`max(ExistingSeparation, MinSpacing)`).

### 4. Pick the final positions
- **Ceiling mount** → same small-room-center / bigger-room center-out selection as smoke detectors,
  using the rating-derived coverage radius and min spacing.
- **Wall mount** → walk the walls at `spacing`, dot 0.15 ft off the wall face, Z = ceiling − 0.5 ft
  clamped to 80–96 in AFF (NFPA 72 §18.5.4.3.1); same filters, then same center-out pick.
