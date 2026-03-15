# Audio Asset Requirements - Lakskongen

## Directory Structure
```
audio/
├── music/           # Background music (looping .ogg files)
├── ambient/         # Ambient environmental sounds (looping .ogg files)
├── sfx/             # Alert and event sound effects (one-shot .ogg files)
└── ui/              # UI interaction sounds (one-shot .ogg files)
```

## Required Files (27 total)

### Music Tracks (8 files) — Loop: YES
**Format:** .ogg, 128-192 kbps, stereo  
**Duration:** 2-4 minutes (loopable)  
**Style:** Nordic ambient/folk, atmospheric

- `music/spring_theme.ogg` — Uplifting, hopeful (strings, light percussion)
- `music/summer_theme.ogg` — Bright, energetic (acoustic guitar, synth pads)
- `music/autumn_theme.ogg` — Melancholic, reflective (cello, piano)
- `music/winter_theme.ogg` — Cold, sparse (minimal synth, wind chimes)
- `music/main_theme.ogg` — Signature theme (full orchestration)
- `music/storm_music.ogg` — Tense, dramatic (heavy percussion, dark strings)
- `music/harbor_theme.ogg` — Industrial, busy (subtle machinery, rhythmic)
- `music/menu_theme.ogg` — Calm, inviting (soft piano, ambient pads)

---

### Ambient Base Layers (6 files) — Loop: YES
**Format:** .ogg, 96-128 kbps, stereo  
**Duration:** 30-90 seconds (seamless loop)  
**Style:** Realistic coastal ambience

- `ambient/calm_sea.ogg` — Gentle waves lapping, distant seagulls
- `ambient/storm.ogg` — Heavy waves crashing, wind howling, rain
- `ambient/wind.ogg` — Moderate wind whistling through structures
- `ambient/rain.ogg` — Rain on water and metal surfaces
- `ambient/harbor.ogg` — Harbor ambience (boats, distant machinery, buoys)
- `ambient/processing_plant.ogg` — Industrial hum, conveyor belts, machinery

---

### Ambient Weather Overlays (3 files) — Loop: YES
**Format:** .ogg, 96-128 kbps, stereo  
**Duration:** 30-60 seconds (seamless loop)  
**Style:** Additive layers (designed to mix with base at 60% volume)

- `ambient/overlay_wind.ogg` — Wind gusts (designed to layer over base)
- `ambient/overlay_rain.ogg` — Rain intensity layer (patter on metal/water)
- `ambient/overlay_storm.ogg` — Storm intensity layer (thunder rumble, heavy rain)

---

### Alert SFX (5 files) — Loop: NO
**Format:** .ogg, 128 kbps, stereo  
**Duration:** 1-3 seconds  
**Style:** Distinct, urgent but not obnoxious

- `sfx/alert_disease.ogg` — Ominous (low tone + subtle alarm)
- `sfx/alert_market_crash.ogg` — Alarming (descending pitch + warning beep)
- `sfx/achievement.ogg` — Triumphant (rising chime + sparkle)
- `sfx/harvest_complete.ogg` — Success (uplifting chime + water splash)
- `sfx/alert_lice.ogg` — Warning (urgent beep + subtle buzz)

---

### UI SFX (6 files) — Loop: NO
**Format:** .ogg, 96-128 kbps, mono  
**Duration:** 0.1-0.5 seconds  
**Style:** Crisp, tactile, professional

- `ui/button_click.ogg` — Satisfying click (soft, not harsh)
- `ui/button_hover.ogg` — Subtle highlight (soft whoosh or tone)
- `ui/notification.ogg` — Gentle bell or chime
- `ui/day_advance.ogg` — Time progression (clock tick or subtle swoosh)
- `ui/menu_open.ogg` — Menu appear (soft rising tone)
- `ui/menu_close.ogg` — Menu close (soft falling tone)

---

## Technical Specifications

### All Files
- **Format:** Ogg Vorbis (.ogg)
- **Sample Rate:** 44.1 kHz or 48 kHz
- **Bit Depth:** 16-bit
- **Normalization:** -3 dB peak to prevent clipping

### Looping Files (Music + Ambient)
- **Loop Points:** Must be seamless (fade in/out or natural loop)
- **Godot Metadata:** Set `loop=true` in Godot import settings

### One-Shot Files (SFX + UI)
- **Loop:** No
- **Length:** Keep short for responsiveness

---

## Free/CC0 Sound Sources

### Music
- **Incompetech (Kevin MacLeod)** — https://incompetech.com/ (CC BY, requires attribution)
- **Purple Planet Music** — https://www.purple-planet.com/ (royalty-free)

### Ambience & SFX
- **Freesound.org** — https://freesound.org/ (CC0/CC BY, filter for "ocean", "wind", "rain")
- **Sonniss GDC Audio Archive** — https://sonniss.com/gameaudiogdc (royalty-free annual bundle)
- **Zapsplat** — https://www.zapsplat.com/ (free tier available)

### UI Sounds
- **Kenney.nl** — https://kenney.nl/assets?q=audio (CC0 UI sound packs)

---

## Norwegian Authenticity Notes

For maximum immersion, consider:
- Recording actual Norwegian coastal ambience (Bergen, Trondheim fjords)
- Using Nordic folk instruments (hardanger fiddle, nyckelharpa) in music themes
- Authentic seagull species (European herring gull, not American gulls)
- Norwegian harbor ambience (Norwegian spoken in background if applicable)

---

## Testing Checklist (Once Assets Added)

- [ ] All files load without errors in Godot
- [ ] Music tracks loop seamlessly
- [ ] Ambient layers loop seamlessly
- [ ] Crossfades are smooth (no pops/clicks)
- [ ] Weather overlays blend well with base (at 60% volume)
- [ ] SFX pool handles rapid-fire clicks
- [ ] Volume levels consistent across categories
- [ ] No clipping or distortion
- [ ] UI sounds not too loud/annoying after 100+ clicks
- [ ] Alert sounds distinct from each other

---

**Last Updated:** 2026-03-15  
**Status:** All assets MISSING (pending sourcing/creation)
