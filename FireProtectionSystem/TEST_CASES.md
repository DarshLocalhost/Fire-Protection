# TEST_CASES — Sprinkler Placement (beginner runbook)

**Purpose:** let someone who is new to Revit verify, click by click, whether the sprinkler
placement feature in this repository works as implemented.

**Model used:** `HOUSE_MODEL` (Revit 2025): 16 rooms on 2 floors, 7 ceilings.
**Written:** 2026-10-23, checked against the current source code and the open model.
**Honesty note:** these tests have **not** been run in Revit yet. Every expected number below was
derived from the source code and the model data, not from a live run. A test only "passes" when
you actually see the result in Revit.

**Big rule:** the tool's rule set is still provisional (no fire-protection engineer has signed it
off). So **"Review Required" is the correct result, not a failure.** A run that says
"PROVISIONAL RULES - engineering review required" is doing exactly what it was built to do.
See [§8](#8-what-is-not-a-failure).

---

## 0. The 10-second version

If you only have ten minutes, run these four:

| Test | What it proves |
|---|---|
| **T3** | One sprinkler appears in a small room, at the right height. |
| **T4** | A bigger room gets **two** sprinklers (coverage rule). |
| **T9** | A sidewall sprinkler mounts on a wall, not in mid-air. |
| **T12** | The report tells the truth (provisional warning, counts, export). |

If all four pass, the core feature works. The remaining tests cover edge cases.

---

## 1. Plain-English words used below

| Word | Means |
|---|---|
| **Sprinkler head / head** | One fire sprinkler. |
| **Pendant** | A sprinkler that hangs down from the ceiling. |
| **Upright** | A sprinkler that stands up under a roof/deck. |
| **Sidewall** | A sprinkler mounted on a wall near the ceiling. |
| **Deflector** | The small metal disc/plate on the head that spreads the water. |
| **Spacing (S→S)** | Distance from one head to the nearest other head. |
| **Wall distance (S→W)** | Distance from a head to the nearest wall. |
| **ft²** | Square feet — a measure of floor area. 225 ft² ≈ 20.9 m². |
| **Hazard class** | How flammable the room contents are. These tests use **LIGHT**. |
| **Provisional** | Placeholder rules that still need engineer sign-off. |
| **Review Required** | The tool did the work but a human must check it. This is expected here. |

---

## 2. What you need

- Revit 2025 with the **FireProtection** add-in installed and the ribbon tab **Fire Protection**.
- A **copy** of `HOUSE_MODEL`.
- At least **two sprinkler family files** (`.rfa`):
  - one **pendant / ceiling-hosted** sprinkler family, and
  - one **sidewall** sprinkler family.
  - Both must be **Sprinkler** category families. A generic-model family will not appear.
- If you do not have these `.rfa` files, the tool cannot place anything. Ask for the project's
  sprinkler families first; this runbook cannot work without them.

---

## 3. One-time setup

### 3.1 Make a test copy (do not skip)

1. Open `HOUSE_MODEL` in Revit 2025.
2. **File → Save As → Project** and save as `HOUSE_MODEL_TEST_COPY.rvt`.
3. Do every test below in `HOUSE_MODEL_TEST_COPY.rvt`.

> Placement writes real sprinkler elements into the model. Undo works, but a copy is safer.

### 3.2 Open the tool once (before loading families)

1. Ribbon tab **Fire Protection**.
2. Panel **Fire Protection** → button **Fire Protection**.
3. Wait for the window titled **Fire Protection System** to open and fill the room list.
4. Leave it open for **T1** and **T2**.

### 3.3 Load the two sprinkler families

1. In the tool's top bar, click **Load families…** (or use Revit's **Insert → Load Family**).
2. Multi-select the pendant `.rfa` and the sidewall `.rfa`; confirm the load summary.
3. **Close the tool window (X) and open it again** from the ribbon button.
   - This is required: the family dropdown is built when the window opens. Reopening also
     re-reads the model, which the advanced tests need.

### 3.4 Learn the screen (30 seconds)

