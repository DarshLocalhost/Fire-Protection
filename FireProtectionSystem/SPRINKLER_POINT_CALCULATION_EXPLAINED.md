# Sprinkler Point Calculation - The Internal Logic

This is the internal math of the sprinkler layout engine only: how the number of points is
computed, how candidate points are created, how feasible points are selected, and every
formula and geometry involved. Source of truth: `FireProtection.Backend\Services\Placement
\Sprinklers\Final\BruteForce\BruteForceCalculationService.cs` (entry point `CalculateRoom`).
No UI, commands, statuses scale, placement or export logic is described here.

Symbols (defined once, reused everywhere):

| Symbol | Meaning | Source |
| --- | --- | --- |
| `S` | max spacing between heads (ft) | rule set `MaxSpacingFt` |
| `s_min` | min spacing between heads (ft) | rule set `MinSpacingFt` |
| `r` | coverage radius (ft) | rule set `CoverageRadiusFt` |
| `Amax` | max coverage area per head (sq ft) | rule set `MaxCoverageAreaSqFt` |
| `D_wall` | max distance from any wall (ft) | rule set `MaxDistanceFromWallsFt` (= S/2) |
| `A` | room floor area (sq ft) | extraction: `Room = room.Area` (`RoomExtractor.cs:213`) |
| `tol` | numeric epsilon | `BruteForceCalculationConfig.ToleranceFt = 1e-6` |

All values come from `DefaultHazardPlacementRules.Show()` and are **provisional**: they are
traced to NFPA 13 (2002) reproduced in the NFSA textbook, they are not FPE/AHJ-verified, and
each rule set carries `IsProvisional`. The rule set provider is the single place to swap in
approved values.

---

## Stage 1 - Rule set resolution (per room)

Input: hazard class (user override or Auto from room name) ->
`DefaultHazardPlacementRules.GetRules(hazardClass)` returns:

| Value | Light | OH1 | OH2 | EH1 | EH2 |
| --- | --- | --- | --- | --- | --- |
| `S` | 15 | 15 | 15 | 12 | 12 |
| `s_min` | 6 | 6 | 6 | 6 | 6 |
| `Amax` | 225 | 130 | 130 | 100 | 100 |
| `r` | 7.5 | 7.5 | 7.5 | 6.0 | 6.0 |
| `D_wall` | 7.5 | 7.5 | 7.5 | 6.0 | 6.0 |
| boundary clearance | 1.0 | 1.0 | 1.0 | 1.5 | 2.0 |
| obstacle clearance | 1.0 | 1.0 | 1.0 | 1.5 | 2.0 |
| beam / column / duct | 1 / 1 / 1.5 | 1 / 1 / 1.5 | 1.5 / 1 / 2 | 2 / 1.5 / 2.5 | 2.5 / 2 / 3 |
| existing-head separation | 6 | 6 | 6 | 6 | 6 |
| min K-factor (stored only) | 5.6 | 5.6 | 8.0 | 8.0 | 11.2 |
| class height factor | 1.0 | 0.9 | 0.85 | 0.8 | 0.75 |

Then, in `CalculateRoom` order:

1. `MergeTypeRuleValues` (`BruteForceCalculationService.cs:2433`) merges catalog values of
   the selected sprinkler type into the rule set by taking the minimum of each type value
   against its edition ceiling, where `spacingCeiling = GetNfpa13MaxSpacingCeiling(hazard)`
   (15 ft Light/Ordinary, 12 ft Extra):

   ```
   S        = min(S_type,        spacingCeiling)        if S_type > 0
   s_min    = min(s_min_type,    spacingCeiling)        if s_min_type > 0
   r        = min(r_type,        spacingCeiling / 2)    if r_type > 0
   Amax     = min(Amax_type,     spacingCeiling^2)      if Amax_type > 0
   ```

2. `ApplyPerRoomOverrides` (`...:2470`) applies per-room user values on top.

