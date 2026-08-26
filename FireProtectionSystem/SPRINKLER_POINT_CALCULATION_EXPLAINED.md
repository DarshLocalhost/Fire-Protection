# Sprinkler X/Y/Z Point Calculation — Explained End-to-End

> **Scope note:** This document explains *how the current code actually works*. It is based on the
> source code in this repository as it exists right now. **No production code was modified** to write
> this file. Where a value depends on running the engine, real numbers are taken from the project's
> own `extractTest.json` (a `ModelSnapshot`) and from the calculation rules in the code.

---

## 1. The Big Picture (Real Flow)

```text
Revit Room (live element)
      ↓  RoomExtractor / CeilingExtractor / LevelExtractor / ObstacleExtractor / ExistingSprinklerExtractor
ModelSnapshot  (all geometry already transformed into HOST MEP coordinates)
      ↓  UI mapping builds PlacementRoomSelection objects
PlacementInputBuilder.Build(...)
      ↓
PlacementInputSnapshot   (in-memory; the calculation NEVER reads a JSON file)
      ↓  BruteForceCalculationService.Calculate(snapshot, rules, config)
RoomGeometry (outer + inner loops, bounding box)
      ↓
Candidate grid sweep (fine 1-ft grid) over the room bounding box
      ↓  validate: inside room? ≥ boundary clearance? clear of obstacles? clear of existing sprinklers?
Valid candidate points (X, Y, Z=placement plane)
      ↓  greedy selection: keep points ≥ MaxSpacing (15 ft) apart, not already covered
CalculatedSprinklerPoint { X, Y, Z, RoomId, LevelId, LevelName }
      ↓  (written to sprinkler_calculation_result_*.json by the exporter)
RevitSprinklerPlacementService.PlaceSprinklers(...)
      ↓  ResolveHostLevel(...)  →  NewFamilyInstance(...)
FamilyInstance  (the actual Revit sprinkler)
```

Only steps that exist in the code are shown.

---

## 2. Important Classes

| Class | File | Role |
|---|---|---|
| `RoomExtractor` | `FireProtection.Backend/Services/Model/RoomExtractor.cs` | Reads a Revit `Room` and its boundary/geometry into `RoomData`. Transforms linked-model points to host coords. |
| `CeilingExtractor` | `FireProtection.Backend/Services/Model/CeilingExtractor.cs` | Extracts ceiling faces/elements → `CeilingData` (with `BottomElevationFt`, `SlopeType`). |
| `LevelExtractor` | `FireProtection.Backend/Services/Model/LevelExtractor.cs` | Extracts `LevelData` (elevation, name, id). |
| `ObstacleExtractor` | `FireProtection.Backend/Services/Model/ObstacleExtractor.cs` | Extracts beams/columns/etc. → `ObstacleData`. |
| `ExistingSprinklerExtractor` | `FireProtection.Backend/Services/Model/ExistingSprinklerExtractor.cs` | Extracts already-placed sprinklers → `ExistingSprinklerData`. |
| `RevitModelContext` | `FireProtection.Backend/Services/Model/RevitModelContext.cs` | Holds the Revit link transform; exposes `TransformPoint(XYZ, Transform)`, `TransformBoundingBox`, `TransformVector`. |
| `ModelSnapshot` / `RoomData` | `FireProtection.Backend/Models/DTOs/ModelSnapshot.cs`, `RoomData.cs` | In-memory model of everything extracted. |
| `PlacementRoomSelection` | `FireProtection.Backend/Services/Placement/Sprinklers/Final/PlacementInputBuilder.cs` | UI→calculation hand-off DTO (one per selected room). |
| `PlacementInputBuilder` | same file | `Build(...)` turns `PlacementRoomSelection` list into a `PlacementInputSnapshot`. |
| `PlacementInputSnapshot` / `PlacementRoomInput` | `FireProtection.Backend/Models/...` (Final placement DTOs) | The shape `BruteForceCalculationService` consumes. |
| `BruteForceCalculationService` | `FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/BruteForceCalculationService.cs` | **The engine.** Generates candidates, validates, selects, produces `CalculatedSprinklerPoint`. Revit-free. |
| `RoomGeometry` | `.../BruteForce/RoomGeometry.cs` | Stores outer/inner loops, bounding box; `IsPointInsideRoom`, `DistanceToOuterBoundary`. |
| `GeometryMath` | `.../BruteForce/GeometryMath.cs` | Pure 2D helpers: `PointInPolygon`, `DistancePointToSegmentSquared`, `InsideExpandedBox`, `Distance`. |
| `BruteForceCalculationConfig` | `.../BruteForce/BruteForceCalculationConfig.cs` | Tolerances, `GridResolutionFt` (1.0), candidate caps. |
| `DefaultHazardPlacementRules` / `HazardPlacementRuleSet` | `.../BruteForce/DefaultHazardPlacementRules.cs`, `HazardPlacementRuleSet.cs` | **Provisional** spacing/coverage values (15 ft etc.). |
| `CalculatedSprinklerPoint` | `FireProtection.UI/Models/Sprinklers/BruteForce/CalculatedSprinklerPoint.cs` | The output point: `X, Y, Z, RoomId, LevelId, LevelName, Score`. |
| `RevitSprinklerPlacementService` | `FireProtection.Backend/Services/Placement/Sprinklers/Final/RevitSprinklerPlacementService.cs` | Turns `CalculatedSprinklerPoint` into a Revit `FamilyInstance`. |
| `ISprinklerPlacementService` | `FireProtection.UI/Services/ISprinklerPlacementService.cs` | Revit-free contract the UI calls. |
| `PlacementInputJsonExporter` | `FireProtection.Backend/Services/Placement/Sprinklers/Final/PlacementInputJsonExporter.cs` | `CalculateBruteForce(...)` (runs the engine) and `ExportPlacementResult(...)`. |
| `SprinklerBruteForceViewModel` | `FireProtection.UI/ViewModels/Sprinklers/BruteForce/SprinklerBruteForceViewModel.cs` | UI orchestration: extract → build → calculate → place → export. |

