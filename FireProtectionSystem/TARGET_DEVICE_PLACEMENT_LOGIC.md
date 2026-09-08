# Target Logic — Where Devices Go

Sprinklers · Smoke Detectors · Notification Appliances
Full pipeline: Revit → Catalog → Hazard → Placement Input → BruteForce Calc → Host Resolve → Revit Place.
Every factor below is tied to the class/method that produces or consumes it.

---

## 0. End-to-end pipeline

| # | Stage | Class | Method |
|---|---|---|---|
| 1 | Revit entry | `FireProtectionCommand` | `Execute(...)` |
| 2 | Model extract | `FireProtectionExtractionService` | `ExtractAndExport(Document)` |
| 3 | Catalog load | `CatalogLoader` → `CatalogValidator` → `CatalogService` | `Load(path)` → `Validate(catalog)` → `FromCatalog(catalog)` |
| 4 | Device ctx | `RevitSprinklerFamilySource` | `ResolveDeviceContext(family, type)` |
| 5 | Build snapshot | `PlacementInputBuilder` | `Build(project, fam, type, rooms)` |
| 6 | Export JSON | `PlacementInputJsonExporter` | `ExportInput(...)` / `Export(snapshot)` |
| 7 | Brute-force calc | `BruteForceCalculationService` | `Calculate(snapshot, rules, config)` |
| 8 | Behavior map | `DevicePlacementBehaviorResolver` | `FromResolvedFamily(...)` |
| 9 | Host resolve | `CeilingHostResolver` | `FindCeilingHost(doc, xyz, level)` |
| 10 | Strategy | `FaceBasedPlacementStrategy` / `WorkPlaneBasedPlacementStrategy` / `LevelBasedPlacementStrategy` | `Place(PlacementContext)` |
| 11 | Place in Revit | `RevitSprinklerPlacementService` | `PlaceSprinklers(family, type, calcResult, ...)` |

---

## 1. Rules file (numbers live here, not in code)

Engineer fills `Catalog.xlsx`. Loader: `CatalogLoader.Load(path)` → strict sheet reader.

**Sheets and columns** (`CatalogValidator` / `CatalogLoader`):

- `Sprinklers`: `FamilyName | TypeName | HazardClass | Mount | Notes`
- `SmokeDetectors`: `FamilyName | TypeName | DetectorType | Mount | CeilingSlope | Notes`
- `NotificationAppliances`: `FamilyName | TypeName | ApplianceType | Candela | NotificationDba | Notes`

`CatalogValidator.Validate` checks required columns, duplicates, known values.

`CatalogService` exposes:
- `GetSprinklerMount(family, type)` → drives orientation factor.
- `GetSprinklerFamilies`, `GetSprinklerTypesForFamily`, `GetHazardClassesForSprinklerFamily`.
- Mirror sets for detectors and appliances.

Per-room rules table comes from `HazardPlacementRuleSet` (see §3). `DefaultHazardPlacementRules.HasApprovedRules => false` — every value is `IsProvisional = true`, so output is flagged `ReviewRequired`.

---

## 2. Shared location method (sprinklers, smoke detectors, strobes)

Divide-and-centre. Do not search.

```
1. Take room outline + inner loops. Source: RoomExtractor.ExtractBoundary
   → BoundaryData.OuterLoop/InnerLoops/Polygon.

2. Align grid to room walls (TransformData BasisX/BasisY).

3. Pick primary ceiling. Rule (RoomExtractor.FindCeilingsForRoom):
   level-matched FLAT > SLOPED > STEPPED > level-mismatched > highest Z.

4. Compute spacing per direction:
     count  = ceil(length / MaxSpacing)
     step   = length / count
     first device = half step in from wall
   → wall distance + spacing correct automatically.

5. placementZ =
     BottomElevationFt               (flat)
     (Top + Bottom) / 2              (sloped)
     LevelElevation + CeilingHeight  (fallback)

6. Filter candidates:
     BoundaryClearanceFt vs RoomGeometry.DistanceToOuterBoundary
     ObstacleClearanceFt per category vs ObstacleBox (Z check via SpansZ)
     ExistingSprinklerSeparationFt   vs ExistingSprinklerData.Location

7. Greedy select:
     skip if covered by CoverageRadiusFt
     enforce MinSpacingFt (default = CoverageRadiusFt)

8. Verify:
     max gap within MaxSpacingFt
     every wall within [BoundaryClearance, MaxDistanceFromWalls]
     coverage grid re-sample; flag shortfall

9. Odd shapes fall back to brute-force search and flag for review.

Helpers (in BruteForce/GeometryMath.cs and RoomGeometry.cs):
- GeometryMath.PointInPolygon(x, y, poly, tol)
- GeometryMath.DistancePointToSegmentSquared(...)
- GeometryMath.InsideExpandedBox(...)
- RoomGeometry.IsPointInsideRoom / DistanceToOuterBoundary
```

