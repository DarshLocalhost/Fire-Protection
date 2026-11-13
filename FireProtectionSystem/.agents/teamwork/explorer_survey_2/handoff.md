# Investigation Report: Requirement 2 — Maximum Throw in Sidewall Sprinklers

## Executive Summary
This report investigates the architecture, current implementation, and exact file modifications required for **Requirement 2: Maximum Throw in Sidewall Sprinklers**:
- Standard sidewall maximum throw is 12 to 14 ft (capped at 14.0 ft).
- If room depth exceeds single-wall throw (14 ft), place heads on opposing walls (up to $2 \times \text{throw} = 28\text{ ft}$).
- If room depth exceeds 28 ft (or single-wall throw when opposing walls cannot cover), flag an error/review warning stating that room depth exceeds maximum sidewall throw and ceiling-mounted heads are required.

---

## 1. Observations

### 1.1 Sidewall Calculation Entry Point & Dispatch
- **File**: `FireProtection.Backend\Services\Placement\Sprinklers\Final\BruteForce\BruteForceCalculationService.cs`
- **Lines 355–373**:
  ```csharp
  bool isSidewall =
      room.SelectedSprinklerPlacementBehavior == DevicePlacementBehavior.WallSidewall
      || string.Equals(room.SelectedSprinklerOrientation, "sidewall", StringComparison.OrdinalIgnoreCase);

  var sprinklerLocationProfile = DeviceLocationPointIdentifier.ResolveSprinklerProfile(
      room.SelectedSprinklerPlacementBehavior.ToString(),
      room.SelectedSprinklerOrientation,
      ruleSet.MaxSpacingFt,
      ruleSet.BoundaryClearanceFt,
      ruleSet.CoverageRadiusFt,
      placementZ,
      room.RoomId);

  if (isSidewall)
  {
      return SelectSidewallDirectional(
          room, geometry, ruleSet, obstacleBoxes, existingSprinklerXy,
          placementZ, config, result, ceilingUnsupported, ceilingNote);
  }
  ```
- **Lines 321–340**:
  For sidewall orientation, `orientationFactor = ruleSet.GetOrientationAdjustment("sidewall")` (= 0.85 from `HazardPlacementRuleSet.cs:117`) scales `ruleSet.MaxSpacingFt` from 15.0 ft to 12.75 ft for Light Hazard, and `ruleSet.CoverageRadiusFt` is divided by $\sqrt{2}$ (half-circle spray pattern).

### 1.2 Core Sidewall Solver: `SelectSidewallDirectional`
- **File**: `FireProtection.Backend\Services\Placement\Sprinklers\Final\BruteForce\BruteForceCalculationService.cs`
- **Lines 1379–1424**:
  ```csharp
  private static RoomCalculationResult SelectSidewallDirectional(
      PlacementRoomInput room,
      RoomGeometry geometry,
      HazardPlacementRuleSet ruleSet,
      List<ObstacleBox> obstacleBoxes,
      List<double[]> existingSprinklerXy,
      double placementZ,
      BruteForceCalculationConfig config,
      RoomCalculationResult result,
      bool ceilingUnsupported,
      string ceilingNote)
  {
      List<double[]> polygon = ExtractOuterPolygon(room);
      polygon = CleanPolygonLoop(polygon);
      if (polygon == null || polygon.Count < 3)
      {
          result.Status = CalculationStatus.InvalidRoomGeometry;
          result.Errors.Add("Sidewall solver: room polygon is degenerate (need at least 3 vertices).");
          return result;
      }
      result.Polygon = polygon;

      double alongSpacing = ruleSet.MaxSpacingFt > 0 ? ruleSet.MaxSpacingFt : 10.0;
      double hazardCeiling = GetNfpa13MaxSpacingCeiling(ruleSet.HazardClass);
      double throwDepth;
      bool throwAssumedSquare = false;
      if (room.TypeMaxCoverageAreaSqFt.HasValue && room.TypeMaxCoverageAreaSqFt.Value > 0 && alongSpacing > 0)
      {
          throwDepth = Math.Min(room.TypeMaxCoverageAreaSqFt.Value / alongSpacing, hazardCeiling);
      }
      else
      {
          throwDepth = alongSpacing;
          throwAssumedSquare = true;
      }
      if (throwDepth <= 0) throwDepth = alongSpacing;

      double endWallMax = alongSpacing * 0.5;
      double standOff = 0.5;
  ```
  - **Observation on Throw Capping**: `throwDepth` is currently bounded only by `hazardCeiling = GetNfpa13MaxSpacingCeiling(ruleSet.HazardClass)` (`:2515-2529`), which returns **15.0 ft** for Light/Ordinary Hazard. When `TypeMaxCoverageAreaSqFt` is missing, `throwDepth = alongSpacing` (which can be up to 15.0 ft). There is currently **no 14.0 ft cap**.
  - **Catalog Parameter**: `CatalogModels.cs:45` defines `public double? SidewallMaxThrowFt { get; set; }` and `ICatalog.cs:105` defines `SidewallMaxThrowFt`, but `PlacementRoomInput.cs` and `PlacementRoomSelection` do not expose or map `SidewallMaxThrowFt`.