Derived quantities on the rule set (`HazardPlacementRuleSet.cs`):

```
EffectiveCoverageRadiusFt = MaxSpacingFt > 0 ? MaxSpacingFt / sqrt(2) : CoverageRadiusFt
RemoteDistanceLimitFt     = 0.7 * MaxSpacingFt
GetObstacleClearance(category)  -> ObstacleSpecificClearances, else ObstacleClearanceFt
GetOrientationAdjustment        -> 1.0 pendent / upright, 0.85 sidewall
GetCeilingSlopeAdjustment       -> 1.0 flat, 0.9 sloped, 0.85 stepped
```

`EffectiveCoverageRadiusFt` is the radius the final advisory sampler uses
(`BruteForceCalculationService.cs:1310`); `RemoteDistanceLimitFt` is the advisory
remote-distance limit (`...:1374`).

### Rulebook constants declared on the rule set

Enforced by the engine:

- `MinWallClearanceFt = 4/12` (4 in) - hard floor for any boundary clearance
  (`...:429`, `...:1131`).
- `WallDistanceFractionOfMaxSpacing = 0.5` - D_wall = S/2 default and the Small-Room-Rule
  gate (`...:2756`).
- `SmallRoomLintelDepthFt = 8/12` (8 in) - small-room lintel condition phrase (`...:2774`).
- `UnobstructedIfMemberSpacingOverFt = 7.5`, `ObstructedIfMemberSpacingUnderFt = 3.0` -
  construction classification (`...:2653`).
- Deflector band: min drop 1/12 (1 in), max 12/12 (unobstructed) or 6/12 (obstructed),
  peak rule 3.0 (`Nfpa13RulebookRules` constants).
- `SmallRoomMaxAreaSqFt = 800`, `SmallRoomMaxWallDistanceFt = 9.0`,
  `ObstructionZonePlaneBelowSprinklerFt = 1.5`,
  `PermanentFixtureRequiringSprinklerWidthFt = 4.0`,
  `ThreeTimesRuleFactor = 3.0`, `ThreeTimesRuleMaxRequiredDistanceFt = 2.0`,
  `ThreeTimesExceptionHalfSpacing = 0.5` (of `S`), `ThreeTimesRuleCategories`
  (structural columns / columns / structural framing) - obstacle and small-room logic below.

Declared on the rule set but NOT computed or enforced anywhere in the engine (recorded so
the documentation never claims they are): `CornerDistanceFractionOfMaxSpacing` (0.75),
`FlatSlopeRisePer12Run = 0.5/12`, `GentleSlopeRisePer12Run = 2/12`,
`DeflectorMaxDropAboveMembersFt = 22/12`, `DeflectorMaxDropInPocketFt`,
`OtherSideExceptionMaxObstructionWidthFt = 4.0`, `UnobstructedOpenCrossSectionFraction = 0.70`
(the Open scissor test cannot be measured from extracted geometry), min K-factor.

---

## Stage 2 - Room geometry preparation

- Outer boundary polygon + inner opening loops are cleaned: duplicate/closing vertices
  dropped (`CleanPolygonLoop`, `...:1869`), collinear vertices merged
  (`MergeCollinearVertices`, `...:1897`). Fewer than 3 outer vertices = degenerate, room
  rejected.
- Point test: `IsPointInsideRoom(p)` = inside outer loop AND inside no inner loop.
- `DistanceToOuterBoundary(p)` = minimum distance from `p` to any segment of the outer
  polygon.
- Obstacles are extracted near the room as axis-aligned boxes (`ObstacleBox`,
  `...:2785`: `MinX..MaxZ`, `Category`, `ClearanceFt` from `GetObstacleClearance`,
  `...:2409`). A box reports `MaxHorizontalDimensionFt = max(WidthFt, DepthFt)` and
  `DistanceToNearEdge(x, y)` - distance from a point to the box's nearest face, measured to
  the near edge, not the centre.

