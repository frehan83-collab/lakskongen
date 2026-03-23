# Tutorial System Architecture

## Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                         GAME LAYER                              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐         │
│  │   C# Game    │  │  UI Elements │  │  Time System │         │
│  │   Systems    │  │   (Godot)    │  │   (C#)       │         │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘         │
│         │                  │                  │                  │
└─────────┼──────────────────┼──────────────────┼─────────────────┘
          │                  │                  │
          │ Signals          │ Name lookup      │ Time events
          ▼                  ▼                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                    GAMEBRIDGE LAYER (C#)                        │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │  GameBridge (Autoload)                                     │ │
│  │  ├─ OnFishFed(penId)           [Signal]                   │ │
│  │  ├─ OnPenHarvested(penId)       [Signal]                   │ │
│  │  ├─ OnDayAdvanced(day)          [Signal]                   │ │
│  │  ├─ OnSmoltPurchased(quantity)  [Signal]                   │ │
│  │  └─ ... (more game event signals)                          │ │
│  └───────────────────────┬───────────────────────────────────┘ │
└──────────────────────────┼─────────────────────────────────────┘
                           │
                           │ Signal emission
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│                   TUTORIAL SYSTEM LAYER                         │
│                                                                  │
│  ┌────────────────────────────────────────────────────────┐    │
│  │  TutorialManager (Autoload, GDScript)                  │    │
│  │  ┌──────────────────────────────────────────────────┐  │    │
│  │  │ State Machine                                      │  │    │
│  │  │  INACTIVE → ACTIVE → PAUSED → COMPLETE/SKIPPED   │  │    │
│  │  └──────────────────────────────────────────────────┘  │    │
│  │  ┌──────────────────────────────────────────────────┐  │    │
│  │  │ Step Progression                                   │  │    │
│  │  │  - Load tutorial_steps.json (23 steps)            │  │    │
│  │  │  - Track current_step                              │  │    │
│  │  │  - Validate completion triggers                    │  │    │
│  │  │  - Advance to next_step                            │  │    │
│  │  └──────────────────────────────────────────────────┘  │    │
│  │  ┌──────────────────────────────────────────────────┐  │    │
│  │  │ Progress Tracking                                  │  │    │
│  │  │  - Save to user://tutorial_progress.json          │  │    │
│  │  │  - Load on startup                                 │  │    │
│  │  │  - Resume from last step                           │  │    │
│  │  └──────────────────────────────────────────────────┘  │    │
│  │  ┌──────────────────────────────────────────────────┐  │    │
│  │  │ Signal Handlers                                    │  │    │
│  │  │  - Connect to GameBridge signals                   │  │    │
│  │  │  - Match triggers to step completion               │  │    │
│  │  │  - Emit tutorial events                            │  │    │
│  │  └──────────────────────────────────────────────────┘  │    │
│  └────────┬────────────────────────────┬──────────────────┘    │
│           │                             │                        │
│           │ step_changed signal         │ get_current_step()    │
│           ▼                             ▼                        │
│  ┌────────────────────────────────────────────────────────┐    │
│  │  TutorialOverlay (CanvasLayer 99, GDScript)            │    │
│  │  ┌──────────────────────────────────────────────────┐  │    │
│  │  │ UI Components                                      │  │    │
│  │  │  - Title label (step title)                       │  │    │
│  │  │  - Text label (instructions)                      │  │    │
│  │  │  - Progress label (Step X/23)                     │  │    │
│  │  │  - Continue button                                 │  │    │
│  │  │  - Skip button                                     │  │    │
│  │  └──────────────────────────────────────────────────┘  │    │
│  │  ┌──────────────────────────────────────────────────┐  │    │
│  │  │ Element Discovery                                  │  │    │
│  │  │  - Search scene tree for UI elements              │  │    │
│  │  │  - Match by name (from JSON "highlight" field)    │  │    │
│  │  │  - Pass to TutorialHighlight                       │  │    │
│  │  └──────────────────────────────────────────────────┘  │    │
│  └────────┬───────────────────────────────────────────────┘    │
│           │                                                      │
│           │ highlight_element(node, type)                       │
│           ▼                                                      │
│  ┌────────────────────────────────────────────────────────┐    │
│  │  TutorialHighlight (CanvasLayer 100, GDScript)         │    │
│  │  ┌──────────────────────────────────────────────────┐  │    │
│  │  │ Visual Rendering                                   │  │    │
│  │  │  - Backdrop (semi-transparent black)              │  │    │
│  │  │  - Highlight shapes (rectangle/circle/arrow)      │  │    │
│  │  │  - Pulsing animation (scale 0.95-1.05)            │  │    │
│  │  └──────────────────────────────────────────────────┘  │    │
│  │  ┌──────────────────────────────────────────────────┐  │    │
│  │  │ Click-through                                      │  │    │
│  │  │  - MOUSE_FILTER_IGNORE on backdrop                │  │    │
│  │  │  - Player can interact with highlighted element   │  │    │
│  │  └──────────────────────────────────────────────────┘  │    │
│  └────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
                           │
                           │ Rendered output
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│                      PLAYER SCREEN                              │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │  [Layer 100] TutorialHighlight                           │  │
│  │    - Dimmed backdrop                                      │  │
│  │    - Glowing highlight around target element             │  │
│  ├──────────────────────────────────────────────────────────┤  │
│  │  [Layer 99] TutorialOverlay                              │  │
│  │    ┌─────────────────────────────────────────────────┐   │  │
│  │    │ Step 5 / 23                                      │   │  │
│  │    │ Fôr fisken din                                   │   │  │
│  │    │ Fisk må fôres daglig for å vokse...             │   │  │
│  │    │ [Neste] [Hopp over tutorial]                    │   │  │
│  │    └─────────────────────────────────────────────────┘   │  │
│  ├──────────────────────────────────────────────────────────┤  │
│  │  [Layer <99] Game UI (Dashboard, Pens, Controls)        │  │
│  └──────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Data Flow Diagram

### Starting Tutorial

```
Player starts new game
         │
         ▼
NewGameManager.start_new_game()
         │
         ▼
TutorialManager.start_tutorial()
         │
         ├─> Load tutorial_steps.json
         ├─> Check saved progress
         ├─> Set state = ACTIVE
         ├─> Set current_step = 1 (or last saved)
         └─> Emit tutorial_started signal
         │
         ▼
TutorialOverlay._on_tutorial_started()
         │
         ├─> Get step data from TutorialManager
         ├─> Display step title, text, progress
         └─> Request highlight from TutorialHighlight
         │
         ▼
TutorialHighlight.highlight_element(node, "rectangle")
         │
         ├─> Find UI element by name
         ├─> Draw backdrop
         ├─> Draw pulsing highlight
         └─> Start animation loop
         │
         ▼
Player sees first tutorial step!
```

### Step Completion Flow

```
Player performs action (e.g., feeds fish)
         │
         ▼
C# FeedingSystem.FeedPen(penId)
         │
         ├─> [Game logic: deduct feed, increase growth]
         └─> GameBridge.NotifyFishFed(penId)
         │
         ▼
GameBridge emits OnFishFed signal
         │
         ▼
TutorialManager._on_fish_fed(penId)
         │
         ├─> Get current step data
         ├─> Check if completion_trigger == "fish_fed"
         └─> If match: call next_step()
         │
         ▼
TutorialManager.next_step()
         │
         ├─> Mark current step as completed
         ├─> Save progress to file
         ├─> Emit step_completed signal
         ├─> Increment current_step
         ├─> Emit step_changed signal
         └─> Load next step data
         │
         ▼
TutorialOverlay._on_step_changed(step_id)
         │
         ├─> Get new step data
         ├─> Update UI (title, text, progress)
         └─> Request new highlight
         │
         ▼
TutorialHighlight.highlight_element(new_element, type)
         │
         └─> Update highlight to new target
         │
         ▼
Player sees next tutorial step!
```

### Skipping Tutorial

```
Player clicks "Hopp over tutorial"
         │
         ▼
TutorialOverlay._on_skip_pressed()
         │
         ├─> Show confirmation dialog
         └─> Wait for user choice
         │
         ▼
Player confirms skip
         │
         ▼
TutorialOverlay._on_skip_confirmed()
         │
         ▼
TutorialManager.skip_tutorial()
         │
         ├─> Set state = SKIPPED
         ├─> Save progress (skipped flag)
         └─> Emit tutorial_skipped signal
         │
         ▼
TutorialOverlay._on_tutorial_skipped()
         │
         ├─> Hide overlay
         └─> Clear highlight
         │
         ▼
TutorialHighlight.hide_highlight()
         │
         └─> Remove backdrop and shapes
         │
         ▼
Player continues game without tutorial
```

---

## State Machine Diagram

```
┌───────────┐
│ INACTIVE  │ (Initial state, no tutorial active)
└─────┬─────┘
      │
      │ start_tutorial()
      ▼
┌───────────┐
│  ACTIVE   │ (Tutorial running, showing steps)
└─────┬─────┘
      │
      ├────────────────────┐
      │                    │
      │ pause_tutorial()   │ skip_tutorial()
      ▼                    ▼
┌───────────┐        ┌───────────┐
│  PAUSED   │        │  SKIPPED  │ (User cancelled tutorial)
└─────┬─────┘        └───────────┘
      │                    ▲
      │ resume_tutorial()  │
      └────────────────────┘
      │
      │ Complete all 23 steps
      ▼
┌───────────┐
│ COMPLETE  │ (All steps finished)
└───────────┘
```

**State Transitions:**
- `INACTIVE → ACTIVE` - Player starts tutorial
- `ACTIVE → PAUSED` - System pauses (e.g., game menu opened)
- `PAUSED → ACTIVE` - Player resumes
- `ACTIVE → SKIPPED` - Player skips tutorial
- `ACTIVE → COMPLETE` - All 23 steps completed
- `PAUSED → SKIPPED` - Player skips while paused

**Note:** `SKIPPED` and `COMPLETE` are terminal states - tutorial won't restart unless progress file is deleted.

---

## File Structure & Relationships

```
tutorial_steps.json (Data)
      │
      │ Loaded by
      ▼
TutorialManager.gd (Logic)
      │
      ├─────────────┐
      │             │
      │ Emits       │ Provides
      │ signals     │ step data
      │             │
      ▼             ▼
TutorialOverlay.gd     TutorialHighlight.gd
(UI Display)           (Visual Effects)
      │                      │
      │ Requests             │
      │ highlight            │
      └──────────────────────┘

GameBridge.cs (C# Events)
      │
      │ Emits signals
      ▼
TutorialManager.gd
      │
      │ Validates completion
      ▼
Next step progression
```

---

## Signal Flow Chart

```
┌─────────────────────┐
│  GameBridge (C#)    │
└──────────┬──────────┘
           │
           │ OnFishFed
           │ OnPenHarvested
           │ OnDayAdvanced
           │ ... (more events)
           │
           ▼
┌─────────────────────┐
│ TutorialManager     │
└──────────┬──────────┘
           │
           │ step_changed
           │ step_completed
           │ tutorial_started
           │ tutorial_completed
           │ tutorial_skipped
           │
           ▼
┌─────────────────────┐
│ TutorialOverlay     │
└─────────────────────┘

Button Clicks:
TutorialOverlay.continue_button.pressed
           │
           ▼
TutorialManager.on_continue_clicked()
           │
           ▼
Step progression (if trigger matches)
```

---

## Performance Characteristics

### Memory Usage

| Component | Memory | Notes |
|-----------|--------|-------|
| tutorial_steps.json | 9.6 KB | Loaded once on startup |
| TutorialManager state | ~200 bytes | Current step, completed array |
| TutorialOverlay UI | ~50 KB | Panel, labels, buttons |
| TutorialHighlight drawing | ~10 KB | Canvas buffers |
| **Total** | **~70 KB** | Minimal footprint |

### CPU Usage

| State | CPU | Notes |
|-------|-----|-------|
| Inactive | 0% | No processing |
| Active (static) | <0.1% | Idle waiting for events |
| Active (animating) | <1% | Pulsing highlight animation |
| Step transition | <0.5% | JSON lookup, UI update |

### GPU Usage

| Operation | Draw Calls | Notes |
|-----------|------------|-------|
| Backdrop | 1 | ColorRect fill |
| Rectangle highlight | 8 | Outline + corner accents |
| Circle highlight | 2 | Arc + segments |
| Text overlay | 3 | Title, text, progress |
| **Total** | **~14** | Per frame when active |

---

## Thread Safety

All components run on the **main thread** (Godot scene tree thread).
- No multithreading issues
- Signals are processed synchronously
- File I/O is blocking (but fast: <10ms)

---

## Error Handling Strategy

### JSON Loading Errors
```gdscript
if file == null:
    push_error("Failed to load tutorial_steps.json")
    return  # Fail gracefully, no tutorial
```

### Missing UI Elements
```gdscript
var element = _find_ui_element(name)
if not element:
    print("Warning: UI element not found: ", name)
    # Continue without highlight
```

### Invalid Step Data
```gdscript
var step = _get_step(step_id)
if step.is_empty():
    push_error("Invalid step ID: ", step_id)
    return  # Don't advance
```

### File Save Errors
```gdscript
var file = FileAccess.open(path, FileAccess.WRITE)
if file == null:
    push_error("Failed to save tutorial progress")
    # Continue anyway, progress not critical
```

**Philosophy:** Fail gracefully. Tutorial is optional - never crash the game.

---

## Extension Points

### Adding New Completion Triggers

1. **Define in JSON:**
   ```json
   "completion_trigger": "my_custom_trigger"
   ```

2. **Add signal handler in TutorialManager:**
   ```gdscript
   func _on_custom_event(data):
       check_completion("my_custom_trigger", data)
   ```

3. **Connect in GameBridge (C#):**
   ```csharp
   [Signal]
   public delegate void OnCustomEventEventHandler(int data);
   
   // Emit when event occurs
   EmitSignal(SignalName.OnCustomEvent, data);
   ```

### Adding New Highlight Types

In `TutorialHighlight.gd`:
```gdscript
match highlight_type:
    "rectangle":
        _draw_rectangle_highlight(rect)
    "circle":
        _draw_circle_highlight(rect)
    "my_custom_shape":
        _draw_custom_shape(rect)  # Implement new shape
```

### Adding Analytics

In `TutorialManager.gd`:
```gdscript
func next_step():
    # ... existing logic ...
    
    # Track completion time
    var time_on_step = Time.get_ticks_msec() - step_start_time
    _send_analytics("step_completed", {
        "step_id": current_step,
        "time_ms": time_on_step
    })
```

---

## Dependencies Graph

```
project.godot (autoload config)
      │
      ├─> TutorialManager.gd
      │         │
      │         ├─> tutorial_steps.json (data)
      │         └─> user://tutorial_progress.json (save)
      │
      └─> TutorialHighlight.gd
                │
                └─> (no external deps)

Main.tscn (scene tree)
      │
      └─> TutorialOverlay.tscn
                │
                ├─> TutorialOverlay.gd
                │         │
                │         ├─> TutorialManager (autoload ref)
                │         └─> TutorialHighlight (autoload ref)
                │
                └─> (UI nodes created at runtime)

GameBridge.cs (C# autoload)
      │
      └─> Emits signals to TutorialManager
```

---

## Security Considerations

1. **Save File Tampering:** Progress is saved to `user://` which is user-writable
   - Impact: Low (player can skip tutorial anyway)
   - Mitigation: Not required for tutorial system

2. **JSON Injection:** tutorial_steps.json is part of game resources
   - Impact: None (controlled by developer)
   - Mitigation: Not required

3. **Memory Safety:** All arrays and dictionaries are managed by Godot GC
   - Impact: None (no manual memory management)
   - Mitigation: Not required

**Conclusion:** Tutorial system has no security-critical components.

---

## Future Architecture Improvements

1. **Event Bus Pattern:** Replace direct signal connections with event bus
2. **Step Factory:** Generate step objects from JSON instead of raw dicts
3. **Async Step Loading:** Load steps on-demand instead of all at once
4. **Animation System:** Scriptable animation sequences for highlights
5. **Telemetry Pipeline:** Built-in analytics with privacy controls
6. **Internationalization:** Multi-language support with translation keys
7. **Visual Editor:** In-editor tool for designing tutorial flows

---

## Questions & Answers

**Q: Why two CanvasLayers (99 and 100)?**
A: Layer 99 (overlay) can be occluded by layer 100 (highlight) to ensure highlight is always on top.

**Q: Why GDScript instead of C#?**
A: UI code benefits from Godot's native scene tree access. C# would add marshalling overhead.

**Q: Why not use built-in tutorial node?**
A: Godot 4 doesn't have a built-in tutorial system. Custom implementation provides more control.

**Q: Why save to user:// instead of res://?**
A: res:// is read-only in exported games. user:// is the correct location for player data.

**Q: Why not use database for progress?**
A: JSON is sufficient for small data. Database would be overkill for <1KB of progress.

---

**End of Architecture Documentation**