### 1.3 Wall Orientation, Edge Geometry & Stand-Off
- **Lines 1425–1461**:
  - `bool isCCW = ComputeSignedArea(polygon) > 0;` determines polygon winding.
  - Tangent vector along edge $i$: $(u_x, u_y) = (e_x / L, e_y / L)$.
  - Inward normal into room: $(n_x, n_y)$, verified with `GeometryMath.PointInPolygon` probes at offsets 0.1, 0.25, 0.5 ft.
  - Head coordinates:
    ```csharp
    double bx = a[0] + ux * d, by = a[1] + uy * d;
    double cx = bx + nx * standOff, cy = by + ny * standOff;
    ```
    Where `standOff = 0.5` ft inboard from the host wall plane.
  - Wall edge index stamped on each candidate: `WallEdgeIndex = i`.

### 1.4 Spacing Along Wall & Alignment
- **Lines 1464–1483**:
  ```csharp
  double minSpacing = ruleSet.MinSpacingFt > 0 ? ruleSet.MinSpacingFt : 0.0;
  List<double> dists = new List<double>();
  if (edgeLen <= 2.0 * endWallMax)
  {
      dists.Add(edgeLen * 0.5);
  }
  else
  {
      double span = edgeLen - 2.0 * endWallMax;
      int segs = Math.Max(1, (int)Math.Ceiling(span / alongSpacing));
      double step = span / segs;
      if (segs >= 1 && step < minSpacing)
      {
          dists.Add(edgeLen * 0.5);
      }
      else
      {
          for (int k = 0; k <= segs; k++) dists.Add(endWallMax + k * step);
      }
  }
  ```
  - For opposing walls of equal length, vertex $A$ on the primary wall runs West-to-East while vertex $A$ on the opposite wall runs East-to-West. Because offsets are symmetrically centered from each corner (`endWallMax + k * step`), the calculated head coordinates along the wall naturally align across the room.

### 1.5 Wall Selection & Opposing Wall Pairing
- **Lines 1586–1629**:
  ```csharp
  // 1. Find longest edge with best initial coverage gain
  int longestEdge = -1;
  ...
  if (longestEdge >= 0 && remaining > 0)
  {
      ApplyEdge(longestEdge);

      // Measure room depth perpendicular to the longest edge:
      double[] anchor = polygon[longestEdge];
      double roomDepth = 0.0;
      foreach (double[] v in polygon)
      {
          if (v == null || v.Length < 2) continue;
          double d = (v[0] - anchor[0]) * edgeInX[longestEdge]
                   + (v[1] - anchor[1]) * edgeInY[longestEdge];
          if (d > roomDepth) roomDepth = d;
      }

      // If room depth exceeds throwDepth, select opposing wall:
      if (roomDepth > throwDepth + config.ToleranceFt)
      {
          int opposite = -1;
          double bestAnti = 0.5;
          for (int e = 0; e < rowsPerEdge.Count; e++)
          {
              if (usedEdge[e] || rowsPerEdge[e].Count == 0) continue;
              double dot = edgeUx[e] * edgeUx[longestEdge] + edgeUy[e] * edgeUy[longestEdge];
              if (-dot > bestAnti) { bestAnti = -dot; opposite = e; }
          }
          if (opposite >= 0)
              ApplyEdge(opposite);
      }
  }
  ```
  - **Opposing Wall Identification**: Evaluates `dot = edgeUx[e] * edgeUx[longestEdge] + edgeUy[e] * edgeUy[longestEdge]`. Anti-parallel edges produce `dot ≈ -1.0`, so `-dot > 0.5` selects the opposing wall running opposite to `longestEdge`.
  - **Greedy Loop Behavior (Lines 1631–1642)**:
    ```csharp
    while (remaining > 0)
    {
        int bestEdge = -1, bestGain = 0;
        for (int e = 0; e < rowsPerEdge.Count; e++)
        {
            int gain = EdgeGain(e);
            if (gain > bestGain) { bestGain = gain; bestEdge = e; }
        }
        if (bestEdge < 0 || bestGain == 0) break;
        ApplyEdge(bestEdge);
    }
    ```
    If `roomDepth > 28 ft`, the center remains uncovered, so `while (remaining > 0)` currently picks perpendicular side walls, creating an ad-hoc 4-wall ring.