---

## Stage 3 - Find the substitutable adjusters (per room, in order)

Applied inside `CalculateRoom` before layout:

```
heightFactor   : Light hazard bump bands (ceiling height > 12 / 15 / 20 / 25 ft)
                 -> x 0.95 / 0.90 / 0.85 / 0.80 ; otherwise 1.0
slopeFactor    = GetCeilingSlopeAdjustment(primary.SlopeType)      (...:303)
combinedFactor = heightFactor * slopeFactor * ruleSet.CeilingHeightAdjustmentFactor   (...:307)
orientationFactor = GetOrientationAdjustment(orientation)          (...:322)
```

The layout maxima (max spacing and every limit derived from it: wall distance, radius
targets, tile-pitch caps) are scaled by `combinedFactor` and `orientationFactor` before any
layout math runs. In sidewall mode the coverage radius is additionally halved,
`ruleSet.CoverageRadiusFt = sidewallCoverage` with `sidewallCoverage = r / sqrt(2)`
(`...:339`) because a sidewall head protects a half-circle.

---

## Stage 4 - Point-count math (how many heads are needed)

Per-head protection areas, both re-reported at the end (`...:1448-1452`, ceiling modes;
`...:1797-1799`, sidewall):

```
ceiling modes : AreaPerHead = pi * r^2
                RequiredCount = ceil(A / AreaPerHead)            if AreaPerHead > 0
                RequiredCount = selected.Count                    otherwise

sidewall      : AreaPerHead = alongSpacing * throwDepth
                RequiredCount = ceil(A / AreaPerHead)            if AreaPerHead > 0 and A > 0
                RequiredCount = selected.Count                    otherwise
```

`RequiredCount` is a spacing-based estimate, never a hydraulic count. If the rule set is
provisional (`IsProvisional`), the room is flagged `ReviewRequired` with an explicit
"estimate" warning (`...:1454-1458`).

---

## Stage 5 - Candidate point creation

Candidate resolution (`ComputeGridResolution`, `...:2016`):

```
res = config.GridResolutionFt (= 1.0 ft)
if room bbox width >= 2*(r/2) and room bbox height >= 2*(r/2): res = max(res, r/2)
cap: res <= 4*r
while floor(bboxW/res)+2 > MaxCandidatePoints(=4000)- overpowering estimate and res < 25: res *= 2
```

Candidate fields depend on the mode:

- **Tile-anchored candidates.** If the primary ceiling has a readable tile grid, a
  `CeilingGrid` (`CeilingGridMath.cs`) is built: `OriginX/Y`, tile extents `UFt/VFt`,
  rotation `AngleRad`. Tile centres are enumerated inside bbox + 1 rim and filtered to the
  bbox. `cos`/`sin` are recomputed from `AngleRad` on demand (a cached rotation once snapped
  a point to a tile corner by mistake, so derivation is deliberately recomputed per point).
  If no readable grid exists and tile U/V sizes were given, a grid of that module drives the
  same math.
- **Free grid candidates.** Otherwise candidates sit on a rectangular lattice at `res`
  across the room bounding box; `validCandidates` are sorted by (Y, then X).

Candidate acceptance - `TryAcceptPoint` - has exactly three rejecting conditions; failing
any removes the point before selection:

```
1. not IsPointInsideRoom(px, py)                                    -> reject
2. DistanceToOuterBoundary < max(BoundaryClearanceFt, MinWallClearanceFt) - tol -> reject
3. exists a head in existingSprinklerXy with
   dist(px, py, existing) <= ExistingSprinklerSeparationFt - tol    -> reject
```

(`MinWallClearanceFt = 4/12` is the hard floor on condition 2, `...:429`.)

Obstacle proximity is **tracked, not rejected** here: candidate counts of
inside-an-obstacle / below-Three-Times-clearance / below-zone are incremented and reported;
no point is dropped for obstacles (decision 028-style no-obstacle-shift policy).