---

## 3. Sprinklers

Driven by: hazard class + sprinkler type.

### 3.1 Hazard → rules (`DefaultHazardPlacementRules.GetRules`)

`HazardClass` enum: `Light, OH1, OH2, EH1, EH2`.

| Field (HazardPlacementRuleSet) | Light | OH1 | OH2 | EH1 | EH2 |
|---|---|---|---|---|---|
| `MaxSpacingFt` | 15 | 12 | 12 | 10 | 10 |
| `CoverageRadiusFt` | 7.5 | 6.0 | 5.0 | 4.5 | 4.5 |
| `MinSpacingFt` | 6 | 6 | 5 | 4.5 | 4.5 |
| `MaxCoverageAreaSqFt` | 225 | 130 | 100 | 90 | 90 |
| `ObstacleClearanceFt` | 1.0 | 1.0 | 1.0 | 1.5 | 2.0 |
| `BoundaryClearanceFt` | 1.0 | 1.0 | 1.0 | 1.5 | 2.0 |
| `ExistingSprinklerSeparationFt` | 7.5 | 6.0 | 5.0 | 4.5 | 4.5 |
| `MaxDistanceFromWallsFt` | 7.5 | 6.0 | 5.0 | 4.5 | 4.5 |
| `MinKFactor` | 5.6 | 5.6 | 8.0 | 8.0 | 11.2 |
| `CeilingHeightAdjustmentFactor` | 1.0 | 0.9 | 0.85 | 0.8 | 0.75 |
| Obstacle beam | 1.0 | 1.0 | 1.5 | 2.0 | 2.5 |
| Obstacle column | 1.0 | 1.0 | 1.0 | 1.5 | 2.0 |
| Obstacle duct | 1.5 | 1.5 | 2.0 | 2.5 | 3.0 |

### 3.2 Ceiling factors

`CeilingSlopeAdjustments` (FLAT 1.0, SLOPED 0.9, STEPPED 0.85).

NFPA 13 §11.1 ceiling-height factor (applied by `BruteForceCalculationService.CalculateRoom`):
```
height ≤ 10 ft  → 1.00
height ≤ 12 ft  → 0.95
height ≤ 15 ft  → 0.90
height ≤ 20 ft  → 0.85
height ≤ 25 ft  → 0.80
else → clamp
```
Combined with `ruleSet.CeilingHeightAdjustmentFactor` and `CeilingSlopeAdjustments[SlopeType]`. Applied to BOTH `MaxSpacingFt` and `CoverageRadiusFt`.

### 3.3 Orientation factor (`HazardPlacementRuleSet.OrientationSpacingAdjustments`)

| Orientation | Factor |
|---|---|
| pendent | 1.0 |
| upright | 1.0 |
| sidewall | 0.85 |

Set by `RevitSprinklerFamilySource.ResolveDeviceContext` reading Revit `FamilyPlacementType` then catalog `Mount`, resolved by `DevicePlacementBehaviorResolver`.

### 3.4 Coverage pattern factor (`CoveragePatternAdjustments`)

circular 1.0, rectangular 0.9, square 0.95.

### 3.5 Per-room overrides (`PlacementRoomInput`)

- `OverrideMaxSpacingFt` → clamped to ≤ 15 ft.
- `OverrideBoundaryClearanceFt` → clamped to ≥ 0.
- Both set room `IsProvisional = true`.

### 3.6 Engine config (`BruteForceCalculationConfig`)

- `ToleranceFt` 1e-6
- `GridResolutionFt` 1.0 ft (floored to `CoverageRadiusFt / 2`)
- `MaxCandidatePoints` 4000
- `MaxSearchIterations` 500000

### 3.7 Sidewall branch

Triggered when `SelectedSprinklerPlacementBehavior == WallSidewall` or `SelectedSprinklerOrientation == "sidewall"`.
`BruteForceCalculationService.GenerateSidewallCandidates`: walks the room perimeter with step = `MaxSpacingFt`, standOff = `BoundaryClearanceFt`. No grid.

