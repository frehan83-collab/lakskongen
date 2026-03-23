# Tutorial System Quick Reference

## Common Operations

### Start Tutorial
```gdscript
TutorialManager.start_tutorial()
```

### Skip Tutorial
```gdscript
TutorialManager.skip_tutorial()
```

### Check if Active
```gdscript
if TutorialManager.is_active():
    # Tutorial is running
```

### Trigger Step Completion
```gdscript
# From UI
TutorialManager.on_action_completed("custom_trigger")

# From C# GameBridge
GameBridge.NotifyFishFed(penId)
```

### Get Current Step
```gdscript
var step = TutorialManager.get_current_step()
print(step["title"])  # Step title
print(step["text"])   # Step instructions
```

### Get Progress
```gdscript
var percent = TutorialManager.get_progress_percent()
print("Tutorial %d%% complete" % percent)
```

---

## Signals

### TutorialManager Signals

```gdscript
# Connect to signals
TutorialManager.step_changed.connect(_on_step_changed)
TutorialManager.step_completed.connect(_on_step_completed)
TutorialManager.tutorial_started.connect(_on_tutorial_started)
TutorialManager.tutorial_completed.connect(_on_tutorial_completed)
TutorialManager.tutorial_skipped.connect(_on_tutorial_skipped)

# Handlers
func _on_step_changed(step_id: int):
    print("Now on step: ", step_id)

func _on_step_completed(step_id: int):
    print("Completed step: ", step_id)

func _on_tutorial_started():
    print("Tutorial started")

func _on_tutorial_completed():
    print("Tutorial finished!")

func _on_tutorial_skipped():
    print("Tutorial skipped")
```

---

## GameBridge Integration (C#)

### Declare Signals
```csharp
[Signal]
public delegate void OnFishFedEventHandler(int penId);

[Signal]
public delegate void OnPenHarvestedEventHandler(int penId);
```

### Emit Signals
```csharp
public void NotifyFishFed(int penId)
{
    EmitSignal(SignalName.OnFishFed, penId);
}

public void NotifyPenHarvested(int penId)
{
    EmitSignal(SignalName.OnPenHarvested, penId);
}
```

### Call from Game Logic
```csharp
// In your feeding system
public void FeedPen(int penId)
{
    // ... feeding logic ...
    GameBridge.Instance.NotifyFishFed(penId);
}
```

---

## Highlight System

### Highlight Element
```gdscript
var my_button = get_node("MyButton")
TutorialHighlight.highlight_element(my_button, "circle")
```

### Hide Highlight
```gdscript
TutorialHighlight.hide_highlight()
```

### Highlight Types
- `"rectangle"` - Rectangular outline
- `"circle"` - Circular outline
- `"arrow"` - Arrow pointing to element
- `"none"` - No highlight

---

## File Locations

| File | Path |
|------|------|
| Step definitions | `res://data/tutorial_steps.json` |
| Progress save | `user://tutorial_progress.json` |
| Manager script | `res://scripts/tutorial/TutorialManager.gd` |
| Overlay script | `res://ui/TutorialOverlay.gd` |
| Highlight script | `res://scripts/tutorial/TutorialHighlight.gd` |

---

## Completion Triggers

| Trigger | Description | Example |
|---------|-------------|---------|
| `button_clicked` | Continue button | Manual advance |
| `fish_fed` | Fish feeding | `GameBridge.NotifyFishFed(1)` |
| `pen_harvested` | Harvest complete | `GameBridge.NotifyPenHarvested(1)` |
| `days_passed` | Time advanced | `GameBridge.NotifyDayAdvanced(30)` |
| `pen_clicked` | UI interaction | `TutorialManager.on_pen_clicked(1)` |
| `smolt_purchased` | Buy smolt | `TutorialManager.on_action_completed("smolt_purchased")` |
| `pen_placed` | Place new pen | `TutorialManager.on_action_completed("pen_placed")` |
| `delousing_completed` | Delousing done | `TutorialManager.on_action_completed("delousing_completed")` |

---

## UI Element Names

Ensure your UI elements have these names for highlights to work:

```gdscript
# Good
var dashboard = Panel.new()
dashboard.name = "Dashboard"

# Bad - won't be found
var dashboard = Panel.new()
dashboard.name = "dash_panel_001"
```

**Required Names:**
- `Dashboard` - Main dashboard
- `PenView` - Pen display
- `TimeControls` - Time buttons
- `FeedButton` - Feed action
- `WaterQuality` - Water stats
- `LiceCounter` - Lice display
- `DelousingPanel` - Delouse panel
- `GrowthChart` - Growth graph
- `HarvestButton` - Harvest action
- `SpotMarket` - Market panel
- `FinancePanel` - Finance display
- (See INTEGRATION.md for full list)

---

## Debug Commands

### Reset Progress
```gdscript
DirAccess.remove_absolute("user://tutorial_progress.json")
TutorialManager.current_step = 0
TutorialManager.completed_steps.clear()
```

### Jump to Step
```gdscript
TutorialManager.current_step = 10
TutorialManager.start_tutorial()
```

### Test Trigger
```gdscript
TutorialManager.check_completion("fish_fed", 1)
```

---

## Common Issues

### Highlight not showing?
```gdscript
# Debug: Print available nodes
for node in get_tree().root.get_children():
    print(node.name)

# Check if element exists
var element = get_node_or_null("UIElementName")
if element:
    print("Found: ", element.name)
else:
    print("Not found!")
```

### Step not advancing?
```gdscript
# Debug: Print current step
print("Current step: ", TutorialManager.current_step)
print("Expected trigger: ", TutorialManager.get_current_step()["completion_trigger"])

# Manually advance
TutorialManager.next_step()
```

### Signal not firing?
```csharp
// Debug in C#
GD.Print("About to emit signal");
EmitSignal(SignalName.OnFishFed, penId);
GD.Print("Signal emitted");
```

---

## Testing

### Run Test Suite
```bash
godot --script res://scripts/tutorial/TutorialTests.gd
```

### Manual Test
```gdscript
# In debug scene
func _ready():
    await get_tree().create_timer(1.0).timeout
    TutorialManager.start_tutorial()
```

---

## Performance

### Disable When Not Active
```gdscript
# Tutorial system automatically disables when:
# - State is INACTIVE
# - State is COMPLETE
# - State is SKIPPED

# Check before heavy operations:
if not TutorialManager.is_active():
    return  # Skip processing
```

---

## Accessibility

### Text Size
```gdscript
# Get overlay reference
var overlay = get_node("/root/TutorialOverlay")

# Scale text
overlay.set_text_size(1.5)  # 150% size
```

### Color Blind Mode
```gdscript
TutorialHighlight.set_colorblind_mode(true)
```

---

## Audio (Optional)

### Add Tutorial Sounds
```gdscript
# In TutorialManager._ready()
step_completed.connect(_play_complete_sound)

func _play_complete_sound(step_id):
    AudioController.play_sound("tutorial_complete")
```

---

## Need More Help?

- **Full docs:** `README.md`
- **Integration guide:** `INTEGRATION.md`
- **Test suite:** `TutorialTests.gd`
- **Step definitions:** `tutorial_steps.json`
