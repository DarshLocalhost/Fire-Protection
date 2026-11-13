# SPA SynthFire — Core Placement & Design Logic Memory

## 1. Unit Conventions
- All internal spatial computations use **Feet (`ft`)** and **Square Feet (`sq ft`)** conforming to internal Revit API units.
- Metric conversions (`Units.cs`):
  - $1\text{ m} \approx 3.28084\text{ ft}$
  - $1\text{ m}^2 \approx 10.7639\text{ ft}^2$

---

## 2. Sprinkler Layout Logic (`SprinklerPlacementSolver` & `CeilingGridLayout`)

### A. Grid Count Calculation
For a room of width $W$ and length $L$ along primary axes:
1. Initial counts:
   $$\text{countX} = \max\left(1, \lceil W / \text{maxSpacing} \rceil\right)$$
   $$\text{countY} = \max\left(1, \lceil L / \text{maxSpacing} \rceil\right)$$
2. Area constraint refinement loop:
   While $(W / \text{countX}) \times (L / \text{countY}) > \text{maxCoverageSqFt}$:
   - Increment $\text{countX}$ if $(W / \text{countX}) \ge (L / \text{countY})$, else increment $\text{countY}$.
3. Spacing validation:
   - If $\text{spacingX} < \text{minSpacing}$ or $\text{spacingY} < \text{minSpacing}$, flags `ReviewRequired = true`.

### B. Device Positioning
- Base coordinates placed at cell centers:
  $$x_i = \text{MinX} + \text{spacingX} \times (i + 0.5)$$
  $$y_j = \text{MinY} + \text{spacingY} \times (j + 0.5)$$

### C. Ceiling Tile Grid Snapping
- If `room.CeilingTile` is supplied with $(\text{OriginX}, \text{OriginY})$ and $(\text{ModuleX}, \text{ModuleY})$ (e.g. 2x2ft or 2x4ft tiles):
  - Snaps to tile center:
    $$x_{\text{snap}} = \text{tileOriginX} + \left(\lfloor(x - \text{tileOriginX}) / \text{modX}\rfloor + 0.5\right) \times \text{modX}$$
  - Accepted only if point remains inside room boundary standoff ($4\text{ inches} = 0.333\text{ ft}$).

### D. Obstacle Avoidance & Coordination
- Obstacle types queried: Ducts (`OST_DuctCurves`), Cable Trays (`OST_CableTray`), Structural Framing (`OST_StructuralFraming`), Pipes (`OST_PipeCurves`).
- Clearance buffer: $0.5\text{ ft}$ ($6\text{ inches}$).
- If an obstacle box overlaps $(x, y)$:
  - Tries an 8-way directional nudge in increments up to maximum search steps ($8$ directions: Cardinal + Diagonal).
  - If a valid clear point inside the room boundary is found, status = `ClashState.Nudged`.
  - If no clear spot within reach, keeps base location and marks status = `ClashState.Unresolved` (`ReviewRequired = true`).

---

## 3. Sidewall Sprinkler Logic (`SidewallLayout`)
- For rooms without ceiling access or specified with `DeviceMount.Sidewall`:
  - Identifies room's longest boundary wall(s).
  - Evaluates room throw distance against maximum allowable throw for sidewalls.
  - Places heads along the wall perimeter at elevation offset below ceiling, facing inward.

---

## 4. Fire Alarm / Detection Logic (`Nfpa72DetectionRules`)
- Reuses `CeilingGridLayout` for regular room coverage:
  - **Smoke Detectors**:
    - Nominal spacing: $30.0\text{ ft}$ (smooth ceilings).
    - Maximum coverage: $900.0\text{ sq ft}$.
    - Minimum spacing: $3.0\text{ ft}$.
  - **Heat Detectors**:
    - Nominal spacing: $50.0\text{ ft}$.
    - Maximum coverage: $2500.0\text{ sq ft}$.
    - Minimum spacing: $3.0\text{ ft}$.
- Notification appliances (strobes, horns):
  - Wall-mounted spacing determined by candela room tables (`NotificationCoverage.cs`).

---

## 5. Revit Placement & Redesign Tracking
- Heads/devices placed via `Doc.Create.NewFamilyInstance`.
- Host resolution handled via `HostFinder`:
  - Face-hosted on ceiling bottom faces.
  - Level-hosted with explicit offset parameter (`PlacementLevels.Apply`) to prevent elements collapsing to Level 1 / ground elevation.
- Redesign / Idempotence:
  - Placed elements have parameter `ALL_MODEL_INSTANCE_COMMENTS` set to `"SPA SynthFire"`.
  - On re-running a room or level, existing heads with this tag are safely deleted before placing new ones, avoiding duplication.
