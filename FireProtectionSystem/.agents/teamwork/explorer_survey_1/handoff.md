# Investigation Report: Requirement 1 — Small Room Head Count Logic

**Context**: Investigation of codebase architecture, calculation pipeline, geometry helpers, rules, and tests for implementing Requirement 1 (Small Room Head Count Logic).  
**Investigator**: Explorer Agent (`explorer_survey_1`)  
**Target Path**: `c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\.agents\teamwork\explorer_survey_1\handoff.md`

---

## 1. Observation

### 1.1 Room Boundary & Polygon Representation
- `PlacementRoomInput.cs` (`FireProtection.Backend/Models/Placement/Sprinklers/Final/PlacementRoomInput.cs`):
  - Line 30-31: `public double AreaSqFt { get; set; }`
  - Line 45-46: `public List<double[]> BoundaryPolygon { get; set; }`
  - Line 49-50: `public BoundaryData Boundary { get; set; }`
- `BoundaryData.cs` (`FireProtection.Backend/Models/DTOs/BoundaryData.cs`):
  - Lines 34-50: `BoundaryLoopData` defines `public bool IsOuter { get; set; } = true;`, `public List<double[]> Polygon { get; set; }`, and `public List<BoundarySegmentData> Segments { get; set; }`.
  - Lines 52-70: `BoundaryData` defines `public BoundaryLoopData OuterLoop { get; set; }` and `public List<BoundaryLoopData> InnerLoops { get; set; }`.
- `BruteForceCalculationService.cs` (`FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/BruteForceCalculationService.cs`):
  - Lines 104-107:
    ```csharp
    List<double[]> outer = ExtractOuterPolygon(room);
    List<List<double[]>> innerLoops = ExtractInnerLoops(room);
    RoomGeometry geometry = new RoomGeometry(outer, innerLoops);
    ```
  - Lines 1899-1913: `ExtractOuterPolygon` prefers `room.Boundary.OuterLoop.Polygon` when available and $\ge 3$ vertices; otherwise falls back to `room.BoundaryPolygon`.
  - Lines 1915-1929: `ExtractInnerLoops` extracts polygons from `room.Boundary.InnerLoops`.
- `RoomGeometry.cs` (`FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/RoomGeometry.cs`):
  - Lines 21-42: Constructor normalizes `_outer` and `_innerLoops` via `CleanPolygon` (strips consecutive duplicate points and closes loops), and computes bounding-box extents: `MinX`, `MaxX`, `MinY`, `MaxY`.
  - Line 51: Exposes `public IReadOnlyList<double[]> OuterPolygon => _outer;`.

### 1.2 Centroid Calculation & Existing Geometry Helpers
- **Current Centroid Implementation in Sprinklers**:
  - `BruteForceCalculationService.cs` does **not** compute a polygon centroid anywhere in its pipeline.
  - In `SelectCenteredGrid` (lines 611-641), grid targets are calculated strictly using bounding-box arithmetic:
    ```csharp
    double width = geometry.MaxX - geometry.MinX;
    double height = geometry.MaxY - geometry.MinY;
    ...
    double ty = geometry.MinY + stepY * (iy + 0.5);
    double tx = geometry.MinX + stepX * (ix + 0.5);
    ```
    For $nx = 1, ny = 1$, the target is solely the bounding box center $((MinX + MaxX)/2, (MinY + MaxY)/2)$, not the polygon area centroid.
- **Centroid Helper in Smoke Detectors**:
  - `SmokeDetectorCalculationService.cs` (lines 1082-1100) contains a naive vertex-average helper:
    ```csharp
    private static void ComputePolygonCentroid(IReadOnlyList<double[]> polygon, ref double cx, ref double cy)
    {
        if (polygon == null || polygon.Count < 3) return;
        double sumX = 0; double sumY = 0; int count = 0;
        foreach (double[] v in polygon) {
            if (v == null || v.Length < 2) continue;
            sumX += v[0]; sumY += v[1]; count++;
        }
        if (count > 0) { cx = sumX / count; cy = sumY / count; }
    }
    ```
    *Documentation Note* (`sim_Expla_3.md:1174`): *"ComputePolygonCentroid is an unweighted mean of vertices — not the area centroid. Wrong for L-shaped rooms."*
