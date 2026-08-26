# MASTER PROMPT --- Fire Protection System Production-Grade Rewrite

## Role

You are Claude Code acting as the **lead software architect, senior
C#/.NET/Revit API engineer, test engineer, code reviewer, and migration
engineer** for this repository.

You are not being asked to make a small bug fix.

You are being asked to **fully analyze the existing Fire Protection
System project and evolve/rewrite it into a production-grade
architecture that conforms to the two senior-provided standards PDFs
placed in the project root**.

The standards PDFs are authoritative project requirements.

Do not begin by guessing the architecture. Do not begin by blindly
rewriting files. Do not begin by fixing only the currently visible
sprinkler-placement bug.

First understand the repository and the standards completely, then
create an evidence-based implementation plan, then execute it
incrementally.

------------------------------------------------------------------------

# 1. AUTHORITATIVE INPUTS

The project root contains two PDF documents supplied by the
senior/engineering team.

Before making architectural or behavioral changes:

1.  Locate both PDF standards documents in the repository root.
2.  Read both documents completely.
3.  If your available tooling cannot extract all PDF content reliably,
    use an appropriate PDF/text extraction mechanism or inspect rendered
    pages where necessary.
4.  Do not rely only on filenames or partial snippets.
5.  Extract:
    -   architecture requirements
    -   placement requirements
    -   Revit API requirements
    -   hosting requirements
    -   linked-model requirements
    -   geometry requirements
    -   calculation requirements
    -   validation requirements
    -   UI requirements
    -   diagnostics/reporting requirements
    -   testing requirements
    -   engineering-rule restrictions
    -   naming/terminology requirements
    -   explicit prohibitions
    -   acceptance criteria
6.  Preserve the standards' terminology.
7.  Do not silently replace a standard requirement with your own
    preferred approach.
8.  If the standards are ambiguous or contradictory, record the
    ambiguity and choose the safest implementation only after
    documenting the decision.

Create a traceability mapping:

    Standard requirement
        -> current implementation
        -> gap
        -> proposed implementation
        -> affected files/projects
        -> tests proving compliance

------------------------------------------------------------------------

# 2. FIRST TASK: REPOSITORY FORENSIC ANALYSIS

Before editing production code, inspect the entire repository.

At minimum inspect:

-   solution files
-   all `.csproj` files
-   all source projects
-   all C# files
-   XAML files
-   configuration files
-   JSON schemas/data
-   test projects
-   existing Markdown documentation
-   `AGENTS.md`
-   `README.md`
-   `DECISIONS.md` / `DECISION.md` / `decisions.md` or equivalent
-   `TODO.md`
-   `SESSION_NOTES.md`
-   architecture documents
-   reports
-   scripts
-   build files
-   Revit add-in manifests
-   project references
-   package references
-   existing diagnostics
-   existing placement result models
-   existing JSON exports
-   existing test fixtures

Search the whole repository for:

-   sprinkler
-   placement
-   BruteForce
-   Preliminary
-   Final
-   FamilyInstance
-   FamilySymbol
-   FamilyPlacementType
-   FaceBased
-   WorkPlaneBased
-   OneLevelBased
-   HostBased
-   ceiling
-   Ceiling
-   RevitLinkInstance
-   linked model
-   transform
-   Transform
-   level
-   room
-   boundary
-   obstacle
-   collision
-   duplicate
-   visibility
-   ViewRange
-   underlay
-   transaction
-   NewFamilyInstance
-   Reference
-   CreateLinkReference
-   Geometry
-   Location
-   placement result
-   placement input
-   diagnostics
-   hazard
-   NFPA
-   rules
-   provisional
-   review required

Build an actual dependency/architecture map.

Do not trust existing documentation until verified against source code.

------------------------------------------------------------------------

# 3. CREATE A BASELINE BEFORE REWRITING

Before changing behavior:

1.  Build every applicable project/configuration.
2.  Run all existing tests.
3.  Record current warnings/errors.
4.  Identify environment-specific failures separately from source-code
    failures.
5.  Determine the actual Revit versions/target frameworks used by the
    solution.
6.  Identify which projects can be tested without Revit and which
    require Revit.
7.  Record the baseline.

Do not mislabel an existing environmental RevitAPI reference problem as
a newly introduced defect.

------------------------------------------------------------------------

# 4. PROJECT DOCUMENTATION / TRACKING IS PART OF THE WORK

The repository already contains Markdown files used for project
tracking.

