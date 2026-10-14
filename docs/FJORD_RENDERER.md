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
├── Terrain (FjordTerrain.gd)             heightfield mesh, collider, height queries
├── WaterPlane (MeshInstance3D)           water_shader.gdshader
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

## Shaders in use

| Shader | Used by |
|---|---|
| `shaders/terrain_shader.gdshader` | terrain material (built by `FjordTerrain.gd`) |
| `shaders/water_shader.gdshader` | `WaterPlane` |
| `shaders/sky_shader.gdshader` | `WorldEnvironment` sky |
| `shaders/tilt_shift.gdshader` | `TiltShiftCanvas` |
| `shaders/underwater_post.gdshader` | `UnderwaterCanvas` |
| `shaders/pen_*.gdshader`, `fish_shader.gdshader` | pen and fish assets |
