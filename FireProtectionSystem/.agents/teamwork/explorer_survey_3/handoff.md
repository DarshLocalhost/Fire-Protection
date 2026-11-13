# Handoff Report — Requirement 3, Requirement 4 & Build/Test Infrastructure Survey

**Agent**: `explorer_survey_3`  
**Date**: 2026-11-03  
**Target Milestone**: NFPA 13 Enhancements & Special Hazard Controls (Requirements 3 & 4)

---

## 1. Observation

### 1.1 Special Hazards in Backend
- **Existing File**: `FireProtection.Backend/Models/Hazard/HazardClassifier.cs` (lines 7–140)
  - `HazardClassifier` contains a static mapping array `Map` of `Tuple<string, HazardClass>` (lines 9–90) and an `AlwaysReview` array (lines 92–101) containing keywords `"storage"`, `"warehouse"`, `"rack"`, `"high bay"`, `"godown"`, `"server"`, `"data"`.
  - Currently, `HazardClassifier.ClassifyByName(string roomName)` only maps rooms to `HazardClass` (`Light`, `OH1`, `OH2`, `EH1`, `EH2`).
  - `"kitchen"` is mapped to `HazardClass.OH1` (line 39), `"electrical room"` is mapped to `HazardClass.OH1` (line 47).
  - There is currently **no classification or helper method** for specialized areas or non-sprinkler suppression systems (UL 300 hood systems, deluge water curtains, clean agent systems, or vertical shafts).
  - There is **no `HazardClassification` enum** in the codebase. The only hazard enum is `FireProtection.Backend.Models.Hazard.HazardClass` (lines 3–10: `Light = 1, OH1 = 2, OH2 = 3, EH1 = 4, EH2 = 5`).
- **Extraction Pipeline**: `FireProtection.Backend/Services/Model/RoomExtractor.cs` (lines 118, 227–235)
  - Line 118: `HazardResult hazard = HazardClassifier.ClassifyByName(room.Name);`
  - Lines 227–235: Instantiates `HazardData` on `RoomData.Hazard`.
  - `FireProtection.Backend/Models/DTOs/HazardData.cs` holds `HazardClass`, `MatchedKeyword`, `MatchedTerms`, `RequiresReview`, `Overridden`, `ConfirmedBy`. It currently has no fields for special hazard systems or exclusions.

### 1.2 UI Integration & Architectural Constraints
- **Architectural Boundary**:
  - `FireProtection.Backend.csproj` line 163: `<ProjectReference Include="..\FireProtection.UI\FireProtection.UI.csproj" />`.
  - `FireProtection.UI.csproj` has **no ProjectReference to `FireProtection.Backend`**.
  - `AGENTS.md` and `DECISIONS.md` (Decision 003): *"FireProtection.Backend references FireProtection.UI (one-way). The UI must NOT reference Backend."*
  - Therefore, `FireProtection.UI` **cannot directly reference `HazardClassifier`**. Any special hazard data consumed by UI must cross via UI service contracts or DTOs defined in `FireProtection.UI`.
- **Preflight Eligibility Pipeline**:
  - `FireProtection.UI/Services/PlacementEligibilityResult.cs` (lines 13–128): Defines `PlacementEligibilityResult` with properties `EligibilityState` (`ELIGIBLE`, `BLOCKED`, `UNDETERMINED`), `Reason`, `StatusCode`, `ValidCandidateCount`, etc.
  - `FireProtection.UI/Services/ISprinklerPlacementService.cs` (lines 45–49): Defines `EvaluateRoomEligibility(RoomUiData room, IReadOnlyList<CalculatedSprinklerPoint> candidates, string selectedFamilyName, string selectedTypeName)`.
  - `FireProtection.Backend/Services/Placement/Sprinklers/Final/RevitSprinklerPlacementService.cs` (lines 393–585): Implements `EvaluateRoomEligibility`. It performs read-only checks on geometry, family symbol resolution, level resolution, and ceiling hosting.