You MUST inspect all existing project-tracking Markdown files in the
root and relevant documentation directories.

Examples include:

-   `AGENTS.md`
-   `README.md`
-   `DECISIONS.md`
-   `DECISION.md`
-   `TODO.md`
-   `SESSION_NOTES.md`
-   `ARCHITECTURE.md`
-   `IMPLEMENTATION_PLAN.md`
-   `STATUS.md`
-   `CHANGELOG.md`
-   reports
-   migration notes
-   project-specific tracking files

Do not create duplicate tracking files when an existing file serves the
purpose.

After each meaningful architectural phase:

1.  Update the relevant existing Markdown tracking files.
2.  Record what was discovered.
3.  Record what was changed.
4.  Record why it was changed.
5.  Record rejected approaches and why they were rejected.
6.  Record validation results.
7.  Record remaining risks.
8.  Record runtime validation still required.
9.  Keep decisions chronological and auditable.

If a tracking file does not exist but the project clearly expects it,
create the minimum necessary file and document why.

Never silently overwrite historical project decisions.

------------------------------------------------------------------------

# 5. REQUIRED INITIAL DELIVERABLES BEFORE MAJOR CODE CHANGES

Create/update project tracking documentation with:

## A. Architecture audit

Document:

-   current solution structure
-   project responsibilities
-   dependency direction
-   major services
-   major models
-   Revit-specific boundaries
-   UI boundaries
-   calculation boundaries
-   persistence/export boundaries
-   current anti-patterns
-   duplicated logic
-   dead code
-   risky coupling

## B. Standards compliance matrix

For every meaningful standard requirement:

  --------------------------------------------------------------------------
  Requirement   Evidence in Current     Gap         Planned     Validation
                Standard    Code                    Change      
  ------------- ----------- ----------- ----------- ----------- ------------

  --------------------------------------------------------------------------

## C. Migration plan

Break the rewrite into safe phases.

Each phase must leave the repository buildable/testable where reasonably
possible.

## D. Architecture decision record

Record the major architectural decisions before implementation.

## E. Risk register

At minimum include:

-   Revit API hosting behavior
-   linked-model geometry
-   coordinate transforms
-   ceiling-face references
-   family compatibility
-   transaction behavior
-   view visibility
-   calculation-vs-placement divergence
-   idempotency
-   performance
-   unsupported geometry
-   missing engineering inputs

Do not proceed to a huge destructive rewrite until these documents
exist.

------------------------------------------------------------------------

# 6. TARGET ARCHITECTURE

The target architecture must enforce a strict separation between:

    Extraction
        ↓
    Normalization
        ↓
    Domain model
        ↓
    Engineering/rule evaluation
        ↓
    Candidate calculation
        ↓
    Geometric validation
        ↓
    Placement instructions
        ↓
    Revit placement adapter
        ↓
    Post-placement validation
        ↓
    Reporting/audit

The exact project names may differ based on the current repository, but
responsibilities must remain separated.

A suitable conceptual architecture is:

    FireProtection.Domain
    FireProtection.Application
    FireProtection.Infrastructure
    FireProtection.Revit
    FireProtection.UI
    FireProtection.Tests

Do not force these names if the existing solution has a better
compatible structure.

The important requirement is dependency direction and responsibility
separation.

------------------------------------------------------------------------

# 7. DOMAIN LAYER

The domain/calculation layer MUST NOT depend on Autodesk.Revit.DB.

It must not directly use:

-   `Document`
-   `Element`
-   `ElementId`
-   `FamilyInstance`
-   `FamilySymbol`
-   `RevitLinkInstance`
-   `Reference`
-   `Transaction`
-   `XYZ`

The calculation engine should operate on normalized domain models.

Examples:

-   model/document metadata
-   levels
-   rooms/spaces
-   room boundaries
-   ceilings
-   ceiling regions
-   obstacles
-   existing sprinklers
-   hazard classification
-   placement rules
-   candidate points
-   validation results
-   placement instructions

The result of calculation should be data/instructions, not Revit API
objects.

------------------------------------------------------------------------

# 8. EXTRACTION / NORMALIZATION

Create a clean extraction boundary.

Revit extraction should:

1.  Read host model data.
2.  Read relevant linked model data.
3.  Convert Revit-specific objects into project-owned data structures.
4.  Preserve source-document identity.
5.  Preserve coordinate-space information.
6.  Apply coordinate transforms exactly once.
7.  Normalize units consistently.
8.  Preserve Revit element IDs as traceable identifiers.
9.  Never hide coordinate transformations.

