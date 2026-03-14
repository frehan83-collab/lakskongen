# Localization Guide for Lakskongen

## Overview

Lakskongen now supports 6 languages:
- **Norwegian (no)** — Default
- **English (en)**
- **German (de)**
- **French (fr)**
- **Spanish (es)**
- **Japanese (ja)**

## How It Works

The localization system uses Godot's built-in translation framework with a CSV file containing all UI strings.

### Translation File

All translations are stored in: `res://translations/strings.csv`

Format:
```csv
keys,no,en,de,fr,es,ja
ui.main_menu.title,"LAKSKONGEN","SALMON KING","LACHSKÖNIG",...
```

### Translation Keys

Keys follow a hierarchical structure:
- `ui.<screen>.<element>` — UI text (e.g., `ui.dashboard.title`)
- `ui.tooltip.<element>` — Tooltips
- `ui.common.<element>` — Common buttons/labels (OK, Cancel, etc.)

## Using Translations in GDScript

### Basic Usage

Replace hardcoded strings with `tr()` calls:

```gdscript
# Before:
label.text = "Nytt Spill"

# After:
label.text = tr("ui.main_menu.new_game")
```

### Dynamic Text

For strings with variables, use format strings:

```gdscript
# Translation key in CSV:
# ui.dashboard.day_label,no,en
# "Dag %d","Day %d"

# In code:
day_label.text = tr("ui.dashboard.day") + " %d" % current_day
```

### Button Tooltips

```gdscript
button.tooltip_text = tr("ui.tooltip.new_game")
```

## Updating Existing UI Files

### Example: MainMenu.gd

**Before:**
```gdscript
btn_new_game.text = "Nytt Spill"
btn_load.text = "Last inn spill"
btn_settings.text = "Innstillinger"
```

**After:**
```gdscript
btn_new_game.text = tr("ui.main_menu.new_game")
btn_load.text = tr("ui.main_menu.load_game")
btn_settings.text = tr("ui.main_menu.settings")
```

### Example: Dashboard.gd

**Before:**
```gdscript
_set_text(day_label, "Dag %d" % _sim.current_day)
health_str = "Kritisk"
```

**After:**
```gdscript
_set_text(day_label, tr("ui.dashboard.day") + " %d" % _sim.current_day)
health_str = tr("ui.dashboard.critical")
```

## Language Switching

Users can change language in Settings → Language tab.

Language preference is automatically saved and persisted across sessions.

### Programmatic Language Change

```gdscript
# Get LocalizationManager
var loc_mgr = get_node("/root/LocalizationManager")

# Change language
loc_mgr.set_language("en")

# Get current language
var current = loc_mgr.get_language()  # Returns "no", "en", etc.

# Listen to language changes
loc_mgr.language_changed.connect(_on_language_changed)

func _on_language_changed(new_locale: String) -> void:
    # Refresh UI text
    update_all_labels()
```

## Adding New Translations

1. Open `salmon-sim/godot/translations/strings.csv`
2. Add a new row with the key and all 6 language versions:
   ```csv
   ui.new_feature.title,"Ny funksjon","New Feature","Neue Funktion","Nouvelle Fonctionnalité","Nueva Función","新機能"
   ```
3. Save the file
4. Use in code: `label.text = tr("ui.new_feature.title")`

## Testing

### In-Game Testing

1. Start the game
2. Go to Settings → Language
3. Select a different language
4. Verify all UI text updates correctly
5. Restart the game — language preference should persist

### Quick Language Change (Development)

In any script:
```gdscript
func _ready():
    # Force English for testing
    get_node("/root/LocalizationManager").set_language("en")
```

## Current Translation Coverage

✅ **Fully Translated:**
- Main Menu
- Settings Menu (all tabs)
- Dashboard (all sections)
- Contracts Panel
- Crew Panel
- Common UI elements

🔄 **Partial Coverage:**
- Achievement notifications
- Analytics panel
- Credits screen
- Event log messages

## Migration Checklist

To migrate an existing UI file to use translations:

1. [ ] Read the file and identify all hardcoded Norwegian strings
2. [ ] Check if translation keys exist in `strings.csv` (or add them)
3. [ ] Replace string assignments with `tr("key")` calls
4. [ ] For formatted strings, use `tr("key") % value` or string concatenation
5. [ ] Test in-game with multiple languages
6. [ ] Verify no hardcoded text remains

## Best Practices

✅ **DO:**
- Use descriptive translation keys (`ui.dashboard.health_status`, not `hs1`)
- Group related keys by screen/feature
- Keep translations concise (especially for Japanese)
- Test with all 6 languages before releasing

❌ **DON'T:**
- Hardcode UI strings (always use `tr()`)
- Concatenate translated fragments (different languages have different word order)
- Assume English is the default (Norwegian is default for this project)

## Troubleshooting

### "Missing translation" warnings

If you see warnings about missing translations:
1. Check the CSV file for the translation key
2. Verify the CSV is registered in `project.godot` under `[internationalization]`
3. Restart the Godot editor to reload translations

### Text not updating after language change

If text doesn't update when switching languages:
1. Ensure the script connects to `LocalizationManager.language_changed` signal
2. Refresh all UI labels in the signal handler
3. For dynamic UI, call `tr()` every time the text is set, not just once in `_ready()`

### Fallback Language

If a translation is missing for a key in a specific language, the system falls back to Norwegian (default locale).

Configure fallback in `project.godot`:
```ini
[internationalization]
locale/fallback="no"
```

## Files Modified

- `salmon-sim/godot/project.godot` — Added localization configuration
- `salmon-sim/godot/translations/strings.csv` — Translation database (200+ strings)
- `salmon-sim/godot/scripts/autoload/LocalizationManager.gd` — Language switching singleton
- `salmon-sim/godot/scripts/ui/SettingsMenu.gd` — Added Language tab

## Future Work

- [ ] Add language auto-detection based on system locale
- [ ] Translate remaining UI files (see Partial Coverage above)
- [ ] Add RTL (right-to-left) support for Arabic/Hebrew
- [ ] Professional translations for DE/FR/ES/JA (currently machine-translated placeholders)
- [ ] Voice-over localization for audio narration
- [ ] Region-specific formatting (dates, numbers, currency)
