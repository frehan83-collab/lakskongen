# Godot Project — Lakskongen

This directory contains the **Godot 4.3+ presentation layer** for Lakskongen. The game logic is implemented in C# (see `../src/`) and integrated here for rendering, UI, and player interaction.

---

## 📂 Project Structure

```
godot/
├── project.godot          # Godot project configuration
├── scenes/                # All scene files (.tscn)
│   ├── MainMenu.tscn      # Main menu (entry point)
│   ├── GameWorld.tscn     # Main game scene
│   ├── ui/                # UI scenes (dashboard, dialogs)
│   └── ...
├── scripts/               # GDScript and C# scripts
│   ├── autoload/          # Autoloaded singletons
│   │   ├── EventBus.cs    # Global event system
│   │   ├── SaveLoadManager.cs
│   │   ├── AudioManager.cs
│   │   └── LocalizationManager.gd
│   ├── ui/                # UI controllers
│   ├── SimBridge.gd       # Bridge to C# simulation
│   └── ...
├── assets/                # Art assets
│   ├── sprites/
│   ├── textures/
│   └── models/
├── audio/                 # Sound effects and music
├── translations/          # Localization files (.po/.mo)
├── theme/                 # UI themes
└── addons/                # Godot plugins
```

---

## 🔧 Setup