- **WPF Views & ViewModels**:
  - `FireProtection.UI/ViewModels/Sprinklers/BruteForce/RoomItemViewModel.cs`:
    - Line 141: `public bool IsEligible => _isEligible;`
    - Line 144: `public bool IsBlocked => string.Equals(_eligibilityState, EligibilityStates.Blocked, ...);`
    - Line 248: `public bool IsSelected { get => _isSelected; set { ... SelectionChanged?.Invoke(...); } }`
    - Line 202: `public void SetEligibility(PlacementEligibilityResult result)` sets `_isEligible`, `_eligibilityState`, `_eligibilityReason`, `_eligibilityStatusCode`.
  - `FireProtection.UI/ViewModels/Sprinklers/BruteForce/SprinklerBruteForceViewModel.cs`:
    - Lines 1634–1725: `RunEligibilityPass()` iterates `AllRooms`, calls `_sprinklerPlacementService.EvaluateRoomEligibility`, and applies selection normalization:
      ```csharp
      1714: if (!roomVm.IsEligible)
      1715:     roomVm.IsSelected = false;
      1716: else if (!wasEligible)
      1717:     roomVm.IsSelected = true;
      ```
  - `FireProtection.UI/Views/Sprinklers/BruteForce/SprinklerBruteForceView.xaml`:
    - Lines 335–342: Room filter section currently has a single checkbox:
      ```xaml
      <CheckBox Grid.Column="0"
                Content="Eligible rooms only"
                IsChecked="{Binding ShowEligibleRoomsOnly, Mode=TwoWay}"
                VerticalAlignment="Center"
                Foreground="{StaticResource TextSecondaryBrush}"
                FontSize="12"
                Margin="0,0,16,0"/>
      ```
    - Lines 545–550: Room row checkbox:
      ```xaml
      <CheckBox Grid.Column="0"
                IsChecked="{Binding IsSelected, Mode=TwoWay}"
                IsEnabled="{Binding IsEligible}"
                VerticalAlignment="Center"
                ToolTip="{Binding EligibilityReason}"/>
      ```
      Notice: `IsEnabled` is bound directly to `IsEligible`. If a room is marked non-eligible / blocked, the checkbox is disabled unless override logic is provided.

### 1.3 Ceiling Offset (Deflector Drop) in Calculation & Placement
- **Calculation in `BruteForceCalculationService.cs`**:
  - Lines 141–192: `placementZ` is initialized from ceiling elevation (`bestCeiling.BottomElevationFt.Value` or fallback `room.LevelElevationFt + room.CeilingHeightFt.Value`).
  - Lines 195–222: Deflector drop below ceiling:
    ```csharp
    double maxDeflectorDrop = constructionClass == CeilingConstructionClass.Obstructed
        ? Nfpa13RulebookRules.DeflectorMaxDropObstructedFt
        : Nfpa13RulebookRules.DeflectorMaxDropUnobstructedFt;

    double appliedDrop = Nfpa13RulebookRules.DeflectorMinDropUnobstructedFt;
    if (appliedDrop > maxDeflectorDrop) appliedDrop = maxDeflectorDrop;

    if (!ceilingUnsupported)
    {
        placementZ -= appliedDrop;
    }
    ```
  - Line 209: `appliedDrop` is hardcoded to `Nfpa13RulebookRules.DeflectorMinDropUnobstructedFt` (which is `1.0 / 12.0` = 1 inch).
  - Lines 355–358: `isSidewall` is determined:
    ```csharp
    bool isSidewall =
        room.SelectedSprinklerPlacementBehavior == DevicePlacementBehavior.WallSidewall
        || string.Equals(room.SelectedSprinklerOrientation, "sidewall", StringComparison.OrdinalIgnoreCase);
    ```
  - **Defect/Gap**: `isSidewall` is evaluated at line 355, **after** `appliedDrop` has already been computed and subtracted at line 214. Consequently, sidewall heads currently receive the 1-inch pendent drop rather than the required 4–6 inches (default 4 in).
- **Rulebook Constants in `Nfpa13RulebookRules.cs`**:
  - Lines 80–88:
    - `DeflectorMinDropUnobstructedFt = 1.0 / 12.0` (1 inch)
    - `DeflectorMaxDropUnobstructedFt = 12.0 / 12.0` (12 inches)
    - `DeflectorMinDropObstructedFt = 1.0 / 12.0` (1 inch)
    - `DeflectorMaxDropObstructedFt = 6.0 / 12.0` (6 inches)
  - There are currently **no sidewall deflector constants** (e.g. `DeflectorMinDropSidewallFt = 4.0 / 12.0`, `DeflectorMaxDropSidewallFt = 6.0 / 12.0`).
