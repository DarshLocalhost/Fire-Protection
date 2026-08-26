# Sprinkler Actual Z Diagnostic

> ⚠️ **PARTIALLY STALE (as of 2026-08-25).** The **(0,0,0) root cause below is still correct and
> runtime-PROVEN.** But the *fix* described here (and any `file:line` references to
> `RevitSprinklerPlacementService`/`FindCeilingHost`/`isFaceBased`) reflect the **superseded Decision 010**.
> The code now implements **Decision 011** (explicit placement-strategy pattern; no WorkPlaneBased→Level
> fallback; post-placement validation). **Current truth: `PROJECT_MEMORY.md` §0 + §7 and `DECISIONS.md` 011.**
> Kept for historical root-cause traceability. Runtime re-verification of the Decision 011 fix is still pending.

## 1. Problem

An automatically placed sprinkler was reported to appear at the wrong elevation / wrong plan view.
Runtime instrumentation was added (temporary, non-production) and the add-in was run in Revit 2025
against `02_FireProtection_Test.rvt` (host MEP) with linked `01_Architectural_Test.rvt`.

The captured diagnostic proves the sprinkler is physically placed at the **project origin (0,0,0)**,
not at the calculated location. This is a **code/placement bug**, not a View Range issue.

## 2. Code Path

```text
RoomExtractor.ExtractRooms
  └─ FindCeilingsForRoom            → room.Ceilings (linked L1 ceiling, bottom 12 ft)
BruteForceCalculationService
  └─ placementZ = flatCeiling.BottomElevationFt = 12
  └─ CandidatePoint.Z = 12  →  CalculatedSprinklerPoint { X=14.12, Y=31.95, Z=12, LevelId="30"(L1) }
RevitSprinklerPlacementService.PlaceSinglePoint
  └─ ResolveHostLevel → Level "L1" (elevation 0)
  └─ isFaceBased = (FamilyPlacementType == "FaceBased")  → FALSE  ◀── MIS-DETECTION
  └─ NewFamilyInstance(xyz, symbol, level, NonStructural)  ◀── WRONG OVERLOAD for a hosted family
  └─ instance.Location = (0, 0, 0)   ◀── actual physical location
```

Source: `FireProtection.Backend/Services/Placement/Sprinklers/Final/RevitSprinklerPlacementService.cs`
- `isFaceBased` detection: line ~84 (matches only `"FaceBased"`).
- Placement branches: `NewFamilyInstance` face-based ≈ line 247, level-based ≈ line 251.
- Temporary diagnostic capture: after the instance-validity check (logs `Actual Instance XYZ`).

## 3. Runtime Values (captured from `sprinkler_z_diagnostic.txt`)

| Value                       | Result |
| --------------------------- | ------ |
| Candidate X                 | 14.123 |
| Candidate Y                 | 31.956 |
| Candidate Z (`point.Z`)     | **12** |
| Passed XYZ.Z (`xyz.Z`)      | **12** |
| Host Level                  | L1 |
| Host Level Elevation (ft)   | 0 |
| **Actual Instance X**       | **0** |
| **Actual Instance Y**       | **0** |
| **Actual Instance Z**       | **0** |
| Calculated Elevation From Level | 0 (0 − 0) |
| Ceiling Bottom Z            | 12 (linked L1 ceiling `1238481`, `bottomElevationFt=12`) |
| Ceiling Source              | Linked `01_Architectural_Test` (link instance `1251203`, transform = identity) |
| FaceBasedPlacement (detected) | **False** |

Cross-check from `sprinkler_placement_result_*.json`: the `PlacedSprinklerEntry` records
`X=14.123, Y=31.956, Z=12` — **these are the input coordinates, not the real instance location**.
The export echoes the calculated point, which is why the bug was invisible until the actual
`instance.Location` was read.

## 4. Root Cause

```text
CODE BUG  (specifically: PLACEMENT OVERLOAD / FAMILY-HOSTING BUG  →  CASE B)
```

Decision-tree result: **Candidate Z = 12 (correct), Passed Z = 12 (correct), Actual Z = 0 (WRONG)**.
That is exactly CASE B of the master prompt: the calculation is correct but the Revit API placement
put the instance at the wrong physical location.

**Why it happens:**

1. `Sprinkler - Pendent - Hosted` is a **hosted** family. A ceiling-hosted sprinkler's
   `FamilyPlacementType` is `WorkPlaneBased` (not `FaceBased`).
2. `RevitSprinklerPlacementService` detects hosted placement only when
   `FamilyPlacementType.ToString() == "FaceBased"`. For this family that is false, so
   `isFaceBased = false`.