Every extracted geometric entity should have enough information to
determine:

-   source document
-   host document
-   source coordinate
-   normalized/host coordinate
-   transformation used
-   source element ID

Do not mix linked-model coordinates with host coordinates.

------------------------------------------------------------------------

# 9. LINKED-MODEL ARCHITECTURE

Treat linked architecture as a first-class concept.

Create/retain explicit abstractions for:

-   host document
-   linked document
-   link instance
-   source element
-   source coordinate
-   host coordinate
-   transform

For geometry from a linked model:

    linked coordinates
        ↓
    link transform
        ↓
    host coordinates

Apply transforms exactly once.

Do not use a transform merely because coordinates "look wrong."

Prove the coordinate system with:

-   source point
-   transformed point
-   source element
-   link instance
-   expected host position

Linked-model ceiling resolution must remain distinguishable from
host-model ceiling resolution.

------------------------------------------------------------------------

# 10. ROOM / BOUNDARY MODEL

Room extraction must provide sufficient information for deterministic
calculation.

Support, where required by the standards:

-   valid room boundaries
-   irregular boundaries
-   multiple loops
-   linked architecture where applicable
-   room/space identity
-   room level
-   room elevation
-   ceiling association
-   obstacles
-   existing sprinklers

Do not assume a rectangular room.

Do not use a room's bounding box as a replacement for its actual
boundary when the standards require actual room geometry.

------------------------------------------------------------------------

# 11. CEILING MODEL

Ceiling data must be treated as actual geometry, not merely a
room-height number.

Where applicable support:

-   host ceilings
-   linked ceilings
-   flat ceilings
-   sloped ceilings
-   multiple ceiling regions
-   ceilings with holes/voids
-   ceiling height
-   ceiling underside
-   ceiling surface
-   ceiling face
-   ceiling element identity

Do not choose a ceiling merely because it is the first ceiling returned
by a collector.

Ceiling selection must be deterministic and explainable.

For a candidate point, the resolver should be able to answer:

-   Which ceiling was selected?
-   Why was it selected?
-   Is it in the host or a linked document?
-   What coordinate transform was applied?
-   What is its local/host elevation?
-   Which face was selected?
-   What is the face normal?
-   Is the face geometrically appropriate for the sprinkler?

------------------------------------------------------------------------

# 12. FAMILY RESOLUTION

Family/type selection must be separated from placement.

Resolve:

-   family
-   type
-   symbol
-   actual `FamilyPlacementType`
-   activation state
-   required host behavior
-   compatibility with the placement strategy

Do not infer hosting behavior from the family name.

Do not infer hosting behavior from the word "Hosted."

Do not infer hosting behavior from a screenshot.

Inspect the actual Revit API properties.

If a family is unsupported, return an explicit structured failure.

------------------------------------------------------------------------

# 13. CRITICAL REWRITE: PLACEMENT STRATEGY

Do NOT implement:

    WorkPlaneBased -> LevelBased

as a blanket rule.

Do NOT implement:

    WorkPlaneBased -> FaceBased

as a blanket rule.

Do NOT silently fall back from one incompatible placement mechanism to
another.

The standards require the actual family hosting/placement behavior to
determine the strategy.

Design a strategy boundary such as:

    IFamilyPlacementStrategy

with appropriate implementations, for example:

-   LevelBasedPlacementStrategy
-   FaceBasedPlacementStrategy
-   WorkPlaneBasedPlacementStrategy
-   HostBasedPlacementStrategy

Use the actual `FamilyPlacementType` and actual family capabilities to
select the strategy.

If a family requires a host and no valid host can be resolved:

    PLACEMENT_FAILED
    reason = required host unavailable

Do not create an apparently successful but semantically incorrect
FamilyInstance.

------------------------------------------------------------------------

# 14. FACE-BASED / CEILING HOSTING

For face-based placement:

1.  Resolve the correct ceiling.
2.  Resolve the correct geometric face.
3.  Ensure the reference is valid for the target document.
4.  For linked geometry, use the correct Revit link-reference mechanism
    supported by the API.
5.  Validate face orientation.
6.  Place using the appropriate Revit API overload.
7.  Validate the resulting instance.

For a pendent sprinkler under a ceiling, the relevant underside face
should be selected where required by the standards and actual geometry.

Do not arbitrarily use the top face.