---

## Stage 6 - Feasible point selection

### 6.1 Free-grid branching (`SelectCenteredGrid`, `...:568`; loop verified `...:604-756`)

```
nx = (bboxW <= S + tol) ? 1 : ceil(bboxW / S - tol)
ny = (bboxH <= S + tol) ? 1 : ceil(bboxH / S - tol)
stepX = bboxW / nx
stepY = bboxH / ny
captureRadius = max(max(stepX, stepY) / 2, res)
```

For each cell target

```
tx = bboxMinX + stepX * (ix + 0.5)
ty = bboxMinY + stepY * (iy + 0.5)
```

in order:

1. Target blocked by an existing head: any existing head within
   `ExistingSprinklerSeparationFt - tol` -> skip (`cellsSkipped++`), `...:632-647`.
2. `IsPlacementValid(tx, ty, ...)` accepts -> head exactly on the target (`placedExact++`),
   `...:653-657`.
3. Else nearest unused candidate within `captureRadius + tol` -> snap there
   (`placedSnapped++`) - candidates are consummed (`used[]`), `...:660-671`.
4. Else obstacle-recovery expanded search: nearest unused candidate within
   `spacing / 2` (`expandedRadius = spacing / 2.0`, `...:686`) -> snap with
   `placedObstacleRecovered++`. Worst resulting neighbour gap is `S + S/2 = 1.5S`, which
   the finalize check flags as a warning - heads are never silently dropped,
   `...:684-700`.
5. Else `cellsSkipped++` and the cell is left empty.
6. Min-spacing check against every already selected head: if the new point is closer than
   `s_min - tol` to any selected point, skip (`...:711-727`).
7. If `selected.Count == 0` the room fails with
   "Centered-grid placement produced no sprinklers" (`...:758-767`).

Diagnostic line records the chosen parameters verbatim
`"Centered grid (free): S=..., nx=..., ny=..., stepX=..., stepY=..."` (`...:753-755`).

### 6.2 Tile-anchored branching (`SelectCenteredGridOnTiles`, `...:774`)

```
tileStepU = max(1, floor((S + tol) / UFt))
tileStepV = max(1, floor((S + tol) / VFt))
minStepU  = max(1, ceil((s_min - tol) / UFt))
minStepV  = max(1, ceil((s_min - tol) / VFt))
tileStepU = max(tileStepU, minStepU)     per axis (min-spacing override wins)
```

Per-axis target indices come from `TileCentricPatternGenerator.TrySolveBalancedLayout`
(`TileCentricPatternGenerator.cs`). The solver implements an X-2X-X rule: heads sit at tile
index pitch P apart plus a head offset from each wall, so a run of tiles looks like
`X - - - - 2X - - - - X` (`2X` = doubled tile centre): pitch `pitchFt = P * module`,
sidewall gap `X = pitchFt / 2`.

Backtracking search (for each axis of the room, bounded by the grid rotation):

```
localBounds        = project the room bbox corners onto the (rotated) grid axes
indexRange         = floor(lo / module) - 1 .. floor(hi / module) + 1
for pitch p from (topPitch) down to 1:
    pitchFt = p * module
    baseCount = round((hi - lo) / pitchFt)
    for n in { baseCount - 1, baseCount, baseCount + 1 } (n >= 1; single allowed when len < 2*module):
        avgGap = (len - (n - 1) * pitchFt) / 2
        accept when |avgGap - pitchFt/2| <= tolerance (# .5 / 1.0 tiles) and avgGap >= -tolerance
best start c0 minimizes symmetry = |w1 - w2| / 2 where
    w1 = (c0 + 0.5) * module - lo
    w2 = (hi) - (last + 0.5) * module
biggest pitch wins; pitch ratio along the two axes kept <= 2
```

