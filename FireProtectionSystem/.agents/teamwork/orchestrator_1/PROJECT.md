# Project: NFPA 13 Enhancements & Special Hazard Controls

## Architecture
- **FireProtection.Backend**: Pure calculations, geometry math, hazard classification, NFPA 13 rules, and Revit placement services.
  - References `FireProtection.UI` (one-way).
  - Coordinates normalized to host MEP system.
- **FireProtection.UI**: MVVM WPF presentation layer for sprinkler generation and room preflight checks.
  - Does NOT reference `FireProtection.Backend`.
  - Communicates via UI contracts (`PlacementEligibilityResult`, `RoomUiData`, `ISprinklerPlacementService`).
- **FireProtection.Tests**: Executable test harness (`net8.0-windows`) validating calculation engines, geometry algorithms, and hazard rules across configurations (`Revit2025`, `Revit2026`).

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | Small Room Centroid Placement | Rooms with area <= ruleSet.MaxCoverageAreaSqFt receive exactly 1 head at polygon centroid (Green's theorem) verified for containment and clearance. | M1 | ORIGINAL_REQUEST §1 |
| 2 | Small Room Multi-Head Scaling | Rooms with area > ruleSet.MaxCoverageAreaSqFt scale up to >= 2 heads (nx * ny >= 2, nU * nV >= 2) along longer dimension. | M1 | ORIGINAL_REQUEST §1 |
| 3 | Single-Head S x L Infinite Neighbour Fix | Single-head rooms treat absence of branch neighbours as 2 * wall distance instead of Infinity in CheckCoverageBySxL. | M1 | Survey Explorer 1 |
| 4 | Sidewall Throw Cap | Standard sidewall max throw distance capped at 14.0 ft (Nfpa13RulebookRules.SidewallMaxThrowCapFt). | M2 | ORIGINAL_REQUEST §2 |
| 5 | Sidewall Opposing Walls | Room depth > 14 ft places heads on opposing anti-parallel walls up to 2 * throw = 28.0 ft. | M2 | ORIGINAL_REQUEST §2 |
| 6 | Sidewall Max Depth Review Warning | Room depth > 28 ft (or > 14 ft without opposing wall) sets ReviewRequired and emits warning that room depth exceeds max sidewall throw and ceiling-mounted heads are required. | M2 | ORIGINAL_REQUEST §2 |
| 7 | Special Hazard Classifier | HazardClassifier.ClassifySpecialHazard classifies commercial kitchens (UL 300), water curtains (deluge), electrical/server rooms (clean agent), and shafts. | M3 | ORIGINAL_REQUEST §3 |
| 8 | Special Hazard UI Preflight & Toggle | PlacementEligibilityResult carries special hazard metadata; UI provides "Exclude specialized/special-hazard rooms" toggle (enabled by default) with row-level override. | M3 | ORIGINAL_REQUEST §3 |
| 9 | Sidewall Deflector Drop | Sidewall heads receive 4–6 inches deflector drop (default 4 in = 4/12 ft) below ceiling in placementZ calculation. | M4 | ORIGINAL_REQUEST §4 |
| 10 | Pendent Deflector Drop | Pendent/upright heads receive 1–12 inches deflector drop (default 1–2 in = 1/12 ft) below ceiling in placementZ calculation. | M4 | ORIGINAL_REQUEST §4 |
| 11 | Revit Placement Elevation/Offset Enforcement | LevelAssociation enforces targetWorldZ - levelElevation via BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM / INSTANCE_ELEVATION_PARAM. | M4 | ORIGINAL_REQUEST §4 |
| 12 | Test Suite Verification & Dual Build | Unit tests in FireProtection.Tests verifying all 4 requirements, passing on both Revit 2025 and Revit 2026 configurations. | M5 | ORIGINAL_REQUEST |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Small Room Head Count & Coverage Core | GeometryMath centroid, BruteForceCalculationService single-head centroid branch, multi-head scale up, S x L infinity fix. | none | IN_PROGRESS |
| M2 | Sidewall Maximum Throw & Opposing Walls | Nfpa13RulebookRules sidewall throw constants (14 ft, 28 ft), BruteForceCalculationService sidewall depth & warning logic. | M1 | IN_PROGRESS |
| M3 | Excluded & Special Hazard Areas | HazardClassifier special hazard helper, PlacementEligibilityResult contract, RoomItemViewModel, SprinklerBruteForceViewModel toggle & manual override, SprinklerBruteForceView.xaml. | none | IN_PROGRESS |
| M4 | Minimum Ceiling Offset (Deflector Drop) | Nfpa13RulebookRules deflector drop constants, BruteForceCalculationService isSidewall-aware placementZ drop, Revit LevelAssociation verification. | M1, M2 | IN_PROGRESS |
| M5 | Comprehensive Tests & Dual Build Validation | FireProtection.Tests new suites/tests, registration in Program.cs, dotnet build on Revit2025 and Revit2026, memory docs update. | M1-M4 | IN_PROGRESS |

## Interface Contracts
### HazardClassifier ↔ RevitSprinklerPlacementService ↔ UI
- `SpecialHazardResult HazardClassifier.ClassifySpecialHazard(string roomName)`
- `PlacementEligibilityResult`:
  - `bool IsSpecialHazard { get; set; }`
  - `string SpecialHazardType { get; set; }`
  - `string SpecialHazardSystem { get; set; }`
  - `const string SpecialHazardExcluded = "SPECIAL_HAZARD_EXCLUDED"`
- `SprinklerBruteForceViewModel`:
  - `bool ExcludeSpecializedRooms { get; set; }` (default true)
  - `RoomItemViewModel.IsSpecialHazardOverridden`

### Calculation Engine ↔ Placement
- `placementZ` contains ceiling elevation minus applied deflector drop (1 in pendent, 4 in sidewall).
- `LevelAssociation.EnforceAndVerify(doc, instance, level, targetWorldZ)` enforces instance offset parameter `targetWorldZ - levelElevation`.

## Code Layout
- `FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/GeometryMath.cs`
- `FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/Nfpa13RulebookRules.cs`
- `FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/BruteForceCalculationService.cs`
- `FireProtection.Backend/Models/Hazard/HazardClassifier.cs`
- `FireProtection.Backend/Services/Placement/Sprinklers/Final/RevitSprinklerPlacementService.cs`
- `FireProtection.UI/Services/PlacementEligibilityResult.cs`
- `FireProtection.UI/ViewModels/Sprinklers/BruteForce/RoomItemViewModel.cs`
- `FireProtection.UI/ViewModels/Sprinklers/BruteForce/SprinklerBruteForceViewModel.cs`
- `FireProtection.UI/Views/Sprinklers/BruteForce/SprinklerBruteForceView.xaml`
- `FireProtection.Tests/SmallRoomHeadCountTests.cs` (or additions to `CenteredGridPlacementTests.cs`)
- `FireProtection.Tests/SidewallDirectionalSolverTests.cs`
- `FireProtection.Tests/SpecialHazardTests.cs`
- `FireProtection.Tests/Program.cs`