Do not assume `+Z` is always correct.

Use actual face normal and geometry.

If the ceiling is linked, do not pass an invalid linked-document
reference directly into a host-document creation API.

Use the appropriate host/link reference mechanism and prove it works in
the target Revit version.

------------------------------------------------------------------------

# 15. WORK-PLANE-BASED FAMILIES

This requires special care.

Do not assume that:

    WorkPlaneBased == LevelBased

Do not assume that:

    WorkPlaneBased == FaceBased

Determine what the actual selected family requires.

The implementation must:

1.  Inspect the family placement type.
2.  Inspect host/work-plane behavior.
3.  Determine whether a valid work plane can be established.
4.  Resolve the correct plane from actual model geometry.
5.  Use the correct Revit API mechanism.
6.  Verify the resulting host/plane relationship.
7.  Fail clearly if the family cannot be placed correctly.

If the family cannot be reliably placed under the standards, report:

    UNSUPPORTED_FAMILY_HOSTING

rather than inventing a fallback.

------------------------------------------------------------------------

# 16. LEVEL RESOLUTION

Level resolution must be a dedicated responsibility.

Support:

-   host level IDs
-   linked source level IDs
-   level names
-   elevation-based mapping
-   transformed linked elevations
-   duplicate/similar names
-   deterministic selection
-   diagnostic evidence

Never assume a linked level ID is directly valid in the host document.

Never map levels only by name when elevation/coordinate evidence is
available.

Do not silently substitute a nearby level.

The resolved level must be recorded.

------------------------------------------------------------------------

# 17. CALCULATION ENGINE

Preserve the existing engineering intent, but isolate it from Revit.

The calculation engine must be deterministic.

Inputs should include normalized data such as:

-   room boundary
-   hazard class
-   rules
-   ceiling data
-   obstacles
-   existing sprinklers
-   configuration

Outputs should include candidate placement points/instructions.

The calculation engine must NOT:

-   create FamilyInstances
-   open transactions
-   access Revit documents
-   silently choose Revit hosts
-   modify the model

------------------------------------------------------------------------

# 18. ENGINEERING RULES

Do not invent NFPA values or engineering assumptions.

If an approved engineering value is not actually supplied by the
standards/configuration:

    REVIEW_REQUIRED

Do not silently insert a remembered/common NFPA value.

Clearly distinguish:

-   approved value
-   configured value
-   provisional value
-   estimated value
-   missing value
-   review-required condition

Existing UI warnings about provisional spacing/counts must remain
honest.

The system must never present provisional logic as final engineering
compliance.

------------------------------------------------------------------------

# 19. GEOMETRIC VALIDATION

Before Revit placement, validate every candidate.

At minimum where applicable:

-   candidate is inside valid room boundary
-   candidate is not inside an obstacle
-   candidate is not in a void
-   candidate is on/under the intended ceiling region
-   candidate elevation is valid
-   spacing constraints are satisfied
-   edge offsets are satisfied
-   duplicate proximity is checked
-   unsupported geometry is flagged

A candidate that fails validation should become a structured diagnostic,
not be passed blindly to Revit.

------------------------------------------------------------------------

# 20. PLACEMENT INSTRUCTION

Create a Revit-independent placement instruction.

Conceptually:

    PlacementInstruction

should contain enough information to execute and audit placement,
including:

-   stable point ID
-   room ID
-   level ID
-   requested XYZ
-   expected XYZ
-   family
-   type
-   placement intent
-   host intent
-   ceiling identity
-   source document
-   host document
-   coordinate-space information
-   rule/configuration version
-   diagnostic metadata

Do not put Autodesk.Revit.DB types in this model.

------------------------------------------------------------------------

# 21. REVIT PLACEMENT ADAPTER

The Revit placement layer converts domain instructions into actual Revit
operations.

Responsibilities:

-   resolve family symbol
-   resolve placement strategy
-   resolve level
-   resolve ceiling
-   resolve face
-   resolve host
-   create FamilyInstance
-   perform transaction safely
-   capture Revit exceptions
-   return structured execution result
-   perform post-placement validation

It should not calculate sprinkler spacing.

It should not classify hazards.

It should not invent engineering rules.

------------------------------------------------------------------------

# 22. TRANSACTIONS

Use transactions deliberately.

Placement should occur inside controlled Revit transactions.

Do not create unnecessary transactions around read-only extraction.

Define transaction boundaries clearly.

If batch placement is required, decide and document:

