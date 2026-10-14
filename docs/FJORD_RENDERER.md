# Fjord renderer

What ships today, file by file. Keep this page in step with the code: the
P0-05 cleanup deleted the stale `godot/scenes/environment/FJORD_RENDERER.md`,
which described zoom levels, palettes and terrain features that no longer
existed. `tests/test_render_stack_cleanup.py` keeps the deleted stacks out.

## One renderer, one presenter

`scenes/GameScene.tscn` instances `scenes/environment/FjordEnvironment.tscn`
and adds two nodes of its own: `FishPenManager` (under the environment) and
`FjordVistaBridge` (beside it).

```
FjordEnvironment (FjordEnvironment.gd)    environment controller + gameplay API
├── Terrain (FjordTerrain.gd)             chunked heightfield mesh, collider, height queries
├── WaterPlane (MeshInstance3D)           water_shader.gdshader (+ OceanSkirt, built at runtime)
├── FjordCamera (FjordCamera.gd)          camera rig
├── Sun, Moon (DirectionalLight3D)        the Moon is Light Only (not drawn by the sky)
├── WorldEnvironment                      sky_shader.gdshader, fog, AgX, glow, SSAO
├── ReflectionProbe                       re-captured per sky state, no ambient
├── TimeOfDay (TimeOfDay.gd)              presentation hour + sim calendar -> sun, moon, aurora
├── WeatherSystem (WeatherController.gd)  presentation weather (blended), rain/snow particles
├── EnvironmentDirector                   the only writer of fog, sky, light, image, waves
├── UnderwaterCanvas                      underwater overlay
├── ParticleEffects (ParticleEffects.gd)
├── WellboatVisualManager / FeedBargeVisualManager / CompetitorFarmVisualManager
└── FishPenManager (GameScene)            pens, nets, fish schools, selection
FjordVistaBridge (GameScene)              the sim -> 3D presenter
```

`FjordVistaBridge` is the only script that reads simulation state for the 3D
world: on every `SimBridge.state_updated` it reconciles the pens and maps the
sim weather, season, waves, wind, temperature and bloom severity onto the
environment. The screenshot harness turns that off (`sim_weather_enabled =
false`) while it stages a shot. The boat and rival-farm managers bind to
`SimBridge` themselves.

Removed in P0-05: `Sim3DSync.gd` (a second sync layer that wrote the same
water uniforms and ignored the harness switch), `VisualAdapter3D.gd` and
`IVisualAdapter.gd` (a pen spawner nothing called), the `NordlandFjord` scene
with its controller, integration, marker and CPU water scripts,
`FjordTerrainGenerator.gd`, `FjordTerrain3D.gd`, the empty `gd-retroterrain`
addon link and the unreferenced `fjord_water`, `water_wave_shader` and
`fjord_sky` shaders.

## Terrain (G1-01)

One generator for every caller: `src/LakskongenSim/Environment/TerrainGenerator.cs`
(world metres, seeded, arithmetic only so heightfields are bit-identical on
every platform). `godot/scripts/environment/TerrainGeneratorBridge.cs` exposes
it to Godot; `FjordTerrain.gd` builds the heightfield and the render arrays on
a worker thread in headless and windowed runs alike, then uploads 64-quad
chunks under a 6 ms per-frame budget and adds a `HeightMapShape3D` collider.
`heightfield_hash` (FNV-1a over the height bits) is pinned in
`TerrainGeneratorTests.GoldenHashSeed42` and checked again by the headless
UI-flow test; the windowed build logs it.

