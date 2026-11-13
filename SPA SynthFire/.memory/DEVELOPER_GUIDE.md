# SPA SynthFire — Quick Developer Guide & Guidelines

## 1. Build and Test Quick Reference

```powershell
# Run all headless tests (instant feedback, does not require Revit running)
dotnet test

# Build the entire solution
dotnet build

# Build and deploy add-in manifests to Revit 2026 / 2025 addin directories
dotnet build -p:DeployToRevit=true -c Revit2026
```

---

## 2. Key Architecture Principles

1. **Revit-Independence in Core and Placement**:
   - Never import `Autodesk.Revit.*` in `SPA.SynthFire.Core` or `SPA.SynthFire.Placement`.
   - Keep all math, geometry, and placement logic purely headless so it can be 100% covered by fast unit tests.
   - Any Revit API interaction must reside strictly in `SPA.SynthFire.Revit`.

2. **MVVM Decoupling in UI**:
   - `SPA.SynthFire.Presentation` contains pure ViewModels and commands without UI framework bloat where possible.
   - `SPA.SynthFire.UI` contains XAML views and HelixToolkit controls.

3. **Rule Conservatism & Code Safety**:
   - User design criteria may **only tighten** code rules (e.g. smaller spacing, smaller area), never relax them (`DesignCriteriaResolver`).
   - Unapproved rules carry `IsProvisional = true` until validated by a Fire Protection Engineer (FPE).

4. **Multi-Level Elevation Handling**:
   - Always route non-face-hosted device placement through `PlacementLevels.Apply` to ensure proper level parameter and elevation offset are committed in Revit.

---

## 3. High-Value Modification Hotspots

- **Adjusting Sprinkler Grids or Obstacle Avoidance**: `src/SPA.SynthFire.Placement/CeilingGridLayout.cs`
- **Adjusting Sidewall Spacing**: `src/SPA.SynthFire.Placement/SidewallLayout.cs`
- **Adjusting NFPA / AS Standards**: `src/SPA.SynthFire.Core/Standards/`
- **Adjusting Family Extraction & Hosting**: `src/SPA.SynthFire.Revit/HostFinder.cs` and `SprinklerPlacer.cs`
- **Dockable Panel / UI Layout**: `src/SPA.SynthFire.UI/Views/` and `src/SPA.SynthFire.Presentation/ViewModels/`
