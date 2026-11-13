# SPA SynthFire — Domain Glossary & Data Dictionary

This glossary defines standard terminology, data representations, and conventions used across all modules of SPA SynthFire.

---

## 1. Domain Entities & Placement Types

| Term | Representation / Type | Description |
|------|-----------------------|-------------|
| **RoomInput** | `SPA.SynthFire.Placement.RoomInput` | Headless snapshot of a Revit room: ID, Name, Level, Footprint, Height, Ceiling elevation, and Obstacle list. |
| **Rectangle2D** | `SPA.SynthFire.Core.Geometry.Rectangle2D` | Axis-aligned bounding box defined by `MinX, MinY, MaxX, MaxY, Width, Height`. |
| **Polygon2D** | `SPA.SynthFire.Core.Geometry.Polygon2D` | Arbitrary 2D room boundary polygon with vertices ordered counter-clockwise. |
| **CeilingTile** | `SPA.SynthFire.Placement.CeilingTile` | Tile grid definition: `OriginXFt, OriginYFt, ModuleXFt, ModuleYFt` (e.g. 2x2ft or 2x4ft). |
| **ObstacleBox** | `SPA.SynthFire.Placement.ObstacleBox` | Bounding box of clashing MEP/structural elements with a $0.5\text{ ft}$ clearance buffer. |
| **DeviceMount** | `SPA.SynthFire.Core.Devices.DeviceMount` | Enum: `Ceiling` (face-hosted), `Sidewall` (wall-hosted), `Free` (unhosted direct XYZ). |
| **ClashState** | `SPA.SynthFire.Placement.ClashState` | Enum: `Clear` (no obstacle), `Nudged` (shifted off obstacle), `Unresolved` (clash remains, review needed). |
| **PlacementResult**| `SPA.SynthFire.Placement.PlacementResult` | Aggregate output containing per-room head counts, coordinates, review flags, and warnings. |

---

## 2. Fire Protection Standards & Classifications

| Hazard Class | Code Standards | Typical Maximum Spacing | Typical Max Coverage |
|--------------|----------------|-------------------------|----------------------|
| **Light Hazard (LH)** | NFPA 13 / AS 2118 | $15.0\text{ ft}$ ($4.6\text{ m}$) | $225\text{ sq ft}$ ($21.0\text{ m}^2$) |
| **Ordinary Hazard 1 (OH1)** | NFPA 13 / AS 2118 | $15.0\text{ ft}$ ($4.0\text{ m}$) | $130\text{ sq ft}$ ($12.0\text{ m}^2$) |
| **Ordinary Hazard 2 (OH2)** | NFPA 13 / AS 2118 | $15.0\text{ ft}$ ($4.0\text{ m}$) | $130\text{ sq ft}$ ($12.0\text{ m}^2$) |
| **Extra Hazard 1 (EH1)** | NFPA 13 / AS 2118 | $12.0\text{ ft}$ ($3.7\text{ m}$) | $100\text{ sq ft}$ ($9.0\text{ m}^2$) |
| **Extra Hazard 2 (EH2)** | NFPA 13 / AS 2118 | $12.0\text{ ft}$ ($3.7\text{ m}$) | $100\text{ sq ft}$ ($9.0\text{ m}^2$) |

---

## 3. Fire Alarm & Detection (NFPA 72)

| Device Category | Nominal Spacing | Maximum Area Coverage | Mounting Rules |
|-----------------|-----------------|-----------------------|----------------|
| **Smoke Detector** | $30.0\text{ ft}$ ($9.1\text{ m}$) | $900.0\text{ sq ft}$ ($83.6\text{ m}^2$) | Ceiling-mounted, min 4" from walls, min 3ft from air diffusers |
| **Heat Detector** | $50.0\text{ ft}$ ($15.2\text{ m}$) | $2500.0\text{ sq ft}$ ($232.2\text{ m}^2$) | Ceiling-mounted smooth ceilings |
| **Notification (Strobe/Horn)**| Per candela tables | Table-driven per room dimension | Wall-mounted $80" - 96"$ above finished floor |

---

## 4. Key Revit BuiltIn Parameters Used
- `ALL_MODEL_INSTANCE_COMMENTS`: Set to `"SPA SynthFire"` to identify plugin-managed heads.
- `FAMILY_LEVEL_PARAM` / `SCHEDULE_LEVEL_PARAM`: Bound to the room's native level.
- `INSTANCE_FREE_HOST_OFFSET_PARAM`: Elevation above the level plane.