### 1.6 Warning & Review Reporting
- **Lines 1673–1695**:
  ```csharp
  result.Status = CalculationStatus.ReviewRequired;
  result.Warnings.Add(
      "Horizontal-sidewall layout generated by the directional solver. Verify against the "
      + "manufacturer's listed coverage table with an FPE: along-wall spacing, throw/room-depth, "
      + "end-wall distance, and obstruction rules (NFPA 13 §11.3 / §10.3.5).");
  ...
  if (remaining > 0 && samples.Count > 0)
  {
      double pct = remaining * 100.0 / samples.Count;
      result.Warnings.Add(
          $"Sidewall coverage gap: {remaining} of {samples.Count} interior sample points ({pct:F1}%) "
          + "are beyond every wall's throw. The room is too deep for sidewall coverage from its walls "
          + "alone — add ceiling sprinklers or an interior branch, or reduce the protected depth.");
  }
  ```
  - **Missing Explicit Review Warning**: Does not emit the specific requirement warning: *"Room depth exceeds maximum sidewall throw and ceiling-mounted heads are required."*
  - Does not distinguish between:
    1. Single wall coverable ($\le 14\text{ ft}$).
    2. Opposing walls coverable ($> 14\text{ ft}$ and $\le 28\text{ ft}$).
    3. Exceeds max sidewall throw ($> 28\text{ ft}$).
    4. Room depth $> 14\text{ ft}$ where opposing walls cannot cover (e.g. no opposing wall exists).

### 1.7 Existing Test Coverage
- **`FireProtection.Tests\SidewallDirectionalSolverTests.cs`**:
  - `TestDeepRoomUsesOpposingWalls()` (line 104): tests 50x30 ft room uses $\ge 2$ distinct walls.
  - `TestHeadsDistributedAlongWall()` (line 117): tests 42x12 ft room spreads $\ge 3$ heads along wall.
  - `TestShallowRoomSingleRowCovers()` (line 130): tests 20x10 ft room uses exactly 1 wall.
  - `TestTooDeepRoomFlagsResidualGap()` (line 141): tests 40x60 ft room flags `ReviewRequired` with "coverage gap".
  - `TestAlwaysFlagsReview()` (line 157): tests `ReviewRequired` status and listing note.
  - `TestHeadsNotRejectedByTheirOwnHostWall()` (line 174): wall bounding-box obstacle clearance margin.
  - `TestMarginalWallDoesNotStackHeads()` (line 209): 13 ft wall gets 1 head, not 2 stacked.
- **`FireProtection.Tests\SidewallPlacementTests.cs`**:
  - `TestSidewallPlacesAlongWalls()`: heads are within 1.5 ft of walls.
  - `TestSidewallDoesNotPlaceInInterior()`: no heads within 5 ft of room center.
  - `TestSidewallAndPendentCandidateSetsDiffer()`: confirms distinct candidate distribution.
  - `TestSelectedSprinklerOrientationFlowsFromBehavior()`: builder orientation flow.
  - `TestSidewallOrientationFactorApplies()`: confirms 0.85 multiplier on spacing.

