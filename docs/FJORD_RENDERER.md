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
├── WaterGrid (WaterGrid.gd)              camera-snapped LOD water grid, water_shader.gdshader
├── FjordCamera (FjordCamera.gd)          camera rig
├── Sun, Moon (DirectionalLight3D)        the Moon is Light Only (not drawn by the sky)
├── WorldEnvironment                      sky_shader.gdshader, fog, AgX, glow, SSAO
├── ReflectionProbe                       re-captured per sky state, no ambient, not on the water
├── TimeOfDay (TimeOfDay.gd)              presentation hour + sim calendar -> sun, moon, aurora
├── WeatherSystem (WeatherController.gd)  presentation weather (blended), rain/snow particles
├── EnvironmentDirector                   the only writer of fog, sky, light, image, waves
├── UnderwaterCanvas                      underwater overlay
├── ParticleEffects (ParticleEffects.gd)
├── WellboatVisualManager / FeedBargeVisualManager / CompetitorFarmVisualManager
└── FishPenManager (GameScene)            sites (FarmSite: pens, grid, hoses), fish, selection
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

The water grid (`WaterGrid.gd`, below) runs out to ±32.8 km, so the view out
of the mouth ends at a horizon.

## Environment: light, sky, fog and weather (G1-02, G1-04)

`EnvironmentDirector.gd` is the only writer of the WorldEnvironment (fog,
tonemap, glow, ambient, SSAO), the sky material's uniforms, the Sun and Moon
lights, the camera's depth of field and the water material's sea state,
phases and bake (`tests/test_environment_director.py` fails on a second
writer). Every frame
it recomposes them from its inputs, so leaving the water or a staged weather
needs no restore step:

| Input | From | Gives |
|---|---|---|
| hour of day | `TimeOfDay` presentation clock (2 game min/s, opens at 10:48; photo mode pins it) | sun and moon position |
| date, latitude | the sim (`GameBridge.GetSkyContext` via `FjordVistaBridge`): day 0 = 1 January, the scenario region's latitude (`RegionGeography`) | season, polar night, midnight sun, aurora |
| visibility, cloud, rain/snow, sea fog, wind | the sim (`AtmosphereModel`) blended by `WeatherController`; named presets for staging | fog, sky, sun dimming, particles, the sea state (from the wind and the fetch) |
| sea temperature, bloom | the sim, always synced | water scatter and absorption |
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
| Exposure | 1 with the sun above 12°; below that the eye adapts as (key_ref / key)^0.72 up to 16×, where the key is the zenith and horizon luminance of the same sky model plus the direct light; under a deck it adapts only part of the way, so storms stay dark |
| Tonemap, glow | AgX, white 6, contrast 1.3, adjustments off; Screen glow, intensity 0.25, threshold 1, levels 3-5 |
| Ambient | sky radiance × 0.6 by day (a fjord wall sees about half the sky), × 6 once the sky is dark |
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

## Water (G1-05)

Opaque: `water_shader.gdshader` writes no `ALPHA` and reads no screen or
depth texture (`tests/test_water_optics.py`), so it stays in the opaque pass
with SSR-ready depth, depth of field and correct sorting against nets and
boats, and the terrain never shows through deep water.

