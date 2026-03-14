# Example: Migrating MainMenu.gd to Use Translations

This document shows how to migrate `MainMenu.gd` from hardcoded Norwegian strings to the translation system.

## Changes Required

### 1. Update Button Text in `_setup_buttons()`

**Before:**
```gdscript
func _setup_tooltips() -> void:
	btn_new_game.tooltip_text = "Start et nytt oppdrettseventyr"
	btn_load.tooltip_text = "Last inn et lagret spill"
	btn_settings.tooltip_text = "Endre innstillinger for lyd, grafikk og spill"
	btn_credits.tooltip_text = "Se hvem som laget spillet"
	btn_quit.tooltip_text = "Avslutt til skrivebordet"
```

**After:**
```gdscript
func _setup_tooltips() -> void:
	btn_new_game.tooltip_text = tr("ui.tooltip.new_game")
	btn_load.tooltip_text = tr("ui.tooltip.load_game")
	btn_settings.tooltip_text = tr("ui.tooltip.settings")
	btn_credits.tooltip_text = tr("ui.tooltip.credits")
	btn_quit.tooltip_text = tr("ui.tooltip.quit")
```

### 2. Update Title and Subtitle (if set in code)

If title/subtitle are set programmatically:

**Before:**
```gdscript
title_label.text = "🐟 LAKSKONGEN"
subtitle_label.text = "Norsk Lakseoppdrett Simulator"
version_label.text = "Lakskongen v0.1.0-alpha"
```

**After:**
```gdscript
title_label.text = "🐟 " + tr("ui.main_menu.title")
subtitle_label.text = tr("ui.main_menu.subtitle")
version_label.text = tr("ui.main_menu.version")
```

### 3. Listen to Language Changes

Add this to support live language switching:

```gdscript
func _ready() -> void:
	_save_manager = get_node_or_null("/root/SaveLoadManager")
	_game_bridge  = get_node_or_null("/root/GameBridge")
	
	# Connect to language change signal
	var loc_mgr = get_node_or_null("/root/LocalizationManager")
	if loc_mgr:
		loc_mgr.language_changed.connect(_on_language_changed)

	_setup_background()
	_setup_buttons()
	_setup_submenus()
	_start_shimmer_loop()
	_spawn_fish()

	version_label.text = tr("ui.main_menu.version")
	_update_button_text()  # NEW: Update all button text

	# ... rest of _ready()


func _update_button_text() -> void:
	"""Update all translatable button text (for language switching)"""
	btn_new_game.text = tr("ui.main_menu.new_game")
	btn_load.text = tr("ui.main_menu.load_game")
	btn_settings.text = tr("ui.main_menu.settings")
	btn_credits.text = tr("ui.main_menu.credits")
	btn_quit.text = tr("ui.main_menu.quit")
	
	# Update tooltips too
	_setup_tooltips()


func _on_language_changed(new_locale: String) -> void:
	"""Called when user changes language in settings"""
	_update_button_text()
	title_label.text = "🐟 " + tr("ui.main_menu.title")
	subtitle_label.text = tr("ui.main_menu.subtitle")
	version_label.text = tr("ui.main_menu.version")
```

## Quick Test

After making these changes:

1. Start the game
2. Click "Innstillinger" (Settings)
3. Go to "Språk" (Language) tab
4. Select "English"
5. Return to main menu
6. Buttons should now show English text:
   - "New Game"
   - "Load Game"
   - "Settings"
   - "Credits"
   - "Quit"

## Notes

- If button text is set in the `.tscn` scene file instead of code, you can either:
  - Set it programmatically in `_ready()` using `tr()`
  - OR use Godot's "Make Translatable" feature on the Text property in the Inspector
  
- The emoji "🐟" in the title is intentionally kept outside the translation key since it's decorative and universal.

- Version numbers (`v0.1.0-alpha`) typically don't need translation, but you can keep them in the CSV for consistency.