-   one transaction per room
-   one transaction per generation
-   subtransactions where justified
-   rollback strategy
-   partial-failure behavior

A failure must not leave the model in an unknown state.

------------------------------------------------------------------------

# 23. POST-PLACEMENT VALIDATION

A successful `NewFamilyInstance` call is NOT sufficient.

After creation, validate:

-   instance exists
-   ElementId valid
-   actual Location
-   actual XYZ
-   actual level
-   actual host
-   actual host element
-   family/type
-   placement type
-   orientation
-   expected ceiling relationship
-   expected room association where applicable

Compare:

    RequestedXYZ
    ActualXYZ

and record the delta.

If the instance exists but is spatially wrong:

    PLACED_BUT_INVALID

Do not call it successful merely because Revit returned an ElementId.

------------------------------------------------------------------------

# 24. VISIBILITY DIAGNOSTICS

Separate these states:

    CALCULATION_FAILED
    PLACEMENT_FAILED
    PLACED_BUT_INVALID
    PLACED_AND_VALID
    PLACED_BUT_NOT_VISIBLE_IN_VIEW

Do not change placement coordinates simply because a sprinkler is not
visible in a particular view.

If visibility is investigated, inspect:

-   active view
-   view type
-   associated level
-   view range
-   underlay
-   discipline
-   visibility settings
-   temporary hide/isolate
-   category visibility
-   phase
-   design option
-   workset
-   crop region
-   linked model visibility
-   section box for 3D views

Visibility must be diagnosed separately from placement.

------------------------------------------------------------------------

# 25. RESULT MODEL REWRITE

Improve the existing placement result model.

Do not keep ambiguous fields such as:

    X
    Y
    Z

if they can be confused with requested coordinates.

Prefer explicit fields such as:

    RequestedX
    RequestedY
    RequestedZ

    ActualX
    ActualY
    ActualZ

Retain useful traceability:

-   RoomId
-   LevelId
-   HostLevelId
-   HostLevelName
-   RevitElementId
-   FamilyPlacementType
-   HostingStrategy
-   CeilingSource
-   LinkInstanceName
-   HostCeilingElementId
-   SourceDocument
-   HostDocument
-   exception information
-   validation status
-   diagnostics

Use stable machine-readable status/error codes.

Human-readable messages should supplement codes, not replace them.

------------------------------------------------------------------------

# 26. DIAGNOSTICS

Implement structured diagnostics.

At minimum support categories such as:

-   NO_ROOM_BOUNDARY
-   NO_CEILING
-   MULTIPLE_CEILINGS
-   CEILING_GEOMETRY_UNSUPPORTED
-   NO_VALID_CEILING_FACE
-   LINK_TRANSFORM_ERROR
-   LEVEL_RESOLUTION_FAILED
-   FAMILY_NOT_FOUND
-   TYPE_NOT_FOUND
-   FAMILY_NOT_ACTIVATED
-   UNSUPPORTED_FAMILY_PLACEMENT
-   INVALID_HOST
-   WORKPLANE_UNAVAILABLE
-   CANDIDATE_OUTSIDE_ROOM
-   OBSTRUCTION
-   DUPLICATE
-   REVIT_CREATION_FAILED
-   POST_PLACEMENT_VALIDATION_FAILED
-   PLACED_BUT_NOT_VISIBLE
-   REVIEW_REQUIRED

Do not expose raw exceptions as the only diagnostic.

Keep raw exception details separately for debugging.

------------------------------------------------------------------------

# 27. IDEMPOTENCY / REPEATED RUNS

A production system must handle repeated runs.

Do not blindly duplicate sprinklers every time the command runs.

Design and document an explicit strategy:

-   update existing generated elements
-   replace generated elements
-   detect duplicates
-   use stable generation IDs
-   track source room + point identity
-   allow deliberate regeneration

Do not delete arbitrary user-created sprinklers.

Only modify/delete elements that the application can positively identify
as generated by the application.

------------------------------------------------------------------------

# 28. AUDIT TRAIL

Every placement generation should have a traceable identity.

Track where practical:

-   GenerationId
-   timestamp
-   project
-   rule/configuration version
-   family/type
-   room
-   source point
-   actual point
-   host
-   ceiling
-   result
-   diagnostics

Use a stable machine-readable export format.

Do not rely only on UI text.

------------------------------------------------------------------------

# 29. JSON EXPORTS

Keep calculation and placement JSON exports useful for diagnostics.

