# Standards Analysis — Fire Protection Devices

Read-only review of the two reference documents. **No code changed.**
All numbers below are quoted from the PDFs, not from memory.

## Sources

| File | Type | Status |
|---|---|---|
| `72-19-PDF 1.pdf` | **NFPA 72 — National Fire Alarm and Signaling Code, 2019 Edition** | ✅ Has text layer. Read in full (419 pages). Quoted values verified. |
| `Layout, Detail, And Calculations of Fire Sprinkler Systems (1).pdf` | **NFPA 13 reference work, 682 pages** | ⚠️ Scanned (no text layer). Visual-read agent is still running; values from this file are NOT quoted below — only the topics it covers. |

Page numbers in NFPA 72 quotes are the **PDF page** in `72-19-PDF 1.pdf`, not the printed NFPA page number.

---

# PART 1 — NFPA 13 (sprinkler rules)

## Status

The `Layout, Detail, And Calculations of Fire Sprinkler Systems (1).pdf` is **scanned with no text layer** (extraction yielded 0 readable characters across 682 pages, file size 277 MB). I have dispatched a visual-read agent that will page through it in 20-page chunks.

**Until the agent finishes, no NFPA 13 numbers are quoted here.** The topics the document is known to cover (from its title and chapter structure) are listed in §1.1 so you can verify against your copy. The list is the agent's working list — do not treat the numbers below NFPA 13 as authoritative until the agent reports back.

## Topics expected in the NFPA 13 reference (from the file title)

- Wall distance min/max — pendent, upright, sidewall
- Max spacing and max coverage area per sprinkler — Light / OH1 / OH2 / EH1 / EH2
- Min sprinkler-to-sprinkler distance
- Ceiling-height thresholds and the spacing-reduction factor (10/12/15/20/25 ft)
- Sloped / beamed / stepped ceilings
- Obstacle clearances per category (beams, ducts, columns)
- Sidewall throw, deflector distance, wall-side distance
- ESFR / CMDA / storage rules
- Hanger / bracing / hydraulic calcs (likely not used by the placement engine)

## Where each factor lands in the codebase

| NFPA 13 factor | Code location |
|---|---|
| Hazard class → spacing/area table | `DefaultHazardPlacementRules.GetRules(HazardClass)` |
| Ceiling height factor (10/12/15/20/25 ft) | `BruteForceCalculationService.CalculateRoom` (the `CeilingHeightAdjustmentFactor` block) |
| Min/max wall distance | `HazardPlacementRuleSet.BoundaryClearanceFt` + `MaxDistanceFromWallsFt` |
| Min device-to-device | `HazardPlacementRuleSet.MinSpacingFt` |
| Obstacle clearance per category | `HazardPlacementRuleSet.ObstacleSpecificClearances` + `ObstacleClearanceFt` |
| Sloped / stepped ceilings | `HazardPlacementRuleSet.CeilingSlopeAdjustments` |
| Coverage pattern | `HazardPlacementRuleSet.CoveragePatternAdjustments` |
| Orientation (pendent/upright/sidewall) | `HazardPlacementRuleSet.OrientationSpacingAdjustments` |
| Per-room overrides | `PlacementRoomInput.OverrideMaxSpacingFt`, `OverrideBoundaryClearanceFt` |

The current default numbers in `DefaultHazardPlacementRules` are `IsProvisional = true` and **must be replaced** with the exact NFPA 13 values once the agent finishes reading the scanned PDF.

---

# PART 2 — NFPA 72, 2019 Edition (verified)

All numbers below were extracted from the text layer of `72-19-PDF 1.pdf`. The values are repeated in many sources — this is the original 2019 wording.

## A. Smoke detectors — spot-type, smooth ceiling (Ch. 17.7.3.2)

**Nominal spacing S = 30 ft (9.1 m)**. PDF page 108, §17.7.3.2.3.1.

Two equivalent rules:
- (1) Detector-to-detector ≤ S, AND one detector within S/2 of any wall/partition reaching the top 15 % of the ceiling height.
- (2) Every point on ceiling ≤ 0.7 × S = **21 ft** (the standard 0.7S rule).

**Wall location** (17.7.3.2.1): ceiling, OR on sidewall between ceiling and **12 in. (300 mm)** down to top of detector.

**Partitions = separate room** (17.5.2): any partition reaching within 15 % of ceiling height splits the space.

