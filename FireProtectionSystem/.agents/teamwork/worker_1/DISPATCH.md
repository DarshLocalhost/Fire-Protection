## 2026-11-03T06:06:31Z
You are a Worker agent. Your working directory is:
c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\.agents\teamwork\worker_1

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

MANDATORY FIRST STEPS:
1. Read the original user request at:
c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\.agents\teamwork\ORIGINAL_REQUEST.md
2. Read project scope and context files:
- c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\.agents\teamwork\PROJECT.md
- c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\AGENTS.md
- c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\PROJECT_CONTEXT.md
- c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\ARCHITECTURE.md
- c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\PROGRESS.md
- c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\DECISIONS.md
- c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\TODO.md
3. Read the 3 Explorer Survey Reports:
- c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\.agents\teamwork\explorer_survey_1\handoff.md
- c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\.agents\teamwork\explorer_survey_2\handoff.md
- c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\.agents\teamwork\explorer_survey_3\handoff.md

YOUR MISSION:
Implement all 4 NFPA 13 enhancements and special hazard controls according to the approved plan and explorer survey specifications:

1. Requirement 1: Small Room Head Count Logic
   - In `GeometryMath.cs`: Implement Green's theorem / shoelace polygon area centroid helper `ComputePolygonCentroid(IReadOnlyList<double[]> polygon, out double cx, out double cy)`. Fallback to bounding box center if degenerate/collinear.
   - In `BruteForceCalculationService.cs`:
     * For rooms with `effectiveArea <= ruleSet.MaxCoverageAreaSqFt` (e.g. 225 sq ft for Light, 130 sq ft for OH1/OH2): place exactly 1 sprinkler at the verified centroid (verified for containment via `geometry.IsPointInsideRoom` and boundary clearance `geometry.DistanceToOuterBoundary >= effectiveBoundary`). If centroid fails containment/clearance or on suspended tile ceiling, snap to nearest valid candidate.
     * If `effectiveArea > ruleSet.MaxCoverageAreaSqFt`: scale up multi-head grid so $nx \times ny \ge 2$ (and $nU \times nV \ge 2$ on tile ceilings) along the longer dimension.
     * Fix the S x L infinity bug in `CheckCoverageBySxL`: when `selected.Count == 1` and neighbours are infinite, use $2.0 \times wallAlongBranch$ and $2.0 \times wallAcrossBranch$ to avoid false `PositiveInfinity` coverage warnings.

2. Requirement 2: Maximum Throw in Sidewall Sprinklers
   - In `Nfpa13RulebookRules.cs`: Add constants `SidewallMaxThrowCapFt = 14.0;` and `SidewallOpposingWallsMaxDepthFt = 28.0;`.
   - In `BruteForceCalculationService.cs` (`SelectSidewallDirectional`):
     * Cap `throwDepth` at `Nfpa13RulebookRules.SidewallMaxThrowCapFt` (14.0 ft).
     * If `roomDepth > throwDepth` (14 ft) up to 28 ft: select opposing anti-parallel wall (`-dot > 0.5`) to place heads on opposing walls.
     * If `roomDepth > 28.0 ft` (or > 14 ft when no opposing wall exists/can cover): set `result.Status = CalculationStatus.ReviewRequired;` and emit warning: `"Room depth ({roomDepth:F1} ft) exceeds maximum sidewall throw (28.0 ft); ceiling-mounted heads are required."` (or appropriate wording for missing opposing wall). Keep non-sidewall/ceiling logic intact.

