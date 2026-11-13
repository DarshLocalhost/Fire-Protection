# SPA SynthFire — Build, Run & Verify

## Prerequisites
- .NET SDK 8+ (10 is installed here and builds the net8.0 targets).
- Revit 2025 and/or 2026 (APIs referenced from `C:\Program Files\Autodesk\Revit <ver>`).
- Visual Studio 2022 17.8+ or `dotnet` CLI.

## Build configurations

| Configuration | Revit API | Auto-deploys to |
|---------------|-----------|-----------------|
| `Debug` / `Release` | Revit 2026 (default) | nothing (opt-in via `-p:DeployToRevit=true`) |
| `Revit2025` | Revit 2025 | `%APPDATA%\Autodesk\Revit\Addins\2025` |
| `Revit2026` | Revit 2026 | `%APPDATA%\Autodesk\Revit\Addins\2026` |

Building a `Revit2025` / `Revit2026` configuration **builds and deploys** the add-in
to that Revit version in one step. Use `Debug` for day-to-day compiling and tests.

## 1. Build

### Visual Studio
If VS shows *"unknown project configuration mappings"*, close and reopen the solution
once (the platform/config list is in `Directory.Build.props`).
- To just compile + run tests: **Debug | Any CPU**, Build Solution (Ctrl+Shift+B).
- To build **and** deploy to Revit: pick **Revit2025 | x64** or **Revit2026 | x64**, then Build.

### CLI
```bash
# compile + test (no deploy)
dotnet build "SPA.SynthFire.slnx" -c Debug

# build + deploy to Revit 2025
dotnet build "src/SPA.SynthFire.Revit/SPA.SynthFire.Revit.csproj" -c Revit2025 -p:Platform=x64

# build + deploy to Revit 2026
dotnet build "src/SPA.SynthFire.Revit/SPA.SynthFire.Revit.csproj" -c Revit2026 -p:Platform=x64
```

> A Revit add-in runs fine as Any CPU (Revit is an x64 host). Building x64 only
> silences the reference-architecture warning.

## 2. Run the tests (headless verification)

### Visual Studio
Test → Test Explorer → Run All. Expect all green.

### CLI
```bash
dotnet test "SPA.SynthFire.slnx" -c Debug
```
Currently 17 tests (geometry, NFPA 13 / AS 2118 rule values, unit conversion,
user-criteria clamping, catalog round-trip + validation).

## 3. Run & verify inside Revit (the real "run")

The add-in loads into Revit; there is no standalone app to F5. Deploy, then launch Revit.

### Deploy (build + copy DLLs + write the .addin manifest)
Just build the matching Revit configuration (see the table above):
```bash
dotnet build "src/SPA.SynthFire.Revit/SPA.SynthFire.Revit.csproj" -c Revit2025 -p:Platform=x64
```
(or `-c Revit2026`). This writes:
- `%APPDATA%\Autodesk\Revit\Addins\<ver>\SPA.SynthFire.addin`
- `%APPDATA%\Autodesk\Revit\Addins\<ver>\SPA.SynthFire\*.dll`

### Verify
1. Launch Revit `<ver>` and open any project (or a new one).
2. If Revit warns about an add-in from an unknown publisher, choose **Always Load**.
3. Look for the **SPA SynthFire** ribbon tab → **Fire Protection** panel → **SynthFire** button.
4. Click it — a dialog confirming the add-in loaded should appear.

After any code change, re-run the deploy command and restart Revit.

### Optional: debug with breakpoints in Revit
Set `SPA.SynthFire.Revit` as the startup project → project Debug settings →
launch executable `C:\Program Files\Autodesk\Revit <ver>\Revit.exe`. F5 starts
Revit with the debugger attached so breakpoints in the add-in are hit.

## Troubleshooting
- **"0 succeeded, N skipped" / config mapping warning:** reopen the solution so the
  `Any CPU` platform from `Directory.Build.props` is picked up.
- **Ribbon tab missing in Revit:** confirm the `.addin` file exists in the addins
  folder above and that you launched the matching Revit version; check
  Revit's *Add-Ins Loaded* / journal for load errors.
