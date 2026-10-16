class_name EnvironmentDirector
extends Node
## The only writer of the fjord's light, sky, fog and image state (G1-02,
## G1-04, G1-06). Every frame it composes, from its inputs:
##
## - TimeOfDay: sun, moon and aurora (sim calendar and latitude, presentation hour);
## - WeatherController: blended cloud, visibility, rain, snow, sea fog, wind, waves;
## - the sim's water state (temperature, bloom) via set_water_state();
## - FjordCamera: distance to the focus, height, underwater;
##
## and writes:
## - WorldEnvironment: fog (Koschmieder, sigma = 3.912 / visibility), tonemap
##   (AgX), glow, ambient light, SSAO;
## - the sky material's uniforms (cloud cover from the weather only, aurora,
##   moon, haze, animation time);
## - the Sun and Moon lights: direction, colour, energy, shadows;
## - the camera's depth of field;
## - the water material: the sea state's wave bands (SeaStateModel: JONSWAP
##   from the wind and the fetch, Monahan whitecaps), their phases, the
##   optics and the terrain's water bake (G1-05);
## - wave_field, the CPU mirror of the drawn water (G2-03): the same wave
##   arrays, grid and bake, so pens and boats ride the waves they sit in.
## - the mood (G3-05, EnvironmentMood): eight art-directed moods weighed
##   from the sim's weather and calendar every frame and blended; they
##   grade the sky, the deck, the fog and the sunlight, set the water's body
##   colour and an exposure bias, lay mist on the mountains under a low
##   deck and keep the land a shade under the sky so a storm leaves the
##   mountains' outline; the frost smoke on the water, the clouds' shadows
##   (CloudShadows), the pens' lamps after dark (PenLamps) and the camera's
##   sway in a storm (StormSway).
##
## Nothing else writes these: tests/test_environment_director.py fails on a
## second writer. Because the result is recomposed from the inputs every
## frame, leaving a mode (underwater, a staged weather) needs no "restore".

@export var world_environment: WorldEnvironment
@export var sun_light: DirectionalLight3D
@export var moon_light: DirectionalLight3D
@export var time_system: TimeOfDay
@export var weather_system: WeatherController
@export var camera: FjordCamera
@export var water_grid: WaterGrid
@export var terrain: FjordTerrain
## Captured once per sky state: the water and terrain reflect it, so a probe
## left at its boot-time capture tints every later frame (olive dawns). Its
## ambient is off: the sky's radiance, updated every frame, lights the land.
@export var reflection_probe: ReflectionProbe
## The water's planar mirror (G2-03): its look (fog, ambient, background)
## is composed here like the main view's; the tier switches it.
@export var planar_reflection: PlanarReflection
## Cloud shadows on the land (G3-05) and the pens' submerged lamps.
@export var cloud_shadows: CloudShadows
@export var pen_lamps: PenLamps

# ── Atmosphere (mirrors sky_shader.gdshader) ──────────────────────────────────
const R_EARTH := 6360.0
const H_RAYLEIGH := 8.0
const H_MIE := 1.2
const BETA_R := Vector3(5.802e-3, 13.558e-3, 33.1e-3)
const BETA_M_EXT := 3.33e-3
const OZONE_ZENITH := Vector3(0.00975, 0.02822, 0.00128)
const OZONE_RADIUS := 6385.0
const OBSERVER_H := 0.1
const R_TOP := 6420.0
const BETA_M := 3.0e-3
const MIE_G := 0.8
## Multiple-scattering fill (isotropic), as a fraction of single scattering.
const MULTI_SCATTER := 0.18
## Scene-linear scale of the sun's irradiance in the sky model.
const SKY_SCALE := 30.0
## Sunlight on clouds, in the DirectionalLight's energy scale (shader's CLOUD_LIGHT).
const CLOUD_LIGHT := 2.6
const CLOUD_HEIGHT_KM := 1.6

# ── Light ─────────────────────────────────────────────────────────────────────
## Sun energy with the sun 60 degrees up on a clear day.
const SUN_ENERGY := 2.4
## White balance: sunlight at this elevation renders neutral white.
const WHITE_BALANCE_ELEVATION := 60.0
## Clouds dim the sun to this fraction at full cover (roadmap: lerp(1, 0.15, c)).
const OVERCAST_SUN_FRACTION := 0.15
const MOON_COLOR := Color("#C7D7E8")
## Lower than a photograph's 0.08: the night exposure opens up to 16x.
const MOON_ENERGY := 0.04
## Sun disc and penumbra: 0.53 degrees clear, wide under cloud.
const SUN_ANGLE_CLEAR := 0.53
const SUN_ANGLE_OVERCAST := 2.0

# ── Exposure (an eye that adapts) ─────────────────────────────────────────────
## Scene key: zenith sky luminance plus shares of the direct light and the
## horizon. Daylight is exposed at 1 down to a sun ADAPT_FROM_ELEVATION high;
## darker scenes open up by (key_ref / key)^ADAPTATION, so a night stays
## darker than a day but reads, up to MAX_EXPOSURE.
const ADAPT_FROM_ELEVATION := 12.0
const ADAPTATION := 0.72
const MAX_EXPOSURE := 16.0
## Share of a deck's darkness (in log terms) the eye does not adapt away:
## the key falls by deck_ratio^(1 - OVERCAST_KEEP), so a storm stays darker.
const OVERCAST_KEEP := 0.65
## Skylight on the land: a fjord wall sees about half the sky (the opposite
## wall hides the rest, at a scale SSAO cannot see), so daylight ambient runs
## below 1. At night it is raised to stand in for dark-adapted eyes.
const AMBIENT_DAY := 0.6
const AMBIENT_NIGHT := 6.0
## Night sky floor (airglow, starlight), mirrored from sky_shader night_sky().
const NIGHT_SKY_ZENITH := Vector3(0.0013, 0.0019, 0.0040)
const NIGHT_SKY_HORIZON := Vector3(0.0022, 0.0027, 0.0045)

# ── Fog ───────────────────────────────────────────────────────────────────────
const KOSCHMIEDER := 3.912
const AERIAL_PERSPECTIVE := 0.6
const SKY_AFFECT := 0.15
const SUN_SCATTER := 0.25
## Visibility above a sea-fog layer (the layer itself is height fog).
const ABOVE_SEA_FOG_VISIBILITY_M := 15000.0
const SEA_FOG_TOP_M := 45.0
const SEA_FOG_HEIGHT_DENSITY := 0.045
## Underwater: fjord water seen through a camera at a few metres' depth.
const UNDERWATER_VISIBILITY_M := 30.0
## Readability (G3-05): in a storm the sim's visibility drops to 1.5-2 km,
## where the drawn fog (now the sky's true luminance) would bury the
## player's own farm at the default zoom and every wall behind it. The drawn
## fog keeps at least READABLE_VIS_M (a smooth floor, exact above
## READABLE_VIS_M + READABLE_SOFT_M): a storm still veils the FJORD focus to
## about 30 % (G1-04: <= 35 %), but the walls and the pens keep their shape.
const READABLE_VIS_M := 2500.0
const READABLE_SOFT_M := 2000.0
## The low deck's mist shows fully while the camera is less than
## MIST_CLEAR_FROM_M over the cloud base and is gone MIST_CLEAR_TO_M over it.
const MIST_CLEAR_FROM_M := 150.0
const MIST_CLEAR_TO_M := 550.0
const UNDERWATER_FOG_COLOR := Color(0.012, 0.055, 0.07)

# ── Tonemap and glow (D13 Vestland Light) ─────────────────────────────────────
const AGX_WHITE := 6.0
const AGX_CONTRAST := 1.3
const GLOW_INTENSITY := 0.25
const GLOW_THRESHOLD := 1.0

# ── Per-zoom image settings (G1-06) ───────────────────────────────────────────
## [camera distance m, shadow max distance m, SSAO radius m, DOF amount]
const ZOOM_TABLE := [
	[140.0, 350.0, 1.5, 0.06],
	[900.0, 1600.0, 4.0, 0.10],
	[2400.0, 4500.0, 8.0, 0.14],
]

# ── Water (G1-05) ─────────────────────────────────────────────────────────────
const SEA_BRIDGE_PATH := "res://scripts/environment/SeaStateBridge.cs"
const GRAVITY := 9.81
## Each band is drawn as two Gerstner components (16 in all, the shader's
## WAVES): headings either side of the band's heading (directional
## spreading, wider for shorter waves, within +-30 deg so no crest runs
## along the wind), wavenumbers detuned so the two never repeat together,
## the band's energy shared equally. A third component per band cost 2 ms
## at 1440p (M1 Pro, FJORD) for no visible gain.
const COMPONENTS_PER_BAND := 2
const BAND_HEADING_DEG := [0.0, 4.0, -6.0, 8.0, -9.0, 11.0, -12.0, 13.0]
const BAND_SPREAD_BASE_DEG := 9.0
const BAND_SPREAD_PER_BAND_DEG := 2.5
const COMPONENT_DETUNE := [0.95, 1.055]
const COMPONENT_WEIGHT := [0.70710678, 0.70710678]
## Gerstner choppiness, scaled down so the bands' summed steepness stays
## below WAVE_FOLD_LIMIT (no surface loops).
const CHOPPINESS := 1.1
const WAVE_FOLD_LIMIT := 0.75
## Ripple (normal-map) strength per m/s of wind, and its range.
const RIPPLE_PER_MS := 0.06
const RIPPLE_MIN := 0.12
const RIPPLE_MAX := 0.75
## The shader's whitecap field (a peak-band crest gate plus breaker noise,
## a wave-group field and foam lace) is not a standard normal: its threshold
## is mapped from the normal quantile of Monahan's share so that the drawn
## cover matches it. Calibrated top-down over the basin at 9, 14 and 20 m/s
## (W 0.7 %, 3.1 %, 10.5 %; G2-03, the cores brighter than luminance 0.45
## under an overcast sky): the cover hits W at thresholds 3.46, 2.72 and
## 1.96; a least-squares line through them.
const WHITECAP_Z_SCALE := 1.25
const WHITECAP_Z_OFFSET := 0.39
## Whitecaps break on the components round the spectral peak (peak_weight).
const PEAK_BAND_SHARPNESS := 1.5
## Re-derive the sea when the wind moves this much (m/s).
const SEA_WIND_STEP := 0.02