| Part | What it does |
|---|---|
| Bake | `WaterBake.cs`, on the terrain worker thread (`TerrainGeneratorBridge`), one RGBA16F texel per heightfield vertex (`FjordTerrain.water_bake_texture`, `water_bake_at()`): R depth below sea level, G signed shore distance (exact Euclidean distance transform, Felzenszwalb & Huttenlocher, on a 2× grid: ±2 m), B open-sea exposure (16 rays per sample; a ray that leaves the world over water counts e^(−distance/1.5 km)), A seabed slope; plus the basin's effective fetch for the prevailing sea (CEM radials ±15°, median over sheltered water: 1.1-1.3 km for the default fjord) |
| Colour | Beer-Lambert over the baked depth along the refracted path: σ = (0.40, 0.09, 0.14) m⁻¹, deep scatter (0.0022, 0.0085, 0.0105); the seabed (sediment or rock by slope, sunlit caustics under 6 m) shows only through shallow water. A bloom adds green absorption and scatter, warm water a touch of green |
| Reflection | the engine's Schlick Fresnel, F0 0.02 (`SPECULAR` 0.354: the engine's F0 is 0.16·SPECULAR², and below F0 0.02 it fades the grazing reflection as specular occlusion), GGX; roughness 0.04 plus the slope variance of the waves too fine for the pixel. Facets never mirror below the horizon. The water sits on render layer 20, which the ReflectionProbe's `reflection_mask` leaves out: the water mirrors the sky radiance (updated with the sky) instead of a one-point capture whose box edge printed a seam across the open sea |
| Sea state | `SeaStateModel.cs`: Carter (1982) JONSWAP fetch-limited growth, gHs/U² = 1.6·10⁻³·χ^½ and gTp/U = 0.286·χ^⅓ (χ = gF/U²), capped at the Pierson-Moskowitz sea (0.243, 8.134); the JONSWAP spectrum (γ 3.3) sampled into 8 fixed bands (128 m down to 1.6 m) with Σa² = Hs²/8; Monahan & O'Muircheartaigh (1980) whitecap cover 3.84·10⁻⁶·U^3.41. Two seas per wind: the sheltered basin (the baked fetch) and the open coast (100 km), blended per pixel by the baked exposure. 3 m/s over the basin: Hs 0.05 m; 20 m/s on the open coast: Hs 3.2 m, Tp 7.9 s |
| Waves | 16 Gerstner components (2 per band, ±9-27° round the band heading, detuned 0.95/1.055), choppiness capped so the summed steepness stays under 0.75; phases integrated by the director (no `TIME`), so a wind change never makes them jump or slide; analytic normals per pixel, each component faded out of the normal once its wavelength is under 2-6 pixel footprints (footprint from the distance and the grazing angle) and its slope variance moved into the roughness; ripple normal maps (`tools/gen_water_textures.py`: a k^−3.5 spectrum) scaled by the wind |
| Foam | shore: a lapping line in a calm, a surf band 1.5-10 m wide with lines running in once the sea builds (shore distance field); whitecaps: crest gate + breaker noise stretched along the crests over a threshold mapped from Monahan's share (calibrated top-down: cover 0.7 / 3.1 / 10.5 % at 9 / 14 / 20 m/s), lacy core and spent foam; past 0.1-0.35 of the dominant wavelength per pixel a faint mean whitening |
| Underside | `cull_disabled`; `!FRONT_FACING` draws Snell's window (48.6°) lit by the day's sky and total internal reflection of the deep water outside it |
| Grid | `WaterGrid.gd`: level 0 is 2 m quads ±256 m round the camera, 4 more levels double the spacing to ±4.1 km (32 culled pieces), then a flat skirt to ±32.8 km; snapped to the camera in 2 m steps; the vertex shader stitches each level's rim to the coarser ring and lays the last rim flat, and draws a component only where it has ≥ 4 vertices per wavelength |

Cost (2560×1440, M1 Pro, A/B in one run, water shown vs hidden, p50):
calm FARM +1.9 ms, FJORD +3.4 ms, COAST −0.6 ms (the opaque water hides
terrain the GPU no longer shades); storm +3.5 / +4.5 / −0.2 ms. The old
transparent water cost about +5.8 ms at FJORD. The frame without water
already takes 25-30 ms there (sky, terrain, SSAO, DOF, MSAA + SMAA), so the
16.7 ms target is a whole-frame budget item, not the water's.

### Floating pens and boats (G2-03, heave part)

