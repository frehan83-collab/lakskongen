class_name OpenSea
extends RefCounted
## The open sea at the camera's offshore plot (roadmap F2-21; farm-types spec
## §4 "Wave driver"), for EnvironmentDirector, the only writer of the water's
## uniforms.
##
## Without a target the open coast is the director's wind sea (SeaStateModel
## over 100 km of fetch from the fjord's wind) and there is no swell. With a
## target (the engine's sea at the plot of the offshore unit nearest the
## camera's focus, SimBridge.get_sea_state_forecast "today": hs, wind_sea_hs,
## swell_total_hs, swell_tp; set_target) the open coast draws THAT sea:
## - the wind sea: the JONSWAP bands of the wind whose open-coast sea has
##   the target's wind-sea height (SeaStateModel.WindForHeight), scaled so
##   their Σa² is exactly wind_sea_hs²/8;
## - the swell: two long bands beyond the wind sea's 128 m (280 m and 160 m,
##   SeaStateModel.SwellBandWavelengthsM), each drawn as two Gerstner
##   components either side of the swell's heading, detuned as the wind
##   bands are (152-168 m and 265-295 m), with Σa² = swell_total_hs²/8;
## so across both Σa² = hs²/8, the plot's Hs (the engine splits it so that
## wind_sea_hs² + swell_total_hs² = hs²). The water shader adds the swell to
## the exposed sea only (water/water_swell.gdshaderinc: its sheltered
## amplitude is 0, the baked exposure blends it in, so the fjord stays calm),
## and WaveField carries it, so the units heave on it.
##
## Without a target the open coast is the wind model's at once, as before
## F2-21 (the fjord's weather moves both seas together). A target blends in
## (and a cleared one out) at FOLLOW_RATE, and a new target's sea (a new
## day's, another unit's) is followed at the same rate, so the sea never
## jumps; staging (snap) takes the goal at once. Phases are integrated here
## at each component's deep-water frequency, like the wind sea's.

const GRAVITY := 9.81
## Two components per swell band, detuned and weighted as the wind bands are
## (EnvironmentDirector COMPONENT_DETUNE / COMPONENT_WEIGHT), so the band's
## energy is shared equally and the two never repeat together.
const COMPONENTS_PER_BAND := 2
const DETUNE := [0.95, 1.055]
const WEIGHT := [0.70710678, 0.70710678]
## A swell is long-crested: its two components run this far apart (deg)
## either side of its heading (the wind sea's spread 9-27 deg).
const SPREAD_DEG := 6.0
## Where the swell runs: from the open Atlantic (WSW, the world's -Z is west,
## +X north) toward the coast (ENE): world x/z, unit.
const HEADING := Vector2(0.258819, 0.965926)
## Gerstner choppiness of the swell: long and round-crested, never steep.
const CHOP := 0.35
## How fast (1/s) the drawn open sea follows its goal.
const FOLLOW_RATE := 0.6

## Swell band wavelengths (m), longest first (SeaStateBridge.SwellBandWavelengths).
var bands_m := PackedFloat32Array()
## The target sea (SimBridge.get_sea_state_forecast "today" plus "plot_id"); {} without one.
var target: Dictionary = {}
## Exposed wind-sea band amplitudes drawn now (m), one per wind band.
var shown_wind := PackedFloat32Array()
## Swell band amplitudes drawn now (m), one per swell band.
var shown_swell := PackedFloat32Array()
## The target's wind-sea peak wavelength (m; 0 without a target).
var goal_wind_peak_m := 0.0
## Per swell component: (wavevector x, z, wavenumber, choppiness), longest first.
var swell_k := PackedVector4Array()
var _band := PackedInt32Array()
var _weight := PackedFloat32Array()
var _phase := PackedFloat64Array()
## Goals: the target's bands, or (no target) the wind model's open coast and no swell.
var _goal_wind := PackedFloat32Array()
var _goal_swell := PackedFloat32Array()
## The target's wind sea as drawn (following its goal), and how far the open
## coast is the target's (0 the wind model's .. 1 the target's).
var _target_wind := PackedFloat32Array()
var _target_weight := 0.0


## Build the swell components once (their wavelengths are fixed).
func setup(swell_bands_m: PackedFloat32Array) -> void:
	bands_m = swell_bands_m
	swell_k.clear()
	_band.clear()
	_weight.clear()
	_phase.clear()
	for band in bands_m.size():
		for j in COMPONENTS_PER_BAND:
			var k := TAU / bands_m[band] * float(DETUNE[j])
			var heading := HEADING.rotated(deg_to_rad((float(j) - 0.5 * (COMPONENTS_PER_BAND - 1)) * SPREAD_DEG))
			swell_k.append(Vector4(heading.x * k, heading.y * k, k, CHOP))
			_band.append(band)
			_weight.append(float(WEIGHT[j]))
			# Fixed, well-mixed starting phases (golden-angle steps, offset from the wind sea's).
			_phase.append(fmod((band * COMPONENTS_PER_BAND + j + 0.5) * 2.39996323, TAU))
	shown_swell.resize(bands_m.size())
	shown_swell.fill(0.0)
	_goal_swell.resize(bands_m.size())
	_goal_swell.fill(0.0)


