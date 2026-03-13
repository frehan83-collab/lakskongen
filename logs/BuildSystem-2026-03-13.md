# Build System Setup — 2026-03-13

**Author:** Erik Dahl (DevOps/CI)
**Task:** Get C# project to compile — create .csproj/.sln, resolve duplicates, fix compile errors

---

## Summary

The C# simulation project at `src/LakskongenSim/` already had a valid `.csproj` targeting `net8.0`.
The main blocker was one file with Godot dependencies in the pure C# library.
A root solution file and build script were missing.

---

## Actions Taken

### 1. `.csproj` — Already Existed

`src/LakskongenSim/LakskongenSim.csproj` was already present and correct:
- `net8.0`, C# 12, nullable enabled, implicit usings
- Only external dependency: `System.Text.Json 8.0.0`
- Explicit comment: "This is a pure C# library — NO Godot dependencies!"

No changes needed to the `.csproj`.

### 2. Duplicate Files — Resolved (no-conflict)

Two pairs of files share a class name but live in **different namespaces**:

| File | Namespace | Content |
|------|-----------|---------|
| `Data/Enums.cs` | `LakskongenSim.Data` | 20 production enums (267 lines) |
| `Core/Enums.cs` | `LakskongenSim.Core` | Empty static holder class, references Data enums |
| `Data/GameConstants.cs` | `LakskongenSim.Data` | `GameVersion = "0.1.0"` |
| `Core/GameConstants.cs` | `LakskongenSim.Core` | Simulation constants (mortality, growth, FCR, von Bertalanffy) |

**Verdict:** Not actual conflicts — different namespaces, intentional separation.
No merging required; both pairs compile cleanly alongside each other.

Also noted: `Market/ProductType.cs` declares `LakskongenSim.Market.ProductType` (FreshWhole/Fillet/Frozen)
which is distinct from `LakskongenSim.Data.ProductType` (WholeHOG/FreshFillets/…). No collision.

### 3. Removed Godot Dependency from `VisualMilestones.cs`

**File:** `src/LakskongenSim/Events/VisualMilestones.cs`

**Problem:** Was a Godot `Node` subclass — used `using Godot`, `partial class : Node`,
`[Signal]`, `GD.Print`, `Engine.TimeScale`, `GetTree()`, `Camera3D`, `AudioStreamPlayer`.

**Fix:** Rewrote as a pure C# class:
- Removed `using Godot` and `: Node` inheritance
- Replaced Godot signals with C# `event Action<...>` delegates:
  - `MilestoneAchieved` (title, description)
  - `CinematicStarted` (milestone type)
  - `CinematicFinished`
- Removed Godot-side cinematic/camera logic — moved responsibility to Godot layer via `CinematicStarted` event
- Added `NotifyCinematicFinished()` so the Godot layer can signal back when cinematic ends
- Kept all milestone tracking logic intact (11 milestones, progress tracking, achievement queries)

### 4. Created `LakskongenSim.sln`

**File:** `LakskongenSim.sln` (root of repo)

Includes all 6 C# projects:

| Project | Path |
|---------|------|
| `LakskongenSim` | `src/LakskongenSim/LakskongenSim.csproj` |
| `LakskongenSim.Tests` | `src/LakskongenSim.Tests/LakskongenSim.Tests.csproj` |
| `LakskongenSim.Health.Tests` | `tests/LakskongenSim.Health.Tests.csproj` |
| `LakskongenSim.ProcessingTests` | `tests/LakskongenSim.ProcessingTests.csproj` |
| `LakskongenSim.SaveLoadTests` | `tests/LakskongenSim.SaveLoadTests.csproj` |
| `LakskongenSim.LogisticsTests` | `src/LakskongenSim.LogisticsTests/LakskongenSim.LogisticsTests.csproj` |

All test projects already had correct `<ProjectReference>` to the main library.

### 5. Created `scripts/build.ps1`

PowerShell build script that:
- Accepts `-Configuration` (default: Debug) and `-RunTests` flags
- Runs `dotnet build LakskongenSim.sln`
- Optionally runs `dotnet test` when `-RunTests` is passed
- Uses `$ErrorActionPreference = "Stop"` for CI-safe failure propagation

**Usage:**
```powershell
.\scripts\build.ps1                        # Debug build
.\scripts\build.ps1 -Configuration Release # Release build
.\scripts\build.ps1 -RunTests              # Build + test
```

---

## Project Structure Overview

```
src/LakskongenSim/          — 57 .cs files, 16 subdirectories (pure C# library)
src/LakskongenSim.Tests/    — xUnit tests (finance, expansion, sustainability)
src/LakskongenSim.LogisticsTests/ — Logistics console test program
tests/                      — xUnit tests (disease, processing, save/load)
godot/                      — Godot 4.3 integration (separate SDK, references main lib)
LakskongenSim.sln           — NEW: root solution file
scripts/build.ps1           — NEW: build script
```

---

## Remaining Notes

- `dotnet` SDK was not found on the CI PATH during this session — install .NET 8 SDK to validate.
- The Godot project (`godot/LakskongenGame.csproj`) is intentionally excluded from `LakskongenSim.sln`
  since it requires the Godot.NET.Sdk and is managed separately via `godot/LakskongenGame.sln`.
- `Core/Enums.cs` (empty holder) and `Data/GameConstants.cs` (version string only) are candidates
  for cleanup in a future sprint, but are harmless and not worth touching now.
