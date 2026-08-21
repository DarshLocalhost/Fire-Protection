# Fire Protection System — Phase 1 Architecture Documentation: Production-Ready Revit Model Data Extraction

## 1. Overview

This document describes the production-ready Model Data Extraction system implemented in **Phase 1** for the Autodesk Revit Fire Protection System add-in.

The objective of Phase 1 is strictly **Model Data Extraction and Coordinate Normalization**: discovering the active MEP host model and linked architectural documents, extracting levels, rooms with true boundary geometry, candidate ceilings with 3D solid inspection, spatial obstacles, and existing sprinkler heads, classifying fire hazards using the senior-provided `HazardClassifier`, validating extraction consistency, and exporting a clean, normalized, versioned JSON snapshot suitable for downstream Phase 2 calculation engines.

---

## 2. Core Separation of Concerns

The solution strictly enforces architectural separation:

```text
MEP Host Model + Architectural Link(s)
                 │
                 ▼
  [ Document / Link Discovery ]  (RevitModelContext / RevitLinkContext)
                 │
                 ▼
     [ Model Data Extraction ]   (LevelExtractor, RoomExtractor, CeilingExtractor,
                 │                ObstacleExtractor, ExistingSprinklerExtractor)
                 ▼
  [ Coordinate Normalization ]   (Host MEP Internal Coordinates in Decimal Feet)
                 │
                 ▼
    [ Hazard Classification ]    (HazardClassifier - Unchanged Senior Engine)
                 │
                 ▼
   [ Normalized DTO Pipeline ]   (ModelSnapshot, RoomData, LevelData, CeilingData, etc.)
                 │
                 ▼
  [ Validation & Consistency ]   (ModelExtractionValidator -> ValidationSummary)
                 │
                 ▼
   [ Versioned JSON Snapshot ]   (JsonSnapshotExporter)
```

```text
┌───────────────────────────┐     ┌─────────────────────────────┐     ┌───────────────────────────┐     ┌───────────┐
│     MODEL EXTRACTION      │ !=  │    PLACEMENT CALCULATION    │ !=  │  REVIT INSTANCE CREATION  │ !=  │    UI     │
│ (Phase 1 Deliverable)     │     │    (Phase 2 Future Scope)   │     │   (Phase 2 Future Scope)  │     │   (WPF)   │
└───────────────────────────┘     └─────────────────────────────┘     └───────────────────────────┘     └───────────┘
```

---

## 3. Host / Link Strategy & Coordinate Normalization

### Canonical Coordinate System
* **Host MEP Model Internal Coordinates**: The canonical coordinate frame for all exported geometric primitives, points, polygons, vectors, bounding boxes, and elevations is the active MEP host model coordinate space.
* **Length Units**: Decimal feet (`ft`), which natively matches the Revit internal unit system.

### Coordinate Transformation
When extracting elements from linked architectural models:
1. The link instance transform is obtained via `linkInstance.GetTotalTransform()` (or `GetTransform()`).
2. Point transformation: `XYZ hostPoint = transform.OfPoint(sourcePoint);`
3. Vector/Normal transformation: `XYZ hostVector = transform.OfVector(sourceVector);`
4. Bounding Box transformation: All 8 corner vertices of the local bounding box are transformed into host space, from which the true axis-aligned bounding box `(Min, Max)` in host space is computed.
5. If architectural elements exist directly in the active host document, `Transform.Identity` is utilized seamlessly without coordinate distortion.

### Source Traceability
Every extracted entity (Level, Room, Ceiling, Obstacle, Sprinkler) records a `SourceReferenceData` block:
* `documentTitle`: Title of the source Revit project (`.rvt`).
* `documentPath`: File system path of the source model where available.
* `linkInstanceId`: ElementId of the `RevitLinkInstance` (if linked).
* `linkName`: Display name of the link instance.
* `isFromLink`: Boolean flag distinguishing host vs. linked origins.

---

## 4. Component Extraction Pipelines

### A. Level Extraction (`LevelExtractor`)
* Discovers levels from both the host MEP document and all linked documents.
* Maps linked level elevation `XYZ(0, 0, level.Elevation)` through the link transform to obtain canonical host elevation.
* Levels are indexed by `(DocumentTitle, ElementId)` and ordered by elevation ascending.

### B. Room & Boundary Extraction (`RoomExtractor`)
* Extracts placed and bounded rooms (`room.Area > 0` and `room.Location != null`).
* **Boundary Loops**: Extracts finish boundary segments using `SpatialElementBoundaryOptions(Finish)`.
* **Outer & Inner Loops**: Accurately differentiates the outer room perimeter from inner voids (shafts, column enclosures, island cutouts).
* **Curved Boundaries**: Linear curves are represented by Start/End/Mid points; Arcs and Splines are captured both parametrically (Center, Radius) and with tessellated polygonal approximations in host coordinates so no shape details are lost.
* Associates each room with its matched canonical level by ElementId and elevation proximity.