## Cloud drift: presentation clouds move this many times faster than the wind.
const CLOUD_DRIFT := 12.0

## Re-capture the reflection probe when the sky changes this much (sun
## elevation in degrees, cloud cover), at most once per PROBE_MIN_INTERVAL.
const PROBE_SUN_STEP := 3.0
const PROBE_CLOUD_STEP := 0.125
const PROBE_MIN_INTERVAL := 2.0
## Wait this long after a change so the sky's radiance has settled first.
const PROBE_SETTLE := 0.4

# ── Planar mirror (G2-03) ─────────────────────────────────────────────────────
## The mirrored camera's fog: its view ray runs camera -> water -> object
## and the main pass fogs the camera -> water leg again, so the mirror
## carries this share of the main fog (for a shore reflection, where the
## first leg is nearly all of it, the two passes add up to the main fog).
const MIRROR_FOG_SHARE := 0.5
## Above this wind (m/s) no water anywhere is calm enough to mirror: the
## mirror is not rendered at all (a storm costs nothing extra).
const MIRROR_MAX_WIND_MS := 11.0
## The mirror's sky marches in its quarter-resolution pass (sky_shader
## sky_pass) and lights the mirror from a small radiance map.
const MIRROR_SKY_PASS := 2
const MIRROR_RADIANCE_SIZE := Sky.RADIANCE_SIZE_32
## How far the surface normal moves the mirror lookup (water_mirror
## planar_distort): little in a dead calm, more once a breeze ripples it.
const MIRROR_DISTORT_CALM := 0.3
const MIRROR_DISTORT_BREEZE := 0.8

# ── Moods and weather drama (G3-05) ────────────────────────────────────────
## The mood weights follow their targets at this rate (1/s): a front or a
## sunset turns the mood over a few seconds, never in a frame.
const MOOD_RATE := 0.8
## Cloud shadows: drawn while the cloud is broken (from CLOUD_SHADOW_FROM to
## CLOUD_SHADOW_TO cover, gone again under a closed deck) and the sun is
## high enough to cast them (under SHADOW_SUN_FROM degrees they run long and
## blur away).
const CLOUD_SHADOW_FROM := 0.06
const CLOUD_SHADOW_PEAK := 0.25
const CLOUD_SHADOW_CLOSE := 0.7
const CLOUD_SHADOW_TO := 0.9
const SHADOW_SUN_FROM := 6.0
const SHADOW_SUN_FULL := 15.0
## Frost smoke: its colour, how much of the sun and sky it scatters back,
## and how fast its wisps drift with the wind (share of the wind speed).
const SMOKE_SUN := 0.8
const SMOKE_SKY := 1.8
const SMOKE_DRIFT := 0.6
## Pen lamps: the lamp's light (white LED, linear) filtered by the water
## above it (water_shader absorption over PenLamps.LAMP_DEPTH_M), the glow's
## screen brightness (linear, after exposure: under the glow threshold, so
## a lit pen glows without blooming) and the underwater light's energy.
const LAMP_WHITE := Vector3(0.95, 1.0, 1.0)
const WATER_ABSORPTION := Vector3(0.40, 0.09, 0.14)
const LAMP_GLOW_ON_SCREEN := 0.75
const LAMP_LIGHT_ENERGY := 2.5
## The mist banks' light against the horizon's (scene-linear).
const MIST_LIGHT := 1.1

var _water_temperature_c := 8.0
var _algae := 0.0
## Mood weights (EnvironmentMood.Id order) and the blended profile composed
## last frame (tests and the harness read them).
var mood_weights := PackedFloat32Array()
var mood: Dictionary = {}
var _smoke_drift := Vector2.ZERO
var _storm_sway: StormSway = null
## The low cloud banks on the walls (created at _ready; tests read them).
var mist_banks: MistBanks = null
var _sky_time := 0.0
var _cloud_offset := Vector2.ZERO
var _aurora := 0.0
var _moon_dir := Vector3.DOWN
var _composed: Dictionary = {}
var _white_balance := Vector3.ONE
var _key_ref := 0.3
var _probe_signature := ""
var _probe_timer := 0.0
var _terrain_was_ready := false
var _probe_due := -1.0
## Reflection-probe captures requested so far (tests read it).
var probe_captures := 0
var _sea_bridge: RefCounted = null
## Per wave component: band index, wavenumber, heading off the sea (deg),
## amplitude weight and phase (rad, integrated every frame).
var _comp_band := PackedInt32Array()
var _comp_k := PackedFloat32Array()
var _comp_heading := PackedFloat32Array()
var _comp_weight := PackedFloat32Array()
var _comp_phase := PackedFloat64Array()
var _sea_time := 0.0
var _sea_wind := -1.0
var _bake_applied: Texture2D = null
## The planar mirror's own sky (G2-03, _ensure_mirror_sky).
var _mirror_sky: Sky = null
## The sea composed last: hs/tp/amplitudes for the sheltered basin and the
## open coast, whitecaps (tests read it).
var sea_state: Dictionary = {}
## The drawn water on the CPU (G2-03): handed the very arrays the shader's
## wave uniforms get, every frame. WaveField.active points at it while this
## director is in the tree; PenCage and VesselModel float on it.
var wave_field := WaveField.new()
## Quality tier (P0-04, G5-01 groundwork): the knobs this director composes
## with, and the global render settings it applies through RenderQuality.
var quality := RenderQuality.new()
## Sky uniform writes made so far (tests read it: none while the world is
## hidden, so the sky's radiance map is not re-rendered behind a panel).
var sky_updates := 0
## The pipeline warm-up of the scene's first frames (frees itself), and the
## shaders it warmed (tests read them).
var prewarm: ShaderPrewarm = null
var prewarmed_shaders := PackedStringArray()
## Off only for the perf harness's A/B of the warm-up itself.
static var prewarm_enabled := true


func _enter_tree() -> void:
	WaveField.active = wave_field
	add_to_group(RenderQuality.DIRECTOR_GROUP)


func _exit_tree() -> void:
	if WaveField.active == wave_field:
		WaveField.active = null
	# The 3D scale lives on the shared viewport: leave it native for whatever
	# renders next (a menu scene with its own director sets its own).
	var vp := get_viewport()
	if vp != null:
		vp.scaling_3d_scale = 1.0
		vp.scaling_3d_mode = Viewport.SCALING_3D_MODE_BILINEAR


func _ready() -> void:
	_white_balance = sun_transmittance(sin(deg_to_rad(WHITE_BALANCE_ELEVATION)))
	var e := deg_to_rad(ADAPT_FROM_ELEVATION)
	var ref_sun := Vector3(-cos(e), sin(e), 0.0)
	_key_ref = clear_key(ref_sun, _clear_sun_energy(ref_sun), 0.0)
	if moon_light:
		# The moon lights the world but is not drawn by the sky's LIGHT0: the
		# shader draws it from moon_direction.
		moon_light.sky_mode = DirectionalLight3D.SKY_MODE_LIGHT_ONLY
	_static_setup()
	quality.set_tier(RenderQuality.load_saved_tier())
	_apply_quality()
	if terrain != null:
		terrain.terrain_ready.connect(_apply_terrain_quality)
	wave_field.bind_grid(water_grid)
	_prewarm()
	if camera != null:
		_storm_sway = StormSway.ensure(camera)
	mist_banks = MistBanks.ensure(get_parent())
	compose(0.0, true)


func _process(delta: float) -> void:
	compose(delta)


# ── Quality (P0-04, G5-01) ────────────────────────────────────────────────────

## Switch the quality tier ("low", "medium", "high", "ultra" or a
## RenderQuality.Tier) with optional knob overrides (the perf harness's A/B
## variants); applies at once. Unknown names keep the current tier.
func set_quality(tier: Variant, overrides: Dictionary = {}) -> void:
	var index := RenderQuality.tier_of(String(tier)) if tier is String else int(tier)
	if index < 0:
		push_warning("EnvironmentDirector: unknown quality tier '%s'" % str(tier))
		index = quality.tier
	quality.set_tier(index, overrides)
	_apply_quality()
	compose(0.0, true)


func get_quality_name() -> String:
	return quality.tier_name()


func get_quality_knobs() -> Dictionary:
	return quality.knobs.duplicate()


## The world is on screen (the Fjord tab) or covered by an opaque full-screen
## panel (FjordVistaBridge, from GameUI.world_view_changed). Behind a panel
## the 3D renders at the tier's hidden_scale (0.5: only thin margins show)
## and the sky stops updating, so its radiance map is not re-rendered for a
## world nobody sees (G1-06).
func set_world_visible(world_visible: bool) -> void:
	if quality.world_hidden == (not world_visible):
		return
	quality.world_hidden = not world_visible
	quality.apply_scale(get_viewport())
	if planar_reflection != null:
		planar_reflection.set_world_visible(world_visible)
	if world_visible:
		compose(0.0, true)


func is_world_visible() -> bool:
	return not quality.world_hidden


func _apply_quality() -> void:
	var env := _env()
	quality.apply_global(get_viewport() if is_inside_tree() else null, sun_light, moon_light, env.sky if env != null else null)
	_apply_terrain_quality()
	if planar_reflection != null:
		var k := quality.knobs
		planar_reflection.configure(bool(k.get("mirror", false)), float(k.get("mirror_scale", 0.5)),
			float(k.get("mirror_near_hz", 0.0)), float(k.get("mirror_far_hz", 0.0)), float(k.get("mirror_from_m", 0.0)))


