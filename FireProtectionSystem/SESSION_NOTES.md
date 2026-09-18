# FireProtectionSystem — Session Notes

> Template for capturing session-specific knowledge that must survive future OpenCode sessions.
> Each entry: date, session focus, key actions, open questions, handoff. Most-recent first.

## Template

```markdown
### YYYY-MM-DD — <Session Focus>
- **Context**: <why this session happened>
- **Actions Taken**: <what was done, with file references>
- **Decisions**: <links to [[DECISIONS]] if a decision was recorded>
- **Open Questions / Blockers**: <unresolved items>
- **Handoff**: <what the next session should pick up>
```

---

### 2026-09-02 — ClosedXML "Browse → load Excel" popup: missing/mis-redirected runtime dependencies (deployment fix)

- **Context**: Clicking **Browse** in the catalog bar and selecting a workbook produced an error popup
  ("Failed to load catalog: …"). The load path itself is fine — `CatalogLoader` (ClosedXML) never got a
  chance to run because the add-in's **deployed dependency set was incomplete and its binding redirects
  pointed at versions that do not exist**. Verified with `AssemblyName.GetAssemblyName` on the NuGet cache
  and by walking every `.nuspec` in the ClosedXML 0.104.1 dependency graph.
- **Root causes (four, all in deployment — no product logic was wrong):**
  1. **Revit 2025/2026 (net8.0): `System.IO.Packaging.dll` was never deployed.**
     `DocumentFormat.OpenXml.Framework 3.0.1` requires it on `net8.0`, and it is **not** part of the .NET 8
     shared framework — so the first `.xlsx` open threw `FileNotFoundException`. (On `.NETFramework4.6` the
     nuspec uses the `WindowsBase` framework assembly instead, so net48 must *not* get a second copy.)
  2. **Revit 2025/2026: three netstandard2.0 facades were being deployed that must not be.**
     `System.Memory` / `System.Buffers` / `System.Numerics.Vectors` are **in-box on .NET 8**; shipping the
     netstandard2.0 *implementations* next to the add-in gives the loader a second `Span<T>`/`Memory<T>`
     type identity, which breaks ClosedXML with `TypeLoadException`/`MissingMethodException` at
     workbook-open time. Now excluded, and stale copies are deleted by the deploy target.
  3. **Revit 2024 (net48): `System.Runtime.CompilerServices.Unsafe.dll` was never deployed** even though
     `FireProtection.Backend.dll.config` already contained a binding redirect for it (proving the intent).
     `System.Memory 4.5.5` needs it on .NET Framework, where it is not in-box.
  4. **All three net48 binding redirects targeted NuGet *package* versions, not *assembly* versions** —
     `newVersion` was `4.5.5.0` / `4.6.1.0` / `4.5.0.0`, but the deployed assemblies are **4.0.1.2**,
     **4.0.5.0**, **4.1.4.0**. A redirect to a version no assembly has fails the load outright.
- **Actions Taken**:
  - `FireProtection.Backend/FireProtection.Backend.csproj` — added `SystemIoPackagingNet8` (net8.0 build) to
    the Revit2025/2026 copy set; added `SystemUnsafeNet48` (`6.0.0/lib/net461`) to the Revit2024 copy set;
    removed `System.Memory`/`System.Buffers`/`System.Numerics.Vectors` from the net8 copy set; added a
    `Delete` of those stale facades from the addins folder for the net8 configs.
  - `FireProtection.Backend/FireProtection.Backend.dll.config` — corrected all three `newVersion` values to
    the real assembly versions and documented the package-vs-assembly-version trap.
  - `FireProtection.UI/ViewModels/Catalog/CatalogViewModel.cs` — `TryLoad` now reports
    `ExceptionType: message` plus the full inner-exception chain (new `Describe`). Plain `ex.Message` was
    useless for exactly this class of failure (`TypeInitializationException` hides the real cause in
    `InnerException`), which is why the popup named no cause.
  - `FireProtection.Tests/BruteForceSelectionTests.cs` — `FakePlacementService` was missing
    `ISprinklerPlacementService.ProbeMissingFamilies` (added by Decision 017), so the whole test project
    failed to compile (`CS0535`) as soon as anything forced a rebuild. Implemented as "no missing families".
- **Verification**:
  - **BUILD**: `Revit2025` **0 errors**; `Revit2024` **0 errors**.
  - **DEPLOY (verified by listing the addins folders)**: 2025 now contains `System.IO.Packaging.dll` and no
    longer contains the three facades; 2024 now contains `System.Runtime.CompilerServices.Unsafe.dll`.
    Confirmed via `.nuspec` walk that `System.IO.Packaging` is the *only* out-of-box dependency in the
    net8 graph, so both dependency sets are now complete.
  - **TEST**: `FireProtection.Tests` (Revit2025) — **ALL TESTS PASSED**, including `CatalogLoaderTests`
    and the template round-trip, which exercise real ClosedXML `.xlsx` read/write on .NET 8.
  - **RUNTIME (Revit)**: still to be confirmed by the user — restart Revit so the new DLL set is loaded.
- **Open Questions / Blockers**: If a popup still appears it will now name the exception type and inner
  chain — that text identifies whether it is a remaining load failure or a genuine schema rejection
  (`CatalogLoader` is fail-fast: a workbook without a `CatalogVersion` header, with duplicate
  `(FamilyName, TypeName)`, or a NotificationAppliance row without `Candela` is *rejected by design*).
  A known-good workbook to test with: `FireProtection.Backend/Resources/Catalogs/CatalogTemplate.xlsx`.
- **Handoff**: Restart Revit before retesting. Unrelated pre-existing gap noticed, not touched:
  `Newtonsoft.Json.dll` is not copied for the net8 configs (it is absent from `bin/Revit2025`), so those
  hosts rely on Revit's own copy.

---

- **Context**: Senior direction for the next product slice: replace the universal sprinkler family/type
  combo with **per-row** dropdowns, source the catalog of families + types from a **master Excel/CSV** (not
  Revit), expose per-row `MaxSpacingFt` ("sprinkler-to-sprinkler space") and `BoundaryClearanceFt`
  ("wall space") overrides, add a missing-in-model interactive modal, add per-level popovers for Smoke
  Detector (`DetectorType` / `Mount` / `CeilingSlope`) and Notification Appliance
  (`ApplianceType` / `Candela` / `NotificationDba`) metadata, and auto-deselect levels when all their
  rooms fail the family check. Also: keep the Revit family listing around (commented out, not deleted),
  one shared catalog for v1, user-selectable path each session, "Reload" button, fail-fast load.
- **Actions Taken** (planning + documentation only; **no code changed this session**):
  - Resolved all 6 outstanding design questions (Q13 propagate-as-default + room override; Q16
    detector-rated-for; Q20 room override on Notification; Q25 override must reach the engine; Q33
    English-only fixed; Q35 one catalog per project for v1).
  - Locked four new decisions in `DECISIONS.md`:
    - **017** — Per-row sprinkler family/type + Excel-driven catalog (Sprinkler/Smoke/Notification);
      catalog schema (one workbook, one sheet per category, `CatalogVersion` header); missing-in-model
      UX (interactive modal + Proceed/Cancel + "Export missing list to CSV" + per-row availability
      icon + "Reset to default" + "Apply to all eligible rows"); per-level device popovers; "⋯" button
      per level; composite (Candela, dBA) pair; `CeilingSlope` = detector-rated-for.
    - **018** — Per-row `MaxSpacingFt` / `BoundaryClearanceFt` override threads through
      `BruteForceCalculationService` via nullable fields on `PlacementRoomInput`; per-room
      `IsProvisional`; preflight keeps the un-overridden rule set; obstacle/ESFR clearances NOT
      overridable in v1.
    - **019** — Per-level Smoke/Notification metadata is declarative-only for v1; no algorithmic
      effect until device placement lands; level value propagates as default to rooms; rooms can override.
    - **020** — Catalog version shown in top bar; `CatalogLoader` is fail-fast with structured
      `CatalogIssue` errors; "Reload" hot-reloads; path is session-scoped (no persistence);
      catalog `HazardClass` falls back to `HazardClassOptions` if invalid.
  - Added 5 🔵 Planned rows to `PROGRESS.md` (per-row family/type, per-row override, catalog, missing
    modal, per-level device popover) and **re-emphasized the P0 runtime-verify precedence** in the
    `PROGRESS.md` "In Progress" block.
  - Rewrote `TODO.md` to add: P1 catalog (Decision 017 + 020), P1 per-row family/type (Decision 017),
    P1 per-row spacing override (Decision 018), P2 missing-in-model modal + `SkippedMissingFamilyCount`,
    P2 per-level Smoke/Notification popover, P3 obstacle/ESFR override deferred; P1 carve-out note
    for the catalog/per-row work must not be runtime-verified before Decision 011/012.
