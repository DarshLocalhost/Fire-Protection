# Fire Protection System — Complete Technical Explanation

> **Based on actual codebase analysis.** Every statement below is traceable to specific source files.
> Lines marked `[IMPLEMENTED]` are proven by code. Lines marked `[NOT IMPLEMENTED]` are absent from the codebase.

---

# PART 1 — PROJECT OVERVIEW

## Solution Structure

```
FireProtectionSystem/
├── FireProtection.Backend/          # Revit API layer + calculation engine
│   ├── Commands/                    # IExternalCommand (1 file)
│   ├── Revit/                       # IExternalApplication (ribbon bootstrap)
│   ├── Services/
│   │   ├── Catalog/                 # Excel catalog loader + validator
│   │   ├── Extraction/              # Revit model → ModelSnapshot
│   │   ├── Model/                   # Extractors (Level, Room, Ceiling, Obstacle, ExistingSprinkler)
│   │   └── Placement/               # All placement logic
│   │       ├── Devices/             # Shared fire-alarm device placement (smoke + notification)
│   │       ├── SmokeDetectors/      # Smoke detector family source + placement executor
│   │       ├── NotificationAppliances/  # Notification appliance family source + executor
│   │       └── Sprinklers/Final/    # Sprinkler placement service + BruteForce engine
│   ├── Models/
│   │   ├── DTOs/                    # All data transfer objects (ModelSnapshot, RoomData, etc.)
│   │   ├── Hazard/                  # HazardClassifier + HazardClass enum
│   │   └── Placement/               # Placement input DTOs
│   └── Infrastructure/              # Empty folder
│
├── FireProtection.UI/               # WPF MVVM front-end (NO Backend reference)
│   ├── ViewModels/                  # All ViewModels
│   ├── Views/                       # All XAML views
│   ├── Models/                      # UI-side models + calculation result DTOs
│   ├── Services/                    # Service interfaces + helpers
│   ├── Controls/                    # InfoBanner, SearchBox
│   ├── Converters/                  # InverseBool, HazardClassToBrush
│   └── Themes/                      # Colors.xaml, Styles.xaml
│
├── FireProtection.Tests/            # Unit tests
└── FireProtection.CatalogStandalone/ # Standalone catalog tool
```

## Architecture Principle

```
FireProtection.Backend ──references──► FireProtection.UI
FireProtection.UI                    (NO reference to Backend)
```

**Backend** contains all Revit API code. **UI** defines only interfaces and result models. This one-way dependency allows the UI to be tested without Revit.

---

# PART 2 — COMPLETE EXECUTION FLOW

## End-to-End: From Revit Button Click to Sprinkler Placement

```
Revit Application Startup
    │
    ▼
FireProtectionApplication.OnStartup()          [Backend/Revit/FireProtectionApplication.cs]
    │  Creates ribbon tab "Fire Protection"
    │  Creates panel + push button
    │  Button invokes: FireProtectionCommand
    │
    ▼
User clicks "Fire Protection" button
    │
    ▼
FireProtectionCommand.Execute()                [Backend/Commands/FireProtectionCommand.cs]
    │
    ├── 1. Gets hostDocument from commandData
    ├── 2. UiLauncher.TryActivateExisting()   → if window open, return
    ├── 3. ConfigureDisplayUnits()             → reads Revit project units
    ├── 4. RevitApiContext.CreateAndRegister() → creates ExternalEvent bridge
    │
    ├── 5. EXTRACTION:
    │   FireProtectionExtractionService.ExtractAndExport(hostDocument)
    │       ├── RevitModelContext (discovers links)
    │       ├── LevelExtractor.ExtractLevels()
    │       ├── CeilingExtractor.CollectAllCeilings()
    │       ├── RoomExtractor.ExtractRooms()
    │       ├── ObstacleExtractor.ExtractObstacles()
    │       ├── ExistingSprinklerExtractor.ExtractExistingSprinklers()
    │       ├── SpatialHelpers (associate obstacles/sprinklers to rooms)
    │       └── ModelExtractionValidator.Validate()
    │       → Returns: ModelSnapshot (JSON-serializable)
    │
    ├── 6. SERVICE WIRING:
    │   ├── CatalogHolder (shared mutable catalog ref)
    │   ├── RevitSprinklerFamilySource (lazy resolver)
    │   ├── PlacementInputJsonExporter
    │   ├── RevitSprinklerPlacementService
    │   ├── CatalogViewModel
    │   └── DevicePlacementSeams (smoke + notification sources + executors)
    │
    └── 7. LAUNCH UI:
        UiLauncher.Show(...) → Opens modeless MainWindow
            │
            ▼
        MainWindowViewModel
            ├── SprinklerViewModel
            │   ├── SprinklerBruteForceViewModel  (the main sprinkler tab)
            │   └── SprinklerCollisionViewModel   (placeholder, empty)
            ├── SmokeDetectorViewModel
            └── NotificationApplianceViewModel
```

## Sprinkler Placement: User Clicks "Place Sprinklers"

```
SprinklerBruteForceViewModel.PlaceSprinklersCommand
    │
    ▼
ExecutePlaceSprinklers()                        [UI/ViewModels/Sprinklers/BruteForce/SprinklerBruteForceViewModel.cs]
    │
    ├── ValidatePlacementInputs()                → family/type/rooms check
    ├── RevitApi.Run(PlaceSprinklersCore)        → queues work for Revit thread
    │
    ▼
PlaceSprinklersCore()                           [runs on Revit API thread]
    │
    ├── CollectSelectedRooms()                   → List<PlacementRoomInputItem>
    │
    ├── _placementInputExporter.ExportInput()    → builds PlacementInputSnapshot JSON
    │       └── PlacementInputBuilder.Build()    → stamps per-row device context
    │
    ├── _placementInputExporter.CalculateBruteForce()
    │       └── BruteForceCalculationService.Calculate(snapshot, rules, config)
    │           ├── For each room: CalculateRoom()
    │           │   ├── ExtractOuterPolygon()
    │           │   ├── ExtractInnerLoops()
    │           │   ├── new RoomGeometry(outer, innerLoops)
    │           │   ├── ParseHazardClass()
    │           │   ├── rules.GetRules(hazardClass) → HazardPlacementRuleSet
    │           │   ├── ApplyPerRoomOverrides()
    │           │   ├── SelectPrimaryCeiling()
    │           │   ├── Compute Z (ceiling / fallback)
    │           │   ├── Ceiling-height adjustment (NFPA 13 Table 21.2.3.1)
    │           │   ├── Orientation adjustment (sidewall = 0.85x)
    │           │   ├── [Branch: sidewall vs ceiling-grid]
    │           │   │   ├── GenerateSidewallCandidates() OR
    │           │   │   └── Grid sweep: for y... for x... → CandidatePoint
    │           │   │       ├── IsPointInsideRoom()        → reject outside
    │           │   │       ├── DistanceToOuterBoundary()  → reject too close to wall
    │           │   │       ├── InsideExpandedBox()        → reject inside obstacle
    │           │   │       └── Distance() to existing     → reject too close
    │           │   │
    │           │   └── SelectFromCandidates()             → greedy coverage-driven
    │           │       ├── Sort candidates (Y, X)
    │           │       ├── For each candidate:
    │           │       │   ├── Coverage check (skip if already covered)
    │           │       │   └── Min-spacing check (skip if too close)
    │           │       │   → Accept as CalculatedSprinklerPoint
    │           │       ├── Post-checks:
    │           │       │   ├── Max-spacing pair verification
    │           │       │   ├── Max-wall-distance verification
    │           │       │   ├── Coverage area validation
    │           │       │   └── Coverage gap detection (sample grid)
    │           │       └── Return RoomCalculationResult
    │
    ├── ProbeMissingFamilies()                   → MissingFamiliesModal
    │
    └── _sprinklerPlacementService.PlaceSprinklers()
            └── RevitSprinklerPlacementService.PlaceSprinklers()
                └── For each room → PlaceSinglePoint()
                    ├── ResolveHostLevel()           → level ID resolution
                    ├── ResolveFamily()              → FamilySymbol lookup
                    ├── Select strategy by FamilyPlacementType:
                    │   ├── FaceBasedPlacementStrategy
                    │   ├── WorkPlaneBasedPlacementStrategy
                    │   ├── LevelBasedPlacementStrategy
                    │   └── WallSidewallPlacementStrategy
                    ├── strategy.Place(context)      → NewFamilyInstance(...)
                    ├── StampTraceability()          → writes Comments parameter
                    └── Post-placement validation    → read back actual host/level
```

