# FireProtectionSystem — OpenCode Project Instructions

This repository has a persistent project-memory system (Markdown files maintained for Obsidian and future OpenCode sessions).

## Project Memory

Before making significant changes to this repository, read:

- [[PROJECT_CONTEXT]]
- [[ARCHITECTURE]]
- [[PROGRESS]]
- [[DECISIONS]]
- [[TODO]]

Use [[SESSION_NOTES]] when historical session context is relevant.

## Source of Truth

The actual source code is authoritative for implemented behavior.
Documentation (including these files and `SPRINKLER_POINT_CALCULATION_EXPLAINED.md`) must NOT be
treated as proof that something is implemented.

If documentation and implementation disagree:

1. Identify the disagreement.
2. Treat the current implementation as the source of truth for actual behavior.
3. Record the discrepancy when appropriate (e.g., in [[DECISIONS]] or [[SESSION_NOTES]]).

## Do Not Assume Previous Chat Context

Do not assume previous OpenCode sessions are available.
Use the project Markdown files as persistent context.

## Before Coding

Before making architectural or behavioral changes:

1. Read [[PROJECT_CONTEXT]].
2. Read [[ARCHITECTURE]].
3. Read [[PROGRESS]].
4. Read [[DECISIONS]].
5. Read [[TODO]] when planning future work.

## After Significant Work

Update the appropriate project-memory files:

- [[PROGRESS]] when implementation status changes.
- [[DECISIONS]] when an architectural/design decision is made.
- [[TODO]] when tasks are completed, added, removed, or reprioritized.
- [[SESSION_NOTES]] when important session-specific knowledge should survive future sessions.
- [[ARCHITECTURE]] only when the actual architecture changes.

## Documentation Discipline

Never fabricate project state. Clearly distinguish:

- implemented
- partially implemented
- planned
- uncertain
- obsolete

## Code Safety

Do not make unrelated changes. Respect the existing architecture and project conventions.

When asked to modify code, first understand the existing implementation rather than creating parallel
or duplicate logic. In particular:

- `FireProtection.Backend` references `FireProtection.UI` (one-way). The UI must NOT reference Backend.
- Keep extraction, calculation, and placement as separate, Revit-API-aware only at the edges.
- All linked-model geometry is already normalized to host-MEP coordinates during extraction;
  placement must NOT apply a second coordinate transform.
- The provisional sprinkler spacing (15 ft) is intentional placeholder behavior, not a bug.

## Revit Runtime Distinction

Separate **static verification** (provable by reading code) from **runtime verification**
(requires Revit, an open model, specific families, linked models, and Revit API behavior).
Never claim runtime behavior is verified merely because the code compiles.

## Related Documentation

- [[PROJECT_CONTEXT]]
- [[ARCHITECTURE]]
- [[DECISIONS]]
- [[PROGRESS]]
- [[TODO]]
- [[SESSION_NOTES]]
- [[SPRINKLER_POINT_CALCULATION_EXPLAINED]]