- **Placement Enforcement in `LevelAssociation.cs` & Placement Strategies**:
  - `FireProtection.Backend/Services/Placement/Sprinklers/Final/Strategies/LevelAssociation.cs` (lines 14–58):
    - Calculates `desiredOffset = targetWorldZ - levelElevation;`.
    - Line 45: Sets `BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM` to `desiredOffset`.
    - Line 52: Falls back to setting `BuiltInParameter.INSTANCE_ELEVATION_PARAM` to `desiredOffset`.
  - `WorkPlaneBasedPlacementStrategy.cs` (lines 35–42):
    - Calls `LevelAssociation.EnforceAndVerify(doc, faceInstance, context.Level, placeAt.Z)`.
    - Sets `offsetParam.Set(0.0)` for face-hosted instances (to prevent shifting away from the host face).
  - `WallSidewallPlacementStrategy.cs` (lines 94, 115, 136):
    - Calls `LevelAssociation.EnforceAndVerify(doc, instance, context.Level, xyz.Z)`.
  - `LevelBasedPlacementStrategy.cs` (line 28):
    - Calls `LevelAssociation.EnforceAndVerify(context.Document, instance, context.Level, context.RequestedPoint.Z)`.

### 1.4 Solution, Configurations, and Test Harness
- **Solution File**: `FireProtectionSystem.slnx`
  - Targets: `Debug`, `Release`, `Revit2024`, `Revit2025`, `Revit2026` across `AnyCPU` and `x64`.
  - Projects: `FireProtection.Backend`, `FireProtection.UI`, `FireProtection.Tests`.
- **Target Frameworks**:
  - `Revit2025`: `net8.0-windows`, compiles with `REVIT_2025` constant, references Revit 2025 API DLLs (`C:\Program Files\Autodesk\Revit 2025\RevitAPI.dll` & `RevitAPIUI.dll`).
  - `Revit2026`: `net8.0-windows`, compiles with `REVIT_2026` constant, references Revit 2026 API DLLs (`C:\Program Files\Autodesk\Revit 2026\RevitAPI.dll` & `RevitAPIUI.dll`).
- **Build Execution Results**:
  - `dotnet build FireProtectionSystem.slnx -c Revit2025`: **Exited 0, 0 Errors**, 2 warnings (benign MSB3277 VisualBasic unification).
  - `dotnet build FireProtectionSystem.slnx -c Revit2026`: **Exited 0, 0 Errors**, 2 warnings (benign MSB3277 VisualBasic unification).
- **Test Harness Execution**:
  - `FireProtection.Tests.csproj` has `<OutputType>Exe</OutputType>`. It is an executable console harness, not an MSTest/xUnit test sdk project.
  - Running `dotnet test` exits with code 0 without executing any tests because no test runner adapter is registered.
  - Execution command: `dotnet run --project FireProtection.Tests/FireProtection.Tests.csproj -c Revit2026` (or `-c Revit2025`).
  - Output: Runs all 19 test suites sequentially; all 19 pass (`ALL TESTS PASSED`, exit code 0).
  - `Program.cs` line 1078 verifies suite registration via `ReportSuiteCoverage()`: any newly added test suite that inherits `*Tests` must be explicitly added to `_registeredSuites`, otherwise the run fails loud.

---

## 2. Logic Chain