---

# PART 3 — REVIT DATA EXTRACTION

## FireProtectionExtractionService

**File:** `Backend/Services/Extraction/FireProtectionExtractionService.cs`

**9-step pipeline:**

```csharp
// Step 1: Create model context
RevitModelContext context = new RevitModelContext(hostDocument);

// Step 2: Extract Levels
List<LevelData> levels = _levelExtractor.ExtractLevels(context, issues);

// Step 3: Extract Ceilings (3D solid recursive geometry)
List<ExtractedCeilingItem> ceilingItems = _ceilingExtractor.CollectAllCeilings(context, issues);

// Step 4: Extract Rooms (with ceiling association)
List<RoomData> rooms = _roomExtractor.ExtractRooms(context, levels, ceilingItems, issues);

// Step 5: Extract Obstacles
List<ObstacleData> obstacles = _obstacleExtractor.ExtractObstacles(context, issues);

// Step 6: Extract Existing Sprinklers
List<ExistingSprinklerData> existingSprinklers = _existingSprinklerExtractor.ExtractExistingSprinklers(context, levels, issues);

// Step 7: Associate obstacles/sprinklers to rooms
AssociateObstaclesToRooms(rooms, obstacles);  // XY+Z overlap
AssociateSprinklersToRooms(rooms, existingSprinklers);  // point-in-polygon

// Step 8: Build ModelSnapshot
ModelSnapshot snapshot = new ModelSnapshot { ... };

// Step 9: Validate
snapshot.Summary = _validator.Validate(snapshot, issues);
```

### RoomExtractor — Boundary Extraction

**File:** `Backend/Services/Model/RoomExtractor.cs` (520 lines)

Key method: `ExtractBoundary(room, transform, issues)`

- Gets `BoundarySegment` loops from Revit `SpatialElementBoundaryLocation.Finish`
- For each loop, iterates segments
- Tessellates curves: straight segments → start/end points; arcs → 12-segment tessellation
- Transforms linked-model coordinates to host-MEP coordinates
- Produces `BoundaryData` with `OuterLoop` (polygon) + `InnerLoops` (holes)

```
Revit Room BoundarySegment[]
    │
    ▼
For each BoundarySegment loop:
    For each segment:
        if Curve is Line → Start, End points
        if Curve is Arc  → Tessellate into 12 segments
    Transform to host-MEP coordinates
    │
    ▼
BoundaryData
    ├── OuterLoop.Polygon  → List<double[]> (first loop = outer)
    └── InnerLoops[]       → List<BoundaryLoopData> (remaining loops = holes)
```

### CeilingExtractor — Slope Classification

**File:** `Backend/Services/Model/CeilingExtractor.cs` (301 lines)

- Collects all ceiling elements from host + linked models
- For each ceiling, extracts solids via `ExportSolids()` or `GeometryElement`
- Recursively walks `Solid.Faces` → `PlanarFace`
- Finds downward-facing faces (`face.Normal.Z < -0.5`)
- Classifies slope:
  - All faces within 0.5° → `FLAT`
  - Faces within 0.5°–5° → `SLOPED`
  - Multiple distinct slope planes → `STEPPED`
- Records `BottomElevationFt`, `TopElevationFt`, `SlopeDegrees`

### ObstacleExtractor

**File:** `Backend/Services/Model/ObstacleExtractor.cs` (237 lines)

Extracts axis-aligned bounding boxes for:
- `OST_StructuralColumns`, `OST_Columns`
- `OST_StructuralFraming` (beams)
- `OST_Walls`
- `OST_DuctCurves`, `OST_PipeCurves`, `OST_CableTray`

### ExistingSprinklerExtractor

**File:** `Backend/Services/Model/ExistingSprinklerExtractor.cs` (210 lines)

Extracts existing sprinkler instances, classifies mounting type by name/orientation heuristics (Pendent/Upright/Sidewall).

---

# PART 4 — ROOM AND SPACE GEOMETRY

## How the Project Represents a Room

### Actual Implementation: Polygon-Based

**File:** `Backend/Services/Placement/Sprinklers/Final/BruteForce/RoomGeometry.cs`

```csharp
internal sealed class RoomGeometry
{
    private readonly List<double[]> _outer;            // Outer polygon vertices
    private readonly List<List<double[]>> _innerLoops; // Holes (openings)

    // Bounding box computed from outer polygon
    private double _minX, _maxX, _minY, _maxY;
}
```

**Room shape handling:**
- Rooms are represented as **polygons** (arbitrary shape, not just rectangles)
- Outer loop = room boundary
- Inner loops = holes (e.g., columns, voids)
- Concave rooms are supported (point-in-polygon works on any shape)
- L-shaped, irregular rooms work correctly

### Point-in-Polygon: Even-Odd Ray Casting

**File:** `Backend/Services/Placement/Sprinklers/Final/BruteForce/GeometryMath.cs`

```csharp
public static bool PointInPolygon(double x, double y, List<double[]> polygon, double tolerance)
{
    // For each edge (pi → pj):
    //   1. If point is within tolerance of edge → inside
    //   2. Classic ray-casting: count intersections with horizontal ray
    //   3. Odd count = inside, even count = outside
}
```

### RoomGeometry.IsPointInsideRoom

```csharp
public bool IsPointInsideRoom(double x, double y, double tolerance)
{
    // Must be inside outer polygon
    if (!GeometryMath.PointInPolygon(x, y, _outer, tolerance)) return false;

    // Must NOT be inside any inner loop (hole)
    foreach (var inner in _innerLoops)
        if (GeometryMath.PointInPolygon(x, y, inner, tolerance)) return false;

    return true;
}
```

### Bounding Box Usage

The bounding box (`MinX/MaxX/MinY/MaxY`) is computed from the outer polygon vertices and is used **only** to scope the grid sweep range (for efficiency). It is NOT used for point-in-polygon tests.

```
Room Polygon                    Bounding Box (sweep scope)

    B ●─────────● C                 ●───────────────────●
    │           │                   │                   │
    │           │                   │   ROOM POLYGON    │
    │    ROOM   │                   │                   │
    │           │                   │                   │
    A ●───●─────● D                 ●───────────────────●

    PointInPolygon checks          Grid sweep iterates
    against actual polygon          from MinX..MaxX, MinY..MaxY
```

### Distance to Boundary

```csharp
public double DistanceToOuterBoundary(double x, double y)
{
    // Minimum distance from (x,y) to any outer polygon segment
    // Uses DistancePointToSegmentSquared for each edge
    // Returns sqrt of minimum squared distance
}
```

---

# PART 5 — COMPLETE SPRINKLER LOGIC

## 5.1 Entry Point

```
SprinklerBruteForceViewModel.PlaceSprinklersCommand
    → ExecutePlaceSprinklers()
        → RevitApi.Run(PlaceSprinklersCore)
            → PlacementInputJsonExporter.ExportInput()
            → PlacementInputJsonExporter.CalculateBruteForce()
                → BruteForceCalculationService.Calculate()
            → RevitSprinklerPlacementService.PlaceSprinklers()
```

**Class:** `SprinklerBruteForceViewModel`  
**File:** `UI/ViewModels/Sprinklers/BruteForce/SprinklerBruteForceViewModel.cs`

## 5.2 Input Data That Affects Sprinkler Calculation

