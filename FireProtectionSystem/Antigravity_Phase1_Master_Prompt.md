# Antigravity Master Prompt — Phase 1: Production-Ready Revit Model Data Extraction

## Objective

Work on the attached existing Revit Fire Protection System solution.

Implement **ONLY Phase 1: production-ready model data extraction**.

The deliverable is a clean, extensible extraction pipeline that reads the real Revit model and produces a normalized JSON representation suitable for Phase 2 sprinkler-placement calculation.

**Do not implement sprinkler placement in this phase.**

---

## Existing Environment

The solution is an existing Revit add-in with:

- Working `.addin` configuration.
- Existing Revit command entry point.
- Existing backend and UI.
- Revit 2024 target.
- Revit 2025 target.
- Revit 2026 target.
- Existing build configurations that must remain usable.

You may:

- create files;
- delete obsolete files;
- move/rename files;
- restructure folders;
- modify existing files;
- create new abstractions.

Do not preserve poor architecture merely for compatibility. Preserve required functionality and build compatibility.

---

# 1. Target Architecture

The production pipeline must become:

```text
MEP Host Revit Model
        |
        +---- Architectural Revit Link(s)
        |
        v
Model/Document Discovery
        |
        v
Model Extraction
        |
        v
Coordinate Normalization
        |
        v
Levels / Rooms / Boundaries / Ceilings / Geometry /
Existing Sprinklers / Model Metadata
        |
        v
Hazard Classification
        |
        v
Normalized DTOs
        |
        v
Versioned JSON
```

The critical separation is:

```text
MODEL EXTRACTION
        !=
PLACEMENT CALCULATION
        !=
REVIT INSTANCE CREATION
        !=
UI
```

Phase 1 ends at normalized model data + JSON.

---

# 2. Future Folder Architecture

Restructure the solution so the future placement structure is clear.

Conceptually:

```text
FireProtection.Backend
│
├── Commands
│
├── Models
│   ├── Model
│   ├── Hazard
│   ├── Placement
│   └── Sprinklers
│
└── Services
    ├── Model
    │   ├── RevitModelContext
    │   ├── RevitLinkContext
    │   ├── LevelExtractor
    │   ├── RoomExtractor
    │   ├── CeilingExtractor
    │   ├── ObstacleExtractor
    │   └── ExistingSprinklerExtractor
    │
    ├── Extraction
    │
    ├── Hazard
    │
    └── Placement
        ├── Sprinklers
        │   ├── Preliminary
        │   │   └── Collision
        │   └── Final
        │       └── BruteForce
        │
        ├── SmokeDetectors
        └── NotificationAppliances
```

For this phase, implement extraction only.

Do not implement Collision or BruteForce.

The structure must make it easy to add them later.

---

# 3. Snowdon JSON Must Not Be the Production Architecture

The current `sprinkler_coordinates_output.json` / `SnowdonPlacementProvider` mechanism was temporary test infrastructure.

Production extraction must NOT depend on:

```text
sprinkler_coordinates_output.json
```

Do not use Snowdon coordinates as production placement input.

If `SnowdonPlacementProvider` is still useful for historical/testing purposes, isolate it as a test adapter.

Otherwise remove it safely after checking references.

The production command must work without the Snowdon JSON file.

---

# 4. MEP Host + Linked Architecture

The senior requirement is that the tool should operate in the **MEP model**.

Therefore design primarily for:

```text
MEP Host Model
      |
      +---- Architectural Link
```

The MEP model is the canonical output coordinate system.

The architecture must also be extensible to support a future case where architectural information exists directly in the active host model.

Do not hardcode the assumption that rooms/ceilings are always in the active document.

---

# 5. Model Context

Create a clear model context abstraction.

It should represent:

- host MEP document;
- loaded Revit links;
- linked documents;
- link instance ElementIds;
- link names;
- link paths where safely available;
- transforms;
- source-document identity.

Every extracted element should be traceable to its source:

```text
Host
or
Linked document + LinkInstance
```