### 2.1 Special Hazards Architecture & Preflight Exclusion Filter (Requirement 3)
1. **Classification Domain & Helper**:
   - The user requires classifying four distinct specialized areas:
     1. Commercial kitchens (UL 300 hood systems)
     2. Water curtains (deluge nozzles)
     3. Electrical/server rooms (clean agent systems)
     4. Shafts (risers/vertical openings)
   - In `HazardClassifier.cs`, add enum `SpecialHazardType`:
     ```csharp
     public enum SpecialHazardType
     {
         None = 0,
         CommercialKitchen,      // UL 300 hood systems
         WaterCurtain,           // Deluge nozzles
         ElectricalServerRoom,   // Clean agent systems
         Shaft                   // Shafts / vertical openings
     }
     ```
   - Add result DTO `SpecialHazardResult`:
     ```csharp
     public class SpecialHazardResult
     {
         public bool IsSpecialHazard => HazardType != SpecialHazardType.None;
         public SpecialHazardType HazardType { get; set; }
         public string SystemType { get; set; }
         public string MatchedKeyword { get; set; }
         public string Description { get; set; }
     }
     ```
   - Add helper method `public static SpecialHazardResult ClassifySpecialHazard(string roomName)`:
     - Match case-insensitive room name against dictionaries:
       - Commercial Kitchen: `"commercial kitchen"`, `"kitchen"`, `"cooking"`, `"culinary"`, `"hood"`, `"galley"`, `"bakery"`, `"canteen"`, `"cafeteria kitchen"` -> `SpecialHazardType.CommercialKitchen`, system `"UL 300 hood system"`.
       - Water Curtain: `"water curtain"`, `"deluge"`, `"drencher"`, `"opening protection"`, `"fire curtain"`, `"glass protection"` -> `SpecialHazardType.WaterCurtain`, system `"Deluge nozzle system"`.
       - Electrical / Server: `"server"`, `"data center"`, `"datacenter"`, `"data room"`, `"electrical room"`, `"elec room"`, `"electrical closet"`, `"telecom"`, `"it room"`, `"idf"`, `"mdf"`, `"ups"`, `"switchgear"`, `"transformer"`, `"clean agent"`, `"fm200"`, `"fm-200"`, `"novec"`, `"inergen"` -> `SpecialHazardType.ElectricalServerRoom`, system `"Clean agent system"`.
       - Shaft: `"shaft"`, `"pipe shaft"`, `"duct shaft"`, `"elevator shaft"`, `"lift shaft"`, `"stair shaft"`, `"chute"`, `"trash chute"`, `"linen chute"`, `"vertical shaft"`, `"riser shaft"`, `"mep shaft"`, `"service shaft"` -> `SpecialHazardType.Shaft`, system `"Shaft / vertical opening exclusion"`.

2. **Crossing the UI Boundary (Dependency Invariant)**:
   - Because `FireProtection.UI` does not reference `FireProtection.Backend`, `FireProtection.UI` cannot instantiate or call `HazardClassifier`.
   - The data must be projected across the existing UI contract:
     - In `FireProtection.UI/Services/PlacementEligibilityResult.cs`, add:
       - `public bool IsSpecialHazard { get; set; }`
       - `public string SpecialHazardType { get; set; }`
       - `public string SpecialHazardSystem { get; set; }`
       - `public const string SpecialHazardExcluded = "SPECIAL_HAZARD_EXCLUDED";`
     - In `FireProtection.Backend/.../RevitSprinklerPlacementService.cs` (`EvaluateRoomEligibility`):
       - Call `SpecialHazardResult sh = HazardClassifier.ClassifySpecialHazard(room?.Name);`
       - Populate `result.IsSpecialHazard = sh.IsSpecialHazard;`, `result.SpecialHazardType = sh.HazardType.ToString();`, `result.SpecialHazardSystem = sh.SystemType;`.