| Factor | Actually Used? | Where Used | Class | Method | Effect |
|--------|---------------|------------|-------|--------|--------|
| Room Area | YES | Post-selection validation | BruteForceCalculationService | SelectFromCandidates | Per-sprinkler area check vs MaxCoverageAreaSqFt |
| Room Boundary (Polygon) | YES | Candidate generation + filtering | BruteForceCalculationService | CalculateRoom, SelectFromCandidates | Grid sweep bounds + inside-room check + boundary clearance |
| Room Shape | YES | Implicit via polygon | RoomGeometry | IsPointInsideRoom | Concave/irregular rooms handled |
| Ceiling Height | YES | Spacing adjustment (Light Hazard) | BruteForceCalculationService | CalculateRoom | heightFactor reduces MaxSpacingFt for >10ft ceilings |
| Ceiling Geometry | YES | Z computation + slope adjustment | BruteForceCalculationService | SelectPrimaryCeiling | FLAT/SLOPED/STEPPED selection, Z averaging for sloped |
| Hazard Classification | YES | Rule set selection | BruteForceCalculationService | CalculateRoom | Different MaxSpacingFt, CoverageRadiusFt, etc. per class |
| Device Family | YES | Placement behavior check | BruteForceCalculationService | CalculateRoom | Unsupported behavior → fail fast |
| Device Type | YES | Per-row family override | PlacementInputBuilder | Build | Row-level family/type override (Decision 017) |
| Orientation | YES | Spacing + coverage adjustment | BruteForceCalculationService | CalculateRoom | Sidewall: 0.85x spacing, coverage halved |
| Level | YES | Z computation fallback | BruteForceCalculationService | CalculateRoom | LevelElevationFt used when no ceiling |
| Ceiling Type | YES | Slope factor | HazardPlacementRuleSet | GetCeilingSlopeAdjustment | FLAT=1.0, SLOPED=0.9, STEPPED=0.85 |
| Per-room MaxSpacingFt override | YES | Rule clamping | BruteForceCalculationService | ApplyPerRoomOverrides | Clamped to NFPA 13 ceiling per hazard class |
| Per-room BoundaryClearanceFt override | YES | Rule clamping | BruteForceCalculationService | ApplyPerRoomOverrides | Minimum 0.0 ft |
| Existing Sprinklers | YES | Coverage + separation checks | BruteForceCalculationService | SelectFromCandidates | Skip covered candidates; reject too-close candidates |
| Obstacles | YES | Candidate rejection | BruteForceCalculationService | CalculateRoom | Expanded bounding box exclusion zone |
| Linked Model | YES | All geometry | RoomExtractor, CeilingExtractor, etc. | Extract* | Coordinates transformed to host-MEP at extraction |
| Family Placement Type | YES | Unsupported check + strategy | PlacementInputBuilder, BruteForceCalculationService | Build, CalculateRoom | Unsupported → block; drives strategy selection at placement |

## 5.3 How Is the Number of Sprinklers Determined?

### CalculatedCount (Actual Sprinklers Placed)

**IMPLEMENTED** — determined by the **greedy coverage-driven selection algorithm**, NOT by a simple area/coverage formula.

The algorithm:
1. Generates all valid candidate points (grid or sidewall)
2. Greedily selects candidates that are:
   - NOT already covered by an existing/placed sprinkler (within CoverageRadiusFt)
   - NOT too close to an already-placed sprinkler (within MinSpacingFt)
3. The number of selected points = CalculatedCount

### RequiredCount (Provisional Estimate)

**IMPLEMENTED** — simple area-based formula for reference only:

```csharp
double coverageArea = Math.PI * ruleSet.CoverageRadiusFt * ruleSet.CoverageRadiusFt;
int provisionalRequired = coverageArea > 0
    ? (int)Math.Ceiling(room.AreaSqFt / coverageArea)
    : selected.Count;
result.RequiredCount = provisionalRequired;
```

**Actual code:** `BruteForceCalculationService.cs:756-759`

```
RequiredCount = Ceiling(RoomArea / π × CoverageRadius²)
```

This is a **reference estimate**, not the actual count. The actual count comes from the greedy algorithm.

## 5.4 Candidate Point Generation

### Ceiling Grid (Pendent/Upright Sprinklers)

**IMPLEMENTED** — `BruteForceCalculationService.cs:390-466`

```
Room Bounding Box
        y
        ↑
        ●──●──●──●──●
        │  │  │  │  │
        ●──●──●──●──●
        │  │  │  │  │
        ●──●──●──●──●
        │  │  │  │  │
        ●──●──●──●──●
        └──────────────► x
```

**Actual code:**

```csharp
for (double y = geometry.MinY; y <= geometry.MaxY + config.ToleranceFt; y += gridRes)
{
    for (double x = geometry.MinX; x <= geometry.MaxX + config.ToleranceFt; x += gridRes)
    {
        CandidatePoint candidate = new CandidatePoint { X = x, Y = y, Z = placementZ };
        // ... validation checks ...
    }
}
```

### Grid Resolution

**IMPLEMENTED** — `BruteForceCalculationService.cs:1007-1045`

```
GridResolution = config.GridResolutionFt  (default: 1.0 ft)

Adjustments:
1. If GridRes > CoverageRadius → GridRes = CoverageRadius
2. Floor at CoverageRadius/2 if room is large enough
3. If estimated points > MaxCandidatePoints (4000) → double resolution until under cap
```

### Sidewall Candidate Generation

**IMPLEMENTED** — `BruteForceCalculationService.cs:795-920`

```
For each polygon edge (A → B):
    1. Compute edge direction and unit vector
    2. Compute two perpendicular normals
    3. Disambiguate inboard direction using IsPointInsideRoom test
    4. Step along edge at MaxSpacingFt intervals
    5. Project each step point 0.5 ft inboard (standoff)
    6. Validate: inside room, no obstacle, no existing sprinkler
```

## 5.5 How Are Candidates Filtered?

**Actual filtering order (ceiling grid):**

```
Candidate Point (x, y, z)
    │
    ├── 1. Inside room polygon?          → REJECT if outside
    │
    ├── 2. Too close to wall boundary?   → REJECT if distance < BoundaryClearanceFt
    │
    ├── 3. Inside obstacle zone?         → REJECT if inside expanded bounding box
    │   (with Z-axis guard: obstacle must span placementZ)
    │
    ├── 4. Too close to existing sprinkler? → REJECT if distance < ExistingSprinklerSeparationFt
    │
    └── 5. Accept as valid candidate
```

**Actual code:** `BruteForceCalculationService.cs:390-466`

## 5.6 How Are Final Sprinkler Points Selected?

**IMPLEMENTED** — Greedy coverage-driven selection (`BruteForceCalculationService.cs:499-775`)

**Algorithm:**

```
1. Sort valid candidates by (Y ascending, X ascending)
2. For each candidate:
   a. COVERAGE CHECK: Skip if within CoverageRadiusFt of any existing or placed sprinkler
   b. MIN-SPACING CHECK: Skip if within MinSpacingFt of any placed sprinkler
   c. If passes both → SELECT as new sprinkler point
3. POST-SELECTION VERIFICATION:
   a. Max-spacing pair check: warn if any pair exceeds MaxSpacingFt
   b. Max-wall-distance check: warn if any sprinkler > MaxDistanceFromWallsFt from wall
   c. Coverage area validation: warn if per-sprinkler area > MaxCoverageAreaSqFt
   d. Coverage gap detection: re-sample room on coarse grid, flag uncovered regions
```

**Key insight:** The algorithm is NOT brute-force combinatorial optimization. Despite the class name "BruteForce", it is a **single-pass greedy selection** that places every valid, uncovered candidate. The "brute force" refers to the exhaustive grid sweep of candidate points.

## 5.7 Spacing Logic

### DefaultHazardPlacementRules

**File:** `Backend/Services/Placement/Sprinklers/Final/BruteForce/DefaultHazardPlacementRules.cs`

| Hazard | MaxSpacingFt | MinSpacingFt | CoverageRadiusFt | MaxCoverageAreaSqFt | BoundaryClearanceFt |
|--------|-------------|-------------|-----------------|---------------------|---------------------|
| Light  | 15.0        | 6.0         | 7.5             | 225.0               | 1.0                 |
| OH1    | 12.0        | 6.0         | 6.0             | 130.0               | 1.0                 |
| OH2    | 12.0        | 5.0         | 5.0             | 100.0               | 1.0                 |
| EH1    | 10.0        | 4.5         | 4.5             | 90.0                | 1.5                 |
| EH2    | 10.0        | 4.5         | 4.5             | 90.0                | 2.0                 |

**All marked `IsProvisional = true`** — NFPA 13-2022 approved values not yet supplied.

### Ceiling-Height Adjustment (Light Hazard Only)

**IMPLEMENTED** — `BruteForceCalculationService.cs:269-317`

```
Ceiling Height > 10 ft → factor = 1.0 (no change)
Ceiling Height > 12 ft → factor = 0.95
Ceiling Height > 15 ft → factor = 0.90
Ceiling Height > 20 ft → factor = 0.85
Ceiling Height > 25 ft → factor = 0.80

Applied: MaxSpacingFt = MaxSpacingFt × heightFactor × slopeFactor × CeilingHeightAdjustmentFactor
```