---

## 2. Logic Chain

1. **Throw Distance Capping**:
   - *Observation*: `throwDepth` is currently computed from `TypeMaxCoverageAreaSqFt / alongSpacing` and clamped only to `hazardCeiling` (15.0 ft) or defaults to `alongSpacing` (12.75–15.0 ft).
   - *Requirement*: Standard sidewall max throw is 12 to 14 ft, capped at 14.0 ft.
   - *Inference*: A constant `SidewallMaxThrowCapFt = 14.0` must be declared in `Nfpa13RulebookRules.cs`. In `SelectSidewallDirectional`, `throwDepth` must be capped via `Math.Min(throwDepth, Nfpa13RulebookRules.SidewallMaxThrowCapFt)`. If catalog `TypeSidewallMaxThrowFt` is present, it should be used (subject to the 14.0 ft cap); otherwise default to 14.0 ft.

2. **Depth Determination & Thresholding**:
   - *Observation*: `roomDepth` is calculated as the maximum scalar projection of all polygon vertices along `(edgeInX[longestEdge], edgeInY[longestEdge])`.
   - *Inference*:
     - **Case 1 (`roomDepth <= throwDepth`)**: Depth $\le 14.0\text{ ft}$. The single longest wall covers the depth. Opposing wall selection is skipped. Exactly 1 wall is used.
     - **Case 2 (`throwDepth < roomDepth <= 2.0 * throwDepth`)**: Depth between 14.0 ft and 28.0 ft. The primary wall cannot cover alone. The solver searches for an opposing wall (`dot < -0.5`). If found, heads are placed on opposing walls. Total reach covers up to $2 \times 14 = 28.0\text{ ft}$.
     - **Case 3 (`roomDepth > 2.0 * throwDepth` or depth $> 14.0\text{ ft}$ with no opposing wall)**:
       - If `roomDepth > 28.0 ft` (or $> 2.0 \times \text{throwDepth}$): Opposing walls cannot physically cover the center. The solver must place heads on opposing walls (up to 28 ft) AND flag `CalculationStatus.ReviewRequired` with the specific warning: *"Room depth ({roomDepth:F1} ft) exceeds maximum sidewall throw (28.0 ft); ceiling-mounted heads are required."*
       - If `roomDepth > throwDepth` (depth $> 14.0\text{ ft}$) but no opposing wall exists (`opposite < 0` or no valid opposing wall), opposing walls cannot cover. The solver must flag `CalculationStatus.ReviewRequired` with: *"Room depth ({roomDepth:F1} ft) exceeds single-wall sidewall throw ({throwDepth:F1} ft) and no opposing wall is available to provide coverage; ceiling-mounted heads are required."*

3. **Status Code & Diagnostic Reporting**:
   - *Observation*: Existing test `TestTooDeepRoomFlagsResidualGap()` asserts `r.Status == CalculationStatus.ReviewRequired`.
   - *Inference*: Flagging `CalculationStatus.ReviewRequired` with explicit warnings added to `result.Warnings` satisfies the requirement ("flag an error/review warning stating that room depth exceeds maximum sidewall throw and ceiling-mounted heads are required") while maintaining backwards compatibility with all 19 test suites.

---

## 3. Caveats

1. **Revit Runtime Verification**: Static code inspection and unit tests verify calculation mathematics and DTO outputs. Live Revit placement (e.g. `WallSidewallPlacementStrategy` creating `FamilyInstance` elements against actual Revit wall faces) requires an active Revit session.
2. **Catalog Property Flow**: `SidewallMaxThrowFt` is defined in `CatalogModels.cs` and `ICatalog.cs`, but is currently not mapped into `PlacementRoomInput.cs`. For complete catalog-driven throw distances (e.g. listed 12 ft throw vs 14 ft throw), this property can be plumbed through `PlacementRoomInputItem` $\to$ `PlacementRoomSelection` $\to$ `PlacementRoomInput`.
3. **No Code Modified**: Per explorer subagent constraints, no source files were altered during this investigation.

---

## 4. Conclusion & Recommendations

### 4.1 Exact Files to Modify