### 3.8 Sprinkler-specific extras

- BOTH min AND max wall distance (min from `BoundaryClearanceFt`, max from `MaxDistanceFromWallsFt`).
- `MinSpacingFt` enforced separately from `MaxSpacingFt`.
- Drop below ceiling checked again after placement (Revit host Z vs requested Z).
- Beams/ducts/columns each get their own `ObstacleSpecificClearances` lookup — not one shared number.
- Mount direction limits (pendent/upright/sidewall).

---

## 4. Smoke detectors

Driven by: ceiling shape, slope, height. Not hazard class.

On top of shared method:
- spacing reduced on beamed / sloped / high ceilings (use `CeilingSlopeAdjustments` + height factor from §3.2).
- clear of air supply grilles (catalog `DetectorType` + rule map → clearance).
- mount: ceiling OR wall within allowed band near ceiling.

Detector rules come from the `SmokeDetectors` sheet of `Catalog.xlsx` via `CatalogService.GetSmokeDetectorFamilies/TypesForFamily/EntriesForFamily`.

---

## 5. Notification appliances

### 5a. Visible (strobes) — same method as §2

Driven by: room size vs candela rating.

- Each rating covers a tile size → room is tiled.
- Mounting height inside an allowed band.
- Must be visible (no occlusion).
- Corridors: separate case.
- Strobes visible from same spot flash together (synchronization group from catalog).

Catalog source: `NotificationAppliances` sheet, column `Candela`. Lookups via `CatalogService.GetNotificationApplianceFamilies/TypesForFamily/AppliancesForFamily`.

### 5b. Audible (horns, speakers) — different method

Loudness problem, not spacing.

1. Take room, ceiling height, surfaces, assumed background noise.
2. Pick trial positions + dBA rating (catalog column `NotificationDba`).
3. For sample points: device loudness (distance drop, surface absorption, door/wall reduction).
4. Required: loud enough above background noise everywhere.
   not met → raise output, add device, or move it.
5. Report calculated loudness per room.

Shares model reading + Revit placement. Does NOT share location maths.

---

## 6. Models + data classes

### 6.1 DTOs (from Revit extractor)

`RoomData`
- `RoomId`, `ElementId`, `Name`, `Number`
- `LevelId/Name/ElevationFt`
- `AreaSqFt`, `VolumeCuFt?`, `LocationPoint`, `BoundingBox`
- `Boundary` (`BoundaryData`)
- `Hazard` (`HazardResult { Class, MatchedKeyword, RequiresHumanReview }`)
- `Classification` (`ClassificationData { HazardClass, SuggestedByClassifier, Overridden, ConfirmedBy }`)
- `Geometry` (`GeometryData { Polygon, CeilingHeightFt?, CeilingType }`)
- `Ceilings`, `AssociatedObstacleIds`, `RequiresHumanReview`, `Warnings`

`BoundaryData` — `OuterLoop`, `InnerLoops`, `Polygon`; segments: `SegmentType { Line, Arc, Other }`.

`CeilingData` — `ElementId`, `LevelId/Name`, `CeilingName`, `FamilyName/TypeName`, `Category`, `BoundingBox`, `BottomElevationFt?`, `TopElevationFt?`, `HeightAboveLevelFt?`, `SlopeType` (default `"FLAT"`), `SlopeDegrees?`, `BoundaryPolygon`, `ThicknessFt?`, `IsRoomDirectCeiling`, `SourceReferenceData { DocumentTitle, DocumentPath, LinkInstanceId, LinkName, IsFromLink }`.

`ObstacleData` — `ElementId`, `Name`, `Category`, `StructuralType`, `LevelId`, `BoundingBox`, `CenterPoint`, `DimensionsFt`, `AssociatedRoomIds`.

`ExistingSprinklerData` — `ElementId`, `FamilyName`, `TypeName`, `Location` (`Point3DData`), `LevelId/Name/ElevationFt`, `RoomId/Name`, `HostElementId/Name`, `Orientation` (default `(0,0,-1)`), `MountingType` (default `"Pendent"`), `BoundingBox`.

`CoordinateSystemInfo` — `Canonical` `"host_mep_model"`, `LengthUnit` `"feet"`.

`TransformData` — `IsIdentity`, `Origin`, `BasisX/Y/Z`, `Scale` (default 1.0).