### Slope Adjustment

```
FLAT    → factor = 1.0
SLOPED  → factor = 0.9
STEPPED → factor = 0.85
```

### Orientation Adjustment

```
pendent  → factor = 1.0
upright  → factor = 1.0
sidewall → factor = 0.85, coverage halved (CoverageRadius / √2)
```

## 5.8 Room Division / Grid Logic

**Does the current project divide rooms into grids?**

**YES** — but not a fixed grid subdivision. The algorithm uses a **fine grid sweep** over the room bounding box, then a **greedy selection** to choose which grid points become sprinklers.

```
Room Bounding Box with 1 ft grid:

    ●─●─●─●─●─●─●─●─●─●
    │ │ │ │ │ │ │ │ │ │
    ●─●─●─●─●─●─●─●─●─●
    │ │ │ │ │ │ │ │ │ │     All ● = candidates generated
    ●─●─●─●─●─●─●─●─●─●     (only those inside polygon)
    │ │ │ │ │ │ │ │ │ │
    ●─●─●─●─●─●─●─●─●─●

    After greedy selection:

    ● ─ ─ ─ ─ ● ─ ─ ─ ─ ●    ● = selected sprinkler
    │           │           │
    │           │           │
    │           │           │
    ● ─ ─ ─ ─ ● ─ ─ ─ ─ ●    (CoverageRadius determines spacing)
```

## 5.9 Obstacle Logic

### Obstacle Extraction

**IMPLEMENTED** — `Backend/Services/Model/ObstacleExtractor.cs`

Extracts bounding boxes for: columns, beams, walls, ducts, pipes, cable trays.

### Obstacle Effect on Placement

**IMPLEMENTED** — `BruteForceCalculationService.cs:421-445`

Each obstacle becomes an `ObstacleBox` with:
- XY bounding box (MinX, MinY, MaxX, MaxY)
- Z extent (MinZ, MaxZ)
- Category-specific clearance

**Candidate rejection rule:**
```csharp
if (GeometryMath.InsideExpandedBox(x, y, box.MinX, box.MinY, box.MaxX, box.MaxY, box.ClearanceFt))
{
    if (!box.SpansZ(placementZ, config.ToleranceFt)) continue; // Z guard
    // REJECT candidate
}
```

**Z-axis guard:** An obstacle only blocks a candidate if its vertical extent contains the placement Z. A low beam (MaxZ well below ceiling) does NOT block a ceiling-mounted sprinkler.

### Obstacle Clearance by Category

| Category | Light | OH1 | OH2 | EH1 | EH2 |
|----------|-------|-----|-----|-----|-----|
| beam     | 1.0   | 1.0 | 1.5 | 2.0 | 2.5 |
| column   | 1.0   | 1.0 | 1.0 | 1.5 | 2.0 |
| duct     | 1.5   | 1.5 | 2.0 | 2.5 | 3.0 |
| default  | 1.0   | 1.0 | 1.0 | 1.5 | 2.0 |

## 5.10 Existing Sprinkler Logic

**IMPLEMENTED** — `BruteForceCalculationService.cs:447-459, 556-575`

**Extraction:** ExistingSprinklerExtractor collects all sprinkler instances, records XY location.

**Effect on placement:**
1. **Coverage check:** A candidate within `CoverageRadiusFt` of an existing sprinkler is considered "already covered" and skipped.
2. **Separation check:** A candidate within `ExistingSprinklerSeparationFt` of an existing sprinkler is rejected.

```
Existing Sprinkler ●─────── CoverageRadius ───────●
                    │                              │
                    │  Candidates in this zone     │
                    │  are SKIPPED (already covered)│
                    │                              │
                    ●─────── SeparationFt ────────●
                    │                              │
                    │  Candidates in this zone     │
                    │  are REJECTED (too close)     │
```

## 5.11 Family and Type Effect

### DevicePlacementBehavior

**File:** `Backend/Models/Placement/Sprinklers/Final/DevicePlacementBehavior.cs`

| Behavior | Meaning | Effect |
|----------|---------|--------|
| CeilingOverhead | Pendent/upright on ceiling | Requires ceiling host |
| WallSidewall | Wall-anchored sidewall | Sidewall candidate branch |
| FaceHosted | FaceBased family | Requires ceiling host |
| WorkPlaneDependent | Work-plane family | Requires ceiling host |
| LevelHosted | OneLevelBased | No ceiling needed |
| Unsupported | Unresolved | Calculation blocked |

### Family/Type → Placement Behavior Mapping

**File:** `Backend/Services/Placement/DevicePlacementBehaviorResolver.cs`

```
FamilyPlacementType → DevicePlacementBehavior:
    FaceBased       → FaceHosted
    WorkPlaneBased  → WorkPlaneDependent
    OneLevelBased   → LevelHosted
```

Mount-aware refinement:
```
Behavior + "Sidewall" mount → WallSidewall
Behavior + "Pendent" mount  → CeilingOverhead
Behavior + "Upright" mount  → CeilingOverhead
```

### Effect on Calculation

| Factor | Calculation | Candidate Generation | Eligibility | Placement |
|--------|------------|---------------------|-------------|-----------|
| Family Name | YES (per-row) | NO | YES | YES |
| Type Name | YES (per-row) | NO | YES | YES |
| PlacementBehavior.Unsupported | → BLOCKS calculation | N/A | → BLOCKED | N/A |
| PlacementBehavior.WallSidewall | → sidewall branch | Sidewall candidates | YES | WallSidewallPlacementStrategy |
| PlacementBehavior.CeilingOverhead | → ceiling grid | Grid candidates | Requires ceiling | WorkPlaneBasedPlacementStrategy |
| PlacementBehavior.LevelHosted | → no ceiling required | Grid candidates | Always eligible | LevelBasedPlacementStrategy |

## 5.12 Ceiling / Host Logic

### Z Computation

**IMPLEMENTED** — `BruteForceCalculationService.cs:140-197`

```
1. SelectPrimaryCeiling(room) → bestCeiling
2. If bestCeiling exists:
   - FLAT ceiling → Z = BottomElevationFt
   - SLOPED ceiling → Z = (BottomElevationFt + TopElevationFt) / 2
3. If no ceiling:
   - If CeilingHeightFt available → Z = LevelElevationFt + CeilingHeightFt
   - Else → Z = LevelElevationFt (floor level, provisional)
```

### Placement Strategies

**File:** `Backend/Services/Placement/Sprinklers/Final/Strategies/`

| Strategy | FamilyPlacementType | Revit API Call |
|----------|-------------------|----------------|
| FaceBasedPlacementStrategy | FaceBased | `NewFamilyInstance(point, faceRef, sketchPlane)` |
| WorkPlaneBasedPlacementStrategy | WorkPlaneBased | `NewFamilyInstance(point, sketchPlane, structuralType)` |
| LevelBasedPlacementStrategy | OneLevelBased | `NewFamilyInstance(point, level, structuralType)` |
| WallSidewallPlacementStrategy | WallSidewall (by WallEdgeIndex) | Finds wall element → face → `NewFamilyInstance` on wall face |

### CeilingHostResolver

**File:** `Backend/Services/Placement/Sprinklers/Final/Strategies/CeilingHostResolver.cs`

- Searches Ceilings, Floors, Roofs in host document first
- Then searches linked models
- Prefers downward-facing planar faces
- Creates `Reference` objects for face-based placement

## 5.13 Sprinkler Eligibility

### 3-State Model

**File:** `UI/Services/PlacementEligibilityResult.cs`

| State | Meaning | UI Effect |
|-------|---------|-----------|
| ELIGIBLE | Room can receive sprinklers | Checkbox enabled, selectable |
| BLOCKED | Deterministic inability | Checkbox disabled, not selectable |
| UNDETERMINED | Configuration/infra error | Checkbox disabled, not selectable |

### What Freezes a Room (BLOCKED)

1. **No ceiling AND family requires ceiling host** → BLOCKED
2. **Unsupported placement behavior** → BLOCKED
3. **Family not loadable in Revit model** → BLOCKED

---

# PART 6 — COMPLETE SMOKE DETECTOR LOGIC

## 6.1 Entry Point