- **Decisions**: 017, 018, 019, 020 — see `DECISIONS.md`.
- **Open Questions / Blockers**:
  - P0 runtime verification of Decision 011/012 in Revit is still pending and **strictly first** — the
    catalog/per-row changes are static/UI plumbing and will not be runtime-verified until P0 is closed.
  - No new file-system artifacts (catalog Excel samples) yet. The first iteration will ship a
    template `.xlsx` in the repo (path TBD) so the test harness has something to read.
  - Excel schema column names are locked but their exact ordering within a sheet is not — will mirror
    the schema above (Category, FamilyName, TypeName, [category-specific], Notes) in the template.
- **Handoff**:
  1. **First**: runtime-verify Decision 011/012 in Revit (P0) before any of the catalog/per-row work
     is touched at runtime. Static implementation of 017–020 may proceed in parallel as long as it
     does not perturb the placement pipeline or the preflight.
  2. **Catalog + loader first** (020 + the Backend `Catalog*` files), then a sample `.xlsx` template,
     then unit tests on the loader.
  3. Then `RoomItemViewModel` per-row family/type + the catalog-driven dropdowns.
  4. Then the missing-in-model modal + `SkippedMissingFamilyCount`.
  5. Then the per-row `MaxSpacingFt` / `BoundaryClearanceFt` override + engine plumbing.
  6. Then the per-level "⋯" popovers for Smoke/Notification (declarative only).

### 2026-09-01 — Catalog + per-row family/type + per-row spacing override + device popovers: implemented (static)

- **Context**: Built the catalog + per-row + device-popover slice that was planned in the
  2026-09-01 plan-locked session. Static-only — no Revit runtime verification yet (P0 still open).
- **Actions Taken** (new files):
  - `FireProtection.Backend/Services/Catalog/CatalogModels.cs`, `CatalogValidator.cs`, `CatalogLoader.cs`,
    `CatalogService.cs`, `FireProtectionConfig.cs`.
  - `FireProtection.Backend/Resources/Catalogs/CatalogTemplate.xlsx` (generated by `CatalogTemplateGenerator`).
  - `FireProtection.Backend/Resources/Catalogs/README.md` (authoring guide).
  - `FireProtection.UI/Services/ICatalog.cs` (Revit-free interface + `SmokeDetectorCatalogEntry` /
    `NotificationApplianceCatalogEntry` DTOs).
  - `FireProtection.UI/Converters/InverseBoolConverter.cs`.
  - `FireProtection.UI/ViewModels/Catalog/CatalogViewModel.cs`.
  - `FireProtection.UI/Views/Catalog/CatalogBar.xaml(.cs)`.
  - `FireProtection.UI/Views/Common/MissingFamiliesModal.xaml(.cs)`.
  - `FireProtection.UI/Views/Common/LevelSettingsPopover.xaml(.cs)`.
  - `FireProtection.Tests/CatalogTemplateGenerator.cs`, `CatalogLoaderRunner.cs`,
    `CatalogLoaderTests.cs`, `BruteForceOverrideTests.cs`.
  - `FireProtection.CatalogStandalone/` (small headless test runner that links catalog + engine
    files into a net8.0-windows exe so the new logic can be exercised in this CLI environment
    without a live Revit host).
- **Actions Taken** (modified files):
  - `FireProtection.Backend/FireProtection.Backend.csproj` — added `ClosedXML 0.104.1`.
  - `FireProtection.Backend/Services/Placement/RevitSprinklerFamilySource.cs` — the Revit family
    listing body is **commented out** and gated by `FireProtectionConfig.UseRevitFamilyListing`
    (default `false`). Not deleted.
  - `FireProtection.Backend/Commands/FireProtectionCommand.cs` — wires the catalog factory.
  - `FireProtection.Backend/Models/Placement/Sprinklers/Final/PlacementRoomInput.cs` — added
    nullable `SelectedSprinklerFamilyName` / `SelectedSprinklerTypeName` / `OverrideMaxSpacingFt` /
    `OverrideBoundaryClearanceFt` (Decisions 017, 018).
  - `FireProtection.Backend/Services/Placement/Sprinklers/Final/PlacementInputBuilder.cs` +
    `PlacementInputJsonExporter.cs` — plumb the new fields through the selection → input path.
  - `FireProtection.Backend/Services/Placement/Sprinklers/Final/BruteForce/BruteForceCalculationService.cs`
    — `ApplyPerRoomOverrides(hazard, room, result)` clones the rule from
    `IHazardPlacementRules.GetRules(hazardClass)` and applies the nullable per-row overrides;
    out-of-range `MaxSpacingFt` is clamped to the provisional ceiling; `IsProvisional` is set
    per-room when an override is in use; the diagnostic line records "Override applied: …".
  - `FireProtection.Backend/Services/Placement/Sprinklers/Final/RevitSprinklerPlacementService.cs` —
    `PlaceSprinklers` now resolves symbols **per row** (Decision 017), activating each distinct
    (family, type) once. `ProbeMissingFamilies(calcResult)` is the read-only pre-place probe
    driven by the per-row `RoomCalculationResult.SprinklerFamilyName` / `SprinklerTypeName`. New
    `SprinklerPlacementResult.SkippedMissingFamilyCount` counter.
  - `FireProtection.UI/ViewModels/Sprinklers/BruteForce/RoomItemViewModel.cs` — per-row
    `SelectedFamily` / `SelectedType` / `MaxSpacingFtOverride` / `BoundaryClearanceFtOverride` /
    `FamilyAvailability` + `SetCatalogDefaults` / `ResetFamilyAndTypeToDefault` /
    `ResetSpacingOverridesToDefault` / `Apply*ToAllEligible` commands. New
    `FamilyAvailability` enum.
  - `FireProtection.UI/ViewModels/Sprinklers/BruteForce/SprinklerBruteForceViewModel.cs` — ctor
    takes `ICatalog catalog`; `SeedPerRowCatalogDefaults()` populates per-row dropdowns;
    `ApplyFamily/Type/MaxSpacing/BoundaryClearanceToAllEligibleCommand` set; per-row family/type
    + spacing overrides flow into `PlacementRoomInputItem`. Missing-family modal invoked
    pre-place.
  - `FireProtection.UI/Views/Sprinklers/BruteForce/SprinklerBruteForceView.xaml(.cs)` — four new
    columns (Family combo, Type combo, S→S ft textbox, Wall ft textbox, AVAIL label) +
    per-row "Reset" button.
  - `FireProtection.UI/Models/Sprinklers/BruteForce/RoomCalculationResult.cs` — new
    `SprinklerFamilyName` / `SprinklerTypeName`.
  - `FireProtection.UI/Models/Sprinklers/BruteForce/SprinklerPlacementResult.cs` — new
    `SkippedMissingFamilyCount`.
  - `FireProtection.UI/ViewModels/MainWindowViewModel.cs`, `SprinklerViewModel.cs` — ctor chain
    threads `CatalogViewModel` through to `SprinklerBruteForceViewModel`.
  - `FireProtection.UI/Views/MainWindow.xaml(.cs)`, `Services/UiLauncher.cs` — add `CatalogBar`
    above the tabs; new constructor overload accepts a `CatalogViewModel`.
  - `FireProtection.UI/ViewModels/Devices/DevicePlacementViewModelBase.cs` — abstract
    `OpenLevelSettings(level)` + helper for per-level default propagation.
  - `FireProtection.UI/ViewModels/Devices/DeviceLevelItemViewModel.cs` — per-level
    `DetectorType` / `Mount` / `CeilingSlope` / `ApplianceType` / `CandelaDba` with
    `PropagateToRooms`.
  - `FireProtection.UI/ViewModels/Devices/DeviceRoomItemViewModel.cs` — per-row
    `GetOverride/SetOverride/ClearAllOverrides` + typed accessors
    (`DetectorTypeOverride`, `MountOverride`, …).
  - `FireProtection.UI/ViewModels/SmokeDetectors/SmokeDetectorViewModel.cs` +
    `NotificationAppliances/NotificationApplianceViewModel.cs` — `OpenLevelSettings` opens the
    popover with the device-specific fields.
  - `FireProtection.UI/Views/Devices/DevicePlacementView.xaml(.cs)` — "⋯" button per level
    opens the popover.
  - `FireProtection.UI/Themes/Styles.xaml` — registers `InverseBoolConverter`.
  - `FireProtection.Tests/Program.cs` — `Main(string[] args)` parses `generate-template` /
    `validate-catalog`; `RunAll` calls the new tests.