| File Path | Component | Changes Required |
|---|---|---|
| `FireProtection.Backend\Services\Placement\Sprinklers\Final\BruteForce\Nfpa13RulebookRules.cs` | Backend Rules | Add `SidewallMaxThrowCapFt = 14.0;` and `SidewallOpposingWallsMaxDepthFt = 28.0;` with NFPA 13 citations. |
| `FireProtection.Backend\Services\Placement\Sprinklers\Final\BruteForce\BruteForceCalculationService.cs` | Calculation Engine | In `SelectSidewallDirectional`: cap `throwDepth` at 14.0 ft; evaluate `roomDepth` against 14 ft and 28 ft; emit required warning when depth exceeds 28 ft or when depth exceeds 14 ft without opposing wall coverage. |
| `FireProtection.Backend\Models\Placement\Sprinklers\Final\PlacementRoomInput.cs` | DTO / Contract | (Optional/Recommended) Add `TypeSidewallMaxThrowFt` to allow catalog-listed 12–14 ft throw values. |
| `FireProtection.Backend\Services\Placement\Sprinklers\Final\PlacementInputBuilder.cs` | Input Builder | (Optional/Recommended) Forward `TypeSidewallMaxThrowFt` from selection. |
| `FireProtection.UI\Services\IPlacementInputExporter.cs` | UI Contract | (Optional/Recommended) Add `TypeSidewallMaxThrowFt` to `PlacementRoomInputItem`. |
| `FireProtection.Tests\SidewallDirectionalSolverTests.cs` | Unit Tests | Add test cases for single wall ($\le 14\text{ ft}$), opposing walls ($14–28\text{ ft}$), depth $> 28\text{ ft}$ requiring ceiling heads, and non-opposing wall geometries. |

### 4.2 Proposed Code Modifications

#### In `Nfpa13RulebookRules.cs`:
```csharp
// ---------------------------------------------------------------------------------
// Chapter 19 / NFPA 13 — Sidewall Sprinklers
// ---------------------------------------------------------------------------------

/// <summary>
/// NFPA 13 standard sidewall maximum throw distance is 12 to 14 ft, capped at 14.0 ft.
/// </summary>
public const double SidewallMaxThrowCapFt = 14.0;

/// <summary>
/// NFPA 13 maximum room depth protectable by sidewall sprinklers on opposing walls (2 * 14.0 ft = 28.0 ft).
/// </summary>
public const double SidewallOpposingWallsMaxDepthFt = 28.0;
```

#### In `BruteForceCalculationService.cs` (`SelectSidewallDirectional`):
```csharp
// 1. Resolve and cap throwDepth at 14.0 ft:
double alongSpacing = ruleSet.MaxSpacingFt > 0 ? ruleSet.MaxSpacingFt : 10.0;
double throwDepth = Nfpa13RulebookRules.SidewallMaxThrowCapFt; // Default 14.0 ft
bool throwAssumedSquare = false;

if (room.TypeSidewallMaxThrowFt.HasValue && room.TypeSidewallMaxThrowFt.Value > 0)
{
    throwDepth = Math.Min(room.TypeSidewallMaxThrowFt.Value, Nfpa13RulebookRules.SidewallMaxThrowCapFt);
}
else if (room.TypeMaxCoverageAreaSqFt.HasValue && room.TypeMaxCoverageAreaSqFt.Value > 0 && alongSpacing > 0)
{
    double derivedThrow = room.TypeMaxCoverageAreaSqFt.Value / alongSpacing;
    throwDepth = Math.Min(derivedThrow, Nfpa13RulebookRules.SidewallMaxThrowCapFt);
}
else
{
    throwDepth = Math.Min(alongSpacing, Nfpa13RulebookRules.SidewallMaxThrowCapFt);
    throwAssumedSquare = true;
}
if (throwDepth <= 0) throwDepth = Nfpa13RulebookRules.SidewallMaxThrowCapFt;

double maxOpposingDepth = 2.0 * throwDepth; // up to 28.0 ft
```

