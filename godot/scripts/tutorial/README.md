# Tutorial System Documentation

## Overview

The Lakskongen tutorial system is a 23-step interactive onboarding experience that guides new players through the first 2 hours of gameplay. It consists of three main components:

1. **TutorialManager** - State machine and progression controller
2. **TutorialOverlay** - UI display and interaction handler
3. **TutorialHighlight** - Visual highlighting system for UI elements

## Architecture

```
TutorialManager (Singleton)
├── Loads tutorial_steps.json
├── Manages state machine (INACTIVE → ACTIVE → COMPLETE)
├── Tracks progress and saves to user://tutorial_progress.json
├── Emits signals for step changes
└── Listens to GameBridge signals for completion triggers

TutorialOverlay (CanvasLayer, layer 99)
├── Displays step title, text, progress
├── Manages Continue/Skip buttons
└── Coordinates with TutorialHighlight

TutorialHighlight (CanvasLayer, layer 100)
├── Draws backdrop (dims screen)
├── Highlights target UI elements
├── Provides pulsing animation
└── Supports rectangle, circle, arrow types
```

## Setup

### 1. Add to Autoload (project.godot)

```ini
[autoload]
TutorialManager="*res://scripts/tutorial/TutorialManager.gd"
TutorialHighlight="*res://scripts/tutorial/TutorialHighlight.gd"
```

### 2. Add TutorialOverlay to Main Scene

In your main game scene:
```gdscript
# Add as child of root or main UI
var tutorial_overlay = preload("res://ui/TutorialOverlay.tscn").instantiate()
add_child(tutorial_overlay)
```

### 3. Start Tutorial

```gdscript
# In game start/new game logic
if TutorialManager:
    TutorialManager.start_tutorial()
```

## Adding New Tutorial Steps

### Step 1: Define Step in JSON

Edit `godot/data/tutorial_steps.json`:

```json
{
  "id": 24,
  "phase": "advanced",
  "title": "New Feature Title",
  "text": "Instruction text for the player (2-3 sentences).",
  "highlight": "UIElementName",
  "highlight_type": "rectangle",
  "action_required": "custom_action",
  "completion_trigger": "custom_trigger",
  "next_step": 25
}
```

**Fields:**
- `id`: Unique step number (sequential)
- `phase`: Group name (introduction, first_cycle, expansion, advanced)
- `title`: Step heading shown to player
- `text`: Instructional text (keep concise, 2-3 sentences)
- `highlight`: Name of UI element to highlight (or null for no highlight)
- `highlight_type`: "rectangle", "circle", "arrow", or "none"
- `action_required`: Type of action player must perform (see below)
- `completion_trigger`: Signal/event that completes this step (see below)
- `next_step`: ID of next step (or null if final step)

### Step 2: Implement Completion Logic

#### Built-in Completion Triggers

These are already implemented in TutorialManager:

- `button_clicked` - User clicks "Continue" button
- `fish_fed` - GameBridge.OnFishFed signal
- `pen_harvested` - GameBridge.OnPenHarvested signal
- `days_passed` - GameBridge.OnDayAdvanced signal
- `pen_clicked` - User clicks a pen in UI

#### Adding Custom Triggers