### 6.2 Placement snapshot (`PlacementInputSnapshot`)

`SchemaVersion="1.0"`, `TimestampUtc`, `Units`, `CoordinateSystem`, `Project`, `Sprinkler` (`SelectedSprinklerInfo`), `TotalRoomsSelected`, `TotalAreaSqFt`, `Rooms : List<PlacementRoomInput>`.

`PlacementRoomInput`:
- Level: `LevelId`, `LevelName`, `LevelElevationFt`
- Identity: `RoomId`, `RoomName`, `RoomNumber`
- Geometry: `AreaSqFt`, `VolumeCuFt`, `CeilingHeightFt`, `CeilingType`, `BoundaryPolygon`, `Boundary`, `Ceilings`
- Context: `Obstacles`, `ExistingSprinklers`
- Hazard: `EffectiveHazardClass`
- Per-row sprinkler: `SelectedSprinklerFamilyName`, `SelectedSprinklerTypeName`, `SelectedSprinklerFamilyPlacementType`, `SelectedSprinklerPlacementBehavior` (enum), `SelectedSprinklerOrientation` (`"pendent"|"upright"|"sidewall"`)
- Per-row overrides: `OverrideMaxSpacingFt`, `OverrideBoundaryClearanceFt`
- `Source`

`SelectedSprinklerInfo` — `FamilyName`, `TypeName`, `FamilyPlacementType`, `PlacementBehavior` (default `Unknown`).

`DevicePlacementContext` — `FamilyName`, `TypeName`, `FamilyPlacementType`, `PlacementBehavior`, `Resolved`, `FailureReason`, `Mount`.

`DevicePlacementBehavior` enum: `Unknown, CeilingOverhead, WallSidewall, FaceHosted, WorkPlaneDependent, LevelHosted, Unsupported`.

---

## 7. Placement calculation

`BruteForceCalculationService.Calculate(snapshot, rules, config)` → `BruteForceCalculationResult`.

Per-room method: `CalculateRoom(...)`. Steps:

1. `ParseHazardClass` → get `HazardPlacementRuleSet` via `rules.GetRules(hazard)`.
2. `ApplyPerRoomOverrides` → clamp `OverrideMaxSpacingFt ≤ 15`, `OverrideBoundaryClearanceFt ≥ 0`, mark `IsProvisional = true`.
3. `SelectPrimaryCeiling` → FLAT > SLOPED > STEPPED, level-matched, highest Z.
4. Apply ceiling-height + slope + class factors → adjusted `MaxSpacingFt`, `CoverageRadiusFt`.
5. Apply `OrientationSpacingAdjustments[orientation]`.
6. Branch on sidewall vs grid:
   - grid: `ComputeGridResolution(min(config.GridResolutionFt, CoverageRadiusFt))`; step `MinX..MaxX / MinY..MaxY` via `RoomGeometry`.
   - sidewall: `GenerateSidewallCandidates` (perimeter walk, step = `MaxSpacingFt`, standOff = `BoundaryClearanceFt`).
7. `BuildObstacleBoxes` (with `SpansZ` test).
8. `BuildExistingSprinklerXy`.
9. Filter: `BoundaryClearanceFt`, obstacle clearance (per-category), `ExistingSprinklerSeparationFt`.
10. `SelectFromCandidates` (greedy, coverage radius skip, `MinSpacingFt`).
11. Post-verify: max-spacing warning, max-distance-from-walls warning, coverage-gap grid re-sample.
12. Cap with `config.MaxCandidatePoints` / `MaxSearchIterations`.

Status flags per room:
- `Unsupported` behavior → `InvalidInput`.
- Missing ceiling + hosted family → `MissingCeiling` (blocked).
- Any `IsProvisional` rule → `ReviewRequired`.

---

## 8. Strategy + host resolution

`DevicePlacementBehaviorResolver`:
- `FaceBased` → `FaceHosted`
- `WorkPlaneBased` → `WorkPlaneDependent`, refined by `Mount` (`sidewall`→`WallSidewall`, else `CeilingOverhead`)
- `OneLevelBased` → `LevelHosted`
- else → `Unsupported`

`CeilingHostResolver.FindCeilingHost(doc, xyz, level)` → `CeilingHostLookup { HostFace, Source, LinkInstanceName, CeilingElementId }`.
- Looks at host doc first, then linked docs.
- Prefers downward-facing planar face.
- Internal: `FindCeilingFaceInDocument`, `FindHostFaceReference`, `ExtractSolids`.