3. The code therefore calls the **level-based** overload
   `NewFamilyInstance(xyz, symbol, level, StructuralType.NonStructural)`.
4. A hosted/face-based family cannot be placed via the level overload without a host; Revit creates
   the element but positions it at the **project origin (0,0,0)** and ignores the supplied `xyz`.
5. Result: the sprinkler is physically at (0,0,0) — i.e. at L1 elevation 0, the base/ground level —
   which is why it shows in the wrong plan view. The world Z is 0, not 12.

The link transform is identity (`ModelSnapshot` confirms `isIdentity: true`), so the linked ceiling
bottom (12 ft) is already the correct host-MEP coordinate; **coordinate transformation is NOT the
issue**. The issue is purely the placement overload / family-hosting handling.

## 5. Evidence

- `sprinkler_z_diagnostic.txt`: `Actual Instance XYZ (ft): 0, 0, 0` while `Point XYZ (ft): 14.12, 31.95, 12`.
- `sprinkler_placement_result_*.json`: 26 sprinklers "Placed" with the input coords; 0 failed — the
  export is misleading because it records the input point, not `instance.Location`.
- `ModelSnapshot`: rooms/ceilings are `isFromLink: true`; host `02_FireProtection_Test` has no ceilings
  of its own (its L1/L2 are levels only). So `FindCeilingHost` (host-document only) finds nothing.
- `FaceBasedPlacement: False` confirms the family was treated as non-hosted.

## 6. Required Fix

A code fix **is** required — and has now been **implemented** (2026-08-25, with explicit go-ahead):

- **File:** `FireProtection.Backend/Services/Placement/Sprinklers/Final/RevitSprinklerPlacementService.cs`
- **Class:** `RevitSprinklerPlacementService`
- **Methods:** `PlaceSinglePoint` (the `isFaceBased` detection) and `FindCeilingHost`
- **What must change:**
  1. **Hosted-family detection** must also treat `WorkPlaneBased` (and `FaceBased`) as hosted, because
     ceiling-hosted sprinklers are `WorkPlaneBased`. The current `"FaceBased"`-only check is the root cause.
  2. **Use the face-based overload** `NewFamilyInstance(hostRef, xyz, new XYZ(0,0,1), symbol)` for hosted
     families, with a ceiling **face reference** as the host.
  3. **Linked-ceiling hosting:** `FindCeilingHost` currently only scans the host document. In this model
     the ceilings live in the linked architectural model, so it returns `null` and placement would fail.
     It must also search linked models and return a linked `Reference` via
     `Reference.CreateLinkReference(linkInstance, faceRefInLink)`. (Pendent/upward-face selection already
     exists in `FindUpwardFaceReference`.)
  4. **Honest export (secondary):** record the **actual** `instance.Location` in `PlacedSprinklerEntry`
     instead of echoing the input `point`, so future diagnostics are not masked.
- **Do NOT:** add arbitrary Z offsets, hard-code levels, or "fix" by changing View Range.

### Implementation status (2026-08-25)
- `RevitSprinklerPlacementService.cs` updated:
  - `isFaceBased` detection matches `WorkPlaneBased` + `FaceBased`.
  - `FindCeilingHost` searches host + linked models; linked ceiling face references are converted to host
    references via `linkFaceRef.CreateLinkReference(linkInstance)` (the `Reference.CreateLinkReference(
    RevitLinkInstance)` instance method — the 2-arg static overload is not present in this API build).
  - `PlacedSprinklerEntry` records the ACTUAL `instance.Location`.
  - Temporary `TEMPORARY Z DIAGNOSTIC` instrumentation removed.
- **Build**: Revit2024/2026 compile clean (0 errors). Revit2025 compiles but its bin-copy is blocked by a
  VS/Revit lock on `FireProtection.UI.dll` (environmental, not a code error).
- **Verification**: static only. Runtime Revit verification is pending (re-run the add-in; expect Actual
  Instance XYZ ≈ (14.12, 31.95, 12) for Room 1235683).

## 7. Important

- The fix is now **implemented** in `RevitSprinklerPlacementService.cs` (see §6). The temporary
  diagnostic instrumentation has been removed. Runtime verification in Revit is the remaining step.
- The earlier `SPRINKLER_LEVEL_Z_PLACEMENT_FIX_REPORT.md` (which concluded Case C / View Range from
  static analysis) is **superseded** by this runtime evidence: the bug is in the placement overload, not
  View Range. See that file's correction note.
- The four critical values are now proven:
  - Candidate Z = 12, Passed Z = 12, **Actual Revit Z = 0**, L1 elevation = 0, Elevation-from-L1 = 0.
  - Therefore a **code fix is required**.