**Option A: Add to GameBridge (C#)**

```csharp
// In GameBridge.cs
public event Action<int> OnCustomAction;

// Call when action happens
public void TriggerCustomAction(int value) {
    OnCustomAction?.Invoke(value);
}
```

Then connect in TutorialManager.gd:
```gdscript
func _connect_game_signals():
    # ... existing connections ...
    if bridge.has_signal("OnCustomAction"):
        bridge.OnCustomAction.connect(_on_custom_action)

func _on_custom_action(value: int):
    check_completion("custom_trigger", value)
```

**Option B: Call from UI**

In your UI script:
```gdscript
func _on_some_button_pressed():
    # ... do action ...
    
    # Notify tutorial
    if TutorialManager and TutorialManager.is_active():
        TutorialManager.on_action_completed("custom_trigger")
```

### Step 3: Ensure UI Element is Findable

The highlight system searches for UI elements by name. Make sure your UI node has the correct name:

```gdscript
# In your UI scene/script
var my_button = Button.new()
my_button.name = "UIElementName"  # Must match "highlight" field in JSON
```

For nested elements, you can use the full path:
```json
"highlight": "MainUI/LeftPanel/UIElementName"
```

## Completion Trigger Reference

| Trigger | Description | Data |
|---------|-------------|------|
| `button_clicked` | Continue button pressed | None |
| `fish_fed` | Fish feeding completed | pen_id |
| `pen_harvested` | Pen harvest completed | pen_id |
| `days_passed` | Game time advanced | day_number |
| `pen_clicked` | User clicked a pen | pen_id |
| `smolt_purchased` | Bought smolt | quantity |
| `pen_placed` | New pen placed | pen_id |
| `delousing_completed` | Delousing finished | pen_id |
| `fish_sold` | Fish sold on market | quantity |
| `treatment_chosen` | Disease treatment selected | treatment_type |
| `research_started` | Research project started | research_id |
| `hatchery_built` | Hatchery constructed | building_id |
| `breeding_started` | Breeding program initiated | program_id |
| `contract_signed` | Contract negotiated | contract_id |
| `processing_built` | Processing plant built | building_id |

## Testing Tutorial Steps

### Manual Testing

```gdscript
# Start at specific step
TutorialManager.current_step = 10
TutorialManager.start_tutorial()

# Reset progress
DirAccess.remove_absolute("user://tutorial_progress.json")

# Skip to end
TutorialManager.skip_tutorial()
```

### Test Checklist

- [ ] Step text is clear and concise
- [ ] Highlight appears on correct UI element
- [ ] Highlight type is appropriate (circle for buttons, rectangle for panels)
- [ ] Completion trigger works reliably
- [ ] Progress saves correctly
- [ ] Can skip at this step
- [ ] No visual glitches or overlaps
- [ ] Works at 1080p, 1440p, 4K

## Audio Integration

### Adding Tutorial Sounds

In `AudioController.gd`:
```gdscript
# Preload sounds
const STEP_COMPLETE_SOUND = preload("res://assets/audio/sfx/tutorial_complete.ogg")
const STEP_ADVANCE_SOUND = preload("res://assets/audio/sfx/tutorial_advance.ogg")

func play_tutorial_complete():
    play_sound(STEP_COMPLETE_SOUND)

func play_tutorial_advance():
    play_sound(STEP_ADVANCE_SOUND)
```

Connect in TutorialManager:
```gdscript
func _ready():
    # ... existing code ...
    step_completed.connect(_on_step_completed_audio)
    step_changed.connect(_on_step_changed_audio)

func _on_step_completed_audio(step_id):
    if AudioController:
        AudioController.play_tutorial_complete()

func _on_step_changed_audio(step_id):
    if AudioController:
        AudioController.play_tutorial_advance()
```

## Accessibility

### Text Size Adjustment

```gdscript
# In TutorialOverlay.gd
func set_text_size(scale: float):
    title_label.add_theme_font_size_override("font_size", int(24 * scale))
    text_label.add_theme_font_size_override("normal_font_size", int(16 * scale))
```

### Color-Blind Mode

```gdscript
# In TutorialHighlight.gd
var colorblind_mode = false
var colorblind_highlight_color = Color("#FFA500")  # Orange

func set_colorblind_mode(enabled: bool):
    colorblind_mode = enabled
    if enabled:
        # Use color-blind friendly palette
        pass
```

### Re-triggering Tutorial

```gdscript
# In settings menu
func restart_tutorial():
    DirAccess.remove_absolute("user://tutorial_progress.json")
    TutorialManager.current_step = 0
    TutorialManager.completed_steps.clear()
    TutorialManager.start_tutorial()
```

## Troubleshooting

### Highlight not appearing
- Check that UI element name matches JSON "highlight" field exactly
- Ensure element is visible and has non-zero size
- Verify element is Control type (not Node2D)

### Step not advancing
- Check completion trigger matches JSON "completion_trigger"
- Verify GameBridge signal is connected
- Add debug print in signal handler

### Progress not saving
- Check file permissions for user:// directory
- Verify JSON serialization (no cyclic references)
- Test with `FileAccess.file_exists(PROGRESS_FILE)`

### Softlock (can't advance)
- Always provide skip button
- Ensure `can_skip = true` in TutorialManager
- Test all completion paths

## Performance Tips

1. **Lazy load UI references** - Don't search for all UI elements upfront
2. **Cache found nodes** - Store references when first found
3. **Disable when inactive** - Hide overlay and highlight when not in use
4. **Limit highlight redraws** - Use `queue_redraw()` not `_process()` every frame
5. **Compress JSON** - Remove whitespace in production build

## Future Enhancements

- [ ] Voice narration support (ElevenLabs integration)
- [ ] Animated transitions between steps
- [ ] Interactive mini-games within tutorial
- [ ] A/B testing different tutorial flows
- [ ] Analytics: track completion rates per step
- [ ] Contextual hints (show again if player stuck)
- [ ] Adaptive difficulty (skip steps if player shows mastery)
