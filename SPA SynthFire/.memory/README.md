# SPA SynthFire — Project Memory Index

Welcome! This directory contains token-efficient, comprehensive architectural and domain memory for the **SPA SynthFire** project. Any LLM or developer working on this codebase should consult these files to maintain consistency and avoid costly context re-indexing.

---

## 🗂️ Memory File Index

| File | Purpose | Key Topics Covered |
|------|---------|---------------------|
| [**`ARCHITECTURE.md`**](./ARCHITECTURE.md) | High-level system design & layout | Target runtimes (.NET 8, Revit 2026/2025), project dependencies, layer responsibilities, test suite setup. |
| [**`DECISIONS.md`**](./DECISIONS.md) | Architectural Decision Records (ADRs) | Decisions 001–009: Clean-room setup, Revit decoupling, unit normalization, unified grid engine, provisional rules, obstacle avoidance, redesign tracking, elevation handling. |
| [**`PLACEMENT_LOGIC.md`**](./PLACEMENT_LOGIC.md) | Math, placement & coordination logic | Formulas for grid calculation, refinement loop, tile snapping, 8-way nudge obstacle avoidance, sidewall layouts, NFPA 72 detection rules. |
| [**`GLOSSARY.md`**](./GLOSSARY.md) | Domain glossary & terminology | Definitions of core data models, hazard classes, NFPA 13/72 & AS 2118 thresholds, Revit parameters. |
| [**`DEVELOPER_GUIDE.md`**](./DEVELOPER_GUIDE.md) | Developer quick reference & CLI | `dotnet test` commands, build & deploy instructions, architectural constraints, modification hotspots. |

---

## ⚡ Quick Cheat Sheet

- **Run all headless tests**:
  ```powershell
  dotnet test
  ```
- **Primary Placement Engine**: `src/SPA.SynthFire.Placement/CeilingGridLayout.cs`
- **Revit Family Placement**: `src/SPA.SynthFire.Revit/SprinklerPlacer.cs` & `DetectorPlacer.cs`
- **Unit Rule**: All coordinates and calculations use **decimal feet (`ft`)** and **square feet (`sq ft`)**.
- **Idempotence**: All placed instances are tagged with comment `"SPA SynthFire"`.