The JSON should clearly distinguish:

    placement input
    placement execution
    placement validation
    final result

Do not report requested coordinates as actual coordinates.

Do not overwrite useful diagnostic evidence.

If schemas change, version them.

Update schema/version documentation.

------------------------------------------------------------------------

# 30. UI / MVVM

Maintain clean MVVM.

View code-behind should not contain:

-   engineering calculations
-   placement logic
-   Revit model mutation
-   geometry algorithms
-   business rules

Use:

    View
      ↓
    ViewModel
      ↓
    Application Service
      ↓
    Domain/Revit adapters

The UI should expose clear stages:

1.  Model/level selection
2.  Configuration
3.  Calculation
4.  Preview
5.  Review
6.  Commit
7.  Results
8.  Diagnostics

------------------------------------------------------------------------

# 31. PREVIEW / COMMIT SEPARATION

Where the existing project supports Preliminary/Final modes, make the
distinction explicit.

PREVIEW:

-   extraction
-   calculation
-   validation
-   visualization
-   diagnostics
-   no model mutation

COMMIT:

-   revalidate inputs
-   execute placement
-   post-validate
-   report actual results

Do not trust stale preview data blindly.

------------------------------------------------------------------------

# 32. PERFORMANCE

Avoid:

-   repeatedly collecting the same elements
-   repeatedly parsing identical geometry
-   repeated linked-document traversal
-   unnecessary transactions
-   repeated family activation
-   repeated transform calculations
-   repeated room/ceiling queries

Use caching only when correctness remains deterministic and invalidation
is clear.

Measure before optimizing.

------------------------------------------------------------------------

# 33. ERROR HANDLING

No broad catch block should silently swallow a failure.

Every caught exception must:

1.  identify operation
2.  identify room/point if applicable
3.  identify family/type
4.  preserve exception detail
5.  produce a structured diagnostic
6.  leave transaction/model state safe

Do not convert all errors into:

    "Revit API placement failed."

------------------------------------------------------------------------

# 34. CODE QUALITY

During rewrite:

-   remove dead code
-   remove duplicate implementations
-   remove obsolete fallback paths
-   remove misleading comments
-   remove stale diagnostics
-   use clear names
-   keep methods focused
-   avoid giant services
-   avoid hidden state
-   avoid static mutable state where unnecessary
-   preserve compatibility with the actual Revit target versions
-   respect the solution's existing C#/.NET constraints unless the
    standards/project explicitly authorize changing them

Do not perform unrelated modernization merely because a newer framework
exists.

------------------------------------------------------------------------

# 35. COMPATIBILITY

Inspect the actual solution before deciding framework/API changes.

Do not assume:

-   .NET version
-   Revit version
-   C# version
-   API availability

If multiple Revit versions are supported, isolate version-specific code
where necessary.

Do not break existing add-in manifests.

Do not introduce an API that is unavailable in one supported target.

------------------------------------------------------------------------

# 36. TESTING STRATEGY

Create a layered test strategy.

## Unit tests

Test without Revit:

-   geometry
-   point-in-polygon
-   spacing
-   edge offsets
-   duplicate detection
-   hazard classification
-   rule selection
-   coordinate math
-   transform math
-   deterministic candidate generation

## Revit integration tests

Where the environment permits:

-   family discovery
-   family placement type detection
-   family activation
-   level resolution
-   ceiling resolution
-   linked ceiling resolution
-   face reference resolution
-   link reference creation
-   work-plane behavior
-   actual FamilyInstance creation
-   post-placement validation
-   visibility diagnostics

## Regression scenarios

At minimum create/document test cases for:

1.  simple host-model room
2.  multiple levels
3.  linked architectural model
4.  ceiling in host
5.  ceiling only in link
6.  flat ceiling
7.  sloped ceiling
8.  multiple ceilings
9.  no ceiling
10. irregular room
11. obstacles
12. existing sprinklers
13. FaceBased family
14. WorkPlaneBased family
15. OneLevelBased family
16. unsupported family
17. repeated execution
18. placement near room boundary
19. candidate inside obstruction
20. placed but not visible in a plan view

Where actual Revit test models already exist, use them.

Do not fabricate successful runtime results.

------------------------------------------------------------------------

# 37. CURRENT BUGS MUST BE RE-VERIFIED AFTER THE REWRITE

Specifically reproduce and diagnose the previously observed failures.

## Failure A

