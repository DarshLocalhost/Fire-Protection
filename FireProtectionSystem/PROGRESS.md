# FireProtectionSystem — Progress

## Fix: Per-Sprinkler Overhead Host Resolution & Pod Ceiling Handling (2026-11-12)

### Context & Defect
- Units with modular model groups (such as 134 SF bathroom pods in 593 SF units on Level 4 of Snowdon Towers) adopted the pod's 8' ceiling height for the entire room.
- Sprinklers in the main living space floated mid-air 4.8 ft below the 12' 10" structural slab, while pendant lights correctly mounted at 12' 10".

### Implementation
1. **`CeilingData.cs` & `CeilingExtractor.cs`**:
   - Added `IsModelGroup` (`element.GroupId != InvalidElementId`) and `AreaSqFt`.
2. **`RoomExtractor.cs`**:
   - Classifies ceilings with `IsModelGroup || AreaSqFt / RoomArea < 0.50` as partial pod ceilings.
   - For rooms with only partial pod ceilings, resolves the full-coverage overhead structural slab (12' 10") as the room's primary ceiling boundary.
3. **`CeilingHostResolver.cs`**:
   - Implemented vertical column raycasting directly above $(point.X, point.Y)$.
   - Resolves the lowest downward face overhead: points under the pod attach at 8' 0"; points in the open area attach at 12' 10".
4. **`LevelBasedPlacementStrategy.cs`**:
   - Aligns `OneLevelBased` families (such as upright sprinklers) to `host.ProjectedPointOnFace.Z - 0.0833`.

### Verification
- Clean builds for `Revit2025` and `Revit2026`.
- All 19 test suites passed (`ALL TESTS PASSED`).
- Fresh DLLs deployed to `%APPDATA%\Autodesk\Revit\Addins\2025` and `2026`.

---

## Fix: Overhead Floor/Roof Slab Identification for Open-Ceiling Rooms (2026-11-12)

### Context & Defect
- In models with open-structure rooms (e.g. Snowdon Towers Sample Architectural Level 4), only Corridor 402 has an actual finished ceiling (`OST_Ceilings`), while Units 401, 403, 404, 405, 406, 407, 408, 409, and 410 are open to the structural floor slab above.
- In `SPRINKLER_PlacementResult_20261112_162535.json`:
  1. Room 401 had 0 sprinklers placed because 0 ceilings were extracted, triggering `CalculationStatus.MissingCeiling` when ceiling-hosted families were chosen.
  2. Rooms 403, 405, 406, 409 falsely matched Corridor 402's ceiling due to coarse AABB overlap, placing candidate points at corridor ceiling height (8.33 ft). During placement, Revit rejected them (*"WorkPlaneBased placement failed: no usable ceiling face"*) or fell back to mid-air `SketchPlane` floating 4+ feet below the structural slab.
  3. `CeilingHostResolver` skipped floor slabs because the floor slab of Level 5 has `LevelId != Level 4`.

### Implementation
1. **`CeilingExtractor.cs`**:
   - Collects `OST_Floors` and `OST_Roofs` alongside `OST_Ceilings`, extracting bottom planar faces (`FaceNormal.Z < -0.1`) and bottom elevation.
2. **`RoomExtractor.cs` (`FindCeilingsForRoom` & `CeilingIntersectsRoom`)**:
   - Two-pass matching: Pass 1 matches finished `OST_Ceilings` using face projection (`face.Project`) and mesh triangulation against room polygon, ensuring corridor ceilings never falsely match adjacent rooms.
   - Pass 2: If no finished ceiling exists, selects the overhead structural slab (`OST_Floors`/`OST_Roofs`) directly above the room (`slabBottomZ >= roomBottomZ + 4.0` and `<= roomTopZ + 4.0`), setting the room ceiling height to the true distance from floor to slab underside.
   - Finished ceilings strictly take priority over overhead slabs.
3. **`CeilingHostResolver.cs`**:
   - Level filtering (`element.LevelId.Equals(level.Id)`) is restricted to `element is Ceiling`. For `Floor` and `RoofBase`, 3D bounding box (`zTol = 4.0`) and downward face projection are used so the overhead slab of the level above is resolved as a valid host face.
4. **Zero Impact on Calculation Logic**: Spacing formulas, hazard rule sets, NFPA 13 tables, obstruction checks, and grid algorithms remain 100% untouched.

### Verification
- Clean compilation for `Revit2025` and `Revit2026`.
- All 19 test suites passed (`ALL TESTS PASSED`).
- Fresh DLLs deployed to `%APPDATA%\Autodesk\Revit\Addins\2025` and `2026`.

---

## Fix: Sidewall Obstacle Notch Bridging and Ignore-Obstacles Contract (2026-11-12)

### Context & Defect
- When "Ignore Obstacles" was checked, sidewall sprinklers clashing with obstacles (e.g. column projections and casework along walls) were still being skipped rather than placed at calculated obstacle locations.
- Root cause:
  1. Room-bounding elements created notches in the boundary polygon extracted from Revit, fragmenting continuous host walls (e.g., South wall in Kitchen R-01) into disjoint micro-segments.
  2. The candidate generation dropped heads whose standoff coordinate fell inside obstacle jogs (`!geometry.IsPointInsideRoom`).
  3. `RevitSprinklerPlacementService` rejected points colliding with room-bounding obstacles under the `!IsPointInsideRoom` host-space guard even when `ignoreObstacles == true`.

### Implementation
1. **Obstacle Notch Bridging (`BruteForceCalculationService.cs`)**:
   - Implemented `BridgeObstacleNotches` to detect local polygon notches returning to a collinear baseline within 3.5 ft depth and 15 ft span, restoring the true continuous host wall baseline for pure grid spacing.
   - Initialized `bridgedGeometry` so point-in-room checks operate against the continuous room baseline.
   - Updated standoff candidate filtering to retain points landing inside obstacle bounding boxes along the wall so that heads are preserved as candidates with `HasObstacleConflict = true`.
   - Updated `BuildWallEdgeMask` to aggregate physical wall segment lengths so obstacle notches do not mark an entire physical wall as non-wall.
2. **Placement Ignore-Obstacles Enforcement (`RevitSprinklerPlacementService.cs`)**:
   - Relaxed the host-space guard `!IsPointInsideRoom` for sidewall heads when `ignoreObstacles && point.HasObstacleConflict && point.WallEdgeIndex.HasValue`.
   - When "Ignore Obstacles" is checked, all calculated sidewall heads (including those on columns or casework) are placed onto the host wall.
   - When "Ignore Obstacles" is unchecked, only colliding heads (`HasObstacleConflict == true`) are skipped as expected.
3. **Verification**:
   - Added unit test `TestSidewallObstacleNotchBridgedAndFlagged` in `SidewallPlacementTests.cs`.
   - All 19 test suites passed (`ALL TESTS PASSED`).
   - Clean builds for `Revit2025` and `Revit2026`.
   - Fresh DLLs deployed to `%APPDATA%\Autodesk\Revit\Addins\2025` and `2026`.

---

## Feature: Placement Result UI Coverage Verification & ReviewRequired Flagging (2026-11-12)

### Context & Need
- The Placement Result Report UI displayed "Success" with a green badge for all rooms as long as the Revit family instances were created without Revit API transaction crashes, even if room throw/coverage left significant gaps (e.g. when room depth exceeded throw or obstacle skips left rooms partially covered).
- In addition, users lacked visibility into the actual geometric area coverage percentage achieved per room.

### Implementation
1. **`PlacementRoomReport` & `PlacementRoomReportViewModel` Data Model**:
   - Added `CoveragePercent` (`double?`) to `PlacementRoomReport`.
   - Exposed `CoverageText` (`{CoveragePercent:F1}%` or `"-"`) in `PlacementRoomReportViewModel`.
   - Added `Coverage` column to CSV export.
2. **`PlacementResultReportView.xaml`**:
   - Added a dedicated `Coverage` DataGrid text column between `Level` and `Status`.
3. **`SprinklerPlacementReportMapper` Coverage Engine**:
   - Implemented 2D ray-casting point-in-polygon sampling ($0.5\text{ ft}$ grid) evaluating the room polygon against all successfully placed sprinkler positions.
   - Evaluates coverage against the room's calculated NFPA throw radius (`AppliedMaxSpacingFt` or default $12.75\text{ ft}$).
   - **Threshold Enforcement**:
     - $\ge 95.0\%$: Retains `Success` status and records `Coverage: XX.X%.` in the message.
     - $< 95.0\%$: Automatically overrides status to `ReviewRequired` (warning), records `Coverage incomplete: XX.X% (threshold 95%).`, and surfaces an issue in `report.ReviewRequiredReasons`.
4. **Verification**:
   - Added unit tests `TestMapperCoverageEvaluationSuccess` and `TestMapperCoverageDeficientTriggersReviewRequired` in `DeviceReportAndKindTests.cs`.
   - All 19 test suites passed (`ALL TESTS PASSED`).
   - Clean builds for both `Revit2025` and `Revit2026`.

---

## Fix: Sidewall boundary filtering for non-wall room edges (2026-11-11)

### Context & Defect
- Sidewall candidate rows were created from every room polygon edge even when the edge came from a room-separation line, opening, or other non-wall boundary.
- The extracted `BoundarySegmentData.IsWall` metadata existed, but the sidewall solver never read it, which allowed phantom sidewall heads on virtual edges and broke placement in kitchens/studies and other rooms with non-wall boundaries.
- This is a narrow sidewall-only fix; ceiling, smoke, and notification placement logic were left untouched.

### Implementation
1. `RoomExtractor` now tags each boundary segment as `IsWall` when the underlying Revit element is a `Wall`.
2. `BruteForceCalculationService.SelectSidewallDirectional` now builds a wall-edge mask and skips any polygon edge whose boundary segment is not a physical wall before generating sidewall rows.
3. The regression test `TestSidewallIgnoresNonWallBoundarySegments` is included in the sidewall suite so the room-edge bug is covered in the calculation harness.

### Verification status
- The repository is not currently buildable in this environment because the Autodesk Revit assemblies are unavailable here; the command output shows `CS0246` missing `Autodesk.*` references.
- The root cause is directly evidenced in the source path above and is scoped to the sidewall logic and boundary metadata used by it.

---

## Fix: Obstacle Hydration for Smoke Detectors & Notification Appliances (2026-11-10)

### Context & Defect
- In smoke detector placement, selecting "No" on the obstacle handling prompt (*"Skip devices with detected obstacle conflicts"*) failed to skip conflicting detectors (e.g. Kitchen R-01 where a detector landed directly on an MEP duct).
- Root Cause Analysis:
  - While sprinklers receive `snapshot.Obstacles` via `PlacementInputJsonExporter`, `RevitSmokeDetectorPlacementExecutor` and `RevitNotificationAppliancePlacementExecutor` were constructed without the obstacle list.
  - `SmokeDetectorPlacementInputBuilder` and `NotificationAppliancePlacementInputBuilder` did not link either room-associated obstacle IDs or global obstacles into `SmokeDetectorRoomInput.Obstacles`.
  - As a result, `roomInput.Obstacles` was empty, `CheckObstacleClash` found 0 obstacles, `point.HasObstacleConflict` remained `false`, and no devices could ever be skipped or flagged.
  - In addition, in `ModelBackedCatalog`, `OST_FireAlarmDevices` was shared without filtering, causing notification appliances (horns, strobes) to appear in the Smoke Detectors dropdown.

### Implementation
1. **Obstacle Wiring in Placement Executors**:
   - `RevitSmokeDetectorPlacementExecutor` and `RevitNotificationAppliancePlacementExecutor` now accept `Func<IReadOnlyList<ObstacleData>> obstaclesAccessor`.
   - Wired `() => snapshot.Obstacles` in `FireProtectionCommand.cs`.
2. **Room Obstacle Association in Builders**:
   - `SmokeDetectorPlacementInputBuilder.BuildSnapshot` and `NotificationAppliancePlacementInputBuilder.BuildSnapshot` associate obstacles from `globalObstacles` matching `item.RoomId` or `roomData.AssociatedObstacleIds`.
   - `HydrateFromRoomJson` returns `RoomData` so room-associated obstacle IDs are parsed.
3. **Segregated Device Family Picker in `ModelBackedCatalog`**:
   - Classified `deviceTypes` using `DeviceKindResolver.TryResolve` into `_smokeFamilies` and `_notificationFamilies`.
   - Smoke Detector tab shows only smoke detector families; Notification Appliances tab shows only notification appliances.
4. **Verification**:
   - Built cleanly on `Revit2025` and `Revit2026` (`net8.0-windows`).
   - Headless test suite: all 19 suites passed (`ALL TESTS PASSED`).


## Fix: Obstacle Conflict Flagging for Structural Obstructions & Three-Times Rule (2026-11-06)

### Context & Defect
- When the user selected "No" on the obstacle handling confirmation dialog (*"Skip candidate sprinklers that conflict with obstacles"*), the tool still placed a sprinkler in Room 395769 (`KITCHEN R-01`) directly adjacent to Column 460814.
- Inspection revealed that in `RevitSprinklerPlacementService.RunPlacementTransaction`, candidates are filtered via `!ignoreObstacles && point.HasObstacleConflict`.
- However, in `BruteForceCalculationService.cs` (`SelectCenteredGrid`, `SelectCenteredGridOnTiles`, and `SelectSingleCentroidHead`), the conflict check only tested `InsideExpandedBox(..., 0.0)` for structural members, skipping `box.ClearanceFt` and completely omitting the **NFPA 13 Chapter 20 "Three Times" Rule** ($3 \times \text{dimension}$, max 24 in) applied to columns and structural members.
- Consequently, points located within 2 ft of structural columns/beams had `HasObstacleConflict = false` and were never skipped when clicking "No".

### Implementation
1. **Centralized `CheckPointObstacleConflict` in `BruteForceCalculationService.cs`**:
   - Created a unified helper method evaluating:
     - Direct obstacle volume penetration (`InsideExpandedBox(..., 0.0)`).
     - Flat clearance for non-structural obstacles (`!box.IsStructuralMember && InsideExpandedBox(..., box.ClearanceFt)`).
     - Structural member clearance under NFPA 13 Chapter 20 Three-Times rule (`box.IsStructuralMember && IsThreeTimesRuleCategory(box.Category)` with required distance $\min(3 \times \text{dim}, 2.0\text{ ft})$).
2. **Applied consistently across all layout branches**:
   - `SelectSingleCentroidHead`
   - `SelectCenteredGrid` (pure grid placement)
   - `SelectCenteredGridOnTiles` (tile-centric placement)
3. **Unit Testing**:
   - Added `TestObstacleConflictFlaggingOnStructuralMember` in `Nfpa13EnhancementsTests.cs` using the exact Kitchen R-01 column dimensions and coordinates.
   - All 20 test suites pass.

---

## Pure Grid & Continuous Baseline Rewrite (2026-11-03)

### Context & User Directive
- User strictly requested removal of all obstacle avoidance/shifting/recovery logic for both sidewall and ceiling-hosted/pendent sprinklers.
- Candidate points must remain on pure calculated grid/wall locations.
- If a candidate point intersects an obstacle bounding box, it is flagged with `HasObstacleConflict = true`.
- When user clicks **Yes** to place regardless of obstacles, all sprinklers are placed at their pure calculated locations (even on obstacles).
- When user clicks **No**, only candidate points with `HasObstacleConflict == true` are skipped, while all non-conflicting points on the same wall or grid are placed as calculated.
- In sidewalls with architectural cutouts/indents (e.g. column jogs along the North wall of Kitchen R-01), the continuous wall baseline is synthesized so both opposing walls receive full, balanced candidate rows (e.g., 4 North, 4 South = 8 heads total), rather than fragmenting into 1 head on North and 4 on South.

### Implementation
1. **Removed Shifting & Recovery from `SelectCenteredGrid`**:
   - Replaced candidate snap search and obstacle recovery search with pure grid evaluation $(t_x, t_y)$.
   - Added direct obstacle collision testing to set `HasObstacleConflict = true` when intersecting obstacle bounding boxes.
2. **Removed Obstacle Recovery from `SelectCenteredGridOnTiles`**:
   - Eliminated fallback radius search that shifted tiles when obstacles were encountered.
   - Tagged `HasObstacleConflict = true` directly on tile target.
3. **Continuous Baseline Synthesis in `CleanPolygonLoop`**:
   - Added `SimplifyArchitecturalCutouts` to bridge small indents (&lt; 2.5 ft deep) along collinear baseline planes.
   - Produces balanced opposing walls for sidewall calculations.
4. **Verification**:
   - All 19 test suites passing (`Nfpa13RulebookRulesTests`, `CenteredGridPlacementTests`, `BruteForceOverrideTests`, etc.).
   - Successful build for both Revit 2025 and Revit 2026.

---

## Fix: Wall Backing Validation for Sidewall Sprinkler Placement (2026-11-02)

### Context & Defect
- In spaces with open boundaries, openings, or room separation lines (e.g. Kitchen boundary facing the hallway/living area), `SelectSidewallDirectional` treated every room boundary edge as a mountable wall.
- During Revit placement, heads generated on non-wall edges failed with `Wall placement failed near edge X` because no wall face or hosting surface existed.

### Implementation
1. **`BoundarySegmentData` metadata**:
   - Added `ElementId` and `IsWall` flag to `BoundarySegmentData` in `FireProtection.Backend.Models.DTOs.BoundaryData`.
2. **`RoomExtractor` segment inspection**:
   - During extraction of room boundary loops, each boundary segment's source element is resolved via `segment.ElementId`.
   - Tags `IsWall = true` only when the underlying Revit element is an actual `Wall` (`OST_Walls`).
3. **`SelectSidewallDirectional` pre-filter**:
   - In `BruteForceCalculationService.cs`, edges that are non-wall boundaries (such as room separation lines or virtual openings) are skipped during candidate row generation.
   - Prevents calculating phantom sidewall heads on open air and avoids mid-transaction placement failures.

---

## Documentation: sprinkler explainer rewrite + 5-slide highlights deck (2026-11-02)

No product code changed. Both deliverables are static-trace documentation; live Revit behavior remains unverified.

### SPRINKLER_POINT_CALCULATION_EXPLAINED.md (rewritten; v2 logic-only supersedes the first pass)
- v2 same-day replacement after user feedback (first rewrite still narrated process/UI; user demanded
  "logic only, no any other bullshit than the logic"). v2 (~250 lines) replaces the file entirely with the
  math pipeline, every formula re-verified verbatim against source: point-count math
  (ceiling: ceil(A / (pi x r^2)) at `BruteForceCalculationService.cs:1448`; sidewall:
  ceil(A / (S_al x throwDepth)) at :1797), candidate creation (ComputeGridResolution caps; the only three
  TryAcceptPoint rejects = outside room / below max(BoundaryClearance, 4 in) - tol / within existing-head
  separation, everything else tracked-not-rejected), selection solvers (free grid nx/ny/captureRadius
  formulas verbatim at :604-756; tile X-2X-X balanced solver with avgGap/symmetry-start math from
  TileCentricPatternGenerator; sidewall throwDepth/endWallMax=0.5S/standOff=0.5/greedy rows), Z/deflector/
  construction/small-room logic, finalize re-checks (nearest-neighbour spacing, wall distance, S x L with
  exact S=max(along,2xwall)/L=max(across,2xwall) formulas from CheckCoverageBySxL:2179-2263, 0.7S
  remote-distance advisory confirmed enforced at :1374), one compact fully-verified worked example
  (30x20 ft Light -> 4 heads), and a declared-but-NOT-enforced constants list (0.75 corner rule, slope
  bands, 22/12 in pocket drops, 70% open-cross-section, etc. so the doc never claims they are enforced).
  Source-line map included at the end.
- First-pass rewrite (superseded by v2) of the veteran explainer from current sources only (`BruteForceCalculationService`,
  `DefaultHazardPlacementRules`, `Nfpa13RulebookRules`, `HazardPlacementRuleSet`,
  `PlacementInputJsonExporter`, `PlacementEligibilityResult`, `HazardClassifier`,
  `SprinklerBruteForceViewModel`), structured around the engineering process: command flow, user
  inputs, extracted model data, governing rules, position logic, outcome statuses, placement
  transaction, exports.
- Corrections over the old document, verified in code: candidate-stage obstacle checks are
  **tracked, not rejected** (only outside-ceiling / boundary-clearance / existing-separation
  reject); required head count is room area / (pi x coverage-radius squared), not rows x columns;
  the ceiling-selection band limit is floor + ceiling height + 5 ft; the nearest-neighbour spacing
  check names the worst head; tile-centric -> centred-grid -> sidewall flows and the finalize
  re-checks are documented as implemented.
- All spacing/clearance values flagged provisional (NFSA rulebook basis, NFPA 13 2002 edition;
  `DefaultHazardPlacementRules.HasApprovedRules = false`).

### Sprinkler_Layout_Highlights.pptx (7 slides, project root)
- Brand-matched summary deck (SPA navy/paper/brass; Segoe UI body, Nasalization accents - Poppins
  and Figtree are not installed on this machine): Title -> What the tool does -> Your inputs ->
  Governing rules (hazard table) -> Where the heads go + one-line provisional-review disclaimer.
- Slides 06-07 (added from a session that verified the math against the live HOUSE_MODEL run via
  Revit MCP): "Worked example: KITCHEN R-01" (35.66 x 14.94 ft, OH1, 15 ft x 0.9 = 13.5 ft spacing,
  3 x 2 = 6 heads, amber callout on why 6 beats the area estimate
  ceil(532.87/130) = 5) and "From count to coordinates" (cell size 11.89 x 7.47 ft, centred-point
  formula, first/last head read back from Revit, COM-drawn room plan with the 6 placed heads, navy
  wall-band/min-spacing/obstacle disclaimer bar). Footers renumber automatically (01/07 .. 07/07).
  Pre-change backup kept as `Sprinkler_Layout_Highlights_backup_5slides.pptx`.
- User-enforced hard floor: every visible text run >= 20 pt (footers, pills, table cells included);
  the build script scans all slide text and notes for < 20 pt runs and forbidden phrases and both
  come out clean.
- Built with COM scripts in `%TEMP%\opencode\deck-build` (`highlights-lib.ps1`, `highlights-slides.ps1`,
  `highlights-build.ps1`) from template `SPA Structural Samples.pptx`; pixel-verified against PNG
  exports (`hi-slides\slide-01..07.png`, contact sheet `hi-contact-sheet.png`).

---

## Sprinkler Tab Wizard Navigation & Whitespace Optimization (2026-10-23)

### 1. Unified Preliminary & Final Sub-Tab Navigation
- **Sub-Tab Synchronization**: Clicking **Next >** on the Preliminary view directly transitions to the **Final** tab. Clicking the **Final** tab directly immediately shows the Review & Placement summary table.
- **Previous Navigation**: Clicking **< Previous** or **Cancel** on the Final review view navigates back to the **Preliminary** tab with all selections preserved.
- **Dedicated Final Review View**: `SprinklerCollisionView.xaml` now hosts the Final Review summary table, sprinkler configuration, and bottom action bar (`< Previous`, `Cancel`, `Apply`) sharing state seamlessly with `SprinklerBruteForceViewModel`.

### 2. Elimination of Excess Whitespace & Compact Sizing
- **Top Sprinkler Configuration Card**: Reduced padding from `18,14` to `12,8`, tightened margins, and reduced combobox widths from `300` to `260`.
- **Compact Catalog Notice**: Collapsed redundant empty vertical spacing under the configuration comboboxes; missing catalog messages only display when active in a clean, compact font.
- **Window Padding & Scaling**: Reduced outer margins in `MainWindow.xaml` and `SprinklerView.xaml` (`Margin="10,4,10,8"`), header icon to `32x32`, and title text to `15px` to provide maximum vertical space for the level and room grids.
- **Cleaned Up Unused Files**: Removed orphaned/unused draft and script files (`DeviceOwnershipStamp.cs`, `CoverageAreaMath.cs`, `Nfpa13BeamRuleTable.cs`, `CeilingFaceSelector.cs`, `NumericInputBehavior.cs`, `ObstacleCategoryOptions.cs`, `ObstacleCategoryItemViewModel.cs`, etc.).

---

## Simplified Obstacle Handling & Sprinkler Review Wizard (2026-10-23)

### 1. Obstacle Handling Simplification
- **Obstacle Conflict Handling Option**:
  - When user clicks **Apply** on the Final page, a prompt asks: *"Place sprinklers at calculated locations regardless of detected obstacles?"*
  - **Yes**: Places all sprinklers at candidate locations regardless of obstacle conflicts (`ignoreObstacles = true`).
  - **No**: Skips those candidate points that conflict with obstacles (`HasObstacleConflict == true`), logging them under `PlacementStatusCodes.ObstacleConflict` while placing all other valid points (`ignoreObstacles = false`).
- **Extraction & Identification Retained**: Revit obstacle extraction (beams, ducts, columns, etc.) and obstacle identification remain fully active. Intersections/clashes are tracked in candidate points and diagnostics.
- **Existing Sprinklers**: Collision policy selection (Skip / Replace / Add) remains governed by user choice in the dropdown.

### 2. Sprinkler Wizard Flow
- **Step 1 (Preliminary)**: Configuration of levels, rooms, hazard classes, and overrides. Bottom bar features a **"Next >"** button instead of direct placement.
- **Step 2 (Final Review)**:
  - Displays a clean review table containing all selected rooms, levels, hazard classes, sprinkler family & type, and active S->S / WALL overrides.
  - Bottom bar layout:
    - Bottom-left: **"< Previous"** button (returns to Preliminary with settings intact).
    - Bottom-right: **"Cancel"** and **"Apply"** (initiates placement with confirmation dialog).

### 3. Responsive Compact UI
- `MainWindow.xaml`: Reduced `MinWidth` from 1040 to 820, `MinHeight` from 620 to 500, default size 1200x760 to ensure visibility on laptops and scaled displays without clipping.
- `SprinklerBruteForceView.xaml`: Optimized grid column widths, margins, and paddings for clean rendering on smaller screens.

---

## Catalog source switch: Model vs Catalog file (2026-10-07)

Static only. The Revit-document reading half is **UNVERIFIED at runtime**.

### What the user gets

A **SOURCE** radio group (Model | Catalog file) in the top bar, replacing the browse-only Catalog bar.
One group covers all three device tabs because the bar is hosted once at window level, outside the
`TabControl`.

| Mode | Sprinkler lists | Smoke / NA lists | Browse… | Reload |
|---|---|---|---|---|
| **Model** (default, new) | from `OST_Sprinklers` | from `OST_FireAlarmDevices`, **identical on both tabs** | hidden | re-reads the document |
| Catalog file (existing) | from workbook | from workbook, per sheet | shown | re-reads the workbook |

A single `InfoBanner` under the bar names the active source and states whether per-type values are
provisional.

### The design problem this had to solve

The two sources are **complementary, not interchangeable**:

- **Model** → what is actually loadable, plus `FamilyPlacementType` (authoritative for hosting).
  Carries **no** engineering values.
- **Workbook** → every per-type engineering value (hazard class, coverage area, spacing, coverage
  radius, K-factor, temperature, deflector, sidewall values, and the alarm attributes). Carries
  **no** placement-type information.

A naive switch was unsafe both ways. Blanking the workbook made `GetSprinklerEntry` return null, which
**silently** zeroed `TypeMaxSpacingFt` / `TypeMaxCoverageAreaSqFt` / `TypeMinSpacingFt` /
`TypeCoverageRadiusFt` / `SprinklerClass` on every room, and the engine then quietly used provisional
15 ft spacing with nothing shown to the user. Discarding the workbook lost the engineering data
entirely.

**Resolution:** `ModelBackedCatalog` takes **NAMES from the model** and **VALUES from the workbook**,
the workbook acting as a live overlay. `ISprinklerFamilySource` keeps only a last-resort fallback role.

### Files

- `Backend/Services/Catalog/ModelBackedCatalog.cs` — `ICatalog`: model lists + workbook overlay
- `Backend/Services/Catalog/ModelFamilyEnumerator.cs` — reads loaded `FamilySymbol`s per category
- `Backend/Services/Catalog/ModelFamilyType.cs` — Revit-free record (own file, deliberately)
- `Backend/Services/Catalog/DeviceCatalogOverlay.cs` — **cross-sheet** alarm lookups
- `UI/Services/CatalogSourceMode.cs`, `UI/Services/CatalogSettingsStore.cs`
- `UI/Converters/EnumToBoolConverter.cs`, `UI/Themes/Styles.xaml` (first RadioButton style)
- `UI/Views/Catalog/CatalogBar.xaml(.cs)`, `UI/Views/MainWindow.xaml`, `CatalogViewModel.cs`
- `Backend/Commands/FireProtectionCommand.cs` — two-holder wiring

### Decision 2 — shared device list, and the trap it created

Per the product decision, the Smoke Detectors and Notification Appliances tabs show the **same**
device list (both come from the single Revit category `OST_FireAlarmDevices`).

That exposed a real gap: the workbook stores alarms in **two separate sheets**, and every sheet-scoped
lookup searched only its own. A strobe catalogued under `NotificationAppliances` would appear on the
Smoke tab with **no** attributes, blanking the derived read-only fields and re-exposing **empty
editable pickers** — worse than today's read-only text. `DeviceCatalogOverlay` now searches **both**
sheets for every alarm-device lookup, keeping the shared list coherent. Attributes the other sheet
lacks are returned as `null` / `0` rather than guessed (`0` already means "no rating known"
downstream). This is the single most important behaviour to preserve here.

### Defects found and fixed along the way

- **Two holders, not one.** The model catalog must never read the shared `CatalogHolder`: in model
  mode it would find *itself* as its own overlay and recurse indefinitely
  (`GetSprinklerEntry` → `Overlay` → itself). A dedicated `workbookHolder` holds **only** the
  workbook, making that impossible by construction rather than by a fragile runtime guard.
- **`FireProtectionConfig.UseRevitFamilyListing` removed.** That gate is why the model listing looked
  dead. Two independent switches for one concept is how the earlier catalog/model inconsistency
  arose, so the flag was retired rather than kept alongside the radio.
- **Test-harness boundary.** `ModelBackedCatalog` contains **no** `Autodesk.Revit.DB` reference so the
  Revit-free harness can exercise it; the document read lives in `ModelFamilyEnumerator.LoadInto`.
- Entering Catalog-file mode with no remembered workbook now **clears** the catalog rather than
  leaving the previous source in place, which would have shown model families while the radio claimed
  "Catalog file".

### Verification

New suite `CatalogSourceModeTests` (12 tests / 53 checks): both-sheet lookups in both directions,
overlay precedence, shared-list equality, no-overlay behaviour, settings round-trip, corrupt-settings
fallback, mode switching, and the provisional-values banner.

**388 checks, 17 suites, green on Revit2024 / 2025 / 2026; 0 build errors on all three.**

## Sprinkler Obstacle Avoidance Recovery & Nearest-Point Relocation (2026-10-07)

**Industry-grade placement logic enhancement:** Fixed silent dropping of sprinkler heads when ideal grid cell targets hit physical obstructions or structural members, ensuring sprinklers snap to the nearest valid candidate position up to `MaxSpacing / 2.0` (the mathematical maximum preserving NFPA 13 spacing limits).

### Fixed Issues

1. **`IsPlacementValid` Alignment with `TryAcceptPoint`**:
   - `IsPlacementValid` previously lacked the **Three Times Rule** (NFPA 13 Ch.20), **Beam Rule** footprint rejection, and the **4 in wall clearance floor**.
   - As a result, grid-ideal positions directly adjacent to columns or beams passed `IsPlacementValid` without any snap, violating NFPA 13 clearance.
   - `IsPlacementValid` now evaluates the complete set of structural member rules and wall clearance constraints.

2. **Free-Grid Obstacle-Recovery Expanded Search**:
   - When a physical obstacle (column, beam, duct, wall) blocks an ideal grid target and the initial tight `captureRadius` snap finds no candidate, the algorithm now expands its recovery search up to `spacing / 2.0`.
   - `spacing / 2.0` is the theoretical maximum displacement that maintains NFPA 13 `MaxSpacingFt` compliance (moving a head by <= S/2 keeps its gap to the adjacent array position <= 1.5S, which is then flagged by `FinalizeSelection`'s nearest-neighbour check).
   - If an existing sprinkler occupies a target grid cell, recovery snap is bypassed for that cell (since the existing sprinkler already satisfies the grid position).

3. **Tile-Grid Obstacle-Recovery Expanded Search**:
   - For suspended tile ceilings, when an 8-immediate-neighbour snap budget fails to find a clear tile, the search expands up to `spacing / 2.0` across the tile grid (`extRange = max(tileStepU, tileStepV) + 1` tiles).
   - `placedObstacleRecovered` is tracked and reported separately in diagnostic outputs to distinguish 8-neighbour tile snaps from expanded recovery snaps.

### Verification

- Build clean on **Revit2026 / Revit2025 / Revit2024** (0 errors).
- All **16 test suites** (over 335 test checks) pass green.

## Rulebook-driven sprinkler rules (2026-10-07)

**Design basis decision (from the project owner):** the two supplied rulebooks are the declared design
basis for this tool, and the tool is to be developed against them. They are old editions (NFPA 13 2002
via the NFSA textbook; NFPA 72 2019 for detectors/appliances) and that is accepted — but every rule must
be traceable to them, and nothing may be invented.

Scope of this pass: **sprinklers only.**

### Where the rules now live

`FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/Nfpa13RulebookRules.cs` — every
value transcribed from the rulebook, each carrying the chapter, page and a verbatim quote. The calc
engine references these constants; it holds no bare engineering numbers of its own.

### Rules implemented

| Rule | Rulebook source | Notes |
|---|---|---|
| Min spacing 6 ft (cold soldering) | Ch.19 p.218 | already correct; now sourced to the constant |
| Max spacing 15 / 12 ft | Ch.19 p.218 | already correct |
| Max wall distance = ½ S | Ch.19 p.219 | already correct |
| **Min wall distance 4 in** | Ch.19 p.220 | **new.** Enforced as a floor — a room or rule set can never go below it |
| **Non-90° corner limit = 0.75 S** | Ch.19 p.220 | value recorded; corner detection is polygon-shape dependent and still advisory |
| **Deflector drop 1–12 in unobstructed** | Ch.19 p.225 | **new and previously a real defect.** Heads sat exactly ON the ceiling plane (zero drop), violating the 1 in minimum. Now applied |
| **Deflector drop 1–6 in obstructed** | Ch.19 p.225-226 | **new.** Band selected by construction class |
| **Unobstructed / obstructed classification** | NFPA 13 3.7.2 via Ch.19 p.216 | **new.** Members > 7.5 ft o.c. = unobstructed regardless of solidity; < 3 ft = necessarily obstructed; between 3 and 7.5 ft falls to the **conservative OBSTRUCTED** branch because the >70 % open-cross-section test cannot be measured from extracted AABBs |
| **Sloped peak rule (3 ft)** | Ch.19 p.229 | **new.** Highest head must be within 3 ft of the peak; flagged when not |
| **S × L coverage rule** | Ch.19 p.222 | **new.** Per-head coverage is now `S × L` where each dimension is the greater of the neighbour distance and **twice** the wall distance. Replaced a room-level `area / headCount` estimate that could not see a head overloaded next to a wall |
| **Small Room Rule** | Ch.19 p.223-224 | **new.** Light hazard + < 800 ft² + unobstructed + enclosed → wall limit relaxed to 9 ft **and** coverage switches to the averaging technique the rulebook requires. Never loosens a limit already tighter than S/2 |
| **Ch.20 zone plane (18 in)** | Ch.20 p.246 | **new.** Splits the room into discharge-development and below-sprinkler zones |
| **"Three Times" rule** | Ch.20 p.248 | **new.** ≥ 3 × the obstruction's **maximum** dimension from its near edge, capped at 24 in, measured to the deflector centreline. Includes the opposing-sprinkler exception (within 0.5 S of the centreline) |
| **Permanent fixture > 4 ft wide** | Ch.20 p.249 | **new.** Reported for obstructions below the 18 in plane; the tool cannot place a head *under* a fixture, so this is raised for manual layout |

### Deliberately NOT implemented — and why

`Nfpa13RulebookRules.BeamRuleTableAvailable = false` plus `Nfpa13RulebookRules.KnownGaps` record these
so they cannot quietly become invented numbers later:

- **Beam rule (NFPA 13 Table 8.6.5.1.2).** The rulebook *references* this table but does not reproduce
  it — Ch.20 p.246 only says "See Table 8.6.5.1.2 and Figure 8.6.5.1.2(a) of NFPA 13 for more
  information on exactly how far away sprinklers need to be". The clear distance depends on the distance
  from the obstruction bottom to the deflector, and those numbers are simply absent from the design
  basis. **No values were invented.** Heads are rejected only when inside a member's own footprint, and
  such rooms are flagged.
- Sprinklers in every beam pocket (Ch.19 p.226) and the concrete-tee exception — need per-member pocket
  geometry the AABB model cannot represent.
- Composite wood joist firestopping (Ch.19 p.228) and concealed-space/attic rules (Ch.19 p.230) —
  extraction does not identify these assemblies.
- High-piled storage and rack storage commodity tables (Ch.37, Ch.38).
- Extended-coverage and ESFR geometry (Ch.21) — ESFR remains a review-only flag.
- Density/area hydraulics — out of scope for a placement tool.

### Defects found and fixed along the way

- **Zero deflector drop.** Heads were placed exactly on the mounting plane. Ch.19 requires 1–12 in.
  This changed every room's Z, so four existing tests asserting "Z == ceiling elevation" were
  **correctly** failing afterwards and were updated to assert ceiling-minus-one-drop.
- **The Small Room Rule initially loosened caller-supplied limits.** Relaxing a wall limit the rule set
  had deliberately set *tighter* than S/2 is wrong; caught by an existing test that pinned a 1.0 ft
  limit. Now the relaxation only applies when the caller is actually relying on the standard S/2 limit.
- **Per-category obstacle clearances never matched.** `GetObstacleClearance` was keyed on
  `"beam"/"column"/"duct"` while the extractor emits Revit categories
  (`OST_StructuralFraming`, `OST_StructuralColumns`, `OST_DuctCurves`, …), so every lookup silently
  fell through to the flat default. Structural members now use the Chapter 20 dimension rule instead of
  a fabricated flat clearance.

### Verification

New suite `Nfpa13RulebookRulesTests` (16 tests / 59 checks) covering every rule above, including an
explicit test that the Beam rule table remains declared unavailable.

**335 checks, 16 suites, all green on Revit2024 / 2025 / 2026; 0 build errors on all three.**

All static. Runtime Revit placement remains **UNVERIFIED**.

## Sprinkler point-identification corrections + test safety net (2026-10-07)

Static only — **runtime Revit placement remains UNVERIFIED**. See "Phase 3 — live Revit verification" below.

### Test safety net (was silently broken)

- `FireProtection.Tests` is now in `FireProtectionSystem.slnx`. Previously the solution contained
  only Backend + UI, so a plain `dotnet build` compiled **no tests at all** and nothing gated a change.
- `FireProtection.Tests.csproj` gained the same `Configurations`/per-config TFM matrix as Backend and
  UI (Revit2024 → `net48`, Revit2025/2026 → `net8.0-windows`), because it references Backend whose TFM
  follows the Revit configuration.
- **Pre-existing solution bug fixed:** `.slnx` mapped `Revit2024|Any CPU` → `Debug` for the UI project,
  rebuilding UI as `net8.0-windows` while Backend built `net48` in the same build. That combination is
  an unsatisfiable project reference, so `dotnet build -c Revit2024` failed outright on the solution
  while each project built correctly on its own. The obsolete mapping was removed.
- Three complete suites existed but were **never registered** in `Program.RunAll()` and so never ran
  while the harness printed `ALL TESTS PASSED`: `HazardRuleValuesTests` (the entire hazard-spacing
  regression lock), `SidewallDirectionalSolverTests` (7 tests, NFPA 13 §11.3 directional sidewall
  solver), `PerTypeCatalogMergeTests` (5 tests, catalog-override merge + ESFR/CMSA review flag). All
  three are now registered and pass.
- `CatalogLoaderTests` was invoked directly rather than through `RunGuarded`; it signals failure by
  throwing, so one failure aborted the whole run. Now guarded like every other suite.
- New `Program.ReportSuiteCoverage()` discovers every `*Tests.cs` declaring `RunAll` and **fails loud**
  if any is not in `_registeredSuites`. Verified negatively: removing a registration makes the run
  exit non-zero with `MISSING : <name>`.
- Fixed 2 genuinely failing tests (`Program.cs` unsupported-behaviour and hosted-family/no-ceiling).
  Root cause: the engine had moved blocking messages from `Diagnostics` to `Errors`; the tests still
  scanned `Diagnostics`. Tests now assert the real contract via `HasMessageContaining`, which searches
  every user-facing channel — moving a message between channels is presentation, not behaviour.
- Baseline: **276 checks, 15 suites, all green on Revit2024/2025/2026; 0 build errors on all three.**

### Point-identification corrections (sprinkler calc engine)

- **Bounded tile snapping.** `SelectCenteredGridOnTiles` searched ±`tileStep` tiles in *both* axes with
  **no distance limit** and took the nearest valid tile, so a head whose array target was blocked could
  be dragged arbitrarily far, producing a pair beyond `MaxSpacingFt`. The placer was relying on the
  post-hoc pairwise checker to report the placer's own violation. The search is now limited to the 8
  immediate neighbours AND bounded by the slack between the array pitch and the permitted spacing
  (capped at half a tile). A cell with no in-budget neighbour is **skipped and reported**, never dragged.
- **Array extent.** `tileStep` is a `Floor` of spacing/pitch, so a centred array left the room ends
  uncovered whenever `(tiles-1)` was not a multiple of the step (4 ft tile + 15 ft spacing → 12 ft step).
  The uncovered tail is what made the unbounded search fire in practice. The array now covers the
  extent and appends the far-edge index when the stride cannot reach it.
- **Coverage radius corrected.** `CoverageRadiusFt` was `MaxSpacingFt / 2` for every hazard class, which
  is valid only along an array EDGE. The true worst case for an array is `S / sqrt(2)` ≈ `0.707 × S`
  at a corner/array centre. Added `HazardPlacementRuleSet.EffectiveCoverageRadiusFt` (= `S/sqrt2`) and
  `RemoteDistanceLimitFt` (= `0.7 S`). `CoverageRadiusFt` is retained as the head's *listing* radius;
  coverage *radius* and coverage *area* (`MaxCoverageAreaSqFt`) are now separate quantities.
- **Coverage is advisory only** (product decision). The previous `if (pct > 5.0)` escalation is removed —
  it reported a gap with no location and no action, training users to ignore it. Now: one concise warning
  with the worst-gap point, plus a diagnostic carrying the 0.7 S limit and the listing radius. It never
  changes room status. Sidewall rooms skip the 2D sampler entirely (a half-disc spray cannot satisfy a
  full-room radius test) with an explicit "skipped" diagnostic rather than a false gap.
- **Max-spacing check was a false positive on virtually every room.** `FinalizeSelection` compared
  **every pair** of heads against `MaxSpacingFt`. Diagonally adjacent heads in an array are always
  further apart than the pitch (15 × 15 ft array → 21.2 ft diagonal against a 15 ft limit), so any room
  with a 2×2-or-larger array emitted a spurious "exceeding MaxSpacingFt" warning and was flagged
  `ReviewRequired` regardless of quality. Replaced with a per-head **nearest-neighbour** test, which is
  what max spacing actually governs and which still catches a dragged or orphaned head.
- **Single owner for grid maths.** `SelectCenteredGridOnTiles` carried local `WorldToIndex` /
  `IndexToWorldX/Y` duplicating `CeilingGridMath.SnapToTileCenter` — they had already drifted once (the
  `// Re-added +0.5 shift` comment). Deleted the duplicates; `CeilingGridMath` now exposes
  `WorldToIndex`, `IndexToWorld`, `IndexKey`, `IndexDistance`.
- **Trap avoided and documented:** caching `cos/sin` in the `CeilingGrid` struct created an invalid
  state — the struct has public settable fields, so a struct initializer left the cached rotation at
  (0,0) and produced a snapped point on a tile *corner*. Caught by the existing `CeilingGridSnapTests`.
  `AngleRad` is now the single source of truth and cannot go stale.
- **Split skip counters.** "too close to another new head" and "too close to a pre-existing sprinkler"
  were merged into one `skippedMin` bucket, telling the user nothing actionable. Now reported
  separately as `noValidNeighbour` / `minSpacing` / `existingSprinkler`.
- **Mounting-plane (Z) selection.** `SelectPrimaryCeiling`'s fallback ranked by **highest** bottom
  elevation, so in a stacked building a room with no level-tagged ceiling selected the ceiling of the
  level **above** — an ~12 ft Z error across every head in the room. Now ranks: own level first, then
  FLAT before SLOPED/STEPPED, then **closest to the room's nominal ceiling height**. Candidates more
  than 5 ft above nominal are excluded as soffit/plenum/slab, and a non-level-matched or
  height-mismatched pick raises an explicit warning instead of silently substituting.
- Dead code removed: `CoveragePatternAdjustments` + `GetCoveragePatternAdjustment` had **zero callers**
  repo-wide — an unvalidated-looking table a future reader could mistake for an implemented rule.
  `MinKFactor` is documented as **not enforced** (it is a hydraulic-stage property; this tool performs
  no hydraulics) rather than being silently present.
- New regression tests: unobstructed grid never exceeds `MaxSpacingFt`; obstructed room serves both
  sides and **reports** the resulting gap rather than hiding it; blocked target with no near neighbour
  is skipped, not dragged; array reaches room extent with a coarse 4 ft tile; level-above slab is not
  used as the mounting plane; own-level ceiling preferred; out-of-band plenum deck rejected + reported.

### Not done in this pass (deliberate)

- **Spatial index for candidate search (P1F).** The free-grid nearest-candidate search remains
  O(cells × candidates). Whole suite runs in 2.6 s, so there is no evidence of a problem at test scale,
  and the fix cannot be validated without a real large linked model. Deliberately deferred rather than
  optimised blind.
- `FireProtection.CatalogStandalone` still duplicates 26 sources via `<Compile Include>` and is not in
  the solution.

### Phase 3 — live Revit verification (OUTSTANDING, requires the user)

Nothing below is proven. The historical (0,0,0) origin-snap defect has never been re-verified.

1. Open a host model + architectural link with a real ceiling family.
2. Place into 2–3 rooms, including one with beams/ducts and one with a suspended tile ceiling.
3. Confirm placed `instance.Location` ≈ calculated X/Y/Z for every head; **none at (0,0,0)**.
4. Confirm Z is on the intended ceiling (not the slab above).
5. Read the run report: reason codes, coverage advisory, bounded-snap skip counts.
6. Re-run to confirm the duplicate guard behaves; remove any stray misplaced instances from prior runs.

## ELI5 location-point explainer (sim_expl_2.md) — written (2026-09-22)

- Documentation only (no code change): `sim_expl_2.md` in the project root is a plain-language
  (ELI5) companion covering location-point logic only for sprinklers, smoke detectors, and
  notification appliances — rules → grid → filters → selection → review flags — plus a side-by-side
  comparison table.
- Companion to the more technical `explanation_2.md`; both flag provisional rules
  (`HasApprovedRules=false`). See [[SESSION_NOTES]] (2026-09-22).

## Candidate-location explainer (explanation_2.md) — written (2026-09-22)

- Documentation only (no code change): `explanation_2.md` in the project root documents candidate-point
  identification for sprinklers, smoke detectors, and notification appliances (plain-English then technical:
  rules, ceiling/Z, generation, filters, selection, post-checks, family/type coverage, edge cases, class &
  method index).
- Flags provisional rule sets (`HasApprovedRules=false`), static-only status, and 12 known doc-vs-code
  discrepancies (code is source of truth). See `explanation_2.md` §9 and [[SESSION_NOTES]] (2026-09-22).

## Catalog-driven sprinkler rules — implemented, static verification (2026-09-21)

- Implemented: optional per-type sprinkler catalog fields, warning-only validation, catalog accessors,
  template examples, a read-only selected-type card, per-room orientation, and ceiling-type display.
- Implemented: listed type values travel through the UI/export contract to the Revit-free engine,
  merge before per-room overrides, and remain bounded by the hazard-class spacing ceiling.
- Implemented: ESFR/CMSA and unlisted storage coverage are ReviewRequired; no structural rule,
  K-factor, pressure, or minimum-head requirement is fabricated.
- Static verification: Revit2026 test-project build completed with 0 errors and the console harness
  reported `ALL TESTS PASSED`. Runtime Revit verification remains outstanding.

> Status legend:
> ✅ Implemented (static) — code exists and compiles; runtime not necessarily verified.
> ✅ Implemented & runtime-verified — confirmed against a live Revit model.
> 🟡 Partial / shell — UI or stubs present, logic not complete.
> 🔴 Not implemented — only referenced or absent.
> ❓ Uncertain — cannot confirm from repository.
>
> "Static" means provable by reading code. "Runtime-verified" requires Revit + an open model.
>
> ⚠️ **PARTIALLY STALE (2026-08-25):** any "In Progress" note describing the placement fix as
> `isFaceBased`/`WorkPlaneBased` detection with a Level *fallback* reflects the **superseded Decision 010**.
> The code now implements **Decision 011** (placement-strategy pattern; post-placement validation; no
> WorkPlaneBased→Level fallback). **Current truth: `PROJECT_MEMORY.md` + `DECISIONS.md` 011.**

## Legend for columns
- **Status**: one of the symbols above.
- **Verification**: `static` (code), `runtime` (run in Revit), `none`.

## Extraction (Phase 1)

| Capability | Status | Verification | Notes |
|---|---|---|---|
| Level extraction (host + links) | ✅ Implemented | static | `LevelExtractor`; elevations normalized. |
| Room extraction (loops, boundaries) | ✅ Implemented | static | `RoomExtractor`; outer/inner loops, curved boundaries. |
| Ceiling extraction & slope classification | ✅ Implemented | static | `CeilingExtractor`; FLAT/SLOPED/STEPPED/NONE. |
| Obstacle extraction | ✅ Implemented | static | `ObstacleExtractor`; columns/beams/walls/MEP curves as AABB. |
| Existing sprinkler extraction | ✅ Implemented | static | `ExistingSprinklerExtractor`. |
| Hazard classification (tagging) | ✅ Implemented | static | `HazardClassifier.ClassifyByName`; tagging only. |
| Linked-model coordinate normalization | ✅ Implemented | static | `RevitModelContext` transforms. |
| Model validation | ✅ Implemented | static | `ModelExtractionValidator`. |
| JSON snapshot export | ✅ Implemented | static | `JsonSnapshotExporter`. |
| Per-room error isolation | ✅ Implemented | static | warnings, non-aborting. |
| Read-only / no-transaction extraction | ✅ Implemented | static | Decision 005. |

## Sprinkler Placement (Phase 2 — BruteForce)

| Capability | Status | Verification | Notes |
|---|---|---|---|
| Placement input builder (in-memory) | ✅ Implemented | static | `PlacementInputBuilder.Build`. |
| BruteForce calculation engine | ✅ Implemented | static | `BruteForceCalculationService`; 1 ft grid, 15 ft greedy select. |
| X/Y grid + greedy selection | ✅ Implemented | static | See [[SPRINKLER_POINT_CALCULATION_EXPLAINED]]. |
| Z resolution (ceiling/level) | ✅ Implemented | static | fallback to level+height when ceiling missing/sloped. |
| Obstacle & boundary clearance | ✅ Implemented | static | 1 ft placeholder clearances. |
| Placement input JSON export | ✅ Implemented | static | `PlacementInputJsonExporter.ExportInput`. |
| Placement result JSON export | ✅ Implemented | static | `ExportPlacementResult`. |
| Revit `FamilyInstance` placement | ✅ Implemented | static | `RevitSprinklerPlacementService.PlaceSprinklers`. |
| Linked-level resolution | ✅ Implemented | static | `ResolveHostLevel`. |
| Ceiling-host detection for placement | ✅ Implemented | static | `CeilingHostResolver.FindCeilingHost` searches host + linked ceilings, returns a downward-face `Reference` (link refs via `CreateLinkReference`). **Decision 011:** `WorkPlaneBased` uses that ceiling face, else a `SketchPlane` honoring world Z — it **never** uses the Level overload (no WorkPlaneBased→Level fallback). ~~Earlier note claimed a Level fallback (Decision 010, superseded).~~ Runtime pending. |
| Actual host/level read-back diagnostics | ✅ Implemented | static | **Decision 012 (2026-08-25):** success path records `ActualHostElementId/Name`, `ActualInstanceLevelId/Name`, `ActualScheduleLevelName` read back from the created instance (best-effort, read-only) + `FamilyPlacementType`/`HostCeilingElementId`/`LinkInstanceName`. Diagnostics-only; no placement semantics changed. Distinguishes a hosting defect from a view-range/level-association symptom in one run. Runtime pending. |
| NFPA13-2022 compliant spacing | 🔴 Not implemented | none | provisional 15 ft only; `HasApprovedRules=false`. |
| Per-row sprinkler family/type (universal → per-room) | ✅ Implemented | static | **Decision 017 (2026-09-01).** Per-row family + type dropdowns in the room list, types filtered by family, "modified" indicator, "Reset to default" + "Apply to all eligible rows". Backed by `ICatalog` (Excel). Missing-family modal flow wired in the placement path. |
| Per-row `MaxSpacingFt` / `BoundaryClearanceFt` override | ✅ Implemented | static | **Decision 018 (2026-09-01).** Nullable fields on `PlacementRoomInput`; `BruteForceCalculationService.ApplyPerRoomOverrides` clones the rule returned by `IHazardPlacementRules.GetRules(hazardClass)` and applies the per-row overrides when present. Per-room `IsProvisional` is set when an override is in use; the preflight continues to use the un-overridden rule set so the NFPA compliance check is preserved. Out-of-range `MaxSpacingFt` is clamped to the provisional ceiling (15 ft) and a diagnostic line is recorded. Obstacle + existing-sprinkler separation NOT overridable in v1. |
| Excel-driven catalog (Sprinkler/Smoke/Notification) | ✅ Implemented | static | **Decision 020 (2026-09-01).** One workbook, one sheet per category, `CatalogVersion` header, fail-fast `CatalogLoader` (ClosedXML, Backend). User-selectable path each session + "Reload" button (no persistence). Catalog version in top bar. 8 catalog tests + 5 BruteForce override tests PASS in the headless standalone runner (`FireProtection.CatalogStandalone`). |
| Missing-in-model modal (interactive Proceed/Cancel + CSV export) | ✅ Implemented | static | **Decision 017 (2026-09-01).** `MissingFamiliesModal` shows `(Room, Family, Type)` for missing entries; "Proceed with available" / "Cancel" + "Export missing list to CSV" button. `ISprinklerPlacementService.ProbeMissingFamilies(calcResult)` reads the per-row family/type from `RoomCalculationResult` and probes the live Revit document. `SprinklerPlacementResult.SkippedMissingFamilyCount` tracks the per-row skip count; `RevitSprinklerPlacementService.PlaceSprinklers` now resolves symbols **per room** (Decision 017) and groups activations. |
| Per-level Smoke/Notification declarative metadata (popover "⋯") | ✅ Implemented | static | **Decision 019 (2026-09-01).** UI-only. `LevelSettingsPopover` ("⋯" button per level) drives `DeviceLevelItemViewModel` (smoke: DetectorType/Mount/CeilingSlope; notification: ApplianceType + composite Candela+dBA). Level value propagates as default to rooms via `DeviceRoomItemViewModel.GetOverride/SetOverride`; rooms can override. "Planning only — backend pending" badge. No algorithmic effect until device placement lands. |

## Collision Workflow

| Capability | Status | Verification | Notes |
|---|---|---|---|
| UI tab (Collision) | 🟡 Shell | static | `SprinklerCollisionViewModel` exists, empty. |
| Collision extraction/calculation/placement | 🔴 Not implemented | none | no logic observed. |

## Smoke Detector Workflow

| Capability | Status | Verification | Notes |
|---|---|---|---|
| UI tab (Smoke Detectors) | ✅ Implemented (shared base) | static | `SmokeDetectorViewModel` inherits `DevicePlacementViewModelBase`; hosts `DevicePlacementView` + NFPA-72 params (detector type / mount / ceiling slope). Placement disabled (backend deferred). |
| Extraction / placement logic | ✅ Implemented | static | Device-specific smoke-detector coverage-grid logic and rule set are now separated from sprinkler logic; live Revit verification remains pending because the backend cannot build without Revit assemblies. |

## Notification Appliance Workflow

| Capability | Status | Verification | Notes |
|---|---|---|---|
| UI tab (Notification Appliances) | ✅ Implemented (shared base) | static | `NotificationApplianceViewModel` inherits `DevicePlacementViewModelBase`; hosts `DevicePlacementView` + NFPA-72 params (appliance type / candela / dBA). Placement disabled (backend deferred). |
| Extraction / placement logic | ✅ Implemented | static | Notification-appliance logic now carries its own candidate-generation and spacing decision path, using visible/audible coverage selection rather than the sprinkler-style generic path; live Revit verification is still blocked by missing Autodesk DLLs. |

## User Interface (WPF)

| Capability | Status | Verification | Notes |
|---|---|---|---|
| Ribbon / entry point | ✅ Implemented | static | `FireProtectionApplication` + `FireProtectionCommand`. |
| Main window & MVVM base | ✅ Implemented | static | `MainWindow`, `ObservableObject`, `RelayCommand`. |
| Room selection / data binding | ✅ Implemented | static | `MainWindowViewModel`, `FireProtectionUiData`. |
| BruteForce UI (run, review, export) | ✅ Implemented | static | `SprinklerBruteForceViewModel`. |
| Room eligibility (3-state deterministic preflight) | ✅ Implemented | static | `RoomItemViewModel.IsEligible/IsBlocked/IsUndetermined/EligibilityReason`; `RevitSprinklerPlacementService.EvaluateRoomEligibility` read-only preflight (no live probe); FaceBased & WorkPlaneBased require a usable ceiling host, OneLevelBased a resolvable level; `yfbxcv 1234` regression fixed (no-ceiling WorkPlaneBased ⇒ BLOCKED). |
| Smart Level Select All / Clear All toggle | ✅ Implemented | static | single `ToggleSelectAllLevelsCommand`; label derived from `AreAllSelectableLevelsSelected` (levels with rooms only). |
| Smart Room Select All / Clear All toggle | ✅ Implemented | static | single `ToggleSelectAllRoomsCommand`; label derived from `AreAllSelectableRoomsSelected` (ELIGIBLE rooms only); manual changes synced via `PropertyChanged`. |
| Blocked/undetermined-room exclusion from selection/placement | ✅ Implemented | static | `ToggleSelectAllRooms` selects only ELIGIBLE; room CheckBox `IsEnabled=IsEligible`; non-eligible auto-deselected. |
| "Show eligible rooms only" / "Show levels with rooms only" toggles | ✅ Implemented | static | visibility-only via `ICollectionView.Refresh()`; source/selection preserved. |
| Default selection (eligible selected, blocked not) | ✅ Implemented | static | `ApplyDefaultSelection()` in ctor + `Reset`. |
| Preliminary/Final tab swap (UI labels + order) | ✅ Implemented | static | BruteForce→"Preliminary" (first), Collision→"Final"; `SprinklerViewModel` order + `SprinklerView.xaml` headers. |
| Hazard color coding | ✅ Implemented | static | `HazardClassToBrushConverter`. |

## Hazard Classification

| Capability | Status | Verification | Notes |
|---|---|---|---|
| Name-based classification | ✅ Implemented | static | `HazardClassifier`. |
| Hazard-specific engineering rules | 🔴 Not implemented | none | all classes use 15 ft placeholder. |

## Build & Test

| Capability | Status | Verification | Notes |
|---|---|---|---|
| Build Revit2024/2025/2026 | ✅ Implemented | static | 0 errors across configs. |
| Revit-free calculation tests | ✅ Implemented | static | 14/14 pass (`FireProtection.Tests`). |
| Runtime Revit placement test | 🔴 Not implemented | none | needs live Revit host; not run in dev env. |

## Deployment

| Capability | Status | Verification | Notes |
|---|---|---|---|
| Auto-deploy `.addin` + DLL copy | ✅ Implemented | static | `DeployToRevit` target; Application-only add-in. |
| Multi-version support | ✅ Implemented | static | 2024/2025/2026 configs. |

## Recent Significant Work

- **Code cleanup** — removed dead exporters/overloads (`ExportCalculationResult`,
  `GetDefaultCalculationExportPath`, `Extract`, `ExtractFromHostModel`, 3 `UiLauncher.Show` overloads,
  MainWindow 1/2/3-arg constructors) and unused `using`s. Builds clean; 14/14 tests pass.
  See [[SESSION_NOTES]].
- **Device-specific location-point split (2026-09-10)** — Added a dedicated location-point identifier layer
  that resolves distinct strategies for sprinklers, smoke detectors, and notification appliances before
  candidate generation. The project now encodes explicit device-family logic:
  `sprinkler-ceiling-grid`, `sprinkler-sidewall-edge-grid`, `smoke-detector-ceiling-grid`,
  `smoke-detector-wall-mounted`, `notification-appliance-ceiling-grid`, and
  `notification-appliance-wall-mounted`. This is a source-level fix only; runtime verification remains
  blocked because the backend cannot compile without the Autodesk Revit DLLs in the current environment.
- **Sprinkler placement Z bug fix (2026-08-25, first pass)** — Root cause (proven at runtime): hosted
  ceiling sprinklers (`FamilyPlacementType` = `WorkPlaneBased`, not `FaceBased`) were placed via the
  level-based overload and landed at the project origin (0,0,0). Fixed `RevitSprinklerPlacementService`:
  detect `WorkPlaneBased`+`FaceBased` as hosted, make `FindCeilingHost` search linked models and return a
  host reference via `Reference.CreateLinkReference`, and record the ACTUAL `instance.Location`.
  Compiles clean on Revit2024/2026; Revit2025 blocked only by a VS/Revit file lock. See
  [[SPRINKLER_ACTUAL_Z_DIAGNOSTIC]].
- **Sprinkler "all failed" fix (2026-08-25, second pass)** — The first-pass fix still routed
  `WorkPlaneBased` into the face-host-only branch; when no ceiling face was found (ceilings are
  linked-only, where hosted placement on a linked face is not reliably supported by the Revit API),
  `FindCeilingHost` returned null and **every** sprinkler failed. Refined fix: `WorkPlaneBased` now falls
  back to the Level-based overload (valid — `WorkPlaneBased` = hosted on a work plane, which a Level
  satisfies) and places at the already-normalized candidate XYZ; `FaceBased` still requires a host face;
  pendent mounting now selects the **downward** ceiling underside face. Added Phase 2/11 diagnostics
  (real `FamilyPlacementType`, hosting strategy, ceiling source, link instance, actual vs requested XYZ,
  full exception detail). Runtime verification in Revit still pending.
- **Revit placement layer** — `RevitSprinklerPlacementService` implemented incl. linked-level
  resolution, replacing the prior external-process approach.
- **BruteForce UI selection/eligibility (2026-08-26)** — UI-only: rooms flagged eligible/blocked from
  existing authoritative `RoomUiData.Geometry` (polygon ≥ 3 points + `CeilingHeightFt`); blocked rooms shown
  disabled with tooltip and excluded from Select All / default selection / placement. Added "Show eligible
  rooms only" and "Show levels with rooms only" visibility toggles (source collections + selection preserved
  via `ICollectionView.Refresh()`); `ApplyDefaultSelection()` seeds eligible levels + eligible rooms. Swapped
  the Preliminary/Final tab labels + order (BruteForce = "Preliminary", Collision = "Final") — presentation
  only; no calculation/placement/NFPA logic touched. UI builds clean (0 errors). See [[SESSION_NOTES]].
- **BruteForce engine** — X/Y/Z calculation completed and documented.
- **Placement Preflight / Eligibility (2026-08-26)** — Implemented a reusable, authoritative pre-placement
  eligibility layer so the UI blocks rooms the production pipeline provably cannot place. `ISprinklerPlacementService`
  gained `EvaluateRoomEligibility(RoomUiData, family, type)`; the Backend `RevitSprinklerPlacementService`
  implements it by **reusing the exact** `ResolveSymbol` + `_strategies.CanHandle` + `ResolveHostLevel` +
  `CeilingHostResolver.FindCeilingHost` logic used by real placement (single source of truth — UI does NOT
  re-derive Revit hosting rules). Result DTO `PlacementEligibilityResult` (UI, Revit-free) carries
  `IsEligible/Reason/StatusCode/FamilyPlacementType/HostingStrategy/CeilingSource/LinkInstanceName/HostLevel*`.
  UI `RoomItemViewModel.SetEligibility` applies it; `SprinklerBruteForceViewModel.RefreshEligibility()` runs it on
  ctor/Reset/family+type change, auto-deselects any room that becomes blocked, and the room grid + single
   Select-All toggle already disable blocked rooms. `CollectSelectedRooms` now guards against blocked rooms.
   **Status: UI builds clean (0 errors). Backend not buildable in headless CLI (Revit API CS0246 — environmental);
   type-correctness verified by inspection. Runtime Revit verification pending.** See [[SESSION_NOTES]], [[DECISIONS]] (013).
- **Placement Preflight → AUTHORITATIVE probe (2026-08-26, supersedes first-pass preflight above)** — The
  first-pass preflight was **runtime-insufficient**: it tested only a representative centroid point and treated
  `WorkPlaneBased` as always eligible, so rooms reported eligible while actual placement produced
  `Placed=0, INVALID=N, Failed=N`. Rewrote `EvaluateRoomEligibility` to be authoritative: the UI computes each
  room's **real candidate points** via the exact `BruteForceCalculationService` (`CalculateBruteForce`), then the
  Backend probes **real placement** of every candidate through the same `strategy.Place` / `ResolveHostLevel` /
  `CeilingHostResolver` path used by `PlaceSprinklers`, inside a **rolled-back `Transaction`**. A room is eligible
  only if ≥1 candidate creates a `FamilyInstance` that is spatially valid (deviation ≤ tolerance) — the identical
  success criterion as real placement, so the `INVALID` blind spot is eliminated. Eligibility is fail-closed
  (`NO_CANDIDATE_POINTS` / `NO_PLACEABLE_CANDIDATE` / `NO_VALID_CANDIDATE` / `PREFLIGHT_ERROR`). Added
  `CollectAllVisibleRooms()`, `ClearEligibilityCache()`, `SelectedVisibleEligibleRoomCount` gating for
  `CanExecutePlaceSprinklers`, and `[ROOM-ELIGIBILITY]` / `[ROOM-SELECTION-GUARD]` `Debug` diagnostics (§17).
  **Status: UI builds clean (0 errors). Backend not buildable in headless CLI (Revit API CS0246 — environmental);
  type-correctness verified by inspection. Runtime Revit verification pending — the probe cannot be exercised without
  a live Revit host, but its logic is the same code path as placement, which is the fix's whole point.** See
   [[SESSION_NOTES]], [[DECISIONS]] (013), PROJECT_MEMORY §8b.
- **Eligibility 4-state model — ELIGIBLE/BLOCKED/PLACEMENT_ERROR/UNKNOWN (2026-08-26, Decision 014)** — Split the
  fail-closed eligibility outcome so real placement defects are never hidden as BLOCKED. `PlacementEligibilityResult`
  now carries an `EligibilityState` with derived `IsEligible/IsBlocked/IsPlacementError/IsUnknown`. `RevitSprinklerPlacementService
  .EvaluateRoomEligibility` classifies: ELIGIBLE (≥1 candidate placed + spatially valid); BLOCKED (deterministic
  inability only — including FaceBased `REQUIRED_HOST_UNAVAILABLE`); PLACEMENT_ERROR (`CREATED_BUT_INVALID`,
  `PROBE_EXCEPTION`, or any unexpected API/runtime failure while a candidate existed and placement was attempted);
  UNKNOWN (family/type not selected, or candidate calculation could not run). The probe inspects `PlacementOutcome
  .ErrorCode` and distinguishes deterministic vs defect; any exception in the probe harness/strategy is `PLACEMENT_ERROR`,
  not BLOCKED — **this breaks the circular hide** (a broken placement path now shows red, countable `PLACEMENT_ERROR`
  rooms instead of a blanket `BLOCKED`). `RefreshEligibility` sets UNKNOWN for every room when the candidate calculation
  throws (instead of probing a null list → BLOCKED) and auto-deselects any non-ELIGIBLE room. Only ELIGIBLE rooms are
  selectable (grid `IsEnabled=IsEligible`, single Select-All selects only `IsEligible`, "Show eligible only" filters
  to `IsEligible`). Added per-candidate `[ROOM-CANDIDATE-DIAGNOSTIC]` `Debug` output (requested vs actual XYZ, delta,
  distance, validation status, exception detail) to pinpoint the exact defect. XAML mutes all non-eligible rooms and
  gives PLACEMENT_ERROR a red outline, UNKNOWN an amber outline. **Status: UI builds clean (0 errors). Backend not
  buildable headless (Revit API CS0246 — environmental); type-correct by inspection. Runtime Revit verification pending.**
  The `(0,0,0)` origin-snap defect is already fixed by Decisions 011/012; if a residual `CREATED_BUT_INVALID` appears at
  runtime it will surface as `PLACEMENT_ERROR` (with requested-vs-actual deviation in `Reason`), to be fixed at the
  placement/coordinate source — **not** by enlarging `PlacementValidationToleranceFt`. See [[SESSION_NOTES]], [[DECISIONS]] (014), PROJECT_MEMORY §8b.

- **Eligibility 3-state deterministic preflight (2026-08-26, Decision 016) — SUPERSEDES the authoritative-probe & 4-state bullets above.** The continuation master prompt replaced the live rolled-back-`Transaction` probe AND the 4-state (PLACEMENT_ERROR/UNKNOWN) model with a **3-state** model (ELIGIBLE/BLOCKED/UNDETERMINED) and **forbids any live `strategy.Place()` probe**. `RevitSprinklerPlacementService.EvaluateRoomEligibility` now performs **read-only** queries only (`ResolveFamily`, `ResolveHostLevel`, `CeilingHostResolver.FindCeilingHost`): FaceBased **and** WorkPlaneBased REQUIRE a usable ceiling/host face (the `yfbxcv 1234` regression — a no-ceiling WorkPlaneBased room is now BLOCKED, not ELIGIBLE); OneLevelBased requires only a resolvable level; a numeric `CeilingHeightFt` is never host proof. Family/type not resolved ⇒ UNDETERMINED (config error, not "all blocked"); candidates null ⇒ UNDETERMINED `CALCULATION_FAILED`; unexpected exception ⇒ UNDETERMINED `PREFLIGHT_ERROR`. `RoomItemViewModel` carries `IsEligible/IsBlocked/IsUndetermined`; the room grid mutes non-eligible, red-outlines BLOCKED, amber-outlines UNDETERMINED, and shows an inline status line + tooltip. `RefreshEligibility` runs on ctor/Reset/family+type change/per-room hazard edit and auto-deselects non-eligible rooms.   **Status: UI builds clean (0 errors). Backend not buildable headless (Revit API CS0246 — environmental); type-correct by inspection. Runtime Revit verification pending.** See [[DECISIONS]] (016), PROJECT_MEMORY §8b.

- **Selection contract + level↔room sync (2026-08-26, master prompt: SPRINKLER_SELECTION_LINKED_MODEL_PRODUCTION)** — Built on the 3-state preflight. All ELIGIBLE rooms are selected by default; BLOCKED/UNDETERMINED are frozen (`IsEnabled=IsEligible`), unchecked, and never selectable. `RefreshEligibility` now normalizes selection: non-eligible always deselected; a room that *just became* ELIGIBLE is selected by default; already-eligible keeps the user's manual choice. Level selection selects/deselects only that level's ELIGIBLE rooms (other levels unaffected). Room Select All/Clear All act on visible ELIGIBLE only. `CollectSelectedRooms` rejects `!IsEligible` (defensive final-placement guard). Linked-model eligibility cache key now includes room `Name`+`LevelName` to avoid cross-link staleness. Added `FireProtection.Tests/BruteForceSelectionTests.cs` (Revit-free, fake `ISprinklerPlacementService`) covering §22 A–G + K. **Status: UI builds clean. `FireProtection.Tests` NOT executed headless (Backend Revit API CS0246) — tests ADDED, run under Revit-enabled build.** See [[SESSION_NOTES]].


## In Progress

- Active fix awaiting **runtime verification in Revit**: confirm placed sprinkler `instance.Location`
  ≈ (14.12, 31.95, 12) for `02_FireProtection_Test` (host) + `01_Architectural_Test` (link). Then remove
  any stray misplaced (0,0,0) instances from prior runs. **P0 — must complete before any of the
  catalog / per-row / device-popover work is runtime-verified.**
- New runtime-verification items from Decision 021: (a) smoke run then NA run over the same rooms with
  policy Skip — neither may skip or delete the other's devices; (b) derived detector/appliance attributes
  follow per-row family changes into the calculation; (c) the sprinkler report window renders themed and
  truthful on a live run.
- Next verification gap after that: **NFPA-compliant spacing** (replacing provisional values — an
  uncommitted "approved" flip in `DefaultHazardPlacementRules` was reverted 2026-09-11: still needs real
  FPE sign-off, the values themselves are plausible NFPA-13 hazard tables but unverified here).
- Production task list has been finalized in [TODO.md](TODO.md): the repository now tracks the required device-specific rule separation, engineering-grade reporting, UI success/failure summaries, and runtime validation gates.
- Build validation note (2026-09-11): the earlier "environment-blocked CS0246" entry is **stale** — the
  solution now builds 0 errors in Revit2025 and Revit2026 (Revit 2026 assemblies present on this machine),
  and the full console test harness passes headless. Only *runtime* Revit verification stays blocked.

## Completed Milestones

- **Family import classification correction (static 2026-10-07).** `FamilyLoadHelper` no longer labels
  every no-count-change load as "already present." It opens the selected RFA as a family document in
  the running Revit application, reads its internal family name, imports that document with load
  conflict options, and reports already-present only when that actual family name exists. This lets
  Revit's current-version family-document open/import path upgrade supported older families. Standard
  pendent/upright point selection was reviewed against the project NFSA/NFPA 13 rule implementation:
  centered max-spacing layout, clearance/obstacle screening, and per-head S x L checks are present;
  no change made to it or to sidewall/wall-based placement. Revit-free suite passes; live RFA upgrade
  behavior still needs Revit runtime verification.

- **Linked-model and family-load reliability (static 2026-10-07).** Link discovery now traverses loaded
  nested Revit links and composes each instance transform into host coordinates while preserving
  nested-link metadata. The top-bar family loader now reports after the Revit API callback, the shared
  loader classifies loads by model family-count changes instead of assuming the file name equals the
  internal family name, and sprinkler-family enumeration returns the actual Revit symbols when enabled.
  Revit2024/2025/2026 builds succeed. Live validation with representative linked models and RFA files is
  still required; no such project model was available during this change.

- **Device pipeline hardening + sprinkler run report (Decision 021, static 2026-09-11).** Kind-scoped
  existing-device policy (fixes cross-device skip/fail), catalog-derived read-only device attributes
  replacing the smoke/NA dropdowns, the BruteForce tab now opens the shared `PlacementResultReportWindow`
  (via `SprinklerPlacementReportMapper`), calc-review + cancelled-run honesty in the device core,
  outside-room guard ported to devices, C-E grid-truncation warning in the sprinkler calc, sprinkler
  S→S/Wall columns showing real applied values with bounded editability, reverted `HasApprovedRules`
  flip. BUILD 0 errors (R2025+R2026), tests all pass incl. `DeviceReportAndKindTests`. RUNTIME-UNVERIFIED.
- Extraction pipeline (Phase 1) — implemented.
- BruteForce sprinkler calculation engine — implemented.
- Revit placement service (element creation) — implemented.
- End-to-end documentation (`SPRINKLER_POINT_CALCULATION_EXPLAINED.md`) — written.
- Dead-code cleanup — completed and verified.
- Plan for catalog + per-row family/type + per-row spacing override (Decisions 017–020) — locked.
- **Catalog + per-row family/type + per-row spacing override + device-popover implementation
  (Decisions 017, 018, 019, 020) — implemented (static, 2026-09-01).** Backend
  `CatalogLoader` / `CatalogService` (ClosedXML), `ICatalog` (UI), `CatalogViewModel` +
  `CatalogBar` (top bar with Browse/Reload/version), per-row `SelectedFamily` / `SelectedType` /
  `MaxSpacingFtOverride` / `BoundaryClearanceFtOverride` + bulk-apply commands, per-row
  `MissingFamiliesModal` (Proceed/Cancel + "Export missing list to CSV") +
  `SkippedMissingFamilyCount`, per-row symbol resolution in `PlaceSprinklers`,
  `BruteForceCalculationService.ApplyPerRoomOverrides` (NFPA clamp + per-room `IsProvisional`),
  `LevelSettingsPopover` (⋯ per level) for Smoke Detector / Notification Appliance declarative
  metadata. 22/22 standalone tests pass (`FireProtection.CatalogStandalone`).
  **Runtime verification in Revit is still P0-first** — none of this has been live-Revit-verified.

## Related Documentation

- [[PROJECT_CONTEXT]]
- [[ARCHITECTURE]]
- [[DECISIONS]]
- [[TODO]]
- [[SESSION_NOTES]]
- [[SPRINKLER_POINT_CALCULATION_EXPLAINED]]

## Catalog Source Discipline Fixes (2026-10-07)

### Implemented
- **Host stability.** Added `FireProtection.UI/Services/WpfHost.cs` (creates + statically pins a WPF
  `Application` with `ShutdownMode.OnExplicitShutdown`; exposes `UiDispatcher` and `RunOnUi`). Called
  from `UiLauncher.Show` before the first `Window` is constructed. Removed the
  `Dispatcher.CurrentDispatcher` fallback from `Dialogs.PumpUi`; replaced the two
  `Application.Current.Dispatcher` dereferences in `MainWindowViewModel.LoadFamilies` (which would
  have thrown `NullReferenceException`) with `WpfHost.RunOnUi`.
- **Crash diagnostics.** `FireProtectionApplication.InstallCrashDiagnostics()` logs unhandled
  exceptions via `AppDomain.CurrentDomain.UnhandledException`; `WpfHost` attaches
  `Dispatcher.UnhandledException`. This is how the real fault behind the `SEHException` was found.
- **Fixed the launch crash.** `SourceMode` (private setter) was bound with `Mode=TwoWay`, throwing
  inside `Window.Show()` and aborting the command; Revit then crashed in its own finalizer. Both radio
  bindings are now `Mode=OneWay`; the `Checked` handler drives `TrySetSourceMode`.
- **Browse button.** `CatalogBar.xaml` used `InverseBoolToVis` on `IsCatalogFileMode`, so Browse showed
  in Model mode and hid in Catalog-file mode. Now `BoolToVis`.
- **Source discipline.** `CanFallBackToModelFamilies()` on both
  `SprinklerBruteForceViewModel` and `DevicePlacementViewModelBase` prevents the silent import of
  every Revit family when the active catalog is empty in Catalog-file mode.
- **Status messages.** Mode-aware `CatalogStatusMessage` on all three tabs, distinguishing no catalog /
  empty sheet / no families in the model. Added `CatalogSheetName` overrides naming the exact sheet.
  New `StringToVisibilityConverter` and `WarningText` style for collapsed explanatory text.
- **Missing-data warnings.** `MissingCatalogDataMessage` on all three tabs; null when the catalog
  covers the selected type.
- **Load-family wiring.** The sprinkler tab now passes a `TryLoadFamily` callback to
  `MissingFamiliesModal`; previously the button was inert. Both tabs report
  `PlacementEligibilityStatusCodes.FamilyNotLoaded`. A per-row family/type change now re-runs the
  eligibility preflight on the sprinkler tab. Placement-skip warnings name the remedy.
- **Notification contract.** All three tab ViewModels react to the same catalog property set,
  including `IsCatalogFileMode`.
- **Documentation.** Corrected the `FamilyAvailability` claim in `SESSION_NOTES.md`; no such member
  exists, and load state is reported through eligibility.

### Not done / open
- Nullable Candela/dBA readers were considered and rejected; see [[DECISIONS]].
- Hazard-class dropdown remains hardcoded (`LIGHT`, `OH1`, `OH2`, `EH1`, `EH2`) by explicit user
  choice. `ICatalog.GetHazardClassesForSprinklerFamily` / `GetHazardClassForSprinkler` remain
  implemented-but-unused.
- Several parsed workbook columns are still read by nothing outside the four type values the engine
  merges: `TempRatingF`, `ResponseType`, `DeflectorToCeilingIn`, `SidewallEndWallClearanceFt`,
  `CoverageRadiusFt`, `MinSpacingFt`.

### Verification status
Static only. Builds clean on Revit2024/2025/2026; 17 test suites pass. **The crash fix and all loading
behaviour are unverified in a live Revit session** and remain P0 to confirm.
## UI Freeze Work, Tier 1 (2026-10-07)

### Implemented
- **`CeilingHostResolver`** now caches the Ceiling/Floor/RoofBase and `RevitLinkInstance` element lists
  per `Document`, bracketed by `BeginPass()`/`EndPass()`. Previously these were rebuilt for EVERY
  candidate point via `4 + 3L` whole-document collectors - the single largest source of the freeze.
  Exposed as `ISprinklerPlacementService.BeginHostResolutionPass` / `EndHostResolutionPass` /
  `HostCollectorCallsSaved`, and bracketed around both the eligibility sweep and the placement run.
  Geometry is still computed per candidate; only the collection is cached.
- **Eligibility refresh is coalesced** in `SprinklerBruteForceViewModel` and
  `DevicePlacementViewModelBase`: a pending flag plus an input version bumped at the decline point, so a
  burst of triggers runs one sweep plus at most one follow-up. Previously every trigger queued a full
  sweep, because `RevitApiContext.Run` enqueues without de-duplicating.
- **Catalog-change handlers are idempotent** in both tab VMs. One mode switch used to queue ~7 sweeps
  (8 `SetCatalog` notifications + `SourceMode`, of which the wide filter matched 6); now 1. This
  corrects a regression introduced in the previous session's "unify the notification contract" change.
- **Eligibility cache no longer cleared unconditionally.** The key already covers room, level name,
  hazard, family and type. Invalidation now happens after a successful `TryLoadFamily`
  (`MissingFamiliesModal` callback and `MainWindowViewModel.LoadFamiliesCore`).
- **Per-sweep instrumentation**: `FireProtectionLog.Info` records elapsed ms, room count, selected
  family and collectors avoided. Read it at `%APPDATA%\FireProtection\logs\`.
- **New suite `EligibilityCoalescingTests`** (6 tests) pinning: 8 triggers -> 1 sweep, a coalesced
  trigger -> exactly one follow-up, a failure does not latch the coalescer, host passes stay balanced
  even when a sweep throws, one mode switch -> 1 sweep.
- `FakePlacementService` updated for the new interface members.

### Open / not done
See [[DECISIONS]] for the Tier 2+ backlog: per-keystroke filters and counts (250 ms debounce approved
but not implemented), `FilteredElementCollector` on the WPF thread from `CatalogBar` (API violation),
modal dialogs blocking Revit's main thread, and O(n^2) backend extraction.

### Verification status
Builds clean on Revit2024/2025/2026. 18 test suites pass. **Not runtime-verified** - the measured
speed-up is unknown until the tool runs in Revit and the new log line is read.
## Tile-Centric Ceiling Placement (2026-10-08)

### Implemented
- **New grid source (Revit 2025.3+).** `CeilingExtractor.TryReadGridFromCeilingGridLines` calls
  `Ceiling.GetCeilingGridLines` through a cached `MethodInfo` so one codebase compiles for 2024/2025/2026.
  Groups lines into two direction families, requires near-perpendicularity, takes the module as the
  median gap between offsets, and sets the origin to the intersection of two real lines -> EXACT phase.
  Exposed as `IsCeilingGridLinesApiAvailable`.
- **Phase is now reported, not assumed.** `CeilingData.GridOriginSource` +
  `GridOriginSources` + `HasExactGridPhase`; mirrored onto `CeilingGrid.PhaseIsExact` /
  `PhaseSource`; `CeilingGridMath.DescribePhase` for reports. A provisional phase now logs a WARNING
  that names the problem and how to fix it.
- **Orthogonality check** added to the material-pattern reader (`OrthogonalityToleranceRad`). The second
  grid angle is no longer silently discarded.
- **New `TileCentricPatternGenerator`** (Backend, Revit-free, pure math):
  - `TryGenerate` - count-driven X-2X-X search with irregular-footprint support, for a caller that has
    a required device count.
  - `TrySolveBalancedLayout` - the API the placement engines use; takes a MAXIMUM whole-tile pitch,
    derives the count, and solves the balanced start tile. This is what replaced the anchored array.
  - `SnapPositionsToTiles` - safety net that moves positions onto free tile blocks without ever
    dropping a device or letting two share a tile.
- **Sprinklers wired in.** `BruteForceCalculationService.SelectCenteredGridOnTiles` now uses the solved
  balanced layout for `uIndices`/`vIndices` when one exists, keeps the existing bounded snap / obstacle
  recovery / `FinalizeSelection`, and falls back to the old anchor-and-append otherwise. Adds a
  diagnostic line describing the solved pattern and wall gaps.
- **Smoke and notification wired in.** New `AddTileCentricCandidates` in
  `SmokeDetectorCalculationService` replaces "enumerate every tile centre" with a balanced
  whole-tile-pitch pattern, still filtered by each tab's `TryAccept`, with the old enumeration as
  fallback.
- **`CeilingGrid` made public** (it was private nested in `CeilingGridMath`) so the generator can use it.
- **New `TileCentricPatternTests`** (13 tests) registered; suite count 19.

### Verified
Builds clean on Revit2024/2025/2026; 19/19 suites pass. Worst deviation from a true tile centre is
0.000E+000 ft on both axis-aligned and 30-degree lattices. The worked example (20 ft room, 2 ft tiles,
15 ft spacing) now yields a 10 ft pitch with 2 columns and 5 ft wall gaps, replacing the previous
1 ft / 14 ft / 4 ft spread.

### Open / not runtime-verified
See [[DECISIONS]] for the full list of deferred items (no view-export fallback for pre-2025.3,
family rotation not aligned to the grid angle, one lattice per multi-ceiling room, and the two engines'
differing ceiling ranking). Nothing here has been confirmed in a live Revit session yet; the new
`CeilingGrid` extraction diagnostic is how to confirm detection and phase.

## Sidewall Sprinkler Placement & Obstacle Mode Pass (2026-11-11)

### Fixed
1. **Full NFPA 13 Sidewall Coverage Generation in Obstructed Rooms (Kitchen R-01, Study R-04)**:
   - In `BruteForceCalculationService.SelectSidewallDirectional`, interior sample points inside obstacle bounding boxes are no longer removed from the coverage verification sample grid. The solver evaluates the entire room space and reliably seeds opposing walls (NFPA 13 §11.3.3.2) when room depth exceeds the listed throw.
   - Cleaned polygon loop (`CleanPolygonLoop`) is now assigned to `result.Polygon` so `WallEdgeIndex` maps 1:1 with `PlacementContext.RoomPolygon` during face hosting.
2. **Linked Architectural Wall vs Host Foundation Wall Arbitration**:
   - `FindWallFaceHostForEdge` evaluates candidate walls across host document AND linked models simultaneously with composite scoring:
     - Faceless candidates penalised by +1000.
     - Foundation/subgrade walls penalised by +500.
     - Linked architectural boundary walls correctly win hosting over host-doc foundation walls.
3. **Obstacle Handling Control in UI**:
   - Added `Ignore Obstacles` checkbox in `SprinklerCollisionView` (Final Review) bound to `SprinklerCollisionViewModel.IgnoreObstacles` / `SprinklerBruteForceViewModel.IgnoreObstacles`.
   - When **Checked**: Places all calculated sidewall sprinklers unconditionally.
   - When **Unchecked** (default): Skips ONLY sprinklers whose calculated position directly collides with an obstacle 3D bounding box (`InsideExpandedBox(..., 0.0)`). Non-colliding sprinklers in the room are placed normally.
4. **Re-run Policy Default**:
   - Defaulted `ExistingDevicePolicySelection` to `ReplaceExisting` so re-testing doesn't skip rooms with heads from previous runs.
5. **Zero Scope Creep**:
   - Ceiling-mounted, pendent/upright, smoke detector, and notification appliance logic strictly untouched. All 19 test suites pass cleanly.

## Pure Sprinkler Layout (No Obstacle Shifting) Pass (2026-11-12)

### Changes
1. **Removed Obstacle Relocation / Candidate Shifting**:
   - In `CenteredGridPattern`: Removed the expanded search (`spacing/2`) and candidate snapping that displaced target points to other locations. Every grid point is kept at its natural coordinates `(rx = tx, ry = ty)`.
   - In `SelectCenteredGridOnTiles`: Removed the expanded obstacle-recovery search that shifted heads to distant tiles.
   - In `TryAcceptPoint` & `IsPlacementValid`: Removed obstacle rejection so candidate generation strictly follows room geometry and spacing rules.
2. **Pure Conflict Tagging & Checkbox Control**:
   - Any point colliding with an obstacle 3D bounding box is tagged `pt.HasObstacleConflict = true` without shifting it.
   - If "Ignore Obstacles" is **Unchecked**: Skips *only* the specific conflicting sprinklers. All other non-colliding sprinklers are placed at their exact positions.
   - If "Ignore Obstacles" is **Checked**: Places all calculated sprinklers unconditionally.
3. **Removed Confirmation Modal Dialog**:
   - Removed `Dialogs.Confirm("Replace will DELETE the existing sprinklers...", ...)` upon clicking "Place Sprinklers".
4. **Verification**:
   - All 19 test suites PASS (`ALL TESTS PASSED`). Clean builds for Revit2025 and Revit2026.


## Sidewall Head Misplacement & Deviation Metric Fix (2026-11-12)

### Fixed
1. **End-face head stacking (kitchen top wall)**: export evidence (run 184029) showed three heads requested at X 22.07/12.97/3.87 all placed at X −7.86 and an edge-0 head at an arbitrary X=11.24. `WallSidewallPlacementStrategy.BuildHostFromWall` now evaluates `HostObjectUtils.GetSideFaces` (both shells) plus the geometry scan, filtered to true side faces only (`|normal · wallDir| ≤ 0.7`), scored by room-facing orientation + distance to the requested point, and synthesizes a ⊥ wallDir plane from the wall curve when no side face is usable. `Place()` additionally aborts with a clear diagnostic when the projected along-wall drift exceeds 3.0 ft.
2. **Broken deviation metric**: `PlacementDeviationFt` compared the instance against the strategy's projected host target (reporting ~0 ft for 29.9-ft errors). It now follows its DTO contract — requested calc point vs actual instance location — while post-placement validity is two-part: execution accuracy (actual vs `ExpectedLocation` ≤ `PlacementValidationToleranceFt`) and sidewall fidelity (projected target vs requested ≤ 1.5 ft face-snap envelope). Misplacements now surface as `PLACED_BUT_INVALID` with a `[REVIEW]` warning naming both deviations.
3. **Loose sanity envelope**: `IsSpatiallySane` widened to `Math.Max(maxDeviationFt, 15.0)` allowed 30-ft misplacements to pass; restored the strict 3.5 ft envelope (origin-snap rejection unchanged).
4. **Wall-edge mask fed real segments (Decision 032)**: `PlacementRoomSelection.Boundary` now carries the rehydrated `RoomData.Boundary` through `PlacementInputJsonExporter.BuildSelections`, and `PlacementInputBuilder.Build` copies `OuterLoop.Segments` into the snapshot so `BuildWallEdgeMask` can mark non-wall edges (glass, room-separation lines) instead of defaulting every edge to a wall.
5. **Verification**: obstacle handling itself needed no change — the per-point gate (`!ignoreObstacles && point.HasObstacleConflict` ⇒ skip that point only) was confirmed correct by the 2026-11-11 exports (kitchen 5 placed + 1 conflicted point skipped unchecked; 24/24 placed checked). Built `Debug`/`Revit2025`/`Revit2026` (0 errors / 0 warnings); Backend deployed to the 2025 + 2026 addins dirs; all 19 test suites pass under Revit2025 and Revit2026 configurations. **Runtime verification pending a Revit restart** (assemblies load at startup).

## Sidewall Sprinkler Balanced Centering & Rulebook Distribution (2026-11-12)

### Fixed
1. **Replaced corner-anchored formula in SelectSidewallDirectional**:
   - The previous formula pinned the first head at endWallMax ({max} / 2$) from the start corner and the last head at endWallMax from the end corner, distributing the remaining span between them. On walls where the remaining span was smaller or close to {max}$ (e.g. 21.61 ft wall with 5.735 ft end clearances), this created a massive 10.14 ft gap right in the center of the wall.
   - When a column or obstacle was in the center of the wall, the two heads sat on either side of it. When the obstacle was deleted, the heads stayed in the exact same positions, giving the false appearance of residual obstacle-dodging logic.
2. **Implemented NFPA 13 & Industry-Preferred Spacing**:
   - Wall length $ is divided into  = \\max(1, \\lceil W / S_{max} \\rceil)$ equal coverage bays.
   - Uniform spacing  = W / N$ between adjacent heads.
   - End clearances are equalized and centered:  = S / 2 = W / (2N)$.
   - Enforced NFPA 13 rules:  \\le S_{max} / 2$,  \\ge 4\\text{ inches}$ minimum clearance (NFPA 13 §10.3.4.2), and  \\ge 6.0\\text{ ft}$ minimum spacing between sprinklers (NFPA 13 §10.3.3.4).
   - Candidate points remain pure mathematical locations; obstacle collisions are flagged per-head with zero displacement or dodging.
3. **Verification**:
   - Added unit test TestSidewallBalancedWallDistribution in SidewallPlacementTests.cs.
   - All 19 test suites PASS (ALL TESTS PASSED). Clean builds for Revit2025 and Revit2026.


## NFPA 72 Chapter 18 Notification Appliance Logic Rewrite & UI Streamlining (2026-11-12)

### Fixed & Implemented
1. **UI Streamlined (`NotificationApplianceView.xaml`)**:
   - Removed the top `Border` card containing redundant pickers (`Appliance Type`, `Candela`, `Notification dBA`) and status text.
   - Cleanly embeds `<vDevices:DevicePlacementView />` inside `<Grid Margin="16,16,16,0">`, achieving direct visual and workflow parity with `SmokeDetectorView.xaml`.
2. **Dedicated Engine (`Nfpa72NotificationApplianceEngine.cs`)**:
   - Separated notification appliance calculation from smoke detector initiating rules.
   - Standard rooms (<= 30 ft x 30 ft / <= 900 sq ft) receive exactly 1 appliance per compartment rule.
   - Wall-mounted appliances target the midpoint of the primary (longest) physical wall edge at 90 inches AFF (7.5 ft above finished floor), within NFPA 72 §18.5.5.1's required 80 in to 96 in envelope. Bridges obstacle notches so `WallEdgeIndex` maps to `WallSidewallPlacementStrategy`.
   - Ceiling-mounted appliances attach at ceiling elevation at the room centroid.
   - Corridors (width <= 20 ft, aspect ratio >= 2.5:1) are spaced along the corridor at <= 100 ft intervals with <= 15 ft end offsets (NFPA 72 §18.5.5.5).
   - Obstacles evaluated non-destructively: intersecting appliances are flagged with `HasObstacleConflict = true`.
3. **Placement Executor (`RevitNotificationAppliancePlacementExecutor.cs`)**:
   - Directly executes `Nfpa72NotificationApplianceEngine.Calculate`.
4. **Verification**:
   - Added unit tests in `NotificationApplianceRulesTests.cs` validating single appliance placement, wall mounting elevation (7.5 ft AFF), corridor layout (100 ft spacing / 15 ft end offsets), and obstacle conflict detection.
   - All 19 test suites pass.
   - Clean compilation on `Revit2025` and `Revit2026`.


## Large Model UI Launch Optimization & Freeze Prevention (2026-11-12)

### Fixed & Implemented
1. **Lightweight Startup Extraction**:
   - Implemented `FireProtectionExtractionService.ExtractLightweightSnapshot`, collecting only levels, ceilings, rooms, and existing sprinklers (what the UI table actually needs to render) in < 1 second.
   - Defer heavy obstacle extraction across tens of thousands of elements to a lazy provider (`lazyObstacles`).
2. **Just-In-Time (JIT) Obstacle Extraction**:
   - Obstacles are extracted on-demand during placement calculation with visible progress updates.
   - Accelerated `AssociateObstaclesToRooms` with vertical Z-screening and $O(1)$ `HashSet` lookups, replacing $O(\text{Rooms} \times \text{Obstacles})$ nested loops.
   - 100% of internal calculation, clash detection, and placement logic is strictly preserved.
3. **Selected-Levels Scoped Preflight**:
   - `RunEligibilityPass` evaluates rooms on the active/selected levels at startup, avoiding computing 500+ brute force layouts across unselected floors simultaneously.
   - Newly toggled levels refresh eligibility via the existing coalesced queue.
4. **Verification**:
   - All 19 unit test suites pass.
   - Builds cleanly for both `Revit2025` and `Revit2026`.
