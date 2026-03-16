# Fjord Renderer — Technical Documentation

## Overview
3D Norwegian fjord environment renderer for Lakskongen. Provides terrain, water, camera, lighting (day/night), and weather systems.

## Architecture

```
FjordEnvironment (Node3D) — Main controller
├── Terrain (FjordTerrain) — Procedural heightmap terrain
├── WaterPlane (MeshInstance3D) — Ocean with custom shader
├── FjordCamera (Camera3D) — Zoom levels + tilt-shift
├── Sun (DirectionalLight3D) — Dynamic sun
├── WorldEnvironment — Sky, fog, ambient
├── TimeOfDay (Node) — Day/night cycle
├── WeatherSystem (WeatherController)
│   ├── RainParticles (GPUParticles3D)
│   └── SnowParticles (GPUParticles3D)
└── TiltShiftCanvas (CanvasLayer) — Post-process
```

## File Inventory

| File | Type | Purpose |
|------|------|---------|
| `scenes/environment/FjordEnvironment.tscn` | Scene | Main 3D environment scene |
| `scripts/environment/FjordEnvironment.gd` | Script | Environment controller + gameplay API |
| `scripts/environment/FjordTerrain.gd` | Script | Procedural terrain generation |
| `scripts/environment/FjordCamera.gd` | Script | Camera with zoom levels |
| `scripts/environment/TimeOfDay.gd` | Script | Day/night cycle + seasons |
| `scripts/environment/WeatherController.gd` | Script | Weather state machine |
| `shaders/water_shader.gdshader` | Shader | Ocean rendering (Gerstner waves) |
| `shaders/tilt_shift.gdshader` | Shader | Post-process miniature effect |
| `shaders/sky_shader.gdshader` | Shader | Dynamic sky with aurora |
| `shaders/terrain_shader.gdshader` | Shader | Multi-layer terrain texturing |

## Camera Controls

| Input | Action |
|-------|--------|
| W/A/S/D or Arrow Keys | Pan camera |
| Mouse Scroll Up | Zoom in (Coast → Fjord → Farm → Underwater) |
| Mouse Scroll Down | Zoom out |
| Middle Mouse Drag | Orbit camera |

### Zoom Levels

| Level | Altitude | FOV | Pitch | Use |
|-------|----------|-----|-------|-----|
| Coast | 1000m | 45° | -60° | Norwegian coast overview |
| Fjord | 200m | 50° | -50° | Site-level fjord view |
| Farm | 50m | 55° | -40° | Pen/boat detail |
| Underwater | -10m | 70° | -10° | Fish/lice/net view |

## Water Shader Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `wave_speed` | 1.0 | Wave animation speed |
| `wave_amplitude` | 0.3 | Wave height |
| `wave_frequency` | 2.0 | Wave density |
| `wave_count` | 4 | Number of Gerstner waves |
| `shallow_color` | #5CA8B8 | Shallow water turquoise |
| `medium_color` | #2A6B7C | Mid-depth teal |
| `deep_color` | #0F3A4A | Deep water navy |
| `foam_amount` | 1.5 | Shore foam distance |
| `wind_strength` | 1.0 | Wave intensity (weather-driven) |

## Time of Day

- 24-hour cycle controlled by `time_scale` (default: 2 min/real-sec)
- **Seasons** affect daylight hours:
  - Summer (season=0.5): ~20h daylight (midnight sun)
  - Winter (season=0.0): ~6h daylight (polar night)
- Signals: `sunrise`, `sunset`, `new_day`, `time_changed(hour)`

## Weather States

| State | Fog | Wind | Rain | Snow | Visibility |
|-------|-----|------|------|------|------------|
| Clear | 0.0005 | 0.3 | — | — | 100% |
| Overcast | 0.002 | 0.5 | — | — | 70% |
| Rain | 0.004 | 0.8 | ● | — | 50% |
| Snow | 0.003 | 0.4 | — | ● | 60% |
| Storm | 0.008 | 2.5 | ●● | — | 30% |
| Fog | 0.02 | 0.1 | — | — | 40% |

- Transitions blend over 10 seconds (configurable)
- Storms increase wave amplitude
- Signals: `weather_changed`, `storm_started`, `storm_ended`, `visibility_changed`

## Gameplay API (FjordEnvironment)

```gdscript
# Weather
environment.set_weather(WeatherController.WeatherState.STORM)
environment.is_storm_active()  # bool
environment.get_visibility()   # 0.0-1.0
environment.get_wind()         # Vector2

# Time
environment.set_time(14.5)     # 2:30 PM
environment.set_season(0.5)    # Summer
environment.get_time_string()  # "14:30"

# Camera
environment.focus_camera_on(Vector3(100, 0, -200), FjordCamera.ZoomLevel.FARM)

# Terrain
environment.get_ground_height(x, z)  # Float (for placing objects)
```

## Color Palette (Norwegian Aesthetic)

### Water
- Shallow: `#5CA8B8` (turquoise)
- Medium: `#2A6B7C` (teal-blue)
- Deep: `#0F3A4A` (dark navy)

### Land
- Rock: `#6B7280` — Moss: `#4A7C59` — Grass: `#7A9E6F`
- Autumn grass: `#B8A36F` — Snow: `#E8F1F5` — Beach: `#B1AA95`

### Sky
- Day: `#87CEEB` — Sunset: `#FF6B4A` → `#FFB347` — Night: `#0A1929`

### Lighting
- Sun: `#FFF4E6` — Moon: `#C7D7E8` — Aurora: `#4FFFB0` + `#A64FFF`

## Terrain Generation

Procedural heightmap with:
- Main fjord channel (winding, 200-400m deep)
- Secondary inlet (branching fjord)
- Cliff walls (steep transitions via `cliff_steepness`)
- 4 scattered islands
- Beach zones near water level
- FBM noise for natural variation

Terrain shader layers by height + slope:
- Underwater rock → Beach → Grass (seasonal) → Moss (north-facing) → Rock (cliffs) → Snow (peaks, seasonal)

## Performance Notes

- Terrain: 256×256 heightmap = 65K vertices (LOD-friendly)
- Water: 128×128 subdivided plane, 4 Gerstner waves in vertex shader
- Particles: Rain max 8000, Snow max 4000 (follow camera)
- Tilt-shift: 16-sample blur (canvas_item shader, lightweight)
- Sky shader: Procedural (no texture lookups)
- Target: 60 FPS @ 1080p on GTX 1060 / RX 580

## Integration

To use in a game scene:
```gdscript
# Instance the environment
var fjord = preload("res://scenes/environment/FjordEnvironment.tscn").instantiate()
add_child(fjord)

# Access systems
fjord.set_weather(WeatherController.WeatherState.RAIN)
fjord.set_season(0.75)  # Autumn
```

## Next Steps (Future Tasks)

- [ ] Particle process materials (rain/snow velocity, gravity, wind)
- [ ] Terrain LOD (chunk-based loading for larger worlds)
- [ ] Underwater post-process (blue tint, caustic overlay, blur)
- [ ] Northern lights particle/shader effect
- [ ] Lighthouse beam (SpotLight3D + volumetric)
- [ ] Terrain texture atlas (actual texture images vs. procedural color)
- [ ] Foam texture for water (noise-based → texture-based)
- [ ] Reflection probe placement
- [ ] Sound integration (ocean waves, rain, wind from AudioController)
