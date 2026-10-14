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
├── Sun (DirectionalLight3D)
├── WorldEnvironment                      sky_shader.gdshader, fog, tonemap
├── ReflectionProbe
├── TimeOfDay (TimeOfDay.gd)              presentation clock, sun, sky uniforms
├── WeatherSystem (WeatherController.gd)  rain/snow particles, fog, wave uniforms
├── TiltShiftCanvas / UnderwaterCanvas    canvas post effects
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

## Shaders in use

| Shader | Used by |
|---|---|
| `shaders/terrain_shader.gdshader` | terrain material (built by `FjordTerrain.gd`) |
| `shaders/water_shader.gdshader` | `WaterPlane` |
| `shaders/sky_shader.gdshader` | `WorldEnvironment` sky |
| `shaders/tilt_shift.gdshader` | `TiltShiftCanvas` |
| `shaders/underwater_post.gdshader` | `UnderwaterCanvas` |
| `shaders/pen_*.gdshader`, `fish_shader.gdshader` | pen and fish assets |
