


1. Sprinklers
    1: Know the rules for this room
        `MaxSpacingFt` — e.g. 15 ft for Light hazard (How far apart max?)[212]
        `BoundaryClearanceFt` — e.g. 1 ft (How close to walls minimum?)[213]
        `CoverageRadiusFt` — e.g. 7.5 ft (How big is one sprinklers circle?)[214]
    2: Make a grid of possible dots 
        pacing dots on imaginary graph
        Every intersection = one candidate dot. Spacing between lines ≈ 1 ft (fine grid). [92]
    3.Deletion of non eligible dots
        A dot is deleted if:
            1. Outside the room (fell off the outline or into a hole)[208]
            2. Too close to a wall (within boundary clearance — e.g. < 1 ft from wall)[214]
            3. Inside/near a beam, column, or duct (and that obstacle is actually at ceiling height — a low beam doesn't count)[248]
            4. Too close to an existing sprinkler already in the model[212]
     4: Pick the final positions
        Normal ceiling sprinkler -> centered rectangle:
            Ex->    Room is 40 ft × 30 ft, max spacing 15 ft
                    Need `ceil(40/15)=3` columns, `ceil(30/15)=2` rows
                    Space them evenly: step = 40/3 ≈ 13.3 ft, 30/2 = 15 ft
                    First line is half a step from each edge (so it's centered, not jammed against a wall)[212,214,612]
        
        Sidewall (wall-mounted) sprinkler -> dots along the walls only:
             Walk each wall, place heads every ~spacing apart, first/last head ≤ half-spacing from corners
             Stand each head 0.5 ft off the wall, pointing into the room
             Pick which walls to use by: "which wall covers the most uncovered floor?" Keep adding walls until the room is covered (or no wall helps)   [228]   

2. Smoke Detectors
    1: Know the rules for this room
        MaxSpacingFt — e.g. 30 ft (How far apart max?)[Section 17.7.3.2.3.1, Page 72-105]
        MinSpacingFt — e.g. 10 ft (How close two detectors can be) [Section A.17.6.3.5.3, Page 72-233]
        MaxCoverageAreaSqFt — e.g. 900 sq ft (How much floor one detector covers) [Annex A.17.6.3.1.1, Page 72-231]
        CoverageRadiusFt — e.g. ~21.2 ft (= 30/√2, one detector's circle)[Annex A.17.6.3.1.1, Page 72-231]
        MinBoundaryClearanceFt — e.g. 0.333 ft / 4 in (How close to walls minimum, ceiling mount)[Section 17.7.3.2.1, Page 72-104 & Figure A.17.6.3.1.3.1, Page 72-234]
        MaxDistanceFromWallsFt — e.g. 15 ft (Never further than this from any wall)
        ExistingDetectorSeparationFt — e.g. 15 ft (Stay away from detectors already in model)[Annex A.17.6.3.1.1, Page 72-231]

    2: Make a grid of possible dots
        Same graph-paper idea: intersections ≈ 1 ft apart (capped by coverage radius)
        Sloped ceiling also adds extra dots within 3 ft of the roof peak (smoke rises there) [Section 17.7.3.3.1, Page 72-106]

    1. Deletion of non-eligible dots
        A dot is deleted if:
            1. Outside the room (outline or hole)
            2. Too close to a wall (< 4 in for ceiling mount) [Section 17.7.3.2.1, Page 72-105]
            3. Inside/near a beam, column, duct at ceiling height — plus HVAC supply registers need extra ~3 ft keep-away (don't put detector in the supply air blast) [Figure A.29.11.3.4(4)(b), Page 72-311]
            4. Too close to an existing detector already in the model [Section A.17.6.3.5.3, Page 72-233]
            4: Pick the final positions
            Small room (one detector enough) → center of room:
                Ex → Room 25 ft × 25 ft = 625 sq ft ≤ 900, max dimension ≤ ~1.8 × coverage radius [Section 17.7.3.2.4.2(5), Page 72-106]
                Take the valid dot closest to the room's centroid → done, one detector dead center 
            Bigger room (need several) → start center, spread outward:
                Sort all valid dots by distance to room center (center first)
                Walk the list:
                    Skip if already inside a chosen/existing detector's coverage circle (~21.2 ft)
                    Skip if closer than min spacing (~10 ft) to a detector already picked
                    Otherwise keep it
                    Ex → 40 ft × 40 ft room: center dot first, then next-nearest uncovered dots until floor is covered [Annex A.17.6.3.1.1, Figures A.17.6.3.1.1(e)–(h), Pages 72-233 to 72-234]
            Wall-mounted detector → dots along the walls only:
                - Walk each wall, drop dots every ~max-spacing (30 ft) apart
                - Stand each dot 0.15 ft off the wall
                - Height = ceiling − ~0.5 ft
                - Same filters (obstacle / existing), then same center-out pick [Section 17.7.3.2.1, Page 72-105]
  
3. Notification Appliances (horn/strobe)
    1: Know the rules for this room
        Spacing comes from the type rating (e.g. Speaker|candela=110|dba=90):
        Visible (strobe) max spacing by candela:
            - ≤15 → 30 ft | ≤30 → 35 ft | ≤75 → 40 ft | ≤110 → 45 ft | >110 → 50 ft [Table 18.5.5.5.1(a), Page 72-114 & Table 18.5.5.5.1(b), Page 72-115]
        Audible (horn) max spacing by dBA:
            - <85 → 20 ft | <90 → 25 ft | <95 → 30 ft | ≥95 → 35 ft [Section 18.4.4, Pages 72-248]
        Then:
            - Stricter (smaller) of visible vs audible wins (combined appliance = tighter constraint)
            - Sloped/peaked ceiling ×0.80, stepped ×0.75
            - Wall mount ×0.85
            - Derived: CoverageRadiusFt = spacing/√2, MinSpacingFt ≈ 35% of max (floor 5 ft)
            - Boundary clearance: ceiling 0.333 ft, wall ~0.05 ft [section 18.4.9.3,72-118]
    2: Make a grid of possible dots
        Same graph paper as smoke detectors — often finer step (max spacing / 3, min 1 ft) because audible/visible spacing can be tighter [Section 18.5.5.5, Page 72-114 to 119]
    3. Deletion of non-eligible dots
        Same four filters as smoke detectors:
        1. Outside the room
        2. Too close to wall (ceiling: 4 in; wall: ~0.05 ft)
        3. Inside/near obstacle at placement height
        4. Too close to an existing device
        4: Pick the final positions

            Ceiling mount → same as smoke detector small/big room logic:
                Small room → nearest valid dot to center
                Bigger room → sort by distance to center; skip if already covered by chosen/existing appliance's circle; skip if closer than min spacing; keep otherwise
            Wall mount → walk the walls (same as wall smoke detector):
                Dots every ~spacing along each wall, 0.5 ft off wall face, dropped 0.5 ft below ceiling
                Same filters, then same center-out / keep-or-skip selection [18.5.5,Page 72-119]













# Automated Device Placement Algorithm & Rules (Annotated with References)

---

## 1. Sprinklers

### Step 1: Know the rules for this room
* **MaxSpacingFt**: e.g. **15 ft** for Light hazard *(How far apart max?)*
  * **Reference:** NFPA 13 Textbook (`_layout_part3_p161-240.pdf`), **Page 212** & **Page 214**; (`_layout_part4_p241-320.pdf`), **Page 258**.
* **BoundaryClearanceFt**: e.g. **4 in (0.33 ft)** minimum to **7.5 ft** maximum *(How close to walls minimum/maximum?)*
  * **Reference:** NFPA 13 Textbook (`_layout_part3_p161-240.pdf`), **Pages 213–214** & **Page 217** *(Small Room Rule: up to 9 ft from a single wall)*.
* **CoverageRadiusFt**: e.g. **11.25 ft** max throw to corner (\\(0.75 \times S\\)); Max protection area **225 sq ft** for Light Hazard *(How big is one sprinkler's circle?)*
  * **Reference:** NFPA 13 Textbook (`_layout_part3_p161-240.pdf`), **Pages 214–215, Table 19-1**.

---

### Step 2: Make a grid of possible dots
* Place candidate dots on an imaginary graph grid.
* Every intersection = one candidate dot.
* Spacing between grid lines \\(\approx 1\text{ ft}\\) (fine grid).
  * **Reference:** NFPA 13 Layout Principles (`_layout_part2_p81-160.pdf`), **Page 92** & **Page 102**.

---

### Step 3: Deletion of non-eligible dots
A candidate dot is deleted if:
1. **Outside the room** (fell off the outline or into an interior void/hole).
   * **Reference:** NFPA 13 Textbook (`_layout_part3_p161-240.pdf`), **Page 208**.
2. **Too close to a wall** (within boundary clearance \\(< 4\text{ in}\\) / \\(0.33\text{ ft}\\) from wall).
   * **Reference:** NFPA 13 Textbook (`_layout_part3_p161-240.pdf`), **Page 214**.
3. **Inside/near a beam, column, or duct at ceiling height** (applying the "Beam Rule" or "Three Times Rule"; low obstacles below clearance plane excluded).
   * **Reference:** NFPA 13 Textbook (`_layout_part4_p241-320.pdf`), **Pages 248–251, Sections 8.6.5.1.2 & 8.6.5.2.1.3**.
4. **Too close to an existing sprinkler** already placed in the model (less than \\(6\text{ ft}\\) minimum separation to avoid cold soldering).
   * **Reference:** NFPA 13 Textbook (`_layout_part3_p161-240.pdf`), **Page 212**.

---

### Step 4: Pick the final positions

#### Normal Ceiling Sprinkler \\(\rightarrow\\) Centered Rectangle Layout:
* **Example:** Room is \\(40\text{ ft} \times 30\text{ ft}\\), max allowable spacing is \\(15\text{ ft}\\).
  * Calculate required columns: \\(\lceil 40 / 15 \rceil = 3\text{ columns}\\).
  * Calculate required rows: \\(\lceil 30 / 15 \rceil = 2\text{ rows}\\).
  * Space evenly: step along length \\(= 40 / 3 \approx 13.3\text{ ft}\\), step along width \\(= 30 / 2 = 15\text{ ft}\\).
  * First line is positioned at half a step (\\(S/2\\)) from each wall edge to ensure a centered layout.
* **Reference:** NFPA 13 Textbook (`_layout_part3_p161-240.pdf`), **Pages 212–214** & (`_layout_part9_p641-682.pdf`), **Page 612**.

#### Sidewall (Wall-Mounted) Sprinkler \\(\rightarrow\\) Dots Along Perimeter Walls Only:
* Walk each wall, drop heads every \\(\sim\text{spacing}\\) apart (\\(14\text{ ft}\\) max for light hazard).
* First and last heads mounted \\(\le \text{half-spacing}\\) (\\(7\text{ ft}\\)) from room corners.
* Stand each head \\(4\text{ in}\\) to \\(6\text{ in}\\) (\\(0.33\text{ ft} - 0.5\text{ ft}\\)) below the ceiling, pointing into the room.
* Wall selection criteria: Choose walls that cover the maximum uncovered floor area iteratively.
* **Reference:** NFPA 13 Textbook (`_layout_part3_p161-240.pdf`), **Page 228, Table 19-2**.

---
---

## 2. Smoke Detectors

### Step 1: Know the rules for this room
* **MaxSpacingFt**: **30 ft** on smooth ceilings *(How far apart max?)*
  * **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Section 17.7.3.2.3.1, Page 72-105** & **Annex A.17.6.3.1.1, Page 72-231**.
* **MinSpacingFt**: **10 ft** minimum separation between detectors *(or \\(0.4H\\) under thermal plume considerations on high ceilings)*
  * **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Section A.17.6.3.5.3, Page 72-233**.
* **MaxCoverageAreaSqFt**: **900 sq ft** (\\(30\text{ ft} \times 30\text{ ft}\\) standard square rating)
  * **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Annex A.17.6.3.1.1, Page 72-231**.
* **CoverageRadiusFt**: **21.2 ft** (\\(0.7 \times \text{Listed Spacing} = 0.7 \times 30\text{ ft}\\))
  * **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Annex A.17.6.3.1.1, Page 72-231** & **Figure A.17.6.3.1.1(d), Page 72-232**.
* **MinBoundaryClearanceFt**: **4 in (0.333 ft)** minimum distance from sidewall for ceiling-mounted devices
  * **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Section 17.7.3.2.1, Page 72-105** & **Figure A.17.6.3.1.3.1, Page 72-234**.
* **MaxDistanceFromWallsFt**: **15 ft** (\\(0.5 \times \text{Listed Spacing}\\)) maximum distance to any wall boundary
  * **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Figure A.17.6.3.1.1(a), Page 72-231**.
* **ExistingDetectorSeparationFt**: **15 ft** minimum buffer away from pre-existing detectors in model.
  * **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Annex A.17.6.3.1.1, Page 72-231**.

---

### Step 2: Make a grid of possible dots
* Generate graph-paper candidate grid (\\(1\text{ ft}\\) step capped by coverage radius).
* **Sloped/Peaked Ceilings:** Add additional candidate points within **36 in (3 ft)** horizontally of the roof peak.
  * **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Section 17.7.3.3.1, Page 72-106** & **Figure A.17.6.3.4(a), Page 72-234**.

---

### Step 3: Deletion of non-eligible dots
A candidate dot is deleted if:
1. **Outside the room** outline or interior void.
2. **Too close to a wall** (\\(< 4\text{ in}\\) / \\(0.333\text{ ft}\\) for ceiling mount).
   * **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Section 17.7.3.2.1, Page 72-105**.
3. **Inside/near architectural obstructions** (beams, ducts) or within **3 ft (36 in)** of HVAC supply air registers to avoid airflow turbulence interference.
   * **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Section 17.7.6.3.2, Page 72-110** & **Figure A.29.11.3.4(4)(b), Page 72-311**.
4. **Too close to an existing detector** already placed in the model (\\(< 10\text{ ft}\\) separation).
   * **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Section A.17.6.3.5.3, Page 72-233**.

---

### Step 4: Pick the final positions

#### Small Room (Single Detector Sufficient):
* **Criteria:** Area \\(\le 900\text{ sq ft}\\) and room diagonal/dimensions fit within \\(21.2\text{ ft}\\) coverage radius.
* Place single detector at the valid candidate point closest to the geometric centroid of the room.
* **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Section 17.7.3.2.4.2(5), Page 72-106** & **Annex A.17.6.3.1.1, Page 72-231**.

#### Large Room (Multiple Detectors Required):
1. Sort all valid candidate grid dots by distance to the room centroid (center-out outward expansion).
2. Iterate through candidate list:
   * **Skip** if point is already within the \\(21.2\text{ ft}\\) coverage radius of an already selected detector.
   * **Skip** if point is closer than \\(10\text{ ft}\\) minimum separation to an existing detector.
   * **Select** point if it covers previously unprotected floor space.
3. **Example:** \\(40\text{ ft} \times 40\text{ ft}\\) room (\\(1600\text{ sq ft}\\)) requires 4 detectors arranged in a \\(20\text{ ft} \times 20\text{ ft}\\) grid pattern centered in the space.
* **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Annex A.17.6.3.1.1, Figures A.17.6.3.1.1(e)–(h), Pages 72-233 to 72-234**.

#### Wall-Mounted Detector Option:
* Drop candidate points along wall perimeter only.
* Position top of detector between **4 in and 12 in** down from the ceiling.
* Horizontal placement \\(0.15\text{ ft}\\) off wall face, spaced \\(\le 30\text{ ft}\\) apart.
* **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Section 17.7.3.2.1, Page 72-105** & **Figure A.17.7.3.2.1, Page 72-235**.

---
---

## 3. Notification Appliances (Horn/Strobe)

### Step 1: Know the rules for this room

#### Visible (Strobe) Max Spacing by Candela (cd) Rating:
* \\(\le 15\text{ cd} \rightarrow \mathbf{30\text{ ft}}\\) spacing (\\(20\text{ ft} \times 20\text{ ft}\\) room rating)
* \\(\le 30\text{ cd} \rightarrow \mathbf{35\text{ ft}}\\) spacing (\\(30\text{ ft} \times 30\text{ ft}\\) room rating)
* \\(\le 75\text{ cd} \rightarrow \mathbf{40\text{ ft}}\\) spacing (\\(40\text{ ft} \times 40\text{ ft}\\) room rating)
* \\(\le 110\text{ cd} \rightarrow \mathbf{45\text{ ft}}\\) spacing (\\(54\text{ ft} \times 54\text{ ft}\\) room rating)
* \\(> 110\text{ cd} \rightarrow \mathbf{50\text{ ft}}\\) spacing (\\(60\text{ ft} \times 60\text{ ft}\\) or greater room rating)
* **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Table 18.5.5.5.1(a), Page 72-114** & **Table 18.5.5.5.1(b), Page 72-115**.

#### Audible (Horn) Max Spacing by dBA Rating:
* \\(< 85\text{ dBA} \rightarrow \mathbf{20\text{ ft}}\\) spacing
* \\(< 90\text{ dBA} \rightarrow \mathbf{25\text{ ft}}\\) spacing
* \\(< 95\text{ dBA} \rightarrow \mathbf{30\text{ ft}}\\) spacing
* \\(\ge 95\text{ dBA} \rightarrow \mathbf{35\text{ ft}}\\) spacing
* **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Section 18.4.3, Pages 72-112 to 72-113**.

#### Constraint Derivations & Ceiling Adjustments:
* **Combined Appliances:** Tighter/smaller spacing constraint of visible vs. audible rating wins.
* **Sloped/Peaked Ceilings:** Multiply allowable max spacing by factor of **0.80**.
* **Stepped Ceilings:** Multiply allowable max spacing by factor of **0.75**.
* **Wall Mounting:** Multiply spacing by **0.85**.
* **Derived Formulas:** \\(\text{CoverageRadiusFt} = \text{Spacing} / \sqrt{2}\\); \\(\text{MinSpacingFt} \approx 35\%\\) of max spacing (floor threshold \\(5\text{ ft}\\)).
* **Boundary Clearance:** Ceiling mount \\(= 0.333\text{ ft}\\) (\\(4\text{ in}\\)); Wall mount \\(= 0.05\text{ ft}\\).
* **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Section 18.5.5, Page 72-114** & **Annex A.18.5.5.5, Page 72-257**.

---

### Step 2: Make a grid of possible dots
* Fine candidate grid (step size \\(= \text{Max Spacing} / 3\\), minimum \\(1\text{ ft}\\)) on ceiling or wall planes.
* **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Section 18.5.5.5, Page 72-114**.

---

### Step 3: Deletion of non-eligible dots
Delete candidate dots if:
1. **Outside the room** boundaries.
2. **Too close to walls/corners** (Ceiling mount \\(< 4\text{ in}\\) / \\(0.333\text{ ft}\\); Wall mount \\(< 0.05\text{ ft}\\)).
3. **Inside/near structural obstructions** at appliance mounting height (\\(80\text{ in}\\) to \\(96\text{ in}\\) AFF for wall appliances).
   * **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Section 18.5.5.1, Page 72-114**.
4. **Too close to an existing notification appliance** (\\(< \text{MinSpacingFt}\\)).

---

### Step 4: Pick the final positions

#### Ceiling Mount Appliance:
* **Small Room:** Select valid candidate dot closest to the geometric center of the room.
* **Large Room:** Sort candidate dots by distance from center; select points ensuring full strobe/horn polar coverage overlap without violating minimum device separation.
* **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Annex A.18.5.5.5, Figures A.18.5.5.5(a)–(c), Pages 72-257 to 72-258**.

#### Wall Mount Appliance (Walking Walls Algorithm):
* Drop candidate points along perimeter walls every \\(\sim\text{spacing}\\) apart.
* Stand off \\(0.5\text{ ft}\\) from wall face; mounting height \\(= 80\text{ in}\\) to \\(96\text{ in}\\) above finished floor (or \\(6\text{ in}\\) below ceiling).
* Select combination of wall positions that covers the maximum square area without visual obstructions.
* **Reference:** NFPA 72 2019 (`72-19-PDF 1.pdf`), **Section 18.5.5.1, Page 72-114** & **Section 18.5.5.6 (Corridor Spacing), Page 72-116**.                






















































































































--------------------------------------------------------------------
-------------------------------------------------


# DEVICE PLACEMENT AUTOMATION — DETAILED LOGIC EXPLANATION

## 1. SPRINKLERS

### 1.1 KNOW THE RULES FOR THIS ROOM

Before placing any sprinkler, the system first needs to know the **rules that apply to the room**.

For example, for a **Light Hazard** room:

```text
MaxSpacingFt = 15 ft
BoundaryClearanceFt = 1 ft
CoverageRadiusFt = 7.5 ft
```

These values answer three basic questions:

```text
MaxSpacingFt
→ How far apart can two sprinklers be?

BoundaryClearanceFt
→ How close can a sprinkler be to the wall?

CoverageRadiusFt
→ How far can one sprinkler effectively cover?
```

Think of these values as the **rules of the game**.

The placement algorithm should not start placing sprinklers until it knows these values.

---

# 1.2 MAKE A GRID OF POSSIBLE DOTS

The system does **not immediately place sprinklers**.

First, it creates many possible locations.

Imagine the room has graph paper placed over it:

```text
•───•───•───•───•
│   │   │   │   │
•───•───•───•───•
│   │   │   │   │
•───•───•───•───•
│   │   │   │   │
•───•───•───•───•
```

Every `•` is a **candidate point**.

For example:

```text
Room = 40 ft × 30 ft

Fine grid ≈ 1 ft × 1 ft
```

So the system may create hundreds or thousands of possible points.

These points are **NOT sprinklers yet**.

They only mean:

> "A sprinkler could potentially be placed here."

The reason for making a fine grid is that the ideal sprinkler location may later be blocked by a beam, duct, column, existing sprinkler, etc.

So the algorithm needs many alternative locations.

### Important distinction

```text
Fine grid
    ↓
"Where CAN I put a sprinkler?"

Final placement
    ↓
"Where SHOULD I put a sprinkler?"
```

---

# 1.3 DELETE NON-ELIGIBLE DOTS

Now the system checks every candidate point.

Each point must pass several tests.

If it fails a test, the point is **deleted**.

---

### FILTER 1 — POINT MUST BE INSIDE THE ROOM

If the point is outside the room boundary:

```text
Room
┌──────────────────────┐
│ •   •   •   •        │
│                      │
│ •   •   •            │
│                  •   │ ← outside
└──────────────────────┘
```

Delete it.

If the room has a hole/opening:

```text
┌──────────────────────┐
│ •   •   •   •        │
│      ┌─────┐         │
│ •    │     │    •    │
│      │hole │         │
│ •    └─────┘    •   │
└──────────────────────┘
```

Points inside the hole are also deleted.

So:

```text
Candidate
   ↓
Inside room?
   ├── NO → DELETE
   └── YES → continue
```

---

### FILTER 2 — POINT MUST NOT BE TOO CLOSE TO THE WALL

Suppose:

```text
BoundaryClearanceFt = 1 ft
```

Then a candidate less than 1 ft from the wall is removed.

```text
WALL
████████████████████████

     ✕  ← 0.5 ft → DELETE

       • ← 1 ft → allowed
```

The important idea is:

```text
Distance from wall < BoundaryClearanceFt
                    ↓
                  DELETE
```

---

### FILTER 3 — POINT MUST NOT CONFLICT WITH AN OBSTACLE

The system checks objects such as:

```text
Beam
Column
Duct
Other ceiling obstruction
```

But the obstacle only matters if it actually affects the sprinkler's placement height.

For example:

```text
CEILING
────────────────────────

████████  ← beam at ceiling
    ✕     ← sprinkler cannot go here


──────────────

        ████ ← low beam
              ↓

          •   ← may still be valid
```

Therefore the algorithm checks:

```text
Is candidate inside obstacle?
        +
Does obstacle actually exist at sprinkler placement height?
        ↓
YES → DELETE
```

A low object that does not interfere with the sprinkler placement plane does not automatically invalidate the point.

---

### FILTER 4 — EXISTING SPRINKLER SEPARATION

Suppose an existing sprinkler is already here:

```text
        Existing
           S
           
       •  •  •
```

If the candidate is too close to the existing sprinkler:

```text
Distance(candidate, existing sprinkler)
        <
ExistingSprinklerSeparationFt

                    ↓

                  DELETE
```

This prevents the algorithm from creating a new sprinkler directly beside an existing one.

---

# 1.4 PICK THE FINAL SPRINKLER POSITIONS

After filtering, the system has:

```text
Many original points
        ↓
Remove invalid points
        ↓
Valid candidate points
        ↓
Choose actual sprinkler positions
```

The selection process depends on the mounting type.

There are two major cases:

```text
1. Ceiling-mounted sprinkler
2. Sidewall-mounted sprinkler
```

---

# 1.4.1 CEILING-MOUNTED SPRINKLER

Suppose:

```text
Room = 40 ft × 30 ft
MaxSpacingFt = 15 ft
```

First calculate how many sprinkler positions are required.

### X direction

```text
40 / 15 = 2.67
```

You cannot have 2.67 columns.

So:

```text
ceil(40 / 15) = 3 columns
```

### Y direction

```text
30 / 15 = 2
```

So:

```text
ceil(30 / 15) = 2 rows
```

Therefore:

```text
3 columns × 2 rows

= 6 sprinkler positions
```

---

### Center the positions

The system does not put the first sprinkler directly against the wall.

Instead:

```text
Room width = 40 ft
Columns = 3

Step = 40 / 3
     ≈ 13.33 ft
```

Similarly:

```text
Room height = 30 ft
Rows = 2

Step = 30 / 2
     = 15 ft
```

The first position is placed approximately **half a step from the boundary**.

Conceptually:

```text
Wall
│
│    S          S          S
│
│
│    S          S          S
│
└──────────────────────────────
```

This creates a centered grid instead of:

```text
Wall
│S─────────────────────────────
│
│
│S─────────────────────────────
```

The objective is:

```text
Room
   ↓
Calculate required rows/columns
   ↓
Create evenly spaced positions
   ↓
Center them in the room
```

---

# 1.4.2 WHAT IF AN IDEAL POSITION IS BLOCKED?

The calculated ideal position might look like:

```text
        Beam
      ███████
          ✕
          ↑
    ideal sprinkler position
```

The system should not necessarily give up.

It already has the fine candidate grid.

So it can:

```text
Ideal position
      ↓
Find nearby valid candidate
      ↓
Move/snap to nearest valid candidate
```

Conceptually:

```text
        •   •
          •
          ✕ ← ideal
        •   •
```

If a valid nearby point exists:

```text
✕ → •
```

If no acceptable point exists:

```text
Ideal position
      ↓
No valid nearby candidate
      ↓
Skip / flag for review
```

This is why the fine grid is important.

---

# 1.4.3 SIDEWALL SPRINKLER

Sidewall sprinklers work differently.

Instead of creating a rectangular ceiling grid, the system works with the **room walls**.

Conceptually:

```text
┌──────────────────────────────┐
S      S      S      S        S
│                              │
│                              │
│                              │
S      S      S      S        S
└──────────────────────────────┘
```

The algorithm:

```text
1. Find room walls
2. Walk along the wall
3. Determine how many heads are required
4. Distribute them along the wall
5. Keep them within the allowed corner distance
6. Place the sprinkler slightly inside the room
```

---

### Spacing along the wall

If:

```text
MaxSpacing = S
```

the algorithm tries to keep the sprinkler spacing within that limit.

The first and last sprinklers are constrained relative to the wall corners.

Conceptually:

```text
Corner
│
│---- S/2 ---- S -------- S ---- S/2 ----│
             ↑          ↑
          sprinkler   sprinkler
```

The idea is:

```text
First sprinkler
    ↓
not too far from first corner

Last sprinkler
    ↓
not too far from second corner
```

---

### Stand-off from wall

The sprinkler is not placed exactly on the wall line.

For example:

```text
WALL
████████████████████████

        S
        •
        ↑
      0.5 ft

       ROOM
```

So:

```text
Wall point
    ↓
Offset inward
    ↓
Actual sprinkler point
```

The sprinkler is then oriented toward the room.

---

### Which walls should be used?

For a sidewall system, the algorithm evaluates the walls based on coverage.

Conceptually:

```text
Wall A → covers this area
Wall B → covers this area
Wall C → covers this area
Wall D → covers this area
```

The system selects a useful wall and checks:

```text
Is the entire room covered?
```

If not:

```text
Choose another useful wall
        ↓
Add another row
        ↓
Check coverage again
```

Continue until:

```text
Room covered
        OR
No useful wall remains
```

---

# 2. SMOKE DETECTORS

## 2.1 KNOW THE RULES FOR THIS ROOM

Before placing smoke detectors, the system determines the applicable values.

Example:

```text
MaxSpacingFt = 30 ft
MinSpacingFt = 10 ft
MaxCoverageAreaSqFt = 900 ft²
CoverageRadiusFt ≈ 21.2 ft
MinBoundaryClearanceFt ≈ 0.333 ft
MaxDistanceFromWallsFt = 15 ft
ExistingDetectorSeparationFt = 15 ft
```

These mean:

```text
30 ft
→ maximum detector spacing rule

10 ft
→ minimum separation between detectors

900 ft²
→ maximum coverage area used by the rule set

21.2 ft
→ derived coverage radius used by the algorithm

0.333 ft
→ approximately 4 inches from wall

15 ft
→ maximum distance from wall

15 ft
→ separation from existing detector
```

---

# 2.2 MAKE A GRID OF POSSIBLE DETECTOR LOCATIONS

Again, the algorithm first creates possible points.

```text
•──•──•──•──•──•
│  │  │  │  │  │
•──•──•──•──•──•
│  │  │  │  │  │
•──•──•──•──•──•
```

Each point means:

> "A smoke detector could potentially go here."

The grid is approximately 1 ft apart, subject to the configured grid rules.

For a sloped ceiling, additional candidates can be created near the roof peak.

Conceptually:

```text
        /\
       / •\
      / •  \
     /      \
    /________\
```

The extra points near the peak account for smoke accumulating near the high point.

---

# 2.3 DELETE NON-ELIGIBLE DETECTOR POINTS

Every candidate is tested.

---

### FILTER 1 — OUTSIDE ROOM

```text
Inside room?
    ↓
NO → DELETE
YES → continue
```

Points inside room holes are also deleted.

---

### FILTER 2 — TOO CLOSE TO WALL

For ceiling-mounted detectors:

```text
Distance to wall < required clearance
                    ↓
                  DELETE
```

Example:

```text
WALL
████████████████████

 ✕       •
 ↑       ↑
too      valid
close
```

---

### FILTER 3 — OBSTACLE / HVAC

The detector cannot simply be placed wherever the ceiling is geometrically empty.

The system checks:

```text
Beam
Column
Duct
Other obstruction
HVAC supply register
```

For an HVAC supply register, an additional keep-away distance is applied.

Conceptually:

```text
       HVAC
        ↓
       [A]
    ↙       ↘
   ✕         ✕
        •
```

The area around the supply register becomes invalid.

---

### FILTER 4 — EXISTING DETECTOR

If there is already a detector:

```text
        Existing
           D
      ↙         ↘
   invalid    invalid
```

Candidates that violate the required separation are removed.

---

# 2.4 PICK FINAL SMOKE DETECTOR POSITIONS

There are two main situations.

```text
Small room
    ↓
One detector may be enough

Large room
    ↓
Multiple detectors may be required
```

---

# 2.4.1 SMALL ROOM

Suppose:

```text
Room = 25 ft × 25 ft

Area = 625 ft²
```

If the applicable rules allow one detector to cover the room:

```text
Room
┌─────────────────────────┐
│                         │
│                         │
│           D             │
│                         │
│                         │
└─────────────────────────┘
```

The algorithm calculates the room centroid.

For a simple rectangle:

```text
centerX = width / 2
centerY = height / 2
```

So:

```text
centerX = 25 / 2 = 12.5 ft
centerY = 25 / 2 = 12.5 ft
```

Then:

```text
Find valid candidate closest to centroid
        ↓
Place detector there
```

Important:

The detector is not blindly placed at the mathematical center.

It chooses the **valid grid point closest to the center**.

---

# 2.4.2 LARGE ROOM

For a larger room, one detector may not cover everything.

The algorithm works approximately like this:

```text
1. Generate valid candidate points
2. Calculate room center
3. Sort candidates by distance from center
4. Start with the candidate closest to center
5. Check remaining candidates
6. Add candidates that provide additional coverage
7. Continue until coverage requirements are satisfied
```

Example:

```text
┌────────────────────────────────────┐
│                                    │
│                                    │
│                  D1                │
│                                    │
│                                    │
│                                    │
└────────────────────────────────────┘
```

After placing `D1`, the algorithm asks:

```text
Does D1 cover everything?
```

If yes:

```text
DONE
```

If no:

```text
Find another useful candidate
```

For example:

```text
┌────────────────────────────────────┐
│                                    │
│       D2             D1            │
│                                    │
│                                    │
│                                    │
└────────────────────────────────────┘
```

Then check again.

---

### Candidate rejection during final selection

A candidate can still be valid geometrically but unnecessary.

Example:

```text
        D1
      (coverage)
    ┌───────────┐
    │  • • • •  │
    │  • • • •  │
    └───────────┘
```

If candidate `•` is already covered by an existing/selected detector, it can be skipped.

Also:

```text
Distance(candidate, selected detector)
        <
MinSpacingFt
        ↓
      SKIP
```

So the final selection has two important questions:

```text
Is this location physically allowed?
        AND
Does adding a detector here actually help?
```

---

# 2.4.3 WALL-MOUNTED SMOKE DETECTOR

For wall-mounted detectors, the placement logic changes.

Instead of selecting arbitrary ceiling-grid points:

```text
Walk along walls
      ↓
Create candidate locations
      ↓
Offset from wall
      ↓
Apply filters
      ↓
Select valid positions
```

Conceptually:

```text
████████████████████████
      D       D       D
│                      │
│                      │
│                      │
████████████████████████
```

The detector is positioned slightly away from the wall and at the appropriate elevation.

The system then checks:

```text
Obstacle?
Existing detector?
Wall constraints?
Coverage?
Spacing?
```

Only valid positions remain.

---

# 3. NOTIFICATION APPLIANCES — HORN / STROBE

## 3.1 KNOW THE RULES FOR THIS ROOM

Notification appliances are different because there can be **two separate requirements**:

```text
VISIBLE
↓
Strobe / candela requirement

AUDIBLE
↓
Horn / dBA requirement
```

Example:

```text
Speaker:
Candela = 110 cd
dBA = 90 dBA
```

The system calculates the maximum spacing allowed by each requirement.

---

# 3.2 VISIBLE SPACING

The strobe's candela rating determines the spacing category.

Conceptually:

```text
Candela
   ↓
Determine maximum visible spacing
```

Example table from the provided rule set:

```text
Candela ≤ 15   → 30 ft
Candela ≤ 30   → 35 ft
Candela ≤ 75   → 40 ft
Candela ≤ 110  → 45 ft
Candela > 110  → 50 ft
```

So:

```text
110 cd
    ↓
45 ft visible spacing
```

---

# 3.3 AUDIBLE SPACING

The horn's dBA rating determines the audible spacing.

Example:

```text
<85 dBA  → 20 ft
<90 dBA  → 25 ft
<95 dBA  → 30 ft
≥95 dBA  → 35 ft
```

So if:

```text
Speaker = 110 cd + 90 dBA
```

then:

```text
Visible = 45 ft
Audible = 25 ft
```

The system uses the tighter requirement:

```text
min(45, 25)
      ↓
   25 ft
```

So the working maximum spacing becomes:

```text
MaxSpacing = 25 ft
```

---

# 3.4 CEILING / SLOPE / WALL ADJUSTMENTS

The rule set then applies the configured orientation/ceiling adjustments.

For example:

```text
Sloped/peaked ceiling
        ↓
spacing × 0.80
```

Stepped ceiling:

```text
spacing × 0.75
```

Wall-mounted:

```text
spacing × 0.85
```

Conceptually:

```text
Base spacing
      ↓
Visible limit
      ↓
Audible limit
      ↓
Take smaller
      ↓
Apply applicable adjustment
      ↓
Final working spacing
```

The final number is what the placement algorithm uses.

---

# 3.5 DERIVED VALUES

From the final spacing:

```text
CoverageRadiusFt = MaxSpacingFt / √2
```

And the configured minimum spacing can be derived/assigned according to the rule set.

The important idea is:

```text
MaxSpacing
     ↓
Controls how far devices can be separated

CoverageRadius
     ↓
Used to reason about coverage around a device
```

---

# 3.6 MAKE A GRID OF POSSIBLE POINTS

Now create the fine candidate grid.

```text
•──•──•──•──•──•
│  │  │  │  │  │
•──•──•──•──•──•
│  │  │  │  │  │
•──•──•──•──•──•
```

The notification-appliance grid may be finer because the system wants enough possible locations to find valid positions.

The configured logic uses approximately:

```text
grid step = MaxSpacing / 3
```

with limits applied to keep the grid from becoming too coarse or too small.

Again:

```text
Grid points ≠ final devices
```

They are only possible locations.

---

# 3.7 DELETE NON-ELIGIBLE POINTS

Each notification candidate goes through the same basic filtering concept.

### FILTER 1

```text
Outside room?
→ DELETE
```

### FILTER 2

```text
Too close to wall?
→ DELETE
```

### FILTER 3

```text
Inside obstacle / invalid placement zone?
→ DELETE
```

The obstacle must be relevant to the appliance's placement height.

### FILTER 4

```text
Too close to existing device?
→ DELETE
```

After this:

```text
Original grid
      ↓
Invalid points removed
      ↓
Valid notification candidates
```

---

# 3.8 PICK FINAL CEILING-MOUNTED DEVICES

For ceiling-mounted notification appliances, the final selection follows the same basic concept:

```text
Valid candidates
      ↓
Find center
      ↓
Sort by distance from center
      ↓
Take useful candidate
      ↓
Check coverage
      ↓
Check spacing
      ↓
Continue until coverage is satisfied
```

For a small room:

```text
One valid candidate near center
        ↓
       DONE
```

For a larger room:

```text
          D1
     
   D2          D3
     
          D4
```

The algorithm keeps adding devices when existing devices do not sufficiently cover the room.

---

# 3.9 PICK FINAL WALL-MOUNTED DEVICES

For wall-mounted notification appliances:

```text
Find walls
    ↓
Walk along walls
    ↓
Generate possible positions
    ↓
Offset from wall
    ↓
Apply obstacle/existing-device filters
    ↓
Select useful positions
```

Conceptually:

```text
WALL
████████████████████████████

   D        D        D

            ROOM
```

The exact vertical and horizontal placement depends on the configured mounting rules.

---

# 4. THE COMPLETE ALGORITHM — ALL THREE SYSTEMS

## SPRINKLER

```text
ROOM
 ↓
Determine hazard/rules
 ↓
Get:
  MaxSpacing
  MinSpacing
  Coverage
  Wall clearance
  Other constraints
 ↓
Generate fine candidate grid
 ↓
Delete candidates:
  ├─ Outside room
  ├─ Inside hole
  ├─ Too close to wall
  ├─ Obstacle conflict
  └─ Existing sprinkler conflict
 ↓
Determine ideal sprinkler layout
 ↓
 ┌──────────────────────┐
 │ Ceiling mounted?     │
 └──────────┬───────────┘
            │
       YES  │  NO
            │
            ↓
     Centered grid       Sidewall logic
            ↓                 ↓
      Validate spacing    Walk walls
            ↓                 ↓
      Validate coverage   Add wall rows
            ↓                 ↓
            └───────┬─────────┘
                    ↓
             Final sprinklers
```

---

# SMOKE DETECTOR

```text
ROOM
 ↓
Determine detector rules
 ↓
Get:
  MaxSpacing
  MinSpacing
  Coverage
  Wall clearance
  Existing separation
 ↓
Generate fine candidate grid
 ↓
Add special candidates
  └─ Peak area for sloped ceiling
 ↓
Delete candidates:
  ├─ Outside room
  ├─ Inside hole
  ├─ Too close to wall
  ├─ Obstacle conflict
  ├─ HVAC conflict
  └─ Existing detector conflict
 ↓
Determine room center
 ↓
Sort valid candidates by distance from center
 ↓
Select useful candidates
 ↓
For every candidate:
  ├─ Already covered?
  │      └─ YES → SKIP
  │
  ├─ Too close to another detector?
  │      └─ YES → SKIP
  │
  └─ Otherwise → SELECT
 ↓
Check coverage
 ↓
Final smoke detectors
```

---

# NOTIFICATION APPLIANCE

```text
ROOM
 ↓
Read appliance rating
 ↓
Determine visible spacing
 ↓
Determine audible spacing
 ↓
Take smaller value
 ↓
Apply applicable:
  ├─ Ceiling condition
  └─ Mounting orientation
 ↓
Final MaxSpacing
 ↓
Generate fine candidate grid
 ↓
Delete candidates:
  ├─ Outside room
  ├─ Inside hole
  ├─ Wall conflict
  ├─ Obstacle conflict
  └─ Existing device conflict
 ↓
Determine ideal positions
 ↓
 ┌────────────────────────┐
 │ Ceiling mounted?       │
 └───────────┬────────────┘
             │
       YES   │   NO
             │
             ↓
      Center-out selection   Wall walking
             ↓                    ↓
      Coverage validation    Coverage validation
             ↓                    ↓
             └────────┬───────────┘
                      ↓
             Final appliances
```

# THE CORE IDEA

All three systems follow the same fundamental pattern:

```text
1. KNOW THE RULES
       ↓
2. CREATE MANY POSSIBLE LOCATIONS
       ↓
3. DELETE LOCATIONS THAT ARE NOT ALLOWED
       ↓
4. SELECT LOCATIONS THAT ACTUALLY PROVIDE COVERAGE
       ↓
5. VALIDATE THE FINAL RESULT
```

The major difference is **how the final locations are selected**:

```text
SPRINKLER
→ Centered grid OR wall-based directional placement

SMOKE DETECTOR
→ Center-first coverage selection OR wall-based placement

NOTIFICATION APPLIANCE
→ Center-first coverage selection OR wall-based placement
```

And the most important architectural separation is:

```text
CANDIDATE GENERATION
        ↓
"Where is it physically possible?"

FILTERING
        ↓
"Where is it physically/legalistically invalid?"

FINAL SELECTION
        ↓
"Which valid locations should actually receive a device?"

VALIDATION
        ↓
"Does the final set satisfy spacing and coverage?"
```