## The tier's terrain detail on the terrain material (its chunks share one),
## for the terrain shader to scale its own detail by.
func _apply_terrain_quality() -> void:
	if terrain == null:
		return
	var chunks := terrain.get_chunk_meshes()
	if chunks.is_empty():
		return
	var mat := chunks[0].material_override as ShaderMaterial
	if mat != null:
		mat.set_shader_parameter(RenderQuality.TERRAIN_DETAIL_UNIFORM, float(quality.knobs["terrain_detail"]))


## Compile the terrain shader's pipelines on the scene's first frames,
## before its chunks arrive from the worker thread (ShaderPrewarm).
func _prewarm() -> void:
	if not prewarm_enabled or camera == null or not ResourceLoader.exists(FjordTerrain.TERRAIN_SHADER):
		return
	var terrain_warm := ShaderMaterial.new()
	terrain_warm.shader = load(FjordTerrain.TERRAIN_SHADER)
	prewarm = ShaderPrewarm.warm(self, camera, [terrain_warm] as Array[Material])
	prewarmed_shaders.append(FjordTerrain.TERRAIN_SHADER)


## Sim water conditions (FjordVistaBridge): sea temperature and the worst
## bloom severity (0-1).
func set_water_state(temperature_c: float, algae01: float) -> void:
	_water_temperature_c = temperature_c
	_algae = clampf(algae01, 0.0, 1.0)


## Staging (the harness's goldens): put the clouds' drift (km, the sky's
## cloud_offset) at a fixed point, so a golden's clouds and their shadows do
## not depend on how long the run has played.
func stage_cloud_drift(offset_km: Vector2) -> void:
	_cloud_offset = offset_km


## The values composed last frame (tests and the harness read them).
func get_composed() -> Dictionary:
	return _composed


## Fraction of light from a point `distance_m` away that reaches the camera
## through the current fog (the distance fog; the sea-fog layer is height fog).
func transmittance(distance_m: float) -> float:
	var env := _env()
	if env == null or not env.fog_enabled:
		return 1.0
	return exp(-env.fog_density * distance_m)


## Recompose everything. `snap` skips the smoothing (first frame, staging).
func compose(delta: float, snap: bool = false) -> void:
	if time_system == null:
		return
	var k_slow := 1.0 if snap else 1.0 - exp(-1.5 * delta)
	_sky_time += delta
	var sun_dir := time_system.get_sun_direction()
	var sun_elev := time_system.get_sun_elevation_deg()
	_moon_dir = time_system.get_moon_direction() if snap else _moon_dir.slerp(time_system.get_moon_direction(), 1.0 - exp(-2.0 * delta)).normalized()
	var cloud := weather_system.get_cloud_cover() if weather_system else 0.2
	var density := weather_system.get_cloud_density() if weather_system else 0.2
	var visibility := weather_system.get_visibility_m() if weather_system else 40000.0
	var sea_fog := weather_system.get_sea_fog() if weather_system else 0.0
	var underwater := _is_underwater()
	var distance := camera.get_current_distance() if camera else 900.0

	var c := {
		"sun_elevation": sun_elev,
		"cloud_cover": cloud,
		"visibility_m": visibility,
		"underwater": underwater,
	}
	_compose_mood(delta, snap, sun_dir, sun_elev, cloud, c)
	_apply_sun(sun_dir, cloud, c)
	_apply_moon(sun_elev, cloud, c)
	var snowfall := weather_system.get_snow() if weather_system else 0.0
	_measure_sky(sun_dir, cloud, density, sea_fog, snowfall, c)
	# Behind an opaque panel the sky holds still: an unchanged sky material
	# re-renders no radiance map (set_world_visible). A snap (staging) still
	# writes it.
	var sky_live := snap or not (quality.world_hidden and bool(quality.knobs["hidden_pause"]))
	if sky_live:
		_apply_sky(delta, sun_elev, cloud, density, visibility, sea_fog, k_slow, c)
	else:
		for key in ["aurora_intensity", "haze_amount"]:
			c[key] = _composed.get(key, 0.0)
	_apply_fog(sun_elev, cloud, visibility, sea_fog, underwater, c)
	_apply_image(sun_elev, distance, underwater, c)
	_apply_water(delta, c)
	_apply_weather_drama(delta, sun_dir, sun_elev, cloud, underwater, c)
	_apply_mirror(underwater, c)
	_refresh_probe(delta, snap, sun_elev, cloud, sea_fog)
	c["world_visible"] = not quality.world_hidden
	c["sky_live"] = sky_live
	c["quality"] = quality.tier_name()
	_composed = c


# ── Light ─────────────────────────────────────────────────────────────────────

func _apply_sun(sun_dir: Vector3, cloud: float, c: Dictionary) -> void:
	if sun_light == null:
		return
	_point_light(sun_light, sun_dir)
	var t := sun_transmittance(sun_dir.y)
	var balanced := Vector3(t.x / _white_balance.x, t.y / _white_balance.y, t.z / _white_balance.z)
	var peak := maxf(balanced.x, maxf(balanced.y, balanced.z))
	var clouds := lerpf(1.0, OVERCAST_SUN_FRACTION, cloud)
	var clear_energy := _clear_sun_energy(sun_dir)
	var energy := clear_energy * clouds
	var tint := Color(balanced.x / peak, balanced.y / peak, balanced.z / peak) if peak > 0.0 else Color.BLACK
	# The mood's sunlight (G3-05): Midnattssol's gold, Høst's low amber.
	tint = tint.lerp(_mood_sun_tint(), clampf(float(mood.get("sun_grade", 0.0)), 0.0, 1.0))
	# Through a closed deck the light arrives diffused and near-neutral.
	var neutral := tint.get_luminance()
	sun_light.light_color = tint.lerp(Color(neutral, neutral, neutral), 0.9 * smoothstep(0.8, 0.97, cloud))
	sun_light.light_energy = energy
	sun_light.shadow_enabled = energy > 0.002
	sun_light.shadow_opacity = lerpf(1.0, 0.25, smoothstep(0.35, 1.0, cloud))
	sun_light.light_angular_distance = lerpf(SUN_ANGLE_CLEAR, SUN_ANGLE_OVERCAST, smoothstep(0.3, 1.0, cloud))
	# Behind a deck there is no sun to mirror: the light left is diffuse, so
	# the water shows no glint (a 6-degree source would paint a blown-out
	# ellipse on it).
	sun_light.light_specular = lerpf(1.0, 0.0, smoothstep(0.45, 0.85, cloud))
	c["sun_energy"] = energy
	c["sun_clear_energy"] = clear_energy
	c["sun_color"] = sun_light.light_color
	c["sun_direction"] = sun_dir


func _apply_moon(sun_elev: float, cloud: float, c: Dictionary) -> void:
	if moon_light == null:
		return
	_point_light(moon_light, _moon_dir)
	var moon_elev := rad_to_deg(asin(clampf(_moon_dir.y, -1.0, 1.0)))
	var night := 1.0 - smoothstep(-8.0, 2.0, sun_elev)
	var energy := MOON_ENERGY * (0.15 + 0.85 * time_system.get_moon_illumination()) \
		* smoothstep(-1.0, 4.0, moon_elev) * (1.0 - 0.85 * cloud) * night
	moon_light.light_color = MOON_COLOR
	moon_light.light_energy = energy
	# One shadowed directional at a time: the moon's only while the sun is out.
	moon_light.shadow_enabled = energy > 0.01 and not sun_light.shadow_enabled
	moon_light.shadow_opacity = lerpf(0.85, 0.3, cloud)
	c["moon_energy"] = energy


# ── Sky ───────────────────────────────────────────────────────────────────────