- **Point Containment**:
  - `RoomGeometry.cs` lines 53-72:
    ```csharp
    public bool IsPointInsideRoom(double x, double y, double tolerance)
    {
        if (IsDegenerate) return false;
        if (!GeometryMath.PointInPolygon(x, y, _outer, tolerance)) return false;
        foreach (List<double[]> inner in _innerLoops)
        {
            if (inner == null || inner.Count < 3) continue;
            if (GeometryMath.PointInPolygon(x, y, inner, tolerance)) return false;
        }
        return true;
    }
    ```
  - `GeometryMath.cs` lines 17-47: `PointInPolygon` implements the even-odd ray casting algorithm with tolerance-based segment edge proximity.
- **Boundary Clearance**:
  - `RoomGeometry.cs` lines 74-91:
    `public double DistanceToOuterBoundary(double x, double y)` measures the minimum distance from $(x, y)$ to any line segment of `_outer`.
  - `Nfpa13RulebookRules.cs` line 59:
    `public const double MinWallClearanceFt = 4.0 / 12.0;` (4-inch code minimum floor per NFPA 13 Ch. 19 p. 220).
  - Effective boundary clearance in `BruteForceCalculationService.cs:436-437`:
    `double effectiveBoundary = Math.Max(ruleSet.BoundaryClearanceFt, Nfpa13RulebookRules.MinWallClearanceFt);`

### 1.3 Multi-Head Grid vs. Single-Head Logic
- In `BruteForceCalculationService.cs`:
  - Lines 426-527: `TryAcceptPoint(x, y)` filters generated candidate points against `IsPointInsideRoom`, boundary clearance (`effectiveBoundary`), obstacle three-times / footprint rules, and existing sprinklers (`ruleSet.ExistingSprinklerSeparationFt`).
  - Lines 529-553: Populates `validCandidates` (either from tile centers if `tileGrid.IsValid` or from grid resolution `gridRes = 1.0 ft`).
  - Lines 572-576: Directly calls `SelectCenteredGrid(...)`.
  - In `SelectCenteredGrid` (lines 614-619):
    ```csharp
    int nx = width <= spacing + config.ToleranceFt ? 1 : (int)Math.Ceiling(width / spacing - config.ToleranceFt);
    int ny = height <= spacing + config.ToleranceFt ? 1 : (int)Math.Ceiling(height / spacing - config.ToleranceFt);
    ```
    *Defect observed*:
    1. A room with area $\le MaxCoverageAreaSqFt$ (e.g. 16 ft x 14 ft = 224 sq ft in Light Hazard where $MaxCoverageArea = 225$ sq ft) has $width = 16 > 15 \Rightarrow nx = 2, ny = 1$, generating **2 heads** instead of 1 head at the centroid.
    2. A room with area $> MaxCoverageAreaSqFt$ (e.g. 12 ft x 12 ft = 144 sq ft in OH1 where $MaxCoverageArea = 130$ sq ft) has $width = 12 \le 15, height = 12 \le 15 \Rightarrow nx = 1, ny = 1$, generating **1 head** instead of scaling up to 2+ heads per standard NFPA rules.

### 1.4 Critical Infinity Bug in Single-Head S x L Verification
- In `BruteForceCalculationService.cs` lines 2169-2191:
  ```csharp
  double alongBranch = double.PositiveInfinity;
  double acrossBranch = double.PositiveInfinity;

  for (int j = 0; j < selected.Count; j++)
  {
      if (i == j) continue;
      ...
  }

  double s = Math.Max(alongBranch, wallAlongBranch * 2.0);
  double l = Math.Max(acrossBranch, wallAcrossBranch * 2.0);
  double area = s * l;
  ```
  When `selected.Count == 1`:
  - Loop over $j$ skips $i == j$, so $j$ never processes any neighbour.
  - `alongBranch` and `acrossBranch` remain `double.PositiveInfinity`.
  - `s = Math.Max(double.PositiveInfinity, ...) = double.PositiveInfinity`.
  - `l = Math.Max(double.PositiveInfinity, ...) = double.PositiveInfinity`.
  - `area = double.PositiveInfinity`, triggering `if (area > ruleSet.MaxCoverageAreaSqFt + 1e-6) overArea++;`.
  - For any non-Light hazard (e.g. OH1, OH2 where `smallRoomRuleApplied` is false), a 1-head room falsely throws an S x L coverage violation warning with `Worst is head #1 with S=Infinity, L=Infinity, area=Infinity`.

