## 2026-11-03T05:49:45Z

Implement the NFPA 13 enhancements and special hazard controls according to the approved plan:

Context and Requirements:
1. Small Room Head Count Logic:
   - For rooms with area <= ruleSet.MaxCoverageAreaSqFt (e.g. 225 sq ft for Light Hazard, 130 sq ft for OH1/OH2 from DefaultHazardPlacementRules.cs), place exactly 1 sprinkler at the centroid of the room boundary polygon (verified for room containment and boundary clearance) instead of multi-head grid spacing.
   - If room area > MaxCoverageAreaSqFt, scale up to 2+ heads per standard NFPA rules.

2. Maximum Throw in Sidewall Sprinklers:
   - Standard sidewall max throw is 12 to 14 ft (cap at 14.0 ft).
   - If room depth exceeds single-wall throw (14 ft), place heads on opposing walls (up to 2 * throw = 28 ft).
   - If room depth exceeds 28 ft (or single-wall throw when opposing walls cannot cover), flag an error/review warning stating that room depth exceeds maximum sidewall throw and ceiling-mounted heads are required.

3. Excluded and Special Hazard Areas:
   - Classify commercial kitchens (UL 300 hood systems), water curtains (deluge nozzles), electrical/server rooms (clean agent systems), and shafts.
   - Add helper in HazardClassifier.cs and provide an automatic exclusion preflight filter with an explicit UI toggle checkbox ("Exclude specialized/special-hazard rooms", enabled by default) with manual room override.

4. Minimum Ceiling Offset (Deflector Drop):
   - 1 to 12 inches for standard pendent heads (default 1 to 2 in below ceiling).
   - 4 to 6 inches below ceiling for sidewall heads (default 4 in).
   - Apply drop during calculation (in placementZ) AND enforce via Revit family elevation/offset parameters during Revit placement.

Verify with unit tests in FireProtection.Tests, build both Revit 2025 and 2026 configurations, and make sure all tests pass. Keep non-sidewall/ceiling logic intact.

---
Operational Guidelines for Orchestrator:
Working Directory: .agents/teamwork/orchestrator_1/
Project Root: c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem
Original Request: c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\.agents\teamwork\ORIGINAL_REQUEST.md
Please read AGENTS.md, PROJECT_CONTEXT.md, ARCHITECTURE.md, PROGRESS.md, DECISIONS.md, and TODO.md before making changes.
Maintain progress.md and BRIEFING.md in your working directory.
Report completion back when all requirements and tests are implemented and verified.