3. **UI Integration & Manual Room Override**:
   - In `RoomItemViewModel.cs`:
     - Add `public bool IsSpecialHazard { get; private set; }`
     - Add `public string SpecialHazardType { get; private set; }`
     - Add `public string SpecialHazardSystem { get; private set; }`
     - Add `public bool IsSpecialHazardOverridden { get; set; }`
     - In `SetEligibility(PlacementEligibilityResult result)`, assign these fields and notify property changes.
     - Update `CanSelectRoom`: A special hazard room is physically eligible (`IsEligible == true`), but flagged.
     - If the user explicitly checks `IsSelected = true` on a special hazard room row, set `IsSpecialHazardOverridden = true`.
   - In `SprinklerBruteForceViewModel.cs`:
     - Add property:
       ```csharp
       private bool _excludeSpecializedRooms = true; // Enabled by default per Requirement 3
       public bool ExcludeSpecializedRooms
       {
           get => _excludeSpecializedRooms;
           set
           {
               if (SetProperty(ref _excludeSpecializedRooms, value))
               {
                   ApplySpecialHazardExclusionFilter();
               }
           }
       }
       ```
     - In `ApplySpecialHazardExclusionFilter()` and `RunEligibilityPass()`:
       - For rooms where `IsSpecialHazard && !IsSpecialHazardOverridden`:
         - If `ExcludeSpecializedRooms == true` -> `IsSelected = false`.
         - If `ExcludeSpecializedRooms == false && IsEligible` -> `IsSelected = true`.
       - For rooms where `IsSpecialHazardOverridden == true`: preserve user's manual selection.
   - In `SprinklerBruteForceView.xaml`:
     - Add CheckBox in the room filter toolbar (next to "Eligible rooms only"):
       ```xaml
       <CheckBox Grid.Column="1"
                 Content="Exclude specialized/special-hazard rooms"
                 IsChecked="{Binding ExcludeSpecializedRooms, Mode=TwoWay}"
                 VerticalAlignment="Center"
                 Foreground="{StaticResource TextSecondaryBrush}"
                 FontSize="12"
                 Margin="0,0,16,0"
                 ToolTip="Automatically excludes commercial kitchens (UL 300), water curtains (deluge), electrical/server rooms (clean agent), and shafts. Check a room to manually override."/>
       ```

### 2.2 Minimum Ceiling Offset / Deflector Drop (Requirement 4)
1. **Rulebook Constants in `Nfpa13RulebookRules.cs`**:
   - Add sidewall deflector constants:
     ```csharp
     // Chapter 19 p.234 / NFPA 13 — Sidewall deflector distance below ceiling
     public const double DeflectorMinDropSidewallFt = 4.0 / 12.0; // 4 inches min (default)
     public const double DeflectorMaxDropSidewallFt = 6.0 / 12.0; // 6 inches max
     ```
2. **Calculation in `BruteForceCalculationService.cs`**:
   - Move `isSidewall` determination to the beginning of ceiling Z resolution (before line 195).
   - In deflector drop calculation:
     - If `isSidewall`:
       - `minDrop = Nfpa13RulebookRules.DeflectorMinDropSidewallFt;` (4 in = 0.3333 ft)
       - `maxDeflectorDrop = Nfpa13RulebookRules.DeflectorMaxDropSidewallFt;` (6 in = 0.5 ft)
       - `appliedDrop = minDrop;` (default 4 in)
     - Else (standard pendent / upright):
       - `minDrop = Nfpa13RulebookRules.DeflectorMinDropUnobstructedFt;` (1 in = 0.0833 ft)
       - `maxDeflectorDrop = constructionClass == CeilingConstructionClass.Obstructed ? Nfpa13RulebookRules.DeflectorMaxDropObstructedFt : Nfpa13RulebookRules.DeflectorMaxDropUnobstructedFt;`
       - `appliedDrop = minDrop;` (default 1 to 2 in, 1 in default)
     - If catalog type provides `DeflectorToCeilingIn.HasValue`: clamp between `[minDrop, maxDeflectorDrop]`.
     - Apply: `placementZ -= appliedDrop;`
     - Report in diagnostics: `$"Deflector drop: {appliedDrop * 12.0:F1} in below the mounting plane (band {minDrop * 12.0:F0}-{maxDeflectorDrop * 12.0:F0} in, type = {(isSidewall ? "sidewall" : "pendent")}, ceiling construction = {constructionClass})."`
3. **Revit Placement Enforcement**:
   - `LevelAssociation.EnforceAndVerify` already computes `desiredOffset = targetWorldZ - levelElevation` and sets `BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM` or `BuiltInParameter.INSTANCE_ELEVATION_PARAM`.
   - Because `targetWorldZ` is `point.Z = placementZ` (which already has the 1 in pendent or 4 in sidewall drop subtracted), setting `desiredOffset` on the instance guarantees the placed head's elevation reflects the deflector drop.
   - For `WorkPlaneBasedPlacementStrategy`: when placing on a face, verify that `placeAt.Z` preserves `xyz.Z` (the dropped deflector Z) rather than snapping to the un-dropped face, and ensure offset parameters are correctly set.

---

## 3. Caveats