```
SmokeDetectorViewModel.PlaceDevicesCommand
    → DevicePlacementViewModelBase.ExecutePlaceDevices()
        → RevitApi.Run(PlaceDevicesCore)
            → RevitSmokeDetectorPlacementExecutor.ExecutePlacement()
                → FireAlarmDevicePlacementCore.ExecutePlacement()
                    → _calculate(items)  // injected delegate
                        → SmokeDetectorPlacementInputBuilder.Build()
                        → SmokeDetectorCalculationService.Calculate()
                    → For each room → PlaceSinglePoint()
```

## 6.2 Calculation Engine

**File:** `Backend/Services/Placement/SmokeDetectors/Final/BruteForce/SmokeDetectorCalculationService.cs` (944 lines)

### How Is the Required Number of Smoke Detectors Determined?

**IMPLEMENTED** — Same greedy coverage-driven algorithm as sprinklers.

```csharp
double coverageArea = ruleSet.MaxCoverageAreaSqFt > 0
    ? ruleSet.MaxCoverageAreaSqFt
    : Math.PI * ruleSet.CoverageRadiusFt * ruleSet.CoverageRadiusFt;
result.RequiredCount = coverageArea > 0
    ? (int)Math.Ceiling(room.AreaSqFt / coverageArea)
    : selected.Count;
```

### NFPA 72 Rules

**File:** `Backend/Services/Placement/SmokeDetectors/Final/BruteForce/Nfpa72SmokeDetectorRules.cs`

| Factor | Default | Adjusted |
|--------|---------|----------|
| MaxSpacingFt | 30.0 | Reduced by airflow, slope, wall mount |
| MinSpacingFt | 10.0 | — |
| MaxCoverageAreaSqFt | 900.0 | Reduced by airflow (125-900 sqft) |
| CoverageRadiusFt | 21.213 | S/√2 |
| MinBoundaryClearanceFt | 0.333 (4 in) | 0.05 for wall mount |
| MaxDistanceFromWallsFt | 15.0 | S/2 |
| WallMountDropFromCeilingFt | 0.5 | — |
| HvacSupplyRegisterClearanceFt | 3.0 | — |

### Airflow Adjustment (NFPA 72 Table 17.7.6.3.3.2)

```
ACH <= 7.5   → 900 sqft, 30ft spacing
ACH >= 8.6   → 875 sqft
ACH >= 10.0  → 750 sqft
ACH >= 12.0  → 625 sqft
ACH >= 15.0  → 500 sqft
ACH >= 20.0  → 375 sqft
ACH >= 30.0  → 250 sqft
ACH >= 60.0  → 125 sqft
```

### Slope Adjustment

```
FLAT    → no change
SLOPED  → spacing × 0.90
STEPPED → spacing × 0.85
```

### Wall Mount

```
spacing × 0.90
MinBoundaryClearanceFt = 0.05 (placed on wall face)
```

### Candidate Generation

**Two modes:**

1. **Ceiling mount** — grid sweep (same as sprinklers)
2. **Wall mount** — wall-edge stepping (same as sprinkler sidewall)

### Selection Algorithm

Same as sprinklers: greedy coverage-driven selection with post-checks.

## 6.3 Differences from Sprinklers

| Aspect | Sprinklers | Smoke Detectors |
|--------|-----------|-----------------|
| Hazard class | Yes (5 classes) | No (ceiling-driven) |
| Ceiling height adjustment | Yes (Light Hazard only) | No |
| Orientation (pendent/sidewall) | Yes | No (mount: Ceiling/Wall) |
| Airflow adjustment | No | Yes (NFPA 72 Table) |
| Wall mount drop | N/A | 0.5 ft below ceiling |
| Coverage area | π × r² | MaxCoverageAreaSqFt (900 default) |
| HasApprovedRules | No (provisional) | **Yes** (NFPA 72 approved) |

---

# PART 7 — COMPLETE NOTIFICATION APPLIANCE LOGIC

## 7.1 Entry Point

```
NotificationApplianceViewModel.PlaceDevicesCommand
    → DevicePlacementViewModelBase.ExecutePlaceDevices()
        → RevitApi.Run(PlaceDevicesCore)
            → RevitNotificationAppliancePlacementExecutor.ExecutePlacement()
                → FireAlarmDevicePlacementCore.ExecutePlacement()
                    → _calculate(items)  // injected delegate
                        → NotificationAppliancePlacementInputBuilder.Build()
                        → SmokeDetectorCalculationService.Calculate()  // REUSES smoke engine!
                    → For each room → PlaceSinglePoint()
```

## 7.2 Calculation Engine

**Reuses `SmokeDetectorCalculationService`** — the same coverage-grid engine as smoke detectors.

**File:** `Backend/Services/Placement/NotificationAppliances/Final/BruteForce/Nfpa72NotificationApplianceRules.cs`

### How Is the Required Number Determined?

Same as smoke detectors: `Ceiling(RoomArea / MaxCoverageAreaSqFt)`.

### Rating-Aware Rules

**IMPLEMENTED** — `Nfpa72NotificationApplianceRules.cs`

**Candela-based visible spacing:**

| Candela | Visible Spacing |
|---------|----------------|
| <= 15   | 30 ft          |
| <= 30   | 35 ft          |
| <= 75   | 40 ft          |
| <= 110  | 45 ft          |
| > 110   | 50 ft          |

**dBA-based audible spacing:**

| dBA  | Audible Spacing |
|------|----------------|
| < 85 | 20 ft          |
| < 90 | 25 ft          |
| < 95 | 30 ft          |
| >= 95| 35 ft          |

**Combined:** `spacing = Min(visibleSpacing, audibleSpacing)`

**Slope adjustment:**
```
FLAT/PEAKED → × 0.80
STEPPED     → × 0.75
```

**Wall mount:** `spacing × 0.85`

## 7.3 Candidate Generation

Reuses `SmokeDetectorCalculationService.GenerateCeilingCandidates()` or `GenerateWallMountCandidates()`.

## 7.4 Key Difference from Smoke Detectors

| Aspect | Smoke Detectors | Notification Appliances |
|--------|----------------|----------------------|
| HasApprovedRules | **Yes** | **No** (provisional) |
| Spacing basis | Ceiling-driven (NFPA 72 Ch.17) | Rating-driven (candela/dBA) |
| Default spacing | 30 ft | Varies (15-50 ft visible, 20-35 ft audible) |
| Coverage area | 900 sqft default | spacing² |

---

# PART 8 — COMPLETE GEOMETRY EXPLANATION

## 2D Geometry Operations

### Point-in-Polygon (Ray Casting)

```
Given point P and polygon with vertices V0..Vn:

inside = false
for each edge (Vi → Vj):
    if ray from P intersects edge:
        inside = !inside
return inside

With tolerance: if dist(P, edge) <= tolerance → inside
```

### Distance Point to Segment

```
Given point P and segment A→B:

t = dot(P-A, B-A) / |B-A|²
t = clamp(t, 0, 1)
closest = A + t × (B-A)
distance = |P - closest|
```

### Inside Expanded Box

```
Given point (x,y) and box (minX,minY,maxX,maxY) with clearance c:

inside if:
    x >= minX - c AND x <= maxX + c AND
    y >= minY - c AND y <= maxY + c
```

### Bounding Box Computation

From polygon vertices:
```
MinX = min(all vertex X)
MaxX = max(all vertex X)
MinY = min(all vertex Y)
MaxY = max(all vertex Y)
```

## 3D Geometry

### Z Computation

All Z values are in feet, normalized to host-MEP coordinates.

```
Placement Z = ceiling bottom elevation (FLAT)
            = (bottom + top) / 2 (SLOPED)
            = level elevation + ceiling height (fallback)
            = level elevation (last resort)
```

### Coordinate Transform

All linked-model geometry is transformed once at extraction time:
```
Revit XYZ (linked coords) × TotalTransform = host-MEP XYZ (feet)
```

No second transform is applied at placement time.

---

# PART 9 — ALL FACTORS THAT ACTUALLY AFFECT DEVICE PLACEMENT