- **Tests** (headless standalone, all PASS — 22/22):
  - Catalog: valid workbook, missing CatalogVersion, duplicate (Family, Type), empty path,
    missing file, empty sheet, unknown HazardClass, CatalogService lookups, missing Candela,
    template round-trip.
  - BruteForceOverride: tighter MaxSpacing increases count, larger BoundaryClearance reduces
    candidates, out-of-range is clamped, override marks room ReviewRequired, no-override is
    deterministic.
- **Decisions**: 017, 018, 019, 020 — now `Implemented (static)`. See `DECISIONS.md`.
- **Open Questions / Blockers**:
  - P0 runtime verification of Decision 011/012 in Revit is still pending and **strictly first**.
  - The catalog/per-row changes are **static-only**; they have NOT been runtime-verified in
    Revit. The placement path now resolves symbols per-row, but the live-Revit behavior
    (activation + Symbol resolution + work-plane / face-based placement) is unchanged from
    Decision 011; the per-row path uses the same code.
  - The `UseRevitFamilyListing` flag defaults to `false`; the Revit family listing is
    commented out. To re-enable (for cross-checks), flip the flag in
    `FireProtectionConfig.UseRevitFamilyListing`.
  - ClosedXML 0.104.1 was added; no `MSB3277` warnings observed on net8.0-windows.
- **Handoff**:
  1. First action: runtime-verify Decision 011/012 in Revit (P0) before any runtime work on
     the new catalog / per-row / device-popover code.
  2. If P0 passes: open a session that runtime-verifies the per-row family/type placement path
     (set up a model with a couple of rooms, run BruteForce, check the placement result JSON
     includes per-row `FamilyPlacementType` / `HostingStrategy` and `SkippedMissingFamilyCount`
     is 0 for a clean run).
  3. NFPA13-2022 hazard-specific rule values (TODO P1) — encode real tables and flip
     `DefaultHazardPlacementRules.HasApprovedRules` to `true`. The per-row override path
     already accommodates tighter values.

### 2026-08-27 — UI-first shared device-placement base (Smoke Detectors + Notification Appliances)

- **Context**: User wanted Smoke Detectors and Notification Appliances to reuse the sprinkler room/level/family
  selection UI without affecting sprinkler placement. Constraint refined to: only sprinkler *placement method/logic*
  is frozen; other refactors allowed. Chose approach **(A) pure duplication**: create a new shared base, leave all
  sprinkler files zero-edited. UI-first: build ViewModels/Views now, backend device (NFPA-72) logic later. Run action
  decided as "Seam + disabled" (`IDevicePlacementExecutor` + `NotImplementedDeviceExecutor`; button disabled/"Backend pending").
- **Actions Taken**:
  - New `FireProtection.UI/ViewModels/Devices/DevicePlacementViewModelBase.cs` (generic levels/rooms selection,
    family/type, eligibility = Eligible for planning, counts, filter/toggle, `ResetCommand`, disabled
    `PlaceDevicesCommand` via `IsBackendPending` seam).
  - New models `DeviceFamilyOption`, `DeviceTypeOption`, `DeviceLevelItemViewModel`, `DeviceRoomItemViewModel`.
  - New services `IDeviceFamilySource`, `IDevicePlacementExecutor`, `DeviceRoomInputItem`, `NotImplementedDeviceExecutor`.
  - New shared view `FireProtection.UI/Views/Devices/DevicePlacementView.xaml` (+`.xaml.cs`).
  - Rewrote `SmokeDetectorViewModel`/`NotificationApplianceViewModel` to inherit the base + device params
    (detector type/mount/ceiling slope; appliance type/candela/dBA); their XAML now host `DevicePlacementView` +
    parameter cards (`DeviceParamCombo` resource).
  - Cleaned `MainWindow.xaml` stray test elements (DockPanel/ListBox).
  - `dotnet build FireProtection.UI -c Debug` → 0 errors / 0 warnings. Test project compiles (Revit-ref warnings only).
- **Decisions**: All sprinkler files untouched (frozen). `72-19-PDF 1.pdf` = NFPA 72 (alarm, no sprinkler tables).
  `Layout...Sprinkler Systems (1).pdf` = image-only scan, no OCR available → NFPA-13 values still unextractable.
  Per STANDARDS_MEMORY rules 1–4 + Decision 004, do NOT invent FPE values; `GenericSprinklerRuleProvider` stays
  UNVERIFIED until OCR/real tables provided.
- **Open Questions / Blockers**: Backend device pipeline deferred (`DeviceType` enum, `IDevicePlacementRules`,
  `DevicePlacementRequest`, `DevicePlacementService`, generic `BruteForceCalculationService` overload, NFPA-72 rule
  providers). `dotnet test` cannot run headless (needs RevitAPI.dll / Revit host). OCR install (chosen earlier)
  still pending to read PDF 2 for sprinkler values.
- **Handoff**: Implement backend device pipeline (new files only; keep `BruteForceCalculationService` sprinkler
  method verbatim) and wire real `IDevicePlacementExecutor` to enable placement. Then install local OCR to extract
  NFPA-13 tables for `GenericSprinklerRuleProvider`.

---

### 2026-08-26 — Selection/level sync + default-eligible + linked-cache hardening (master prompt: SPRINKLER_SELECTION_LINKED_MODEL_PRODUCTION)

- **Context**: Continuation master prompt required the 3-state eligibility to also drive correct SELECTION behavior
  (all eligible selected by default; blocked/undetermined frozen + never selected; room Select All/Clear All; level↔room
  sync; hazard/type invalidation; truthful counts; final-placement guard) without altering the read-only preflight.