func _apply_sky(delta: float, sun_elev: float, cloud: float, density: float, visibility: float,
		sea_fog: float, k_slow: float, c: Dictionary) -> void:
	var mats := _sky_materials()
	if mats.is_empty():
		return
	var wind := weather_system.get_wind() if weather_system else Vector2(4.0, 0.0)
	# km of drift: the presentation clock runs fast, the clouds a little.
	_cloud_offset += wind * 0.001 * CLOUD_DRIFT * delta
	var aurora: Dictionary = time_system.get_aurora()
	var darkness := smoothstep(-6.0, -14.0, sun_elev)
	var clear := 1.0 - smoothstep(0.55, 0.85, cloud)
	var aurora_target := float(aurora["brightness"]) * darkness * clear
	_aurora = lerpf(_aurora, aurora_target, k_slow)
	if aurora_target <= 0.0 and _aurora < 0.002:
		_aurora = 0.0
	var above_fog_vis := lerpf(visibility, ABOVE_SEA_FOG_VISIBILITY_M, sea_fog)
	var sigma_km := KOSCHMIEDER / maxf(above_fog_vis, 1.0) * 1000.0
	var haze := smoothstep(0.0, 1.0, (sigma_km - 0.12) / 1.6) * clampf(float(mood.get("sky_haze", 1.0)), 0.0, 1.0)
	var terms := sky_fog_terms(cloud, sea_fog)
	var underwater: bool = c.get("underwater", false)
	for i in mats.size():
		var mat: ShaderMaterial = mats[i]
		mat.set_shader_parameter("sky_time", _sky_time)
		mat.set_shader_parameter("cloud_coverage", cloud)
		mat.set_shader_parameter("cloud_density", density)
		mat.set_shader_parameter("cloud_offset", _cloud_offset)
		mat.set_shader_parameter("aurora_intensity", _aurora)
		mat.set_shader_parameter("aurora_offset_km", float(aurora["offset_km"]))
		mat.set_shader_parameter("aurora_kp", float(aurora["kp"]))
		mat.set_shader_parameter("moon_direction", _moon_dir)
		mat.set_shader_parameter("moon_illumination", time_system.get_moon_illumination())
		# haze_color is the fog's light colour (the land's aerial perspective);
		# the sky's own horizon haze may stand a shade brighter (G3-05). Both
		# are source_color uniforms: encoded like the environment's colours.
		mat.set_shader_parameter("haze_color", encoded(c.get("fog_light_color", c["horizon_color"])))
		mat.set_shader_parameter("sky_haze_color", encoded(c["horizon_color"]))
		mat.set_shader_parameter("haze_amount", haze)
		# The zenith sky lights the clouds; one value for every pixel.
		mat.set_shader_parameter("sky_zenith", c["sky_zenith"])
		# Clouds from a reduced-resolution pass come back fogged by the engine;
		# the shader takes that fog out again with these terms (the fog
		# _apply_fog writes, in the engine's sky formula). Underwater the fog
		# replaces the sky outright: the clouds are marched per pixel there.
		mat.set_shader_parameter("sky_fog_k", terms.x)
		mat.set_shader_parameter("sky_fog_scatter", terms.y)
		# The mood's grade (G3-05).
		mat.set_shader_parameter("grade_zenith", EnvironmentMood.vec(mood.get("zenith", Color.WHITE)))
		mat.set_shader_parameter("grade_horizon", EnvironmentMood.vec(mood.get("horizon", Color.WHITE)))
		mat.set_shader_parameter("grade_horizon_sun", EnvironmentMood.vec(mood.get("horizon_sun", Color.WHITE)))
		mat.set_shader_parameter("grade_amount", float(mood.get("sky_grade", 0.0)))
		mat.set_shader_parameter("grade_deck", EnvironmentMood.vec(mood.get("deck", Color.WHITE)))
		mat.set_shader_parameter("grade_deck_amount", float(mood.get("deck_grade", 0.0)))
		mat.set_shader_parameter("grade_deck_value", float(mood.get("deck_value", 1.0)))
		mat.set_shader_parameter("grade_horizon_lift", float(mood.get("horizon_lift", 0.0)))
		mat.set_shader_parameter("cloud_pass", 0 if underwater else int(quality.knobs["cloud_pass"]))
		mat.set_shader_parameter("cloud_steps", int(quality.knobs["cloud_steps"]))
		mat.set_shader_parameter("cloud_octaves", int(quality.knobs["cloud_octaves"]))
		mat.set_shader_parameter("cloud_search", int(quality.knobs["cloud_search"]))
		# The main sky draws per pixel (cloud_pass decides the clouds); the
		# mirror's whole sky, aurora included, goes in its quarter pass.
		var mirror_sky := i > 0
		mat.set_shader_parameter("sky_pass", MIRROR_SKY_PASS if mirror_sky else 0)
		mat.set_shader_parameter("sky_pass_aurora", mirror_sky)
	sky_updates += 1
	c["aurora_intensity"] = _aurora
	c["haze_amount"] = haze


## The engine's fog on the sky (Godot sky.glsl fog_process), for the fog
## _apply_fog writes above water: rgb' = rgb (1 - k) + k fog_light_color +
## s sun_light pow(cos, 8) pi, with k = sky_affect (1 - aerial_perspective)
## and s = sky_affect sun_scatter (0 at or below the engine's 0.001 gate).
static func sky_fog_terms(cloud: float, sea_fog: float) -> Vector2:
	var sky_affect := lerpf(SKY_AFFECT, 0.5, sea_fog)
	var sun_scatter := SUN_SCATTER * (1.0 - cloud * 0.7)
	return Vector2(sky_affect * (1.0 - AERIAL_PERSPECTIVE), sky_affect * sun_scatter if sun_scatter > 0.001 else 0.0)


## Sample the clear sky model (the shader's own atmosphere, twilight and
## night floor) at the zenith and round the horizon, then grey it under
## cloud: the fog colour, the ambient colour, the sky haze and the exposure
## key all come from the sky the player sees.
func _measure_sky(sun_dir: Vector3, cloud: float, density: float, sea_fog: float, snowfall: float, c: Dictionary) -> void:
	var zenith := sky_radiance(Vector3.UP, sun_dir, 6) + twilight_radiance(Vector3.UP, sun_dir) + NIGHT_SKY_ZENITH
	var flat := Vector2(sun_dir.x, sun_dir.z)
	flat = flat.normalized() if flat.length() > 1e-4 else Vector2(1.0, 0.0)
	# Four horizon samples 45 degrees off the sun's azimuth: the fog colour is
	# the sky's, not the sun's glare.
	var horizon := Vector3.ZERO
	for k in 4:
		var a := flat.rotated(PI * 0.25 + PI * 0.5 * k)
		var d := Vector3(a.x, 0.06, a.y).normalized()
		horizon += sky_radiance(d, sun_dir, 6) + twilight_radiance(d, sun_dir) + NIGHT_SKY_HORIZON
	horizon *= 0.25
	# Under cloud the sky is the deck's base: sunlight diffused through it.
	var overcast := smoothstep(0.35, 0.85, cloud)
	var deck := cloud_base_luminance(sun_dir, density) + _luminance(zenith) * lerpf(0.9, 0.35, density)
	var grey := Vector3(0.92, 0.96, 1.0) * deck
	# The mood's hues (G3-05), as the sky shader grades its own pixels.
	var sky_grade := float(mood.get("sky_grade", 0.0))
	zenith = EnvironmentMood.grade_rgb(zenith, mood.get("zenith", Color.WHITE), sky_grade)
	var mood_horizon: Color = (mood.get("horizon", Color.WHITE) as Color).lerp(mood.get("horizon_sun", Color.WHITE), 0.3)
	horizon = EnvironmentMood.grade_rgb(horizon, mood_horizon, sky_grade)
	grey = EnvironmentMood.grade_rgb(grey, mood.get("deck", Color.WHITE), float(mood.get("deck_grade", 0.0)))
	var fog_rgb := horizon.lerp(grey, overcast)
	# A sea-fog layer: bright, nearly white, lit from above.
	fog_rgb = fog_rgb.lerp(Vector3(0.95, 0.97, 1.0) * _luminance(horizon) * 0.8, sea_fog * 0.85)
	# Falling snow scatters the light (and snow on the ground sends it back
	# up into the deck): a brighter, cooler veil than rain.
	var snow_veil := clampf(snowfall, 0.0, 1.0)
	fog_rgb = fog_rgb.lerp(Vector3(0.88, 0.93, 1.0) * _luminance(fog_rgb) * 2.4, snow_veil * 0.85)
	var clear_key := _luminance(zenith) + 0.04 * float(c.get("sun_clear_energy", 0.0)) + 0.15 * _luminance(horizon) \
		+ 0.3 * float(c.get("moon_energy", 0.0))
	# Under a deck the eye adapts part of the way: by the square root of how
	# much darker the deck is than the clear horizon (storms stay darker).
	var deck_ratio := clampf(deck / maxf(_luminance(horizon), 1e-4), 0.02, 1.0)
	var key := clear_key * lerpf(1.0, pow(deck_ratio, 1.0 - OVERCAST_KEEP), overcast)
	# Snowfall is bright: snow on the ground and the deck reflect the light
	# back and forth, so a snowy day reads light grey, not gloomy.
	key /= 1.0 + 2.0 * snow_veil
	# The mood's aerial perspective; the land's fog a shade under the sky
	# behind it (fog_value), so far mountains keep their outline.
	fog_rgb = EnvironmentMood.grade_rgb(fog_rgb, mood.get("fog", Color.WHITE), float(mood.get("fog_grade", 0.0)))
	var fog_light := fog_rgb * clampf(float(mood.get("fog_value", 1.0)), 0.3, 2.0)
	c["sky_zenith"] = zenith
	c["horizon_color"] = Color(fog_rgb.x, fog_rgb.y, fog_rgb.z)
	c["fog_light_color"] = Color(fog_light.x, fog_light.y, fog_light.z)
	c["scene_key"] = key


## Clear-sky scene key for a sun direction (the exposure reference).
static func clear_key(sun_dir: Vector3, sun_energy: float, moon_energy: float) -> float:
	var zenith := sky_radiance(Vector3.UP, sun_dir, 6) + twilight_radiance(Vector3.UP, sun_dir) + NIGHT_SKY_ZENITH
	var flat := Vector2(sun_dir.x, sun_dir.z)
	flat = flat.normalized() if flat.length() > 1e-4 else Vector2(1.0, 0.0)
	var horizon := Vector3.ZERO
	for k in 4:
		var a := flat.rotated(PI * 0.25 + PI * 0.5 * k)
		var d := Vector3(a.x, 0.06, a.y).normalized()
		horizon += sky_radiance(d, sun_dir, 6) + twilight_radiance(d, sun_dir) + NIGHT_SKY_HORIZON
	horizon *= 0.25
	return _luminance(zenith) + 0.04 * sun_energy + 0.15 * _luminance(horizon) + 0.3 * moon_energy


## Clear-sky sun energy for a direction: transmittance, white balance, horizon fade.
func _clear_sun_energy(sun_dir: Vector3) -> float:
	var elev := rad_to_deg(asin(clampf(sun_dir.y, -1.0, 1.0)))
	var t := sun_transmittance(sun_dir.y)
	return SUN_ENERGY * _luminance(t) / _luminance(_white_balance) * smoothstep(-1.2, 1.5, elev)


## Luminance of an overcast deck's base: sunlight at cloud height through the
## deck by two-stream diffuse transmission (sky_shader cloud_layer_low()).
static func cloud_base_luminance(sun_dir: Vector3, density: float) -> float:
	var tau := lerpf(6.0, 60.0, density)
	var t_d := 1.0 / (1.0 + 0.75 * tau * 0.15)
	var sun_in := _transmittance_at(CLOUD_HEIGHT_KM, sun_dir.y) * CLOUD_LIGHT
	return _luminance(sun_in) * maxf(sun_dir.y, 0.0) * t_d


# ── Fog ───────────────────────────────────────────────────────────────────────