| Feature | Value (defaults, seed 42) |
|---|---|
| World | 4 km square, 512² grid, 7.8 m spacing |
| Cross-section | floor −D·(1 − (d/w)⁴); Hermite wall to the shoulder; saturating upland |
| Basin | half-width 430 m ±15 %, depth 190 m ±12 %, shoulders 620 m ±12 % |
| Mouth (−Z) | 45 m sill at z = −1300, skerry strandflat outside with a 32 m fairway |
| Head (+Z) | walls close round a 2.5 m delta at z = 1450 |
| Detail | OpenSimplex2 FBM 1/1200 m⁻¹, 6 octaves, gain 0.5, 170 m warp at 1/2500 m⁻¹; ridged peaks behind the shoulders; gullies and ledges on the walls |
| Normals | central differences; triangles wind clockwise (Godot's front face) |

Height queries before the grid lands use the generator's analytic height, so
pens and boat routes placed in the first frames sit on the same fjord. Pen
slots run along `pen_slot_range()` on `channel_center_x()`.
`terrain_shader.gdshader` paints the Vestland Light palette by slope, height
and the mesh's cavity colour; its textures add luminance and normal detail.

## Camera (G1-03)

`FjordCamera.gd` orbits a focus point on the water.

| Input | Action |
|---|---|
| WASD, arrows, middle-drag | pan |
| Q/E, right-drag | rotate (right-drag also tilts) |
| Wheel | zoom ×1.15 toward the cursor, 25–3000 m; past 25 m: underwater |
| T | top-down toggle |
| Home | recentre on the first pen, looking down the fjord to the mouth |
| Double-click | focus the clicked point |

Pitch and field of view follow the distance (`VIEW_TABLE`): FARM 140 m −32°
50°, FJORD 900 m −17° 45°, COAST 2400 m −20° 52°, which puts the horizon
13 % from the top of the frame at the FJORD and COAST defaults. Every axis is
damped exponentially. Terrain avoidance lifts the camera 32 m over the
ground and 15 m over a 32-sample sight line to the focus, and comes in along
the bearing when the lift alone would look straight down. The camera stays
300 m inside the world edge. Camera input acts only while the Fjord tab
shows the world (`GameUI.world_view_changed` → `FjordVistaBridge` →
`input_enabled`), letters are ignored while a text field has focus and arrows
while any Control has focus. Idle auto-orbit starts after 30 s.

`FjordEnvironment.gd` adds an ocean skirt (30 km, the water material) around
the 4 km water plane so the view out of the mouth ends at a horizon.

## Environment: light, sky, fog and weather (G1-02, G1-04)

`EnvironmentDirector.gd` is the only writer of the WorldEnvironment (fog,
tonemap, glow, ambient, SSAO), the sky material's uniforms, the Sun and Moon
lights, the camera's depth of field and the water's wave uniforms
(`tests/test_environment_director.py` fails on a second writer). Every frame
it recomposes them from its inputs, so leaving the water or a staged weather
needs no restore step:

| Input | From | Gives |
|---|---|---|
| hour of day | `TimeOfDay` presentation clock (2 game min/s, opens at 10:48; photo mode pins it) | sun and moon position |
| date, latitude | the sim (`GameBridge.GetSkyContext` via `FjordVistaBridge`): day 0 = 1 January, the scenario region's latitude (`RegionGeography`) | season, polar night, midnight sun, aurora |
| visibility, cloud, rain/snow, sea fog, wind, waves | the sim (`AtmosphereModel`) blended by `WeatherController`; named presets for staging | fog, sky, sun dimming, particles, waves |
| sea temperature, bloom | the sim, always synced | water tint |
| distance, height | `FjordCamera` | DOF, shadows, SSAO, underwater |

The models live in the sim and are unit-tested (`SkyModelTests.cs`):
`SolarGeometry` (Spencer declination, spherical-astronomy elevation and
azimuth, a low-precision moon; Bodø has polar night on 21 December and
midnight sun on 21 June, Bergen's noon sun stands 53° at midsummer),
`AtmosphereModel` (Koschmieder visibility from clean air, rain
σ = a·R^0.67, snow, spray and sea fog) and `AuroraModel` (a smooth
deterministic Kp per day, the oval's edge at 66.5° − 2.05·Kp geomagnetic).
`CelestialBridge.cs` exposes the first and the last to `TimeOfDay`. World
axes: north +X, up +Y, east +Z; the fjord opens west (−Z).

| Composed | Rule |
|---|---|
| Fog | `fog_density = 3.912 / visibility` (Koschmieder); aerial perspective 0.6, sky affect 0.15, sun scatter 0.25; height fog only for a sea-fog layer; underwater 30 m visibility |
| Sun | direction from `SolarGeometry`; colour and energy from the same Chapman-function transmittance the sky draws, white-balanced at 60°; clouds dim it (× lerp(1, 0.15, c)), soften its shadow and take its specular glint off the water |
| Moon | #C7D7E8, energy 0.04 × phase × elevation × (1 − 0.85 c), shadows only when the sun's are off |
| Exposure | 1 in daylight; past a 3° sun the eye adapts as (key_ref / key)^0.72 up to 16×, where the key is the zenith and horizon luminance of the same sky model plus the direct light; under a deck it adapts only part of the way, so storms stay dark |
| Tonemap, glow | AgX, white 6, contrast 1.3, adjustments off; Screen glow, intensity 0.25, threshold 1, levels 3-5 |
| Ambient | sky radiance × 0.6 by day (a fjord wall sees about half the sky), × 3.5 once the sky is dark |
| Aurora | `AuroraModel` brightness × darkness (sun below −6° to −14°) × clear sky (gone at 0.85 cover) |

`sky_shader.gdshader` (written for the project; formula references in its
header): single scattering through a spherical atmosphere (Rayleigh, Mie,
ozone) with Schüler's Chapman approximation for the sun ray, a twilight
term and a night floor; the sun disc is `LIGHT0` (`LIGHT0_DIRECTION`,
`_COLOR`, `_ENERGY`); a cumulus slab (1.2-2.4 km, 6 marched steps) and
cirrus at 8 km lit by the sun at cloud height (pink clouds after sunset),
scud under storms; stars; a phased moon disc; aurora curtains as vertical
sheets 100-300 km up crossed analytically per ray (557.7 nm green, a 630 nm
red top in strong displays). No `TIME`: `sky_time` animates, and the
radiance pass (`Sky.process_mode` incremental) skips the sun disc, the
aurora and the fine detail so ambient light and fog never count the sun
twice or turn green.

The screenshot harness stages its shots with sim weather off. `09` is a
real gameplay state: `lofoten_eventyr` (Nordland, 1 January), the first
calm, clear night with the oval to the north, hour pinned at 22:00; it fails
unless the aurora holds ≥ 3 % of the frame, and 0 % on the same night under
overcast.

## Image pipeline (G1-06, post and anti-aliasing)

MSAA 4× plus SMAA (`screen_space_aa = 2`); no tilt-shift pass (removed with
its canvas); `CameraAttributesPractical` depth of field focused on the
camera's focus distance (far blur from 1.8× the distance, amount FARM 0.06,
FJORD 0.10, COAST 0.14, off underwater and at night, near blur below 500 m);
directional shadow distance 350 / 1600 / 4500 m and SSAO radius 1.5 / 4 / 8 m
by zoom (SSAO fades out 1.5-6 km); no SDFGI (Godot 4.7 gets no indirect light
from it on Metal, #120496).

## Shaders in use

| Shader | Used by |
|---|---|
| `shaders/terrain_shader.gdshader` | terrain material (built by `FjordTerrain.gd`) |
| `shaders/water_shader.gdshader` | `WaterPlane` |
| `shaders/sky_shader.gdshader` | `WorldEnvironment` sky |
| `shaders/underwater_post.gdshader` | `UnderwaterCanvas` |
| `shaders/pen_*.gdshader`, `fish_shader.gdshader` | pen and fish assets |