- **Actions Taken** (`SprinklerBruteForceViewModel.cs`):
  - `OnLevelItemPropertyChanged`: selecting a level now selects its **ELIGIBLE** rooms; clearing a level deselects its
    ELIGIBLE rooms; non-eligible rooms stay deselected; other levels unaffected (§7/§8).
  - `RefreshEligibility`: now normalizes selection to the authoritative state — a non-eligible room is always
    deselected; a room that **just became ELIGIBLE** is selected by default; an already-eligible room keeps the user's
    manual choice (§4/§10/§11/§19). Fixes "blocked→eligible after refresh stays unchecked".
  - `CollectSelectedRooms` already rejects `!IsEligible` (defensive guard, §16/K).
  - `ApplyDefaultSelection`/`Reset` unchanged (select eligible by default).
- **Backend** (`RevitSprinklerPlacementService.EvaluateRoomEligibility`): eligibility cache key now folds in
  room `Name` + `LevelName` alongside `RoomId` so linked-model rooms with colliding element-ids don't return another
  model's cached result (§15). **Known residual limitation:** `RoomUiData` carries no typed `LinkInstanceId`, so a
  true link-unique key (roomId+linkInstanceId) is not yet threaded end-to-end; recommended follow-up is to add
  `LinkInstanceId` to `RoomUiData` and include it in the cache key + candidate-calc dictionary. The linked-room
  preflight itself already reuses `CeilingHostResolver.FindCeilingHost` (host coords) and the extraction already
  normalizes linked geometry to host-MEP coordinates, so linked rooms are NOT auto-blocked and are evaluated in host
  placement context (§12/§13/§14).
- **Tests**: added `FireProtection.Tests/BruteForceSelectionTests.cs` (Revit-free, fake `ISprinklerPlacementService`)
  covering §22 A–G + K (default selection, Select All, Clear All, level sync, hazard invalidation, family/type
  invalidation, non-eligible-never-selected invariant). H/I/J (linked eligible/blocked/undetermined) require the
  Revit-enabled Backend and are covered by architecture reuse + manual Revit acceptance.
- **Open Questions / Blockers**: Backend (and therefore `FireProtection.Tests`) not buildable in this headless CLI
  (Revit API `CS0246`). Tests ADDED but NOT EXECUTED here.
- **Handoff**: In a Revit-enabled build, run `BruteForceSelectionTests.RunAll()` and perform the §23 manual Revit
  acceptance (normal host room, no-ceiling host room, multi-hazard, family/type change, linked eligible + linked
  no-host) confirming ELIGIBLE→enabled+selected, BLOCKED/UNDETERMINED→frozen+unchecked.

### 2026-08-26 — Eligibility reworked to 3-state deterministic preflight (ELIGIBLE/BLOCKED/UNDETERMINED); yfbxcv 1234 regression fixed

- **Context**: Continuation master prompt (`SPRINKLER_ROOM_ELIGIBILITY_CONTINUE_OPENCODE_MASTER_PROMPT.md`) superseded
  the 4-state + live-probe approach. It mandated a **3-state** model, **forbade any live `strategy.Place()` probe**,
  required a **no-ceiling WorkPlaneBased room to be BLOCKED before placement**, and required HazardClass participation.
  Production room `yfbxcv 1234` (WorkPlaneBased, no usable ceiling, SketchPlane fallback rejected by Revit) placed
  `Calculated:2, Placed:0, Failed:2` — proving the prior WorkPlaneBased-ELIGIBLE assumption was wrong.
- **Actions Taken**:
  - `FireProtection.UI/Services/PlacementEligibilityResult.cs`: 3-state DTO (`EligibilityStates` = ELIGIBLE/BLOCKED/
    UNDETERMINED; factories `Eligible`/`Blocked(2-arg & 3-arg)`/`Undetermined`/`Pending`); status codes incl.
    `NO_USABLE_CEILING_HOST`, `CALCULATION_FAILED`, `UNSUPPORTED_FAMILY_PLACEMENT`, `PREFLIGHT_ERROR`.
  - `FireProtection.Backend/.../RevitSprinklerPlacementService.cs`: `EvaluateRoomEligibility` is now a **read-only**
    deterministic preflight (no Transaction, no `FamilyInstance`). `requiresCeilingHost = FaceBased || WorkPlaneBased`;
    candidates without a usable ceiling host are skipped; ≥1 valid candidate ⇒ ELIGIBLE (records host/level info).
    Family/type unresolved ⇒ UNDETERMINED; candidates null ⇒ UNDETERMINED `CALCULATION_FAILED`; exception ⇒ UNDETERMINED
    `PREFLIGHT_ERROR`. Cache key now includes `HazardClass`. Removed `EmitCandidateDiagnostic`.
  - `FireProtection.UI/ViewModels/Sprinklers/BruteForce/RoomItemViewModel.cs`: dropped `IsPlacementError`/`IsUnknown`,
    added `IsUndetermined`; now keys off `EligibilityStates`.
  - `FireProtection.UI/ViewModels/Sprinklers/BruteForce/SprinklerBruteForceViewModel.cs`: counts split into
    Eligible/Blocked/Undetermined (`VisibleUndeterminedRoomCount` replaces `VisiblePlacementErrorRoomCount` +
    `VisibleUnknownRoomCount`); `RefreshEligibility` calc-fail ⇒ UNDETERMINED; re-runs on family/type **and** per-room
    HazardClass edit; auto-deselects non-eligible rooms.
  - `FireProtection.UI/Views/Sprinklers/BruteForce/SprinklerBruteForceView.xaml`: non-eligible rooms muted; BLOCKED red
    outline, UNDETERMINED amber outline; inline status line (state + reason) under the room name; tooltip = reason.
- **Decisions**: See [[DECISIONS]] (016) — supersedes 014/015.
- **Open Questions / Blockers**: Backend not buildable in headless CLI (Revit API `CS0246`); runtime Revit verification
  pending. The 3-state design assumes `CeilingHostResolver.FindCeilingHost` returns the same host placement would use
  (read-only) — to be confirmed at runtime on `yfbxcv 1234` and normal rooms.
- **Handoff**: Next session should run Revit with a model containing a WorkPlaneBased family + a no-ceiling room and a
  FaceBased family + normal ceiling room; confirm the no-ceiling room is BLOCKED (`NO_USABLE_CEILING_HOST`) and the
  normal room is ELIGIBLE; verify `[ROOM-ELIGIBILITY]` diagnostics.

### 2026-08-26 — Eligibility hardened to a 4-state model (ELIGIBLE/BLOCKED/PLACEMENT_ERROR/UNKNOWN); circular hide broken

- **Context**: Master prompt `SPRINKLER_ROOM_ELIGIBILITY_PRODUCTION_GRADE_FIX_MASTER_PROMPT.md` required that the
  BruteForce UI block **only** deterministically-unplaceable rooms and **surface real placement defects** instead of
  hiding them behind "blocked". Hard rules: Error ≠ Blocked; Unknown ≠ Eligible/Blocked; fix the actual placement
  defect (do **not** mask it by enlarging `PlacementValidationToleranceFt`); break the circular dependency where a
  broken placement path made preflight mark every room BLOCKED.
