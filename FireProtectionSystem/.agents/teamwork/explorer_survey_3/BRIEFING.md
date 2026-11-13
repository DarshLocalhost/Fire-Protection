# BRIEFING — 2026-11-03T05:58:00Z

## Mission
Investigate codebase architecture, current implementation, and exact file locations for Requirement 3 (Excluded and Special Hazard Areas), Requirement 4 (Minimum Ceiling Offset / Deflector Drop), and Build/Testing configurations.

## 🔒 My Identity
- Archetype: explorer
- Roles: investigation, synthesis
- Working directory: c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\.agents\teamwork\explorer_survey_3
- Original parent: 40812835-a2ee-412c-9039-601455b774d5
- Milestone: Requirements 3 & 4 Exploration (Special Hazards Exclusion & Ceiling Offset / Deflector Drop & Build/Test)

## 🔒 Key Constraints
- Read-only investigation — do NOT implement
- Do NOT write or modify any source code files
- Write only to .agents/teamwork/explorer_survey_3/
- Backend references UI (one-way). UI must NOT reference Backend.

## Current Parent
- Conversation ID: 40812835-a2ee-412c-9039-601455b774d5
- Updated: 2026-11-03T05:51:46Z

## Investigation State
- **Explored paths**:
  - `FireProtection.Backend/Models/Hazard/HazardClassifier.cs`, `HazardClass.cs`, `HazardResult.cs`
  - `FireProtection.Backend/Models/DTOs/RoomData.cs`, `HazardData.cs`
  - `FireProtection.Backend/Services/Model/RoomExtractor.cs`
  - `FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/BruteForceCalculationService.cs`, `Nfpa13RulebookRules.cs`
  - `FireProtection.Backend/Services/Placement/Sprinklers/Final/RevitSprinklerPlacementService.cs`
  - `FireProtection.Backend/Services/Placement/Sprinklers/Final/Strategies/` (`LevelAssociation.cs`, `WorkPlaneBasedPlacementStrategy.cs`, `FaceBasedPlacementStrategy.cs`, `WallSidewallPlacementStrategy.cs`, `LevelBasedPlacementStrategy.cs`)
  - `FireProtection.UI/Services/PlacementEligibilityResult.cs`, `ISprinklerPlacementService.cs`
  - `FireProtection.UI/ViewModels/Sprinklers/BruteForce/RoomItemViewModel.cs`, `SprinklerBruteForceViewModel.cs`
  - `FireProtection.UI/Views/Sprinklers/BruteForce/SprinklerBruteForceView.xaml`
  - `FireProtection.Tests/Program.cs`, `HazardRuleValuesTests.cs`, `Nfpa13RulebookRulesTests.cs`, `SidewallPlacementTests.cs`
  - Solution and project files (`FireProtectionSystem.slnx`, `FireProtection.Backend.csproj`, `FireProtection.UI.csproj`, `FireProtection.Tests.csproj`)
- **Key findings**:
  - Item 1: `HazardClassifier.cs` currently has no special hazard classification helper or enum. A helper `ClassifySpecialHazard(string roomName)` can classify commercial kitchens (UL 300 hood systems), water curtains (deluge nozzles), electrical/server rooms (clean agent systems), and shafts.
  - Item 2: UI integration requires respecting Backend -> UI one-way reference. `PlacementEligibilityResult` in UI can hold `IsSpecialHazard`, `SpecialHazardType`, `SpecialHazardSystem`. `RoomItemViewModel` exposes `IsSpecialHazard` and `IsSpecialHazardOverridden`. `SprinklerBruteForceViewModel` adds `ExcludeSpecializedRooms` (default true) with auto-deselection, while manual room checkbox clicks override exclusion.
  - Item 3: Deflector drop in calculation currently applies 1 inch uniformly in `BruteForceCalculationService.cs:195-216` regardless of sidewall vs pendent. Sidewall heads require 4 to 6 inches (default 4 in), while pendent uses 1 to 12 inches (default 1-2 in). In placement, `LevelAssociation.EnforceAndVerify` sets `INSTANCE_FREE_HOST_OFFSET_PARAM` and `INSTANCE_ELEVATION_PARAM` to `targetWorldZ - levelElevation`.
  - Item 4: Solution uses `FireProtectionSystem.slnx`. Both `Revit2025` and `Revit2026` configurations build cleanly (0 errors) on .NET 8 (`net8.0-windows`). `FireProtection.Tests` is an `Exe` console test harness run via `dotnet run --project FireProtection.Tests/FireProtection.Tests.csproj -c Revit2026` (all 19 suites pass).
- **Unexplored areas**: None within scope of Requirements 3 & 4.

## Key Decisions Made
- Confirmed architectural model: UI models hold DTO properties; Backend services populate them.
- Verified both build configs and test harness execution mechanisms.

## Artifact Index
- DISPATCH.md — Incoming dispatch message
- BRIEFING.md — Situational awareness and state
- progress.md — Liveness heartbeat and progress tracking
- handoff.md — Comprehensive 5-component report
