# Catalog (Excel) — Authoring Guide

The Fire Protection add-in reads its sprinkler + device catalog from a single Excel workbook (one
workbook, one sheet per category, with a `CatalogVersion` header row). The user selects the
workbook each session; the add-in never writes back.

## Sheets

| Sheet                  | Purpose                                     |
|------------------------|---------------------------------------------|
| `Sprinklers`           | Sprinkler families + types                  |
| `SmokeDetectors`       | Smoke detector families + types + params     |
| `NotificationAppliances` | Notification appliance families + types + params |

This workbook is the **source of truth for every family/type and device-parameter dropdown** in the
UI (Decision 017) — sprinklers, smoke detectors, and notification appliances alike. No option list is
hardcoded in the view models: with no workbook loaded the combos are empty and the tab shows a
"catalog not loaded" banner. Each dropdown offers the **distinct values present in its sheet**, so a
column that only ever holds one value yields a one-item dropdown.

The legacy listing of families out of the open Revit document is disabled by default
(`FireProtectionConfig.UseRevitFamilyListing = false`) and is only a fallback used when the catalog
yields no sprinklers. Practical consequence: a family/type that is not in this sheet cannot be
selected, and a family/type in this sheet that does not exist in the Revit model will fail at
placement time — `FamilyName` / `TypeName` must match Revit character-for-character.

Which sheet column feeds which control:

| Tab                     | Sheet column      | Control                                        |
|-------------------------|-------------------|------------------------------------------------|
| all three               | `FamilyName`      | Device Family (tab-wide, and the per-room column) |
| all three               | `TypeName`        | Device Type (filtered by the selected family)  |
| Smoke Detectors         | `DetectorType`    | Detector Type                                  |
| Smoke Detectors         | `Mount`           | Mount                                          |
| Smoke Detectors         | `CeilingSlope`    | Ceiling Slope                                  |
| Notification Appliances | `ApplianceType`   | Appliance Type                                 |
| Notification Appliances | `Candela`         | Candela (values `> 0` only)                    |
| Notification Appliances | `NotificationDba` | Notification dBA (values `> 0` only)           |
| Notification Appliances | `Candela` + `NotificationDba` | the per-level "Candela / dBA" combo — the **pairs that actually occur** in the sheet, not a cartesian product |

## Schema

### Row 1 (every sheet)
| A              | B             |
|----------------|---------------|
| `CatalogVersion` | `<your version>` (e.g. `2026-09-02.2`) |

### Row 2 (column headers)

**Sprinklers** (col 1..6): `Category, FamilyName, TypeName, HazardClass, Mount, Notes`
**SmokeDetectors** (col 1..7): `Category, FamilyName, TypeName, DetectorType, Mount, CeilingSlope, Notes`
**NotificationAppliances** (col 1..7): `Category, FamilyName, TypeName, ApplianceType, Candela, NotificationDba, Notes`

### Row 3+ (data rows)

#### Sprinklers

| Column        | Required | Allowed values                                                            |
|---------------|----------|---------------------------------------------------------------------------|
| `Category`    | yes      | `Sprinkler`                                                                |
| `FamilyName`  | yes      | any text (must match the Revit `Family.Name` for placement)                |
| `TypeName`    | yes      | any text (must match the Revit `FamilySymbol.Name`)                        |
| `HazardClass` | no       | leave **blank** (see below); if filled: `LIGHT`, `OH1`, `OH2`, `EH1`, `EH2` — other values are accepted with a warning |
| `Mount`       | no       | `Pendent`, `Upright`, `Sidewall`, `Recessed` (other values accepted with a warning) |
| `Notes`       | no       | free text                                                                  |

**Leave `HazardClass` blank.** It is validated only when non-blank, and nothing in the pipeline
reads it: the hazard class is a **per-room** choice made in the UI from the hardcoded
`HazardClassOptions.All`, not looked up from this sheet. Filling it in is also actively awkward,
because a duplicate `(FamilyName, TypeName)` is a hard error — so one type cannot be listed once per
hazard class. Every row in the shipped `CatalogTemplate.xlsx` leaves it blank.

**`Mount` records orientation only** — `Pendent` or `Sidewall`. It is informational (it feeds the
`AvailableSprinklerMounts` list) and does not drive placement. Trim style (recessed / semi-recessed
/ exposed) and wet-vs-dry are already encoded in the Revit `FamilyName`, so do not repeat them here.

#### SmokeDetectors