func _apply_fog(sun_elev: float, cloud: float, visibility: float, sea_fog: float,
		underwater: bool, c: Dictionary) -> void:
	var env := _env()
	if env == null:
		return
	env.fog_enabled = true
	env.fog_mode = Environment.FOG_MODE_EXPONENTIAL
	if underwater:
		var day := smoothstep(-8.0, 20.0, sun_elev) * lerpf(1.0, 0.4, cloud)
		env.fog_density = KOSCHMIEDER / UNDERWATER_VISIBILITY_M
		env.fog_light_color = UNDERWATER_FOG_COLOR * lerpf(0.15, 1.0, day)
		env.fog_light_energy = 1.0
		env.fog_aerial_perspective = 0.0
		env.fog_sky_affect = 1.0
		env.fog_sun_scatter = 0.0
		env.fog_height_density = 0.0
		c["fog_density"] = env.fog_density
		c["fog_color"] = env.fog_light_color
		return
	var distance_vis := drawn_visibility(lerpf(visibility, ABOVE_SEA_FOG_VISIBILITY_M, sea_fog))
	env.fog_density = KOSCHMIEDER / maxf(distance_vis, 1.0)
	env.fog_light_color = encoded(c.get("fog_light_color", c["horizon_color"]))
	env.fog_light_energy = 1.0
	env.fog_aerial_perspective = AERIAL_PERSPECTIVE
	env.fog_sky_affect = lerpf(SKY_AFFECT, 0.5, sea_fog)
	env.fog_sun_scatter = SUN_SCATTER * (1.0 - cloud * 0.7)
	# Height fog for a sea-fog layer; under a low deck (G3-05, the mood's
	# cloud base) a negative density thickens the fog above the base instead,
	# so the mountains' tops go into the cloud and their slopes stay.
	var base := float(mood.get("cloud_base_m", 0.0))
	var base_fog := float(mood.get("cloud_base_fog", 0.0)) * mist_clear_for(base)
	if sea_fog > 0.01 or base <= 0.0 or base_fog <= 1e-5:
		env.fog_height = SEA_FOG_TOP_M
		env.fog_height_density = SEA_FOG_HEIGHT_DENSITY * sea_fog
	else:
		env.fog_height = base
		env.fog_height_density = -base_fog
	c["cloud_base_m"] = env.fog_height if env.fog_height_density < 0.0 else 0.0
	c["mist_view"] = mist_clear_for(base)
	c["fog_density"] = env.fog_density
	c["fog_color"] = env.fog_light_color


## How much of a low deck's mist the view shows (1 under the deck, 0 well
## above it): a management view keeps its clarity, so as the camera climbs
## past the cloud base toward COAST the mist on the mountains (the height
## fog and MistBanks) thins away instead of whiting out the farm below.
func mist_clear_for(base_m: float) -> float:
	if base_m <= 0.0 or camera == null:
		return 1.0
	var above := camera.global_position.y - base_m
	return 1.0 - smoothstep(MIST_CLEAR_FROM_M, MIST_CLEAR_TO_M, above)


## The visibility the fog is drawn at (m): the weather's, with the
## readability floor (a polynomial smooth maximum with READABLE_VIS_M).
static func drawn_visibility(visibility_m: float) -> float:
	var h := clampf(0.5 + 0.5 * (visibility_m - READABLE_VIS_M) / READABLE_SOFT_M, 0.0, 1.0)
	return lerpf(READABLE_VIS_M, visibility_m, h) + READABLE_SOFT_M * h * (1.0 - h)


# ── Image: exposure, tonemap, glow, ambient, SSAO, shadows, depth of field ────

func _apply_image(sun_elev: float, distance: float, underwater: bool, c: Dictionary) -> void:
	var env := _env()
	if env != null:
		var key: float = c["scene_key"]
		var exposure := clampf(pow(_key_ref / maxf(key, 1e-5), ADAPTATION), 1.0, MAX_EXPOSURE) \
			* pow(2.0, float(mood.get("exposure_ev", 0.0)))
		env.tonemap_mode = Environment.TONE_MAPPER_AGX
		env.tonemap_agx_white = AGX_WHITE
		env.tonemap_agx_contrast = AGX_CONTRAST
		env.tonemap_exposure = exposure
		env.adjustment_enabled = false
		env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
		env.ambient_light_sky_contribution = 0.85
		env.ambient_light_color = encoded(c["horizon_color"])
		# The night boost only once the sky is dark: twilight lights the walls itself.
		env.ambient_light_energy = lerpf(AMBIENT_DAY, AMBIENT_NIGHT, smoothstep(-6.0, -14.0, sun_elev))
		var zoom := zoom_settings(distance)
		env.ssao_enabled = bool(quality.knobs["ssao"])
		env.ssao_radius = zoom.y
		env.glow_enabled = bool(quality.knobs["glow"])
		c["exposure"] = exposure
		c["ssao_radius"] = zoom.y
	var zs := zoom_settings(distance)
	var shadow_distance := zs.x * float(quality.knobs["shadow_distance"])
	if sun_light:
		sun_light.directional_shadow_max_distance = shadow_distance
	if moon_light:
		moon_light.directional_shadow_max_distance = shadow_distance
	c["shadow_distance"] = shadow_distance
	_apply_depth_of_field(distance, zs.z if bool(quality.knobs["dof"]) else 0.0, underwater, c)


func _apply_depth_of_field(distance: float, amount: float, underwater: bool, c: Dictionary) -> void:
	if camera == null:
		return
	var attrs := camera.attributes as CameraAttributesPractical
	if attrs == null:
		return
	# Off at night: the stars and the aurora must stay points and rays.
	var night := smoothstep(-2.0, -8.0, float(c.get("sun_elevation", 0.0)))
	amount *= 1.0 - night
	# The tier's reach: past it the blur fades out and the pass is skipped
	# (at FJORD and COAST the haze already softens the far fjord as much).
	var reach := float(quality.knobs["dof_reach"])
	amount *= 1.0 - smoothstep(reach * 0.6, reach, distance)
	var on := not underwater and amount > 0.001
	attrs.dof_blur_far_enabled = on
	attrs.dof_blur_near_enabled = on and distance < 500.0 and bool(quality.knobs["dof_near"])
	# Sharp around the focus; the far fjord softens gently.
	attrs.dof_blur_far_distance = distance * 1.8
	attrs.dof_blur_far_transition = distance * 3.0
	attrs.dof_blur_near_distance = distance * 0.45
	attrs.dof_blur_near_transition = distance * 0.35
	attrs.dof_blur_amount = amount if on else 0.0
	c["dof_amount"] = attrs.dof_blur_amount


## (shadow max distance, SSAO radius, DOF amount) for a camera distance,
## interpolated in log distance between FARM, FJORD and COAST.
static func zoom_settings(distance: float) -> Vector3:
	var d := clampf(distance, ZOOM_TABLE[0][0], ZOOM_TABLE[ZOOM_TABLE.size() - 1][0])
	for i in range(ZOOM_TABLE.size() - 1):
		var a: Array = ZOOM_TABLE[i]
		var b: Array = ZOOM_TABLE[i + 1]
		if d <= b[0]:
			var t := (log(d) - log(a[0])) / (log(b[0]) - log(a[0]))
			return Vector3(lerpf(a[1], b[1], t), lerpf(a[2], b[2], t), lerpf(a[3], b[3], t))
	var last: Array = ZOOM_TABLE[ZOOM_TABLE.size() - 1]
	return Vector3(last[1], last[2], last[3])


# ── Planar mirror (G2-03) ─────────────────────────────────────────────────────

## The mirror's environment follows the main one (composed above this
## frame): the same sky, fog colour and ambient, a share of the fog (see
## MIRROR_FOG_SHARE), no tone mapping, glow or SSAO (the mirror holds scene
## radiance; the main pass exposes and tone-maps the water once). No mirror
## underwater or once the wind roughens every bit of the water.
func _apply_mirror(underwater: bool, c: Dictionary) -> void:
	if planar_reflection == null:
		return
	var wind := weather_system.get_wind_speed_ms() if weather_system else 4.0
	var wanted := not underwater and wind < MIRROR_MAX_WIND_MS
	planar_reflection.set_wanted(wanted)
	c["mirror_wanted"] = wanted
	var env := _env()
	var m := planar_reflection.environment
	if env == null or m == null or not wanted:
		return
	m.background_mode = Environment.BG_SKY
	m.sky = _ensure_mirror_sky(env)
	m.background_energy_multiplier = env.background_energy_multiplier
	m.ambient_light_source = env.ambient_light_source
	m.ambient_light_sky_contribution = env.ambient_light_sky_contribution
	m.ambient_light_color = env.ambient_light_color
	m.ambient_light_energy = env.ambient_light_energy
	m.reflected_light_source = env.reflected_light_source
	m.tonemap_mode = Environment.TONE_MAPPER_LINEAR
	m.tonemap_exposure = 1.0
	m.glow_enabled = false
	m.ssao_enabled = false
	m.ssil_enabled = false
	m.ssr_enabled = false
	m.sdfgi_enabled = false
	m.volumetric_fog_enabled = false
	m.adjustment_enabled = false
	m.fog_enabled = env.fog_enabled
	m.fog_mode = env.fog_mode
	m.fog_density = env.fog_density * MIRROR_FOG_SHARE
	m.fog_light_color = env.fog_light_color
	m.fog_light_energy = env.fog_light_energy
	m.fog_aerial_perspective = env.fog_aerial_perspective
	m.fog_sky_affect = env.fog_sky_affect
	m.fog_sun_scatter = env.fog_sun_scatter
	m.fog_height = env.fog_height
	m.fog_height_density = env.fog_height_density * MIRROR_FOG_SHARE
	# The mirror's own sun and moon: the scene's, without shadows.
	_copy_light(sun_light, planar_reflection.sun)
	_copy_light(moon_light, planar_reflection.moon)


