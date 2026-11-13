# SPA SynthFire — Roadmap

SPA SynthFire is a **standalone, built-from-scratch** Revit add-in that replicates the
capabilities of [Tandm.ai](https://tandm.ai/au/) for fire-protection design. It is a
**production** tool. It does **not** reuse code from the sibling FireProtectionSystem project.

## Feature parity target (from Tandm)

| # | Capability | Notes |
|---|-----------|-------|
| 1 | Sprinkler placement ("wet fire") | Auto, code-compliant layouts in the Revit model |
| 2 | Detection placement ("dry fire") | Smoke/heat detectors + notification devices |
| 3 | Ceiling-tile positioning | Snap heads to ceiling grid / tile centers |
| 4 | Automated clash avoidance | Detect + shift/flag against other model elements |
| 5 | Coordination | Cross-discipline aware placement |
| 6 | One-button redesign | Re-run placement after model changes |
| 7 | Configurable code compliance | AS2118 + NFPA 13 (BS EN 12845 later) + user criteria |
| 8 | Estimator tool | Quantity takeoff / BOM for tendering |

Runtime target: **Revit 2026** (.NET 8, Windows).

## Solution layout (planned)

`SPA.SynthFire.sln`

- `SPA.SynthFire.Core` — domain models, geometry, standards/rules engine (Revit-independent)
- `SPA.SynthFire.Catalog` — Excel-driven device catalog load/validate
- `SPA.SynthFire.Placement` — solvers (sprinkler + detection), clash avoidance, ceiling snap
- `SPA.SynthFire.Estimator` — quantities / BOM
- `SPA.SynthFire.Revit` — add-in: ribbon, commands, model read/write, live clash queries
- `SPA.SynthFire.UI` — WPF panels/dialogs (criteria, report, estimator)
- `SPA.SynthFire.Tests` — headless test harness (no Revit needed)

## Phases

- **Phase 0 — Foundations:** solution scaffold, Revit 2026 refs, `.addin` generation, stub
  ribbon tab that loads, headless test harness. _Exit: builds; add-in loads; ribbon shows._
- **Phase 1 — Standards + catalog:** configurable rules engine (AS2118 + NFPA 13 + user
  criteria overlay, provisional-until-approved gating); Excel catalog loader/validator +
  template. _Exit: unit tests green._
- **Phase 2 — Sprinkler placement e2e:** coverage/spacing solver honoring active standard,
  ceiling-tile snap, residual-gap flags; Revit read rooms/ceilings + place families +
  report UI + one-button re-run. _Exit: harness tests + manual Revit run._
- **Phase 3 — Clash avoidance + coordination:** query nearby elements, shift/flag, redesign.
  _Exit: seeded-clash model resolves/flags all._
- **Phase 4 — Detection placement:** smoke/heat + notification solvers on shared framework.
  _Exit: harness + Revit run._
- **Phase 5 — Estimator:** takeoff/BOM export (schedule + CSV/Excel). _Exit: counts match._
- **Phase 6 — UI polish + hardening:** dockable panels, criteria editor, batch redesign, docs.

## Verification strategy

- Headless-first: solvers/rules/catalog validated in `SPA.SynthFire.Tests` with no Revit.
- Revit integration: each placement phase manually run on a sample `.rvt` in Revit 2026.
- Standards toggle: same room run under AS2118 vs NFPA 13 to prove configurability.
- All fire-protection code values start **provisional/unverified** until FPE-reviewed.

See `docs/TODO.md` for the live task tracker.