| Factor | Sprinkler | Smoke Detector | Notification Appliance | Where Used |
|--------|-----------|---------------|----------------------|------------|
| Room Area | YES | YES | YES | Post-selection coverage validation |
| Room Boundary (Polygon) | YES | YES | YES | Candidate generation + filtering |
| Room Shape | YES | YES | YES | Implicit via polygon |
| Ceiling Height | YES | NO | NO | Spacing adjustment (Light Hazard) |
| Ceiling Geometry | YES | YES | YES | Z computation + slope adjustment |
| Hazard Class | YES | NO | NO | Rule set selection |
| Device Family | YES | YES | YES | Placement behavior check |
| Device Type | YES | YES | YES | Per-row family override |
| Orientation/Mount | YES | YES | YES | Spacing adjustment + candidate branch |
| Level | YES | YES | YES | Z computation fallback |
| Ceiling Type | YES | YES | YES | Slope factor |
| Per-room MaxSpacingFt | YES | YES | YES | Rule override |
| Per-room BoundaryClearanceFt | YES | YES | YES | Rule override |
| Obstacles | YES | YES | YES | Candidate rejection (bounding box + clearance) |
| Existing Devices | YES | YES | YES | Coverage + separation checks |
| Linked Model | YES | YES | YES | All geometry (transformed at extraction) |
| Airflow (ACH) | NO | YES | NO | NFPA 72 Table adjustment |
| Candela | NO | NO | YES | Visible spacing |
| dBA | NO | NO | YES | Audible spacing |

---

# PART 10 — ACTUAL DATA FLOW

## Sprinklers

```
Revit Room (SpatialElement)
    │
    ▼
RoomExtractor.ExtractRooms()
    ├── BoundarySegment[] → tessellate → List<double[]> polygon
    ├── HazardClassifier.ClassifyByName() → HazardData
    ├── CeilingExtractor → CeilingData list
    └── Area, Volume, BoundingBox, Level
    │
    ▼
RoomData (DTO)
    │
    ▼
ModelSnapshot (JSON)
    │
    ▼
FireProtectionUiData (deserialized)
    │
    ▼
RoomItemViewModel (UI row)
    ├── IsSelected, IsEligible
    ├── SelectedHazardClass
    ├── SelectedFamily, SelectedType
    ├── MaxSpacingFtOverride, BoundaryClearanceFtOverride
    └── SelectedOrientation
    │
    ▼
CollectSelectedRooms() → List<PlacementRoomInputItem>
    │
    ▼
PlacementInputBuilder.Build()
    ├── Resolves per-row DevicePlacementContext (family → behavior)
    ├── Derives orientation from behavior + catalog mount
    └── Creates PlacementRoomInput (boundary, ceilings, obstacles, etc.)
    │
    ▼
PlacementInputSnapshot
    │
    ▼
BruteForceCalculationService.Calculate()
    ├── For each room:
    │   ├── RoomGeometry (outer + inner loops)
    │   ├── HazardPlacementRuleSet (from DefaultHazardPlacementRules)
    │   ├── ApplyPerRoomOverrides()
    │   ├── Ceiling selection + Z
    │   ├── Ceiling-height/orientation adjustment
    │   ├── Candidate generation (grid or sidewall)
    │   ├── Filtering (inside room, boundary, obstacle, existing)
    │   └── Greedy selection → CalculatedSprinklerPoint[]
    └── Returns BruteForceCalculationResult
    │
    ▼
RevitSprinklerPlacementService.PlaceSprinklers()
    ├── For each room:
    │   ├── ResolveHostLevel()
    │   ├── ResolveFamily() → FamilySymbol
    │   ├── Select strategy (by PlacementBehavior)
    │   ├── strategy.Place() → NewFamilyInstance(...)
    │   ├── StampTraceability()
    │   └── Post-placement validation
    └── Returns SprinklerPlacementResult
```

## Smoke Detectors

```
Revit Room
    │
    ▼ (same extraction as sprinklers)
    │
    ▼
DevicePlacementViewModelBase
    ├── CollectSelectedRooms() → List<DeviceRoomInputItem>
    └── SmokeDetectorPlacementInputBuilder.Build()
        ├── Parses boundary/ceiling from room JSON
        └── Creates SmokeDetectorPlacementInputSnapshot
    │
    ▼
SmokeDetectorCalculationService.Calculate()
    ├── Same geometry pipeline as sprinklers
    ├── Nfpa72SmokeDetectorRules.GetRules()
    │   ├── Airflow adjustment
    │   ├── Slope adjustment
    │   └── Wall mount adjustment
    ├── Ceiling or wall candidate generation
    └── Greedy selection → CalculatedSmokeDetectorPoint[]
    │
    ▼
FireAlarmDevicePlacementCore.ExecutePlacement()
    └── PlaceSinglePoint() → NewFamilyInstance(...)
```

## Notification Appliances

```
Same as Smoke Detectors except:
    ├── NotificationAppliancePlacementInputBuilder.Build()
    │   └── Parses candela/dBA from pipe-delimited descriptor
    ├── Nfpa72NotificationApplianceRules.GetRules()
    │   ├── Visible spacing (candela-based)
    │   ├── Audible spacing (dBA-based)
    │   └── Combined = Min(visible, audible)
    └── SmokeDetectorCalculationService.Calculate() (REUSED)
```

---

# PART 11 — ACTUAL METHOD CALL TRACE

## Sprinkler Placement Call Trace

```
SprinklerBruteForceViewModel.ExecutePlaceSprinklers()
    │
    ├─► ValidatePlacementInputs()
    │
    └─► RevitApi.Run(PlaceSprinklersCore)
        │
        ├─► CollectSelectedRooms()
        │   Returns: List<PlacementRoomInputItem>
        │
        ├─► PlacementInputJsonExporter.ExportInput()
        │   ├─► PlacementInputBuilder.Build()
        │   │   ├─► SafeResolve() for each room
        │   │   │   └─► RevitSprinklerFamilySource.ResolveDeviceContext()
        │   │   │       └─► DevicePlacementBehaviorResolver.FromFamilyPlacementType()
        │   │   │       └─► DevicePlacementBehaviorResolver.FromResolvedFamily()
        │   │   Returns: PlacementInputSnapshot
        │   └─► Serializes to JSON
        │
        ├─► PlacementInputJsonExporter.CalculateBruteForce()
        │   └─► BruteForceCalculationService.Calculate(snapshot, rules, config)
        │       ├─► DefaultHazardPlacementRules.GetRules(hazardClass)
        │       │   Returns: HazardPlacementRuleSet
        │       │
        │       └─► For each room: CalculateRoom()
        │           ├─► ExtractOuterPolygon()
        │           ├─► ExtractInnerLoops()
        │           ├─► new RoomGeometry(outer, innerLoops)
        │           ├─► ParseHazardClass()
        │           ├─► ApplyPerRoomOverrides()
        │           ├─► SelectPrimaryCeiling()
        │           ├─► Ceiling-height adjustment
        │           ├─► Orientation adjustment
        │           ├─► BuildObstacleBoxes()
        │           ├─► BuildExistingSprinklerXy()
        │           │
        │           ├─► [if sidewall] GenerateSidewallCandidates()
        │           │   or
        │           ├─► [if ceiling] Grid sweep → List<CandidatePoint>
        │           │
        │           └─► SelectFromCandidates()
        │               ├─► Sort by (Y, X)
        │               ├─► Greedy select (coverage + min-spacing)
        │               ├─► Post-checks (max-spacing, wall-distance, coverage area, gaps)
        │               └─► Returns: RoomCalculationResult
        │       Returns: BruteForceCalculationResult
        │
        ├─► ProbeMissingFamilies()
        │
        └─► RevitSprinklerPlacementService.PlaceSprinklers()
            └─► For each room: PlaceSinglePoint()
                ├─► ResolveHostLevel()
                ├─► ResolveFamily()
                ├─► Select strategy
                ├─► strategy.Place()
                │   └─► NewFamilyInstance(...)
                ├─► StampTraceability()
                └─► Post-placement validation
```

---

# PART 12 — EXACT CODE BLOCKS

## Core Candidate Grid Generation