Strategies (`IFamilyPlacementStrategy`):

| Class | When | Calls |
|---|---|---|
| `FaceBasedPlacementStrategy` | `FaceBased` | `Document.Create.NewFamilyInstance(hostFace, xyz, BasisX, symbol)`. Refuses with `REQUIRED_HOST_UNAVAILABLE` if no host. |
| `WorkPlaneBasedPlacementStrategy` | `WorkPlaneBased` | Tries ceiling face, else `SketchPlane` through XYZ. Never falls back to level. |
| `LevelBasedPlacementStrategy` | `OneLevelBased` | `NewFamilyInstance(xyz, symbol, level, NonStructural)`. |

`PlacementStatusCodes` (string constants used everywhere): `PlacedAndValid`, `PlacedButInvalid`, `PlacementFailed`, `SkippedDuplicate`, `SkippedRoomHasDevices`, `NonFiniteCoordinates`, `LevelResolutionFailed`, `UnsupportedFamilyPlacement`, `RequiredHostUnavailable`, `WorkPlaneUnavailable`, `InvalidHost`, `RevitCreationFailed`, `PostPlacementValidationFailed`, `Duplicate`, `OutsideRoomBoundary`.

---

## 9. Revit placement

`RevitSprinklerPlacementService.PlaceSprinklers(family, type, calcResult, progress, existingDevicePolicy)`.

`PlaceSinglePoint` flow:
1. Coord sanity (`IsFinite`).
2. Near-duplicate check (`config.DuplicateProximityFt` default 0.25, `SkipNearDuplicates` default true).
3. `ResolveHostLevel` — direct host ElementId → per `RevitLinkInstance` transform → elevation ±0.01 ft → name → host name.
4. `ResolveSymbol(family, type)` — filter `OST_Sprinklers`/`FamilySymbol`; activate if needed; cache `family::type`.
5. Pick `IFamilyPlacementStrategy` by proven `FamilyPlacementType`.
6. `Place(PlacementContext { Document, Symbol, Level, RequestedPoint, FamilyPlacementType, CeilingHostResolver })`.
7. Post-validate: `|instance.Location.Point − requested| > PlacementValidationToleranceFt (0.5)` → `PlacedButInvalid`.
8. `StampTraceability` writes comment: `FireProtection auto-placed <stamp> | spacing X ft | wall Y ft | PROVISIONAL RULES…`.

Room-level policy (`ExistingDevicePolicy`): `SkipRoom` (default) | `ReplaceExisting` | `AddAnyway`. Window `ExistingDeviceZWindowFt` (default 6 ft) for room matching.

One `TransactionGroup` wraps the run; cancellation rolls back.

---

## 10. What differs between device families

| | Sprinklers | Smoke detectors | Strobes | Horns/speakers |
|---|---|---|---|---|
| Driven by | Hazard + sprinkler type | Ceiling shape/slope/height | Room size + candela | Background noise + surfaces |
| Location maths | Divide + centre (or sidewall perimeter) | Divide + centre | Divide + centre | Loudness calc |
| Wall distance | min + max | max | max | n/a |
| Height | ceiling − drop | on/near ceiling | mounting band | mounting band |
| Keep clear of | beams, ducts, columns | air grilles | occluders | doors, walls |
| Uses §2 method | yes | yes | yes | no |

Three of four share the method with a different rules table. Only horns/speakers need separate maths.

---

## 11. Factors that decide a sprinkler's final XYZ (priority order)

1. **Hazard class** → `PlacementRoomInput.EffectiveHazardClass` → `DefaultHazardPlacementRules.GetRules` → `HazardPlacementRuleSet` (spacing, coverage, clearances, K-factor, class height factor).
2. **Primary ceiling** (FLAT > SLOPED > STEPPED, level-matched, highest Z) → `placementZ`.
3. **Ceiling height** → NFPA 13 §11.1 factor (10/12/15/20/25 ft) combined with `CeilingSlopeAdjustments[SlopeType]` and `ruleSet.CeilingHeightAdjustmentFactor`.
4. **Per-room overrides** — `OverrideMaxSpacingFt` ≤ 15, `OverrideBoundaryClearanceFt` ≥ 0; both → `IsProvisional`.
5. **Sprinkler family/type**:
   - Revit `FamilyPlacementType` → strategy.
   - Catalog `Mount` (`DevicePlacementBehaviorResolver.FromResolvedFamily`) → orientation → `GetOrientationAdjustment` (sidewall 0.85).
   - Sidewall branch trigger.