- **Actions Taken**:
  - `FireProtection.UI/Services/PlacementEligibilityResult.cs`: replaced the boolean `IsEligible` field with a
    `EligibilityState` (ELIGIBLE/BLOCKED/PLACEMENT_ERROR/UNKNOWN) + derived `IsEligible/IsBlocked/IsPlacementError/
    IsUnknown`; added factories `Eligible`, `Blocked(template,code,reason)`, `PlacementError(template,code,reason)`,
    `Unknown(reason,code)`; `Pending()` now maps to UNKNOWN (family not selected). Added status codes
    `CREATED_BUT_INVALID`, `PROBE_EXCEPTION`, `PLACEMENT_ERROR`, `UNKNOWN`.
  - `FireProtection.UI/ViewModels/Sprinklers/BruteForce/RoomItemViewModel.cs`: carries `EligibilityState` /
    `IsPlacementError` / `IsUnknown`; `SetEligibility` copies the 4-state result and raises all four flags.
  - `FireProtection.Backend/.../RevitSprinklerPlacementService.cs`: rewrote `EvaluateRoomEligibility` to classify the
    4 states. Deterministic inability (incl. FaceBased `REQUIRED_HOST_UNAVAILABLE`) → BLOCKED; any unexpected
    API/runtime failure or created-but-invalid → PLACEMENT_ERROR; family not selected / calc could not run → UNKNOWN;
    ≥1 spatially-valid candidate → ELIGIBLE. Any exception in the probe harness/strategy → `PLACEMENT_ERROR`
    (`ProbeException`), **not** BLOCKED — this is the circular-hide break. Added `[ROOM-CANDIDATE-DIAGNOSTIC]` `Debug`
    output (requested vs actual XYZ, delta, distance, validation status, exception detail) to pinpoint the defect.
  - `FireProtection.UI/ViewModels/Sprinklers/BruteForce/SprinklerBruteForceViewModel.cs`: `RefreshEligibility` now sets
    **UNKNOWN for every room when the candidate calculation throws** (was: would probe a null list → BLOCKED);
    auto-deselects any non-ELIGIBLE room. Counts split into `VisibleEligibleRoomCount` / `VisibleBlockedRoomCount` /
    `VisiblePlacementErrorRoomCount` / `VisibleUnknownRoomCount`; `RoomsFoundText`/`RoomsSelectedSummary` surface all
    four. Selectable population = `IsEligible` only (grid `IsEnabled=IsEligible`, single Select-All, `ApplyDefaultSelection`,
    `CollectSelectedRooms` guard, "Show eligible only" filter all keyed to `IsEligible`).
  - `FireProtection.UI/Views/Sprinklers/BruteForce/SprinklerBruteForceView.xaml`: non-eligible rooms muted (Opacity);
    PLACEMENT_ERROR gets a red outline, UNKNOWN an amber outline; checkbox `IsEnabled=IsEligible` + tooltip = reason.
- **Decisions**: See [[DECISIONS]] (014). The `(0,0,0)` origin-snap defect was already fixed by Decisions 011/012
  (no Level-overload; all strategies place at the requested ceiling-height point; `CeilingHostResolver` handles linked
  ceilings correctly) — verified by code inspection. If a residual `CREATED_BUT_INVALID` appears at runtime (e.g.
  requested Z from `CeilingHeightFt` vs actual ceiling-face elevation mismatch), it will surface as `PLACEMENT_ERROR`
  with the deviation in `Reason` — to be fixed at the placement/coordinate source, **not** by enlarging tolerance.
- **Verification (static)**: `dotnet build FireProtection.UI -c Debug` → **0 errors, 0 warnings**. Backend/Tests NOT
  buildable in this headless CLI (Revit API `CS0246` — environmental). Runtime Revit verification **pending** (no Revit
  host). The probe is the same code path as placement, so its classification is the fix's whole point, but the four
  counts (Eligible/Blocked/PlacementError/Unknown) must be confirmed in a live Revit session.
- **Handoff**: On a Revit host, run the BruteForce flow and read `[ROOM-ELIGIBILITY]` + `[ROOM-CANDIDATE-DIAGNOSTIC]`
  `Debug` output. If rooms show PLACEMENT_ERROR with `CREATED_BUT_INVALID`, capture requested-vs-actual Z and fix the
  coordinate source (likely `CeilingHeightFt` vs linked-ceiling elevation). Do not bump `PlacementValidationToleranceFt`
  to mask it.

### 2026-08-26 — BruteForce UI: room eligibility, blocked-room handling, eligibility/levels-only toggles, default selection, and Preliminary/Final tab swap

- **Context**: Master prompt `FIRE_PROTECTION_UI_SELECTION_MASTER_PROMPT.md` asked for UI-only changes so that
  rooms are flagged eligible/blocked from existing authoritative data, blocked rooms are excluded from
  selection/placement and shown disabled, two new filter toggles ("Show eligible rooms only",
  "Show levels with rooms only") drive visibility (never destroy the source collections / selection), a
  default selection seeds eligible levels + eligible rooms (blocked never selected), Select All skips
  blocked, and the Preliminary/Final tab labels + order are swapped (BruteForce = "Preliminary" first,
  Collision = "Final" second). Explicit constraint: **UI-only — do not touch NFPA/spacing/calculation/
  placement/hosting/coordinate/Revit logic**, and preserve MVVM (no business logic in XAML code-behind).
- **Actions Taken** (all in `FireProtection.UI`):
  - `ViewModels/Sprinklers/BruteForce/RoomItemViewModel.cs`: added `IsEligible`, `IsBlocked`,
    `EligibilityReason` (readonly), computed in ctor via `EvaluateEligibility(room, out reason)`. Eligibility
    source = existing authoritative data only: `room.Geometry != null && Geometry.Polygon.Count >= 3 &&
    Geometry.CeilingHeightFt.HasValue`. No engineering rules invented.
  - `ViewModels/Sprinklers/BruteForce/SprinklerBruteForceViewModel.cs`: added `ShowEligibleRoomsOnly` and
    `ShowLevelsWithRoomsOnly` bool props (setters call `RoomsView.Refresh()` / `LevelsView.Refresh()` only);
    `FilterLevel` drops empty levels when `ShowLevelsWithRoomsOnly`; `FilterRoom` drops blocked rooms when
    `ShowEligibleRoomsOnly`. Replaced the two-button Select All / Clear commands with **one smart toggle per
    section**: `ToggleSelectAllLevelsCommand` / `ToggleSelectAllRoomsCommand`, whose label is derived live from
    `AreAllSelectableLevelsSelected` / `AreAllSelectableRoomsSelected` (computed over the selectable
    population only — levels with rooms, and non-blocked rooms — so blocked/empty items never stick the button
    on "Select All"). Manual checkbox changes are synced via `PropertyChanged` subscriptions
    (`OnLevelItemPropertyChanged` / `OnRoomItemPropertyChanged`) that raise `LevelSelectionToggleLabel` /
    `RoomSelectionToggleLabel`. `ApplyDefaultSelection()` (selects eligible levels + eligible rooms; never
    blocked) is called from ctor and `Reset`.
  - `Views/Sprinklers/BruteForce/SprinklerBruteForceView.xaml`: added the two CheckBox filter toggles (level
    section `Show levels with rooms only` + room section `Show eligible rooms only`) and, per section, **one
    smart toggle button** bound to `ToggleSelectAllLevelsCommand` / `LevelSelectionToggleLabel` (and the room
    equivalents) — replaces the previous two permanent Select All / Clear buttons. Blocked rooms are styled —
    row `Opacity=0.55` via `IsBlocked` DataTrigger, room CheckBox `IsEnabled="{Binding IsEligible}"` +
    `ToolTip="{Binding EligibilityReason}"`.
  - `Views/Sprinklers/SprinklerView.xaml`: swapped header labels — Collision header = "Final", BruteForce
    header = "Preliminary".
  - `ViewModels/Sprinklers/SprinklerViewModel.cs`: `SubTabViewModels` order now `{ BruteForce, Collision }`,
    `_selectedSubTabViewModel = BruteForce` (so BruteForce shows first as "Preliminary").
- **Decisions**: Eligibility derived strictly from existing `RoomUiData.Geometry` (no new engineering rules),
  per master-prompt §3. Filtering is visibility-only via `ICollectionView.Refresh()`; source `ObservableCollection`
  and selection state are preserved so filtering cannot destroy selection.