## Draw the open coast as `sea` (the engine's split of a plot's sea: "hs",
## "wind_sea_hs", "swell_total_hs", "swell_tp"; any other keys ride along).
## `bridge` is the director's SeaStateBridge, `exposed_fetch_m` the open
## coast's fetch. An empty `sea` clears the target.
func set_target(sea: Dictionary, bridge: RefCounted, exposed_fetch_m: float) -> void:
	if sea.is_empty() or bridge == null:
		clear_target()
		return
	target = sea.duplicate()
	var wind_hs := maxf(float(sea.get("wind_sea_hs", sea.get("hs", 0.0))), 0.0)
	_goal_wind = PackedFloat32Array()
	goal_wind_peak_m = 0.0
	if wind_hs > 0.0:
		var wind := float(bridge.call("WindForHeight", wind_hs, exposed_fetch_m))
		var drawn: Dictionary = bridge.call("Sea", wind, exposed_fetch_m)
		_goal_wind = drawn["amplitudes"]
		goal_wind_peak_m = maxf(float(drawn["peak_wavelength"]), 0.5)
		# Exactly the target's wind sea (the bisection lands within a micron).
		var sum := 0.0
		for a in _goal_wind:
			sum += a * a
		if sum > 0.0:
			var scale := sqrt(wind_hs * wind_hs / 8.0 / sum)
			for i in _goal_wind.size():
				_goal_wind[i] *= scale
	var swell_hs := maxf(float(sea.get("swell_total_hs", 0.0)), 0.0)
	_goal_swell = bridge.call("SwellAmplitudes", swell_hs, maxf(float(sea.get("swell_tp", 0.0)), 0.0)) if swell_hs > 0.0 else PackedFloat32Array()
	if _goal_swell.size() != bands_m.size():
		_goal_swell.resize(bands_m.size())
		_goal_swell.fill(0.0)


func clear_target() -> void:
	target = {}
	_goal_wind = PackedFloat32Array()
	goal_wind_peak_m = 0.0
	_goal_swell.resize(bands_m.size())
	_goal_swell.fill(0.0)


func has_target() -> bool:
	return not target.is_empty()


## The target's exposed wind-sea amplitude of wind band `band` (m; 0 without a target).
func goal_amplitude(band: int) -> float:
	return _goal_wind[band] if band >= 0 and band < _goal_wind.size() else 0.0


## One frame: the open coast's wind bands are the wind model's
## (`model_exposed`, at once) blended toward the target's (followed at
## FOLLOW_RATE), the swell follows its goal, the swell's phases advance;
## `snap` takes every goal at once.
func step(delta: float, snap: bool, model_exposed: PackedFloat32Array) -> void:
	var k := 1.0 if snap else 1.0 - exp(-FOLLOW_RATE * delta)
	var to_weight := 1.0 if not _goal_wind.is_empty() else 0.0
	_target_weight = to_weight if absf(to_weight - _target_weight) < 1e-3 else lerpf(_target_weight, to_weight, k)
	if not _goal_wind.is_empty():
		if _target_wind.size() != _goal_wind.size() or _target_weight <= k:
			# A target blending in from the model's coast starts at its own sea.
			_target_wind = _goal_wind.duplicate()
		for i in _goal_wind.size():
			_target_wind[i] = lerpf(_target_wind[i], _goal_wind[i], k)
	shown_wind = model_exposed.duplicate()
	if _target_weight > 0.0 and _target_wind.size() == shown_wind.size():
		for i in shown_wind.size():
			shown_wind[i] = lerpf(model_exposed[i], _target_wind[i], _target_weight)
	for i in shown_swell.size():
		shown_swell[i] = lerpf(shown_swell[i], _goal_swell[i], k)
	for i in swell_k.size():
		_phase[i] = fmod(_phase[i] + sqrt(GRAVITY * swell_k[i].z) * delta, TAU)


## The swell's water_shader / WaveField amplitude rows: (sheltered 0, exposed
## amplitude, phase, wavelength) per component.
func swell_amp() -> PackedVector4Array:
	var out := PackedVector4Array()
	for i in swell_k.size():
		out.append(Vector4(0.0, shown_swell[_band[i]] * _weight[i], _phase[i], TAU / swell_k[i].z))
	return out


## Hs (m) of the open coast drawn now: wind sea and swell together.
func exposed_hs() -> float:
	var sum := 0.0
	for a in shown_wind:
		sum += a * a
	for a in shown_swell:
		sum += a * a
	return sqrt(8.0 * sum)


## Hs (m) of the swell drawn now.
func swell_hs() -> float:
	var sum := 0.0
	for a in shown_swell:
		sum += a * a
	return sqrt(8.0 * sum)