`WaveField.gd` is the drawn water on the CPU: the shader's `bake_at`,
`component_amplitude`, `displacement` and the grid terms of `vertex()`,
mirrored term for term and fed the very arrays the director writes to the
`wave_k` / `wave_amp` uniforms, the grid's origin and the water bake
(`EnvironmentDirector.wave_field`, `WaveField.active`). `height_at(x, z)`
inverts the Gerstner map by fixed-point iteration. Only drawn geometry
counts: a component lifts a body only where its wavelength spans 3 grid
spacings, as in the shader. Bodies ride the least-squares plane of the water
under them, in closed form per component: a pen collar its 26 m ring
(`ring()`: J0 for the mean, 2·J1/R for the slope), a hull its waterplane
rectangle (`hull()`: sinc for the mean, 3(sin u − u cos u)/u³ for the
slopes). `PenCage.ride_waves()` and `VesselModel.ride_waves()` set height and
tilt every frame (every 8th past the LOD2 distance) and leave x, z and yaw to
their owners; the barge's tender floats on its own water while staying tied
to the barge (on the barge's schedule). Each component is carried to second
order in its steepness (over fixed ground a Gerstner wave sits Q·k·a²/2
lower, with a 2k ripple), which keeps a 116 m hull within 0.05 m of the
sampled water on the open coast in a 25 m/s gale. 26 bodies cost 0.25 ms a
frame in a storm (M1 Pro), with no allocations.
The UI flow test pins the mirror to the shader numerically and the bodies to
the water (RUN_LOG §7, 2026-10-14). The site's lighter floating gear rides
the same water on the GPU (below).

### Sites: pens in a mooring grid (G2-04)

A site is a grid of round pens, not one ring (owner reference 1). Every sea
site the sim reports is a `FarmSite` with the engine's pen count
(`GetSeaSiteDetails` `"pen_count"`, `SeaSite.NumberOfPens`); `SiteLayout.gd`
plans it as pure functions:

| | |
|---|---|
| Grid | up to 3 pens in one row along the channel, more in two rows (`grid_dims`); one pen per 90 m frame cell (`MooringGrid.CELL_M`: Scottish site plans hold 160 m pens in 90-110 m grids; A2-01 asks 70-100 m); an odd count leaves the last cell empty |
| Channel | the 12 slots along the basin; a site takes as many as its grid needs plus a 40 m gap (`slot_count`), the player's from the mouth, the rivals' from the head (`farm_slot`, rivals take one slot each) |
| Placement | the site's centre level with its slots, in the deepest water where the whole grid floats over 30 m (`FishPenManager._site_position`), laid along the channel (`channel_yaw_at`) |
| Berths | from the grid's half-width (`SiteBerths.gd`): the feed barge square off the frame line its hoses enter the lane by (a one-row site's middle), its inner hull 16 m outside the outer frame line, a wellboat's 3.5 m outside the buoys on the fairway side; the workboat inside the up-fjord cell of the first column |
| Hoses | when the barge moors (`FeedBargeVisualManager` → `attach_feed_barge`): one white hose per pen from an outlet on the barge's grid side (lifting to its deck), through the near row between two pens, along the lane between the rows and into each pen's hose port 30 m from its centre (a one-row site: straight from the barge); Catmull-Rom with a gentle meander; yellow floats every 70 m, merged where hoses run together (`hose_key_points`, `smooth_hose`, `hose_floats`) |
| Variants | per site: bird-net tent (centre mast) or green net on blue rim poles (`bird_net_for`), lice skirt (`has_skirt`); rivals add their colour band, orange buoys, quiet nets, no health rings or pen numbers, and a moored barge with hoses |

`FishPenManager` keeps the site-id API the UI uses (`get_pen_ids`,
`get_pen_position` = the grid's centre, `select_pen`, `pen_clicked`) and adds
`get_site`, `get_site_cages`, `get_site_label`, `slots_used` and
`cage_selected`. A click on any pen (or on the water inside the grid, the
nearest pen) selects the site and that pen: a bright ring round the pen,
quiet rings round the site's others. Per-pen data (`"pens"`, S2-01b) drives
each pen's ring and fish; without it each pen shows the site's lice and an
even share of its fish. One name per site over the grid, a number on each
pen of a multi-pen site, up close.

**Draw calls.** Each pen is a `PenCage` (its LOD0 meshes, health ring and
click area), but past LOD0 the site draws all its pens through one
MultiMesh per pen part (LOD1, LOD2), so the far field costs one draw per
shown part whatever the pen count; each pen hands over by its own camera
distance (`PenCage.LOD_SWITCH`, `LOD_HYSTERESIS`), and nothing past LOD0
casts a shadow. The poses are uploaded as one buffer (`MultiMeshPack`) only
when a pen has ridden or changed LOD. Measured (po3_red_sone, 1920×1080, M1
Pro, sites shown vs hidden in one run): draws FARM 175 vs 35, FJORD 168 vs
64, COAST 254 vs 138 (two player sites of 6 and 4 pens, six rival farms,
barges, boats, hoses); render-thread CPU +0.1-0.15 ms; frame time within the
shared machine's noise. Script cost per frame (algekatastrofe, 9 sites, 41
pens): fish 0.3-0.5 ms, LODs 0.2 ms, riding 0.2 ms. The salmon schools are
`FishSchool.gd` (one MultiMesh per pen, drawn and swum within 450 m).

