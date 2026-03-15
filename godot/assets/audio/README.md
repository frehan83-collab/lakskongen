# Lakskongen Audio Assets

## 📁 Directory Structure

```
audio/
├── README.md                        ← You are here
├── AUDIO_ASSET_REQUIREMENTS.md      ← Full asset specifications
├── AUDIO_SYSTEM_QUICKREF.md         ← Developer quick reference
├── music/                           ← Background music (8 files needed)
├── ambient/                         ← Environmental ambience (9 files needed)
├── sfx/                             ← Event sound effects (5 files needed)
└── ui/                              ← UI sounds (6 files needed)
```

## 🚨 Current Status: **MISSING ASSETS**

**Total Assets Required:** 27 audio files  
**Total Assets Present:** 0 (placeholders only)

## ✅ Audio System Status

| Component | Status | Notes |
|-----------|--------|-------|
| Architecture | ✅ Complete | Multi-layer C#/GDScript design |
| Crossfading | ✅ Complete | Smooth 1.5-2.0s fades |
| Weather Reactivity | ✅ Complete | Dynamic ambient layers |
| Alert System | ✅ Complete | 5 alert types wired |
| Volume Controls | ✅ Complete | Full API for settings menu |
| SFX Pooling | ✅ Complete | 8-player pool |
| Resource Paths | ✅ Complete | MusicLibrary.gd catalog |
| Audio Assets | ❌ **MISSING** | **BLOCKING RELEASE** |

## 📋 Required Files (27 total)

### Music (8 files)
- `music/spring_theme.ogg`
- `music/summer_theme.ogg`
- `music/autumn_theme.ogg`
- `music/winter_theme.ogg`
- `music/main_theme.ogg`
- `music/storm_music.ogg`
- `music/harbor_theme.ogg`
- `music/menu_theme.ogg`

### Ambient Base (6 files)
- `ambient/calm_sea.ogg`
- `ambient/storm.ogg`
- `ambient/wind.ogg`
- `ambient/rain.ogg`
- `ambient/harbor.ogg`
- `ambient/processing_plant.ogg`

### Ambient Weather Overlays (3 files)
- `ambient/overlay_wind.ogg`
- `ambient/overlay_rain.ogg`
- `ambient/overlay_storm.ogg`

### Alert SFX (5 files)
- `sfx/alert_disease.ogg`
- `sfx/alert_market_crash.ogg`
- `sfx/achievement.ogg`
- `sfx/harvest_complete.ogg`
- `sfx/alert_lice.ogg`

### UI SFX (6 files)
- `ui/button_click.ogg`
- `ui/button_hover.ogg`
- `ui/notification.ogg`
- `ui/day_advance.ogg`
- `ui/menu_open.ogg`
- `ui/menu_close.ogg`

## 🎵 Free Audio Sources

### Royalty-Free Music
- [Incompetech (Kevin MacLeod)](https://incompetech.com/) — CC BY, Nordic ambient available
- [Purple Planet Music](https://www.purple-planet.com/) — Royalty-free
- [Bensound](https://www.bensound.com/) — Free tier available

### Sound Effects & Ambience
- [Freesound.org](https://freesound.org/) — CC0/CC BY, filter for "ocean", "wind", "rain"
- [Sonniss GDC Audio Archive](https://sonniss.com/gameaudiogdc) — Annual free bundle
- [Zapsplat](https://www.zapsplat.com/) — Free tier

### UI Sounds
- [Kenney.nl](https://kenney.nl/assets?q=audio) — CC0 UI sound packs

## 🔧 Technical Specs

- **Format:** Ogg Vorbis (.ogg)
- **Sample Rate:** 44.1 kHz or 48 kHz
- **Bit Depth:** 16-bit
- **Normalization:** -3 dB peak
- **Channels:** Stereo (music/ambient), Mono (UI)
- **Looping:** Music and ambient files must loop seamlessly

## 📖 Documentation

- **Full Requirements:** See `AUDIO_ASSET_REQUIREMENTS.md`
- **Developer Guide:** See `AUDIO_SYSTEM_QUICKREF.md`
- **System Review:** See `/T63_SOUND_DESIGN_REPORT.md` (workspace root)

## 🚀 Next Steps

1. **Source or create 27 audio files** (prioritize UI + alerts + calm_sea + main_theme)
2. Place files in correct subdirectories (`music/`, `ambient/`, `sfx/`, `ui/`)
3. Enable `loop=true` in Godot import settings for music/ambient files
4. Test in-game (see `AUDIO_SYSTEM_QUICKREF.md` for test checklist)

## ⚠️ Known Issues

- Harvest alert (`sfx/harvest_complete.ogg`) defined but not yet triggered (harvest system not implemented)
- Spatial 3D audio for boats/pens not yet implemented (future enhancement)

---

**Status:** Ready for audio asset integration  
**Last Updated:** 2026-03-15  
**Contact:** Audio team or project lead