Rationale (source comments): pure anchor snapping produces one-sided wall gaps (e.g. a
20-ft room with 2-ft tiles and S=15 -> side length 7 tiles at 14 ft pitch -> gaps
1 ft / 14 ft / 4 ft), while tile snapping of an X-2X-X point fails because the ideal pitch
is rarely a whole number of tiles. The solver instead picks one pitch and a start tile that
makes the two wall gaps as close to `pitchFt / 2` as the module allows.
`TrySolveBalancedLayout` does NOT validate tile usability (blocked-by-beams etc.) - that is
the snap logic's job below.

Placement per target: bounded snap among the 8 neighbouring tiles within

```
snapBudget = min(0.5 * UFt_or_VFt, slack/2 + tol)     slack = max(0, S - pitchFt)
```

If no such candidate exists: obstacle-recovery expanded search over
`extRange = max(tileStepU, tileStepV) + 1` tiles, within `spacing / 2`, closest first. If
both fail the cell is skipped and counted. Final safety net `SnapPositionsToTiles`
re-snaps anything off a tile without dropping devices or letting two heads share a tile.
Min-spacing and existing-separation rules apply exactly as in the free-grid branching.

### 6.3 Sidewall branching (`SelectSidewallDirectional`, `...:1478`)

```
alongSpacing (S_al) = ruleSet.MaxSpacingFt > 0 ? ruleSet.MaxSpacingFt : 10.0
throwDepth          = (typeCoverageArea > 0 and S_al > 0)
                      ? min(typeCoverageArea / S_al, GetNfpa13MaxSpacingCeiling(hazard))
                      : S_al                  # square-throw assumption when no listed area
endWallMax          = 0.5 * S_al             # half-spacing from end walls
standOff            = 0.5 ft                 # standoff off the wall plane
isCCW               = ComputeSignedArea(polygon) > 0          (...:1856)
```

Candidate rows per wall edge (`rowsPerEdge`): heads along walls at `standOff`, spaced
`S_al` along the wall, first row per wall starts at `endWallMax` from the end wall.
Parameterized by edge local axes derived from `isCCW`. Rows are chosen greedily: the
longest wall first, then the opposite wall if the room is deeper than one throw, then
whichever row removes the most uncovered floor samples. Coverage radius was already
`r / sqrt(2)` (Stage 3). If the depth exceeds any single throw the uncovered percentage is
reported; the room stays in review.

`selected.Count == 0` -> `NoValidCandidates`. Sidewall always sets `ReviewRequired` with
"verify against the manufacturer's listed coverage" (`...:1801-1810`).

---

## Stage 7 - Z plane, deflector drop, construction, small-room rule

### 7.1 Mounting plane (`SelectPrimaryCeiling`, `...:2059`)

Candidate ceilings ranked by: (1) ceilings on the room's own level first, (2)
`FLAT < SLOPED < STEPPED`, (3) then closest `heightDelta` to
`expectedCeilingZ = floor elevation + room ceiling height`. A candidate passing the band
gates becomes the plane; otherwise `placementZ` falls back to `floorZ + CeilingHeightFt`
(or the level elevation if no ceiling height was given) with an explicit warning
(`ceilingUnsupported`), `...:2128-2250`. Any `heightDelta > 1.0` warns.

```
sloped ceiling : placementZ = (ceiling bottom elevation + ceiling top elevation) / 2
flat ceiling   : placementZ = ceiling bottom elevation
final z        = placementZ - deflectorDrop
deflectorDrop  = shallowest legal drop = 1 in (min); band max = 12 in unobstructed,
                 6 in obstructed construction; sloped-peak warning if the drop exceeds 3 ft
```

### 7.2 Construction class (`ClassifyCeilingConstruction`, `...:2620s`)