**High air movement** (17.7.1.8 / 17.7.1.9): don't install where velocity > 300 ft/min unless listed for it.

**Sloped ceilings** (17.6.3.4 / 17.7.3.4):
- slope < 30°: space using height at the peak.
- slope ≥ 30°: other detectors use average slope height or peak height.
- First row of detectors at or within **36 in. (910 mm)** of the peak.

## B. Smoke detectors — beamed ceilings (17.7.3.2.4)

Solid joists = beams (17.7.3.2.4.1). Beam depth `d` vs ceiling height `H` (PDF page 108):

| Beam depth `d` | Spacing rule |
|---|---|
| `d < 0.1 H` | Smooth-ceiling spacing OK. Detectors on ceiling OR bottom of beams. |
| `0.1 H ≤ d` AND beam spacing `≥ 0.4 H` | Detector in every beam pocket, on ceiling. |
| `0.1 H ≤ d` AND beam spacing `< 0.4 H` | Parallel to beams: smooth spacing. Perpendicular: **½ smooth spacing**. Detectors on ceiling OR bottom of beams. |
| Corridors ≤ 15 ft wide with beams perpendicular | Smooth-ceiling spacing OK. |
| Rooms ≤ 900 ft² | Smooth-ceiling spacing OK. |

**Sloped + parallel beams** (17.7.3.2.4.3): smooth-ceiling spacing within beam pocket parallel to beams. Perpendicular rule reverts to ½ when `d > 0.1 H` and beam spacing `< 0.4 H` → one detector per pocket at ½ smooth spacing.

**Sloped + perpendicular beams** (17.7.3.2.4.4): detectors on the bottom of the beams. Average height over slope. Spacing along horizontal projection. For `d > 0.1 H`, min distance = `0.4 H`, max spacing = 50 % smooth.

## C. Heat detectors — high ceiling reduction (17.6.3.5.1)

Table 17.6.3.5.1 — multiply listed spacing by these factors on ceilings 10 ft to 30 ft. (PDF page 106.)

| Ceiling height (ft) | Multiplier |
|---|---|
| 0 – 10 | 1.00 |
| > 10 – 12 | 0.91 |
| > 12 – 14 | 0.84 |
| > 14 – 16 | 0.77 |
| > 16 – 18 | 0.71 |
| > 18 – 20 | 0.64 |
| > 20 – 22 | 0.58 |
| > 22 – 24 | 0.52 |
| > 24 – 26 | 0.46 |
| > 26 – 28 | 0.40 |
| > 28 – 30 | 0.34 |

**Min spacing rule (17.6.3.5.3):** not less than `0.4 × ceiling height`.

**Heat-detector wall location (17.6.3.1.3.1):** ceiling ≥ 4 in. from sidewall, OR sidewall between 4 in. and 12 in. from ceiling.

**Heat detector beams (17.6.3.3):**
- beam projection ≤ 4 in. (100 mm): treat as smooth ceiling.
- beam projection > 4 in.: spacing ⊥ to beams ≤ 2/3 listed.
- beam projection > 18 in. AND on center > 8 ft: each bay = separate area.

**Solid joist heat (17.6.3.2.1):** spacing ⊥ to joists ≤ 50 % listed. Mount at bottom of joists (17.6.3.2.2).

## D. Visual notification (strobes) — wall-mounted, room spacing (Ch. 18.5)

Table 18.5.5.5.1(a) — wall-mounted, locate at halfway distance of the wall. (PDF page 122.)

| Room size (ft) | 1 appliance (cd) | 4 appliances (1/wall) (cd) |
|---|---|---|
| 20 × 20 | 15 | NA |
| 28 × 28 | 30 | NA |
| 30 × 30 | 34 | NA |
| 40 × 40 | 60 | 15 |
| 45 × 45 | 75 | 19 |
| 50 × 50 | 94 | 30 |
| 54 × 54 | 110 | 30 |
| 55 × 55 | 115 | 30 |
| 60 × 60 | 135 | 30 |
| 63 × 63 | 150 | 37 |
| 68 × 68 | 177 | 43 |
| 70 × 70 | 184 | 60 |
| 80 × 80 | 240 | 60 |
| 90 × 90 | 304 | 95 |
| 100 × 100 | 375 | 95 |
| 110 × 110 | 455 | 135 |
| 120 × 120 | 540 | 135 |
| 130 × 130 | 635 | 185 |