### 1. Install Godot 4.3+ (with .NET support)
Download from [godotengine.org](https://godotengine.org/).

**Important:** Ensure you have the **.NET-enabled** build (look for "Mono" or ".NET" in the version).

### 2. Build C# Simulation Core
Before opening the Godot project, build the simulation library:

```bash
cd ../src
dotnet build LakskongenSim.sln
```

This creates `LakskongenSim.dll` referenced by Godot.

### 3. Open Project in Godot
1. Launch Godot Editor
2. Click **Import**
3. Navigate to this directory and select `project.godot`
4. Click **Import & Edit**

### 4. First Build in Godot
When the project opens:
1. **Editor → Manage C# Projects → Build**
2. Wait for build to complete (may take 1-2 minutes)
3. If errors occur, ensure .NET 8.0 SDK is installed

---

## ▶️ Running the Game

### From Godot Editor
Press **F5** (or click the Play button) to run.

**Entry point:** `scenes/MainMenu.tscn`

### Export Build
1. **Project → Export**
2. Configure export presets for Windows/macOS/Linux
3. **Export Project**

---

## 🧩 Autoloaded Singletons

These are globally accessible nodes configured in `project.godot`:

| Singleton | File | Purpose |
|-----------|------|---------|
| **GameBridge** | `scripts/autoload/GameBridge.cs` | Main interface to C# simulation engine |
| **EventBus** | `scripts/autoload/EventBus.cs` | Global event dispatcher |
| **SaveLoadManager** | `scripts/autoload/SaveLoadManager.cs` | Save/load game state |
| **AudioManager** | `scripts/autoload/AudioManager.cs` | Music and SFX control |
| **MultiplayerBridge** | `scripts/autoload/MultiplayerBridge.cs` | Multiplayer sync (planned) |
| **LocalizationManager** | `scripts/autoload/LocalizationManager.gd` | Language switching |
| **SimBridge** | `scripts/SimBridge.gd` | GDScript helper for C# sim |
| **ToastNotification** | `scripts/ui/ToastNotification.gd` | In-game notifications |

**Usage Example (GDScript):**
```gdscript
# Advance simulation one day
GameBridge.AdvanceDay()

# Play sound effect
AudioManager.PlaySFX("harvest_complete")

# Show notification
ToastNotification.Show("Fish harvested: 50,000 kg", ToastNotification.Type.SUCCESS)
```

---

## 🌐 Localization

Translations are stored in `translations/` using Godot's CSV format.

### Adding a New Language
1. Create `translations/<lang_code>.po` (e.g., `de.po` for German)
2. Add entries for all `tr("...")` strings
3. Compile: **Project → Project Settings → Localization → Import**
4. Add locale to `LocalizationManager.gd`

**Currently Supported:**
- Norwegian (Bokmål) — `nb_NO`
- English — `en`

---

## 🎨 Theming

UI theme is defined in `theme/default_theme.gd` and loaded via the **LakskongenTheme** autoload.

**Modifying theme:**
1. Edit `theme/default_theme.gd`
2. Reload scene or restart editor

---

## 🎵 Audio System

### Audio Files
- **Music:** `audio/music/` — `.ogg` format (compressed, looped)
- **SFX:** `audio/sfx/` — `.wav` format (short, uncompressed)

### AudioManager Usage
```gdscript
# Play background music
AudioManager.PlayMusic("ocean_ambience")

# Play sound effect
AudioManager.PlaySFX("button_click")

# Crossfade music
AudioManager.CrossfadeMusic("harvest_theme", 2.0) # 2-second fade
```

### Music Library
Managed by `MusicLibrary.gd` autoload — tracks registered in `audio/music/library.json`.

---

## 🧪 Testing in Godot

### Manual Testing
Run scenes individually from the editor:
1. Open scene (e.g., `scenes/ui/Dashboard.tscn`)
2. Press **F6** to run current scene
3. Verify UI and interactions

### Debug Features
- **F10:** Toggle debug overlay (FPS, memory, active fish count)
- **F11:** Reload current scene (hot reload)
- **Console:** Check **Output** tab for logs

---

## 🔗 C# Simulation Integration

The Godot layer communicates with the C# simulation via **GameBridge**.

### Example: Advancing Time
```csharp
// GameBridge.cs
public void AdvanceDay()
{
    _simulationEngine.AdvanceOneDay();
    EmitSignal(SignalName.DayAdvanced, _simulationEngine.CurrentDay);
}
```

```gdscript
# GDScript UI
func _on_next_day_pressed():
    GameBridge.AdvanceDay()
```

### Signal Flow
1. **C# emits signals** → `EventBus.cs` dispatches
2. **GDScript subscribes** → UI updates reactively

**Example:**
```csharp
// C# (SimulationEngine.cs)
EventBus.Instance.EmitSignal("HarvestCompleted", new HarvestData { ... });
```

```gdscript
# GDScript
func _ready():
    EventBus.HarvestCompleted.connect(_on_harvest_completed)

func _on_harvest_completed(data):
    update_ui(data)
```

---

## 📦 Dependencies

### Godot Plugins
- *(None currently — standard Godot 4.3 features only)*

### C# Libraries (referenced in `.csproj`)
- `LakskongenSim.dll` — Core simulation
- .NET 8.0 Base Class Libraries

---

## 🐛 Troubleshooting

### "C# project could not be created"
**Fix:** Install .NET 8.0 SDK and restart Godot.

### "Assembly not found: LakskongenSim"
**Fix:** Build C# project first:
```bash
cd ../src
dotnet build LakskongenSim.sln
```

### Audio not playing
**Fix:** Check `AudioManager.cs` file paths are correct (relative to `res://`).

### Localization not working
**Fix:**
1. **Project → Project Settings → Localization**
2. Ensure `.po` files are imported
3. Restart editor

---

## 📝 Code Style

### GDScript
- **Indentation:** Tabs (Godot default)
- **Naming:** `snake_case` for variables/functions, `PascalCase` for classes
- **Signals:** Declare at top of script
- **Type hints:** Use where possible (`var speed: float = 10.0`)

### C# in Godot
- Follow .NET conventions (see `../CONTRIBUTING.md`)
- Use `[Signal]` attribute for signals
- Call `EmitSignal()` with `SignalName.` constant

**Example:**
```csharp
[Signal]
public delegate void FishHarvestedEventHandler(int count);

// Later:
EmitSignal(SignalName.FishHarvested, 50000);
```

---

## 🚀 Performance Tips

### Profiling
Use Godot's built-in profiler:
1. **Debug → Profiler**
2. Run game (F5)
3. Check **Script** tab for hotspots

### Optimization
- **Minimize `_process()` calls** — Use signals for updates when possible
- **Object pooling** — Reuse nodes instead of `queue_free()` every frame
- **Batch rendering** — Use MultiMeshInstance for repeated objects
- **LOD** — Reduce detail for distant/off-screen elements

---

## 📚 Further Reading

- [Godot Documentation](https://docs.godotengine.org/en/stable/)
- [C# in Godot Guide](https://docs.godotengine.org/en/stable/getting_started/scripting/c_sharp/)
- [Godot Signals Tutorial](https://docs.godotengine.org/en/stable/getting_started/step_by_step/signals.html)

---

**Happy coding!** 🐟
