# Location Point Logic — ELI5 (Sprinklers, Smoke Detectors, Notification Appliances)

Plain-English explanation of how this project decides **where each device point goes** in a room.
Code is the source of truth; all rule **values** below are provisional (`HasApprovedRules = false`).

---

## 1. Sprinklers — How the Dot Gets Its Spot

**Goal:** put dots (sprinklers) on the floor plan of one room.

### Step 1: Know the rules for this room

- How far apart max? (`MaxSpacingFt` — e.g. 15 ft for Light hazard)
- How close to walls minimum? (`BoundaryClearanceFt` — e.g. 1 ft)
- How big is one sprinkler's circle? (`CoverageRadiusFt` — e.g. 7.5 ft)

These get shrunk if the ceiling is high, sloped, or the head is sideways.

### Step 2: Make a grid of possible dots

Imagine laying graph paper over the room:

```
·  ·  ·  ·  ·
·  ·  ·  ·  ·
·  ·  ·  ·  ·
```

Every intersection = one candidate dot. Spacing between lines ≈ 1 ft (fine grid).

### Step 3: Throw away bad dots

A dot is **deleted** if:

1. **Outside the room** (fell off the outline or into a hole)
2. **Too close to a wall** (within boundary clearance — e.g. < 1 ft from wall)
3. **Inside/near a beam, column, or duct** (and that obstacle is actually at ceiling height — a low beam doesn't count)
4. **Too close to an existing sprinkler** already in the model

What's left = list of **valid** dots.

### Step 4: Pick the final positions

**Normal ceiling sprinkler** → centered rectangle:

- Room is 40 ft × 30 ft, max spacing 15 ft
- Need `ceil(40/15)=3` columns, `ceil(30/15)=2` rows
- Space them evenly: step = 40/3 ≈ 13.3 ft, 30/2 = 15 ft
- First line is **half a step** from each edge (so it's centered, not jammed against a wall)

```
        ← 13.3 → ← 13.3 → ← 13.3 →
   ┌────●────────●────────●────┐  ↑
   │                           │  15
   │                           │  ↓
   └────●────────●────────●────┘
```

If a target lands on a bad spot (obstacle, too close to wall), it **snaps** to the nearest valid fine-grid dot nearby. If nothing nearby is free, that cell is skipped.

**Sidewall (wall-mounted) sprinkler** → dots along the walls only:

- Walk each wall, place heads every ~spacing apart, first/last head ≤ half-spacing from corners
- Stand each head 0.5 ft off the wall, pointing into the room
- Pick which walls to use by: "which wall covers the most uncovered floor?" Keep adding walls until the room is covered (or no wall helps)

### Step 5: Double-check after picking

- Any two chosen dots too far apart? → warn
- Any dot too far from a wall? → warn
- Any floor area outside every sprinkler's circle? → warn
- Everything provisional (rules not FPE-approved)? → always warn "needs review"

### TL;DR

**Grid the room → delete bad dots → lay a centered rectangle (or walk the walls for sidewall) → audit spacing/coverage → flag for review.**

---

## 2. Smoke Detectors — How the Dot Gets Its Spot

**Goal:** put dots (smoke detectors) on the floor plan of one room.

### Step 1: Know the rules for this room

Defaults for a normal ceiling photoelectric detector (provisional NFPA 72 values):

- Max spacing: **30 ft**
- Min spacing between two detectors: **10 ft**
- Max coverage: **900 sq ft** (one detector)
- Coverage circle radius: **~21.2 ft** (30 / √2)
- Stay at least **4 in (0.333 ft)** off the wall (ceiling mount)
- Never further than **15 ft** from any wall
- Keep **15 ft** away from an existing detector

Rules get tighter when:

| Situation | What happens |
|---|---|
| **Sloped / peaked ceiling** | Spacing ×0.90, coverage area shrinks |
| **Stepped ceiling** | Spacing ×0.85 |
| **High airflow** (ACH table) | Coverage shrinks (e.g. 60+ ACH → 125 sq ft) |
| **Wall mount** | Spacing ×0.90, sits ~0.5 ft below ceiling, almost on the wall (0.05 ft) |
| **Beam detector** | Different numbers (60 ft path etc.); room must be long enough for the beam path or it refuses |

### Step 2: Make a grid of possible dots

Same graph-paper idea as sprinklers: lay a fine grid (~1 ft, capped by coverage radius) over the room outline.

### Step 3: Throw away bad dots

A dot is **deleted** if:

1. **Outside the room** (outline or hole)
2. **Too close to the wall** (< 4 in for ceiling mount; tighter rules for wall mount)
3. **Inside/near an obstacle** at ceiling height — including **HVAC supply registers** (extra ~3 ft keep-away so the detector isn't blasted by supply air)
4. **Too close to an existing detector** (within existing-separation, default 15 ft)

What's left = **valid** dots.

### Step 4: Pick the final positions

**Small room (one detector is enough):**

- If the room fits in one coverage circle (area ≤ 900 sq ft and not too deep),
- pick the valid dot **closest to the room's center** → done. One detector, dead center.

**Bigger room (need several):**

1. Sort all valid dots by **distance to room center** (center first).
2. Walk the list:
   - Skip if already inside a chosen/existing detector's coverage circle.
   - Skip if closer than **min spacing** to a detector already picked.
   - Otherwise **keep it**.
3. Result: detectors spread from the middle outward, never too close, never redundant.

**Wall-mounted detector** → only dots along the walls:

- Walk each wall edge, drop dots every ~max-spacing apart, **0.15 ft off the wall**,
- height = ceiling minus ~0.5 ft,
- same obstacle/existing filters, then the same center-out selection.

**Sloped ceiling extra row:**

- Smoke rises to the high point, so on a steep slope the code **also** offers extra dots within **3 ft of the roof peak** (NFPA 72 peak-row idea) and tends to place the first detector there.

### Step 5: Double-check / honesty flags

- No valid dots at all → hard fail ("nothing can go here").
- Missing/unsupported ceiling → warn / block (can't host the device).
- Beam detector in a room shorter than the listed path → refuse and say "use a point detector."
- Rules not approved → always "needs review."

### TL;DR

**Grid the room → throw out dots near walls/obstacles/old detectors → small room = one in the middle, big room = start center and spread out by coverage → wall mount = walk the walls → flag for review.**

---

## 3. Notification Appliances (horns/strobes) — How the Dot Gets Its Spot

**Goal:** put dots (horn/strobe appliances) on the floor plan so people can **see** the flash and **hear** the alarm.

Notification appliances **reuse the same smoke-detector engine** (same room grid, same obstacle/existing filters). Only the **spacing numbers** and a few extras differ.

### Step 1: Know the rules for this room

Spacing is driven by the appliance's **rating**, parsed from the type name (e.g. `Speaker|candela=110|dba=90`):

**Visible (strobe) spacing by candela:**

| Candela | Max spacing |
|---|---|
| ≤ 15 | 30 ft |
| ≤ 30 | 35 ft |
| ≤ 75 | 40 ft |
| ≤ 110 | 45 ft |
| > 110 | 50 ft |

**Audible (horn/speaker) spacing by dBA:**

| dBA | Max spacing |
|---|---|
| < 85 | 20 ft |
| < 90 | 25 ft |
| < 95 | 30 ft |
| ≥ 95 | 35 ft |

Then:

1. **The stricter (smaller) of visible vs audible wins.** Combined appliance = tighter constraint.
2. Ceiling slope: sloped/peaked ×0.80, stepped ×0.75.
3. Wall mount: ×0.85.
4. Coverage radius = spacing / √2; min spacing ≈ 35% of max spacing (floor 5 ft).

All values provisional (`IsProvisional = true`).

### Step 2: Make a grid of possible dots

Same graph paper as smoke detectors — often a **finer step** (`max spacing / 3`, min 1 ft) because audible/visible spacing can be tighter.

### Step 3: Throw away bad dots

Same four filters as smoke detectors:

1. Outside the room
2. Too close to wall (ceiling: 4 in; wall mount: ~0.05 ft)
3. Inside/near obstacle at placement height
4. Too close to an existing device

### Step 4: Pick the final positions

- **Ceiling mount:** same center-out greedy as smoke detectors — cover first, don't bunch (min-spacing guard uses the audible/visible min, not the smoke default).
- **Wall mount:** walk the walls like a wall smoke detector (standoff + drop below ceiling), same filters, same selection.

### Step 5: Audible sound check (separate audit)

There's a post-placement **sound-level audit** (`AudibleCoverageEngine`):

1. Required dBA = ambient + 15 dB (or +5 over max sustained; **≥75 dBA if sleeping area**).
2. Sample the room on a coarse grid.
3. At each sample, estimate loudness from the nearest appliance: listed dBA at 10 ft, drop with distance, minus surface absorption, minus wall crossings, minus obstacles.
4. Any sample below the requirement → **coverage gap** warning with worst deficit and location.

Note: this engine is wired for **internal/test use** today; the main placement path still drives points from the spacing grid above.

### TL;DR

**Parse candela/dBA → take the stricter spacing → same grid + same bad-dot filters as smoke detectors → center-out (ceiling) or wall-walk (wall) → optionally audit actual sound levels → always flag provisional rules for review.**

---

## Side-by-side (one glance)

| | Sprinkler | Smoke detector | Notification appliance |
|---|---|---|---|
| **Standard** | NFPA 13 | NFPA 72 Ch. 17 | NFPA 72 Ch. 18 |
| **Default max spacing** | 15 ft (Light) | 30 ft | From candela/dBA table (stricter wins) |
| **Ceiling pick** | Centered rectangle | Center first, then spread | Same as smoke detector |
| **Wall pick** | Sidewall row solver | Wall walk (0.15 ft off) | Wall walk |
| **Wall min distance** | ~1 ft (hazard) | 4 in (ceiling) | 4 in / ~0.05 ft (wall) |
| **Obstacle Z check** | Yes | Yes | Yes |
| **Extra** | Height/slope/orientation shrink | Airflow, slope, peak row, beam path | Slope/wall shrink, dBA audit |
| **Approved rules?** | No (provisional) | No (provisional) | No (provisional) |