### C. Ceiling Extraction (`CeilingExtractor`)
* Gathers candidate ceilings from both host and linked architectural models.
* **3D Solid Geometry Inspection**: Recursively navigates `Solid`, `GeometryInstance`, and nested geometry.
* **Bottom Face Analysis**: Filters downward-facing planar faces (`Normal.Z < -0.1` in host coordinates) to determine the true lower finished ceiling surface elevation.
* **Slope & Type Classification**: Computes face angles relative to the gravity vector to classify ceilings as `FLAT`, `SLOPED` (with exact slope angle in degrees), `STEPPED`, or `NONE`.
* **Room Association**: Determines ceiling spatial overlap with room horizontal boundaries and vertical extent.

### D. Obstacle Extraction (`ObstacleExtractor`)
* Selectively filters structural and architectural elements critical for fire sprinkler distribution:
  * Structural Columns (`OST_StructuralColumns`)
  * Architectural Columns (`OST_Columns`)
  * Structural Framing / Beams (`OST_StructuralFraming`)
  * Boundary Walls (`OST_Walls`)
  * MEP Distribution Curves (Ducts, Pipes, Cable Trays)
* Normalizes centerlines, bounding boxes, and dimensional extents `(Width, Depth, Height)` in host MEP space.

### E. Existing Sprinkler Extraction (`ExistingSprinklerExtractor`)
* Discovers all `OST_Sprinklers` instances in the host MEP model.
* Captures ElementId, Family Name, Type Name, 3D Location, Level, Host Element, Facing Orientation vector, and mounting classification (`Pendent`, `Upright`, `Sidewall`, `Concealed`).

### F. Hazard Classification (`HazardClassifier`)
* Integrates the senior-provided `HazardClassifier` verbatim.
* Classifies room names into `HazardClass` (`Light`, `OH1`, `OH2`, `EH1`, `EH2`).
* Captures matched keywords and sets `requiresReview` flag when multiple keywords match or high-risk occupancy terms (e.g. `storage`, `warehouse`, `server`) are encountered.

---

## 5. Normalized DTO Layer & JSON Schema

The exported snapshot is fully decoupled from Autodesk Revit API types.

### Schema Structure (`schemaVersion: "1.0"`)

```json
{
  "schemaVersion": "1.0",
  "units": {
    "length": "ft",
    "area": "sq_ft",
    "volume": "cu_ft",
    "angle": "degrees"
  },
  "coordinateSystem": {
    "canonical": "host_mep_model",
    "lengthUnit": "feet",
    "description": "All coordinates, elevations, and geometry are normalized to the active MEP host model internal coordinate system in decimal feet."
  },
  "project": {
    "name": "MEP_FireProtection_Host",
    "standard": "NFPA13-2022",
    "timestampUtc": "2026-08-20T13:00:00Z"
  },
  "model": {
    "host": {
      "title": "MEP_FireProtection_Host.rvt",
      "path": "C:\\Projects\\MEP_FireProtection_Host.rvt",
      "isWorkshared": false,
      "revitVersion": "2025",
      "isHostMep": true
    },
    "links": [
      {
        "instanceId": "481029",
        "linkName": "Architectural_Link.rvt : 1 : location <Not Shared>",
        "documentTitle": "Architectural_Link.rvt",
        "documentPath": "C:\\Projects\\Architectural_Link.rvt",
        "isLoaded": true,
        "isNested": false,
        "roomCount": 42,
        "transform": {
          "isIdentity": false,
          "origin": { "x": 100.0, "y": 50.0, "z": 0.0 },
          "basisX": { "x": 1.0, "y": 0.0, "z": 0.0 },
          "basisY": { "x": 0.0, "y": 1.0, "z": 0.0 },
          "basisZ": { "x": 0.0, "y": 0.0, "z": 1.0 },
          "scale": 1.0
        }
      }
    ]
  },
  "levels": [
    {
      "levelId": "101",
      "elementId": "101",
      "name": "Level 1",
      "elevationFt": 0.0,
      "origin": "Architectural_Link.rvt",
      "source": { "documentTitle": "Architectural_Link.rvt", "isFromLink": true, "linkInstanceId": "481029" },
      "roomCount": 24,
      "rooms": []
    }
  ],
  "rooms": [
    {
      "roomId": "20541",
      "elementId": "20541",
      "name": "Conference Room",
      "number": "104",
      "levelId": "101",
      "levelName": "Level 1",
      "levelElevationFt": 0.0,
      "areaSqFt": 450.0,
      "volumeCuFt": 4050.0,
      "phase": "New Construction",
      "source": { "documentTitle": "Architectural_Link.rvt", "isFromLink": true, "linkInstanceId": "481029" },
      "locationPoint": { "x": 125.4, "y": 82.1, "z": 0.0 },
      "boundingBox": {
        "min": { "x": 110.0, "y": 70.0, "z": 0.0 },
        "max": { "x": 140.0, "y": 95.0, "z": 9.0 }
      },
      "boundary": {
        "outerLoop": {
          "isOuter": true,
          "polygon": [[110.0, 70.0], [140.0, 70.0], [140.0, 95.0], [110.0, 95.0]],
          "segments": []
        },
        "innerLoops": [],
        "polygon": [[110.0, 70.0], [140.0, 70.0], [140.0, 95.0], [110.0, 95.0]]
      },
      "hazard": {
        "hazardClass": "Light",
        "matchedKeyword": "conference",
        "matchedTerms": ["conference"],
        "requiresReview": false
      },
      "ceilings": [
        {
          "elementId": "30911",
          "ceilingName": "2x4 ACT System",
          "familyName": "Compound Ceiling",
          "typeName": "2x4 ACT",
          "bottomElevationFt": 9.0,
          "heightAboveLevelFt": 9.0,
          "slopeType": "FLAT",
          "slopeDegrees": 0.0
        }
      ],
      "requiresHumanReview": false,
      "warnings": []
    }
  ],
  "existingSprinklers": [],
  "obstacles": [],
  "summary": {
    "levelsExtracted": 3,
    "roomsExtracted": 42,
    "roomsWithBoundaries": 42,
    "roomsMissingBoundaries": 0,
    "ceilingsResolved": 38,
    "roomsWithoutCeilings": 4,
    "linkedModelsFound": 1,
    "linkedModelsLoaded": 1,
    "existingSprinklersExtracted": 0,
    "obstaclesExtracted": 116,
    "warningCount": 0,
    "errorCount": 0,
    "isValid": true
  },
  "warnings": [],
  "errors": []
}
```