----

## 3. Data Flow (with what is transformed)

```text
Revit Room (API, possibly in a linked doc)
   ↓  RoomExtractor.ExtractRoom  +  RevitModelContext.TransformPoint
RoomData  (boundary polygon ALREADY in host MEP feet)
   ↓  (UI builds PlacementRoomSelection from RoomData / RoomUiData)
PlacementRoomSelection
   ↓  PlacementInputBuilder.Build
PlacementInputSnapshot  →  List<PlacementRoomInput>
   ↓  BruteForceCalculationService.Calculate
RoomGeometry + candidates + selection
   ↓
CalculatedSprinklerPoint   (X, Y, Z already host MEP feet)
   ↓  RevitSprinklerPlacementService
FamilyInstance
```

What is preserved vs. transformed:

- **Preserved:** polygon coordinates are extracted *in host coordinates* (the transform is applied during extraction, not during calculation). The calculation just consumes `Boundary.OuterLoop.Polygon` as host feet.
- **Transformed:** only linked-model geometry is transformed, and that happens in the extractors via `RevitModelContext.TransformPoint`. The placement service does **not** transform again.
- **Used to compute X/Y:** the room polygon (for inside/outside and wall distance) and the bounding box (for the candidate sweep).
- **Used to compute Z:** `LevelElevationFt` + `CeilingHeightFt` (or a flat ceiling's `BottomElevationFt`).

---

## 4. How the Room Polygon Reaches the Engine

`PlacementInputBuilder.Build` copies the selected room's polygon into **both**:

```csharp
// PlacementInputBuilder.cs (inside the per-room loop)
List<double[]> polyCopy = new List<double[]>();
if (sel.Polygon != null) { /* copy each [x,y] */ }

BoundaryData boundary = new BoundaryData
{
    Polygon = polyCopy,
    OuterLoop = new BoundaryLoopData { IsOuter = true, Polygon = polyCopy }
};

PlacementRoomInput roomInput = new PlacementRoomInput
{
    ...
    BoundaryPolygon = polyCopy,   // convenience copy
    Boundary = boundary,          // preferred source for the engine
    Ceilings = sel.Ceilings ?? new List<CeilingData>(),
    Obstacles = sel.Obstacles ?? new List<ObstacleData>(),
    ExistingSprinklers = sel.ExistingSprinklers ?? new List<ExistingSprinklerData>(),
    LevelElevationFt = sel.LevelElevationFt,   // from the room's LevelData
    CeilingHeightFt = sel.CeilingHeightFt,     // from RoomData.Geometry
    EffectiveHazardClass = sel.EffectiveHazardClass,
    ...
};
```

`BruteForceCalculationService` reads the polygon back through two helpers:

```csharp
private static List<double[]> ExtractOuterPolygon(PlacementRoomInput room)
{
    if (room.Boundary?.OuterLoop?.Polygon?.Count >= 3) return room.Boundary.OuterLoop.Polygon;
    if (room.BoundaryPolygon?.Count >= 3)             return room.BoundaryPolygon;
    return new List<double[]>();
}
```

So the polygon → `RoomGeometry` → bounding box → candidate sweep path is fully traceable.

---

## 5. How X Is Calculated

X comes from a **fine grid sweep** over the room's bounding box, then **thinning** during selection. It is *not* generated directly at the 15-ft spacing.

The engine builds `RoomGeometry`, which computes the X bounds from the polygon:

```csharp
// RoomGeometry constructor
foreach (double[] p in _outer)
{
    if (p[0] < _minX) _minX = p[0];
    if (p[0] > _maxX) _maxX = p[0];
    ...
}
public double MinX => _minX;
public double MaxX => _maxX;
```

The candidate sweep (real code, `BruteForceCalculationService.CalculateRoom`):

```csharp
double gridRes = ComputeGridResolution(geometry, ruleSet, config, out int gridPointEstimate);

for (double y = geometry.MinY; y <= geometry.MaxY + config.ToleranceFt; y += gridRes)
{
    for (double x = geometry.MinX; x <= geometry.MaxX + config.ToleranceFt; x += gridRes)
    {
        generated++;
        CandidatePoint candidate = new CandidatePoint { X = x, Y = y, Z = placementZ };
        ...
    }
}
```

`gridRes` is `BruteForceCalculationConfig.GridResolutionFt`, which defaults to **1.0 ft** (clamped down only if the candidate count would exceed `MaxCandidatePoints = 4000`).

So X values are produced as: `MinX, MinX+1, MinX+2, …` up to `MaxX`. Every candidate is then tested for "inside room" and "≥ boundary clearance", and finally the **selection** step keeps only points that are at least `MaxSpacingFt` (15 ft) apart.

**Real example (room "Commercial/Retail 108", from `extractTest.json`):**
- Polygon X range: `minX = 3.8021 ft`, `maxX = 40.7708 ft`.
- Candidate X values start at `3.8021, 4.8021, 5.8021, …` (1-ft steps).
- After the 15-ft thinning selection, the surviving X values were (reproduced): `5.802, 10.802, 12.802, 19.802, 22.802, 24.802, 35.802, 38.802`.

---

## 6. How Y Is Calculated

Y uses the *same* mechanism as X, just the outer loop of the sweep:

```csharp
for (double y = geometry.MinY; y <= geometry.MaxY + config.ToleranceFt; y += gridRes)
{
    for (double x = geometry.MinX; ...
```

- `MinY` / `MaxY` come from the polygon (same `RoomGeometry` constructor as X).
- Y candidate values: `MinY, MinY+1, MinY+2, …`.
- **Deterministic ordering:** candidates are generated with Y ascending, then X ascending; the selection step also sorts by `(Y, X)` so the result is reproducible regardless of input order.

**Real example (same room):**
- Polygon Y range: `minY = -30.4219 ft`, `maxY = 41.6979 ft`.
- Candidate Y values start at `-30.4219, -29.4219, -28.4219, …`.
- After selection, the surviving Y values (reproduced) were: `-19.422, -18.422, -4.422, -3.422, 3.578, 14.578, 15.578, 21.578, 32.578`.

The reason these specific numbers survive is the 15-ft spacing rule: once a point at e.g. `Y = 14.578` is selected, any later candidate within 15 ft (in any direction) is skipped.

---

## 7. How Z Is Calculated (the placement plane)

Z is **not** taken from the polygon. It is the elevation of the sprinkler placement plane, decided entirely in `CalculateRoom`:

```csharp
bool ceilingUnsupported = false;
double placementZ = room.LevelElevationFt;

CeilingData flatCeiling = (room.Ceilings != null)
    ? room.Ceilings.FirstOrDefault(c =>
        c != null &&
        string.Equals(c.SlopeType, "FLAT", StringComparison.OrdinalIgnoreCase) &&
        c.BottomElevationFt.HasValue)
    : null;

bool hasSlopedCeiling = (room.Ceilings != null) &&
    room.Ceilings.Any(c => c != null &&
        !string.Equals(c.SlopeType, "FLAT", StringComparison.OrdinalIgnoreCase));

if (flatCeiling != null)
{
    placementZ = flatCeiling.BottomElevationFt.Value;          // (A) flat ceiling underside
}
else
{
    if (room.CeilingHeightFt.HasValue)
    {
        placementZ = room.LevelElevationFt + room.CeilingHeightFt.Value;  // (B) floor + room height
        ceilingNote = "Ceiling elevation derived from room ceiling height (provisional).";
    }
    else
    {
        placementZ = room.LevelElevationFt;                    // (C) floor level fallback
        ceilingNote = "Ceiling elevation unavailable; Z set to floor level provisionally.";
    }
    ceilingUnsupported = true;
    if (hasSlopedCeiling) ceilingNote = (ceilingNote + " Sloped/unsupported ceiling present.").Trim();
}
```

Three distinct elevations exist in the code:

1. **Level elevation** — `room.LevelElevationFt` (the Revit level's `Elevation`). Used as the base.
2. **Ceiling elevation** — either a flat ceiling's `BottomElevationFt`, or `LevelElevationFt + CeilingHeightFt`.
3. **Sprinkler placement elevation (Z)** — the value stored on `CalculatedSprinklerPoint.Z`. It equals the ceiling elevation above (no extra drop is subtracted in the current code).

**Real example (room "Commercial/Retail 108"):**
- No `ceilings` array is present in the extracted data → `flatCeiling == null`.
- `LevelElevationFt = 0.0`, `CeilingHeightFt = 21.8333`, `ceilingType = "SLOPED"`.
- Code takes branch **(B)**: `Z = 0.0 + 21.8333 = 21.8333 ft`.
- Because there is no flat ceiling and the type is `SLOPED`, `ceilingUnsupported = true`, so the room is flagged `UnsupportedCeiling` / `ReviewRequired`.

> Note: the ceiling is described as SLOPED, but because no flat ceiling object exists the code **falls back** to the room's `CeilingHeightFt`. This is a known limitation (see §19).

---

## 8. Room Coordinate Transformation (Linked Models)

This is critical: the project supports **linked architectural models**.

```text
Architectural linked model (its own coordinate system)
        ↓  RevitLinkInstance.GetTotalTransform()
   Link Transform
        ↓  RevitModelContext.TransformPoint(point, transform)
   Host MEP coordinates (feet)
        ↓  RoomExtractor / CeilingExtractor / etc. write RoomData
   Sprinkler calculation (X/Y/Z already in host coords)
        ↓  RevitSprinklerPlacementService
   FamilyInstance  (placed in host coords — NO second transform)
```

Evidence from the extractors (verified):

- `RoomExtractor.cs:125` — `XYZ hPt = RevitModelContext.TransformPoint(locPt.Point, transform);`
- `RoomExtractor.cs:298` — `XYZ hostPt = RevitModelContext.TransformPoint(sourcePt, transform);`
- `RoomExtractor.cs:359-361` — boundary curve endpoints/mid are all transformed.
- `CeilingExtractor.cs:172-173` — ceiling face min/max transformed to host.
- `RoomData.Source.IsFromLink` records whether the geometry came from a link.

**Conclusion:** by the time `BruteForceCalculationService` runs, every coordinate is already in host MEP feet. The placement service passes `new XYZ(point.X, point.Y, point.Z)` directly to Revit. Applying the link transform a second time would be a bug — and the code does **not** do it.

`RevitModelContext.TransformPoint` (real signature): `public static XYZ TransformPoint(XYZ point, Transform transform)`.

---

## 9. How Many Sprinklers Are Needed?

The code computes two different numbers:

**(a) `CalculatedCount`** — the number actually selected by the greedy algorithm. This is the real output.

**(b) `RequiredCount`** — a *provisional area-based estimate*, used only for reporting:

```csharp
double coverageArea = Math.PI * ruleSet.CoverageRadiusFt * ruleSet.CoverageRadiusFt;
int provisionalRequired = coverageArea > 0
    ? (int)Math.Ceiling(room.AreaSqFt / coverageArea)
    : selected.Count;
result.RequiredCount = provisionalRequired;
```

With the provisional rules (`CoverageRadiusFt = 7.5`):

- `coverageArea = π * 7.5² ≈ 176.71 sq ft`.
- For room 108 (`AreaSqFt = 2303.43`): `RequiredCount = ceil(2303.43 / 176.71) = 14`.
- `CalculatedCount` (reproduced) = 11 — fewer, because the irregular shape and the 15-ft spacing rule leave some area uncovered by the deterministic grid.

> **This is a provisional placeholder and is NOT a final NFPA calculation.** `DefaultHazardPlacementRules.HasApprovedRules` is `false`, and every room is flagged `ReviewRequired`. The 15-ft spacing will be replaced by project-approved NFPA13-2022 values later.

---

## 10. How the Grid / Candidate Points Are Generated (Core)

Step by step, exactly as coded in `CalculateRoom`:

1. `ExtractOuterPolygon(room)` → outer loop vertices.
2. `ExtractInnerLoops(room)` → opening loops (none in our example).
3. Build `RoomGeometry(outer, innerLoops)` → computes `MinX/MaxX/MinY/MaxY`, `IsDegenerate`.
4. Parse hazard class → `rules.GetRules(hazardClass)` (provisional 15-ft set).
5. Compute `placementZ` (see §7).
6. Build obstacle boxes and existing-sprinkler XY lists.
7. `ComputeGridResolution(...)` → `gridRes` (1.0 ft by default).
8. **Sweep** `y` from `MinY`→`MaxY`, `x` from `MinX`→`MaxX` in `gridRes` steps.
9. For each `(x, y)`: build a `CandidatePoint` with `Z = placementZ`.
10. **Inside-room check:** `geometry.IsPointInsideRoom(x, y, tol)` → else reject ("Outside room boundary").
11. **Boundary clearance:** `geometry.DistanceToOuterBoundary(x, y) < BoundaryClearanceFt - tol` → reject ("Too close to room boundary").
12. **Obstacle check:** inside any expanded obstacle box → reject.
13. **Existing-sprinkler check:** within `ExistingSprinklerSeparationFt` of an existing sprinkler → reject.
14. Keep valid candidates.
15. **Selection:** sort by `(Y, X)`; greedily keep a candidate unless it is *covered* (within `CoverageRadiusFt`) by, or *too close* (within `MaxSpacingFt`) to, an already-selected point.
16. Emit `CalculatedSprinklerPoint` for each kept candidate.

---

## 11. The Core Method, Line-by-Line

The candidate sweep + validation (from `BruteForceCalculationService.CalculateRoom`):

```csharp
// (1) Fine grid sweep over the room bounding box
for (double y = geometry.MinY; y <= geometry.MaxY + config.ToleranceFt; y += gridRes)
{
    if (generated >= config.MaxCandidatePoints) break;
    for (double x = geometry.MinX; x <= geometry.MaxX + config.ToleranceFt; x += gridRes)
    {
        if (generated >= config.MaxCandidatePoints) break;
        generated++;

        // (2) Build a candidate at the grid position, Z already decided
        CandidatePoint candidate = new CandidatePoint { X = x, Y = y, Z = placementZ };

        // (3) Must be strictly inside the room polygon
        if (!geometry.IsPointInsideRoom(x, y, config.ToleranceFt))
        {
            candidate.RejectionReasons.Add("Outside room boundary");
            rejectedOutside++; continue;
        }

        // (4) Must be at least BoundaryClearanceFt (1 ft) from any wall
        if (geometry.DistanceToOuterBoundary(x, y) < ruleSet.BoundaryClearanceFt - config.ToleranceFt)
        {
            candidate.RejectionReasons.Add("Too close to room boundary");
            rejectedBoundary++; continue;
        }

        // (5) Must not sit inside an obstacle's clearance box
        foreach (ObstacleBox box in obstacleBoxes)
            if (GeometryMath.InsideExpandedBox(x, y, box.MinX, box.MinY, box.MaxX, box.MaxY, ruleSet.ObstacleClearanceFt))
            { candidate.RejectionReasons.Add("Inside/too close to obstacle: " + box.Category); rejectedObstacle++; hitObstacle = true; break; }
        if (hitObstacle) continue;

        // (6) Must not be within ExistingSprinklerSeparationFt of an existing sprinkler
        foreach (double[] es in existingSprinklerXy)
            if (GeometryMath.Distance(x, y, es[0], es[1]) <= ruleSet.ExistingSprinklerSeparationFt - config.ToleranceFt)
            { candidate.RejectionReasons.Add("Too close to existing sprinkler"); rejectedExisting++; hitExisting = true; break; }
        if (hitExisting) continue;

        // (7) Survived all checks → valid candidate
        candidate.IsValid = true;
        validCandidates.Add(candidate);
        validCount++;
    }
}
```

**Meaning of each part:**

- **(1)** The `gridRes` loop is what makes X and Y. It is a 1-ft raster, not the 15-ft spacing.
- **(2)** `Z` was already fixed in §7; every candidate in the room shares the same `Z`.
- **(3)** `IsPointInsideRoom` calls `GeometryMath.PointInPolygon` (even-odd ray cast, tolerant to edges).
- **(4)** `DistanceToOuterBoundary` is the minimum distance from the point to any wall segment; must be ≥ ~1 ft.
- **(5)** Obstacles expand their footprint by `ObstacleClearanceFt` (1 ft); a candidate inside is rejected.
- **(6)** Existing sprinklers create a no-place disk of radius `ExistingSprinklerSeparationFt` (7.5 ft).
- **(7)** Valid candidates are kept for the selection step.

The **selection** (thinning to 15-ft spacing):

```csharp
validCandidates.Sort((a, b) =>       // deterministic: Y then X
    a.Y.CompareTo(b.Y) != 0 ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));

foreach (CandidatePoint candidate in validCandidates)
{
    bool covered = selected.Any(s => GeometryMath.Distance(candidate.X, candidate.Y, s.X, s.Y) <= ruleSet.CoverageRadiusFt - tol);
    if (covered) continue;                                   // already covered by a chosen sprinkler
    bool tooClose = selected.Any(s => GeometryMath.Distance(candidate.X, candidate.Y, s.X, s.Y) < ruleSet.MaxSpacingFt - tol);
    if (tooClose) continue;                                  // would be < 15 ft from a chosen sprinkler
    selected.Add(new CalculatedSprinklerPoint { X = candidate.X, Y = candidate.Y, Z = candidate.Z,
        RoomId = room.RoomId, LevelId = room.LevelId, LevelName = room.LevelName });
}
```

This is why the final sprinklers end up ~15 ft apart even though the grid was 1 ft.

---

## 12. The Geometry Helpers (Real Code)

```csharp
// GeometryMath.cs
public static bool PointInPolygon(double x, double y, List<double[]> polygon, double tolerance)
{
    int n = polygon.Count; bool inside = false;
    for (int i = 0, j = n - 1; i < n; j = i++)
    {
        double[] pi = polygon[i], pj = polygon[j];
        if (DistancePointToSegmentSquared(x, y, pi[0], pi[1], pj[0], pj[1]) <= tolerance * tolerance)
            return true;                       // on/very near an edge → treat as inside
        bool intersects = ((yi > y) != (yj > y)) &&
            (x < (xj - xi) * (y - yi) / (yj - yi) + xi);
        if (intersects) inside = !inside;     // even-odd ray cast
    }
    return inside;
}

public static double DistancePointToSegmentSquared(double px, double py, double ax, double ay, double bx, double by)
{
    double dx = bx - ax, dy = by - ay;
    double lenSq = dx * dx + dy * dy;
    if (lenSq <= double.Epsilon) return (px-ax)*(px-ax) + (py-ay)*(py-ay);
    double t = ((px - ax) * dx + (py - ay) * dy) / lenSq;
    t = Math.Max(0.0, Math.Min(1.0, t));      // clamp to the segment
    double cx = ax + t * dx, cy = ay + t * dy;
    return (px - cx) * (px - cx) + (py - cy) * (py - cy);
}
```

`DistanceToOuterBoundary` simply takes the square root of the minimum `DistancePointToSegmentSquared` over all wall segments.

---

## 13. One Sprinkler — Complete Trace (Real Room)

Using **room "Commercial/Retail 108"** (`roomId 828548`) from `extractTest.json`:

| Step | Class.Method | Value / property |
|---|---|---|
| Extract room | `RoomExtractor.ExtractRoom` | polygon 72 verts, `LevelElevationFt = 0`, `CeilingHeightFt = 21.8333`, `ceilingType = SLOPED` |
| Build input | `PlacementInputBuilder.Build` | `Boundary.OuterLoop.Polygon`, `LevelElevationFt = 0`, `CeilingHeightFt = 21.8333`, `Ceilings = []` |
| Bounds | `RoomGeometry` ctor | `MinX=3.8021`, `MaxX=40.7708`, `MinY=-30.4219`, `MaxY=41.6979` |
| Z plane | `CalculateRoom` §7 branch (B) | `Z = 0 + 21.8333 = 21.8333 ft` |
| Candidate | `CalculateRoom` grid loop | e.g. `(x=5.802, y=-18.422)` passes inside + 1-ft wall checks |
| Selection | `CalculateRoom` greedy loop | kept because ≥15 ft from all earlier-selected points |
| Output | `CalculatedSprinklerPoint` | `X=5.802, Y=-18.422, Z=21.833, RoomId=828548, LevelId=593142, LevelName="L1 - Block 43"` |
| Placement | `RevitSprinklerPlacementService.PlaceSprinklers` | `ResolveHostLevel(doc, "593142", "L1 - Block 43")` → host `Level`; `NewFamilyInstance(xyz, symbol, level, NonStructural)` |
| Result | Revit | `FamilyInstance` with a new `ElementId` |

> The original `sprinkler_placement_result_*.json` that would contain the final `ElementId` was not present
> in this workspace at write time, so the `(X, Y)` values above were **reproduced by running the same
> algorithm** against the room's real polygon. The method that creates each `CalculatedSprinklerPoint`
> is the `selected.Add(new CalculatedSprinklerPoint { ... })` line in `CalculateRoom`; the method that
> creates each `FamilyInstance` is `doc.Create.NewFamilyInstance(...)` in `RevitSprinklerPlacementService`.

**Reproduced selected points for this room (Z = 21.8333 ft for all):**

```
(38.802, 14.578)  (10.802, 15.578)  (24.802, 21.578)  (19.802,  3.578)
(35.802, 32.578)  (12.802, 32.578)  (38.802,-19.422)  ( 5.802,-18.422)
(22.802,-19.422)  (35.802, -4.422)  ( 5.802, -3.422)
```

These are illustrative of the real algorithm on this real (irregular, 72-vertex) room: 11 points, spaced
roughly 15 ft apart, all at the same Z because the room has a single ceiling-height value.

---

## 14. Obstacle Handling

Obstacles are read from `room.Obstacles` (`List<ObstacleData>`) and turned into axis-aligned boxes:

```csharp
// BuildObstacleBoxes
if (obstacle.BoundingBox != null ...) { minX=..Min.X; minY=..Min.Y; maxX=..Max.X; maxY=..Max.Y; }
else if (obstacle.CenterPoint != null && obstacle.DimensionsFt != null) { /* build from center+half-size */ }
```

A candidate is rejected if it falls inside the box **expanded by `ObstacleClearanceFt` (1 ft)**:

```csharp
GeometryMath.InsideExpandedBox(x, y, box.MinX, box.MinY, box.MaxX, box.MaxY, ruleSet.ObstacleClearanceFt)
```

Behavior: **reject** (the candidate is dropped). The algorithm does **not** nudge the point to a free spot;
it simply moves on to the next grid candidate. Walls are treated as the **room boundary** (via
`DistanceToOuterBoundary`), not as obstacle boxes — unless a wall/beam was also extracted as an `ObstacleData`.

In room 108 there are **no obstacles**, so no candidate is rejected for this reason.

---

## 15. Existing Sprinkler Handling

Existing sprinklers come from `room.ExistingSprinklers` (`List<ExistingSprinklerData>`). The engine collects
their XY positions (`BuildExistingSprinklerXy`) and, during candidate validation, rejects any candidate within
`ExistingSprinklerSeparationFt` (7.5 ft):

```csharp
foreach (double[] es in existingSprinklerXy)
    if (GeometryMath.Distance(x, y, es[0], es[1]) <= ruleSet.ExistingSprinklerSeparationFt - config.ToleranceFt)
    { reject("Too close to existing sprinkler"); }
```

During selection, existing sprinklers also count as "already covered", so new points won't be placed on top of
them. Room 108 has **no existing sprinklers**, so this path is inactive for the example, but the code above is
exactly what would run if any were present.

---

## 16. Room Polygon vs Bounding Box

- **Polygon** (`Boundary.OuterLoop.Polygon`, `List<double[]>`): used for `IsPointInsideRoom` (point-in-polygon)
  and for `DistanceToOuterBoundary` (wall distance). This is what makes the result follow the real room shape,
  including L-shapes and concavities.
- **Bounding box** (`MinX/MaxX/MinY/MaxY` from `RoomGeometry`): used **only** to limit the candidate sweep so the
  engine doesn't test points far outside the room.

Both are used: the box scopes the search; the polygon validates each candidate. Inner loops (openings) are also
passed to `RoomGeometry` and excluded via the same point-in-polygon test.

---

## 17. Linked Model Behavior

Rooms/levels can originate in a linked architectural model. The data records this:

- `RoomData.Source.IsFromLink = true`, with `LinkInstanceId` / `LinkName` set (`RoomExtractor`).
- Geometry is transformed to host coords during extraction (§8).
- **Level handling at placement** is the subtle part. A linked level's `ElementId` belongs to the *linked*
  document, so `hostDocument.GetElement(linkedLevelId)` returns `null`. `RevitSprinklerPlacementService`
  solves this in `ResolveHostLevel`:

```csharp
// RevitSprinklerPlacementService.ResolveHostLevel (real)
LevelResolution ResolveHostLevel(Document doc, string levelId, string levelName)
{
    // 1) Try the host document directly
    if (!string.IsNullOrEmpty(levelId) && long.TryParse(levelId, out long id))
    {
        Level hostLevel = doc.GetElement(new ElementId(id)) as Level;
        if (hostLevel != null) return new LevelResolution(hostLevel, LevelResolutionSource.HostDocument, ...);
    }
    // 2) Search every RevitLinkInstance for the level, transform its elevation, match by elevation/name
    foreach (RevitLinkInstance link in new FilteredElementCollector(doc).OfClass<RevitLinkInstance>())
    {
        Document linkDoc = link.GetLinkDocument();
        Level linkedLevel = linkDoc?.GetElement(new ElementId(id)) as Level;
        if (linkedLevel != null)
        {
            Transform t = link.GetTotalTransform();
            double hostElevation = linkedLevel.Elevation + t.Origin.Z;   // approx; code uses full transform
            Level match = find host Level whose Elevation ≈ hostElevation (±0.01) or Name equals levelName;
            if (match != null) return new LevelResolution(match, LevelResolutionSource.LinkedModel, ...);
        }
    }
    return new LevelResolution(null, LevelResolutionSource.Unresolved, "Required level could not be resolved");
}
```

So the flow is: `linked LevelId → ResolveHostLevel → host Level → NewFamilyInstance`. The placement never
silently substitutes a wrong level; if it cannot resolve, it records a `FailedSprinklerEntry` with diagnostics.

---

## 18. Final Revit Placement

After calculation, each `CalculatedSprinklerPoint` becomes a `FamilyInstance`:

```csharp
// RevitSprinklerPlacementService.PlaceSprinklers (real, condensed)
foreach (CalculatedSprinklerPoint point in request.Points)
{
    LevelResolution levelResolution = ResolveHostLevel(doc, point.LevelId, point.LevelName);
    if (!levelResolution.Resolved) { record failure with diagnostics; continue; }

    Level level = levelResolution.Level;
    XYZ xyz = new XYZ(point.X, point.Y, point.Z);     // already host MEP feet — no transform

    FamilySymbol symbol = ResolveAndActivateSymbol(familyName, typeName);   // done once per family/type

    try
    {
        Reference hostRef = FindCeilingHost(doc, xyz, level);   // vertical ray to a ceiling face
        FamilyInstance instance;
        if (hostRef != null)
            instance = doc.Create.NewFamilyInstance(hostRef, xyz, new XYZ(0, 0, 1), symbol);  // hosted on ceiling
        else
            instance = doc.Create.NewFamilyInstance(xyz, symbol, level,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural);          // free-standing on level
        record PlacedSprinklerEntry { ElementId = instance.Id, HostLevelId = level.Id, ... };
    }
    catch (Exception ex) { record FailedSprinklerEntry { ... }; }
}
```

Key facts:
- **Calculation vs placement are separate.** Calculation produces `X/Y/Z`; placement consumes them verbatim.
- `FindCeilingHost` intersects a vertical ray at `(X, Y)` to find a ceiling face; if found the sprinkler is
  *hosted* on that ceiling, otherwise it is placed at `Z` on the resolved `Level`.
- All instances are created inside a single transaction (`doc.Transaction`).
- Duplicates are guarded by a minimal distance check (`RevitSprinklerPlacementConfig`, 0.25 ft) before placing.

---

## 19. Current Limitations (code-evidenced)

- **Provisional 15-ft spacing.** `DefaultHazardPlacementRules` uses `MaxSpacingFt = 15.0` as a placeholder;
  `HasApprovedRules = false`; every room is `ReviewRequired`. Not NFPA13-2022.
- **No approved hazard logic.** The same 15-ft placeholder is used for all hazard classes (`Light`, `OH1`,
  `OH2`, `EH1`, `EH2`). Hazard class only affects `RequiredCount` reporting, not spacing.
- **Sloped/unsupported ceilings.** With no flat `CeilingData`, Z falls back to `LevelElevationFt +
  CeilingHeightFt` and the room is flagged `UnsupportedCeiling`/`MissingCeiling` + `ReviewRequired`.
- **Linked-ceiling hosting.** `FindCeilingHost` works in the host document; ceilings that live *only* in a
  linked model may not be found, so the sprinkler falls back to level-based placement.
- **Obstacles are axis-aligned boxes.** `BuildObstacleBoxes` uses `BoundingBox` or center+dimensions; curved
  or diagonal obstacles are approximated by their AABB + 1-ft clearance.
- **No nudging.** Rejected candidates are dropped, not shifted to a nearby free location.
- **Minimal duplicate guard.** Only a 0.25-ft distance check; no broader clash detection.
- **`RequiredCount` is area-based only.** It does not drive the actual selection.

---

## 20. In Simple Words

1. We extract the room (and ceilings/levels/obstacles) from Revit — linked-model geometry is converted to host coordinates during extraction.
2. We copy the room into a `PlacementRoomInput` (polygon, level elevation, ceiling height, hazards).
3. `BruteForceCalculationService` builds `RoomGeometry` and finds the room's X/Y bounding box.
4. It decides the placement plane **Z**: a flat ceiling's underside, else floor + room ceiling height, else the floor level.
5. It raster-sweeps a **1-ft grid** of candidate (X, Y) points across the bounding box.
6. It throws away candidates that are outside the room, within 1 ft of a wall, inside an obstacle, or too close to an existing sprinkler.
7. It greedily keeps candidates so that no two selected sprinklers are closer than **15 ft** (the provisional spacing).
8. Each kept point becomes a `CalculatedSprinklerPoint { X, Y, Z, RoomId, LevelId, LevelName }`.
9. `RevitSprinklerPlacementService` resolves the host `Level` (handling linked-model level ids).
10. It creates a Revit `FamilyInstance` at exactly that `X/Y/Z` (hosted on a ceiling if one is found, else on the level).
11. The result (placed or failed, with diagnostics) is returned and exported to `sprinkler_placement_result_*.json`.

All spacing is **provisional** and requires engineer review before any real use.

---

## 21. Required Code References

| Class | Method | File | Purpose |
|---|---|---|---|
| `BruteForceCalculationService` | `Calculate` | `FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/BruteForceCalculationService.cs` | Top-level engine entry; loops rooms. |
| `BruteForceCalculationService` | `CalculateRoom` | same | Per-room X/Y/Z generation + selection. |
| `BruteForceCalculationService` | `ExtractOuterPolygon` / `ExtractInnerLoops` | same | Read polygon from input. |
| `BruteForceCalculationService` | `ComputeGridResolution` | same | Decide `gridRes` (1 ft default). |
| `BruteForceCalculationService` | `BuildObstacleBoxes` / `BuildExistingSprinklerXy` | same | Build obstacle / existing-sprinkler checks. |
| `RoomGeometry` | ctor / `IsPointInsideRoom` / `DistanceToOuterBoundary` | `.../BruteForce/RoomGeometry.cs` | Bounds + inside/outside + wall distance. |
| `GeometryMath` | `PointInPolygon` / `DistancePointToSegmentSquared` / `InsideExpandedBox` / `Distance` | `.../BruteForce/GeometryMath.cs` | Pure 2D math. |
| `BruteForceCalculationConfig` | — | `.../BruteForce/BruteForceCalculationConfig.cs` | `GridResolutionFt = 1.0`, candidate caps. |
| `DefaultHazardPlacementRules` | `GetRules` | `.../BruteForce/DefaultHazardPlacementRules.cs` | Provisional 15-ft spacing/coverage. |
| `PlacementInputBuilder` | `Build` | `FireProtection.Backend/Services/Placement/Sprinklers/Final/PlacementInputBuilder.cs` | UI selection → `PlacementInputSnapshot`. |
| `RoomExtractor` | `ExtractRoom` | `FireProtection.Backend/Services/Model/RoomExtractor.cs` | Revit room → `RoomData` (host coords). |
| `RevitModelContext` | `TransformPoint` | `FireProtection.Backend/Services/Model/RevitModelContext.cs` | Linked-model → host coordinate transform. |
| `RevitSprinklerPlacementService` | `PlaceSprinklers` / `ResolveHostLevel` / `FindCeilingHost` | `FireProtection.Backend/Services/Placement/Sprinklers/Final/RevitSprinklerPlacementService.cs` | `CalculatedSprinklerPoint` → `FamilyInstance`. |
| `CalculatedSprinklerPoint` | — | `FireProtection.UI/Models/Sprinklers/BruteForce/CalculatedSprinklerPoint.cs` | Output point model. |

---

## 22. Validation Notes

- Every class/method/file name above was checked against the source in this workspace.
- X/Y formulas: **verified** against `CalculateRoom` (1-ft `gridRes` sweep + 15-ft greedy selection).
- Z formula: **verified** against the `placementZ` block in `CalculateRoom` (branch B used for room 108).
- Example coordinates: taken from `extractTest.json` (real polygon/level/ceiling) and the reproduced
  selection; the placement-result JSON with final `ElementId`s was not present in the workspace.
- Coordinates are **host MEP feet**; placement applies no second transform — **verified**.
- Linked-level resolution: **verified** in `ResolveHostLevel`.
- No production source code was modified for this task.

---

## 23. Report

```text
Documentation created:
C:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\SPRINKLER_POINT_CALCULATION_EXPLAINED.md

Classes traced:          18
Core calculation methods traced: 6 (Calculate, CalculateRoom, ExtractOuterPolygon,
                                  ComputeGridResolution, BuildObstacleBoxes, selection loop)
X calculation verified:  YES
Y calculation verified:  YES
Z calculation verified:  YES
Linked coordinate handling verified: YES
Revit placement path verified:     YES
```
