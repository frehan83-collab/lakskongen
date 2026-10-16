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
├── PlanarReflection (PlanarReflection.gd) the water's mirror: SubViewport, mirrored camera
├── VesselWakes (VesselWakes.gd)          wakes behind moving boats (ribbons, wake_shader)
├── PenSpray (PenSpray.gd)                spray at the pens in a gale (particles)
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
| Fog | `fog_density = 3.912 / visibility` (Koschmieder), drawn at no less than a smooth 2.5 km (G3-05, below); aerial perspective 0.6, sky affect 0.15, sun scatter 0.25; height fog for a sea-fog layer, or mist above a mood's low cloud base; underwater 30 m visibility; the colour (the mood-graded sky) written sRGB-encoded |
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
`_COLOR`, `_ENERGY`); a cumulus slab (1.2-2.4 km: a search, a bisected edge and 3 steps in, G3-05) and
cirrus at 8 km lit by the sun at cloud height (pink clouds after sunset),
scud under storms; stars; a phased moon disc; aurora curtains as vertical
sheets 100-300 km up crossed analytically per ray (557.7 nm green, a 630 nm
red top in strong displays). No `TIME`: `sky_time` animates, and the
radiance pass (`Sky.process_mode` incremental) skips the sun disc, the
aurora and the fine detail so ambient light and fog never count the sun
twice or turn green.