Avoid leaking Revit API objects into normalized algorithm DTOs.

---

# 6. Coordinate System

This is mandatory.

The canonical coordinate system is:

```text
HOST MEP MODEL COORDINATES
```

For linked architectural data:

```text
Linked coordinates
        |
        v
RevitLinkInstance transform
        |
        v
Host MEP coordinates
```

Never simply copy linked X/Y/Z.

Capture transform metadata where useful for diagnostics.

Do not use `Transform.Identity` unless the actual transform is identity.

The JSON must explicitly declare:

```text
units = feet
coordinateSystem = host_mep_model
```

---

# 7. Level Extraction

Extract all relevant levels.

Each level should include, where available:

- ElementId;
- name;
- elevation in feet;
- source document;
- source link;
- host coordinate information where relevant.

Do not use level name as the primary identity.

Level names can repeat.

Use document identity + Revit ElementId internally.

---

# 8. Room Extraction

For every relevant room extract:

- ElementId;
- room ID;
- room name;
- room number;
- level ID;
- level name;
- level elevation;
- area;
- volume where reliably available;
- phase where relevant;
- source document;
- source link;
- location point where available;
- bounding box;
- actual room boundary;
- normalized boundary in host MEP coordinates;
- hazard classification;
- hazard review status;
- warnings.

Do not represent room shape only with a bounding rectangle.

The future placement engine requires the actual room boundary.

---

# 9. Room Boundaries

Extract actual boundary geometry.

Support where possible:

- rectangular rooms;
- L-shaped rooms;
- irregular rooms;
- multiple segments;
- arcs/curves;
- linked-room boundaries.

Transform linked boundaries into host MEP coordinates.

Do not silently discard geometry.

If a geometry representation cannot be fully serialized, preserve as much information as possible and add a warning.

---

# 10. Ceiling Extraction

For each relevant room identify candidate ceilings.

Extract:

- ElementId;
- ceiling type/name;
- category;
- source document;
- source link;
- bounding box;
- elevation;
- boundary/profile where available;
- slope information where available;
- relevant geometry;
- normalized host coordinates;
- diagnostics/status.

Handle:

- no ceiling;
- one ceiling;
- multiple ceilings;
- partial ceiling coverage;
- stepped ceilings;
- sloped ceilings;
- linked ceilings;
- complex ceiling geometry.

Do NOT assume:

```text
Room Level Elevation == Ceiling Elevation
```

They are separate pieces of information.

---

# 11. Ceiling Geometry

The previous Snowdon implementation only inspected limited direct geometry.

The production extraction layer must support recursive geometry inspection where required, including:

- Solid;
- GeometryInstance;
- nested geometry;
- relevant faces;
- relevant edges/curves.

Do not silently ignore valid ceiling geometry because it is nested in a GeometryInstance.

The extracted ceiling information must give Phase 2 enough information to determine:

- ceiling height;
- usable ceiling region;
- ceiling surface;
- mounting context.

Do not perform sprinkler placement here.

---

# 12. Relevant Obstacles / Geometry

Extract geometry needed for future placement validation.

At minimum investigate:

- walls;
- columns;
- beams;
- floors;
- ceilings;
- shafts/openings where accessible;
- relevant MEP elements;
- existing fire-protection elements.

Do not dump the entire Revit model into JSON.

Every extracted category must have a reason related to future fire-protection placement.

Use spatial filtering/caching where appropriate.

---

# 13. Existing Sprinklers

Extract existing sprinkler instances from the MEP host model.

For each existing sprinkler capture where available:

- ElementId;
- family name;
- type name;
- location;
- level;
- host information;
- source document;
- orientation;
- placement/mounting classification;
- bounding box.

Do not modify existing sprinklers.

The purpose is to allow Phase 2 to understand existing coverage and avoid blind duplication.

---

# 14. Family / Type Information

The existing `SprinklerFamilyResolver` is useful for future placement.

Do not hardcode one sprinkler family.

Do not assume all families have identical placement behavior.