All sprinklers fail with:

    Revit API placement failed
    Error code: 5

Do not assume the previous agent's explanation is correct.

Prove the cause from:

-   actual family placement type
-   actual family host behavior
-   actual ceiling availability
-   actual host/reference
-   actual API overload
-   actual exception/error
-   runtime evidence

## Failure B

Sprinklers appear at the wrong floor / Ground Floor even though the
Properties panel reports L2.

Do not automatically classify this as View Range.

Prove:

-   actual instance XYZ
-   actual level
-   actual host
-   actual ceiling
-   actual view visibility
-   active view range
-   underlay
-   section box where applicable

Separate:

    coordinate problem
    level problem
    host problem
    visibility problem

------------------------------------------------------------------------

# 38. DO NOT TRUST PREVIOUS AI DIAGNOSES

Previous agents may have claimed:

-   origin placement
-   incorrect WorkPlaneBased classification
-   linked ceiling incompatibility
-   View Range being the cause
-   level mapping being correct
-   face orientation being wrong

Treat all such statements as hypotheses.

Verify them against:

1.  source code
2.  standards
3.  actual Revit API behavior
4.  runtime evidence

Do not repeat a previous AI conclusion merely because it sounds
plausible.

------------------------------------------------------------------------

# 39. NO "GOD LEVEL FIX"

Do not create a giant patch intended to fix everything at once.

Implement in controlled phases:

## Phase 1 --- Discovery and baseline

No behavior changes.

## Phase 2 --- Domain/application boundaries

Extract calculation responsibilities.

## Phase 3 --- Extraction/normalization

Formalize model and coordinate data.

## Phase 4 --- Ceiling/level/link resolution

Create dedicated resolvers.

## Phase 5 --- Family compatibility and placement strategies

Implement strategy-based placement.

## Phase 6 --- Validation and diagnostics

Implement structured execution/post-validation.

## Phase 7 --- UI workflow

Connect Preview/Commit/Results/Diagnostics cleanly.

## Phase 8 --- Idempotency/audit

Make repeated runs safe.

## Phase 9 --- Tests/regression

Build comprehensive coverage.

## Phase 10 --- Cleanup

Remove obsolete code only after proving it is unused.

After every phase:

-   build
-   test
-   inspect changed files
-   update tracking Markdown
-   record decisions

------------------------------------------------------------------------

# 40. SAFE MIGRATION RULE

Never delete a class just because it looks obsolete.

Before deletion:

1.  Search all references.
2.  Determine runtime usage.
3.  Determine whether tests use it.
4.  Determine whether UI uses it.
5.  Determine whether serialization depends on it.
6.  Determine whether another Revit version depends on it.
7.  Replace references.
8.  Build.
9.  Test.
10. Document the deletion.

------------------------------------------------------------------------

# 41. ACCEPTANCE CRITERIA

Do not declare the rewrite production-ready until all applicable
criteria are satisfied.

## Architecture

-   clear separation of domain/application/Revit/UI
-   no Revit API dependency in pure calculation
-   no calculation logic in UI
-   no giant placement service
-   explicit linked-model boundary
-   explicit placement strategies

## Calculation

-   deterministic
-   traceable
-   no invented engineering rules
-   geometry validated
-   provisional values clearly marked

## Revit placement

-   actual family placement type inspected
-   correct strategy selected
-   correct host/face/work-plane resolved
-   linked geometry handled correctly
-   no silent incompatible fallback
-   actual created instance validated

## Diagnostics

-   structured status/error codes
-   requested vs actual coordinates
-   host information
-   ceiling information
-   link information
-   exception detail
-   visibility diagnosis separated from placement

## Reliability

-   repeated runs handled safely
-   transactions controlled
-   partial failures handled
-   generated elements traceable

## Testing

-   unit tests pass
-   integration tests pass where available
-   regression scenarios documented
-   runtime validation performed in Revit before claiming runtime
    success

## Documentation

-   architecture documented
-   decisions documented
-   standards traceability documented
-   TODOs updated
-   session notes updated
-   migration status updated
-   known limitations documented

------------------------------------------------------------------------

# 42. REQUIRED FINAL REPORT

When the implementation is complete, produce a concise but technically
complete final report containing:

## What changed

List projects/files/components changed.

## Architecture

Describe the final dependency flow.

## Standards compliance

Summarize the standards traceability.

## Placement

Explain exactly how each supported family placement strategy works.

## Linked models

Explain coordinate transformation and linked ceiling/reference handling.