```csharp
// BruteForceCalculationService.cs:390-466
for (double y = geometry.MinY; y <= geometry.MaxY + config.ToleranceFt; y += gridRes)
{
    if (generated >= config.MaxCandidatePoints) break;
    for (double x = geometry.MinX; x <= geometry.MaxX + config.ToleranceFt; x += gridRes)
    {
        if (generated >= config.MaxCandidatePoints) break;
        generated++;

        CandidatePoint candidate = new CandidatePoint { X = x, Y = y, Z = placementZ };

        // 1. Inside room?
        if (!geometry.IsPointInsideRoom(x, y, config.ToleranceFt))
        {
            candidate.IsValid = false;
            candidate.RejectionReasons.Add("Outside room boundary");
            rejectedOutside++;
            continue;
        }

        // 2. Too close to wall?
        if (geometry.DistanceToOuterBoundary(x, y) < ruleSet.BoundaryClearanceFt - config.ToleranceFt)
        {
            candidate.IsValid = false;
            candidate.RejectionReasons.Add("Too close to room boundary");
            rejectedBoundary++;
            continue;
        }

        // 3. Inside obstacle?
        bool hitObstacle = false;
        foreach (ObstacleBox box in obstacleBoxes)
        {
            if (GeometryMath.InsideExpandedBox(x, y, box.MinX, box.MinY, box.MaxX, box.MaxY, box.ClearanceFt))
            {
                if (!box.SpansZ(placementZ, config.ToleranceFt)) continue;
                candidate.IsValid = false;
                candidate.RejectionReasons.Add("Inside/too close to obstacle: " + box.Category);
                rejectedObstacle++;
                hitObstacle = true;
                break;
            }
        }
        if (hitObstacle) continue;

        // 4. Too close to existing sprinkler?
        bool hitExisting = false;
        foreach (double[] es in existingSprinklerXy)
        {
            if (GeometryMath.Distance(x, y, es[0], es[1]) <= ruleSet.ExistingSprinklerSeparationFt - config.ToleranceFt)
            {
                candidate.IsValid = false;
                candidate.RejectionReasons.Add("Too close to existing sprinkler");
                rejectedExisting++;
                hitExisting = true;
                break;
            }
        }
        if (hitExisting) continue;

        candidate.IsValid = true;
        candidate.Score = 1.0;
        validCandidates.Add(candidate);
        validCount++;
    }
}
```

## Greedy Selection Algorithm

```csharp
// BruteForceCalculationService.cs:549-602
foreach (CandidatePoint candidate in validCandidates)
{
    if (iterations >= config.MaxSearchIterations) break;
    iterations++;

    // COVERAGE CHECK
    bool alreadyCovered = false;
    foreach (double[] es in existingSprinklerXy)
    {
        if (GeometryMath.Distance(candidate.X, candidate.Y, es[0], es[1]) <= coverageRadiusFt - config.ToleranceFt)
        {
            alreadyCovered = true;
            break;
        }
    }
    if (!alreadyCovered)
    {
        foreach (CalculatedSprinklerPoint s in selected)
        {
            if (GeometryMath.Distance(candidate.X, candidate.Y, s.X, s.Y) <= coverageRadiusFt - config.ToleranceFt)
            {
                alreadyCovered = true;
                break;
            }
        }
    }
    if (alreadyCovered) continue;

    // MIN SPACING CHECK
    bool tooClose = false;
    foreach (CalculatedSprinklerPoint s in selected)
    {
        double d = GeometryMath.Distance(candidate.X, candidate.Y, s.X, s.Y);
        if (d < minSpacingFt - config.ToleranceFt)
        {
            tooClose = true;
            break;
        }
    }
    if (tooClose) continue;

    selected.Add(new CalculatedSprinklerPoint
    {
        X = candidate.X, Y = candidate.Y, Z = candidate.Z,
        RoomId = room.RoomId, LevelId = room.LevelId,
        LevelName = room.LevelName, WallEdgeIndex = candidate.WallEdgeIndex
    });
}
```

## Point-in-Polygon (Ray Casting)

```csharp
// GeometryMath.cs:17-46
public static bool PointInPolygon(double x, double y, List<double[]> polygon, double tolerance)
{
    int n = polygon.Count;
    bool inside = false;

    for (int i = 0, j = n - 1; i < n; j = i++)
    {
        double[] pi = polygon[i];
        double[] pj = polygon[j];
        double xi = pi[0], yi = pi[1];
        double xj = pj[0], yj = pj[1];

        // Tolerant edge test
        if (DistancePointToSegmentSquared(x, y, xi, yi, xj, yj) <= tolerance * tolerance)
            return true;

        bool intersects = ((yi > y) != (yj > y)) &&
            (x < (xj - xi) * (y - yi) / (yj - yi) + xi);

        if (intersects) inside = !inside;
    }
    return inside;
}
```

---

# PART 13 — FORMULAS

## Coverage Radius from Spacing

```
CoverageRadiusFt = MaxSpacingFt / √2

Example: Light Hazard: 15 / √2 = 10.61 ft
         (but DefaultHazardPlacementRules hardcodes 7.5 ft)
```

## Required Count (Provisional Estimate)

```
RequiredCount = ⌈RoomArea / (π × CoverageRadius²)⌉

Example: 500 sqft room, 7.5 ft radius:
         500 / (π × 56.25) = 500 / 176.71 = 2.83 → 3 sprinklers
```

## Ceiling-Height Factor (Light Hazard)

```
heightFactor = 1.0               if ceilingHeight <= 10
             = 0.95              if ceilingHeight <= 12
             = 0.90              if ceilingHeight <= 15
             = 0.85              if ceilingHeight <= 20
             = 0.80              if ceilingHeight > 20
```

## Combined Spacing Factor

```
combinedFactor = heightFactor × slopeFactor × CeilingHeightAdjustmentFactor
MaxSpacingFt = MaxSpacingFt × combinedFactor
```

## Sidewall Coverage Halving

```
sidewallCoverageRadius = coverageRadius / √2
```

## Notification Appliance Spacing

```
visibleSpacing = f(candela)  // 30-50 ft based on candela rating
audibleSpacing = f(dba)      // 20-35 ft based on dBA rating
finalSpacing = Min(visibleSpacing, audibleSpacing) × slopeFactor × mountFactor
```

## Grid Resolution

```
gridRes = config.GridResolutionFt (1.0 ft default)

Adjustments:
gridRes = Min(gridRes, CoverageRadiusFt)
gridRes = Max(gridRes, CoverageRadiusFt/2) if room large enough
while estimatedPoints > MaxCandidatePoints:
    gridRes *= 2
```

---

# PART 14 — WHAT IS ACTUALLY IMPLEMENTED VS WHAT IS NOT

## SPRINKLERS

| Feature | Status | Evidence |
|---------|--------|----------|
| Room extraction | IMPLEMENTED | RoomExtractor.cs |
| Boundary polygon extraction | IMPLEMENTED | RoomExtractor.ExtractBoundary() |
| Hazard classification | IMPLEMENTED | HazardClassifier.ClassifyByName() |
| Ceiling extraction + slope | IMPLEMENTED | CeilingExtractor.cs |
| Obstacle extraction | IMPLEMENTED | ObstacleExtractor.cs |
| Existing sprinkler extraction | IMPLEMENTED | ExistingSprinklerExtractor.cs |
| Candidate generation (grid) | IMPLEMENTED | BruteForceCalculationService:390-466 |
| Candidate generation (sidewall) | IMPLEMENTED | GenerateSidewallCandidates() |
| Point-in-polygon filtering | IMPLEMENTED | GeometryMath.PointInPolygon() |
| Boundary clearance filtering | IMPLEMENTED | RoomGeometry.DistanceToOuterBoundary() |
| Obstacle filtering (XY+Z) | IMPLEMENTED | ObstacleBox.InsideExpandedBox() + SpansZ() |
| Existing device filtering | IMPLEMENTED | BruteForceCalculationService:447-459 |
| Greedy coverage selection | IMPLEMENTED | SelectFromCandidates() |
| Ceiling-height adjustment | IMPLEMENTED | BruteForceCalculationService:269-317 |
| Slope adjustment | IMPLEMENTED | HazardPlacementRuleSet.GetCeilingSlopeAdjustment() |
| Orientation adjustment | IMPLEMENTED | BruteForceCalculationService:319-344 |
| Per-room overrides | IMPLEMENTED | ApplyPerRoomOverrides() |
| Post-selection max-spacing check | IMPLEMENTED | BruteForceCalculationService:610-627 |
| Post-selection wall-distance check | IMPLEMENTED | BruteForceCalculationService:636-650 |
| Post-selection coverage area check | IMPLEMENTED | BruteForceCalculationService:658-669 |
| Coverage gap detection | IMPLEMENTED | BruteForceCalculationService:679-751 |
| RequiredCount formula | IMPLEMENTED | BruteForceCalculationService:756-759 |
| Z computation (ceiling/level) | IMPLEMENTED | BruteForceCalculationService:140-197 |
| Family placement type check | IMPLEMENTED | BruteForceCalculationService:209-224 |
| Ceiling-host requirement check | IMPLEMENTED | RequiresCeilingHost() |
| NFPA 13 approved values | NOT IMPLEMENTED | DefaultHazardPlacementRules.HasApprovedRules = false |
| Brute-force combinatorial optimization | NOT IMPLEMENTED | Single-pass greedy, not combinatorial |
| Beam/duct/column-specific spacing | PARTIAL | Dictionary lookup exists, values are placeholders |