### 1.5 Hazard Class Placement Rules & `MaxCoverageAreaSqFt`
In `DefaultHazardPlacementRules.cs`:
- `HazardClass.Light`: `MaxCoverageAreaSqFt = 225.0`, `MaxSpacingFt = 15.0`, `CoverageRadiusFt = 7.5`, `MaxDistanceFromWallsFt = 7.5`.
- `HazardClass.OH1`: `MaxCoverageAreaSqFt = 130.0`, `MaxSpacingFt = 15.0`, `CoverageRadiusFt = 7.5`, `MaxDistanceFromWallsFt = 7.5`.
- `HazardClass.OH2`: `MaxCoverageAreaSqFt = 130.0`, `MaxSpacingFt = 15.0`, `CoverageRadiusFt = 7.5`, `MaxDistanceFromWallsFt = 7.5`.
- `HazardClass.EH1`: `MaxCoverageAreaSqFt = 100.0`, `MaxSpacingFt = 12.0`, `CoverageRadiusFt = 6.0`, `MaxDistanceFromWallsFt = 6.0`.
- `HazardClass.EH2`: `MaxCoverageAreaSqFt = 100.0`, `MaxSpacingFt = 12.0`, `CoverageRadiusFt = 6.0`, `MaxDistanceFromWallsFt = 6.0`.
- In `MergeTypeRuleValues` (lines 2417-2422): If catalog type defines `TypeMaxCoverageAreaSqFt`, `ruleSet.MaxCoverageAreaSqFt` is overridden by `Math.Min(room.TypeMaxCoverageAreaSqFt.Value, areaCeiling)`.

### 1.6 Existing Tests in `FireProtection.Tests`
- Console test runner in `FireProtection.Tests/Program.cs` executes 19 registered test suites. All 19 pass (`dotnet run --project FireProtection.Tests/FireProtection.Tests.csproj -c Revit2026`).
- Test construction idioms:
  - `CenteredGridPlacementTests.cs:80-103`: Helper `MakeRoom(string id, List<double[]> polygon, ...)` sets up `PlacementRoomInput` with `AreaSqFt`, `CeilingHeightFt`, `BoundaryPolygon`, `Boundary.OuterLoop.Polygon`.
  - `Nfpa13RulebookRulesTests.cs:463-478`: `TestSmallRoomRuleRelaxesWallDistance` uses a 20x20 ft room ($400$ sq ft) with Light hazard.
  - `HazardRuleValuesTests.cs`: Locks the per-hazard-class rule values for `Light (225)`, `OH1 (130)`, `OH2 (130)`, `EH1 (100)`, `EH2 (100)`.

---

## 2. Logic Chain

1. **Polygon Centroid Formulation**:
   - Observations 1.1 and 1.2 demonstrate that the sprinkler calculation has no centroid calculation, and `SmokeDetectorCalculationService` only does a vertex average.
   - For a general non-self-intersecting 2D polygon with vertices $(x_0, y_0), \dots, (x_{n-1}, y_{n-1})$ where $(x_n, y_n) = (x_0, y_0)$, the true area centroid coordinates $(C_x, C_y)$ are:
     $$A = \frac{1}{2}\sum_{i=0}^{n-1}(x_i y_{i+1} - x_{i+1} y_i)$$
     $$C_x = \frac{1}{6A}\sum_{i=0}^{n-1}(x_i + x_{i+1})(x_i y_{i+1} - x_{i+1} y_i)$$
     $$C_y = \frac{1}{6A}\sum_{i=0}^{n-1}(y_i + y_{i+1})(x_i y_{i+1} - x_{i+1} y_i)$$
   - This formula must be implemented in `GeometryMath.cs` as a reusable static helper `ComputePolygonCentroid`. If $|A| < 10^{-6}$ (degenerate/collinear), it falls back to bounding-box center.

2. **Containment & Clearance Verification**:
   - Per Observation 1.2, `RoomGeometry` provides `IsPointInsideRoom(cx, cy, tol)` and `DistanceToOuterBoundary(cx, cy)`.
   - The effective boundary clearance is `effectiveBoundary = Math.Max(ruleSet.BoundaryClearanceFt, Nfpa13RulebookRules.MinWallClearanceFt)`.
   - When $(C_x, C_y)$ is verified:
     - Containment: `geometry.IsPointInsideRoom(cx, cy, config.ToleranceFt) == true`
     - Boundary clearance: `geometry.DistanceToOuterBoundary(cx, cy) >= effectiveBoundary - config.ToleranceFt`
     - Separation from existing sprinklers: `GeometryMath.Distance(cx, cy, es.X, es.Y) > ruleSet.ExistingSprinklerSeparationFt - config.ToleranceFt`
   - If verified and no tile grid is active, $(C_x, C_y)$ is the exact placement location.
   - If a tile grid is active (`tileGrid.IsValid == true`), the project's Tile-Centric Ceiling Placement decision dictates that heads must land on exact tile centers. The head snaps to the nearest candidate in `validCandidates` (which are already verified tile centers).
   - If $(C_x, C_y)$ fails containment or boundary clearance (e.g., L-shaped / concave room where centroid falls outside or within 4 inches of an inner corner):
     - The head falls back to the candidate in `validCandidates` closest to $(C_x, C_y)$ (since all points in `validCandidates` are verified for containment and boundary clearance).