3. Requirement 3: Excluded and Special Hazard Areas
   - In `HazardClassifier.cs`: Add enum `SpecialHazardType` and helper `ClassifySpecialHazard(string roomName)` returning `SpecialHazardResult`. Classify:
     * Commercial kitchens (UL 300 hood systems)
     * Water curtains (deluge nozzles)
     * Electrical / server rooms (clean agent systems)
     * Shafts (risers/vertical openings)
   - Preserve strict architectural boundary: `FireProtection.Backend` references `FireProtection.UI` (one-way). `FireProtection.UI` must NOT reference Backend!
   - In `FireProtection.UI/Services/PlacementEligibilityResult.cs`: Add `IsSpecialHazard`, `SpecialHazardType`, `SpecialHazardSystem`.
   - In `FireProtection.Backend/.../RevitSprinklerPlacementService.cs` (`EvaluateRoomEligibility`): populate special hazard fields from `HazardClassifier.ClassifySpecialHazard`.
   - In `FireProtection.UI/ViewModels/Sprinklers/BruteForce/RoomItemViewModel.cs`: Expose `IsSpecialHazard`, `IsSpecialHazardOverridden`.
   - In `FireProtection.UI/ViewModels/Sprinklers/BruteForce/SprinklerBruteForceViewModel.cs`: Add `ExcludeSpecializedRooms` (default true). If enabled, specialized rooms default to unselected. Allow manual user override by checking the room row checkbox.
   - In `FireProtection.UI/Views/Sprinklers/BruteForce/SprinklerBruteForceView.xaml`: Add CheckBox toggle ("Exclude specialized/special-hazard rooms", default checked) in the room filter bar.

4. Requirement 4: Minimum Ceiling Offset (Deflector Drop)
   - In `Nfpa13RulebookRules.cs`: Add `DeflectorMinDropSidewallFt = 4.0 / 12.0;` (4 in) and `DeflectorMaxDropSidewallFt = 6.0 / 12.0;` (6 in).
   - In `BruteForceCalculationService.cs`: Evaluate `isSidewall` BEFORE computing `placementZ` deflector drop. Apply:
     * Sidewall heads: 4 to 6 inches (default 4 in = 4/12 ft) below ceiling.
     * Standard pendent / upright heads: 1 to 12 inches (default 1 to 2 in, 1 in default = 1/12 ft) below ceiling.
   - In Revit placement: verify `LevelAssociation.EnforceAndVerify` sets `BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM` / `INSTANCE_ELEVATION_PARAM` using `targetWorldZ - levelElevation`.

5. Unit Tests & Build Verification:
   - In `FireProtection.Tests`: Add comprehensive unit tests covering:
     * Small room 1 head at centroid (area <= 225 Light, <= 130 OH1)
     * Scale up to 2+ heads for area > MaxCoverageAreaSqFt
     * L-shaped room centroid containment fallback
     * Single-head S x L check (no infinity warning)
     * Sidewall throw cap at 14 ft, opposing walls for 14-28 ft, depth > 28 ft review warning
     * Special hazard classification helper for all 4 types
     * Deflector drop (sidewall 4 in vs pendent 1 in)
   - Register any new test suite in `FireProtection.Tests/Program.cs` `_registeredSuites`.
   - Execute builds and tests:
     * `dotnet build FireProtectionSystem.slnx -c Revit2025`
     * `dotnet build FireProtectionSystem.slnx -c Revit2026`
     * `dotnet run --project FireProtection.Tests/FireProtection.Tests.csproj -c Revit2026`
     * `dotnet run --project FireProtection.Tests/FireProtection.Tests.csproj -c Revit2025`
     Ensure 100% of tests pass!
6. Update project memory files per AGENTS.md:
   - `PROGRESS.md`
   - `DECISIONS.md`
   - `TODO.md`

OUTPUT:
Write your complete handoff report to:
`c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\.agents\teamwork\worker_1\handoff.md`
Format with the 5 required sections: Observation, Logic Chain, Caveats, Conclusion, Verification Method.
Maintain `progress.md` in your directory.
Send a completion message to the caller with build/test results when done.


## 2026-11-03T06:19:03Z
**Context**: NFPA 13 Enhancements Implementation
**Content**: Heartbeat check: Checking in on your progress across the implementation steps (Requirements 1-4, tests, and builds). Please update your progress.md with your current status.
**Action**: Continue implementation and report your status.