- **Verification (static)**: `dotnet build FireProtection.UI -c Debug` → **0 errors, 0 warnings** (my UI-only
  changes compile cleanly). **Backend/Tests could NOT be built or run in this headless CLI environment** because
  the Revit API assembly is not resolvable under standalone `dotnet build` (`CS0246: Document/FamilyInstance/
  Level/Element not found`) — an **environmental limitation** (these projects build inside Visual Studio where
  the Revit references resolve), **not a code defect, and unrelated to the UI-only change**. Runtime UI/Revit
  verification still pending; no live Revit host here.
- **Open Questions / Blockers**: Runtime UI verification in Revit not performed (no live host). The
  Preliminary/Final label swap is presentation-only; the underlying ViewModels/commands are unchanged, so no
  behavioral risk — but a user visual check is recommended.
- **Handoff**: Run the add-in in Revit, open BruteForce ("Preliminary") tab, confirm blocked rooms appear
  greyed/disabled with a tooltip, the two toggles filter visibility without losing selection, and Reset
  re-applies the eligible default selection. No placement/calculation code was touched.

---

### 2026-08-25 — Decision 012: placement records the ACTUAL host/level read back from the created instance (diagnostics-only)

- **Context**: Continuing P0 (physically-correct, runtime-verifiable placement). Before touching any
  placement semantics, inspected the current Decision 011 implementation to establish the 10 runtime
  evidence fields. Found the post-placement diagnostics recorded requested-vs-actual XYZ, strategy, ceiling
  source and status — but did **not** read back the created instance's ACTUAL host / associated level /
  Schedule Level. Those are exactly the fields that distinguish a genuine hosting defect from a view-range /
  level-association symptom (the "sprinkler shows in the wrong floor plan" report). Closed that gap — the
  single permitted immediate edit — so **one** Revit run yields complete evidence.
- **Actions Taken**:
  - `FireProtection.UI/Models/Sprinklers/BruteForce/SprinklerPlacementResult.cs`: added 8 diagnostic fields
    to `PlacedSprinklerEntry` — `FamilyPlacementType`, `HostCeilingElementId`, `LinkInstanceName`, and the
    ACTUAL read-backs `ActualHostElementId`, `ActualHostName`, `ActualInstanceLevelId`,
    `ActualInstanceLevelName`, `ActualScheduleLevelName`. Revit-free DTO; Newtonsoft serializes automatically.
  - `FireProtection.Backend/Services/Placement/Sprinklers/Final/RevitSprinklerPlacementService.cs`: populate
    those fields on the success path from `outcome` + read-only `ReadActual*` helpers
    (`instance.Host`, `instance.LevelId`, `INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM`). All helpers are best-effort
    try/catch → null so a read failure never breaks placement. **No placement/strategy semantics changed.**
  - Re-ran build (Revit2025, **0 errors**, 2 benign warnings) and `FireProtection.Tests` (**all PASS**).
- **Decisions**: [[DECISIONS]] **012** recorded (Implemented — static + build-verified; runtime pending).
  Deliberately diagnostics-only per the "DO NOT change placement strategy blindly" constraint.
- **ACTUAL-location-is-truth**: actual fields are read from the created instance and are **never**
  back-filled with requested coordinates. The export must tell the truth about what Revit created.
- **Open Questions / Blockers**: **Runtime verification still BLOCKED** — no live Revit host here. Cannot
  yet observe whether the instance is hosted, on which level, and whether it is physically at the requested
  point. NFPA-13 spacing values remain UNVERIFIED (PDF 2 image-only).
- **Handoff**: Run the add-in once in Revit 2025 (host `02_FireProtection_Test.rvt` + link
  `01_Architectural_Test.rvt`, family `Sprinkler - Pendent - Hosted`, type `3/4" Pendent on Drop with
  Guard`) and return the per-entry diagnostics (see `TODO.md` P0 / `PROJECT_MEMORY.md` §15). Triage:
  physically-correct-but-wrong-plan ⇒ view-range / `ActualScheduleLevelName` issue — **do not move coords**;
  else genuine hosting defect. No further code changes without runtime evidence.

---

### 2026-08-25 — Context Compaction: PROJECT_MEMORY + STANDARDS_MEMORY created; doc-drift reconciled

- **Context**: A context-compaction/continuation master prompt asked for ONE authoritative compact context
  file so future sessions continue without rereading the whole repo or both PDFs. **No production code
  modified this session** (verified — see below).
- **Actions Taken**:
  - Created **`PROJECT_MEMORY.md`** (single source of current truth) and **`STANDARDS_MEMORY.md`**
    (evidence-grounded standards cache) in the project root.
  - **Verified actual source vs docs** by reading `RevitSprinklerPlacementService.cs`, all
    `…/Final/Strategies/*` (`PlacementStrategyContracts`, `CeilingHostResolver`, `FaceBasedPlacementStrategy`,
    `WorkPlaneBasedPlacementStrategy`, `LevelBasedPlacementStrategy`), and
    `UI/Models/Sprinklers/BruteForce/SprinklerPlacementResult.cs`.
  - **Confirmed the code now implements Decision 011** (explicit `IFamilyPlacementStrategy` per proven
    `FamilyPlacementType`; WorkPlaneBased uses a ceiling face **or** a `SketchPlane` honoring world Z,
    **never** the Level overload; post-placement spatial validation; structured status codes; requested-vs-
    actual XYZ recorded). This **supersedes Decision 010**.
  - Re-confirmed baseline from `.analysis/` logs (Revit2025 build **0 errors**; **14/14** tests) — **not
    re-run** this session.