3. **Branch Location for Small Room Head Count Logic**:
   - Per Observation 1.3, `CalculateRoom` determines the room area and `ruleSet.MaxCoverageAreaSqFt`.
   - The effective area is `double effectiveArea = room.AreaSqFt > 0 ? room.AreaSqFt : Math.Abs(ComputeSignedArea(outer));`.
   - The threshold is `double maxArea = ruleSet.MaxCoverageAreaSqFt > 0 ? ruleSet.MaxCoverageAreaSqFt : 225.0;`.
   - **Branch Condition**:
     ```csharp
     bool isSmallRoom = effectiveArea <= maxArea + config.ToleranceFt;
     ```
   - If `isSmallRoom == true`:
     - Bypass multi-head grid spacing (`SelectCenteredGrid` / `SelectCenteredGridOnTiles`).
     - Place exactly 1 head at the verified centroid $(C_x, C_y)$ (or nearest valid candidate if off-grid tile or clearance/containment failure).
     - Check obstacle collision at the placed point: if it intersects any obstacle bounding box at `placementZ`, set `HasObstacleConflict = true` (per Decision 030, no obstacle displacement).
     - Set `result.CalculatedCount = 1`, `result.RequiredCount = 1`.
     - Route to `FinalizeSelection` to execute standard post-placement diagnostic logging.
   - If `isSmallRoom == false` (`effectiveArea > maxArea + config.ToleranceFt`):
     - Proceed to standard NFPA grid calculation (`SelectCenteredGrid` / `SelectCenteredGridOnTiles`).
     - **Scale-Up Guard**: If `nx * ny == 1` in `SelectCenteredGrid`, or `nU * nV == 1` in `SelectCenteredGridOnTiles`, the room cannot remain at 1 head. It must scale up to at least 2 heads (or $\lceil effectiveArea / maxArea \rceil$):
       - For free grid: along the longer dimension (`if (width >= height) nx = Math.Max(2, nx); else ny = Math.Max(2, ny);`).
       - For tile grid: along the larger tile dimension (`if (tilesU >= tilesV) nU = Math.Max(2, nU); else nV = Math.Max(2, nV);`).

4. **Resolution of the S x L Infinity Bug**:
   - Per Observation 1.4, `CheckCoverageBySxL` must handle `selected.Count == 1`.
   - When no neighbour exists along the branch line (`double.IsPositiveInfinity(alongBranch)`), $S = wallAlongBranch \times 2.0$.
   - When no neighbour exists across branch lines (`double.IsPositiveInfinity(acrossBranch)`), $L = wallAcrossBranch \times 2.0$.
   - This eliminates the false `overArea` warning and `PositiveInfinity` crash on single-head rooms in OH1, OH2, EH1, EH2.

---

## 3. Caveats

1. **Non-Sidewall Scope**: This logic applies to ceiling-mounted / pendent / upright sprinklers calculated via the centered grid / tile pipeline. Sidewall sprinklers have their own directional wall solver (`SelectSidewallDirectional`), addressed under Requirement 2.
2. **Revit Runtime Verification**: Static calculation and unit tests in `FireProtection.Tests` verify geometry and coordinate generation without Revit. Runtime verification inside Revit requires an active Revit session.
3. **Tile Grid Priority**: In rooms with suspended ceilings (`tileGrid.IsValid == true`), snapping the centroid to the nearest valid tile center aligns with Decision 027/028/Tile-Centric Placement, preventing heads from landing on ceiling grid tees.

---

## 4. Conclusion

### Summary of Findings
1. **Geometry Representation**: Room boundary loops are represented in `PlacementRoomInput.BoundaryPolygon` and `Boundary.OuterLoop.Polygon`, wrapped by `RoomGeometry`.
2. **Centroid Calculation**: True polygon area centroid should be implemented in `GeometryMath.ComputePolygonCentroid(outerPolygon, out double cx, out double cy)` using Green's Theorem / Shoelace formula.
3. **Verification**: Containment is checked via `geometry.IsPointInsideRoom(cx, cy, tol)` and boundary clearance via `geometry.DistanceToOuterBoundary(cx, cy) >= effectiveBoundary - tol`.
4. **Branching**:
   - If `effectiveArea <= ruleSet.MaxCoverageAreaSqFt`: place exactly 1 head at the verified centroid (or nearest valid candidate if centroid fails containment/clearance or on tile ceilings).
   - If `effectiveArea > ruleSet.MaxCoverageAreaSqFt`: multi-head grid spacing scales up to $\ge 2$ heads (ensuring $nx \times ny \ge 2$).