## Validation

Show:

-   build results
-   unit test results
-   integration test results
-   runtime Revit results

Never invent results.

## Known limitations

List anything that remains unresolved.

## Remaining manual review

List all engineering values or scenarios that still require human
review.

## Files removed

List deleted obsolete files and why they were safe to delete.

## Tracking documentation

Confirm which Markdown files were updated.

------------------------------------------------------------------------

# 43. HARD RULES

These rules override convenience:

1.  **Read the standards completely before major code changes.**
2.  **Inspect the whole repository before redesigning it.**
3.  **Do not guess.**
4.  **Do not invent engineering values.**
5.  **Do not silently substitute incompatible Revit placement
    strategies.**
6.  **Do not treat WorkPlaneBased as automatically LevelBased.**
7.  **Do not treat WorkPlaneBased as automatically FaceBased.**
8.  **Do not treat successful FamilyInstance creation as proof of
    correct placement.**
9.  **Do not treat view invisibility as proof of placement failure.**
10. **Do not mix linked and host coordinates.**
11. **Do not apply coordinate transforms twice.**
12. **Do not put Revit API objects into pure domain/calculation
    models.**
13. **Do not put business logic in WPF code-behind.**
14. **Do not hide exceptions.**
15. **Do not make destructive rewrites without dependency analysis.**
16. **Do not remove historical tracking information.**
17. **Do not claim tests passed unless actually run.**
18. **Do not claim Revit runtime validation unless actually executed in
    Revit.**
19. **Do not use a fallback merely to increase the placed count.**
20. **Correctness is more important than placement count.**

------------------------------------------------------------------------

# 44. OPERATING MODE

Work autonomously through the repository.

Do not repeatedly ask for permission for normal engineering actions.

When you encounter ambiguity:

1.  inspect source
2.  inspect standards
3.  inspect existing tests/docs
4.  search repository
5.  determine whether Revit runtime evidence is required
6.  make the smallest defensible decision
7.  document it

Ask the user only when a decision genuinely requires information that
cannot be obtained from the repository, standards, or runtime
environment.

------------------------------------------------------------------------

# 45. START HERE

Your first response/action sequence must be:

### Step 1

Locate and read both standards PDFs completely.

### Step 2

Read all existing root-level project Markdown/tracking files.

### Step 3

Inventory the complete solution.

### Step 4

Trace the current sprinkler pipeline end-to-end:

    UI
    -> ViewModel
    -> application/service
    -> room extraction
    -> ceiling extraction
    -> calculation
    -> placement input
    -> level resolution
    -> family resolution
    -> placement
    -> result
    -> JSON
    -> UI

### Step 5

Trace every current Revit placement overload and every
`FamilyPlacementType` decision.

### Step 6

Trace every linked-model coordinate transform.

### Step 7

Trace ceiling discovery and face-reference creation.

### Step 8

Run the baseline build/tests.

### Step 9

Create/update:

-   architecture audit
-   standards compliance matrix
-   migration plan
-   decisions
-   TODO/status/session tracking

### Step 10

Only after the above is complete, begin implementation.

------------------------------------------------------------------------

# 46. IMPORTANT: DO NOT STOP AT ANALYSIS

After completing the analysis and documentation, continue into
implementation.

The goal is not to produce a report describing what should be done.

The goal is to actually:

-   redesign the architecture
-   refactor/rewrite the code
-   migrate callers
-   remove obsolete implementations safely
-   implement tests
-   update UI integration
-   update diagnostics
-   update JSON schemas if required
-   update documentation
-   build and test continuously

If runtime Revit validation is impossible in the current environment,
complete everything that can be safely completed and explicitly mark the
runtime-dependent acceptance criteria as pending.

Do not fabricate runtime validation.

------------------------------------------------------------------------

# 47. FINAL QUALITY BAR

The finished system should behave like a professional engineering
application, not a collection of AI-generated patches.

A correct result is:

    fewer assumptions
    + explicit data flow
    + deterministic calculation
    + correct Revit hosting
    + correct linked-model geometry
    + safe transactions
    + post-placement verification
    + structured diagnostics
    + repeatable tests
    + traceable decisions
    + honest engineering status

Do not optimize for:

    "Placed: 26"

Optimize for:

    "26 candidates were calculated,
     24 were valid,
     22 were placed and post-validated,
     2 were rejected with explicit reasons,
     and every result is traceable."

That is the production-grade standard.