- **DOC-DRIFT FLAGGED (history preserved, not rewritten):** the entry below ("Sprinkler Placement Fix
  IMPLEMENTED") and `PROGRESS.md` / `TODO.md` / `SPRINKLER_ACTUAL_Z_DIAGNOSTIC.md` still describe the
  **Decision 010** shape (`isFaceBased` matching `WorkPlaneBased`; `FindCeilingHost` inside the service;
  a WorkPlaneBased→Level *fallback*; `file:line` refs like `…Service.cs:254-263`). **Those `file:line`
  references are obsolete** — the code moved to the strategy pattern. They remain correct about the
  **runtime (0,0,0) root cause**, only wrong about the current **fix shape**. See `PROJECT_MEMORY.md` §0.
- **Decisions**: [[DECISIONS]] 011 confirmed implemented (STATIC/BUILD-VERIFIED); 010 SUPERSEDED.
- **Open Questions / Blockers**: **Runtime verification of Decision 011 is still pending / BLOCKED** — no
  live Revit host in this environment. NFPA-13 spacing values remain **UNVERIFIED** (PDF 2 image-only) — see
  [[STANDARDS_MEMORY]].
- **Handoff**: Read `PROJECT_MEMORY.md` then `STANDARDS_MEMORY.md` first. Next action = **runtime-verify
  placement** in Revit 2025 (host `02_FireProtection_Test.rvt` + link `01_Architectural_Test.rvt`, family
  `Sprinkler - Pendent - Hosted`): expect actual XYZ ≈ (14.12, 31.95, 12) for Room 1235683, no (0,0,0)
  instances, strategy `WorkPlaneCeilingFace`/`WorkPlaneSketchPlane`. Do **not** invent NFPA-13 values.

---

### 2026-08-25 — Persistent Project Memory System Created

- **Context**: A master prompt requested a durable, Obsidian-friendly memory system (7 Markdown files)
  so future OpenCode sessions do not rely on prior chat context. No application code may be modified.
- **Actions Taken**:
  - Created the 7 memory files in the project root: `AGENTS.md`, `PROJECT_CONTEXT.md`,
    `ARCHITECTURE.md`, `DECISIONS.md`, `PROGRESS.md`, `TODO.md`, `SESSION_NOTES.md`.
  - Grounded all content in actual source (read `FireProtection.Backend.csproj`,
    `FireProtection.UI.csproj`, `FireProtection.Tests.csproj`, `FireProtectionApplication.cs`,
    `FireProtectionCommand.cs`) and `TextFile1.txt`.
  - Explicitly recorded that **"Snowdon" is not an integration** — it is only a sample dataset name in
    `extractTest.json` and a design note in `TextFile1.txt`. No `Snowdon*` classes exist.
  - Recorded the prior code-cleanup pass (dead exporters/overloads removed; unused `using`s removed;
    builds clean across Revit2024/2025/2026; 14/14 tests pass) so it is not lost.
- **Decisions**: see [[DECISIONS]] (Decisions 001–009 + Undetermined section).
- **Open Questions / Blockers**:
  - Runtime Revit placement has never been executed in this environment (no live Revit host).
  - NFPA13-2022 compliance path and Collision/Smoke/Notification scope are unestablished in the repo.
- **Handoff**: Next session should (1) prioritize runtime placement verification, (2) encode real
  NFPA spacing, and (3) resolve the empty-shell Collision/Smoke/Notification tabs — but only after
  reading the memory files and confirming intent with the user.

---

### 2026-08-25 — Sprinkler Level / Z Placement Diagnostic

- **Context**: Master prompt `SPRINKLER_LEVEL_Z_PLACEMENT_FIX_MASTER_PROMPT.md` asked to diagnose a
  "First Floor sprinkler appearing in the Ground Floor plan" issue and fix code only if actually wrong.
- **Actions Taken**: Traced the full pipeline
  (RoomExtractor → BruteForce Z → ResolveHostLevel → NewFamilyInstance). Verified:
  - Linked geometry is normalized to host-MEP feet during extraction (no double transform in placement).
  - Host-level resolution (`ResolveHostLevel` path #1) returns the exact host `Level` for host rooms.
  - Linked-level resolution (path #2) transforms the link level elevation by the link transform and
    matches the host Level by elevation — coordinate-correct.
  - Placement uses the calculated `XYZ` unchanged in both face-based and level-based branches.
- **Decisions**: Concluded **Case C (Revit View Range / view configuration)**. No code change made
  (per the prompt's "no unnecessary changes" rule). Full report:
  `SPRINKLER_LEVEL_Z_PLACEMENT_FIX_REPORT.md`.
- **Separate finding**: `RoomExtractor.FindCeilingsForRoom` Z-overlap window can match the level-below
  slab, and `BruteForceCalculationService` `FirstOrDefault(FLAT)` can then pick a lower ceiling as the
  placement plane. This is a real defect but does NOT cause the reported Ground-Floor symptom (its
  world Z stays at the room's own level). Tracked in [[TODO]]; not applied.
- **Validation**: Revit2024/2025/2026 build = 0 errors; `FireProtection.Tests` = 14/14 PASS.
  Runtime Revit verification NOT performed (no live model in this environment).
- **Open Questions / Blockers**: Confirm in Revit whether the sprinkler's `Schedule Level`/`Elevation
  From Level` are correct and whether Ground Floor View Depth/Underlay includes the element's world Z.
- **Handoff**: If runtime confirms correct world Z + correct Schedule Level, the issue is View Range.
  Address the ceiling-association weakness only as a deliberate, separate change.

---

### 2026-08-25 — Sprinkler Actual Z Diagnostic (RUNTIME PROOF)

- **Context**: Ran the temporary Z diagnostic (instrumentation added in
  `RevitSprinklerPlacementService.PlaceSinglePoint`) in Revit 2025 against `02_FireProtection_Test.rvt`
  (host) + linked `01_Architectural_Test.rvt`. Captured `sprinkler_z_diagnostic.txt`,
  `sprinkler_placement_result_*.json`, `ModelSnapshot_*.json`.
- **Key captured numbers (Room 1235683 / L1):**
  - Candidate Z = 12, Passed Z = 12, Host Level = L1 (elev 0)
  - **Actual Instance XYZ = 0, 0, 0** (project origin)
  - `FaceBasedPlacement: False`
- **Root cause (PROVEN): CASE B — placement-overload / family-hosting bug.**
  `Sprinkler - Pendent - Hosted` is a hosted family whose `FamilyPlacementType` is `WorkPlaneBased`,
  but the code only detects `"FaceBased"`, so it used the level-based `NewFamilyInstance(xyz, symbol,
  level, NonStructural)` overload. A hosted family placed without a host lands at (0,0,0). Link
  transform is identity, so coordinate transformation is NOT the issue. Elevation/level resolution is
  correct; the bug is purely the wrong placement overload.
- **Correction:** Supersedes the earlier static-only conclusion in
  `SPRINKLER_LEVEL_Z_PLACEMENT_FIX_REPORT.md` (which said Case C / View Range / CODE FIX NOT REQUIRED).
  That conclusion was wrong; runtime evidence shows a real code bug.
- **Required fix (identified, NOT implemented):** `RevitSprinklerPlacementService.cs` —
  `PlaceSinglePoint` must detect `WorkPlaneBased` (and `FaceBased`) as hosted and use the face-based
  `NewFamilyInstance(hostRef, xyz, dir, symbol)` overload; `FindCeilingHost` must also search linked
  models and return a linked `Reference` via `Reference.CreateLinkReference`. Also record the ACTUAL
  `instance.Location` in the placement result (currently it echoes the input point, masking the bug).
- **Open Questions**: none on root cause; awaiting go-ahead to implement the fix.
- **Handoff**: confirm in Revit that Actual Instance XYZ ≈ (14.12, 31.95, 12) after the fix; remove the
  stray (0,0,0) instances from prior runs.

---

### 2026-08-25 — Sprinkler Placement Fix IMPLEMENTED

- **Approved & implemented** the placement-overload fix identified in the diagnostic:
  - `RevitSprinklerPlacementService.cs`:
    - `isFaceBased` detection now also matches `FamilyPlacementType.WorkPlaneBased` (ceiling-hosted
      sprinklers), not just `FaceBased`.
    - `FindCeilingHost` now searches the host document AND linked models; for linked ceilings it maps the
      host-space point into link coordinate space and returns a host reference via
      `linkFaceRef.CreateLinkReference(linkInstance)` — the `Reference.CreateLinkReference(RevitLinkInstance)`
      **instance** method. (Note: the 2-arg static overload and the `Reference(linkInstance, ref)`
      constructor do NOT exist in this Revit API build.)
    - `PlacedSprinklerEntry` now records the ACTUAL `instance.Location` (was echoing the input point,
      which masked the bug).
    - Temporary `TEMPORARY Z DIAGNOSTIC` instrumentation removed (field, helper, `using System.IO`).
- **Build**: Revit2024 and Revit2026 compile clean (0 errors). Revit2025 compiles but the bin copy is
  blocked by a VS/Revit lock on `FireProtection.UI.dll` (environmental, not a code error).
- **Verification status**: static only. Runtime Revit verification is the remaining step (re-run the
  add-in; expect Actual Instance XYZ ≈ (14.12, 31.95, 12) for Room 1235683). The 14/14 unit tests are
  logic-only and do not exercise Revit placement.
- **Open**: user to re-run in Revit and confirm; then delete prior (0,0,0) misplaced instances.

### Prior Sessions (summary; no detailed chat history imported)

- **Phase 1 — Model Extraction**: implemented read-only, zero-transaction extraction (levels, rooms,
  ceilings, obstacles, existing sprinklers) with linked-coordinate normalization. Status: implemented
  (static). Source of truth: `Services/Model/*`, `Services/Extraction/*`.
- **Phase 2 — Sprinkler Placement (BruteForce)**: implemented `PlacementInputBuilder`,
  `BruteForceCalculationService` (X/Y/Z), `RevitSprinklerPlacementService` (actual `FamilyInstance`
  creation + linked-level resolution), and the WPF BruteForce UI. Status: implemented (static).
  Documented in `SPRINKLER_POINT_CALCULATION_EXPLAINED.md`.
- **Code Cleanup**: removed dead `ExportCalculationResult`, `GetDefaultCalculationExportPath`,
  `Extract`, `ExtractFromHostModel`, 3 `UiLauncher.Show` overloads, and MainWindow 1/2/3-arg
  constructors; removed unused `using`s. Builds clean; 14/14 tests pass.

> Note: The summaries above are reconstructed from repository evidence (code + prior cleanup). Detailed
> step-by-step chat history from those sessions is not available in this environment. Treat them as
> high-confidence but not transcribed.

## 2026-08-26 — Placement Preflight → AUTHORITATIVE probe (Decision 013, corrected)

Goal: the BruteForce UI must block rooms the production pipeline provably cannot place. The first-pass preflight
(representative centroid point + `WorkPlaneBased == always eligible`) was **runtime-insufficient** — rooms reported
eligible while actual placement produced `Placed=0, INVALID=N, Failed=N`. Root cause: it never ran the real
calculation or a real placement probe over the candidate set, so the spatially-invalid (e.g. (0,0,0)) placements
were invisible to it.

- **Final design (authoritative):** Eligibility is produced **only** by the Backend, but now reuses the *full*
  placement pipeline. The UI computes each room's **real candidate points** via the exact
  `BruteForceCalculationService` (`IPlacementInputExporter.CalculateBruteForce`), then the Backend probes **real
  placement** of every candidate through the same `strategy.Place` / `ResolveHostLevel` / `CeilingHostResolver`
  path as `PlaceSprinklers`, inside a **rolled-back `Transaction`**. A room is eligible **only if ≥1 candidate
  creates a `FamilyInstance` that is spatially valid** (deviation ≤ `PlacementValidationToleranceFt`) — the identical
  success criterion as real placement, so the `INVALID` blind spot is gone. `FireProtection.UI` stays Revit-free.
- **Backend:** `RevitSprinklerPlacementService.EvaluateRoomEligibility(RoomUiData, IReadOnlyList<CalculatedSprinklerPoint> candidates, family, type)`;
  fail-closed status codes `NO_CANDIDATE_POINTS` / `NO_PLACEABLE_CANDIDATE` / `NO_VALID_CANDIDATE` / `PREFLIGHT_ERROR`;
  results cached per (family/type/room) + `ClearEligibilityCache()`.
- **UI:** `CollectAllVisibleRooms()` builds inputs for every visible room; `RefreshEligibility()` runs the probe and
  `RoomItemViewModel.SetEligibility` applies it; blocked rooms disabled in grid + Select-All toggle and
  auto-deselected on family/type change; `CollectSelectedRooms` + `CanExecutePlaceSprinklers` (now gated on
  `SelectedVisibleEligibleRoomCount`) hard-guard blocked rooms; `[ROOM-ELIGIBILITY]` / `[ROOM-SELECTION-GUARD]`
  `Debug` diagnostics (§17).
- **Verification:** UI builds clean (0 errors, 0 warnings). Backend not buildable in this headless CLI
  (Revit API `CS0246` — environmental); type-correctness checked by inspection against `PlacementContext`,
  `strategy.Place`, `PlacementOutcome`, `LevelResolution`. **Runtime Revit verification still pending** (no Revit host
  here) — but the probe IS the real placement path, which is what makes the UI state agree with reality.
- **Files:** new `UI/Services/PlacementEligibilityResult.cs`; edited `UI/Services/ISprinklerPlacementService.cs`,
  `UI/ViewModels/Sprinklers/BruteForce/RoomItemViewModel.cs`, `UI/ViewModels/Sprinklers/BruteForce/SprinklerBruteForceViewModel.cs`,
  `Backend/.../RevitSprinklerPlacementService.cs`. Docs: `DECISIONS.md` (013), `PROJECT_MEMORY.md` (§8b), `PROGRESS.md`.

## Related Documentation

- [[PROJECT_CONTEXT]]
- [[ARCHITECTURE]]
- [[DECISIONS]]
- [[PROGRESS]]
- [[TODO]]
- [[SPRINKLER_POINT_CALCULATION_EXPLAINED]]

---

### 2026-09-11 — Cross-device skip/fail fix, catalog-derived attributes, sprinkler run report (Decision 021)

- **Context:** continued the device-pipeline work (smoke/NA calc repaired 2026-09-10). Three defects fixed
  together; one honesty regression reverted. All static — the Revit runtime gate is unchanged (no host here).
- **Actions Taken:**
  - `FireAlarmDevicePlacementCore`: existing-device collection + skip/replace + duplicate guard are now
    **kind-scoped** (`DeviceKindResolver.TryResolve` attributes every `OST_FireAlarmDevices` instance by
    family/type name; only own-kind drives the policy; unclassifiable = never touched). Skip messages name
    the kind. Root cause of "smoke→NA skipped / NA→smoke failed".
  - Device core also gained: calc-level `ReviewRequired` no longer clobbered to "Success";
    `OverallStatus="Cancelled"` + summary on rollback; outside-room coordinate guard (ported from the
    sprinkler service).
  - `DevicePlacementViewModelBase` + smoke/NA VMs + both views: DetectorType/Mount/CeilingSlope and
    ApplianceType/Candela/dBA are **derived from the catalog row of the row's own family/type** and shown
    read-only when derivable (new `DeriveAttribute` / `OnUniversalFamilyTypeChanged` hooks,
    `Show*Picker` visibility); per-level attribute popover retired (`HasLevelSettings=false` hides the ⋯).
  - New `SprinklerPlacementReportMapper` (UI) + `SprinklerBruteForceViewModel`: sprinkler runs open the
    same `PlacementResultReportWindow`; report shows OverallStatus, provisional/rules line, skipped-room
    issues; window now merges the theme dictionaries (was silently falling back to system grey).
  - `DefaultHazardPlacementRules`: uncommitted `HasApprovedRules=true`/`IsProvisional=false` flip reverted
    — **no FPE sign-off exists anywhere in the repo** (docs still list it as TODO P1). Spacing values kept;
    only the approval claim fixed.
  - `BruteForceCalculationService`: C-E grid-truncation guard — when the candidate sweep stops at
    `MaxCandidatePoints` with points left over, the room flags ReviewRequired with a warning.
  - Sprinkler S→S / Wall columns: cells now show the value the calculation actually used
    (`AppliedMaxSpacingFt/Clearance` fed back from the eligibility pass), editable with range validation
    (1–40 ft spacing, 0–10 ft wall space; blank = clear override); bulk apply + per-row reset re-run the
    preflight so columns refresh.
  - New tests: `FireProtection.Tests/DeviceReportAndKindTests.cs` (mapper verdicts, kind separation).
- **Decisions:** [[DECISIONS]] 021. PROJECT_MEMORY §Update 2026-09-11.
- **Verification:** BUILD 0 errors (Revit2025 + Revit2026) · TESTS all PASS · RUNTIME-UNVERIFIED.
- **Open Questions / Blockers:** who signed off the NFPA-13 values (if anyone) — until an FPE confirms,
  `HasApprovedRules` stays false; name-based kind attribution can mis-bucket oddly-named families.
- **Handoff:** live Revit pass — (1) place smoke → place NA in same rooms with policy Skip: neither may
  skip/delete the other; (2) confirm derived attributes follow per-row family changes into the calc;
  (3) confirm the sprinkler report window renders themed + truthful on a real run.
