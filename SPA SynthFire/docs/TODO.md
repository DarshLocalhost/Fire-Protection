# SPA SynthFire — TODO Tracker

Status legend: `[ ]` todo · `[~]` in progress · `[x]` done · `[!]` blocked/needs input

Built from scratch. FireProtectionSystem is NOT a code source. See `docs/ROADMAP.md`.

## Phase 0 — Foundations  ✅ (2026-09-21, awaiting visual Revit confirmation)
- [x] Create `SPA.SynthFire.slnx` and project skeletons (Core, Catalog, Placement, Estimator, Revit, UI, Tests)
- [x] .NET 8 target (Core/Catalog/Placement/Estimator/Tests = net8.0; Revit/UI = net8.0-windows); x64
- [x] Revit API references (RevitAPI, RevitAPIUI) via `RevitVersion`-switched HintPath, Private=False — builds for 2025 AND 2026
- [x] MSBuild `DeployToRevit` target generates `.addin` manifest + deploys (opt-in: `-p:DeployToRevit=true`)
- [x] `IExternalApplication` stub: "SPA SynthFire" ribbon tab + "Fire Protection" panel + SynthFire button
- [x] `IExternalCommand` stub wired to the button (TaskDialog)
- [x] Headless xunit test harness runs (2 passing geometry tests)
- [x] Solution builds (0 errors); add-in deployed to Revit 2025 + 2026 addins folder
- [x] Visual confirm: Revit → "SPA SynthFire" tab → SynthFire button → dialog appears ✅ verified in Revit sample project 2026-09-22

## Phase 1 — Standards + catalog  ✅ (2026-09-21)
- [x] `IDesignStandard` / `SprinklerRuleSet` abstraction (max area, max spacing, max-to-wall, min spacing) + `Units` (m↔ft)
- [x] NFPA 13 rule set (LH/OH1/OH2/EH1/EH2 — provisional, `HasApprovedRules=false`)
- [x] AS2118 rule set (LH/OH1-3/HHP metric — provisional, `HasApprovedRules=false`)
- [x] `UserDesignCriteria` overlay + `DesignCriteriaResolver` (may only tighten, never relax the code)
- [x] `IsProvisional` gating carried on every rule set; `StandardRegistry` for runtime standard selection
- [x] Excel catalog loader (fail-fast, ClosedXML) + `CatalogValidator` (dup=error) + models (sprinklers + detectors) + `ICatalog`/`CatalogService`
- [x] `CatalogTemplateWriter` (starter workbook)
- [x] Verify: 17 xunit tests green (rule values, m→ft conversion, criteria clamping, catalog round-trip + validation)

## Phase 2 — Sprinkler placement (wet fire)  ~ (solver ✅ tested; live Revit run awaiting user)
- [x] Room-geometry snapshot model (`RoomInput`, `Rectangle2D`) — Revit-independent
- [x] Coverage/spacing solver honoring active standard (`SprinklerPlacementSolver`, grid + area refine + min-spacing/provisional review flags)
- [ ] Ceiling-tile snapping (deferred — needs ceiling grid read; Phase 2b)
- [x] Residual-gap / min-spacing conflict detection + review flags
- [x] Revit: read rooms → build snapshot (`RoomReader`, bbox footprint + ceiling elevation)
- [x] Revit: resolve + place sprinkler families (`SprinklerPlacer`, finds OST_Sprinklers symbol, transaction)
- [x] Placement options dialog + report UI (WPF `PlacementOptionsWindow`, `PlacementReportWindow`) + ribbon "Place Sprinklers" button
- [x] One-button re-run (re-running lays out again; does NOT yet clear prior heads — Phase 3)
- [x] Verify: 25 xunit tests green (8 solver tests); full build 0 errors; deployed to Revit 2026
- [!] LIVE VERIFY (user): open model with rooms + a loaded Sprinklers family → Place Sprinklers → pick standard/hazard → heads placed + report shows
- [!] KNOWN GAP: ClosedXML not in deploy output (Catalog transitive dep) — harmless in Phase 2 (catalog unused in Revit); fix before Phase 4 wires catalog into Revit

## Phase 2b — Per-family placement strategy  ✅ (solver/logic; live-verify pending)
- [x] `SprinklerMountResolver` classifies family → Ceiling / Sidewall / Free (by placement type + name)
- [x] Ceiling: ray-cast to ceiling face + host (pendent/upright)
- [x] Sidewall: host to room's longest wall(s), space heads along them
- [x] Free: exact-XYZ unhosted placement (fixes the wrong-floor bug)
- [!] LIVE VERIFY: heads land on correct floor + hosted correctly per family type

## Phase 3 — Clash avoidance + coordination  ~ (solver ✅ tested; live-verify pending)
- [x] Query nearby model elements — `RoomReader.ReadObstacles` (ducts, structural framing, cable tray, pipes) via BoundingBoxIntersectsFilter
- [x] Shift-or-flag logic — solver `AvoidObstacles` nudges heads off obstacle boxes, flags review if unresolvable (3 clash tests green)
- [x] Redesign / re-run — `SprinklerPlacer` tags heads (Comments="SPA SynthFire") + clears previous on re-run
- [!] Verify: seeded-clash model — heads shift off ducts, report shows shifted/review counts

## Phase 4 — Detection placement (dry fire)  ~ (smoke/heat done; notification pending)
- [x] Shared `CeilingGridLayout` engine (sprinkler + detector reuse; clearance + wall standoff + 8-way nudge)
- [x] Smoke/heat detector rules (`Nfpa72DetectionRules`: smoke 30ft/900ft², heat 50ft/2500ft² — provisional)
- [x] Detector solver (reuses shared grid), `DetectorFamilyReader`, `DetectorPlacer` (ceiling-host + redesign clear), `DetectorOptionsWindow`, "Place Detectors" ribbon button
- [ ] Notification-appliance solver (audible/visible coverage) — still pending
- [x] Verify (headless): 32 tests green (3 detection tests)
- [!] LIVE VERIFY: Place Detectors → smoke/heat hosted to ceilings, report shows counts

## Phase 5 — Estimator  ~ (logic ✅ tested; live-verify pending)
- [x] Quantity takeoff / BOM (`EstimateBuilder` groups placed devices by category/family/type, optional price list + totals)
- [x] CSV export (`EstimateBuilder.ToCsv`); `EstimateCommand` collects tagged devices → writes CSV to Desktop + summary
- [x] "Estimate" ribbon button
- [x] Verify (headless): 36 tests green (4 estimator tests)
- [!] LIVE VERIFY: place devices → Estimate → CSV counts match placed elements

## Level/elevation fix (recurring "everything at ground level")
- [x] `PlacementLevels.Apply` forces level param + elevation-from-level offset on all non-face-hosted placements (sprinkler free/sidewall, detector free/wall)
- [!] LIVE VERIFY: upper-floor devices sit on their own floor (host-driven read-only elevation may still need per-family handling)

## Phase 6 — UI polish + hardening
- [ ] Dockable panels + design-criteria editor
- [ ] Batch redesign, robustness, error handling
- [ ] User docs

## Open questions / blockers
- [!] Confirm Revit 2026 + API assemblies are installed on the dev/test machine for integration testing
- [!] Confirm .NET 8 SDK available
- [ ] Decide first sprinkler type/standard to target in Phase 2