For existing sprinklers preserve:

- family;
- type;
- mounting/placement type;
- host relationship.

Do not implement automatic sprinkler family selection rules in Phase 1.

---

# 15. HazardClassifier

There is an existing senior-provided `HazardClassifier`.

**Use it exactly as provided.**

Do NOT rewrite:

- keyword mappings;
- hazard enum values;
- review logic;
- classification semantics.

Use its existing result in extracted room data.

Preserve:

- hazard class;
- matched terms/evidence where exposed;
- review flag.

If useful additional room metadata is extracted, store it as raw data, but do not alter the classifier.

---

# 16. View Independence

Do not design extraction around a particular Revit view.

A sprinkler is a 3D model element and can appear differently depending on:

- floor plan;
- reflected ceiling plan;
- 3D view;
- section;
- discipline;
- view range;
- category visibility;
- phase;
- workset;
- family visibility settings.

Phase 1 must extract the model, not "what is visible in the current view."

Do not create separate data models for "floor-plan sprinkler" and "ceiling-plan sprinkler."

---

# 17. Normalized DTO Layer

Create Revit-free normalized DTOs where practical.

Conceptual models:

```text
ModelSnapshot
DocumentInfo
LinkInfo
LevelData
RoomData
BoundaryData
CeilingData
GeometryData
ExistingSprinklerData
HazardData
```

Revit API types should remain inside extraction/Revit-specific services as much as practical.

Future Phase 2 should consume normalized data rather than directly querying arbitrary Revit elements everywhere.

---

# 18. JSON Schema

Create a versioned JSON schema.

Use this conceptual structure:

```json
{
  "schemaVersion": "1.0",
  "units": "ft",
  "coordinateSystem": {
    "canonical": "host_mep_model",
    "lengthUnit": "feet"
  },
  "model": {
    "host": {},
    "links": []
  },
  "levels": [],
  "rooms": [],
  "existingSprinklers": [],
  "obstacles": [],
  "warnings": [],
  "errors": []
}
```

Room data should conceptually contain:

```json
{
  "id": "...",
  "name": "...",
  "number": "...",
  "levelId": "...",
  "source": {},
  "boundary": {},
  "boundingBox": {},
  "areaSqFt": 0,
  "volumeCuFt": 0,
  "hazard": {
    "class": "Light",
    "matchedTerms": [],
    "requiresReview": false
  },
  "ceilings": [],
  "warnings": []
}
```

Do not invent values to fill fields.

Use `null` or an explicit warning where information is unavailable.

---

# 19. JSON Requirements

The JSON must:

- be valid JSON;
- have a schema version;
- explicitly declare units;
- explicitly declare coordinate system;
- distinguish host and linked sources;
- preserve Revit ElementIds;
- preserve source-document information;
- contain levels;
- contain rooms;
- contain actual room boundaries;
- contain ceiling data;
- contain relevant geometry/obstacles;
- contain existing sprinklers;
- contain hazard data;
- contain warnings/errors;
- be deterministic where practical;
- be readable;
- be suitable for future Phase 2 consumption.

It must NOT contain:

- hardcoded Snowdon placement coordinates;
- Snowdon-generated sprinkler points;
- production placement decisions;
- fake engineering values.

---

# 20. Extraction Validation

After extraction, validate:

- room identity;
- level resolution;
- room boundary;
- coordinate consistency;
- link transforms;
- ceiling resolution;
- geometry validity;
- duplicate IDs;
- missing links;
- unloaded links;
- existing sprinkler locations.

A room-level problem must not destroy the entire extraction.

Produce a summary similar to:

```text
Levels extracted: ...
Rooms extracted: ...
Rooms with boundaries: ...
Rooms missing boundaries: ...
Ceilings resolved: ...
Rooms without ceilings: ...
Linked models: ...
Existing sprinklers: ...
Warnings: ...
Errors: ...
```

Use actual values from the run. Never fabricate them.

---

# 21. Error Handling

Do not fail the whole extraction because one room is problematic.

Prefer:

```text
Room extraction
    |
    +-- success -> add result
    |
    +-- problem -> record warning/error -> continue
```

Global failure is appropriate only for genuinely unrecoverable conditions such as:

- invalid active document;
- failure to initialize model context;
- unrecoverable Revit API failure;
- serialization failure.

---

# 22. Performance

Design for large buildings.

Avoid:

- full-document collectors inside every room;
- repeated geometry extraction;
- repeatedly opening linked documents;
- repeated transform computation;
- unnecessary Revit API calls.

Prefer:

- cached levels;
- cached links;
- cached transforms;
- cached ceilings;
- cached existing sprinklers;
- spatial filtering;
- extraction once, consumption many times.

Keep the code understandable.

---

# 23. Transactions

Phase 1 is extraction.

Do not create write transactions just to read the model.

Do not modify the Revit document.

No:

- sprinkler creation;
- parameter writes;
- placement transactions;
- model edits.

---

# 24. Command Integration

Preserve the existing Revit command entry point where practical.

The command should:

1. obtain active Revit document;
2. initialize model context;
3. discover host/links;
4. execute extraction;
5. classify hazards;
6. validate extraction;
7. serialize normalized data;
8. export JSON;
9. report summary.

Do not put extraction logic into UI code-behind.

The extraction service must be independently callable.

---

# 25. JSON Export Location

Do not use developer-specific absolute paths.

Use the existing project's/add-in's sensible output convention or a clearly configured export location.

The output must be easy to locate during testing.

The production extractor must not require:

```text
C:\Users\<developer>\...
```

---

# 26. Existing Project Inspection Before Editing

Before changing files:

1. Inspect the entire solution.
2. Inspect all projects.
3. Inspect project files.
4. Inspect `.addin` files.
5. Inspect Revit-version configurations.
6. Inspect current command registration.
7. Inspect existing extraction services.
8. Inspect existing room/level models.
9. Inspect existing placement classes.
10. Inspect `HazardClassifier`.
11. Inspect all references to Snowdon.
12. Inspect all references to `SprinklerPlacementPoint`.
13. Inspect all references to `RevitSprinklerPlacer`.
14. Identify duplicate/dead code.
15. Determine what can safely be moved/deleted.

Do not blindly delete files.

---

# 27. Refactoring Rules

You may restructure the solution aggressively if required.

When moving/deleting files:

- update namespaces;
- update project references;
- update using statements;
- update dependency injection/constructors;
- update command references;
- update `.addin` references only if necessary;
- preserve public contracts where useful;
- remove dead code only after checking references.

Do not leave duplicate implementations.

There should be one authoritative extraction path.

---

# 28. Revit Version Compatibility

The solution must build for:

```text
Revit 2024
Revit 2025
Revit 2026
```

Do not assume APIs are identical.

If version-specific code is required:

- isolate it;
- keep it out of the normalized DTO layer;
- avoid spreading version checks throughout the project.

Do not claim compatibility until each configured target has actually been built.

---

# 29. Code Quality

Use:

- single responsibility;
- clear service boundaries;
- meaningful names;
- small focused classes;
- existing DI patterns where appropriate;
- no giant God classes;
- no UI business logic;
- no hardcoded model IDs;
- no hardcoded coordinates;
- no developer-specific paths;
- no magic engineering numbers without a documented source;
- explicit warnings/errors;
- clean namespaces.

Do not add engineering rules that were not provided.

---

# 30. Documentation

Create a repository document explaining:

- final architecture;
- extraction flow;
- host/link strategy;
- coordinate strategy;
- room extraction;
- ceiling extraction;
- obstacle extraction;
- existing sprinkler extraction;
- hazard classification;
- JSON schema;
- validation;
- error handling;
- performance strategy;
- known limitations;
- Phase 2 integration points.

Clearly distinguish:

```text
Confirmed project requirement
vs.
Implementation decision
vs.
Assumption
```

---

# 31. Testing

After implementation, test with the actual Revit model.

Verify at minimum:

1. MEP host with architectural link.
2. Multiple levels.
3. Multiple rooms.
4. Corridor.
5. Irregular room.
6. Room with ceiling.
7. Room without ceiling.
8. Multiple ceiling conditions.
9. Existing sprinklers.
10. Multiple sprinkler families/types.
11. Linked geometry.
12. Non-identity link transform.
13. Multiple levels with different elevations.

The objective is to verify:

> Does the JSON accurately represent the real model?

Do not evaluate sprinkler placement yet.

---

# 32. Acceptance Criteria

Phase 1 is complete only when:

## Architecture

- [ ] Extraction is separated from placement.
- [ ] Snowdon is not a production dependency.
- [ ] Host/link model abstraction exists.
- [ ] Normalized DTO layer exists.
- [ ] Future sprinkler/smoke/fire-alarm expansion is possible.

## Model

- [ ] Host MEP model is identified.
- [ ] Revit links are discovered.
- [ ] Link transforms are captured.
- [ ] Linked geometry is transformed into host coordinates.
- [ ] Levels are extracted.
- [ ] Rooms are extracted.
- [ ] Room boundaries are extracted.
- [ ] Ceilings are extracted.
- [ ] Relevant geometry is extracted.
- [ ] Existing sprinklers are extracted.
- [ ] Family/type information is available.
- [ ] Source-document information is preserved.

## Hazard

- [ ] Existing `HazardClassifier` is used unchanged.
- [ ] Hazard class is included.
- [ ] Review status is included.

## JSON

- [ ] Schema version exists.
- [ ] Units exist.
- [ ] Coordinate system exists.
- [ ] Host/link metadata exists.
- [ ] Levels exist.
- [ ] Rooms exist.
- [ ] Boundaries exist.
- [ ] Ceilings exist.
- [ ] Relevant obstacles exist.
- [ ] Existing sprinklers exist.
- [ ] Hazard data exists.
- [ ] Warnings/errors exist.
- [ ] No Snowdon placement coordinates exist.

## Robustness

- [ ] One bad room does not stop extraction.
- [ ] Missing ceiling is reported.
- [ ] Missing boundary is reported.
- [ ] Missing/unloaded links are reported.
- [ ] Invalid geometry is reported.
- [ ] Duplicate/invalid identities are reported.

## Compatibility

- [ ] Revit 2024 builds.
- [ ] Revit 2025 builds.
- [ ] Revit 2026 builds.
- [ ] Existing `.addin` setup remains valid.
- [ ] Existing command launches in Revit.

---

# 33. REQUIRED FINAL REPORT

When implementation is complete, report:

## A. Files created

Every new file and purpose.

## B. Files modified

Every modified file and reason.

## C. Files deleted/moved

Every deleted/moved file and reason.

## D. Final folder structure

Show the relevant final structure.

## E. Extraction pipeline

Show:

```text
MEP host
 -> links
 -> extraction
 -> coordinate normalization
 -> hazard
 -> DTOs
 -> JSON
```

## F. Actual JSON schema

Show the implemented schema.

## G. Sample output

Use actual extracted data if a Revit model was available.

Never fabricate model values.

## H. Extraction statistics

Report actual:

- levels;
- rooms;
- ceilings;
- links;
- existing sprinklers;
- warnings;
- errors.

## I. Build status

Report separately:

- Revit 2024;
- Revit 2025;
- Revit 2026.

Only claim success after actually building.

## J. Known limitations

List anything that could not be verified without running inside the real Revit model.

---

# 34. HARD STOP

When Phase 1 is complete, STOP.

Do not implement:

- BruteForce;
- Collision;
- sprinkler point calculation;
- sprinkler creation;
- sprinkler optimization;
- Smoke Detector placement;
- Notification Appliance placement.

The deliverable is only:

> A production-oriented, model-driven extraction system that produces a reliable normalized JSON representation of the MEP host model and relevant linked architectural model data.

Phase 2 will begin only after the generated JSON has been run against the real Revit model and manually verified.