1. **Revit Runtime Environment**:
   - Static analysis, compilation, and the headless test runner (`FireProtection.Tests`) verify calculation logic, DTO mapping, and build success. Live Revit runtime placement (modifying a `.rvt` document inside an interactive Revit process) requires an interactive Revit session with loaded families.
2. **Revit 2024 Build**:
   - While `Revit2025` and `Revit2026` compile with .NET 8 (`net8.0-windows`), `Revit2024` targets .NET Framework 4.8 (`net48`) and references Revit 2024 API assemblies. Building Revit 2024 requires Revit 2024 DLLs at `C:\Program Files\Autodesk\Revit 2024\`.
3. **Room Naming Variations**:
   - Room names in user models vary widely across languages and architectural firms. The keyword dictionary for special hazards covers the major industry terms, but manual override in the UI is necessary to allow users to handle unlisted or edge-case room names.

---

## 4. Conclusion

1. **Special Hazards (Requirement 3)**:
   - Introduce `SpecialHazardType` enum and `ClassifySpecialHazard` helper in `FireProtection.Backend/Models/Hazard/HazardClassifier.cs` covering commercial kitchens (UL 300), water curtains (deluge), electrical/server rooms (clean agent), and shafts.
   - Extend `PlacementEligibilityResult` in `FireProtection.UI` to carry `IsSpecialHazard`, `SpecialHazardType`, and `SpecialHazardSystem`.
   - Extend `RoomItemViewModel` with `IsSpecialHazard` and `IsSpecialHazardOverridden`.
   - Add `ExcludeSpecializedRooms` (default `true`) to `SprinklerBruteForceViewModel` and wire the CheckBox in `SprinklerBruteForceView.xaml`.
   - Maintain one-way dependency (Backend -> UI) with zero UI back-reference to Backend.

2. **Ceiling Offset / Deflector Drop (Requirement 4)**:
   - Add `DeflectorMinDropSidewallFt` (4.0 / 12.0) and `DeflectorMaxDropSidewallFt` (6.0 / 12.0) to `Nfpa13RulebookRules.cs`.
   - In `BruteForceCalculationService.cs`, evaluate `isSidewall` before calculating `placementZ` drop. For sidewalls, apply 4–6 inches (default 4 in); for pendent heads, apply 1–12 inches (default 1–2 in).
   - In Revit placement, `LevelAssociation.EnforceAndVerify` enforces the elevation/offset parameters using `targetWorldZ - levelElevation`.

3. **Build & Test Infrastructure**:
   - Solution `FireProtectionSystem.slnx` builds clean for both `Revit2025` and `Revit2026` (0 errors).
   - Tests in `FireProtection.Tests` are executed via `dotnet run --project FireProtection.Tests/FireProtection.Tests.csproj -c Revit2026` (or `-c Revit2025`). All 19 existing test suites pass.

---

## 5. Verification Method

### 5.1 Build Verification Commands
```powershell
# Build Revit 2025 configuration
dotnet build FireProtectionSystem.slnx -c Revit2025

# Build Revit 2026 configuration
dotnet build FireProtectionSystem.slnx -c Revit2026
```
**Expected Outcome**: 0 Errors, 2 MSB3277 warnings (benign VisualBasic version resolution from RevitAPI).

### 5.2 Test Execution Commands
```powershell
# Run all unit tests under Revit 2026
dotnet run --project FireProtection.Tests/FireProtection.Tests.csproj -c Revit2026

# Run all unit tests under Revit 2025
dotnet run --project FireProtection.Tests/FireProtection.Tests.csproj -c Revit2025
```
**Expected Outcome**: All suites report PASS, `ALL TESTS PASSED`, exit code 0.

### 5.3 Code Inspection & Invalidation Conditions
- Inspect `HazardClassifier.cs`: ensure helper classifies all 4 specialized categories.
- Inspect `BruteForceCalculationService.cs`: ensure sidewall heads receive a 4-inch drop and pendent heads receive a 1-inch drop.
- Inspect `SprinklerBruteForceView.xaml`: ensure toggle checkbox "Exclude specialized/special-hazard rooms" is present and enabled by default.
- Invalidation condition: If `FireProtection.UI.csproj` gains a ProjectReference to `FireProtection.Backend.csproj`, or if `dotnet build` fails, the implementation is invalid.