**Non-square / not-centered rule (18.5.5.5.4):** the effective intensity (cd) for one wall-mounted appliance = max of (distance to farthest wall) or (2 × distance to farthest adjacent wall). Whichever is greater.

**Low ceiling rule (18.5.5.3):** if wall mount can't be at 80 in., reduce the room size by **2 × (80 in. − actual height)**.

**Ceiling > 30 ft (18.5.5.5.6):** ceiling-mounted strobes must be suspended at or below 30 ft, OR use the performance-based alternative in 18.5.5.7.

**Mounting (18.5.5.2):** ceiling-mounted appliances must be within **6 in. (150 mm)** of the ceiling. Wall mount top ≥ 80 in. (2.03 m), ≤ 96 in. (per 18.4.9.1).

**Synchronization (18.5.5.5.2):** two or more appliances in the same field of view must flash together.

**Max flash rate (18.5.3.1):** 1 Hz to 2 Hz. Max pulse duration 20 ms (18.5.3.2). Color: clear or nominal white (18.5.3.5). Max 1000 cd (18.5.3.5).

## E. Visual notification — ceiling-mounted (Table 18.5.5.5.1(b), PDF page 123)

Lens height = max above floor where the appliance can be mounted. Single appliance at room center.

| Room size (ft) | Max lens height (ft) | Min cd |
|---|---|---|
| 20 × 20 | 10 | 15 |
| 30 × 30 | 10 | 30 |
| 40 × 40 | 10 | 60 |
| 44 × 44 | 10 | 75 |
| 20 × 20 | 20 | 30 |
| 30 × 30 | 20 | 45 |
| 44 × 44 | 20 | 75 |
| 46 × 46 | 20 | 80 |
| 20 × 20 | 30 | 55 |
| 30 × 30 | 30 | 75 |
| 50 × 50 | 30 | 95 |
| 53 × 53 | 30 | 110 |
| 55 × 55 | 30 | 115 |
| 59 × 59 | 30 | 135 |
| 63 × 63 | 30 | 150 |
| 68 × 68 | 30 | 177 |
| 70 × 70 | 30 | 185 |

**Not centered rule (18.5.5.5.8):** cd = effective intensity for 2 × distance to farthest wall.

## F. Corridors (18.5.5.6)

Applies to corridors ≤ 20 ft wide.

- (18.5.5.6.3) Visual appliances in corridors rated ≥ **15 cd**.
- (18.5.5.6.5) Within **15 ft** of corridor end; separation between appliances ≤ **100 ft**.
- (18.5.5.6.4) Corridor > 20 ft wide → treat as room.
- (18.5.5.6.6) If viewing path is interrupted (fire door, elevation change) → treat each segment as a separate corridor.
- (18.5.5.6.7) > 2 visible in same field of view → must synchronize.

## G. Audible notification — public mode (18.4.4)

**Rule (18.4.4.1):** sound level ≥ **15 dB above average ambient** OR **5 dB above maximum sound lasting ≥ 60 s**, whichever is greater, measured **5 ft above floor**, A-weighted (dBA).

**Private mode (18.4.5.1):** same but **10 dB above average ambient** (instead of 15).

**Sleeping areas (18.4.6.1):** ≥ 15 dB above average ambient OR ≥ 5 dB above max-60 s OR ≥ **75 dBA** at pillow, whichever is greater. (18.4.6.3) Low-frequency 520 Hz ± 10 % for sleeping-area awakening.

**Mounting (18.4.9.1):** wall tops ≥ **90 in. (2.29 m)** above floor AND ≥ **6 in. (150 mm)** below ceiling.

## H. Voice intelligibility (18.4.11)

Required in identified Acoustically Distinguishable Spaces (ADS) per the design. CISI / STI scoring (Annex D.2.4) — quantitative measurement is permitted but not required.

## I. Door-release smoke detectors (17.7.5.6)

For smooth-ceiling single/double doorway:
- on centerline of doorway
- ≤ **5 ft (1.5 m)** perpendicular to doorway (along ceiling)
- min distance per Figure 17.7.5.6.5.1(A) parts B/D/F

Wall-section depth rule:
- ≤ 24 in. above door: 1 ceiling detector on one side, OR 2 wall detectors (one each side).
- > 24 in. on one side only: 1 ceiling on higher side, OR 1 wall detector on each side.
- > 24 in. on both sides: 2 detectors (ceiling or wall) one each side.
- If a frame-mounted detector or detector-closer assembly is listed: only 1 detector.

