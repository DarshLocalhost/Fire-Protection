# Location Points — How Each of the 3 Devices Decides *Where* to Sit

**Companion to** `sim_expl_2.md` (ELI5 candidate-location logic) and `explanation_2.md` (technical
class/method index).

> **Scope of this document.** A *location point* is the final X/Y/Z coordinate of a device, decided
> entirely inside the **Revit-free calculation engines**. It happens *before* Revit is involved.
> This file answers one question: **which files, methods and formulas produce that point, for
> sprinklers, smoke detectors and notification appliances.**
>
> **Status legend:** every numeric value in this codebase is currently **PROVISIONAL**.
> See [Provisional basis](#provisional-basis--read-this-before-trusting-any-number).

---

## Contents

1. [The one-page answer](#the-one-page-answer)
2. [Shared stage 0 — the placement profile](#shared-stage-0--the-placement-profile)
3. [Sprinkler location points](#1-sprinkler-location-points)
4. [Smoke detector location points](#2-smoke-detector-location-points)
5. [Notification appliance location points](#3-notification-appliance-location-points)
6. [File responsibility table](#file-responsibility-table)
7. [Shared formulas](#shared-formulas)
8. [Trace these first](#trace-these-first)
9. [Provisional basis](#provisional-basis--read-this-before-trusting-any-number)
10. [Known defects and uncertainties](#known-defects-and-uncertainties)

---

## The one-page answer

There is **no single "LocationPointCalculator" class**. Instead every device answers the same four
questions in the same order, and all three share **one helper class** plus **two geometry classes**.

```
  Q1  WHERE is Z?          → placementZ
  Q2  HOW FINE is the grid? → ComputeGridResolution(...)
  Q3  WHICH points exist?  → Generate*Candidates(...)
  Q4  WHICH points survive? → TryAccept / IsPlacementValid   (4 rejection rules)
  Q5  WHICH points win?    → Select*  (sprinkler differs from devices)
```

| | Sprinkler | Smoke detector | Notification appliance |
|---|---|---|---|
| Engine | `BruteForceCalculationService` (2431 lines) | `SmokeDetectorCalculationService` (1599 lines) | **same class** — reuses it |
| Q1 Z | `SelectPrimaryCeiling` + slope average | same + `ComputeNfpa72WallMountZ` | same |
| Q2 grid | `ComputeGridResolution` (sprinkler version) | `ComputeGridResolution` (**device version — different!**) | `notificationGridRes` |
| Q3 generate | `SelectSidewallDirectional` **or** inline lattice | `GenerateCeilingCandidates` / `GenerateWallMountCandidates` | `GenerateNotificationApplianceCandidates` |
| Q4 filter | `IsPlacementValid` | `TryAccept` (closure inside generator) | `TryAccept` (**looser**) |
| Q5 select | `SelectCenteredGrid` (array) | `SelectFromCandidates` (greedy + fast path) | `SelectFromCandidates` |
| Profile | `ResolveSprinklerProfile` | `ResolveSmokeDetectorProfile` | `ResolveNotificationApplianceProfile` |

### The shared files

| File | Role for all 3 devices |
|---|---|
| **`Services/Placement/LocationPoints/DeviceLocationPointIdentifier.cs`** | Declares the strategy + the resolved grid/min-spacing/coverage numbers, and writes the "why" into diagnostics. **Read-only metadata — it does not place anything.** |
| **`.../Sprinklers/Final/BruteForce/CeilingGridMath.cs`** | Snaps ceiling targets onto acoustic-tile centres. **Used by all three** even though it lives in the *sprinkler* namespace. |
| **`.../BruteForce/GeometryMath.cs`** | 4 primitives. Point-in-polygon, distance, expanded-box test. |
| **`.../BruteForce/RoomGeometry.cs`** | Wraps the room polygon; answers "is this inside?" and "how far from a wall?". |

> ⚠️ **Naming trap:** `DeviceLocationPointIdentifier` sounds like the place where points are
> computed. **It is not.** It returns a `DeviceLocationPointProfile` — a *description* of the
> strategy. The actual X/Y/Z comes from the generators. Confirmed by grep: the three
> `Resolve*Profile` methods are called only to feed `result.Diagnostics`.

---

## Shared stage 0 — the placement profile

**File:** `FireProtection.Backend/Services/Placement/LocationPoints/DeviceLocationPointIdentifier.cs`

Three methods, one per device. Each returns a `DeviceLocationPointProfile` describing mount,
strategy name, grid resolution, min spacing, coverage radius, placement Z and a prose
`SelectionBasis`.

### Sprinkler

```csharp
// DeviceLocationPointIdentifier.cs:30
public static DeviceLocationPointProfile ResolveSprinklerProfile(
    string placementBehavior, string orientation,
    double maxSpacingFt, double boundaryClearanceFt, double coverageRadiusFt,
    double placementZ, string roomId = null)
{
    bool isSidewall = string.Equals(placementBehavior, "WallSidewall", StringComparison.OrdinalIgnoreCase)
        || string.Equals(orientation, "sidewall", StringComparison.OrdinalIgnoreCase);

    double resolvedSpacing = maxSpacingFt > 0 ? maxSpacingFt : 15.0;
    double resolvedCoverage = coverageRadiusFt > 0 ? coverageRadiusFt : Math.Max(7.5, resolvedSpacing / 1.75);
    double resolvedMinSpacing = Math.Max(5.0, Math.Min(resolvedSpacing, resolvedCoverage));

    return new DeviceLocationPointProfile
    {
        DeviceKind = "Sprinkler",
        Mount = isSidewall ? "Wall" : "Ceiling",
        Strategy = isSidewall ? DeviceLocationPointStrategy.SprinklerSidewall
                              : DeviceLocationPointStrategy.SprinklerCeilingGrid,
        StrategyName = isSidewall ? "sprinkler-sidewall-edge-grid" : "sprinkler-ceiling-grid",
        GridResolutionFt = Math.Max(1.0, Math.Min(resolvedSpacing, resolvedCoverage)),
        MinSpacingFt = resolvedMinSpacing,
        CoverageRadiusFt = resolvedCoverage,
        PlacementZ = placementZ,
        SelectionBasis = isSidewall
            ? "Wall-sidewall candidate path selected from room perimeter; perimeter edge spacing controls location point selection."
            : "Ceiling candidate grid selected from room interior; interior spacing and boundary clearance drive location point selection."
    };
}
```

**In plain terms.** *Is this a wall sprinkler or a ceiling sprinkler? If wall, the layout comes from
walking the perimeter. If ceiling, it comes from a grid across the room's interior.*

**Called by** `BruteForceCalculationService.CalculateRoom` at line **373** — immediately before the
sidewall branch, so the profile is computed on every path and only *used* on the sidewall branch:

```csharp
// BruteForceCalculationService.cs:369-388
bool isSidewall =
    room.SelectedSprinklerPlacementBehavior == DevicePlacementBehavior.WallSidewall
    || string.Equals(room.SelectedSprinklerOrientation, "sidewall", StringComparison.OrdinalIgnoreCase);

var sprinklerLocationProfile = DeviceLocationPointIdentifier.ResolveSprinklerProfile(
    room.SelectedSprinklerPlacementBehavior.ToString(),
    room.SelectedSprinklerOrientation,
    ruleSet.MaxSpacingFt, ruleSet.BoundaryClearanceFt, ruleSet.CoverageRadiusFt,
    placementZ, room.RoomId);

result.Diagnostics.Add(
    "Location-point strategy=" + sprinklerLocationProfile.StrategyName
    + ", mount=" + sprinklerLocationProfile.Mount
    + ", grid=" + sprinklerLocationProfile.GridResolutionFt.ToString("F2") + " ft, ...");

if (isSidewall)
{
    return SelectSidewallDirectional(room, geometry, ruleSet, obstacleBoxes,
        existingSprinklerXy, placementZ, config, result, ceilingUnsupported, ceilingNote);
}
```

> 📌 **The `sprinklerLocationProfile` local is assigned and logged but never read again.** The
> sidewall solver recomputes its own `alongSpacing` / `throwDepth` from the rule set. Verified:
> no other reference to `sprinklerLocationProfile` in the file.

---

## 1. Sprinkler location points

**Primary file:** `FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/BruteForceCalculationService.cs`

### 1.1 Stage Q1 — Z

```csharp
// BruteForceCalculationService.cs:163-208 (abridged)
CeilingData bestCeiling = SelectPrimaryCeiling(room, out string selectionReason, result);

if (bestCeiling != null && bestCeiling.BottomElevationFt.HasValue)
{
    // For a SLOPED ceiling, BottomElevationFt is the LOW point and TopElevationFt the HIGH
    // point. A weighted average places the plane at the centroid height, closer to the
    // median sprinkler position than the low end. FLAT ceilings ignore it (top == bottom).
    if (string.Equals(bestCeiling.SlopeType, "SLOPED", StringComparison.OrdinalIgnoreCase)
        && bestCeiling.TopElevationFt.HasValue
        && bestCeiling.TopElevationFt.Value > bestCeiling.BottomElevationFt.Value)
    {
        double avg = (bestCeiling.BottomElevationFt.Value + bestCeiling.TopElevationFt.Value) / 2.0;
        placementZ = avg;
    }
    else
    {
        placementZ = bestCeiling.BottomElevationFt.Value;
    }
}
else
{
    if (room.CeilingHeightFt.HasValue)
    {
        placementZ = room.LevelElevationFt + room.CeilingHeightFt.Value;
        ceilingNote = "Ceiling elevation derived from room ceiling height (provisional).";
    }
    else
    {
        placementZ = room.LevelElevationFt;
        ceilingNote = "Ceiling elevation unavailable; Z set to floor level provisionally.";
    }
    ceilingUnsupported = true;
}
```

**In plain terms.**
```
sloped ceiling  →  Z = (bottom + top) / 2      // average, not the low edge
flat ceiling    →  Z = bottom
no ceiling      →  Z = floor + ceilingHeight   // then flag for review
no height       →  Z = floor                   // then flag for review
```

### 1.2 Stage Q2 — grid resolution

```csharp
// BruteForceCalculationService.cs:1933
private static double ComputeGridResolution(
    RoomGeometry geometry, HazardPlacementRuleSet ruleSet,
    BruteForceCalculationConfig config, out int estimate)
{
    double res = config.GridResolutionFt;                      // 1.0 ft default
    if (res > ruleSet.CoverageRadiusFt)
        res = ruleSet.CoverageRadiusFt;

    // Floor the grid step at half the coverage radius so every coverage diameter
    // is sampled by AT LEAST two candidate points.
    if (ruleSet.CoverageRadiusFt > 0)
    {
        double roomW = geometry.MaxX - geometry.MinX;
        double roomH = geometry.MaxY - geometry.MinY;
        double floor = ruleSet.CoverageRadiusFt / 2.0;
        if (floor > 0 && roomW >= 2.0 * floor && roomH >= 2.0 * floor)
        {
            if (res < floor) res = floor;
        }
    }

    estimate = EstimateGridPoints(geometry, res);
    int guard = 0;
    while (estimate > config.MaxCandidatePoints && res < 25.0 && guard < 64)
    {
        res *= 2.0;                                             // coarsen
        if (res > ruleSet.CoverageRadiusFt * 4.0) res = ruleSet.CoverageRadiusFt * 4.0;
        estimate = EstimateGridPoints(geometry, res);
        guard++;
    }
    return res;
}
```

**In plain terms.** *Start with a 1 ft grid. Never finer than the coverage radius. Never coarser than
half the coverage radius — so any one head's coverage always has at least two candidate points
sampled inside it. If the room is so big the grid would exceed the 4000-point budget, double the step
until it fits (never beyond 4× the coverage radius).*

### 1.3 Stage Q3 — candidate generation

Two mutually exclusive branches, chosen by `isSidewall`.

#### 1.3a Ceiling branch — an inline uniform lattice

```csharp
// BruteForceCalculationService.cs:405-425
double gridRes = ComputeGridResolution(geometry, ruleSet, config, out int gridPointEstimate);

int generated = 0, validCount = 0;
int rejectedBoundary = 0, rejectedObstacle = 0, rejectedExisting = 0, rejectedOutside = 0;

List<CandidatePoint> validCandidates = new List<CandidatePoint>();

// Deterministic ordering: ascending Y, then ascending X.
for (double y = geometry.MinY; y <= geometry.MaxY + config.ToleranceFt; y += gridRes)
{
    if (generated >= config.MaxCandidatePoints) break;
    for (double x = geometry.MinX; x <= geometry.MaxX + config.ToleranceFt; x += gridRes)
    {
        if (generated >= config.MaxCandidatePoints) break;
        generated++;

        CandidatePoint candidate = new CandidatePoint
        {
            X = x,
            Y = y,
            Z = placementZ
        };

        if (!geometry.IsPointInsideRoom(x, y, config.ToleranceFt)) { rejectedOutside++; continue; }
        if (geometry.DistanceToOuterBoundary(x, y) < ruleSet.BoundaryClearanceFt - config.ToleranceFt)
        { rejectedBoundary++; continue; }
        // ... obstacle + existing-sprinkler tests ...
    }
}
```

**In plain terms.** *Walk the room's bounding box in a raster, one grid step at a time, in reading
order. Test each point. Keep the survivors. The point's Z is simply the room-wide `placementZ`.*

#### 1.3b Sidewall branch — `SelectSidewallDirectional`

Same file, line **1171**. This is the only method that puts heads on walls.

**Step 1 — derive the three governing dimensions (line 1192):**

```csharp
// Along-wall spacing S: the orientation-adjusted MaxSpacingFt already carries the
// hazard ceiling and the sidewall orientation factor, so it is the correct S.
double alongSpacing = ruleSet.MaxSpacingFt > 0 ? ruleSet.MaxSpacingFt : 10.0;

double hazardCeiling = GetNfpa13MaxSpacingCeiling(ruleSet.HazardClass);
double throwDepth;
bool throwAssumedSquare = false;

if (room.TypeMaxCoverageAreaSqFt.HasValue && room.TypeMaxCoverageAreaSqFt.Value > 0 && alongSpacing > 0)
{
    // Listed protection area = S(along) x D(throw). Derive D, clamp to ceiling.
    throwDepth = Math.Min(room.TypeMaxCoverageAreaSqFt.Value / alongSpacing, hazardCeiling);
}
else
{
    // No listed area: treat the protected area as square (D = S).
    // This is a geometric assumption, NOT a manufacturer value - flag it.
    throwDepth = alongSpacing;
    throwAssumedSquare = true;
}

double endWallMax = alongSpacing * 0.5;   // derived S/2 at the row ends
double standOff   = 0.5;                  // inboard standoff from the wall face
```

**In plain terms.**
```
S  (along wall)   = the already-adjusted max spacing
D  (throw)        = listed coverage area ÷ S, capped at the hazard ceiling
                   ...or D = S if the catalog lists no area  (flagged as assumed)
corner limit      = S ÷ 2        // no head nearer than half a spacing to a corner
inboard standoff  = 0.5 ft       // so the head sits just off the wall face
```

**Step 2 — head stations along each edge (line 1270):**

```csharp
// Head positions along the edge: first/last <= endWallMax from the ends,
// interior steps <= alongSpacing. n = ceil((L - 2*end)/S) segments, evenly distributed.
double minSpacing = ruleSet.MinSpacingFt > 0 ? ruleSet.MinSpacingFt : 0.0;

List<double> dists = new List<double>();
if (edgeLen <= 2.0 * endWallMax)
{
    dists.Add(edgeLen * 0.5);                 // short wall: one centred head
}
else
{
    double span = edgeLen - 2.0 * endWallMax;              // usableSpan
    int segs = Math.Max(1, (int)Math.Ceiling(span / alongSpacing));
    double step = span / segs;

    // A wall only marginally longer than 2*endWallMax yields a tiny span, which would
    // drop two heads almost on top of each other (below min spacing).
    if (segs >= 1 && step < minSpacing)
    {
        dists.Add(edgeLen * 0.5);             // collapse to one centred head
    }
    else
    {
        for (int k = 0; k <= segs; k++) dists.Add(endWallMax + k * step);
    }
}

foreach (double d in dists)
{
    double bx = a[0] + ux * d, by = a[1] + uy * d;              // point ON the wall
    double cx = bx + nx * standOff, cy = by + ny * standOff;    // projected inboard
    if (!geometry.IsPointInsideRoom(cx, cy, config.ToleranceFt)) continue;
    // ... obstacle + existing tests ...
    row.Add(new SidewallHead { X = cx, Y = cy, WallEdgeIndex = i, AlongX = ux, AlongY = uy, InX = nx, InY = ny });
}
```

**In plain terms.**
```
usableSpan = wallLength − (2 × cornerClearance)
segs       = ceil(usableSpan ÷ alongSpacing)
step       = usableSpan ÷ segs
positions  = cornerClearance + k·step   for k = 0 … segs
```
*Take the wall length and remove the protected distance near both corners. Divide what's left into
equal pieces, each no wider than the allowed spacing. Place a head at each dividing point.*

**The outboard-obstacle exemption** (line 1310) is the key insight:

```csharp
// A sidewall head mounts against a wall, so an obstacle BEHIND the head
// (outboard, opposite the throw direction) is the wall it hangs on - its
// clearance margin must not reject the head. Only apply clearance to
// obstacles that lie in the throw path (inboard of the head).
double boxCx = (box.MinX + box.MaxX) * 0.5;
double boxCy = (box.MinY + box.MaxY) * 0.5;
double towardBox = (boxCx - cx) * nx + (boxCy - cy) * ny;
if (towardBox <= config.ToleranceFt) continue;   // behind/beside the head
```

**In plain terms.** *In a live model every wall is itself an obstacle. If clearance applied to all of
them, every single candidate would be rejected. So measure whether the obstacle is in front of the
head (its throw path) or behind it — behind is the wall itself, so ignore it.*

**Step 3 — selection (line 1368):** applies **whole wall rows**, never individual heads — seed the
longest wall, add the opposing wall when the room is deeper than one throw, then greedily fill.

```csharp
if (roomDepth > throwDepth + config.ToleranceFt)   // room genuinely deeper than one throw
{
    int opposite = -1;
    double bestAnti = 0.5;                        // require clearly anti-parallel
    for (int e = 0; e < rowsPerEdge.Count; e++)
    {
        if (usedEdge[e] || rowsPerEdge[e].Count == 0) continue;
        double dot = edgeUx[e] * edgeUx[longestEdge] + edgeUy[e] * edgeUy[longestEdge];
        if (-dot > bestAnti) { bestAnti = -dot; opposite = e; }
    }
    if (opposite >= 0) ApplyEdge(opposite);
}
```

**In plain terms.** *Wall heads are picked wall-by-wall. Start with the longest wall. If the room is
deeper than one throw can reach, the far wall is genuinely dry — add the wall most directly facing
the first. Then keep adding whichever wall covers the most uncovered floor until none helps.*

### 1.4 Stage Q4 — the filter

```csharp
// BruteForceCalculationService.cs:1107
private static bool IsPlacementValid(
    double x, double y, double placementZ,
    RoomGeometry geometry, HazardPlacementRuleSet ruleSet,
    List<ObstacleBox> obstacleBoxes, List<double[]> existingSprinklerXy,
    BruteForceCalculationConfig config)
{
    if (!geometry.IsPointInsideRoom(x, y, config.ToleranceFt)) return false;
    if (geometry.DistanceToOuterBoundary(x, y) < ruleSet.BoundaryClearanceFt - config.ToleranceFt) return false;

    foreach (ObstacleBox box in obstacleBoxes)
    {
        if (GeometryMath.InsideExpandedBox(x, y, box.MinX, box.MinY, box.MaxX, box.MaxY, box.ClearanceFt))
        {
            if (!box.SpansZ(placementZ, config.ToleranceFt)) continue;
            return false;
        }
    }

    foreach (double[] es in existingSprinklerXy)
    {
        if (GeometryMath.Distance(x, y, es[0], es[1]) <= ruleSet.ExistingSprinklerSeparationFt - config.ToleranceFt)
            return false;
    }
    return true;
}
```

**In plain terms.** Four rejections, first hit wins: *outside the room · too near a wall · inside an
obstacle at this height · too near a sprinkler that's already there.*

> 📌 The Z test reads `if (!box.SpansZ(...)) continue;` — a box that does **not** span Z is skipped,
> so only a box **at** the placement height blocks. Correct, but counter-intuitive on first read.

### 1.5 Stage Q5 — selection: a centered array, **not** greedy

```csharp
// BruteForceCalculationService.cs:912
double spacing = ruleSet.MaxSpacingFt;
if (spacing <= 0)
    spacing = ruleSet.CoverageRadiusFt > 0 ? ruleSet.CoverageRadiusFt : 12.0;

double width  = geometry.MaxX - geometry.MinX;
double height = geometry.MaxY - geometry.MinY;

// n lines per axis; step = dim/n (<= spacing); first line at step/2 (centered array).
int nx = width  <= spacing + config.ToleranceFt ? 1 : (int)Math.Ceiling(width  / spacing - config.ToleranceFt);
int ny = height <= spacing + config.ToleranceFt ? 1 : (int)Math.Ceiling(height / spacing - config.ToleranceFt);
double stepX = width  / nx;
double stepY = height / ny;

double captureRadius = Math.Max(Math.Max(stepX, stepY) / 2.0, gridRes);

for (int iy = 0; iy < ny; iy++)
{
    double ty = geometry.MinY + stepY * (iy + 0.5);
    for (int ix = 0; ix < nx; ix++)
    {
        double tx = geometry.MinX + stepX * (ix + 0.5);
        // 1) optionally snap onto the ceiling tile centre
        // 2) try this exact point            (IsPlacementValid)
        // 3) else nearest UNUSED candidate within captureRadius
        // 4) else reject against MinSpacingFt and skip the cell
    }
}
```

**In plain terms.**
```
nx      = ceil(roomWidth ÷ spacing)      // round UP on purpose
stepX   = roomWidth ÷ nx                 // real step is therefore ≤ spacing
firstX  = roomMinX + stepX ÷ 2          // half a step in from the wall
```
*How many sprinklers fit across? Round **up**, so the real gap is **smaller** than the limit —
conservative in the safe direction. Then the wall distance is automatically half a step, which is
≤ spacing ÷ 2, satisfying the wall rule for free.*

> ⚠️ Changing `Math.Ceiling` to `Math.Floor` here would break spacing in the **non-compliant**
> direction. This is the single most safety-critical rounding in the engine.

**Tile snapping** (`SelectCenteredGrid`, ~line 975) moves the centred target onto the nearest tile
centre *before* validity testing, so heads land on the acoustic-tile RCP.

---

## 2. Smoke detector location points

**Primary file:** `FireProtection.Backend/Services/Placement/SmokeDetectors/Final/BruteForce/SmokeDetectorCalculationService.cs`

### 2.1 Stage Q1 — Z, plus a wall-specific correction

```csharp
// SmokeDetectorCalculationService.cs:152-186
if (bestCeiling != null && bestCeiling.BottomElevationFt.HasValue)
{
    if (string.Equals(bestCeiling.SlopeType, "SLOPED", StringComparison.OrdinalIgnoreCase)
        && bestCeiling.TopElevationFt.HasValue
        && bestCeiling.TopElevationFt.Value > bestCeiling.BottomElevationFt.Value)
    {
        double avg = (bestCeiling.BottomElevationFt.Value + bestCeiling.TopElevationFt.Value) / 2.0;
        placementZ = avg;
    }
    else
    {
        placementZ = bestCeiling.BottomElevationFt.Value;
    }
}
else
{
    if (room.CeilingHeightFt.HasValue)
    {
        placementZ = room.LevelElevationFt + room.CeilingHeightFt.Value;
        ceilingNote = "Ceiling elevation derived from room ceiling height (provisional).";
    }
    else
    {
        placementZ = room.LevelElevationFt + 10.0;      // NOTE: +10, not +0 as in sprinklers
        ceilingNote = "Ceiling elevation unavailable; Z defaulted to floor + 10 ft provisionally.";
    }
    ceilingUnsupported = true;
}
```

Wall-mounted detectors get their own Z from `ComputeNfpa72WallMountZ` (line **1538**):

```csharp
private static double ComputeNfpa72WallMountZ(
    double ceilingZ, double floorZ, double dropFromCeilingFt,
    bool isNotificationAppliance, SmokeDetectorRoomCalculationResult result)
{
    double wallZ = ceilingZ - dropFromCeilingFt;     // default: just below the ceiling
    double affHeight = wallZ - floorZ;

    if (isNotificationAppliance)
    {
        const double minAffFt = 6.67;                // 80 in
        const double maxAffFt = 8.0;                 // 96 in

        if (affHeight > maxAffFt)      { wallZ = floorZ + maxAffFt; }   // too high -> drop
        else if (affHeight < minAffFt) { wallZ = floorZ + minAffFt; }   // too low  -> lift
    }
    else
    {
        if (affHeight < 0.0)          { wallZ = floorZ + 0.5; }        // below floor -> 6 in
    }
    return wallZ;
}
```

**In plain terms.** *For a wall device, start just under the ceiling. Then clamp the mounting height
above the floor into a legal band — 6 ft 8 in to 8 ft for a notification appliance, or 6 in for a
smoke detector if the ceiling is so low the drop would go below the floor.*

### 2.2 Branch selection

```csharp
// SmokeDetectorCalculationService.cs:248-256
bool isWallMount =
    string.Equals(ruleSet.Mount, "Wall", StringComparison.OrdinalIgnoreCase)
    || room.SelectedPlacementBehavior == DevicePlacementBehavior.WallSidewall;

DevicePlacementMode deviceMode = room.DeviceKind == DeviceKind.NotificationAppliance
    ? DevicePlacementMode.NotificationAppliance
    : DevicePlacementMode.SmokeDetector;

if (!isWallMount && deviceMode == DevicePlacementMode.SmokeDetector)
{
    ApplyBeamAndSlopeAdjustments(room, geometry, ruleSet, obstacleBoxes, placementZ, result);
}
```

Then (line **289**) three generators are selected:

```csharp
if (deviceMode == DevicePlacementMode.NotificationAppliance)
    candidates = GenerateNotificationApplianceCandidates(...);
else if (isWallMount)
{
    double wallZ = ComputeNfpa72WallMountZ(placementZ, room.LevelElevationFt,
        ruleSet.WallMountDropFromCeilingFt,
        deviceMode == DevicePlacementMode.NotificationAppliance, result);
    candidates = GenerateWallMountCandidates(room, geometry, ruleSet, obstacleBoxes,
        existingDetectorXy, wallZ, config, result);
}
else
{
    candidates = GenerateCeilingCandidates(geometry, ruleSet, obstacleBoxes,
        existingDetectorXy, placementZ, config, result, tileGrid);

    List<CandidatePoint> peakRowCandidates = GenerateSlopedCeilingPeakRowCandidates(
        room, geometry, ruleSet, obstacleBoxes, existingDetectorXy, placementZ, config, result);
    if (peakRowCandidates.Count > 0)
        candidates.AddRange(peakRowCandidates);
}
```

### 2.3 Stage Q2 — grid resolution (⚠️ different from sprinklers)

```csharp
// SmokeDetectorCalculationService.cs:1048
private static double ComputeGridResolution(
    RoomGeometry geometry, SmokeDetectorPlacementRuleSet ruleSet, BruteForceCalculationConfig config)
{
    double res = config.GridResolutionFt;                      // 1.0
    if (ruleSet.CoverageRadiusFt > 0 && res > ruleSet.CoverageRadiusFt)
        res = ruleSet.CoverageRadiusFt;

    if (ruleSet.CoverageRadiusFt > 0)
    {
        double roomW = geometry.MaxX - geometry.MinX;
        double roomH = geometry.MaxY - geometry.MinY;
        double floor = ruleSet.CoverageRadiusFt / 2.0;
        if (floor > 0 && roomW >= 2.0 * floor && roomH >= 2.0 * floor)
        {
            if (res < floor) res = floor;
        }
    }

    if (res <= 0) res = 1.0;
    return res;
}
```

**In plain terms.** *Same idea as sprinklers — but with **no coarsening loop**. The effective step is
therefore exactly `coverageRadius ÷ 2`, about **10.6 ft** with the default 21.213 ft radius.*

> ⚠️ **The advertised `MaxSpacingFt` (30 ft) does *not* drive this lattice.** The sprinkler engine
> derives `n = ceil(dim / S)` from spacing; the device engine derives the lattice from the coverage
> **radius** and picks spacing later, during selection.

### 2.4 Stage Q3 — ceiling candidates

```csharp
// SmokeDetectorCalculationService.cs:435
private static List<CandidatePoint> GenerateCeilingCandidates(
    RoomGeometry geometry, SmokeDetectorPlacementRuleSet ruleSet,
    List<ObstacleBox> obstacleBoxes, List<double[]> existingDetectorXy,
    double placementZ, BruteForceCalculationConfig config,
    SmokeDetectorRoomCalculationResult result, CeilingGridMath.CeilingGrid tileGrid)
{
    List<CandidatePoint> validCandidates = new List<CandidatePoint>();
    double gridRes = ComputeGridResolution(geometry, ruleSet, config);

    // Per-point filter shared by the lattice scan and the tile-center enumeration.
    bool TryAccept(double x, double y)
    {
        if (!geometry.IsPointInsideRoom(x, y, config.ToleranceFt)) { rejectedOutside++; return false; }
        if (geometry.DistanceToOuterBoundary(x, y) < ruleSet.MinBoundaryClearanceFt - config.ToleranceFt)
        { rejectedBoundary++; return false; }
        foreach (ObstacleBox box in obstacleBoxes)
        {
            if (GeometryMath.InsideExpandedBox(x, y, box.MinX, box.MinY, box.MaxX, box.MaxY, box.ClearanceFt))
            {
                if (!box.SpansZ(placementZ, config.ToleranceFt)) continue;
                rejectedObstacle++;
                return false;
            }
        }
        foreach (double[] es in existingDetectorXy)
        {
            if (GeometryMath.Distance(x, y, es[0], es[1])
                <= ruleSet.ExistingDetectorSeparationFt - config.ToleranceFt)
            { rejectedExisting++; return false; }
        }
        validCandidates.Add(new CandidatePoint { X = x, Y = y, Z = placementZ, IsValid = true, Score = 1.0 });
        return true;
    }

    if (tileGrid.IsValid)
    {
        // Tile-center candidates: enumerate every acoustic-tile centre and filter it.
        List<double[]> tileCenters = CeilingGridMath.EnumerateTileCenters(
            in tileGrid, geometry.MinX, geometry.MinY, geometry.MaxX, geometry.MaxY, config.ToleranceFt);
        foreach (double[] c in tileCenters) { TryAccept(c[0], c[1]); }
        return validCandidates;                     // free lattice is NOT scanned
    }

    for (double y = geometry.MinY; y <= geometry.MaxY + config.ToleranceFt; y += gridRes)
        for (double x = geometry.MinX; x <= geometry.MaxX + config.ToleranceFt; x += gridRes)
            TryAccept(x, y);

    return validCandidates;
}
```

**In plain terms.** *If the ceiling has a readable tile grid, use the tile centres as the candidate
list and skip the free lattice entirely. Otherwise sweep a coarse grid. Either way the same four
rejection rules apply.*

> ⚠️ `CandidatePoint.Score = 1.0` is a **constant**. It is never read for ranking in any engine.

### 2.5 Stage Q3 — wall-mounted candidates

```csharp
// SmokeDetectorCalculationService.cs:673
const double wallStandoffFt = 0.15;
double step = ruleSet.MaxSpacingFt > 0 ? ruleSet.MaxSpacingFt : 15.0;

for (int i = 0; i < polygon.Count; i++)
{
    double[] a = polygon[i];
    double[] b = polygon[(i + 1) % polygon.Count];

    double ex = b[0] - a[0];
    double ey = b[1] - a[1];
    double edgeLen = Math.Sqrt(ex * ex + ey * ey);
    if (edgeLen < 1e-6) continue;

    // Left-hand normal, then flip it if it points OUT of the room.
    double nxLeft = -ey / edgeLen;
    double nyLeft =  ex / edgeLen;
    double midX = (a[0] + b[0]) * 0.5;
    double midY = (a[1] + b[1]) * 0.5;

    bool leftIsInboard = geometry.IsPointInsideRoom(
        midX + nxLeft * 0.5, midY + nyLeft * 0.5, config.ToleranceFt);
    double nx = leftIsInboard ? nxLeft : -nxLeft;
    double ny = leftIsInboard ? nyLeft : -nyLeft;

    int n = Math.Max(1, (int)Math.Ceiling(edgeLen / step));
    for (int k = 0; k <= n; k++)                    // NOTE: k <= n  => n+1 points
    {
        double t = (double)k / (double)n;
        double cx = a[0] + ex * t + nx * wallStandoffFt;   // on the edge, pushed 0.15 ft inboard
        double cy = a[1] + ey * t + ny * wallStandoffFt;
        // ... outside / obstacle / existing tests ...
        candidates.Add(new CandidatePoint { X = cx, Y = cy, Z = wallZ, IsValid = true,
                                            Score = 1.0, WallEdgeIndex = i });
    }
}
```

**In plain terms.**
```
n     = ceil(edgeLength ÷ step)
for k = 0 … n:      ← n + 1 points, INCLUDING both endpoints
   t = k ÷ n
   point = edgeStart + t·edgeVector + 0.15 ft inboard
```
*Divide each wall into equal pieces and put a detector at every dividing point, including both
corners, nudged 0.15 ft off the wall.*

> ⚠️ Because the loop is `k <= n`, **both endpoints of every wall get a point**, so adjacent walls
> each emit a candidate at the shared corner — duplicates. Harmless (selection dedupes by coverage),
> but worth knowing.
>
> ⚠️ Unlike the sprinkler sidewall solver there is **no corner-clearance exclusion** and **no
> `MinBoundaryClearanceFt` test** — wall points are near a wall by construction.

### 2.6 Stage Q5 — selection: centroid fast path, then greedy

```csharp
// SmokeDetectorCalculationService.cs:788
// Centroid = MEAN OF VERTICES, not the area centroid.
double centroidX = (geometry.MinX + geometry.MaxX) / 2.0;
double centroidY = (geometry.MinY + geometry.MaxY) / 2.0;
ComputePolygonCentroid(geometry.OuterPolygon, ref centroidX, ref centroidY);

double maxCoverageArea  = ruleSet.MaxCoverageAreaSqFt > 0 ? ruleSet.MaxCoverageAreaSqFt : 900.0;
double roomMaxDimension = Math.Max(geometry.MaxX - geometry.MinX, geometry.MaxY - geometry.MinY);

// Single-device fast path
if (!isWallMount && room.AreaSqFt <= maxCoverageArea && roomMaxDimension <= (coverageRadiusFt * 1.8))
{
    CandidatePoint bestCenterCandidate = FindBestCentroidCandidate(
        validCandidates, centroidX, centroidY, geometry, obstacleBoxes, existingDetectorXy, config);

    if (bestCenterCandidate != null)
    {
        result.Points = new List<CalculatedSmokeDetectorPoint> { singlePoint };
        result.CalculatedCount = 1;
        result.RequiredCount = 1;
        result.Status = CalculationStatus.Success;              // ← unconditional
        VerifyMaxWallDistance(result.Points, geometry, ruleSet, config, isWallMount, result);
        return result;
    }
}

// Greedy ordering: nearest-to-centroid first (sprinklers sort by raster Y,X instead)
validCandidates.Sort((a, b) =>
{
    double distA = GeometryMath.Distance(a.X, a.Y, centroidX, centroidY);
    double distB = GeometryMath.Distance(b.X, b.Y, centroidX, centroidY);
    return distA.CompareTo(distB);
});

foreach (CandidatePoint candidate in validCandidates)
{
    if (iterations >= config.MaxSearchIterations) break;

    // already covered by an existing detector, or by one already selected?
    if (alreadyCovered) continue;

    // too close to one already selected?
    if (tooClose) continue;

    selected.Add(new CalculatedSmokeDetectorPoint { X = candidate.X, Y = candidate.Y, ... });
}
```

**In plain terms.** *Most rooms need one detector, so: if the room fits inside one coverage area,
just take the valid candidate nearest the middle and stop. Otherwise work outward from the middle,
adding a detector whenever the spot isn't already covered and isn't too close to one just placed.*

> ⚠️ **`roomMaxDimension <= coverageRadiusFt * 1.8`** — the `1.8` is an unnamed magic multiplier
> appearing nowhere else.
>
> ⚠️ `ComputePolygonCentroid` averages **vertices**, so for an L-shaped room it is not the area
> centroid.
>
> 🐛 **`result.Status = CalculationStatus.Success` is unconditional** and silently overwrites any
> `ReviewRequired` already set at line 224 for an unsupported/absent ceiling.

---

## 3. Notification appliance location points

**There is no separate notification appliance engine.** Notification appliances are a **branch inside
`SmokeDetectorCalculationService`**, selected by `DeviceKind`:

```csharp
// SmokeDetectorCalculationService.cs:252
DevicePlacementMode deviceMode = room.DeviceKind == DeviceKind.NotificationAppliance
    ? DevicePlacementMode.NotificationAppliance
    : DevicePlacementMode.SmokeDetector;
```

The input DTO is shared too — `SmokeDetectorRoomInput`. Only the rules object and `DeviceKind`
differ.

### 3.1 The generator

```csharp
// SmokeDetectorCalculationService.cs:338
private static List<CandidatePoint> GenerateNotificationApplianceCandidates(
    SmokeDetectorRoomInput room, RoomGeometry geometry, SmokeDetectorPlacementRuleSet ruleSet,
    List<ObstacleBox> obstacleBoxes, List<double[]> existingDetectorXy,
    double placementZ, BruteForceCalculationConfig config,
    SmokeDetectorRoomCalculationResult result, bool isWallMount,
    DeviceLocationPointProfile locationProfile, CeilingGridMath.CeilingGrid tileGrid)
{
    List<CandidatePoint> validCandidates = new List<CandidatePoint>();

    double baseGridRes          = ComputeGridResolution(geometry, ruleSet, config);
    double notificationGridRes  = Math.Max(1.0,
        Math.Min(baseGridRes, Math.Max(2.0, ruleSet.MaxSpacingFt / 3.0)));

    if (isWallMount)
    {
        double wallZ = placementZ - ruleSet.WallMountDropFromCeilingFt;
        return GenerateWallMountCandidates(room, geometry, ruleSet, obstacleBoxes,
            existingDetectorXy, wallZ, config, result);
    }

    // Per-point filter shared by the lattice scan and the tile-center enumeration.
    bool TryAccept(double x, double y)
    {
        if (!geometry.IsPointInsideRoom(x, y, config.ToleranceFt)) return false;

        // NOTE: clamped UP to at least 0.333 ft — this UNDOES the wall-mount 0.05 relaxation.
        if (geometry.DistanceToOuterBoundary(x, y)
            < Math.Max(ruleSet.MinBoundaryClearanceFt, 0.333) - config.ToleranceFt)
            return false;

        foreach (ObstacleBox box in obstacleBoxes)
        {
            if (GeometryMath.InsideExpandedBox(x, y, box.MinX, box.MinY, box.MaxX, box.MaxY,
                Math.Max(box.ClearanceFt, ruleSet.ObstacleClearanceFt)))
            {
                if (!box.SpansZ(placementZ, config.ToleranceFt)) continue;
                return false;
            }
        }

        foreach (double[] es in existingDetectorXy)
        {
            if (GeometryMath.Distance(x, y, es[0], es[1])
                <= Math.Max(ruleSet.ExistingDetectorSeparationFt, ruleSet.MinSpacingFt) - config.ToleranceFt)
                return false;
        }

        validCandidates.Add(new CandidatePoint { X = x, Y = y, Z = placementZ, IsValid = true, Score = 1.0 });
        return true;
    }

    if (tileGrid.IsValid)
    {
        List<double[]> tileCenters = CeilingGridMath.EnumerateTileCenters(
            in tileGrid, geometry.MinX, geometry.MinY, geometry.MaxX, geometry.MaxY, config.ToleranceFt);
        foreach (double[] c in tileCenters) TryAccept(c[0], c[1]);
        return validCandidates;
    }

    for (double y = geometry.MinY; y <= geometry.MaxY + config.ToleranceFt; y += notificationGridRes)
        for (double x = geometry.MinX; x <= geometry.MaxX + config.ToleranceFt; x += notificationGridRes)
            TryAccept(x, y);

    return validCandidates;
}
```

**In plain terms.** *Appliances are closer together than smoke detectors, so the grid is finer:
roughly `spacing ÷ 3`, but never coarser than the base grid and never below 2 ft.*

### 3.2 The finer grid, in plain terms

```
baseGridRes         = the device engine's coverageRadius ÷ 2
notificationGridRes = max( 1.0, min( baseGridRes, max(2.0, spacing ÷ 3) ) )
```
*Take the base grid, but if the spacing rule asks for something finer than 2 ft, use `spacing ÷ 3`.*

### 3.3 Where candela and dBA enter

**They do not influence the location point.** Candela and dBA are used **after** placement, by the
audible-coverage validator:

```csharp
// AudibleCoverageEngine.cs:137
double distance = GeometryMath.Distance(sx, sy, appliance.X, appliance.Y);
if (distance < MinSampleDistanceFt) distance = MinSampleDistanceFt;   // 0.5 ft floor

// Inverse-square-law attenuation from the UL 10 ft reference distance.
// sourceDbaAt10Ft is the manufacturer's listed output at 10 ft (e.g. 90 dBA).
double attenuatedDb = sourceDbaAt10Ft - 20.0 * Math.Log10(distance / ReferenceDistanceFt);
attenuatedDb -= DefaultSurfaceAbsorptionDb;                          // 3 dB

int wallCrossed = CountWallCrossings(sx, sy, appliance.X, appliance.Y, geometry);
if (wallCrossed > 0)
    attenuatedDb -= wallCrossed * DefaultWallAttenuationDb;          // 2 dB per wall

if (attenuatedDb > totalDb) totalDb = attenuatedDb;                  // loudest wins
```

and the requirement:

```csharp
// AudibleCoverageEngine.cs:128
private static double CalculateRequiredDb(double ambientDb, double maxSustainedDb, bool isSleepingArea)
{
    if (isSleepingArea)
        return Math.Max(75.0, Math.Max(ambientDb + 15.0, maxSustainedDb + 5.0));
    return Math.Max(ambientDb + 15.0, maxSustainedDb + 5.0);
}
```

**In plain terms.** *Sound gets quieter the further you are: listed dBA minus 20·log₁₀(distance ÷ 10).
At 20 ft that's −6 dB; at 40 ft, −12 dB. Then subtract 3 dB for the room, 2 dB per wall between you
and the appliance, and 3 dB if something solid is in the way. The room passes if the loudest point
still beats 15 dB above the background noise (5 dB above the loudest sustained noise), with a 75 dB
floor for sleeping areas.*

> 📌 The **visible** (candela) half is driven purely by the spacing rules — there is no candela-based
> geometry anywhere in the codebase.

---

## File responsibility table

| Device | File | Method | Line | Stage |
|---|---|---|---|---|
| **All** | `Services/Placement/LocationPoints/DeviceLocationPointIdentifier.cs` | `ResolveSprinklerProfile` | 30 | profile |
| **All** | ″ | `ResolveSmokeDetectorProfile` | 69 | profile |
| **All** | ″ | `ResolveNotificationApplianceProfile` | 118 | profile |
| **Sprinkler** | `Sprinklers/Final/BruteForce/BruteForceCalculationService.cs` | `CalculateRoom` | 95 | orchestrator |
| | ″ | `SelectPrimaryCeiling` | 1995 | Q1 Z |
| | ″ | `ComputeGridResolution` | 1933 | Q2 grid |
| | ″ | inline lattice loop | 417 | Q3 generate (ceiling) |
| | ″ | `SelectSidewallDirectional` | 1171 | Q3 generate (sidewall) |
| | ″ | `IsPlacementValid` | 1107 | Q4 filter |
| | ″ | `SelectCenteredGrid` | 912 | Q5 select |
| | ″ | `SampleCoveredByRow` | 1542 | sidewall coverage test |
| | ″ | `FinalizeSelection` | 707 | validation |
| | ″ | `GetNfpa13MaxSpacingCeiling` | 2373 | rule clamp |
| | ″ | `ClampToNfpa13MaxSpacing` | 2343 | rule clamp |
| **Sprinkler + all** | `Sprinklers/Final/BruteForce/CeilingGridMath.cs` | `SnapToTileCenter` | 96 | tile snap |
| | ″ | `EnumerateTileCenters` | 158 | tile candidate list |
| | ″ | `TryResolveRoomGrid` | 129 | grid source |
| **Smoke** | `SmokeDetectors/Final/BruteForce/SmokeDetectorCalculationService.cs` | `CalculateRoom` | 99 | orchestrator |
| | ″ | `ComputeNfpa72WallMountZ` | 1538 | Q1 Z (wall) |
| | ″ | `ComputeGridResolution` | 1048 | Q2 grid |
| | ″ | `GenerateCeilingCandidates` | 435 | Q3 generate (ceiling) |
| | ″ | `GenerateWallMountCandidates` | 673 | Q3 generate (wall) |
| | ″ | `GenerateSlopedCeilingPeakRowCandidates` | 545 | Q3 extra (peak row) |
| | ″ | `SelectFromCandidates` | 788 | Q5 select |
| | ″ | `FindBestCentroidCandidate` | 993 | Q5 fast path |
| | ″ | `ComputePolygonCentroid` | 973 | centroid |
| | ″ | `VerifyMaxWallDistance` | 934 | validation |
| **Notification** | ″ (same file) | `GenerateNotificationApplianceCandidates` | 338 | Q3 generate |
| **Notification** | `NotificationAppliances/Final/BruteForce/AudibleCoverageEngine.cs` | `CalculateSampleDb` | 137 | audible check |
| | ″ | `CalculateRequiredDb` | 128 | audible check |
| | ″ | `CountWallCrossings` | 226 | audible check |
| | `NotificationAppliances/NotificationAppliancePlacementInputBuilder.cs` | `BuildSnapshot` | 14 | builds the shared DTO |
| **All** | `Sprinklers/Final/BruteForce/GeometryMath.cs` | 4 primitives | 17–92 | geometry |
| **All** | `Sprinklers/Final/BruteForce/RoomGeometry.cs` | `IsPointInsideRoom` | 53 | geometry |
| | ″ | `DistanceToOuterBoundary` | 74 | geometry |

---

## Shared formulas

### Ceiling tile snapping — `CeilingGridMath.SnapToTileCenter`

```csharp
double cos = Math.Cos(grid.AngleRad);
double sin = Math.Sin(grid.AngleRad);

// World -> local (grid) frame: subtract origin, rotate by -angle.
double dx = tx - grid.OriginX;
double dy = ty - grid.OriginY;
double lu =  dx * cos + dy * sin;    // component along û
double lv = -dx * sin + dy * cos;    // component along v̂

int iu = (int)Math.Floor(lu / grid.UFt);   // which tile are we in?
int iv = (int)Math.Floor(lv / grid.VFt);

double cu = (iu + 0.5) * grid.UFt;          // move to that tile's centre
double cv = (iv + 0.5) * grid.VFt;

// Local -> world: rotate by +angle, add origin.
sx = grid.OriginX + cu * cos - cv * sin;
sy = grid.OriginY + cu * sin + cv * cos;
```

**In plain terms.** *Subtract the grid origin, spin the point so the grid runs left-to-right, work out
which tile square it lands in (integer division), then step half a tile past that square's corner —
the tile's centre. Spin back and re-add the origin.*

> 📌 `FromTileSize` anchors at world **(0,0)** deliberately. The comment explains: anchoring at the
> room's bounding-box corner put tile centres on the drawn grid **lines**, because a corner is not a
> tile edge. World-anchoring lands them on the **centres**.

### Ceiling grid minimum pitch

```csharp
private const double MinPitchFt = 0.25; // a tile smaller than 3 in is not a real ceiling grid
```

### Room height fallback chain (extraction, feeds `CeilingHeightFt`)

```
bounding-box Z extent  >  0.5 ft   →  use it
else Room.UnboundedHeight          →  use it
else                               →  20.0 ft   (hardcoded fallback)
```

### Wall-distance bounds — both are enforced, in opposite directions

| Rule | Value | Meaning | Enforced |
|---|---|---|---|
| `BoundaryClearanceFt` | 1.0 / 1.5 / 2.0 ft | **minimum** distance from a wall | during candidate filtering |
| `MaxDistanceFromWallsFt` | = `MaxSpacingFt / 2` | **maximum** distance from a wall | after selection |

---

## Trace these first

Ordered easiest → deepest. Each is a real file:line.

1. **`FireProtectionApplication.OnStartup`** — `Revit/FireProtectionApplication.cs:23` — ribbon creation
2. **`FireProtectionCommand.Execute`** — `Commands/FireProtectionCommand.cs:22` — the composition root; builds every service
3. **`UiLauncher.Show`** — `FireProtection.UI/Services/UiLauncher.cs:71` — opens the modeless window, sets the owner handle
4. **`SprinklerBruteForceViewModel.ExecutePlaceSprinklers`** — `FireProtection.UI/ViewModels/Sprinklers/BruteForce/SprinklerBruteForceViewModel.cs:755` — the UI entry point
5. **`DevicePlacementViewModelBase.ExecutePlaceDevices`** — `FireProtection.UI/ViewModels/Devices/DevicePlacementViewModelBase.cs:823` — device entry point
6. **`RevitApiContext.Run`** — `Services/RevitApiContext.cs:44` — why every Revit call is marshalled
7. **`FireProtectionExtractionService.ExtractModelSnapshot`** — `FireProtection.Backend/Services/Extraction/FireProtectionExtractionService.cs:30` — the extraction order
8. **`GeometryMath.PointInPolygon`** — `BruteForce/GeometryMath.cs:17` — the containment primitive
9. **`GeometryMath.InsideExpandedBox`** — `BruteForce/GeometryMath.cs:87` — the obstacle primitive
10. **`RoomGeometry.IsPointInsideRoom`** — `BruteForce/RoomGeometry.cs:53` — outer minus holes
11. **`RoomGeometry.DistanceToOuterBoundary`** — `BruteForce/RoomGeometry.cs:74` — drives both wall rules
12. **`DeviceLocationPointIdentifier`** (all 3 methods) — `FireProtection.Backend/Services/Placement/LocationPoints/DeviceLocationPointIdentifier.cs:30`, `:69`, `:118` — declares the strategy
13. **`DefaultHazardPlacementRules.GetRules`** — `BruteForce/DefaultHazardPlacementRules.cs:23` — the per-hazard table
14. **`Nfpa72SmokeDetectorRules.GetRules`** — `FireProtection.Backend/Services/Placement/SmokeDetectors/Final/BruteForce/Nfpa72SmokeDetectorRules.cs:17` — the device table
15. **`GetNfpa13MaxSpacingCeiling`** — `BruteForce/BruteForceCalculationService.cs:2373` — the 15 ft / 12 ft clamp
16. **`CeilingGridMath.SnapToTileCenter`** — `BruteForce/CeilingGridMath.cs:96` — tile snapping math
17. **`BruteForceCalculationService.CalculateRoom`** — `:95` — the whole sprinkler flow in one method
18. **`IsPlacementValid`** — `:1107` — the four sprinkler rejection rules
19. **`ComputeGridResolution`** (sprinkler) — `:1933` — grid sizing **with** the coarsening loop
20. **`SelectCenteredGrid`** — `:912` — the nx × ny array; `Math.Ceiling` is safety-critical
21. **`SelectSidewallDirectional`** — `:1171` — spacing, throw, corner clearance, seeding, greedy
22. **`FinalizeSelection`** — `:707` — the four post-selection checks
23. **`SmokeDetectorCalculationService.CalculateRoom`** — `:99` — the whole device flow
24. **`GenerateCeilingCandidates`** — `:435` — tile centres or a lattice
25. **`GenerateWallMountCandidates`** — `:673` — perimeter walk with a 0.15 ft standoff
26. **`GenerateNotificationApplianceCandidates`** — `:338` — the finer appliance grid
27. **`SelectFromCandidates`** — `:788` — centroid fast path + greedy
28. **`AudibleCoverageEngine.CalculateSampleDb`** — `:137` — the dBA attenuation formula

---

## Provisional basis — read this before trusting any number

**Every rule set in this codebase self-declares as unapproved.**

```csharp
// DefaultHazardPlacementRules.cs:19
public bool HasApprovedRules => false; // Set to true after engineering review

// Nfpa72SmokeDetectorRules.cs:15 — same, and so does Nfpa72NotificationApplianceRules
```

Consequences, all verified:

- `result.IsProvisional = !rules.HasApprovedRules` is therefore **always true**.
- The sprinkler engine sets `CalculationStatus.ReviewRequired` per room from it.
  **The smoke/device engine does not** — it only emits a root-level warning, so a device room can
  report `Success` using unapproved values.
- Every element placed by the sprinkler service is stamped in
  `ALL_MODEL_INSTANCE_COMMENTS` with `"PROVISIONAL RULES - NOT NFPA-13 APPROVED"`.

### Where the numbers came from

`DefaultHazardPlacementRules`'s own header comment states the values are traced to
**NFPA 13 (2002)** as reproduced in the client NFSA textbook *"Layout, Detail and Calculation of Fire
Sprinkler Systems"*:

| Quantity | Value | Cited page |
|---|---|---|
| Max spacing, light/ordinary | 15 ft | p.218 |
| Max spacing, extra hazard | 12 ft | p.218 |
| Min spacing (cold solder) | 6 ft | p.218 |
| Max distance to wall | = S ÷ 2 | p.219 |
| Coverage areas | 225 / 130 / 100 sq ft | Table 19-1, p.221 |

**But:**

- The 2002 edition is itself superseded.
- The comment explicitly says the values are **"NOT AHJ/FPE-verified, engineering review required"**.
- **The obstacle and boundary clearance values (1.0 / 1.5 / 2.0 ft) carry no page citation at all.**
  They are stand-ins.
- `STANDARDS_MEMORY.md` rule `SPR-ALL` records: *"PDF 2 is an image-only scan with no OCR; zero
  numeric values have been read."* So no number could be read from the actual sprinkler source.
- **Edition mismatch, unresolved:** the rules cite NFPA 13 **(2002)** while `PlacementInputBuilder`
  stamps `Project.Standard = "NFPA13-2022"` onto the snapshot.

### Unverified device values

`Nfpa72SmokeDetectorRules` uses 30 / 10 / 900 / 21.213 / 15 / 0.333 for a smooth ceiling and
60 / 15 / 3600 / 30 / 30 / 0.5 for a beam detector, plus the ACH table
125/250/375/500/625/750/875/900. Its header states the *logic* is real but the *values* are
unverified until an AHJ-approved design basis is supplied.

> 🐛 **That header also promises** *"every run marks affected rooms ReviewRequired"* — **but the
> service never reads `ruleSet.IsProvisional`.** Only the sprinkler path does.

---

## Known defects and uncertainties

Marked **PROVEN** (verified by reading the code) or **UNKNOWN** (cannot be established from the repo).

### PROVEN — real defects in the current code

| # | Where | Issue |
|---|---|---|
| 1 | `SmokeDetectorCalculationService.cs:844` | The single-device fast path sets `Status = Success` **unconditionally**, silently erasing a `ReviewRequired` set at line 224 for an unsupported/absent ceiling. |
| 2 | `SmokeDetectorCalculationService.cs:809` | `ruleSet.IsProvisional` is never consulted, contradicting the rule file's own documented promise. |
| 3 | `SmokeDetectorCalculationService.cs:973` | `ComputePolygonCentroid` is an **unweighted mean of vertices** — not the area centroid. Wrong for L-shaped rooms. |
| 4 | `SmokeDetectorCalculationService.cs:584-587` | `peakIsMaxX = true` and the other three peak flags `false`, so 3 of 4 peak-zone branches at lines 604-606 are **unreachable**. |
| 5 | `SmokeDetectorCalculationService.cs:819` | `roomMaxDimension <= coverageRadiusFt * 1.8` — the `1.8` is an **unnamed magic multiplier**. |
| 6 | `SmokeDetectorCalculationService.cs:1580` | `ParseDbaFromDescriptor` is declared and **never called** (dead code). |
| 7 | `SmokeDetectorCalculationService.cs:725-727` | `k <= n` emits `n+1` points per wall edge, so **adjacent walls duplicate their shared corner**. |
| 8 | `SmokeDetectorCalculationService.cs:376` | `Math.Max(ruleSet.MinBoundaryClearanceFt, 0.333)` **undoes** the wall-mount 0.05 relaxation on notification appliances. |
| 9 | `SmokeDetectorCalculationService.cs:809` | The literal `21.213` is duplicated here as a fallback, also present in `SmokeDetectorPlacementRuleSet.cs:80`. |
| 10 | `BruteForceCalculationService.cs:585, 1580` | `SelectFromCandidates` and `GenerateSidewallCandidates` have **no call sites** (dead), yet their doc comments still describe them as the live sidewall path. |
| 11 | `HazardPlacementRuleSet.cs:87-89` | `CoveragePatternAdjustments` is defined with an accessor but has **no consumer** anywhere. |
| 12 | `AudibleCoverageEngine.cs:22` | `DefaultDoorAttenuationDb = 6.0` is declared and **never used**, despite the class summary claiming "door/wall reduction". |
| 13 | `BruteForceCalculationService.cs:933` | `minSpacingFt` defaults to `0.0` on the `SelectCenteredGrid` path — **no** fallback to `CoverageRadiusFt`, unlike the dead greedy. |
| 14 | `RoomExtractor.cs:289-292` | `ResolveRoomLevel` matches bare numeric `LevelId` across documents — **can collide**. |
| 15 | `RoomExtractor.cs:370, 383` / `ExistingSprinklerExtractor.cs:128` | `LengthFt`, `RadiusFt` and `LevelElevationFt` are read **pre-transform**; valid only while link scale is exactly 1.0. |

### UNKNOWN — cannot be established from the repository

- **Whether any of these numbers match the client's actual design basis.** Only an FPE can say.
  `STANDARDS_MEMORY.md` says the source PDF is unreadable.
- **Whether any of it works at runtime.** Everything in this document is *static* verification —
  reading the code. No live Revit run backs any of it.
- **Whether the ceiling tile grid phase is right.** `CeilingGridMath.FromTileSize` anchors at world
  (0,0) by design, but the code itself logs *"RCP origin/phase provisional"*. Real models may use a
  different phase.
- **Whether `CountWallCrossings` matches real acoustics.** It counts polygon crossings only —
  no material, no diffraction, no door treatment (see defect 12).
- **`ObstacleBox` Z semantics.** An obstacle built from `CenterPoint + DimensionsFt` without a
  `BoundingBox` gets `MinZ = MaxZ = NaN`, and `SpansZ` then returns `true` unconditionally — it
  blocks at **every** height, with no diagnostic.

### Where the location point is *not* decided

For completeness — the following affect the final element but **not** the calculated X/Y/Z:

| Concern | Where |
|---|---|
| Which Revit host / overload is used | `Sprinklers/Final/Strategies/*.cs`, dispatched at `RevitSprinklerPlacementService.cs:724` |
| Level association, elevation offset | `Strategies/LevelAssociation.cs` |
| Ceiling host lookup across links | `Strategies/CeilingHostResolver.cs` |
| Room polygon → host space (devices only) | `FireAlarmDevicePlacementCore.TransformPolygonToHostSpace` |
| Post-placement spatial validation | `RevitSprinklerPlacementService` / `FireAlarmDevicePlacementCore` |

A calculated point can therefore be *geometrically correct* and still fail to place in Revit — see
`SPRINKLER_ACTUAL_Z_DIAGNOSTIC.md` for a worked case.

---

## Related documentation

- `sim_expl_2.md` — ELI5 companion covering the same logic
- `explanation_2.md` — technical candidate-location index for all three devices
- `sourced.md` — rule provenance audit
- `STANDARDS_MEMORY.md` — rule confidence register
- `DEVICE_PLACEMENT_RULES.md` — device branch overview
- `SPRINKLER_POINT_CALCULATION_EXPLAINED.md` — X/Y/Z deep dive
- `.archify/architecture-fps-runtime-20261005-101500/01-system-architecture.html` — interactive
  architecture + source-code map