static func _copy_light(from: DirectionalLight3D, to: DirectionalLight3D) -> void:
	if from == null or to == null:
		return
	to.global_transform = Transform3D(from.global_transform.basis, to.global_transform.origin)
	to.light_color = from.light_color
	to.light_energy = from.light_energy
	to.light_angular_distance = from.light_angular_distance
	to.light_specular = from.light_specular
	to.visible = from.visible


## The mirror's own sky: the main sky's shader and uniforms (written with
## the main ones every frame, _apply_sky), drawn in the quarter-resolution
## pass (sky_pass), with a small radiance map for the mirror's own ambient
## light. Sharing the main Sky would draw the full per-pixel sky a second
## time: 1.9 ms of a 4.7 ms mirror at 1440p FJORD (P0-04 harness, A/B).
func _ensure_mirror_sky(env: Environment) -> Sky:
	if _mirror_sky != null or env.sky == null or not (env.sky.sky_material is ShaderMaterial):
		return _mirror_sky if _mirror_sky != null else env.sky
	_mirror_sky = Sky.new()
	_mirror_sky.sky_material = (env.sky.sky_material as ShaderMaterial).duplicate()
	_mirror_sky.radiance_size = MIRROR_RADIANCE_SIZE
	_mirror_sky.process_mode = Sky.PROCESS_MODE_INCREMENTAL
	return _mirror_sky


# ── Reflection probe ──────────────────────────────────────────────────────────

func _refresh_probe(delta: float, snap: bool, sun_elev: float, cloud: float, sea_fog: float) -> void:
	if reflection_probe == null:
		return
	_probe_timer += delta
	var terrain_ready := terrain == null or not terrain.has_method("is_ready") or terrain.is_ready()
	var terrain_arrived := terrain_ready and not _terrain_was_ready
	_terrain_was_ready = terrain_ready
	# Nothing before the terrain is up: a capture renders the scene six times
	# and would stall the chunk uploads (and capture a fjord without walls).
	if not terrain_ready:
		return
	var signature := "%d|%d|%d" % [roundi(sun_elev / PROBE_SUN_STEP), roundi(cloud / PROBE_CLOUD_STEP), roundi(sea_fog)]
	if terrain_arrived or (signature != _probe_signature and (snap or _probe_timer >= PROBE_MIN_INTERVAL)):
		_probe_signature = signature
		_probe_timer = 0.0
		_probe_due = PROBE_SETTLE
	if _probe_due < 0.0:
		return
	_probe_due -= delta
	if _probe_due <= 0.0:
		_probe_due = -1.0
		# Setting the mode again queues one capture on the next frame.
		reflection_probe.update_mode = ReflectionProbe.UPDATE_ONCE
		probe_captures += 1


# ── Water ─────────────────────────────────────────────────────────────────────

func _apply_water(delta: float, c: Dictionary) -> void:
	var mat := water_material()
	if mat == null:
		return
	_apply_water_bake(mat)
	if not _ensure_sea_bridge():
		return
	var wind := weather_system.get_wind_speed_ms() if weather_system else 4.0
	if absf(wind - _sea_wind) > SEA_WIND_STEP or sea_state.is_empty():
		_sea_wind = wind
		_derive_sea(wind)
	# Phases advance at each component's deep-water frequency, integrated
	# here so a change of sea never makes the waves jump.
	_sea_time += delta
	var amp := PackedVector4Array()
	var a_sh: PackedFloat32Array = sea_state["sheltered_amplitudes"]
	var a_ex: PackedFloat32Array = sea_state["exposed_amplitudes"]
	for i in _comp_k.size():
		var omega := sqrt(GRAVITY * _comp_k[i])
		_comp_phase[i] = fmod(_comp_phase[i] + omega * delta, TAU)
		var band := _comp_band[i]
		amp.append(Vector4(a_sh[band] * _comp_weight[i], a_ex[band] * _comp_weight[i], _comp_phase[i], TAU / _comp_k[i]))
	mat.set_shader_parameter("wave_k", sea_state["wave_k"])
	mat.set_shader_parameter("wave_amp", amp)
	wave_field.set_waves(sea_state["wave_k"], amp)
	mat.set_shader_parameter("wave_peak", sea_state["wave_peak"])
	# Dead calm mirrors crisply; a breeze's ripples break the mirror up.
	mat.set_shader_parameter("planar_distort", lerpf(MIRROR_DISTORT_CALM, MIRROR_DISTORT_BREEZE, smoothstep(1.0, 6.0, wind)))
	mat.set_shader_parameter("whitecaps", sea_state["whitecaps"])
	mat.set_shader_parameter("whitecap_threshold", whitecap_threshold(float(sea_state["whitecaps"])))
	mat.set_shader_parameter("ripple_strength", clampf(RIPPLE_MIN + RIPPLE_PER_MS * wind, RIPPLE_MIN, RIPPLE_MAX))
	mat.set_shader_parameter("sea_height", Vector2(sea_state["sheltered_hs"], sea_state["exposed_hs"]))
	mat.set_shader_parameter("sea_peak", Vector2(sea_state["sheltered_peak_m"], sea_state["exposed_peak_m"]))
	mat.set_shader_parameter("sea_direction", sea_state["direction"])
	mat.set_shader_parameter("sea_time", _sea_time)
	mat.set_shader_parameter("temperature_factor", clampf((_water_temperature_c - 2.0) / 18.0, 0.0, 1.0))
	mat.set_shader_parameter("algae_density", _algae)
	# Seen from below, Snell's window shows the day's sky light.
	var horizon: Color = c.get("horizon_color", Color(0.5, 0.6, 0.7))
	var day := smoothstep(-8.0, 20.0, float(c.get("sun_elevation", 30.0)))
	mat.set_shader_parameter("underside_light", Vector3(horizon.r, horizon.g, horizon.b) * lerpf(0.05, 0.9, day))
	c["sea_hs_sheltered"] = sea_state["sheltered_hs"]
	c["sea_hs_exposed"] = sea_state["exposed_hs"]


## The water material (null without a water surface).
func water_material() -> ShaderMaterial:
	if water_grid == null:
		return null
	return water_grid.material


## Hand the terrain's water bake (depth, shore distance, exposure, seabed
## slope) to the water once the terrain build lands.
func _apply_water_bake(mat: ShaderMaterial) -> void:
	if terrain == null or terrain.water_bake_texture == null or terrain.water_bake_texture == _bake_applied:
		return
	_bake_applied = terrain.water_bake_texture
	var size := terrain.terrain_size
	var texels := float(terrain.terrain_resolution)
	mat.set_shader_parameter("water_bake", _bake_applied)
	mat.set_shader_parameter("bake_origin", -size * 0.5)
	mat.set_shader_parameter("bake_spacing", size.x / (texels - 1.0))
	mat.set_shader_parameter("bake_texels", texels)
	mat.set_shader_parameter("has_bake", 1.0)
	wave_field.set_bake(terrain.get_water_bake_image(), -size * 0.5, size.x / (texels - 1.0), texels)
	_sea_wind = -1.0


func _ensure_sea_bridge() -> bool:
	if _sea_bridge != null:
		return true
	var script: Script = load(SEA_BRIDGE_PATH) if ResourceLoader.exists(SEA_BRIDGE_PATH) else null
	if script == null or not script.can_instantiate():
		return false
	_sea_bridge = script.new()
	var bands: PackedFloat32Array = _sea_bridge.call("BandWavelengths")
	for band in bands.size():
		var spread := BAND_SPREAD_BASE_DEG + BAND_SPREAD_PER_BAND_DEG * band
		for j in COMPONENTS_PER_BAND:
			_comp_band.append(band)
			_comp_k.append(TAU / bands[band] * COMPONENT_DETUNE[j])
			_comp_heading.append(BAND_HEADING_DEG[band % BAND_HEADING_DEG.size()] + (j - 0.5 * (COMPONENTS_PER_BAND - 1)) * spread)
			_comp_weight.append(COMPONENT_WEIGHT[j])
			# Fixed, well-mixed starting phases (golden-angle steps).
			_comp_phase.append(fmod((band * COMPONENTS_PER_BAND + j) * 2.39996323, TAU))
	return true


## The wind sea of the sheltered basin (the terrain's fetch) and of the open
## coast for a wind, as the shader's wave bands.
func _derive_sea(wind: float) -> void:
	var fetch := terrain.sheltered_fetch_m if terrain != null else 3000.0
	var sheltered: Dictionary = _sea_bridge.call("Sea", wind, fetch)
	var exposed: Dictionary = _sea_bridge.call("Sea", wind, _sea_bridge.call("ExposedFetchM"))
	var direction := terrain.sea_direction if terrain != null else Vector2(0.3, 1.0).normalized()
	var a_sh: PackedFloat32Array = sheltered["amplitudes"]
	var a_ex: PackedFloat32Array = exposed["amplitudes"]
	var steepness := 0.0
	for i in _comp_k.size():
		steepness += _comp_k[i] * _comp_weight[i] * maxf(a_sh[_comp_band[i]], a_ex[_comp_band[i]])
	var chop := minf(CHOPPINESS, WAVE_FOLD_LIMIT / maxf(steepness, 1e-4))
	var shape := PackedVector4Array()
	var peak := PackedVector2Array()
	var sheltered_peak := maxf(float(sheltered["peak_wavelength"]), 0.5)
	var exposed_peak := maxf(float(exposed["peak_wavelength"]), 0.5)
	for i in _comp_k.size():
		var heading := direction.rotated(deg_to_rad(_comp_heading[i]))
		shape.append(Vector4(heading.x * _comp_k[i], heading.y * _comp_k[i], _comp_k[i], chop))
		var wavelength := TAU / _comp_k[i]
		peak.append(Vector2(peak_weight(wavelength, sheltered_peak), peak_weight(wavelength, exposed_peak)))
	sea_state = {
		"wind_ms": wind,
		"fetch_m": fetch,
		"direction": direction,
		"sheltered_hs": float(sheltered["hs"]),
		"sheltered_tp": float(sheltered["tp"]),
		"sheltered_amplitudes": a_sh,
		"exposed_hs": float(exposed["hs"]),
		"exposed_tp": float(exposed["tp"]),
		"exposed_amplitudes": a_ex,
		"sheltered_peak_m": maxf(float(sheltered["peak_wavelength"]), 0.5),
		"exposed_peak_m": maxf(float(exposed["peak_wavelength"]), 0.5),
		"whitecaps": float(sheltered["whitecaps"]),
		"wave_k": shape,
		"wave_peak": peak,
		"choppiness": chop,
	}