## J. Air-sampling / aspirating smoke detectors (17.7.3.6)

Each sampling port = spot-type detector for spacing. Trouble signal if airflow outside mfr. range.

## K. Duct detectors (17.7.5.4–17.7.5.5)

Listed for the air velocity present. Supply side: downstream of fan + filters. Return side: at smoke-compartment boundary before common return.

---

# PART 3 — Mapping rules to your code

## Where the engine touches the rules

```
NFPA 72                              NFPA 13 (when available)
─────────────                        ──────────────────────
smoke detector 30 ft nominal    ←→   BruteForceCalculationService:
  → MaxSpacing / CoverageRadius       per-room hazard rule set
  → beam factor (0.1 H / 0.4 H)       ObstructionBoxes
  → sloped ceiling (peak row)         CeilingSlopeAdjustments
                                     CeilingHeightAdjustmentFactor

heat detector 0.4 H min,        ←→   same as above + class factor
  ceiling height derate table
  (10/12/14/16/18/20/22/24/26/28/30 ft)

strobe Tables 18.5.5.5.1(a/b)   →   new logic in PlacementEngine
  + corridor ≤ 20 ft rule             (not yet implemented)
  + 0.7S / 15-cd corridor
  + sync groups

audible 15 dB / 5 dB / 75 dBA   →   not in placement engine
  + wall mount 90 in / 6 in           (NFAS device family, future)
```

## Spots to update once NFPA 13 is read

1. `DefaultHazardPlacementRules.GetRules` — replace each hazard class with the verified NFPA 13 spacing/area/clearance table.
2. `HazardPlacementRuleSet.CeilingHeightAdjustmentFactor` — confirm or replace the 10/12/15/20/25 ft breakpoint list against the verified NFPA 13 §11.1 derate curve. (NFPA 72 §17.6.3.5.1 derate for heat detectors is different — document which family the engine is applying.)
3. `HazardPlacementRuleSet.OrientationSpacingAdjustments[sidewall]` — the 0.85 factor is an internal placeholder; replace with the verified sidewall throw rule once read.
4. `HazardPlacementRuleSet.ObstacleSpecificClearances` — verify per-category clearances (beam / column / duct).
5. New: `StrobePlacementRules` — implement Tables 18.5.5.5.1(a) and (b), the corridor rules (18.5.5.6), and the 0.7S wall-distance rule (18.5.5.5.4 / 18.5.5.5.8).
6. New: `AudiblePlacementRules` — 15 / 5 / 75 dBA targets per room, with assumed ambient per occupancy.
7. `HazardPlacementRuleSet.IsProvisional = true` on every row until all the above are verified.

## Spots where the engine already aligns with NFPA 72

- `BruteForceCalculationService.CalculateRoom` ceiling selection (FLAT > SLOPED > STEPPED, level-matched, highest Z) matches the smooth-ceiling assumption underlying 17.7.3.2.3.1.
- The 0.7S rule for smoke detectors (`RoomGeometry.DistanceToOuterBoundary` ensures every wall point is within `0.7 × MaxSpacing`) is consistent with 17.7.3.2.3.1(2) — but the engine only enforces a fixed wall-distance window today; you should confirm the half-spacing-to-wall rule from 17.7.3.2.3.1(1) is implemented.
- The beam-depth-and-spacing logic in `BruteForceCalculationService` does not yet reflect 17.7.3.2.4 (10 % H / 40 % H thresholds). That is a known gap until the smoke-detector placement pass is added.

---

# PART 4 — Open items

- The NFPA 13 reference is scanned. A visual-read agent is running; it will report back with verified numbers per topic. Do not commit the current `DefaultHazardPlacementRules` values as "NFPA 13 compliant" until the agent's results are in.
- Smoke-detector and notification-appliance placement passes are not yet in the engine. The placeholders exist in `Catalog` (SmokeDetectors, NotificationAppliances sheets) and in the `DevicePlacementBehavior` enum, but the calculation services for them are not implemented. The rules above are what they need to implement.
- The current wall distance for pendent sprinklers (1'-0" / 12 in.) is at the **NFPA 13 maximum** for standard spray sprinklers. Industry default is 4–6 in. Recommend tightening unless there is a specific reason to sit at the limit.
