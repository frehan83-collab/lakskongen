# Building from Source

> Complete guide to building Lakskongen from source on Windows, macOS, and Linux.

## Prerequisites

| Tool | Version | Download |
|------|---------|----------|
| .NET SDK | 8.0+ | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) |
| Godot | 4.3+ (Mono/.NET) | [godotengine.org](https://godotengine.org/) |
| Git | 2.30+ | [git-scm.com](https://git-scm.com/) |
| IDE (optional) | VS 2022 / Rider / VS Code | - |

### Verify Installation

```bash
dotnet --version   # Should show 8.0.x
git --version      # Should show 2.30+
```

## Step 1: Clone the Repository

```bash
git clone https://github.com/yourusername/lakskongen.git
cd lakskongen
```

## Step 2: Build the C# Simulation Library

```bash
cd src
dotnet restore           # Download NuGet packages
dotnet build             # Compile all projects
```

### Expected Output
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Solution Structure

```
LakskongenSim.sln
├── LakskongenSim/               # Core simulation library
├── LakskongenSim.Tests/         # Unit tests (xUnit)
├── LakskongenSim.SaveLoadTests/ # Save/load integration tests
├── LakskongenSim.LogisticsTests/# Logistics system tests
└── PerformanceBenchmark/        # Performance profiling tool
```

## Step 3: Run Tests

```bash
cd LakskongenSim.Tests
dotnet test --verbosity normal
```

See [Running Tests](running-tests.md) for full test suite instructions.

## Step 4: Open in Godot

1. Launch **Godot 4.3+** (Mono/.NET edition)
2. Click **Import** → Browse to `godot/project.godot`
3. Click **Import & Edit**
4. Wait for Godot to scan assets and compile C# bindings
5. Click **Build** (`Ctrl+Shift+B`) to compile the C# Godot scripts

### Godot Project Structure

```
godot/
├── project.godot          # Main project file
├── LakskongenGame.csproj  # C# project (references LakskongenSim)
├── LakskongenGame.sln     # Solution for Godot C# scripts
├── scripts/
│   ├── autoload/          # Singleton autoloads
│   │   ├── GameBridge.cs  # Main C# → Godot bridge
│   │   ├── EventBus.cs    # Signal dispatcher
│   │   └── SaveLoadManager.cs
│   ├── SimBridge.gd       # GDScript wrapper for GameBridge
│   └── ui/                # UI controllers (GDScript)
├── scenes/                # .tscn scene files
├── assets/                # Art, audio, sprites
├── translations/          # Localization CSV files
└── theme/                 # UI theme resources
```

## Step 5: Run the Game

Press **F5** in Godot Editor, or:

```bash
# From command line (Windows)
"C:\path\to\Godot_v4.3-stable_mono_win64.exe" --path godot/

# From command line (macOS/Linux)
godot --path godot/
```

## Step 6: Build for Distribution

### Windows Export
1. In Godot: **Project → Export → Add → Windows Desktop**
2. Configure export template
3. Click **Export Project**

### Using the Build Script
```powershell
# From project root
.\scripts\build.ps1
```

## Common Issues

### "LakskongenSim not found" in Godot
**Fix:** Ensure `dotnet build` succeeded in `src/`. The Godot project references the simulation DLL.

### C# build errors in Godot
**Fix:** Close Godot → run `dotnet build` in `src/` → reopen Godot → Build again.

### Missing NuGet packages
**Fix:** Run `dotnet restore` in `src/` directory.

### Godot can't find .NET SDK
**Fix:** Ensure `DOTNET_ROOT` environment variable points to your .NET 8 installation.

## IDE Setup

### Visual Studio 2022
1. Open `src/LakskongenSim.sln` for simulation development
2. Open `godot/LakskongenGame.sln` for Godot C# scripts
3. Use **Test Explorer** for running xUnit tests

### JetBrains Rider
1. Open `src/LakskongenSim.sln`
2. Rider auto-detects xUnit tests
3. For Godot: Install the **Godot Support** plugin

### VS Code
1. Install **C# Dev Kit** extension
2. Open project root folder
3. Use `dotnet test` in terminal for tests

## Next Steps

- 🧪 [Running Tests](running-tests.md) — Full test guide
- 🏗️ [Architecture](../Architecture.md) — Understand the codebase
- 🤝 [Contributing](../../CONTRIBUTING.md) — Contribution workflow
