# BRIEFING — 2026-11-03T05:48:30Z

## Mission
Supervise the implementation of NFPA 13 enhancements and special hazard controls for FireProtectionSystem.

## 🔒 My Identity
- Archetype: sentinel
- Working directory: c:\Users\darshan.badigera\Documents\test\MEP\FireProtectionSystem\.agents\teamwork\sentinel
- Orchestrator: 40812835-a2ee-412c-9039-601455b774d5
- Victory Auditor: to be spawned on victory claim

## 🔒 Key Constraints
- No technical decisions — relay only
- Victory Audit is MANDATORY before reporting completion
- Small Room Head Count: area <= MaxCoverageAreaSqFt -> 1 head at centroid
- Maximum Throw in Sidewall Sprinklers: cap 14 ft single-wall, 28 ft opposing, flag review warning if exceeded
- Excluded and Special Hazard Areas: classify commercial kitchens, water curtains, electrical/server rooms, shafts; auto-exclusion filter with UI toggle checkbox & manual override
- Minimum Ceiling Offset (Deflector Drop): 1-12 in pendent, 4-6 in sidewall; apply in calculation and enforce via Revit parameters
- Verify with unit tests in FireProtection.Tests
- Build both Revit 2025 and 2026 configurations and ensure all tests pass
- Keep non-sidewall/ceiling logic intact
- Adhere to AGENTS.md rules and project conventions

## User Context
- **Last user request**: Implement the NFPA 13 enhancements and special hazard controls according to the approved plan.
- **Pending clarifications**: none (proceed directly with execution confirmed)
- **Delivered results**: none yet

## Project Status
- **Phase**: in progress (routing to General / teamwork_orchestrator)
- **Routing Decision**: General path (teamwork_orchestrator) — multi-component SWE implementation across backend calculation, Revit placement, hazard classification, UI, and test suites.

## Victory Audit Status
- **Triggered**: no
- **Verdict**: pending
- **Retry count**: 0

## Artifact Index
- .agents/teamwork/ORIGINAL_REQUEST.md — Original request and parameters record
