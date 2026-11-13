# SPA SynthFire — Project Overview & Architecture Memory

## 1. Project Purpose & Scope
- **Standalone Revit Add-in** targeting **Revit 2026** (and 2025 compatible) running on **.NET 8 (Windows)**.
- Replicates [Tandm.ai](https://tandm.ai/au/) capabilities for fire protection engineering:
  1. Automatic Sprinkler Placement (wet fire)
  2. Automatic Fire Detection & Notification Placement (dry fire / NFPA 72)
  3. Ceiling grid & tile snap
  4. Clash avoidance & coordination with MEP/Structure
  5. Per-room hazard classification & configurable standards (NFPA 13, AS 2118, user criteria)
  6. 2D/3D Coverage Plan visualization (HelixToolkit WPF)
  7. Estimation / Quantity Takeoff (BOM export)

---

## 2. Solution Structure & Dependency Flow
```
SPA.SynthFire.Core (Domain, Geometry, Standards, Settings)
       ^
       |-------------+--------------------+
       |             |                    |
SPA.SynthFire.Catalog  SPA.SynthFire.Placement  SPA.SynthFire.Estimator
       ^                     ^
       |                     |
       +----------+----------+
                  |
         SPA.SynthFire.Presentation (ViewModels, MVVM, Contracts)
                  ^
                  |
         SPA.SynthFire.UI (WPF Views, Custom Controls, 3D Viewport)
                  ^
                  |
         SPA.SynthFire.Revit (Revit Addin, Commands, Element Readers/Placers)
```
- **Tests**: `tests/SPA.SynthFire.Tests` (158 passing xUnit tests, headless-first, no Revit dependency needed for testing core/solvers).

---

## 3. Project Responsibilities & Key Types

### `SPA.SynthFire.Core` (net8.0)
- **Geometry**: `Point3D`, `Polygon2D`, `Rectangle2D`, `CeilingHeight`. Internal units are Revit standard feet (`ft` and `sq ft`).
- **Devices**: `DeviceMount` enum (`Ceiling`, `Sidewall`, `Free`).
- **Standards & Codes**:
  - `IDesignStandard`: standard definitions (`StandardRegistry`).
  - `Nfpa13Standard`: NFPA 13 rules (`LightHazard`, `OrdinaryHazard1`, `OrdinaryHazard2`, `ExtraHazard1`, `ExtraHazard2`).
  - `As2118Standard`: AS 2118 Australian fire standard rules.
  - `Nfpa72DetectionRules`: Smoke detectors (30ft spacing, 900 sq ft) and Heat detectors (50ft spacing, 2500 sq ft).
  - `NotificationCoverage`: Candela & wall-mount strobe / horn strobe rules.
  - `DesignCriteriaResolver`: applies `UserDesignCriteria` overlay (strictly restricts/tightens, never relaxes code).
  - `RoomHazardClassifier`: classifies rooms based on name/function.
- **Settings**: `SynthFireSettings`, `SettingsProfile`, `ProfileBook`, `SettingsStore`, `EffectiveRules`.

### `SPA.SynthFire.Placement` (net8.0)
- **Solvers & Engines**:
  - `CeilingGridLayout`: Unified layout engine for sprinklers and detectors. Handles spacing refinement, obstacle avoidance (8-way nudge), ceiling tile snapping (`SnapToTileCenter`), and min-spacing review flags.
  - `SprinklerPlacementSolver`: Drives sprinkler layout per room, per room hazard, and mounting type.
  - `SidewallLayout`: Longest-wall distribution solver for sidewall sprinklers.
  - `CoverageField` & `CoverageValidator`: Evaluates 2D coverage density, unreached areas, and dead zones.
  - `GapInfill`: Fills residual coverage gaps.
  - `CeilingTilePlanner`: Alignment and snapping to architectural ceiling grids.
  - `LevelPreview` & `LevelPreviewBuilder`: Aggregates room-level layout models for visualization.

### `SPA.SynthFire.Catalog` (net8.0)
- `CatalogLoader`, `CatalogValidator`, `CatalogSchema`, `CatalogTemplateWriter`.
- Excel/ClosedXML-based device library (provisional models, K-factors, coverage radii, family-to-device mapping).

### `SPA.SynthFire.Estimator` (net8.0)
- Bill of materials (BOM), device count aggregations, unit cost lookup, CSV export.

### `SPA.SynthFire.Presentation` (net8.0)
- MVVM view models for clean UI decoupling: `SprinklerDesignViewModel`, `DetectorDesignViewModel`, `ObservableObject`, `RelayCommand`.
- Contracts: `IPlacementRunner`, `IDetectorRunner`, `PlacementOptions`.
- `FireAlarmFamilyClassifier`: Differentiates smoke vs heat vs strobes/chimes.

### `SPA.SynthFire.UI` (net8.0-windows)
- WPF windows & panels:
  - `SynthFirePanelControl`: Main dockable control panel.
  - `CoveragePlanWindow` & `CoveragePlan3DWindow`: 2D canvas and HelixToolkit 3D coverage viewers.
  - `PlacementOptionsWindow`, `PlacementReportWindow`: Quick execution & summary dialogs.
  - `HazardClassificationWindow`, `RoomCreationWindow`, `SettingsWindow`.
- Controls: `CoveragePlanCanvas`, `CoveragePlanViewport3D`.

### `SPA.SynthFire.Revit` (net8.0-windows, Revit 2026/2025 API)
- **Add-in Entry**: `SynthFireApplication` (implements `IExternalApplication`), registers ribbons and dockable pane (`SynthFireDockablePane`).
- **Readers**:
  - `RoomReader`: Extracts room footprints (`Polygon2D`/`Rectangle2D`), ceiling elevations, and obstacle bounding boxes (`ReadObstacles` for ducts, pipes, cable trays, structural framing).
  - `SprinklerFamilyReader` & `DetectorFamilyReader`: Inspects loaded Revit families and maps them to mounting types.
  - `HazardRoomReader`: Scans room parameters and hazard classifications.
- **Placers & Runners**:
  - `SprinklerPlacer` & `DetectorPlacer`: Transaction-based family placement in Revit. Sets `Comments = "SPA SynthFire"` for tracked redraw/redesign.
  - `PlacementLevels`: Fixes elevation & floor hosting across multi-level buildings.
  - `HostFinder` & `SidewallWallHost`: Face-hosting and wall-hosting logic.
  - `WarningSwallower`: Suppresses non-fatal Revit transaction warnings.
- **Commands**: `PlaceSprinklersCommand`, `PlaceDetectorsCommand`, `HazardClassificationCommand`, `RoomCreationCommand`, `ClearDevicesCommand`, `SettingsCommand`, `SynthFirePanelCommand`.