| Column         | Required | Allowed values                                                            |
|----------------|----------|---------------------------------------------------------------------------|
| `Category`     | yes      | `SmokeDetector`                                                            |
| `FamilyName`   | yes      | any text                                                                   |
| `TypeName`     | yes      | any text                                                                   |
| `DetectorType` | yes      | `Ionization`, `Photoelectric`, `Heat`, `CO`, `MultiCriteria`, `Aspirating` |
| `Mount`        | yes      | `Ceiling`, `Wall`, `Floor`                                                |
| `CeilingSlope` | yes      | `Flat`, `Sloped`, `Stepped` (a property of the *detector* — not the room)  |
| `Notes`        | no       | free text                                                                  |

Because `DetectorType`, `Mount`, and `CeilingSlope` populate the three dropdowns on the Smoke
Detectors tab, list every value you want to be selectable on at least one row. The shipped sample
rows deliberately span all six detector types, both `Ceiling` and `Wall` mounts, and all three slopes.

#### NotificationAppliances

| Column            | Required | Allowed values                                                                              |
|-------------------|----------|---------------------------------------------------------------------------------------------|
| `Category`        | yes      | `NotificationAppliance`                                                                     |
| `FamilyName`      | yes      | any text                                                                                    |
| `TypeName`        | yes      | any text                                                                                    |
| `ApplianceType`   | yes      | `Horn`, `Strobe`, `HornStrobe`, `Speaker`, `SpeakerStrobe`, `Chime`, `ChimeStrobe`           |
| `Candela`         | conditional | positive integer for visible types (anything with `Strobe` in the name); blank/0 for audible-only |
| `NotificationDba` | conditional | positive integer for audible types (Horn / Speaker / Chime); blank/0 for strobe-only     |
| `Notes`           | no       | free text                                                                                   |

`Candela` is the visible rating and `NotificationDba` the audible one; **each is required only for the
types that have one, and a row must carry at least one of the two.** A plain `Horn` has no candela, a
ceiling `Strobe` has no dBA, and a combination type (`HornStrobe`, `SpeakerStrobe`, `ChimeStrobe`) has
both. A `0` means "no such rating" and is filtered out of the Candela / dBA dropdowns.

## Validation (fail-fast)

The loader is **strict**. Any of the following causes `CatalogLoadException` at load time:

- Missing `CatalogVersion` header row.
- Duplicate `(FamilyName, TypeName)` within a sheet.
- Missing `Category`, `FamilyName`, or `TypeName` in a data row.
- `Candela <= 0` for a visible type (anything with `Strobe` in the name).
- `NotificationDba <= 0` for audible types (Horn / Speaker / Chime).
- A notification-appliance row with **neither** a `Candela` nor a `NotificationDba` rating.
- A negative `Candela` or `NotificationDba`.
- Empty workbook / unreadable file.

Warnings (not fatal) are issued for:
- Unknown `HazardClass` / `Mount` / `DetectorType` / `CeilingSlope` / `ApplianceType` (the value is preserved as-is).

Optional columns left blank produce no issue at all — the value is only range-checked when present.

## Template

`CatalogTemplate.xlsx` is a checked-in starter: 39 sprinkler rows, 9 smoke-detector rows, 5
notification-appliance rows.

Its `Sprinklers` sheet mirrors the 9 sprinkler families (39 types) loaded in the
`02_FireProtection_Test.rvt` host model, so those strings are real. **The `SmokeDetectors` and
`SmokeDetectors` and `NotificationAppliances` contain the family/type names observed in the target
Revit model. If a selected row is not loaded in the active model, placement opens the missing-family
loader before proceeding. Their
*parameter* columns (`DetectorType`, `Mount`, `CeilingSlope`, `ApplianceType`, `Candela`,
`NotificationDba`) are real, chosen to span the selectable ranges described above.

To retarget the workbook at a different model, edit the family/type string arrays in
`FireProtection.Tests/CatalogTemplateGenerator.cs` and regenerate — editing the xlsx by hand works
too, but the next regeneration overwrites it.

Regenerate it any time with:

```
dotnet run --project FireProtection.CatalogStandalone/FireProtection.CatalogStandalone.csproj -c Debug -- generate-template path/to/CatalogTemplate.xlsx
```

Validate any candidate workbook with:

```
dotnet run --project FireProtection.CatalogStandalone/FireProtection.CatalogStandalone.csproj -c Debug -- validate-catalog path/to/your.xlsx
```

## Where the add-in looks for the file

- The user selects the file **each session** via a file picker in the top bar.
- The path is **session-scoped** — not persisted across sessions.
- A "Reload" button in the top bar hot-reloads from the same path.