| Area | What it is |
|---|---|
| **SPRINKLER CONFIGURATION** (top) | **Sprinkler Family** and **Sprinkler Type** dropdowns. Start here. |
| **LEVELS** (left) | Tick a level to show its rooms. Empty levels are faded. One toggle button (top right) flips between **Select All** and **Clear All**; another button flips **Hide unselected / Show all**. |
| **Room list** (right) | One row per room: checkbox, **ROOM NAME**, **AREA (FT²)**, **HAZARD**, **CEILING TYPE**, **FAMILY / TYPE**, **S→S**, **WALL**, **S→W**, **TILE**, **RESET**. It has its own **Select All / Clear All** toggle at the top right. |
| **Eligible rooms only** | Hides rooms the tool cannot place in. Untick it to see all 16 rooms. |
| **Allow open structure (deck/unhosted)** | Lets rooms without a ceiling be placed at level height. |
| **OBSTRUCTION CATEGORIES** | Which objects (columns, beams, ducts…) block a head. Leave the defaults. |
| **IF ROOM ALREADY HAS DEVICES** | What a second run does: Skip / Add / Replace. |
| **Place Sprinklers** (bottom right) | Runs the calculation and creates the heads. |
| **Cancel** | Appears during a run; stops at the next room and undoes the whole run. |
| **Report window** | Opens automatically: status cards, room table, issues, **Export JSON / Export CSV**. |

### 3.5 Read the unit before typing an override

The two override columns show the project's own length unit in their headers:

- `S→S (MM)` and `WALL (MM)` → type **millimetres**
- `S→S (FT)` and `WALL (FT)` → type **feet**

Do not guess. On this model the header will most likely say **(MM)**.

| You want | If (MM), type | If (FT), type |
|---|---|---|
| 1 in | 25 | 0.08 |
| 4 in | 102 | 0.33 |
| 6 in | 152 | 0.5 |
| 1 ft | 305 | 1 |
| 3 ft | 914 | 3 |
| 6 ft | 1829 | 6 |
| 7.5 ft | 2286 | 7.5 |
| 8 ft | 2438 | 8 |
| 9 ft | 2743 | 9 |
| 15 ft | 4572 | 15 |

**AREA stays in ft²** no matter what the project unit is. That is deliberate.

---

## 4. Model facts used by the tests

Areas below were read from the model. They are what drives the minimum head count.

| Room (as shown in the tool) | Floor | Area ft² | Minimum heads at LIGHT | Used by |
|---|---|---|---|---|
| W.C 8 | 1. Ground Floor | 29.2 | 1 | T3, T8, T9 |
| ENSUITE 5 | 1. Ground Floor | 66.5 | 1 | backup for T9 |
| STUDY R-04 | 1. Ground Floor | 159.4 | 1 | T7, T14 |
| SITTING ROOM R-02 | 1. Ground Floor | 229.0 | **2** | optional |
| KITCHEN R-01 | 1. Ground Floor | 532.9 | **3** | T5, T6, T10 |
| BEDROOM 5 10 | 2. First Floor | 244.1 | **2** | T4, T11 |
| LANDING 16 | 2. First Floor | 184.2 | 1 | optional |

Minimum heads = round **up** (area ÷ 225 ft²). Example: 244.1 ÷ 225 = 1.08 → 2 heads.

### Heights (ceiling underside is 2525 mm above each level)

| Floor | Level elevation | Ceiling underside | Pendant deflector (1 in below) | Sidewall deflector (4 in below) |
|---|---|---|---|---|
| 1. Ground Floor | 0.100 m | 2.625 m | **2.600 m** | **2.523 m** |
| 2. First Floor | 3.000 m | 5.525 m | **5.500 m** | **5.423 m** |