```
spacing = EstimateMemberSpacing(members)
spacing > 7.5 ft -> UnobstructedMemberSpacing   ("members > 7.5 ft on centre"
                                                 are unobstructed regardless of solidity)
spacing < 3.0 ft -> ObstructedDenseMembers      ("members < 3 ft on centre"
                                                 are necessarily obstructed)
3.0 - 7.5 ft     -> Obstructed                  (the >70 % open cross-section test cannot be
                                                 measured from extracted geometry)
```

### 7.3 Small Room Rule (`TryApplySmallRoomRule`, `...:2718-2782`)

Applied only when ALL hold:

```
hazard is Light
0 < A < 800 sq ft                                   (SmallRoomMaxAreaSqFt)
construction class is UNOBSTRUCTED (not Obstructed / ObstructedDenseMembers)
ruleSet.MaxDistanceFromWallsFt >= S/2 - 1e-6        (never loosens a value that was
                                                     deliberately set tighter than S/2;
                                                     gate uses WallDistanceFractionOfMaxSpacing)
```

Effect: `D_wall` relaxed to `SmallRoomMaxWallDistanceFt = 9 ft`, and per-head coverage is
checked by **averaging** (`A / head count`) instead of per-head S x L. Condition 4 (walls +
ceiling with at least an 8 in lintel over openings - `SmallRoomLintelDepthFt`) cannot be
machine-verified from extraction data, so the rule always emits the reviewer-facing warning
while applying.

---

## Stage 8 - Finalize re-checks (`FinalizeSelection`, `...:1145`)

Run against the selected points only; every failure is advisory-to-review, no point is
silently dropped:

1. **Max spacing (per head).** For each head, distance to its NEAREST other selected head
   must be `<= S`. Checking every pair would flag the array's own diagonals; nearest-
   neighbour is the geometrically correct test - a head with no neighbour within `S` is a
   real local gap and flags review (worst head named).
2. **Wall distance.** Distance to the nearest wall of every head `<= D_wall`, else review.
3. **S x L coverage (`CheckCoverageBySxL`, `...:2179-2263`).** Branch lines run along the
   room's longer bbox axis (`branchLinesAlongX = bboxW >= bboxH`, `...:2190`). Per head,
   with wall distances taken per-axis from the bounding box:

   ```
   alongBranch  = min distance to a neighbour that is essentially aligned on the branch axis
                  (across-component <= tol)                    (...:2227)
   acrossBranch = min across-axis distance to any neighbour      (...:2228)
   S_head = max(alongBranch, 2 * wallAlongBranch)                (...:2232)
   L_head = max(acrossBranch, 2 * wallAcrossBranch)              (...:2233)
   area   = S_head * L_head
   area > Amax + 1e-6  -> overArea++, room -> review with worst head details (...:2244-2262)
   ```

   A head next to a wall covers twice its wall distance - that is the rule. Small-Room-Rule
   rooms use `A / headCount > Amax + 1e-6` (averaging) instead.
4. **Coverage advisory (pendent/upright).** The floor is sampled on a grid of step
   `max(min(EffectiveCoverageRadiusFt, S) / 3, 0.5)` (`...:1310`); any sample farther than
   `RemoteDistanceLimitFt = 0.7 * S` (`...:1374`, `HazardPlacementRuleSet.cs:64`) from every
   head (new or existing) is counted and the worst location reported. Advisory only.
5. **Obstacle conflicts.** Final heads are re-checked against obstacle boxes (including
   Three-Times: `required = min(3 * box.MaxHorizontalDimensionFt,
   ThreeTimesRuleMaxRequiredDistanceFt)` against `box.DistanceToNearEdge`, with the
   half-spacing centreline exception) - a hit only sets
   `point.HasObstacleConflict = true`. Obstacles never displace a selected point.
6. **Reporting.** `CalculatedCount = selected.Count`; `Points` = selected; `RequiredCount`
   from Stage 4; provisional rule set -> `ReviewRequired` + "estimate" warning
   (`...:1445-1458`).

---