5. **Coverage Bug Fix**: In `CheckCoverageBySxL`, single-head rooms must treat infinite neighbour distance as twice the wall distance, preventing false `PositiveInfinity` warnings.

### Exact Files to Modify
1. `FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/GeometryMath.cs`:
   - Add `ComputePolygonCentroid(IReadOnlyList<double[]> polygon, out double cx, out double cy)`.
2. `FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/BruteForceCalculationService.cs`:
   - Add single-head centroid placement method `SelectSingleCentroidHead(...)`.
   - Add branch in `CalculateRoom` / `SelectCenteredGrid`:
     - If `effectiveArea <= ruleSet.MaxCoverageAreaSqFt` $\rightarrow$ invoke `SelectSingleCentroidHead`.
     - If `effectiveArea > ruleSet.MaxCoverageAreaSqFt` $\rightarrow$ scale up $nx \times ny \ge 2$ and $nU \times nV \ge 2$.
   - Fix `CheckCoverageBySxL`: replace `double.PositiveInfinity` fallthrough with $2.0 \times wall$.
3. `FireProtection.Tests/CenteredGridPlacementTests.cs` (or dedicated new test class `SmallRoomHeadCountTests.cs`):
   - Add unit tests verifying 1 head at centroid for area $\le MaxCoverageAreaSqFt$, 2+ heads for area $> MaxCoverageAreaSqFt$, L-shaped room centroid containment fallback, and boundary clearance compliance.

---

## 5. Verification Method

### 5.1 Test Execution Commands
Execute from repository root:
```powershell
# Build solution for Revit 2026 and run tests
dotnet build FireProtectionSystem.slnx -c Revit2026
dotnet run --project FireProtection.Tests/FireProtection.Tests.csproj -c Revit2026

# Build solution for Revit 2025
dotnet build FireProtectionSystem.slnx -c Revit2025
```

### 5.2 Specific Test Cases to Verify
1. **Light Hazard Small Room (Area $\le 225$ sq ft)**:
   - Room: 14 ft x 14 ft = 196 sq ft, Light Hazard ($MaxCoverageArea = 225$).
   - Expected: Exactly 1 sprinkler placed at $(7.0, 7.0)$ (centroid).
2. **Light Hazard Multi-Head Room (Area $> 225$ sq ft)**:
   - Room: 16 ft x 15 ft = 240 sq ft, Light Hazard.
   - Expected: At least 2 sprinklers placed ($nx = 2, ny = 1$).
3. **Ordinary Hazard OH1 Small Room (Area $\le 130$ sq ft)**:
   - Room: 11 ft x 11 ft = 121 sq ft, OH1 ($MaxCoverageArea = 130$).
   - Expected: Exactly 1 sprinkler placed at $(5.5, 5.5)$ (centroid), with no S x L infinity warning.
4. **Ordinary Hazard OH1 Room Needing Scale-Up (Area $> 130$ sq ft, dimensions $\le 15$ ft)**:
   - Room: 12 ft x 12 ft = 144 sq ft, OH1 ($MaxCoverageArea = 130$, $MaxSpacing = 15$).
   - Expected: Dimensions $12 \le 15$ would previously place 1 head; must scale up to 2 heads ($nx = 2, ny = 1$).
5. **L-Shaped Room with External Centroid**:
   - Room: Concave polygon where centroid $(C_x, C_y)$ falls in the cutout/outside polygon.
   - Expected: Centroid containment check detects outside; snaps to nearest valid candidate inside polygon with $\ge 1$ ft wall clearance.
6. **Boundary Condition Exactly at `MaxCoverageAreaSqFt`**:
   - Room: 15 ft x 15 ft = 225.0 sq ft, Light Hazard.
   - Expected: Exactly 1 sprinkler placed at centroid $(7.5, 7.5)$.

### 5.3 Invalidation Conditions
- A room with area $\le MaxCoverageAreaSqFt$ placing 2 or more heads when 1 head can clear walls.
- A room with area $> MaxCoverageAreaSqFt$ placing only 1 head.
- Centroid falling outside the room boundary or within the 4-inch / 1-ft clearance floor.
- Single-head placement in OH1/OH2 triggering `S=Infinity, L=Infinity` in S x L check.