All 7 ceilings are type `Ceiling-45_Metal_Stud-15P`, offset **2525 mm**, so these heights apply
wherever a ceiling is found. If you load an **upright** family instead of a pendant, the drop is
**6 in**, so expect 2.473 m / 5.373 m. Family-origin caveat is in [§8](#8-what-is-not-a-failure).

---

## 5. Core tests

### T1 — The tool opens and sees the whole model

**Why:** proves the extraction found the right rooms and levels before anything is placed.

**Do:**
1. Open the tool.
2. In **LEVELS**, make sure the list is not filtered: untick **Levels with rooms only** if it is ticked.
   Make sure both **1. Ground Floor** and **2. First Floor** are ticked (click the level toggle
   until **Select All** is shown, then tick those two if needed).
3. Untick **Eligible rooms only** in the room list.
4. Read the level rows and the room list.

**Expect:**
- **5 levels**: `-1 Foundation`, `0. Ground Level`, `1. Ground Floor`, `2. First Floor`, `3. Roof`.
- The two floors with rooms show **9/9** and **7/7**.
- The other three levels are faded and their checkboxes are disabled.
- **16 rooms** total: 9 on `1. Ground Floor`, 7 on `2. First Floor`.
- Room names/areas match the table in §4.

**PASS:** 16 rooms and 5 levels, no crash.
**FAIL:** wrong count, missing floor, or the window did not open.

---

### T2 — A missing family is explained, not crashed

**Why:** the most common first-run problem is "no sprinkler family loaded". The tool must say so.

**Do:** before loading any family (this is why you opened the tool in step 3.2):
1. Look under the **Sprinkler Family** dropdown.
2. Select a room if you can, then click **Place Sprinklers**.

**Expect:**
- A visible message containing: `No sprinkler families found in the open model — load families with "Load families..." in the header, or switch to Catalog file.`
- **Place Sprinklers** does nothing, or is disabled. Nothing is created.
- No Revit error dialog and no crash.

**PASS:** clear message, no device, no crash.
**FAIL:** silent nothing, or an exception/Revit crash.

> If you already loaded families, this test is **N/A** — note it and continue.

---

> **How to select one room (used by every test below)**
>
> 1. In **LEVELS**, click the toggle button (top right of the level list) until **no level** is
>    ticked. Then tick the one level named in the test.
> 2. In the room list, click the toggle button (top right of the room list) until **no room** is
>    ticked. Then tick the one room named in the test.
>
> Each button alternates between **Select All** and **Clear All**, so click it again if you get the
> wrong action. Selecting only one room on one level is what makes the expected counts exact.

### T3 — One head in a small room (W.C 8, pendant)

**Why:** the simplest possible placement. If this fails, stop and report it; nothing else will work.

**Do:**
1. In **SPRINKLER CONFIGURATION**, choose the **pendant** family and one of its types.
2. Select only **W.C 8** (see *How to select one room* above).
3. In the W.C 8 row, set **HAZARD** to **LIGHT**.
4. Click **Place Sprinklers** (bottom right).
5. Wait for the **SPRINKLER Placement Report** window.
6. Read the report, then close it.
7. In Revit, select the new head and check its elevation (see "Checking a head's height" below).

**Expect:**
- Exactly **1** sprinkler in W.C 8.
- Report cards: **Succeeded 0**, **Failed 0**, **Skipped 0**, **Review Required 1**, **Devices Placed / Requested 1 / 1**.
- Room row **Status** = `ReviewRequired` (no space; the summary badge may show `REVIEWREQUIRED`).
- Row **Message** contains: `The calculation engine flagged this room (ReviewRequired).`
- Report **Rules line** starts: `PROVISIONAL RULES - engineering review required.`
- **Issues & Warnings** contains a provisional/engineering-review line.
- The head is at least **12 in (305 mm)** from every wall.
- The head elevation is **≈ 2.600 m** on Ground Floor (±25 mm).

**PASS:** all of the above.
**FAIL:** zero heads, more than one head, no message, or elevation near 2.625 m / 0.000 m.

> If you loaded an **upright** family, replace 2.600 m with **2.473 m** (6-in drop).

**After the test:** close the report, then press **Ctrl+Z once** to remove this run before T8/T9.
(One run = one undo step by design.)

#### Checking a head's height (beginner method)

1. Click the placed sprinkler in a 3D view or section.
2. Open **Properties**.
3. Look for **Elevation** (or **Offset from Level**). Read the value.
4. If the family does not show either, open a **Section** through the room
   (**View → Section**) and use **Measure** between the ceiling and the head.

The exact number depends on where the family's insertion point sits; judge the **drop below the
ceiling**, not the absolute number, if your family's origin is not the deflector.

---

### T4 — A large room gets two heads (BEDROOM 5 10)

**Why:** proves the **coverage-area** rule, not just "one head per room".
244.1 ft² ÷ 225 ft² = 1.08, so one head is not enough.

**Do:**
1. Keep the pendant family.
2. Select only **BEDROOM 5 10** (level **2. First Floor**; see *How to select one room*).
3. Hazard **LIGHT**; **Place Sprinklers**.
4. Read the report and close it.

**Expect:**
- **At least 2 heads.**
- Distance from each head to its nearest neighbour is **between 6 ft and 15 ft**
  (1.83 m – 4.57 m).
- Every head is **within 7.5 ft (2.29 m)** of a wall.
- Every head is **at least 12 in (305 mm)** from a wall.
- Elevation **≈ 5.500 m** (First Floor).
- Message does **not** contain `not fully covered`.
- Status `ReviewRequired`, overall `REVIEWREQUIRED` (provisional).

If exactly 2 heads, the ideal layout is: **9.37 ft** apart, **4.68 ft** from the two short walls,
**6.51 ft** from the two long walls.

**PASS:** ≥ 2 heads and every bullet above.
**FAIL:** exactly 1 head (coverage violation) or a head outside the room.

**Do not undo this run** — T11 uses it.

---

### T5 — Spacing and wall distance in a long room (KITCHEN R-01)

**Why:** proves the **maximum spacing** and **maximum wall distance** rules in the largest room.

**Do:**
1. Pendant family; select only **KITCHEN R-01** (level **1. Ground Floor**).
2. Hazard **LIGHT**; **Place Sprinklers**; read the report; close it.

**Expect:**
- **At least 3 heads** (532.9 ÷ 225 = 2.37 → 3).
- Nearest-neighbour spacing **6–15 ft**.
- Every head **within 7.5 ft** of a wall and **at least 12 in** from a wall.
- **Area per head ≤ 225 ft²** (532.9 ÷ number of heads).
- No `not fully covered` message.
- Elevation **≈ 2.600 m**.

Record the actual count. If it is exactly **3 in one row**, the layout is:
**11.89 ft** between heads, **5.94 ft** from the two ends, **7.47 ft** from the long walls.
If the tool uses **2 rows (6 heads)**, that is also compliant — the rule check is what matters,
not the count.

**PASS:** all bullets.
**FAIL:** any head farther than 15 ft from its neighbour, closer than 6 ft, or farther than 7.5 ft
from every wall.

**After the test:** close the report; **Ctrl+Z** once.

---

### T6 — A spacing override really changes the layout (KITCHEN)

**Why:** proves the per-room **S→S** override is read by the calculation, not just stored in the UI.

**Do:**
1. Select only **KITCHEN R-01**, pendant, LIGHT.
2. In the **bulk edit row** above the room list, find the box labeled **S→S** (its header shows the
   unit, e.g. `S→S (MM)`).
3. Type **8 ft in the project's unit**: `2438` if the header says `(MM)`, `8` if it says `(FT)`.
4. Click the **Apply** button immediately to the right of that box.
5. Set **IF ROOM ALREADY HAS DEVICES** to **Replace devices this tool placed (hand-placed kept)**.
6. **Place Sprinklers**; confirm the replace prompt; read the report.

**Expect:**
- **No head is more than 8 ft (2.44 m) from its nearest neighbour.**
- No two heads are closer than **6 ft (1.83 m)**.
- At least **5 heads** (an 8-ft grid over 532.9 ft² needs ≥ 5).
- Report **Issues** contains a line starting `Override applied: MaxSpacingFt=...`.
- A warning says the override is `not NFPA13-2022 approved`.
- Overall status `REVIEWREQUIRED`.

**PASS:** the 8-ft limit is respected.
**FAIL:** heads are still ~11.9 ft apart (the override was ignored).

**After the test:** close the report; **Ctrl+Z** once (this also restores the 3 heads from T5).

---

### T7 — Bad input is refused (no crash)

**Why:** proves the override boxes validate instead of passing garbage into the engine.

**Do (no placement needed):**
1. Click the **S→S** box in **STUDY R-04** and type `abc`, then press **Enter** or click elsewhere.
2. Click the **WALL** box in the same row and type a value **above the allowed maximum**:
   `5000` if the header says `(MM)`, or `50` if it says `(FT)` — both are more than 10 ft.
   Press **Enter**.

**Expect:**
- The box gets a **red border** (validation error).
- The last good value is kept; the bad text does not become an override.
- A tooltip or error message explains the problem. The WALL error reads
  `Wall space must be between 0.0 and 10.0 MM` (or `FT`).
- No crash; no sprinkler is placed by this test.

**Not a bad input:** `0` is accepted in the WALL box on purpose (it means "no extra clearance"),
and a **blank** box means "use the default". So do not use `0` as the invalid value here.
Clearing the box (blank) removes the override — that is allowed.
**PASS:** both bad entries refused visibly.
**FAIL:** `abc`/the over-maximum value accepted, or the tool throws.

---

### T8 — An impossible clearance is refused (W.C 8)

**Why:** proves the tool fails honestly instead of quietly placing a head in an illegal spot.
W.C 8 is only **4.89 ft** wide; a **3 ft (914 mm)** clearance from both long walls is impossible.

**Do:**
1. First make sure W.C 8 has **no sprinkler** (undo/delete the T3 run if it is still there).
2. Select only **W.C 8**, pendant, LIGHT.
3. In the bulk edit row, type **3 ft** into the **WALL** box
   (`914` if `(MM)`, `3` if `(FT)`), then click its **Apply** button.
4. **Place Sprinklers**; read the report.

**Expect:**
- **Zero sprinklers in W.C 8.**
- Room status **`Skipped`** (the tool's current wording) or, in a later build, `ReviewRequired`.
- Placed **0**, Requested **0** (no points were calculated).
- Message: `Room skipped - nothing was calculated or attempted here.`
- The JSON export (T12) shows an engine status of `NoValidCandidates` for the room.
- No crash and no sprinkler outside the room.

**PASS:** no head in W.C and a visible skip/no-candidate reason.
**FAIL:** a head is placed anyway, or the report claims Success.

**After the test:** click **Reset** in the W.C row (clears the 3 ft override).

---

### T9 — Sidewall head mounts on a wall (W.C 8 or ENSUITE 5)

**Why:** proves the separate sidewall path: it must use a **wall**, not the ceiling centre, and its
deflector must be **4–6 in** below the ceiling (the old bug was 7 in).

**Do:**
1. In **SPRINKLER CONFIGURATION**, change the family to the **sidewall** family (and one type).
2. Select only **W.C 8** (or ENSUITE 5 if W.C shows no ceiling).
3. Hazard **LIGHT**; **Place Sprinklers**; read the report.

**Expect:**
- **1 head**, mounted **on a wall** — not floating in the middle of the room.
- Stand-off from the wall face **≈ 1.5 in (38 mm)** (the head body is just off the wall).
- Deflector **4–6 in (102–152 mm)** below the ceiling underside:
  **≈ 2.52 m** on Ground Floor (**2.523 m** for a 4-in drop).
- Report: Placed **1 / 1**, status `ReviewRequired`, provisional warning present.
- W.C's area per head (29.2 ft²) is far below the sidewall limit (120 ft² for Light combustible),
  so the room is **not** "too wide".

**PASS:** wall-mounted + 4–6 in drop + 1/1.
**FAIL:** ceiling-centre head, drop ≈ 7 in (outside 4–6), or no head.

**After the test:** close the report; **Ctrl+Z** once.

---

### T10 — Too-wide room is flagged for sidewall (KITCHEN)

**Why:** proves the tool warns when a sidewall listing cannot span the room.
KITCHEN is **14.94 ft** wide; the sidewall limit is **12 ft** for a combustible finish and **14 ft**
for noncombustible — too wide either way.

**Do:**
1. Keep the **sidewall** family selected.
2. Select only **KITCHEN R-01** (level **1. Ground Floor**).
   You do **not** need to place.

**Expect:**
- An amber badge in the KITCHEN row: **`⚠ TOO WIDE FOR SIDEWALL — SWITCH TO PENDENT`**.
- The KITCHEN checkbox may be disabled (blocked) with a short reason such as `BLOCKED — ...`.
- Other narrow rooms (W.C, BEDROOM 5) do not show the badge.

**PASS:** badge appears on KITCHEN.
**FAIL:** no badge, or the tool silently places sidewall heads across a 14.94 ft room without a
review flag.

---

### T11 — Re-running does not duplicate (BEDROOM 5 10)

**Why:** proves the three existing-device policies. This is the test that protects a real project
from double sprinklers.

**Precondition:** T4 left **≥ 2 tool-placed heads** in BEDROOM 5. Do not undo T4.

**Part A — Skip (default):**
1. Pendant family; select only **BEDROOM 5 10**.
2. **IF ROOM ALREADY HAS DEVICES** = **Skip rooms that already have devices**.
3. **Place Sprinklers**.
4. Count the heads in the room (or read the report).

**Expect A:** count **unchanged**; report shows the room **Skipped**; summary says 0 placed;
no duplicate heads; warning says the room already has sprinklers and was left untouched.

**Part B — Replace:**
1. Same room; policy = **Replace devices this tool placed (hand-placed kept)**.
2. **Place Sprinklers** → a confirmation dialog appears containing
   `Replace will DELETE the FireProtection-placed sprinklers inside every selected room before ...`
3. Confirm.
4. Read the report; close it.

**Expect B:** the old tool-placed heads are gone and a fresh set is created; the total is roughly
the same; summary contains `Replaced ... previously-placed sprinkler(s)`; **one Ctrl+Z** (after
closing the report) puts the previous run back.

**Part C — hand-placed device (optional):**
1. In Revit, place **one new sprinkler by hand** in BEDROOM 5 using Revit's own
   **Systems → Fire Protection → Sprinkler** tool (not this add-in).
2. Run **Replace** again.
3. Read the summary.

**Expect C:** the hand-placed head is **kept**, and the report's Issues & Warnings contains
`N hand-placed sprinkler(s) in this room were PRESERVED: they were not placed by FireProtection`;
the room is reported as partially replaced.

**PASS:** A unchanged; B replaces; C keeps the hand-placed device.
**FAIL:** duplicates in A, or the hand-placed head deleted in C.

---

### T12 — The report tells the truth (and exports)

**Why:** proves the UI does not hide the provisional status or the per-room reason.

**Do:**
1. After any pendant run (T3–T6), read the **SPRINKLER Placement Report** window.
2. Check the title, the five cards, the rules line, the room table and **Issues & Warnings**.
3. Click **Export JSON**, save the file, and open it in Notepad.

**Expect:**
- Window title **Placement Report**; report heading **SPRINKLER Placement Report**.
- Five status cards: **Succeeded / Failed / Skipped / Review Required / Devices Placed / Requested**.
- Rules line starts **`PROVISIONAL RULES - engineering review required.`**
- Room **Status** for pendant rooms is `ReviewRequired` and the message names the engine flag.
- **Issues & Warnings** includes a provisional/engineering-review line.
- **Export JSON** writes a file and shows `Exported to: <path>`.
- The JSON contains the room list and `PlacementDiagnostics`, including `[REVIEW]` lines.
- **Export CSV** also writes a file (same dialog behaviour).

**Known wording defect — record it, do not fail the run:**
the rules line says **NFPA 13 (2002)**, while some override warnings say
**`not NFPA13-2022 approved`**. Both refer to the same design basis but name different editions.
That is a real documentation/string defect, already recorded in the project notes.

**PASS:** all bullets; the wording mismatch is written down as a defect.
**FAIL:** report says Success for a pendant room, or no provisional warning anywhere.

---

## 6. Advanced tests (optional)

### T13 — A room with no ceiling (open structure)

**Why:** proves the **Allow open structure (deck/unhosted)** toggle and the no-ceiling fallback.

**Preparation (do this in the test copy; the tool caches the model when it opens):**
1. Close the tool window.
2. Find a room whose **CEILING TYPE** is `—` (none). If every room has a ceiling, delete one
   ceiling in the test copy (**select it in a section/3D view → Delete**) and note which room lost it.
3. Reopen the tool from the ribbon so it re-reads the model.

**Part A — toggle OFF:**
1. Untick **Allow open structure (deck/unhosted)**.
2. Select only the no-ceiling room; **Place Sprinklers**.

**Expect A:** the room is **blocked/not placeable** (disabled checkbox and a reason mentioning the
missing ceiling/host), or the run reports it Failed/Review Required with **no device**.

**Part B — toggle ON:**
1. Tick **Allow open structure (deck/unhosted)**.
2. Select only the same room; **Place Sprinklers**.

**Expect B:** a head is created with a fallback warning. Height = **level + the room's ceiling
height** if that height is ≥ 7.5 ft; otherwise **level + 9 ft**. In this fallback the normal
**1-in pendant drop is not applied**. The report is Review Required and explains the fallback.

**PASS:** OFF = no device with a clear reason; ON = fallback device + warning.
**FAIL:** no-ceiling room silently places at 0 m, or OFF still places.

Restore any deleted ceiling with **Ctrl+Z** when finished.

---

### T14 — Obstacle / Three Times rule (needs a structural column)

**Why:** proves the Three Times rule rejects heads too close to a column. There are **no structural
columns or framing** in HOUSE_MODEL, so you must add one.

**Preparation:**
1. Close the tool window.
2. In the test copy, draw a **12 in × 12 in (300 × 300 mm) structural column** in the middle of
   **STUDY R-04**, from level `1. Ground Floor` up through the ceiling.
   (Structure tab → Column → Structural Column; any concrete/steel column type.)
3. Reopen the tool.

**Do:**
1. Pendant family; select only **STUDY R-04**; LIGHT.
2. Keep the default **OBSTRUCTION CATEGORIES** (Structural columns must stay ticked).
3. **Place Sprinklers**.

**Expect:**
- No head **inside** the column.
- No head within **24 in (610 mm)** of the column face.
  (Three Times rule = 3 × 12 in = 36 in, capped at 24 in.)
- If the layout cannot satisfy that, the room is flagged **Review Required** with an
  obstacle/Three-Times message, not silently accepted.

**PASS:** the column is respected or the room is flagged.
**FAIL:** a head inside/too close to the column with no warning.

---

### T15 — Cancel during a run (optional)

**Why:** proves Cancel stops the run and rolls back everything created in it.

**Do:**
1. Tick only **1. Ground Floor** so its rooms appear, then click the room-list toggle so every
   eligible Ground Floor room is ticked (the button reads **Select All** when none are ticked).
2. **Place Sprinklers**.
3. While the progress text is still moving, click **Cancel**.
4. Close the report and compare the model with the "before" count.

**Expect:**
- Report overall status **CANCELLED** and summary
  `Placement cancelled by the user; everything created in this run was rolled back.`
- **No new heads remain** anywhere from that run. You should **not need Ctrl+Z** to remove partial
  work.
- If the run finishes before you can click Cancel, mark this **N/A** — the model is too small to
  catch it.

**PASS:** cancelled run leaves nothing behind.
**FAIL:** some rooms keep heads, or the report claims success after Cancel.

---

### T16 — Developer check: unit tests (no Revit UI)

**Why:** the calculation rules and solvers have automated tests; these catch regressions the UI
runbook does not.

**Do:** from the repository root, run the test project:

```powershell
dotnet run --project FireProtection.Tests\FireProtection.Tests.csproj -c Revit2025 -p:SkipRevitDeploy=true
```

Two parts of that command matter:

- `-c Revit2025` — the tests compile the Backend, which needs the Revit API. The default `Debug`
  configuration has no Revit reference and fails with `CS0246` errors (`XYZ`, `FamilySymbol`, ...).
- `-p:SkipRevitDeploy=true` — needed while Revit is open. Without it the build tries to copy the
  add-in DLL into `%AppData%\Autodesk\Revit\Addins\2025\`, which the running Revit locks
  (`MSB3027` / `MSB3021`). This is a file lock, not a code failure.

**Expect:** build succeeds and all tests pass. The last lines say:

```text
  OK: all 27 discovered test suites are registered.

ALL TESTS PASSED
```

and the exit code is `0`. The project covers NFPA Table 19-2 values, the tile grid, the sidewall
solver, obstacle categories, overrides, and report honesty.

**Do not use `dotnet test` for this project** — it is a plain console program, not an MSBuild test
project, so `dotnet test` prints nothing useful and still exits `0`. It is not running the tests.

**PASS:** "ALL TESTS PASSED" and exit code `0`.
**FAIL:** any red test (the failing test name and message are printed). That is a product
regression, not a test-setup problem. If the build fails for a machine reason — Revit not installed,
or the add-in DLL locked because Revit is open — record it as an **environment** failure (see
TODO.md, "Environment status") and do not call it a product failure.

---

## 7. Record sheet

Copy this into your notes and fill it in.

| Test | What it checks | Result | Evidence / notes |
|---|---|---|---|
| T1 | 5 levels, 16 rooms | ☐ PASS ☐ FAIL ☐ N/A | |
| T2 | Missing-family message | ☐ PASS ☐ FAIL ☐ N/A | |
| T3 | W.C 8: 1 pendant head, Z ≈ 2.600 m | ☐ PASS ☐ FAIL | Actual count: ___ Z: ___ |
| T4 | BEDROOM 5: ≥ 2 heads | ☐ PASS ☐ FAIL | Actual count: ___ spacing: ___ |
| T5 | KITCHEN: ≥ 3, spacing 6–15 ft, wall ≤ 7.5 ft | ☐ PASS ☐ FAIL | Actual count: ___ |
| T6 | 8-ft spacing override respected | ☐ PASS ☐ FAIL | Actual nearest gap: ___ |
| T7 | Bad inputs refused | ☐ PASS ☐ FAIL | |
| T8 | W.C 8 + 3 ft clearance → nothing placed | ☐ PASS ☐ FAIL | Status: ___ |
| T9 | Sidewall: wall-mounted, 4–6 in drop | ☐ PASS ☐ FAIL | Drop: ___ |
| T10 | KITCHEN sidewall "TOO WIDE" badge | ☐ PASS ☐ FAIL | |
| T11 | Skip / Replace / hand-placed kept | ☐ PASS ☐ FAIL | |
| T12 | Report + provisional + export | ☐ PASS ☐ FAIL | Wording defect noted? ☐ |
| T13 | No-ceiling toggle/fallback | ☐ PASS ☐ FAIL ☐ N/A | |
| T14 | 12-in column respected (24-in zone) | ☐ PASS ☐ FAIL ☐ N/A | |
| T15 | Cancel rolls back whole run | ☐ PASS ☐ FAIL ☐ N/A | |
| T16 | Unit tests green | ☐ PASS ☐ FAIL ☐ N/A | |

---

## 8. What is NOT a failure

- **`Review Required` / `REVIEWREQUIRED` / `PROVISIONAL RULES`** — by design. No engineer-approved
  rule set is loaded. Every pendant run is expected to say this.
- **More heads than the minimum.** The tile-grid solver may add rows/columns to satisfy the
  X-2X-X rule. 3 vs 6 heads in the kitchen can both be correct. Judge **spacing, wall distance and
  area per head**, not the count.
- **The amber "TOO WIDE FOR SIDEWALL" badge on KITCHEN** — expected and correct.
- **`Skipped` instead of `Failed` for the 3-ft clearance test** — the current report wording for
  "nothing was calculated or attempted". What matters is **0 heads placed** and a reason.
- **Elevation slightly off (≤ 1 in / 25 mm).** Revit family insertion points differ. Judge the drop
  below the ceiling. If **every** head is offset by the same amount, it is the family origin, not
  the placement engine.
- **Report table spelling `ReviewRequired` vs card label `Review Required`** — same status.

## 9. If a test fails

Record these five things; they make the failure fixable:

1. **Test ID** (T3, T8, …) and the room name.
2. **Expected vs actual** (count, spacing, status, message).
3. **The report**: click **Export JSON** and keep the file. It contains per-room diagnostics.
4. **A screenshot** of the tool window with the room selected and the report open.
5. **Revit warnings**: Manage → Warnings, and any error dialog text.

Then add a line to `SESSION_NOTES.md` under a new dated heading, and add the defect to `TODO.md`
if it is a product bug. Do not edit the code before the failure is captured.

## 10. What this runbook does NOT prove

- It does **not** prove compliance with NFPA 13. The rules are provisional and unapproved.
- It does **not** test linked-model placement: HOUSE_MODEL has **no linked Revit files**.
- It does **not** test sloped ceilings: the 7 ceilings in this model are flat.
- It does **not** test beams/framing: the model has **0 structural framing** members.
- It does **not** test the small-room rule: no room here has the required 16–18 ft square shape.
  That rule is covered by the unit tests in T16.

## Appendix — where the expected numbers come from

| Fact | Code location |
|---|---|
| Light hazard: 15 ft max spacing, 6 ft min, 225 ft² max, 7.5 ft max wall distance, 1 ft boundary clearance | `FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/DefaultHazardPlacementRules.cs` |
| 4 in minimum wall clearance constant | `.../BruteForce/Nfpa13RulebookRules.cs` (`MinWallClearanceFt`) |
| Pendant drop = 1 in; upright = 6 in; no-ceiling fallback | `.../BruteForce/BruteForceCalculationService.cs` lines ~195–335 |
| Sidewall drop = 4–6 in, stand-off 1.5 in | `.../BruteForce/BruteForceCalculationService.cs` (~2971) and `Nfpa13RulebookRules.cs` |
| Provisional → `ReviewRequired` + warning | `.../BruteForce/BruteForceCalculationService.cs` (~1975–1990) and `DefaultHazardPlacementRules.HasApprovedRules` |
| Three Times rule = 3× dimension, capped 24 in | `Nfpa13RulebookRules.cs` (~372–425) |
| Report statuses/messages and provisional rules line | `FireProtection.UI/Services/SprinklerPlacementReportMapper.cs`, `ViewModels/PlacementResultReportViewModel.cs` |
| Existing-device policies | `FireProtection.UI/Services/IPlacementProgress.cs` |
| One transaction group per run (single undo; cancel rolls back) | `FireProtection.Backend/Services/Placement/Sprinklers/Final/RevitSprinklerPlacementService.cs` (~107–120) |
| Unit conversion for the override boxes | `FireProtection.Backend/Commands/FireProtectionCommand.cs` (~184–220), `FireProtection.UI/Services/UnitDisplay.cs` |

---

*Companion docs: [[SPRINKLER_POINT_CALCULATION_EXPLAINED]] (how the calculation works) and
[[FireProtection-Sprinklers]] (full feature overview). This file is a test runbook, not proof that
the runtime behaviour has been verified.*
