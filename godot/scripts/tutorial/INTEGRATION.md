# Tutorial System Integration Guide

## Quick Start (5 minutes)

### 1. Add Autoloads

Open `project.godot` and add these autoloads:

```ini
[autoload]

# ... existing autoloads ...
TutorialManager="*res://scripts/tutorial/TutorialManager.gd"
TutorialHighlight="*res://scripts/tutorial/TutorialHighlight.gd"
```

**OR** via Godot Editor:
- Project → Project Settings → Autoload
- Add `res://scripts/tutorial/TutorialManager.gd` as `TutorialManager`
- Add `res://scripts/tutorial/TutorialHighlight.gd` as `TutorialHighlight`
- Check "Enable" for both

### 2. Add Overlay to Main Scene

In your main game scene (e.g., `Main.tscn`):

**Option A: Via Editor**
1. Open main scene
2. Right-click root node → Add Child Node
3. Search for "CanvasLayer"
4. Rename to "TutorialOverlay"
5. Attach script: `res://ui/TutorialOverlay.gd`

**Option B: Via Code**
```gdscript
# In Main.gd or game initialization
func _ready():
    var tutorial_overlay = preload("res://ui/TutorialOverlay.tscn").instantiate()
    add_child(tutorial_overlay)
```

### 3. Add GameBridge Signals (C#)

In your `GameBridge.cs` or equivalent C# bridge:

```csharp
using Godot;
using System;

public partial class GameBridge : Node
{
    // Tutorial-relevant signals
    [Signal]
    public delegate void OnFishFedEventHandler(int penId);
    
    [Signal]
    public delegate void OnPenHarvestedEventHandler(int penId);
    
    [Signal]
    public delegate void OnDayAdvancedEventHandler(int day);
    
    [Signal]
    public delegate void OnSmoltPurchasedEventHandler(int quantity);
    
    [Signal]
    public delegate void OnPenPlacedEventHandler(int penId);
    
    [Signal]
    public delegate void OnDelousingCompletedEventHandler(int penId);
    
    [Signal]
    public delegate void OnFishSoldEventHandler(int quantity);
    
    // Call these methods when game events occur
    public void NotifyFishFed(int penId)
    {
        EmitSignal(SignalName.OnFishFed, penId);
    }
    
    public void NotifyPenHarvested(int penId)
    {
        EmitSignal(SignalName.OnPenHarvested, penId);
    }
    
    public void NotifyDayAdvanced(int day)
    {
        EmitSignal(SignalName.OnDayAdvanced, day);
    }
    
    // Add more as needed...
}
```

### 4. Start Tutorial

In your new game / game start logic:

```gdscript
# In NewGameManager.gd or similar
func start_new_game():
    # ... game initialization ...
    
    # Start tutorial for new players
    if TutorialManager:
        TutorialManager.start_tutorial()
```

### 5. Test

Run the game. You should see the first tutorial step appear with:
- Dark panel at bottom of screen
- Title: "Velkommen til Lakskongen"
- Progress indicator: "Steg 1 / 23"
- "Neste" and "Hopp over tutorial" buttons

---

## Integration Points by Game System

### Feed System

When player feeds fish:
```csharp
// In FeedingSystem.cs or equivalent
public void FeedPen(int penId)
{
    // ... feeding logic ...
    
    // Notify tutorial
    GameBridge.Instance.NotifyFishFed(penId);
}
```

### Harvest System

When harvest completes:
```csharp
// In HarvestSystem.cs
public void CompleteHarvest(int penId)
{
    // ... harvest logic ...
    
    GameBridge.Instance.NotifyPenHarvested(penId);
}
```

### Time System

When day advances:
```csharp
// In TimeController.cs
public void AdvanceDay()
{
    currentDay++;
    // ... day logic ...
    
    GameBridge.Instance.NotifyDayAdvanced(currentDay);
}
```

### UI Interactions

For UI-triggered events:
```gdscript
# In PenUI.gd
func _on_pen_clicked():
    # ... pen interaction ...
    
    # Notify tutorial
    if TutorialManager and TutorialManager.is_active():
        TutorialManager.on_pen_clicked(pen_id)
```

---

## UI Element Naming Convention

To ensure highlights work, name your UI elements consistently:

### Dashboard
```gdscript
# Main dashboard panel
var dashboard = Panel.new()
dashboard.name = "Dashboard"
```

### Pen View
```gdscript
# Pen display panel
var pen_view = Control.new()
pen_view.name = "PenView"
```

### Time Controls
```gdscript
# Time control buttons container
var time_controls = HBoxContainer.new()
time_controls.name = "TimeControls"
```

### Feed Button
```gdscript
# Feed action button
var feed_button = Button.new()
feed_button.name = "FeedButton"
```

### Full Naming Map

