# Audio System Quick Reference

## For Developers

### Playing Audio

#### Background Music
```gdscript
# Automatic based on season (handled by AudioController)
# Crossfades happen automatically when SimBridge state updates
```

#### Ambient Sounds
```gdscript
# Automatic based on weather (handled by AudioController)
# Weather overlays fade in/out automatically
```

#### Alert SFX
```gdscript
# From C# simulation code:
SimulationEngine.Audio.TriggerAlert("disease_outbreak")
# or "market_crash", "achievement", "harvest", "lice_warning"

# From GDScript:
SimBridge.audio_alert_triggered.emit("lice_warning")
```

#### UI Sounds
```gdscript
# From any GDScript UI code:
AudioController.play_ui("button_click")
# or "button_hover", "notification", "day_advance", "menu_open", "menu_close"
```

#### Custom SFX
```gdscript
AudioController.play_sfx("res://audio/sfx/custom_sound.ogg", 0.0, 1.0)
# play_sfx(path, volume_db, pitch_scale)
```

---

### Volume Controls

```gdscript
# Settings menu integration:
AudioController.set_music_volume(0.7)    # 0.0 to 1.0
AudioController.set_ambient_volume(0.5)  # 0.0 to 1.0
AudioController.set_sfx_volume(0.8)      # 0.0 to 1.0
AudioController.set_master_volume(1.0)   # 0.0 to 1.0

# Mute music:
AudioController.set_music_muted(true)
```

---

### Audio State (Read-Only)

```gdscript
# From SimBridge (GDScript):
var current_track = SimBridge.audio_seasonal_track  # "spring_theme", etc.
var base_ambient = SimBridge.audio_base_track       # "calm_sea", "storm", etc.
var weather_layer = SimBridge.audio_weather_layer   # "wind", "rain", "storm", ""
var weather_state = SimBridge.audio_weather_state   # "calm", "wind", "storm"

# From C# (GameBridge):
string track = GameBridge.AudioSeasonalTrack;
string base = GameBridge.AudioBaseTrack;
string weather = GameBridge.AudioWeatherLayer;
float volume = GameBridge.AudioVolume;
```

---

## For Audio Designers

### File Naming Convention
- **Music:** `{season}_theme.ogg` (e.g., `spring_theme.ogg`)
- **Ambient Base:** `{type}.ogg` (e.g., `calm_sea.ogg`, `storm.ogg`)
- **Ambient Overlay:** `overlay_{type}.ogg` (e.g., `overlay_wind.ogg`)
- **Alerts:** `alert_{type}.ogg` (e.g., `alert_disease.ogg`)
- **UI:** `{action}.ogg` (e.g., `button_click.ogg`)

### Audio Paths
```
res://audio/
├── music/           ← Background music (looping)
├── ambient/         ← Environmental ambience (looping)
├── sfx/             ← Event sound effects (one-shot)
└── ui/              ← UI sounds (one-shot)
```

### Technical Requirements
- **Format:** Ogg Vorbis (.ogg)
- **Sample Rate:** 44.1 kHz or 48 kHz
- **Channels:** Stereo (music/ambient), Mono (UI)
- **Normalization:** -3 dB peak
- **Looping:** Enable `loop=true` in Godot import settings for music/ambient

### Volume Mixing
- **Music Bus:** Crossfades at 70% default volume
- **Ambient Bus:** Base at 50%, weather overlay at 30% (60% of ambient)
- **SFX Bus:** 80% default volume
- **Master Bus:** 100% (user-controlled)

---

## For QA Testing

### Audio Checklist
- [ ] Music changes smoothly when seasons change (1.5s crossfade)
- [ ] Ambient base changes when weather changes (2.0s crossfade)
- [ ] Weather overlay fades in during storms (2.0s fade)
- [ ] Alert SFX plays on disease outbreak, market crash, etc.
- [ ] UI sounds respond to button clicks/hovers
- [ ] No audio popping or clicking during crossfades
- [ ] Volume sliders work in settings menu
- [ ] Mute button silences music
- [ ] No crashes when SFX pool exhausted (8 players)
- [ ] Missing audio files logged to console (warnings, not errors)

### Test Scenarios
1. **Seasonal Transition:** Advance 90+ days, verify music changes
2. **Weather Storm:** Trigger storm weather, verify ambient + overlay
3. **Rapid Clicks:** Click UI buttons 20+ times rapidly (SFX pool stress test)
4. **Volume Controls:** Test all sliders from 0% to 100%
5. **Missing Files:** Remove one audio file, verify warning in console

---

## Troubleshooting

### Audio Not Playing
1. Check console for `push_warning` messages (missing files)
2. Verify file paths in `MusicLibrary.gd` match actual files
3. Ensure `AudioController` and `MusicLibrary` are registered as autoloads
4. Check that `SimBridge` exists and is emitting `state_updated` signal

### Audio Popping/Clicking
1. Check audio file loop points (must be seamless)
2. Verify normalization (should be -3 dB peak, not 0 dB)
3. Check sample rate (should be consistent across all files)

### SFX Not Playing
1. Check if SFX pool exhausted (warning in console)
2. Increase `SFX_POOL_SIZE` constant if needed (currently 8)
3. Verify file exists in `res://audio/sfx/` or `res://audio/ui/`

### Volume Not Changing
1. Verify audio bus exists (check `default_bus_layout.tres`)
2. Check that `AudioController.set_*_volume()` is being called
3. Ensure bus isn't muted (`AudioServer.is_bus_mute()`)

---

**Last Updated:** 2026-03-15  
**Audio System Version:** 1.0 (Wave 4)