## SMOKE DETECTORS

| Feature | Status | Evidence |
|---------|--------|----------|
| Room extraction | IMPLEMENTED | Same as sprinklers |
| NFPA 72 rules | IMPLEMENTED | Nfpa72SmokeDetectorRules.HasApprovedRules = true |
| Airflow adjustment | IMPLEMENTED | Nfpa72SmokeDetectorRules.ApplyAirflowAdjustment() |
| Slope adjustment | IMPLEMENTED | Nfpa72SmokeDetectorRules.ApplySlopeAdjustment() |
| Wall mount adjustment | IMPLEMENTED | Nfpa72SmokeDetectorRules:39-44 |
| Ceiling candidate generation | IMPLEMENTED | GenerateCeilingCandidates() |
| Wall mount candidate generation | IMPLEMENTED | GenerateWallMountCandidates() |
| Coverage-driven selection | IMPLEMENTED | SelectFromCandidates() (same algorithm) |
| Obstacle filtering | IMPLEMENTED | Same as sprinklers |
| Existing device filtering | IMPLEMENTED | Same as sprinklers |
| Post-selection checks | IMPLEMENTED | Same as sprinklers |
| Revit placement | IMPLEMENTED | FireAlarmDevicePlacementCore + RevitSmokeDetectorPlacementExecutor |
| HVAC supply register clearance | IMPLEMENTED | SmokeDetectorCalculationService:846-850 |
| PEAKED ceiling type | IMPLEMENTED | Nfpa72SmokeDetectorRules:79 (treated as SLOPED) |

## NOTIFICATION APPLIANCES

| Feature | Status | Evidence |
|---------|--------|----------|
| Rating-aware spacing | IMPLEMENTED | Nfpa72NotificationApplianceRules.VisibleSpacing/AudibleSpacing |
| Candela-based visible spacing | IMPLEMENTED | Nfpa72NotificationApplianceRules:89-96 |
| dBA-based audible spacing | IMPLEMENTED | Nfpa72NotificationApplianceRules:98-105 |
| Combined constraint | IMPLEMENTED | Nfpa72NotificationApplianceRules:37 |
| Slope adjustment | IMPLEMENTED | Nfpa72NotificationApplianceRules:107-113 |
| Wall mount adjustment | IMPLEMENTED | Nfpa72NotificationApplianceRules:44 |
| Reuses smoke detector engine | IMPLEMENTED | SmokeDetectorCalculationService.Calculate() |
| NFPA 72 approved values | NOT IMPLEMENTED | Nfpa72NotificationApplianceRules.HasApprovedRules = false |
| Candela/dBA parsing from descriptor | IMPLEMENTED | Nfpa72NotificationApplianceRules.ParseDescriptor() |
| Revit placement | IMPLEMENTED | FireAlarmDevicePlacementCore + RevitNotificationAppliancePlacementExecutor |

---

# PART 15 — DEAD CODE AND UNUSED CODE

| File | Status | Notes |
|------|--------|-------|
| `SprinklerCollisionViewModel` | EMPTY STUB | Collision tab placeholder, no logic |
| `SprinklerCollisionView.xaml` | EMPTY STUB | Empty Grid |
| `Infrastructure/` folder | EMPTY | Created but never populated |
| `Models/Extraction/` folder | EMPTY | Created but never populated |
| `JsonSnapshotExporter.ExportToFile()` | STUBBED | Returns null, not called in production path |
| `RevitSprinklerFamilySource.GetAvailableFamilies()` | GATED | Returns empty unless `FireProtectionConfig.UseRevitFamilyListing` is true |
| `SprinklerPlacementResult` (UI model) | USED | Carries placement result back to UI |
| `CandidatePoint.Score` | SET BUT NOT USED | Always set to 1.0, never read for selection |

---

# PART 16 — IMPORTANT GAPS

## Missing / Not Found

1. **NFPA 13-2022 approved spacing values** — `DefaultHazardPlacementRules.HasApprovedRules = false`. All values are provisional placeholders.

2. **Sprinkler collision detection** — The `SprinklerCollisionViewModel` is an empty stub. No collision logic exists.

3. **Combinatorial optimization** — Despite the "BruteForce" name, the algorithm is single-pass greedy, not combinatorial. There is no brute-force search of all possible combinations.

4. **Room division into sub-regions** — Rooms are NOT divided into smaller regions. The algorithm sweeps the entire room bounding box as one region.

5. **Coverage pattern adjustment** — `HazardPlacementRuleSet` has a `CoveragePatternAdjustments` dictionary (circular=1.0, rectangular=0.9, square=0.95), but it is NEVER READ by the calculation engine.

6. **Beam/duct/column-specific spacing** — `ObstacleSpecificClearances` dictionary exists but values are placeholders. No NFPA 13 beam-spacing rules are implemented.

7. **Room hazard auto-override from catalog** — The catalog provides hazard classes per sprinkler type, but the room's hazard class is determined solely by `HazardClassifier.ClassifyByName()` (keyword-based). There is no catalog-driven hazard override.

---

# PART 17 — FINAL END-TO-END EXPLANATION

## Sprinklers — In Plain English

```
Imagine the room is a playground.

1. Revit tells us the playground shape (polygon vertices).
2. The extractor collects the corners, converting linked-model coordinates.
3. The application creates room data with boundary, ceiling, obstacles.
4. The UI shows the room with hazard class, family/type options.
5. The system checks if the selected sprinkler can actually be placed
   (can the family be loaded? is there a ceiling host? is the behavior supported?).
6. If not, the room is frozen (BLOCKED).
7. If yes, the room enters calculation.
8. The system picks the best ceiling (FLAT > SLOPED > STEPPED, level-matched first).
9. It computes the placement Z from the ceiling.
10. It adjusts spacing for ceiling height (Light Hazard only) and slope.
11. It generates candidate locations on a 1-ft grid over the room bounding box.
12. It removes candidates that are:
    - Outside the room polygon
    - Too close to walls (BoundaryClearanceFt)
    - Inside obstacle zones (with Z-axis guard)
    - Too close to existing sprinklers
13. It sorts remaining candidates by (Y, X).
14. It greedily selects candidates:
    - Skip if already covered by an existing/placed sprinkler (CoverageRadiusFt)
    - Skip if too close to a placed sprinkler (MinSpacingFt)
    - Otherwise, ACCEPT as a new sprinkler
15. It verifies post-selection:
    - Are any pair of sprinklers too far apart? (MaxSpacingFt)
    - Are any sprinklers too far from walls? (MaxDistanceFromWallsFt)
    - Is per-sprinkler coverage area too large? (MaxCoverageAreaSqFt)
    - Are there coverage gaps? (re-samples room interior)
16. Revit resolves family/type/host for each point.
17. Revit creates the sprinkler FamilyInstance.
18. The system writes diagnostic data into the Comments parameter.
```

## Smoke Detectors — In Plain English

```
1. Same room extraction as sprinklers.
2. NFPA 72 rules determine spacing (30ft default, reduced by airflow/slope).
3. Ceiling-mounted: same grid sweep as sprinklers, different spacing.
4. Wall-mounted: steps along room boundary, 0.15 ft standoff, 0.5 ft below ceiling.
5. Same greedy selection algorithm.
6. Revit creates the fire alarm device FamilyInstance.
```

## Notification Appliances — In Plain English

```
1. Same room extraction.
2. Candela rating determines visible spacing (30-50 ft).
3. dBA rating determines audible spacing (20-35 ft).
4. Uses the stricter of the two constraints.
5. Slope and wall mount further reduce spacing.
6. Reuses the smoke detector coverage-grid engine for candidate generation and selection.
7. Revit creates the fire alarm device FamilyInstance.
```

---

*Document generated from codebase analysis on 2026-09-08.*
*All conclusions traceable to actual source files in FireProtectionSystem/.*