---

## 6. Validation & Error Handling

* **Non-Destructive Per-Room Recovery**: If an individual room suffers from missing boundary segments or unusual geometry, a structured `ExtractionIssue` warning is logged in the room and global issue list, allowing the remaining model elements to be extracted completely.
* **Global Preflight Checks**: Verifies that the host document exists, links are checked for load status, duplicate room IDs are flagged, and missing ceilings/boundaries are reported in `ValidationSummary`.

---

## 7. Performance & Memory Optimization

* **Single Document Discovery Pass**: Links and levels are resolved and cached once per extraction run.
* **Spatial Bounding-Box Filtering**: Candidate ceiling and obstacle associations utilize quick 2D/3D bounding-box rejection before opening intensive solid Brep representations.
* **Read-Only / Zero Transaction Overhead**: The extraction service opens 0 Revit write transactions, ensuring the active model document is never dirtied or locked during extraction.

---

## 8. Requirements vs. Decisions vs. Assumptions Matrix

| Topic | Category | Description |
| :--- | :--- | :--- |
| **Canonical Coordinates** | **Confirmed Requirement** | All output coordinates must be normalized to Host MEP model internal coordinates in decimal feet. |
| **Hazard Classifier** | **Confirmed Requirement** | Must use the senior-provided `HazardClassifier` verbatim without altering keywords or logic. |
| **Snowdon JSON Decoupling**| **Confirmed Requirement** | Production extraction must not depend on Snowdon JSON; Snowdon is isolated strictly as an optional test fixture. |
| **Multi-Target Build** | **Confirmed Requirement** | Solution must build cleanly on Revit 2024 (.NET Framework 4.8), Revit 2025 (.NET 8.0-windows), and Revit 2026 (.NET 8.0-windows). |
| **Curved Boundary Handling** | **Implementation Decision** | Linear segments store Start/End/Mid; Arcs store radius and tessellated polygonal vertices for true geometric fidelity. |
| **Ceiling Height Fallback** | **Implementation Decision** | When no physical ceiling geometry intersects a room volume, the room's unbounded height parameter is preserved with an explicit notice. |
| **JSON Export Path** | **Implementation Decision** | Defaults to `%USERPROFILE%\Documents\FireProtectionSystem\Exports\ModelSnapshot_<Name>_<Timestamp>.json` or add-in assembly folder. |
| **Phase 2 Expansion** | **Assumption** | Phase 2 sprinkler placement algorithms (Grid, Collision, BruteForce) will consume `ModelSnapshot` directly. |
