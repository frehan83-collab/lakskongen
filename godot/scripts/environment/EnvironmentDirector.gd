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
## The shader's whitecap field (a crest gate plus breaker noise and foam
## lace) is not a standard normal: its threshold is mapped from the normal
## quantile of Monahan's share so that the drawn cover matches it.
## Calibrated top-down over the basin at 9, 14 and 20 m/s (W 0.7 %, 3.1 %,
## 10.5 %): the cover hits W at thresholds 2.49, 2.24 and 1.66; a least-
## squares line through them.
const WHITECAP_Z_SCALE := 0.68
const WHITECAP_Z_OFFSET := 0.88
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

var _water_temperature_c := 8.0
var _algae := 0.0
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
## The sea composed last: hs/tp/amplitudes for the sheltered basin and the
## open coast, whitecaps (tests read it).
var sea_state: Dictionary = {}
## The drawn water on the CPU (G2-03): handed the very arrays the shader's
## wave uniforms get, every frame. WaveField.active points at it while this
## director is in the tree; PenCage and VesselModel float on it.
var wave_field := WaveField.new()


func _enter_tree() -> void:
	WaveField.active = wave_field


func _exit_tree() -> void:
	if WaveField.active == wave_field:
		WaveField.active = null


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
	wave_field.bind_grid(water_grid)
	compose(0.0, true)


func _process(delta: float) -> void:
	compose(delta)


## Sim water conditions (FjordVistaBridge): sea temperature and the worst
## bloom severity (0-1).
func set_water_state(temperature_c: float, algae01: float) -> void:
	_water_temperature_c = temperature_c
	_algae = clampf(algae01, 0.0, 1.0)


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
	_apply_sun(sun_dir, cloud, c)
	_apply_moon(sun_elev, cloud, c)
	var snowfall := weather_system.get_snow() if weather_system else 0.0
	_measure_sky(sun_dir, cloud, density, sea_fog, snowfall, c)
	_apply_sky(delta, sun_elev, cloud, density, visibility, sea_fog, k_slow, c)
	_apply_fog(sun_elev, cloud, visibility, sea_fog, underwater, c)
	_apply_image(sun_elev, distance, underwater, c)
	_apply_water(delta, c)
	_refresh_probe(delta, snap, sun_elev, cloud, sea_fog)
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
	var mat := _sky_material()
	if mat == null:
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
	var haze := smoothstep(0.0, 1.0, (sigma_km - 0.12) / 1.6)
	mat.set_shader_parameter("sky_time", _sky_time)
	mat.set_shader_parameter("cloud_coverage", cloud)
	mat.set_shader_parameter("cloud_density", density)
	mat.set_shader_parameter("cloud_offset", _cloud_offset)
	mat.set_shader_parameter("aurora_intensity", _aurora)
	mat.set_shader_parameter("aurora_offset_km", float(aurora["offset_km"]))
	mat.set_shader_parameter("aurora_kp", float(aurora["kp"]))
	mat.set_shader_parameter("moon_direction", _moon_dir)
	mat.set_shader_parameter("moon_illumination", time_system.get_moon_illumination())
	mat.set_shader_parameter("haze_color", c["horizon_color"])
	mat.set_shader_parameter("haze_amount", haze)
	c["aurora_intensity"] = _aurora
	c["haze_amount"] = haze


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
	c["sky_zenith"] = zenith
	c["horizon_color"] = Color(fog_rgb.x, fog_rgb.y, fog_rgb.z)
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
	var distance_vis := lerpf(visibility, ABOVE_SEA_FOG_VISIBILITY_M, sea_fog)
	env.fog_density = KOSCHMIEDER / maxf(distance_vis, 1.0)
	env.fog_light_color = c["horizon_color"]
	env.fog_light_energy = 1.0
	env.fog_aerial_perspective = AERIAL_PERSPECTIVE
	env.fog_sky_affect = lerpf(SKY_AFFECT, 0.5, sea_fog)
	env.fog_sun_scatter = SUN_SCATTER * (1.0 - cloud * 0.7)
	# Height fog only for a sea-fog layer.
	env.fog_height = SEA_FOG_TOP_M
	env.fog_height_density = SEA_FOG_HEIGHT_DENSITY * sea_fog
	c["fog_density"] = env.fog_density
	c["fog_color"] = env.fog_light_color


# ── Image: exposure, tonemap, glow, ambient, SSAO, shadows, depth of field ────

func _apply_image(sun_elev: float, distance: float, underwater: bool, c: Dictionary) -> void:
	var env := _env()
	if env != null:
		var key: float = c["scene_key"]
		var exposure := clampf(pow(_key_ref / maxf(key, 1e-5), ADAPTATION), 1.0, MAX_EXPOSURE)
		env.tonemap_mode = Environment.TONE_MAPPER_AGX
		env.tonemap_agx_white = AGX_WHITE
		env.tonemap_agx_contrast = AGX_CONTRAST
		env.tonemap_exposure = exposure
		env.adjustment_enabled = false
		env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
		env.ambient_light_sky_contribution = 0.85
		env.ambient_light_color = c["horizon_color"]
		# The night boost only once the sky is dark: twilight lights the walls itself.
		env.ambient_light_energy = lerpf(AMBIENT_DAY, AMBIENT_NIGHT, smoothstep(-6.0, -14.0, sun_elev))
		var zoom := zoom_settings(distance)
		env.ssao_enabled = true
		env.ssao_radius = zoom.y
		c["exposure"] = exposure
		c["ssao_radius"] = zoom.y
	var zs := zoom_settings(distance)
	if sun_light:
		sun_light.directional_shadow_max_distance = zs.x
	if moon_light:
		moon_light.directional_shadow_max_distance = zs.x
	c["shadow_distance"] = zs.x
	_apply_depth_of_field(distance, zs.z, underwater, c)


func _apply_depth_of_field(distance: float, amount: float, underwater: bool, c: Dictionary) -> void:
	if camera == null:
		return
	var attrs := camera.attributes as CameraAttributesPractical
	if attrs == null:
		return
	# Off at night: the stars and the aurora must stay points and rays.
	var night := smoothstep(-2.0, -8.0, float(c.get("sun_elevation", 0.0)))
	amount *= 1.0 - night
	var on := not underwater and amount > 0.001
	attrs.dof_blur_far_enabled = on
	attrs.dof_blur_near_enabled = on and distance < 500.0
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
	for i in _comp_k.size():
		var heading := direction.rotated(deg_to_rad(_comp_heading[i]))
		shape.append(Vector4(heading.x * _comp_k[i], heading.y * _comp_k[i], _comp_k[i], chop))
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
		"choppiness": chop,
	}


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

func _env() -> Environment:
	if world_environment == null:
		return null
	return world_environment.environment


func _sky_material() -> ShaderMaterial:
	var env := _env()
	if env == null or env.sky == null:
		return null
	return env.sky.sky_material as ShaderMaterial


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