## A wave component's weight round the spectral peak (G2-03: whitecaps break
## on the crests of the waves there): a Gaussian in octaves of wavelength,
## 1 at the peak, e^-1.5 an octave off.
static func peak_weight(wavelength: float, peak_wavelength: float) -> float:
	var octaves := log(wavelength / peak_wavelength) / log(2.0)
	return exp(-octaves * octaves * PEAK_BAND_SHARPNESS)


## Shader threshold for a whitecap cover (fraction of the surface).
static func whitecap_threshold(cover: float) -> float:
	return WHITECAP_Z_SCALE * normal_quantile(1.0 - cover) + WHITECAP_Z_OFFSET


## Standard-normal quantile: the z with P(Z < z) = p (Abramowitz & Stegun
## 26.2.23, |error| < 4.5e-4).
static func normal_quantile(p: float) -> float:
	var q := clampf(p, 1e-9, 1.0 - 1e-9)
	var tail := minf(q, 1.0 - q)
	var t := sqrt(-2.0 * log(tail))
	var z := t - (2.515517 + 0.802853 * t + 0.010328 * t * t) / (1.0 + 1.432788 * t + 0.189269 * t * t + 0.001308 * t * t * t)
	return z if q > 0.5 else -z


## Highest crest (m) the drawn waves can stack to where the open-sea
## exposure is `exposure` (the water bake's B channel): the sum of every
## component's amplitude there, all crests at one point.
func max_crest_at(exposure: float) -> float:
	if sea_state.is_empty():
		return 0.0
	var a_sh: PackedFloat32Array = sea_state["sheltered_amplitudes"]
	var a_ex: PackedFloat32Array = sea_state["exposed_amplitudes"]
	var total := 0.0
	for i in _comp_k.size():
		total += lerpf(a_sh[_comp_band[i]], a_ex[_comp_band[i]], exposure) * _comp_weight[i]
	return total


# ── Moods and weather drama (G3-05) ───────────────────────────────────────────

## Weigh the moods from this frame's inputs (the sim's weather and calendar
## through the weather and the clock) and blend their profiles. The weights
## follow their targets at MOOD_RATE; a snap (staging) takes them at once.
func _compose_mood(delta: float, snap: bool, sun_dir: Vector3, sun_elev: float, cloud: float, c: Dictionary) -> void:
	var inputs := {
		"sun_elevation": sun_elev,
		"sun_direction": sun_dir,
		"day_of_year": time_system.get_day_of_year(),
		"cloud": cloud,
		"rain": (weather_system.get_rain() + weather_system.get_snow()) if weather_system else 0.0,
		"wind": weather_system.get_wind_speed_ms() if weather_system else 4.0,
	}
	var target := EnvironmentMood.weights(inputs)
	if snap or mood_weights.size() != target.size():
		mood_weights = target
	else:
		var k := 1.0 - exp(-MOOD_RATE * delta)
		for i in target.size():
			mood_weights[i] = lerpf(mood_weights[i], target[i], k)
	mood = EnvironmentMood.blend(mood_weights)
	var by_name := {}
	for i in mood_weights.size():
		by_name[EnvironmentMood.NAMES[i]] = mood_weights[i]
	c["mood"] = EnvironmentMood.NAMES[EnvironmentMood.dominant(mood_weights)]
	c["mood_weights"] = by_name


## The mood's sunlight as a light colour (brightest channel 1).
func _mood_sun_tint() -> Color:
	var sun: Color = mood.get("sun", Color.WHITE)
	var srgb := sun.linear_to_srgb()
	var peak := maxf(srgb.r, maxf(srgb.g, srgb.b))
	return Color(srgb.r / peak, srgb.g / peak, srgb.b / peak) if peak > 0.0 else Color.WHITE


## The mood's water body, the frost smoke, the clouds' shadows, the pens'
## lamps and the storm's sway.
func _apply_weather_drama(delta: float, sun_dir: Vector3, sun_elev: float, cloud: float, underwater: bool, c: Dictionary) -> void:
	var mat := water_material()
	var wind := weather_system.get_wind() if weather_system else Vector2(4.0, 0.0)
	if mat != null:
		var scatter: Color = mood.get("water", Color(0.0022, 0.0085, 0.0105))
		mat.set_shader_parameter("deep_scatter", Vector3(scatter.r, scatter.g, scatter.b))
		_apply_frost_smoke(mat, delta, sun_dir, sun_elev, wind, c)
	_apply_cloud_shadows(mat, sun_dir, sun_elev, cloud, c)
	_apply_lamps(mat, sun_elev, cloud, c)
	_apply_mist(delta, underwater, wind.length(), c)
	if _storm_sway != null:
		_storm_sway.set_wind(0.0 if underwater else wind.length())
		c["sway_level"] = _storm_sway.level


func _apply_frost_smoke(mat: ShaderMaterial, delta: float, sun_dir: Vector3, sun_elev: float, wind: Vector2, c: Dictionary) -> void:
	var sim_smoke := weather_system.get_frost_smoke() if weather_system else 0.0
	var smoke := EnvironmentMood.frost_smoke_shown(sim_smoke, sun_elev)
	_smoke_drift += wind * SMOKE_DRIFT * delta
	# The smoke scatters the sun (low and warm on a frost morning) and the sky.
	var sun_color: Color = c.get("sun_color", Color.WHITE)
	var sun_in := Vector3(sun_color.r, sun_color.g, sun_color.b) * float(c.get("sun_energy", 0.0)) * SMOKE_SUN
	var sky: Color = c.get("horizon_color", Color(0.5, 0.55, 0.6))
	var sky_in := Vector3(sky.r, sky.g, sky.b) * SMOKE_SKY
	var tint := EnvironmentMood.vec(EnvironmentMood.SEA_SMOKE.srgb_to_linear())
	var light := (sun_in * maxf(sun_dir.y, 0.0) * 0.5 + sun_in * 0.5 + sky_in) * tint
	mat.set_shader_parameter("frost_smoke", smoke)
	mat.set_shader_parameter("frost_smoke_light", light)
	mat.set_shader_parameter("frost_smoke_drift", _smoke_drift)
	mat.set_shader_parameter("frost_smoke_wind", wind.normalized() if wind.length() > 0.05 else Vector2(1.0, 0.0))
	c["frost_smoke"] = smoke


## Cloud shadows: strongest under broken cloud with the sun well up, none
## under a clear sky or a closed deck. The shadows drift with the sky's low
## clouds (the sky shader's cloud_offset, km).
func _apply_cloud_shadows(mat: ShaderMaterial, sun_dir: Vector3, sun_elev: float, cloud: float, c: Dictionary) -> void:
	var broken := smoothstep(CLOUD_SHADOW_FROM, CLOUD_SHADOW_PEAK, cloud) * (1.0 - smoothstep(CLOUD_SHADOW_CLOSE, CLOUD_SHADOW_TO, cloud))
	var strength := broken * smoothstep(SHADOW_SUN_FROM, SHADOW_SUN_FULL, sun_elev)
	if bool(c.get("underwater", false)) or not bool(quality.knobs["cloud_shadows"]):
		strength = 0.0
	var focus := camera.get_focus_point() if camera != null else Vector3.ZERO
	var drift := -_cloud_offset * 1000.0
	if cloud_shadows != null:
		cloud_shadows.update(sun_dir, cloud, strength, drift, focus)
		strength = cloud_shadows.strength
	else:
		strength = 0.0
	if mat != null:
		var on := cloud_shadows != null and cloud_shadows.get_map() != null and strength > 0.001
		mat.set_shader_parameter("cloud_shadow_strength", strength if on else 0.0)
		if on:
			mat.set_shader_parameter("cloud_shadow_map", cloud_shadows.get_map())
			mat.set_shader_parameter("cloud_shadow_origin", cloud_shadows.map_origin)
			mat.set_shader_parameter("cloud_shadow_axis_u", cloud_shadows.map_axis_u)
			mat.set_shader_parameter("cloud_shadow_axis_v", cloud_shadows.map_axis_v)
	c["cloud_shadow_strength"] = strength
	c["cloud_shadow_drift"] = drift


## Low cloud on the walls (MistBanks) from the mood's cloud base up, lit by
## the deck's light a shade over the fog's (cloud, not the air in front of
## it), built once the terrain stands.
func _apply_mist(delta: float, underwater: bool, wind_ms: float, c: Dictionary) -> void:
	if mist_banks == null:
		return
	if not mist_banks.built and terrain != null and terrain.is_ready():
		mist_banks.build(terrain)
	var shown := 0.0 if underwater else float(mood.get("mist", 0.0)) * float(c.get("mist_view", 1.0))
	var light: Color = c.get("horizon_color", Color(0.5, 0.55, 0.6))
	mist_banks.update(float(c.get("cloud_base_m", 0.0)), shown, light * MIST_LIGHT, wind_ms, delta)
	c["mist"] = mist_banks.amount