```csharp
// 2. Wall Selection & Opposing Wall Evaluation:
if (longestEdge >= 0 && remaining > 0)
{
    ApplyEdge(longestEdge);

    double[] anchor = polygon[longestEdge];
    double roomDepth = 0.0;
    foreach (double[] v in polygon)
    {
        if (v == null || v.Length < 2) continue;
        double d = (v[0] - anchor[0]) * edgeInX[longestEdge]
                 + (v[1] - anchor[1]) * edgeInY[longestEdge];
        if (d > roomDepth) roomDepth = d;
    }

    int opposite = -1;
    if (roomDepth > throwDepth + config.ToleranceFt)
    {
        double bestAnti = 0.5;
        for (int e = 0; e < rowsPerEdge.Count; e++)
        {
            if (usedEdge[e] || rowsPerEdge[e].Count == 0) continue;
            double dot = edgeUx[e] * edgeUx[longestEdge] + edgeUy[e] * edgeUy[longestEdge];
            if (-dot > bestAnti) { bestAnti = -dot; opposite = e; }
        }
        if (opposite >= 0)
            ApplyEdge(opposite);
    }

    // Check conditions requiring ceiling-mounted heads:
    if (roomDepth > maxOpposingDepth + config.ToleranceFt)
    {
        result.Status = CalculationStatus.ReviewRequired;
        result.Warnings.Add(
            $"Room depth ({roomDepth:F1} ft) exceeds maximum sidewall throw ({maxOpposingDepth:F1} ft for opposing walls, " +
            $"cap {Nfpa13RulebookRules.SidewallOpposingWallsMaxDepthFt:F1} ft); ceiling-mounted heads are required.");
    }
    else if (roomDepth > throwDepth + config.ToleranceFt && (opposite < 0 || rowsPerEdge[opposite].Count == 0))
    {
        result.Status = CalculationStatus.ReviewRequired;
        result.Warnings.Add(
            $"Room depth ({roomDepth:F1} ft) exceeds single-wall sidewall throw ({throwDepth:F1} ft), " +
            "and opposing walls cannot provide coverage; ceiling-mounted heads are required.");
    }
}
```

---

## 5. Verification Method

### 5.1 Test Execution Commands
The test suite can be run independently using the console test runner:
```powershell
# Run all tests on Revit 2026 configuration
dotnet run --project FireProtection.Tests/FireProtection.Tests.csproj -c Revit2026

# Build solution across Revit 2025 and 2026 configurations
dotnet build FireProtectionSystem.slnx -c Revit2025
dotnet build FireProtectionSystem.slnx -c Revit2026
```

### 5.2 Unit Test Scenarios to Validate
In `SidewallDirectionalSolverTests.cs`:
1. **Depth $\le 14\text{ ft}$**: Room 30 ft wide $\times$ 12 ft deep. Assert `DistinctWalls(r) == 1`. Assert no "ceiling-mounted heads are required" warning.
2. **$14\text{ ft} < \text{Depth} \le 28\text{ ft}$**: Room 30 ft wide $\times$ 24 ft deep. Assert `DistinctWalls(r) == 2`. Assert `remaining == 0` (no residual gap). Assert no "ceiling-mounted heads are required" warning.
3. **$\text{Depth} > 28\text{ ft}$**: Room 30 ft wide $\times$ 35 ft deep. Assert `r.Status == CalculationStatus.ReviewRequired`. Assert warning contains `"exceeds maximum sidewall throw"` and `"ceiling-mounted heads are required"`.
4. **Depth $> 14\text{ ft}$ with no opposing wall**: Open-boundary or triangular room with depth 20 ft where opposite wall has 0 segments. Assert `r.Status == CalculationStatus.ReviewRequired`. Assert warning contains `"ceiling-mounted heads are required"`.
5. **Throw Cap Verification**: Supply `TypeMaxCoverageAreaSqFt = 300` and `alongSpacing = 10` (mathematical throw 30 ft). Verify `throwDepth` is strictly capped at 14.0 ft.

### 5.3 Invalidation Conditions
- If `r.Status` is changed to `CalculationStatus.Failed` such that `r.Points.Count == 0`, existing test `TestDeepRoomUsesOpposingWalls` (50x30 ft room) will fail. Status must remain `CalculationStatus.ReviewRequired` to preserve compatibility while flagging the engineering review warning.
- If throw distance exceeds 14.0 ft for Light Hazard baseline rooms, the 14 ft cap is violated.