**Floating gear on the GPU.** The hoses, grid buoys, hose floats, corner
lights and the buoy ends of the chains and pen ends of the bridles ride the
drawn water in their vertex shaders (`site_float.gdshaderinc`): the water
shader's displacement and bake lookup mirrored term for term, inverted by
fixed-point iteration (4 steps) like `WaveField.height_at`. `SiteFloat.sync`
copies the director's wave arrays, the grid's placement and the terrain's
water bake into the seven floating materials once a frame; the UI flow tests
hold `SiteFloat.reference_height` (the same algorithm on the CPU) to
`WaveField.height_at` (1e-4 m in a 25 m/s storm) and `tests/test_site_float.py`
pins the include to the shader. Loop bounds are uniforms, so the loops are
not unrolled (an unrolled draft took minutes to compile on Metal). The hoses
are camera-facing ribbons at least 1.6 px wide and the buoys at least 3 px
(up to 3× their size), so the gear reads from FARM to COAST like the white
lines and yellow dots of the aerial photos. Every minimum-pixel formula takes
`abs(PROJECTION_MATRIX[1][1])`: the renderer's projection flips y, and
without the abs the minimum silently never applied (the corner-light halos
and the health rings had that bug).

### Site layout and berths

A site's frame mooring lies along the channel (`FishPenManager.channel_yaw_at`).
The wellboat berth is on the fairway side, its inner hull 3.5 m outside the
grid buoys; the feed barge lies on the other side, square off the frame
line its hoses enter by, its inner hull 16 m outside the outer frame line,
its hoses running to every pen; the workboat lies in the up-fjord cell of the grid's first
column (`SiteBerths.gd` through `get_berth` and `site_zones`).
`ChannelBoat.berth_route` sails a boat from the harbour or depot along the
channel with keel depth under its whole length, clear of every other site's
grid and berths, and runs it straight in along the grid to its berth.

## Image pipeline (G1-06, post and anti-aliasing)

MSAA 4× plus SMAA (`screen_space_aa = 2`); no tilt-shift pass (removed with
its canvas); `CameraAttributesPractical` depth of field focused on the
camera's focus distance (far blur from 1.8× the distance, amount FARM 0.06,
FJORD 0.10, COAST 0.14, off underwater and at night, near blur below 500 m);
directional shadow distance 350 / 1600 / 4500 m and SSAO radius 1.5 / 4 / 8 m
by zoom (SSAO fades out 1.5-6 km); no SDFGI (Godot 4.7 gets no indirect light
from it on Metal, #120496). The pen net (`pen_net_shader.gdshader`) writes its
thread coverage as alpha with `alpha_to_coverage_and_one`, so MSAA resolves
the threads, and fades to its mean coverage 1 − (1 − w)² once the mesh passes
0.35 cells per pixel: no moire on a distant or orbiting pen.

## Shaders in use

| Shader | Used by |
|---|---|
| `shaders/terrain_shader.gdshader` | terrain material (built by `FjordTerrain.gd`) |
| `shaders/water_shader.gdshader` | `WaterGrid` |
| `shaders/sky_shader.gdshader` | `WorldEnvironment` sky |
| `shaders/underwater_post.gdshader` | `UnderwaterCanvas` |
| `shaders/pen_*.gdshader`, `fish_shader.gdshader` | pen and fish assets |
| `shaders/site_float.gdshaderinc` (in `feed_hose`, `mooring_buoy`, `mooring_lamp`, `mooring_light_glow`, `mooring_rope`, `site_kit_float`) | the site's floating gear |