## Worked example (every number from the verified code)

Room 30 x 20 ft, Light hazard, no readable tile grid, no type catalog data, no per-room
overrides, no ceiling tiles, flat ceiling, no existing heads.

```
Rules          : S=15, s_min=6, D_wall=7.5 (=S/2), Amax=225, r=7.5,
                 boundary=1.0 (>= 4 in floor), existingSep=6
combinedFactor : heightFactor=1.0 (ceiling <= 12 ft), slope=flat 1.0, class=1.0 -> 1.0
                 orientation pendent -> 1.0. All layout maxima unchanged.

Point count    : AreaPerHead = pi * 7.5^2 = 176.71 sq ft
                 RequiredCount = ceil(600 / 176.71) = ceil(3.393) = 4

Candidates     : no tile grid -> free grid at res = 1.0 ft
                 acceptance: inside polygon, wall >= 1.0 ft, away from existing heads

Selection      : nx = ceil(30/15 - tol) = 2 ; ny = ceil(20/15 - tol) = 2
                 stepX = 15, stepY = 10
                 captureRadius = max(max(15,10)/2, 1.0) = 7.5
                 targets: (7.5,5) (22.5,5) (7.5,15) (22.5,15)   (+ room bbox origin)
                 all four targets valid -> placedExact = 4

Z plane        : flat ceiling bottom - 1 in deflector drop

Finalize       : nearest-neighbour spacing = 15 <= S=15            PASS
                 wall distances 5..7.5 ft <= 7.5                   PASS
                 branch lines along X (30 >= 20):
                   S_head = max(15, 2*7.5) = 15
                   L_head = max(10, 2*5)  = 10
                   area = 150 <= Amax=225                          PASS
                 advisory sampler: worst sample = cell centre (15,10),
                   nearest head 9.01 ft <= remoteLimit 0.7*15 = 10.5   PASS

Result         : CalculatedCount = 4 = RequiredCount (the S x L check agrees with the
                 area/ pi r^2 estimate for this geometry)
```

---

## Source map

- `BruteForceCalculationService.cs` - `Calculate` :14, `CalculateRoom` :89,
  `SelectCenteredGrid` :568, `SelectCenteredGridOnTiles` :774, `FinalizeSelection` :1145,
  `SelectSidewallDirectional` :1478, `ComputeSignedArea` :1856, `ComputeGridResolution`
  :2016, `SelectPrimaryCeiling` :2059, `MakeObstacleBox` :2325, `BuildObstacleBoxes` :2397,
  `ClampToNfpa13MaxSpacing` :2540, `GetNfpa13MaxSpacingCeiling` :2559,
  `ClassifyCeilingConstruction` :2620s, `TryApplySmallRoomRule` :2718, `ObstacleBox` :2785.
- `TileCentricPatternGenerator.cs` - X-2X-X balanced solver.
- `CeilingGridMath.cs` - `CeilingGrid` struct, tile-centre enumeration.
- `BruteForceCalculationConfig.cs` - `ToleranceFt = 1e-6`, `GridResolutionFt = 1.0`,
  `MaxCandidatePoints = 4000`, `MaxSearchIterations = 500000`.
- `RoomGeometry.cs` - polygon cleaning, inside test, boundary distance.
- `DefaultHazardPlacementRules.cs` - provisional hazard table (NFPA 13 (2002) via the NFSA
  textbook, p.218/p.219/p.221), `HasApprovedRules = false`, every set `IsProvisional`.
- `HazardPlacementRuleSet.cs` - derived quantities and adjusters.
- `Nfpa13RulebookRules.cs` - rulebook constants and explicit `KnownGaps` (beam-rule table,
  pocket exceptions, sloped 2 ft intervals, etc.).

Values are provisional placeholders until a qualified FPE signs off the design basis
(`HasApprovedRules` flips then); no NFPA 13-2022 compliance is implied anywhere.