6. **Room geometry** — `RoomGeometry` outer + inner loops → `MinX/MaxX/MinY/MaxY`, `IsPointInsideRoom`, `DistanceToOuterBoundary` → `BoundaryClearanceFt`.
7. **Obstacles** — `ObstacleData.BoundingBox` (or `CenterPoint+DimensionsFt`); per-category clearance from `ObstacleSpecificClearances`; Z-range (`ObstacleBox.SpansZ`).
8. **Existing sprinklers** — `ExistingSprinklerData.Location` → `ExistingSprinklerSeparationFt` (filter), `CoverageRadiusFt` (coverage skip), `DuplicateProximityFt` (post-calc near-duplicate skip).
9. **Linked-model transform** — `RevitLinkInstance.GetTotalTransform()` applied to ceilings, levels, existing sprinklers → all in host MEP space; `IsPointInsideRoom` host-coord guard.
10. **Strategy + host** — `IFamilyPlacementStrategy.Place` + `CeilingHostResolver` (host or linked ceiling, downward normal preferred).
11. **Engine tuning** — `BruteForceCalculationConfig { ToleranceFt=1e-6, GridResolutionFt=1.0, MaxCandidatePoints=4000, MaxSearchIterations=500000 }`.
12. **Post-placement safety** — `RevitSprinklerPlacementConfig { SkipNearDuplicates=true, DuplicateProximityFt=0.25, PlacementValidationToleranceFt=0.5, ExistingDeviceZWindowFt=6.0, ExistingDevicePolicy=SkipRoom }`.

---

## 12. Engineer must supply (per device family)

- Standard + edition + local amendments.
- Room category list.
- Max spacing + area per device, per room category + device type + ceiling condition.
- Min AND max wall distance.
- Min device-to-device distance.
- Clearances from beams / ducts / columns / air grilles.
- Drop below ceiling OR mounting height band.
- What changes on sloped / high / beamed / concealed ceilings.
- Strobes: candela → tile size.
- Horns/speakers: assumed background noise + required margin.

Until supplied, every result is `IsProvisional = true` and the room is flagged `ReviewRequired`.

---

## 13. Class index (quick lookup)

- Entry: `FireProtectionCommand`
- Extract: `FireProtectionExtractionService`, `RoomExtractor`, `LevelExtractor`, `CeilingExtractor`, `ObstacleExtractor`, `ExistingSprinklerExtractor`, `SpatialHelpers`
- Hazard: `HazardClassifier`, `HazardClass`, `HazardResult`
- Catalog: `CatalogService`, `Catalog`, `CatalogLoader`, `CatalogValidator`, `CatalogModels`, `CatalogOptions`, `CatalogValidationResult`, `CatalogIssue`, `CatalogLoadException`, `FireProtectionConfig`
- DTOs: `RoomData`, `ClassificationData`, `GeometryData`, `BoundaryData`, `BoundaryLoopData`, `BoundarySegmentData`, `CeilingData`, `SourceReferenceData`, `ObstacleData`, `ExistingSprinklerData`, `Point3DData`, `BoundingBox3DData`, `CoordinateSystemInfo`, `TransformData`, `HazardData`, `LevelData`, `DocumentInfo`, `ModelSnapshot`, `ExtractionIssue`
- Placement input: `PlacementInputBuilder`, `PlacementInputJsonExporter`, `PlacementInputSnapshot`, `PlacementRoomInput`, `SelectedSprinklerInfo`, `DevicePlacementBehavior`, `DevicePlacementContext`, `PlacementRoomSelection`
- Calc engine: `BruteForceCalculationService`, `BruteForceCalculationConfig`, `DefaultHazardPlacementRules`, `HazardPlacementRuleSet`, `IHazardPlacementRules`, `GeometryMath`, `RoomGeometry`
- Behavior + family: `DevicePlacementBehaviorResolver`, `RevitSprinklerFamilySource`
- Strategies: `FaceBasedPlacementStrategy`, `WorkPlaneBasedPlacementStrategy`, `LevelBasedPlacementStrategy`, `CeilingHostResolver`, `PlacementContext`, `PlacementOutcome`, `PlacementStatusCodes`, `IFamilyPlacementStrategy`
- Revit placement: `RevitSprinklerPlacementService`, `RevitSprinklerPlacementConfig`