| JSON "highlight" | UI Element | Location |
|-----------------|------------|----------|
| `Dashboard` | Main dashboard panel | Main UI |
| `PenView` | Pen grid/list container | Main UI |
| `TimeControls` | Time control buttons | Top bar |
| `FeedButton` | Feed action button | Pen detail or toolbar |
| `WaterQuality` | Water quality panel | Dashboard or pen detail |
| `LiceCounter` | Lice counter display | Pen detail |
| `DelousingPanel` | Delousing action panel | Pen detail |
| `GrowthChart` | Growth statistics chart | Dashboard or analytics |
| `HarvestButton` | Harvest action button | Pen detail |
| `SpotMarket` | Spot market panel | Market UI |
| `FinancePanel` | Finance/economics panel | Dashboard |
| `SmoltPurchase` | Smolt purchase panel | Market or shop |
| `PenPlacement` | Pen placement tool | Map or build mode |
| `PenOverview` | Multi-pen overview | Main UI |
| `DiseaseAlert` | Disease notification | Alert system |
| `TreatmentOptions` | Treatment choice panel | Disease response |
| `ResearchPanel` | Research/tech tree | Tech UI |
| `HatcheryPanel` | Hatchery management | Hatchery UI |
| `BreedingPanel` | Breeding program | Hatchery UI |
| `ContractPanel` | Contract negotiation | Market UI |
| `ProcessingPanel` | Processing plant | Processing UI |
| `MapOverview` | World/facility map | Map UI |

---

## Optional: Settings Menu Integration

Add tutorial restart option:

```gdscript
# In SettingsMenu.gd
func _ready():
    var restart_tutorial_button = Button.new()
    restart_tutorial_button.text = "Start tutorial på nytt"
    restart_tutorial_button.pressed.connect(_on_restart_tutorial)
    settings_container.add_child(restart_tutorial_button)

func _on_restart_tutorial():
    # Clear progress
    if FileAccess.file_exists("user://tutorial_progress.json"):
        DirAccess.remove_absolute("user://tutorial_progress.json")
    
    # Reset and start
    if TutorialManager:
        TutorialManager.current_step = 0
        TutorialManager.completed_steps.clear()
        TutorialManager.current_state = TutorialManager.State.INACTIVE
        TutorialManager.start_tutorial()
    
    # Close settings
    get_tree().change_scene_to_file("res://scenes/Main.tscn")
```

---

## Testing Integration

### Test Signals Flow

```gdscript
# Create test script: res://test_tutorial.gd
extends Node

func _ready():
    # Wait for autoloads
    await get_tree().create_timer(0.5).timeout
    
    # Test signal
    if GameBridge:
        print("Testing GameBridge.OnFishFed signal...")
        GameBridge.NotifyFishFed(1)
        
        await get_tree().create_timer(0.5).timeout
        
        print("Testing GameBridge.OnPenHarvested signal...")
        GameBridge.NotifyPenHarvested(1)
```

### Test Step Progression

```gdscript
# In debug console or test script
func test_tutorial_flow():
    TutorialManager.start_tutorial()
    
    # Advance through first 5 steps
    for i in range(5):
        await get_tree().create_timer(2.0).timeout
        TutorialManager.next_step()
```

### Test Highlight System

```gdscript
func test_highlights():
    # Create dummy button
    var test_button = Button.new()
    test_button.name = "TestButton"
    test_button.position = Vector2(400, 300)
    test_button.size = Vector2(200, 50)
    add_child(test_button)
    
    # Test rectangle highlight
    await get_tree().create_timer(1.0).timeout
    TutorialHighlight.highlight_element(test_button, "rectangle")
    
    # Test circle highlight
    await get_tree().create_timer(3.0).timeout
    TutorialHighlight.highlight_element(test_button, "circle")
    
    # Clear
    await get_tree().create_timer(3.0).timeout
    TutorialHighlight.hide_highlight()
```

---

## Troubleshooting

### "TutorialManager not found"
- Check autoload is enabled in project settings
- Restart Godot editor
- Verify script path is correct

### "GameBridge signals not working"
- Ensure GameBridge is marked as autoload
- Check signal names match exactly (case-sensitive)
- Verify C# signals are declared with `[Signal]` attribute

### "Highlight not showing"
- Check UI element name matches JSON exactly
- Ensure element is visible and has non-zero size
- Verify element is type Control (not Node2D)
- Add debug print in `_find_ui_element()`

### "Tutorial not starting"
- Check `tutorial_steps.json` is in `res://data/`
- Verify JSON is valid (use JSONLint.com)
- Check console for error messages

### "Steps not advancing"
- Verify completion trigger matches action
- Check GameBridge signals are emitting
- Add debug print in signal handlers

---

## Performance Impact

Minimal performance impact when designed correctly:

- **Memory**: ~50 KB (JSON data + scripts)
- **CPU**: Negligible when inactive, <1% when active
- **GPU**: Minimal (simple 2D overlay drawing)

Tips for optimization:
1. Hide overlay when not active
2. Disable highlight processing when not highlighting
3. Cache UI element references
4. Use `queue_redraw()` instead of continuous redraw

---

## Next Steps

After integration:
1. Test all 23 steps manually
2. Verify highlights on actual UI elements
3. Add custom completion triggers for your game
4. Adjust tutorial text based on playtest feedback
5. Add audio cues (see README.md)
6. Implement accessibility features

---

## Support

If you encounter issues:
1. Check console for error messages
2. Verify all file paths are correct
3. Test signals in isolation
4. Review README.md for detailed docs
5. Check that Godot version is 4.x+

For questions about extending the system, see `README.md` section "Adding New Tutorial Steps".
