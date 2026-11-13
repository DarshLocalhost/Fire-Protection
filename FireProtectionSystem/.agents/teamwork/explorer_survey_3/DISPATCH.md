## 2026-11-03T05:51:46Z

You are an Explorer agent. Your working directory is:
c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\.agents\teamwork\explorer_survey_3

MANDATORY FIRST STEPS:
1. Read the original user request at:
c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\.agents\teamwork\ORIGINAL_REQUEST.md
2. Read project context files:
- c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\AGENTS.md
- c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\PROJECT_CONTEXT.md
- c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\ARCHITECTURE.md
- c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\PROGRESS.md
- c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\DECISIONS.md
- c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\TODO.md
- c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\SPRINKLER_POINT_CALCULATION_EXPLAINED.md

YOUR MISSION:
Investigate codebase architecture, current implementation, and exact file locations for:
Requirement 3:
"Excluded and Special Hazard Areas:
- Classify commercial kitchens (UL 300 hood systems), water curtains (deluge nozzles), electrical/server rooms (clean agent systems), and shafts.
- Add helper in HazardClassifier.cs and provide an automatic exclusion preflight filter with an explicit UI toggle checkbox ("Exclude specialized/special-hazard rooms", enabled by default) with manual room override."
Requirement 4:
"Minimum Ceiling Offset (Deflector Drop):
- 1 to 12 inches for standard pendent heads (default 1 to 2 in below ceiling).
- 4 to 6 inches below ceiling for sidewall heads (default 4 in).
- Apply drop during calculation (in placementZ) AND enforce via Revit family elevation/offset parameters during Revit placement."
And Build / Testing requirements:
"Verify with unit tests in FireProtection.Tests, build both Revit 2025 and 2026 configurations, and make sure all tests pass. Keep non-sidewall/ceiling logic intact."

SPECIFIC INVESTIGATION ITEMS:
1. Special Hazards in Backend:
   - Check `HazardClassifier.cs`, `HazardClassification` enum, and any room metadata/naming dictionaries.
   - How are commercial kitchens, water curtains, electrical/server rooms, and shafts detected or classified? What helper method should be added to `HazardClassifier.cs`?
   - How does the preflight filter work? Is there an exclusion list, a status/warning, or a flag on room candidates?
2. UI Integration in `FireProtection.UI`:
   - Inspect the WPF views, ViewModels, and controls. Where are rooms listed and selected?
   - How can an explicit UI toggle checkbox ("Exclude specialized/special-hazard rooms", default checked/enabled) be added with manual room override capability (e.g. checkbox in room list or status toggle)?
   - Respect the architectural constraint: `FireProtection.Backend` references `FireProtection.UI` (or whichever direction is specified in AGENTS.md — remember: AGENTS.md states: "FireProtection.Backend references FireProtection.UI (one-way). The UI must NOT reference Backend." Verify the exact project references and ViewModel architecture!).
3. Ceiling Offset (Deflector Drop):
   - Where is `placementZ` computed during calculation (e.g. `SprinklerPointCalculationService`, `SidewallSprinklerCalculator`, etc.)?
   - How is ceiling height extracted and how is deflector drop applied? (1 to 12 inches, default 1-2 in for pendent; 4 to 6 inches, default 4 in for sidewall).
   - How is elevation/offset enforced during Revit placement (e.g. `SprinklerPlacementService.cs`, `INSTANCE_ELEVATION_PARAM`, `INSTANCE_FREE_HOST_OFFSET_PARAM`, or family type parameters)?
4. Build & Test Infrastructure:
   - Check solution configuration (`FireProtectionSystem.slnx` or `.sln`, `.csproj` files).
   - How are Revit 2025 and Revit 2026 configurations defined? What targets and constants exist?
   - How are `FireProtection.Tests` executed (e.g., `dotnet test`)?

OUTPUT:
Write your complete report to:
`c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\.agents\teamwork\explorer_survey_3\handoff.md`
Maintain `progress.md` in your directory.
When finished, send a message to the caller with a concise summary of your findings and the path to your handoff.md.
Do NOT write or modify any source code files. You are an explorer agent.