## The pens' lamps burn after dark in the lighting season (PenLamps); the
## water draws their glow at a fixed brightness on screen (divided by the
## night's exposure, which would otherwise blow it out), the underwater
## lights light the nets.
func _apply_lamps(mat: ShaderMaterial, sun_elev: float, cloud: float, c: Dictionary) -> void:
	var on := 0.0
	var entries := PackedVector4Array()
	if pen_lamps != null:
		on = PenLamps.night_for(sun_elev, cloud) * PenLamps.season_for(time_system.get_day_of_year()) \
			* (1.0 if pen_lamps.enabled else 0.0)
		if on > 0.001:
			entries = pen_lamps.lamp_entries()
		for i in pen_lamps.lights.size():
			var light := pen_lamps.lights[i]
			light.visible = on > 0.001 and i < entries.size()
			light.light_energy = LAMP_LIGHT_ENERGY * on
	var exposure := maxf(float(c.get("exposure", 1.0)), 1e-3)
	var filtered := LAMP_WHITE * Vector3(exp(-WATER_ABSORPTION.x * PenLamps.LAMP_DEPTH_M),
		exp(-WATER_ABSORPTION.y * PenLamps.LAMP_DEPTH_M), exp(-WATER_ABSORPTION.z * PenLamps.LAMP_DEPTH_M))
	filtered /= maxf(filtered.x, maxf(filtered.y, filtered.z))
	if mat != null:
		var padded := PackedVector4Array(entries)
		padded.resize(PenLamps.MAX_LAMPS)
		mat.set_shader_parameter("lamp_glow", padded)
		mat.set_shader_parameter("lamp_count", entries.size())
		mat.set_shader_parameter("lamp_light", filtered * (LAMP_GLOW_ON_SCREEN * on / exposure))
	c["lamps_on"] = on
	c["lamp_count"] = entries.size()


# ── Static setup (once) ───────────────────────────────────────────────────────

func _static_setup() -> void:
	var env := _env()
	if env != null:
		env.background_mode = Environment.BG_SKY
		env.reflected_light_source = Environment.REFLECTION_SOURCE_SKY
		env.glow_enabled = true
		env.glow_normalized = true
		env.glow_blend_mode = Environment.GLOW_BLEND_MODE_SCREEN
		env.glow_intensity = GLOW_INTENSITY
		env.glow_strength = 1.0
		env.glow_bloom = 0.0
		env.glow_hdr_threshold = GLOW_THRESHOLD
		for level in range(1, 8):
			env.set("glow_levels/%d" % level, 1.0 if level >= 3 and level <= 5 else 0.0)
		env.ssao_intensity = 1.2
		env.ssao_power = 1.5
		env.ssao_light_affect = 0.0
		env.sdfgi_enabled = false
		env.volumetric_fog_enabled = false
		if env.sky != null:
			env.sky.process_mode = Sky.PROCESS_MODE_INCREMENTAL
	if reflection_probe != null:
		reflection_probe.ambient_mode = ReflectionProbe.AMBIENT_DISABLED


# ── Helpers ───────────────────────────────────────────────────────────────────

## A scene-linear colour as the engine's colour inputs take it. The
## environment's fog and ambient colours and the shaders' source_color
## uniforms are sRGB-encoded: the renderer linearises them. Everything the
## director measures is scene-linear radiance, so it is encoded on the way
## in and arrives unchanged (G3-05: written raw, a linear 0.18 arrived as
## 0.027, and every overcast, storm, dusk and night fog came out 4 to 10
## times too dark, the far land and sea sinking into it).
static func encoded(linear: Color) -> Color:
	return Color(maxf(linear.r, 0.0), maxf(linear.g, 0.0), maxf(linear.b, 0.0), linear.a).linear_to_srgb()


func _env() -> Environment:
	if world_environment == null:
		return null
	return world_environment.environment


func _sky_material() -> ShaderMaterial:
	var env := _env()
	if env == null or env.sky == null:
		return null
	return env.sky.sky_material as ShaderMaterial


## The main sky's material, then the planar mirror's (while it is wanted).
func _sky_materials() -> Array[ShaderMaterial]:
	var out: Array[ShaderMaterial] = []
	var main := _sky_material()
	if main == null:
		return out
	out.append(main)
	if _mirror_sky != null and planar_reflection != null and planar_reflection.environment.sky == _mirror_sky:
		out.append(_mirror_sky.sky_material as ShaderMaterial)
	return out


func _is_underwater() -> bool:
	if camera == null:
		return false
	var level := terrain.water_level if terrain != null else 0.0
	return camera.global_position.y < level


static func _point_light(light: DirectionalLight3D, toward: Vector3) -> void:
	var forward := -toward.normalized()
	var up := Vector3.UP if absf(forward.y) < 0.999 else Vector3.BACK
	light.global_transform = Transform3D(Basis.looking_at(forward, up), light.global_position)


static func _luminance(v: Vector3) -> float:
	return 0.2126 * v.x + 0.7152 * v.y + 0.0722 * v.z


## Schüler's approximation of the Chapman function (see sky_shader.gdshader).
static func _chapman(x_scale: float, h_norm: float, cos_z: float) -> float:
	var c := sqrt(x_scale + h_norm)
	if cos_z >= 0.0:
		return c / (c * cos_z + 1.0) * exp(-h_norm)
	var x0 := sqrt(maxf(1.0 - cos_z * cos_z, 0.0)) * (x_scale + h_norm)
	var c0 := sqrt(x0)
	return 2.0 * c0 * exp(minf(x_scale - x0, 60.0)) - c / (1.0 - c * cos_z) * exp(-h_norm)


## The shader's twilight(): blue upper sky and afterglow after sunset.
static func twilight_radiance(dir: Vector3, sun_dir: Vector3) -> Vector3:
	var e := rad_to_deg(asin(clampf(sun_dir.y, -1.0, 1.0)))
	var level := 0.035 * exp(minf(e, 0.0) / 2.6) * smoothstep(2.0, -1.0, e)
	if level <= 0.0:
		return Vector3.ZERO
	var up := clampf(dir.y, 0.0, 1.0)
	var flat_dir := Vector2(dir.x, dir.z + 1e-5).normalized()
	var flat_sun := Vector2(sun_dir.x, sun_dir.z + 1e-5).normalized()
	var toward := flat_dir.dot(flat_sun) * 0.5 + 0.5
	var glow := pow(toward, 3.0) * exp(-up * 7.0) * smoothstep(-11.0, -1.0, e)
	var belt := pow(1.0 - toward, 3.0) * exp(-absf(up - 0.08) * 25.0) * smoothstep(-6.0, -1.0, e)
	var zenith_blue := Vector3(0.16, 0.30, 0.85)
	return (zenith_blue * (0.65 + 0.35 * (1.0 - up)) + Vector3(1.0, 0.42, 0.16) * glow * 1.6 + Vector3(0.75, 0.45, 0.55) * belt * 0.5) * level


## Skylight from the sun in a direction (scene-linear, before clouds): a port
## of the shader's atmosphere() so fog, ambient and exposure follow the same
## sky the player sees. `steps` view samples.
static func sky_radiance(dir: Vector3, sun_dir: Vector3, steps: int = 8) -> Vector3:
	var r0 := R_EARTH + OBSERVER_H
	var mu := maxf(dir.y, 0.002)
	var view := Vector3(dir.x, mu, dir.z).normalized()
	var t_max := -r0 * mu + sqrt(maxf(r0 * r0 * (mu * mu - 1.0) + R_TOP * R_TOP, 0.0))
	var sum_r := Vector3.ZERO
	var sum_m := Vector3.ZERO
	var od_r := 0.0
	var od_m := 0.0
	for i in steps:
		var a := float(i) / steps
		var b := float(i + 1) / steps
		var t0 := t_max * a * a
		var t1 := t_max * b * b
		var t := 0.5 * (t0 + t1)
		var ds := t1 - t0
		var p := Vector3(0.0, r0, 0.0) + view * t
		var r := p.length()
		var h := r - R_EARTH
		var d_r := exp(-h / H_RAYLEIGH) * ds
		var d_m := exp(-h / H_MIE) * ds
		od_r += d_r * 0.5
		od_m += d_m * 0.5
		var t_view := _exp3(-(BETA_R * od_r + Vector3.ONE * (BETA_M_EXT * od_m)))
		var t_sun := _transmittance_at(h, (p / r).dot(sun_dir))
		sum_r += t_view * t_sun * d_r
		sum_m += t_view * t_sun * d_m
		od_r += d_r * 0.5
		od_m += d_m * 0.5
	var cos_theta := view.dot(sun_dir)
	var phase_r := 3.0 / (16.0 * PI) * (1.0 + cos_theta * cos_theta)
	var g := MIE_G
	var phase_m := (1.0 - g * g) / (4.0 * PI * pow(maxf(1.0 + g * g - 2.0 * g * cos_theta, 1e-4), 1.5))
	var single := BETA_R * sum_r * phase_r + sum_m * (BETA_M * phase_m)
	var multi := (BETA_R * sum_r + sum_m * BETA_M) * (MULTI_SCATTER / (4.0 * PI) * (0.35 + 0.65 * mu))
	return (single + multi) * SKY_SCALE * (0.7 + 0.5 * mu)


static func _exp3(v: Vector3) -> Vector3:
	return Vector3(exp(v.x), exp(v.y), exp(v.z))


static func _transmittance_at(h_km: float, cos_z: float) -> Vector3:
	var ch_r := _chapman(R_EARTH / H_RAYLEIGH, h_km / H_RAYLEIGH, cos_z)
	var ch_m := _chapman(R_EARTH / H_MIE, h_km / H_MIE, cos_z)
	var s := (R_EARTH + h_km) / OZONE_RADIUS
	var ozone_mass := 1.0 / sqrt(maxf(1.0 - s * s * (1.0 - cos_z * cos_z), 0.02))
	if cos_z < 0.0 and ch_r > 200.0:
		ozone_mass = 0.0
	var tau := BETA_R * H_RAYLEIGH * ch_r + Vector3.ONE * (BETA_M_EXT * H_MIE * ch_m) + OZONE_ZENITH * ozone_mass
	return _exp3(-tau)


## Transmittance of sunlight to the observer for the sun's height cos_z
## (the y of its direction): the same atmosphere the sky shader draws.
static func sun_transmittance(cos_z: float) -> Vector3:
	return _transmittance_at(OBSERVER_H, cos_z)