The three cloud layers can be marched in the sky's half- or quarter-
resolution pass instead of per pixel (`cloud_pass`, from the quality tier:
High uses the half pass, Medium and Low the quarter pass, which turns the
dark cumulus against an aurora blocky). The pass writes them premultiplied
and the full-resolution pass composites them over the sky. The engine
blends the environment's fog into every sky pass, the reduced ones too
(Godot `sky.glsl`: rgb' = rgb (1 − k) + k fog colour + s sun light ·
cos⁸ · π with k = sky affect × (1 − aerial perspective)), so the composite
takes that first fogging back out with the terms the director wrote
(`sky_fog_k`, `sky_fog_scatter`, `haze_color` = the fog colour); a clear,
broken and overcast sky measure within 1.5/255 of the per-pixel march. The
zenith colour that lights the clouds from above is one value per frame
(`sky_zenith`, the director's own sky model) instead of a march per pixel.

The screenshot harness stages its shots with sim weather off. `09` is a
real gameplay state: `lofoten_eventyr` (Nordland, 1 January), the first
calm, clear night with the oval to the north, hour pinned at 22:00; it fails
unless the aurora holds ≥ 3 % of the frame, and 0 % on the same night under
overcast.

## Moods and weather drama (G3-05)

The physical model above decides how much light there is; a **mood** is the
art direction on top of it (`EnvironmentMood.gd`): the hue of the clear sky,
the cloud deck, the fog and the sunlight, the water's body colour, a small
exposure bias, a low cloud base with mist on the mountains, the deck's
brightness and a lift of the last degrees over the horizon. Grading keeps
the model's luminance and takes the chroma from the mood, so a mood never
fights the light it is given. Eight profiles:

| Mood | When (weighed every frame from the sim's weather and calendar) | Look |
|---|---|---|
| Sommerdag | clear day | blue sky paling to the horizon, deep blue water (owner #0B3F7A-#12407A), cloud shadows on the land |
| Midnattssol | clear, early summer, the sun low (< 12°) in the northern half | gold horizon (#F3D4A4) and sunlight (#FFC27A), slate water |
| Gråvær | overcast (cloud 0.62-0.88) | closed grey deck (#A7B3BA), grey water (owner #5E666C), cloud base 200 m: low cloud banks and mist on the walls |
| Storm | wind 11-14.5 m/s under cloud, or heavy rain under a deck | dark slate deck (#4C5861, ×0.42) over a lighter far horizon (+1.4), rain veil a shade under it, slanting rain, scud on the walls, camera sway |
| Blåtime | the sun between -7° and +3° (mørketid noons in the north) | deep blue sky (#3C5A8A-#7F96BC), pink south (#D99AA8), slate mirror |
| Nordlys | night (sun under -7° to -11°), clear | night sky (#08121F) for the aurora, water mirroring it, pen lamps |
| Frostrøyk | clear, still (< 3-6 m/s) winter day | cool cyan-grey sky (ref 2), slate mirror, sea smoke from the sim |
| Høst | clear, late August to mid November | low golden sun (#FFD9A0) on the birches, clear dark sea |

`weights()` turns the inputs (sun elevation and bearing, day of year,
cloud, rain and snow, wind) into eight weights summing to one; the
director moves its weights toward them at `MOOD_RATE` (0.8/s), so a front
or a sunset turns the mood over seconds, and blends the profiles in linear
light. The screenshot harness snaps them once a QA shot's weather has
settled. Sources: the art audit's palette and the owner's three water
moods (`SPECS`); the mood goldens are measured against them (below).

**The engine's colour inputs are sRGB.** `Environment.fog_light_color`,
`ambient_light_color` and every `source_color` shader uniform are
linearised by the renderer (probe: a fog colour of 0.5 renders 0.498 in
sRGB, i.e. 0.214 linear). The director computes scene-linear radiance, so
it writes them through `encoded()` (`linear_to_srgb`). Before G3-05 they
went in raw: a linear 0.18 arrived as 0.027, and every overcast, storm,
dusk and night fog came out 4-10× too dark, the far land and sea sinking
into it (the art review's "mountains vanish entirely" in storms).

**Fog in a storm.** Fog stays Koschmieder's (3.912 / visibility) above
`READABLE_VIS_M` + `READABLE_SOFT_M` (4.5 km); under it the drawn
visibility is a smooth maximum with 2.5 km (`drawn_visibility()`), because
the sim's storm (1.5-2 km) would bury the player's own farm at the default
zoom. A storm still veils the FJORD focus to 25-35 % (G1-04: ≤ 35 %).

**Under a closed deck** the sky shows no clear sky behind the thinning far
deck (the bright atmosphere leaked through as a white band under storms):
the horizon is the deck's own diffused light (the haze colour), lifted in
the last degrees by `horizon_lift`. Far broken cloud fades out over the
last ~3° (`HORIZON_FADE`).

**Low cloud on the mountains** (`MistBanks.gd`, `mist_bank.gdshader`):
under a low deck (`cloud_base_m`, `mist`) soft banks of cloud stand on the
walls' contour lines (170-540 m, every 260 m along both walls, 106 cards in
one MultiMesh draw), upright and turned to face the camera about the
vertical, ragged FBM wisps drifting with the wind, cut flat at the cloud
base, faded against the slope (depth buffer) and near the camera, lit by
the deck's light. The engine's height fog (negative density above the base)
whitens the tops behind them. Both clear as the camera climbs past the
deck (`mist_clear_for`: full under base + 150 m, gone at base + 550 m), so
COAST keeps the farm in sight. Off the mirror (NO_MIRROR_LAYER).

**Frost smoke** (sim: `AtmosphereModel.FrostSmoke`, Saunders 1964 steaming
over a cold-air outbreak, clear and calm) is a layer 16 m deep drawn by the
water shader (`water_mood.gdshaderinc`): its path through the layer thickens
at a graze, so the far fjord is veiled and the water under the camera stays
clear; soft three-octave puffs inside banks with clear water between, lit
by the sun and the sky, lifting once the sun warms the air (6-18°).

**Cloud shadows** (`CloudShadows.gd`): one Decal over the view, projected
along the sunlight, a seamless noise thresholded at the cloud cover (cover
0.28 shades 27 % of the ground), drifting with the sky's low clouds; the
water reads the same map. Strongest under broken cloud with the sun over
6-15°, none under a closed deck or at night; `cloud_shadows` knob (off on
Low).

**Pen lamps** (`PenLamps.gd`): one OmniLight 5 m down in each stocked pen
(up to 24 nearest) after dusk in the lighting season (October-April); the
water draws their glow through the surface at a fixed screen brightness
(divided by the night's exposure). **Storm sway** (`StormSway.gd`): an
additive h/v offset of the view from 11 m/s, at most 0.45 % of the view
height; reduced motion holds it still. **Rain** slants with the wind
(`rain_streak.gdshader`, streaks along each drop's velocity) and its
emitter sits upwind.

**World UI at night**: a vessel's status chip divides its text and pill
by the exposure (`VesselChip.set_exposure`, `world_chip.gdshader`
`exposure_comp`); site labels and signal rings do the same through
`SiteLights` on the art-site track (`integration/wave2`).

**The clouds' march** (`cloud_layer_low`): the low slab's noise is 2D, so
samples at fixed distances along a ray stack each cloud into horizontal
slices. The march searches a finer grid on screen (≤ 0.6 km, up to
`cloud_search` × the tier's steps, two octaves fewer plus a margin), bisects
to the first cloud's edge (4 steps) and integrates 3 steps into it (0.4 km,
longer far off), lit by one look toward the sun at its edge: edges and
shading are continuous. The radiance map and the planar mirror's sky (blurred
into ambient light and a wave-distorted reflection) keep the plain march
(`cloud_layer_low_simple`), and the water draws its lamp glow and frost smoke
per vertex: in the fragment stage their code (off on most days) cost ~1 ms at
FJORD in register pressure alone.

**Mood goldens** (`MoodGoldens.gd`, `ScreenshotHarness -- --only=moods`,
frames `20_mood_*.png`, report `20_moods.json`): eight FJORD frames from one
camera, each a real sim atmosphere (`GameBridge.SkyContextFor`: regime,
wind, sea temperature, day, latitude) at an hour; the run fails unless each
reads as its own mood, the frames differ pairwise by pHash ≥ 9, the sky and
the water within 2 km read within CIEDE2000 10 of `SPECS`, the lamps light
≥ 0.3 % of the night frame (A/B with them off), the cloud shadows move the
land (12 s, ≥ 0.5 dL* and 3× the frame without them), a storm raises Hs ≥ 2
m on the exposed coast (also at the sim's weakest storm wind) and the
storm's skyline stands ≥ 15 dL* off the sky.

## Water (G1-05)

Opaque: `water_shader.gdshader` writes no `ALPHA` and reads no screen or
depth texture (`tests/test_water_optics.py`), so it stays in the opaque pass
with SSR-ready depth, depth of field and correct sorting against nets and
boats, and the terrain never shows through deep water.

| Part | What it does |
|---|---|
| Bake | `WaterBake.cs`, on the terrain worker thread (`TerrainGeneratorBridge`), one RGBA16F texel per heightfield vertex (`FjordTerrain.water_bake_texture`, `water_bake_at()`): R depth below sea level, G signed shore distance (exact Euclidean distance transform, Felzenszwalb & Huttenlocher, on a 2× grid: ±2 m), B open-sea exposure (16 rays per sample; a ray that leaves the world over water counts e^(−distance/1.5 km)), A seabed slope; plus the basin's effective fetch for the prevailing sea (CEM radials ±15°, median over sheltered water: 1.1-1.3 km for the default fjord) |
| Colour | Beer-Lambert over the baked depth along the refracted path: σ = (0.40, 0.09, 0.14) m⁻¹, deep scatter (0.0022, 0.0085, 0.0105); the seabed (sediment or rock by slope, sunlit caustics under 6 m) shows only through shallow water. A bloom adds green absorption and scatter, warm water a touch of green |
| Reflection | the engine's Schlick Fresnel, F0 0.02 (`SPECULAR` 0.354: the engine's F0 is 0.16·SPECULAR², and below F0 0.02 it fades the grazing reflection as specular occlusion), GGX; roughness 0.04 plus the slope variance of the waves too fine for the pixel. Facets never mirror below the horizon. The water sits on render layer 20, which the ReflectionProbe's `reflection_mask` leaves out: the water mirrors the sky radiance (updated with the sky) instead of a one-point capture whose box edge printed a seam across the open sea. Calm water adds the planar mirror on top (below, G2-03); `specular_occlusion_disabled`: the engine's specular occlusion scaled every reflection by the ambient light, which at night turned the water into a black void |
| Sea state | `SeaStateModel.cs`: Carter (1982) JONSWAP fetch-limited growth, gHs/U² = 1.6·10⁻³·χ^½ and gTp/U = 0.286·χ^⅓ (χ = gF/U²), capped at the Pierson-Moskowitz sea (0.243, 8.134); the JONSWAP spectrum (γ 3.3) sampled into 8 fixed bands (128 m down to 1.6 m) with Σa² = Hs²/8; Monahan & O'Muircheartaigh (1980) whitecap cover 3.84·10⁻⁶·U^3.41. Two seas per wind: the sheltered basin (the baked fetch) and the open coast (100 km), blended per pixel by the baked exposure. 3 m/s over the basin: Hs 0.05 m; 20 m/s on the open coast: Hs 3.2 m, Tp 7.9 s |
| Waves | 16 Gerstner components (2 per band, ±9-27° round the band heading, detuned 0.95/1.055), choppiness capped so the summed steepness stays under 0.75; phases integrated by the director (no `TIME`), so a wind change never makes them jump or slide; analytic normals per pixel, each component faded out of the normal once its wavelength is under 2-6 pixel footprints (footprint from the distance and the grazing angle) and its slope variance moved into the roughness; ripple normal maps (`tools/gen_water_textures.py`: a k^−3.5 spectrum) scaled by the wind |
| Foam | shore: a lacy fringe 4 m wide (`SHORE_FRINGE_M`) and a lapping line in any weather, a surf band 4-13 m wide with lines running in once the sea builds (shore distance field); whitecaps (G2-03): a crest gate on the components at the spectral peak (the swell lifts the sea without breaking it), breaker noise along the crests and a wave-group field a few hundred metres across that gathers the caps into patches; a soft halo round each core, spent foam trailing the crest (where it has just passed, streaked down the wind) and, in a gale, long wind streaks; past 0.1-0.35 of the dominant wavelength per pixel their mean cover |
| Underside | `cull_disabled`; `!FRONT_FACING` draws Snell's window (48.6°) lit by the day's sky and total internal reflection of the deep water outside it |
| Grid | `WaterGrid.gd`: level 0 is 2 m quads ±256 m round the camera, 4 more levels double the spacing to ±4.1 km (32 culled pieces), then a flat skirt to ±32.8 km; snapped to the camera in 2 m steps; the vertex shader stitches each level's rim to the coarser ring and lays the last rim flat, and draws a component only where it has ≥ 4 vertices per wavelength |

Cost (2560×1440, M1 Pro, A/B in one run, water shown vs hidden, p50):
calm FARM +1.9 ms, FJORD +3.4 ms, COAST −0.6 ms (the opaque water hides
terrain the GPU no longer shades); storm +3.5 / +4.5 / −0.2 ms. The old
transparent water cost about +5.8 ms at FJORD. The frame without water
already takes 25-30 ms there (sky, terrain, SSAO, DOF, MSAA + SMAA), so the
16.7 ms target is a whole-frame budget item, not the water's.

### Water v2: the mirror, foam, wakes and spray (G2-03)

**The planar mirror** (`PlanarReflection.gd`, `shaders/water/water_mirror.gdshaderinc`).
A SubViewport renders the scene from the player's camera mirrored in the
water plane (`mirror_transform`: M·B·M with M = diag(1, −1, 1), a proper
rotation along the mirrored view) into a linear HDR texture (`use_hdr_2d`,
no anti-aliasing) at a share of the window (`mirror_scale`). The water
looks a pixel up by projecting the point where the view ray meets the still
water with the very view-projection that rendered the texture
(`planar_view_proj`), so a texture rendered a frame earlier still lines up
with what it shows; the surface normal turns the reflected ray and moves
the lookup by the projected change of direction (`planar_distort` 0.7),
which breaks the mirror up under ripples and stretches it under chop. Its
share of the reflection falls with the roughness (0.07-0.2), with the facet
slope (0.15-0.35) and at the texture's edges; it is added as emission with
water's Schlick fresnel and the engine keeps the rest (`SPECULAR ·
sqrt(1 − share)`: below F0 0.02 the engine's grazing term scales with F0,
so this hands it exactly 1 − share). Fog: the mirrored camera's ray runs
camera → water → object and the main pass fogs the first leg again, so the
mirror carries half the main fog density (`MIRROR_FOG_SHARE`).

What the mirror camera must not see, because it sits under the water and
Godot cannot clip a camera to a plane: the water (layer 20) and render layer
19 (`NO_MIRROR_LAYER`): the net, sinker tube and lice skirt (`PenCage.
NO_MIRROR_PARTS`), the fish schools, the mooring ropes, pen labels and
vessel chips, rain and snow round the camera, wakes and spray. The terrain
lifts its under-water vertices onto the plane in the mirror pass
(`shaders/include/mirror_pass.gdshaderinc`, `lk_mirror_vertex` in the
terrain's `vertex()`): laid flat, the seabed faces away from a camera below
it and is culled; without it the strandflat and the sill showed as a dark
band where the open sea should mirror the sky. The mirror pass is
recognised per camera (its cull mask lacks layer 19 and it sits below the
water), not by a global uniform, which would hold one value for every
viewport drawn in a frame.

The mirror is lit by its own shadowless copy of the Sun and Moon
(`MIRROR_ONLY_LAYER`, render layer 18, which the player's camera and the
reflection probe leave out; the scene's Sun and Moon sit on layer 19):
a directional light is drawn for the cameras whose cull mask holds its
layer, and its shadow maps are rendered for every viewport it lights. Its
sky is its own Sky (the main one's shader and uniforms, written together
by the director) drawn in the sky's quarter-resolution pass, aurora
included (`sky_pass` 2), with stars and the sun and moon discs per pixel;
the main sky stays per pixel. Measured at 1440p FJORD (P0-04 harness,
paired): the mirror cost 4.7 ms sharing the main sky with shadows, 2.8 ms
with its own quarter-pass sky, 1.9 ms without shadows, 1.4 ms at 0.4 of
the window; the gate run with it measures p95 FARM 16.05, FJORD 16.14 and
COAST 15.31 ms on High (`docs/run/evidence/g2_03_water.md`).

| Tier | Mirror | Resolution | Updates | Drawn from |
|---|---|---|---|---|
| Low | none: the sky's radiance and the probe | | | |
| Medium | yes | 0.33 of the window | 30 Hz, 20 Hz at COAST | 300 m |
| High | yes | 0.4 | every frame, 30 Hz at COAST | 300 m (FARM views look steeply down: a few per cent fresnel) |
| Ultra | yes | 0.5 | every frame | any distance |

No mirror underwater, behind an opaque panel, or above 11 m/s of wind
(`MIRROR_MAX_WIND_MS`: no water anywhere is calm enough), so a storm costs
nothing extra; it fades in and out over 0.6 s.

**Whitecaps** are calibrated top-down against Monahan's share (the director's
`WHITECAP_Z_SCALE`/`OFFSET`, 1.25 and 0.39): drawn cover 0.75 / 3.88 / 11.9 %
at 9 / 14 / 20 m/s against 0.7 / 3.1 / 10.5 %. The crest gate's per-component
weights round the spectral peak come from the director (`wave_peak`, per
sea), not from a log2/exp per pixel (0.45 ms at 1440p FJORD).

**Wakes** (`VesselWakes.gd`, `shaders/water/wake_shader.gdshader`). Every
VesselModel in the fjord is followed by its stern; every 0.9 beams (4-14 m)
of track it lays a point (where, sideways, when, how fast). A boat's points
make one mesh of three ribbons: the wash down the track (a beam wide,
widening as sqrt(age), gone in about 24 s) and the two Kelvin arms at
tan(19.47°) of the distance the boat has gone since, fading over 200 m;
the shader shapes and fades them from the age, so the mesh is rebuilt only
when a point is laid or dropped. They float 0.18 m over the still water and
fade out as the whitecap cover grows (a gale hides wakes in its own foam).

**Spray** (`PenSpray.gd`): one GPUParticles3D per pen, emitting off the
collar ring; its share follows the wind (from 9 m/s, a near gale) and the
wave height at the pen, in gusts.

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
the water (RUN_LOG §7, 2026-10-14). Grid buoys, lamps and ropes stay at rest
height for now.

### Site layout and berths

A site's frame mooring lies along the channel (`FishPenManager.channel_yaw_at`).
The wellboat berth is on the fairway side, its inner hull 38.5 m from the
pen (3.5 m outside the grid buoys); the feed barge lies on the other side, its
inner hull 46 m out, with the pen's feed hose turned towards it; the workboat
lies at the grid's up-fjord end (`get_berth`, `site_zones`).
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
from it on Metal, #120496). That is the Ultra tier; the tiers below trim it
(next section). The pen net (`pen_net_shader.gdshader`) writes its
thread coverage as alpha with `alpha_to_coverage_and_one`, so MSAA resolves
the threads, and fades to its mean coverage 1 − (1 − w)² once the mesh passes
0.35 cells per pixel: no moire on a distant or orbiting pen.

## Quality tiers and frame budget (P0-04, G5-01 groundwork)

`RenderQuality.gd` holds four tiers, Low / Medium / High / Ultra, as one
knob table each; `EnvironmentDirector` owns one, applies its global part
(the viewport's MSAA, SMAA and 3D scale with its upscaler; the
RenderingServer's SSAO, depth-of-field and soft-shadow quality and the
shadow atlas; the cascade count; the sky's radiance size) and composes with
the rest every frame (SSAO, glow, depth of field and its reach, shadow
distance, where and how finely the sky marches its clouds). Nothing else
writes these (`tests/test_perf_harness.py`). The player picks the tier in
Innstillinger → Grafikk (Grafikkvalitet: Lav, Middels, Høy, Ultra; saved as
`graphics.quality`, applied at once through the `environment_director`
group). High is the default.

| Knob | Low | Medium | High | Ultra |
|---|---|---|---|---|
| MSAA + screen AA | off + FXAA | 2× + SMAA | 4× + SMAA | 4× + SMAA |
| 3D scale (MetalFX spatial on macOS, FSR 1 elsewhere) | 0.6 | 0.67 | 0.75 | 1.0 |
| SSAO | off | off | off | medium, half size |
| Depth of field | off | box, very low, to 700 m | box, very low, to 700 m (FARM) | hexagon, low, every zoom, near blur |
| Shadows | 2048, 2 cascades, 0.6× distance, hard | 4096, 2 cascades, 0.8×, very low | 4096, 4 cascades, 1.0×, very low | 4096, 4 cascades, 1.0×, low |
| Clouds | quarter pass, 4 steps, 4 octaves | quarter, 5, 5 | half, 6, 6 | per pixel, 6, 6 |
| Sky radiance map | 64 | 128 | 256 | 256 |
| Terrain detail (`quality_detail` for the terrain shader) | 0 | 0.5 | 1 | 1 |
| Planar mirror (G2-03): share of the window, updates, drawn from | none | 0.33, 30 / 20 Hz, 300 m | 0.4, every frame / 30 Hz at COAST, 300 m | 0.5, every frame, always |

Ultra is the Wave-1 image unchanged. High trims what does not show at
1440p (frames side by side in `docs/run/evidence/p0_04_perf.md`): SSAO
(its depth and normal prepass is the single most expensive effect on
Metal and lights nothing the baked cavity colour does not), depth of field
past 700 m (the haze softens the far fjord as much at FJORD and COAST),
per-pixel clouds (soft: the half-resolution pass, a quarter of the
pixels, draws the same sky, even dark cumulus against an aurora), and
renders the 3D at 0.75 with MetalFX spatial upscaling. It keeps MSAA 4× +
SMAA (D12): the pen net resolves its threads with alpha-to-coverage, and
the thin rails, ropes and masts stay whole. Temporal upscalers (FSR 2,
MetalFX temporal, TAA) are not used: the water's waves are vertex-animated
and carry no motion vectors, so they would smear.

Behind an opaque full-screen panel (any tab but Fjord, with the UI layer
drawn) the 3D renders at half scale and the sky material holds still, so
its radiance map is not re-rendered for a world nobody sees (G1-06): the
panel frame drops from 31 ms to 7.9 ms on High. The screenshot harness
hides the UI layer for its fjord shots, which counts as the world showing.

`ShaderPrewarm` draws a millimetre triangle per late-arriving material
(the terrain shader, with its chunks' vertex arrays) in front of the camera
on the scene's first frames, so the terrain's pipelines are compiled before
its chunks arrive from the worker thread.

The P0-04 smoke test (`tools/perf_smoke.sh`, `godot/scripts/tools/
PerfHarness.gd`) boots the seeded QA scenario and sweeps the camera 50° at
FARM, FJORD and COAST, then holds behind the finance panel, 10 s each at
2560×1440 with vsync off, and reports p50/p95/p99 frame times, script and
render submission time, draw calls, shadow draws and video memory
(`tools/perf_report.py`); Metal reports no GPU timestamps, so the frame
time is the measure (the frame is GPU-bound: script ≈ 0.8 ms). Variants
(tiers and knob overrides) run interleaved, or `--paired` next to a
reference block, because other GPU work on a shared machine slows every
frame; compare inside one run. CI step 18 runs it warn-only. On High the
gate run measures p95 FARM 15.9, FJORD 15.0 and COAST 14.8 ms (the Wave-1
build: 29.3, 32.6 and 30.9 ms); `docs/run/evidence/p0_04_perf.md` has the
runs, the cost breakdown and the frames.

## Shaders in use

| Shader | Used by |
|---|---|
| `shaders/terrain_shader.gdshader` | terrain material (built by `FjordTerrain.gd`) |
| `shaders/water_shader.gdshader` (+ `shaders/water/water_mirror.gdshaderinc`) | `WaterGrid` |
| `shaders/water/wake_shader.gdshader` | `VesselWakes` ribbons |
| `shaders/include/mirror_pass.gdshaderinc` | the terrain (keeps the seabed out of the mirror) |
| `shaders/sky_shader.gdshader` | `WorldEnvironment` sky |
| `shaders/water/water_mood.gdshaderinc` | the water: frost smoke, cloud shadows, pen lamps (G3-05) |
| `shaders/mist_bank.gdshader` | `MistBanks` (low cloud on the walls, G3-05) |
| `shaders/rain_streak.gdshader` | the rain particles (streaks along the wind, G3-05) |
| `shaders/world_chip.gdshader` | `VesselChip` status pills |
| `shaders/underwater_post.gdshader` | `UnderwaterCanvas` |
| `shaders/pen_*.gdshader`, `fish_shader.gdshader` | pen and fish assets |
