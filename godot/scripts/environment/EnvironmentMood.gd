class_name EnvironmentMood
extends RefCounted
## The fjord's eight lighting moods (G3-05): Sommerdag, Midnattssol,
## Gråvær, Storm, Blåtime (mørketid), Nordlys, Frostrøyk and Høst.
##
## The physical model (EnvironmentDirector: the sun's geometry and
## transmittance, the sky's scattering, Koschmieder fog, the eye's
## adaptation) decides how much light there is. A mood is the art
## direction on top of it: the hue of the sky, the cloud deck, the fog and
## the sunlight, the water's body colour, a small exposure bias and mist on
## the mountains. Grading keeps the physical luminance and takes the chroma
## from the mood (grade_rgb), so a mood never fights the light it is given.
##
## Nothing here is forced: the moods are weighed from the sim's weather and
## calendar each frame (weights()): the sun's height and bearing (latitude,
## date, hour), cloud, rain, wind, sea fog and the season. A frame blends
## the moods by weight (blend()), so a storm rolling in or a sunset changes
## the mood gradually. The director applies the blend and is the only
## writer of the result (tests/test_environment_director.py).
##
## Sources of the colours (display sRGB):
## - the art audit's mood palette (2026-10-14 render/art research):
##   Sommerdag sun #FFF1DD, zenith #5E8DB8, horizon #D5E0E6, aerial fog
##   #A9BCC8; Midnattssol sun #FFC27A, horizon #F3D4A4; Gråvær sky #A7B3BA,
##   fog #9AA7AE; Storm sky #4C5861, fog #6E7A82, foam #E7ECEC, light ~0.45;
##   Blåtime sky #3C5A8A to #7F96BC, south horizon #D99AA8, artificial light
##   #FFB35C; Nordlys sky #08121F, aurora #78F2A0; Frostrøyk sea smoke
##   #DDE4E8; Høst low sun #FFD9A0; clear water deep #0D2B3B, mid #1F4D5E;
## - the owner's water moods (OWNER_REFERENCE_BRIEF): sunny deep blue
##   #0B3F7A-#12407A, calm winter slate mirror #1E3340-#2A4655, overcast
##   grey #5E666C;
## - the owner's calm-winter reference photo (ref 2) for the Frostrøyk sky,
##   which neither source gives: its upper sky #81A0B0, its lower sky
##   #C5D2DA.
## SPECS holds the sky and water targets the mood goldens are measured
## against (MoodMetrics, ScreenshotHarness): delta E 2000 <= SPEC_DELTA_E.

enum Id { SOMMERDAG, MIDNATTSSOL, GRAAVAER, STORM, BLAATIME, NORDLYS, FROSTROYK, HOEST }

const NAMES: Array[String] = ["sommerdag", "midnattssol", "graavaer", "storm", "blaatime", "nordlys", "frostroyk", "hoest"]
const COUNT := 8

## Region targets of the mood goldens (display sRGB). A pair is a range:
## the region's mean must lie within SPEC_DELTA_E of the segment between
## the two (in CIELAB). "source" says where each value comes from.
const SPEC_DELTA_E := 10.0
const SPECS := {
	"sommerdag": {"sky": ["#5E8DB8", "#D5E0E6"], "water": ["#0B3F7A", "#12407A"],
		"source": "sky: art audit zenith to horizon; water: owner sunny deep blue"},
	"midnattssol": {"sky": ["#F3D4A4"], "water": ["#1E3340", "#2A4655"],
		"source": "sky: art audit horizon; water: owner calm slate mirror (a calm, low-sun mirror; no source gives a midnight-sun water)"},
	"graavaer": {"sky": ["#A7B3BA"], "water": ["#5E666C"],
		"source": "sky: art audit; water: owner overcast grey"},
	"storm": {"sky": ["#4C5861", "#6E7A82"], "water": ["#1F4D5E", "#6E7A82"],
		"source": "sky: art audit deck to its fog (the FJORD view's sky is the band over the horizon); water: art audit clear water mid to the storm's fog (no source gives a storm sea: within 2 km of the camera it is the sea's body seen through the rain veil)"},
	"blaatime": {"sky": ["#3C5A8A", "#7F96BC"], "water": ["#1E3340", "#2A4655"],
		"source": "sky: art audit; water: owner calm winter slate mirror"},
	"nordlys": {"sky": ["#08121F"], "water": ["#08121F", "#1E3340"],
		"source": "sky: art audit (aurora pixels excluded); water: the night sky mirrored to the owner's slate"},
	"frostroyk": {"sky": ["#81A0B0", "#C5D2DA"], "water": ["#1E3340", "#2A4655"], "smoke": ["#DDE4E8"],
		"source": "sky: owner ref 2 upper to lower sky; water: owner calm winter slate mirror where no smoke lies; smoke: art audit sea smoke"},
	"hoest": {"sky": ["#5E8DB8", "#D5E0E6"], "water": ["#0D2B3B", "#1F4D5E"],
		"source": "sky: art audit clear-day zenith to horizon; water: art audit clear water deep to mid"},
}

## Water pixels at least this light (CIELAB L*) in a golden with frost smoke
## are smoke (the slate water reads L* 20-30, the smoke 80-90): their mean is
## measured against SPECS "smoke", the rest against "water".
const SMOKE_SPLIT_L := 55.0
## The aurora's core (557.7 nm oxygen green) and the sea smoke's colour.
const AURORA_GREEN := Color("#78F2A0")
const SEA_SMOKE := Color("#DDE4E8")

## Profile keys:
##   zenith, horizon, horizon_sun   sky gradient (the sun's side of the horizon
##                                  may differ: Blåtime's pink south)
##   sky_grade                      0-1: how far the clear sky takes the mood's hue
##   deck, deck_grade               the cloud deck's hue under overcast
##   fog, fog_grade                 the fog's (aerial perspective's) hue
##   fog_value                      fog light on the land relative to the
##                                  horizon sky: under 1, far mountains stay a
##                                  shade darker than a bright sky behind
##                                  them; over 1 (a storm's dark deck), they
##                                  fade lighter than the deck: either way
##                                  their outline reads
##   sun, sun_grade                 the sunlight's colour
##   water                          the deep water column's scatter (linear
##                                  albedo, water_shader deep_scatter)
##   exposure_ev                    exposure bias (stops) on the adapted eye
##   cloud_base_m, cloud_base_fog   mist on the mountains: fog thickening
##                                  above this height (height fog with a
##                                  negative density), 0 for none
##   sky_haze                       share of the visibility's haze the sky
##                                  takes at the horizon: under 1 (a storm)
##                                  the deck stays dark behind the peaks
##   deck_value                     the cloud deck's brightness against the
##                                  sky model's (a storm's deck hangs dark
##                                  over a lighter horizon)
##   horizon_lift                   under a closed deck, how much lighter
##                                  the last degrees over the horizon are
##                                  than the haze (the far deck thinning)
##   mist                           low cloud banks on the walls from the
##                                  cloud base up (MistBanks), 0-1
const PROFILES := [
	# Sommerdag: a high sun, a blue sky paling to the horizon, deep blue water.
	{"zenith": "#5E8DB8", "horizon": "#C6D8E6", "horizon_sun": "#E3E5E0", "sky_grade": 0.55,
		"deck": "#E9EDF0", "deck_grade": 0.0, "fog": "#A9BCC8", "fog_grade": 0.6, "fog_value": 0.92,
		"sun": "#FFF1DD", "sun_grade": 0.5, "water": Color(0.0010, 0.014, 0.070), "exposure_ev": -0.15,
		"cloud_base_m": 0.0, "cloud_base_fog": 0.0, "sky_haze": 1.0, "deck_value": 1.0, "horizon_lift": 0.0, "mist": 0.0},
	# Midnattssol: the sun a few degrees up in the north, gold on the horizon
	# and on the slopes it lights (gfx-polish-1: with the sunlight's
	# luminance kept, its hue moves further toward the spec's #FFC27A and the
	# sun's side of the sky and the deck a shade warmer, toward #F3D4A4).
	{"zenith": "#5A8AB4", "horizon": "#F8CC8C", "horizon_sun": "#F6C27E", "sky_grade": 1.0,
		"deck": "#F3C890", "deck_grade": 0.85, "fog": "#E6C89C", "fog_grade": 0.6, "fog_value": 0.5,
		"sun": "#FFC27A", "sun_grade": 0.7, "water": Color(0.0, 0.016, 0.040), "exposure_ev": 0.45,
		"cloud_base_m": 0.0, "cloud_base_fog": 0.0, "sky_haze": 1.0, "deck_value": 1.3, "horizon_lift": 0.0, "mist": 0.0},
	# Gråvær: the west coast's closed grey deck, grey water, mist on the tops.
	{"zenith": "#9AA8B0", "horizon": "#A7B3BA", "horizon_sun": "#A7B3BA", "sky_grade": 0.8,
		"deck": "#A7B3BA", "deck_grade": 0.85, "fog": "#9AA7AE", "fog_grade": 0.85, "fog_value": 0.85,
		"sun": "#E8ECEE", "sun_grade": 0.6, "water": Color(0.090, 0.100, 0.105), "exposure_ev": 0.2,
		"cloud_base_m": 200.0, "cloud_base_fog": 0.008, "sky_haze": 1.0, "deck_value": 1.0, "horizon_lift": 0.25, "mist": 1.0},
	# Storm: a dark slate deck over a lighter horizon, rain driven by the
	# wind; the far land fades lighter than the deck, so the mountains keep
	# their outline.
	{"zenith": "#4C5861", "horizon": "#6A767E", "horizon_sun": "#6A767E", "sky_grade": 0.85,
		"deck": "#4C5861", "deck_grade": 0.9, "fog": "#6E7A82", "fog_grade": 0.85, "fog_value": 0.24,
		"sun": "#DDE3E6", "sun_grade": 0.7, "water": Color(0.008, 0.022, 0.026), "exposure_ev": 0.65,
		"cloud_base_m": 220.0, "cloud_base_fog": 0.004, "sky_haze": 0.75, "deck_value": 0.42, "horizon_lift": 1.4, "mist": 0.15},
	# Blåtime / mørketid: the sun just under the horizon, the sky deep blue
	# with a pink south, the water a slate mirror.
	{"zenith": "#3C5A8A", "horizon": "#7F96BC", "horizon_sun": "#D99AA8", "sky_grade": 0.9,
		"deck": "#55688A", "deck_grade": 0.6, "fog": "#6E82A6", "fog_grade": 0.8, "fog_value": 0.9,
		"sun": "#FFC9A0", "sun_grade": 0.5, "water": Color(0.0018, 0.0048, 0.0085), "exposure_ev": -0.2,
		"cloud_base_m": 0.0, "cloud_base_fog": 0.0, "sky_haze": 1.0, "deck_value": 1.0, "horizon_lift": 0.0, "mist": 0.0},
	# Nordlys: a deep night sky for the aurora, the calm water mirroring it.
	{"zenith": "#08121F", "horizon": "#14243A", "horizon_sun": "#14243A", "sky_grade": 0.8,
		"deck": "#1A2430", "deck_grade": 0.6, "fog": "#16243A", "fog_grade": 0.8, "fog_value": 0.9,
		"sun": "#FFF1DD", "sun_grade": 0.0, "water": Color(0.006, 0.02, 0.09), "exposure_ev": -1.0,
		"cloud_base_m": 0.0, "cloud_base_fog": 0.0, "sky_haze": 1.0, "deck_value": 1.0, "horizon_lift": 0.0, "mist": 0.0},
	# Frostrøyk: a clear, still, bitter winter day: a cool cyan-grey sky, a
	# slate mirror, sea smoke on the water.
	{"zenith": "#81A0B0", "horizon": "#C5D2DA", "horizon_sun": "#CCD3D6", "sky_grade": 0.9,
		"deck": "#C9D2D8", "deck_grade": 0.5, "fog": "#B9C8D0", "fog_grade": 0.75, "fog_value": 0.9,
		"sun": "#FFE6C8", "sun_grade": 0.5, "water": Color(0.0016, 0.0050, 0.0072), "exposure_ev": -0.35,
		"cloud_base_m": 0.0, "cloud_base_fog": 0.0, "sky_haze": 1.0, "deck_value": 1.0, "horizon_lift": 0.0, "mist": 0.0},
	# Høst: a low golden sun on the birches, a clear, dark sea.
	{"zenith": "#5E8DB8", "horizon": "#AEC4DA", "horizon_sun": "#DCCFB8", "sky_grade": 0.9,
		"deck": "#C4CCD6", "deck_grade": 0.6, "fog": "#AAB8C2", "fog_grade": 0.5, "fog_value": 0.6,
		"sun": "#FFD9A0", "sun_grade": 0.8, "water": Color(0.0, 0.0085, 0.0125), "exposure_ev": -0.25,
		"cloud_base_m": 0.0, "cloud_base_fog": 0.0, "sky_haze": 1.0, "deck_value": 0.9, "horizon_lift": 0.0, "mist": 0.0},
]

## Colour keys (sRGB hex in PROFILES, blended in linear light).
const COLOR_KEYS: Array[String] = ["zenith", "horizon", "horizon_sun", "deck", "fog", "sun"]
const SCALAR_KEYS: Array[String] = ["sky_grade", "deck_grade", "fog_grade", "fog_value", "sun_grade",
	"exposure_ev", "cloud_base_m", "cloud_base_fog", "sky_haze", "deck_value", "horizon_lift", "mist"]

# ── Weighing (degrees, m/s, mm/h, day of year) ───────────────────────────────
## Overcast from this cloud cover to full at OVERCAST_FULL.
const OVERCAST_FROM := 0.62
const OVERCAST_FULL := 0.88
## Storm: wind (the sim's storms blow 13-17 m/s, its fresh days 5-9) or
## heavy rain under a closed deck.
const STORM_WIND_FROM := 11.0
const STORM_WIND_FULL := 14.5
const STORM_RAIN_FROM := 2.5
const STORM_RAIN_FULL := 6.0
## Sun elevation bands: night below -11 to -7 degrees, the blue hour from
## there to the sun just up, day once it clears 3 degrees.
const NIGHT_FROM := -11.0
const NIGHT_TO := -7.0
const DAY_FROM := -0.5
const DAY_TO := 3.0
## The midnight sun: low (under LOW_SUN_TO degrees, above -7) and in the
## northern half of the sky, in early summer.
const LOW_SUN_TO := 12.0
## Season edges (day of year): early summer, autumn, the cold season.
const SUMMER := [115.0, 145.0, 205.0, 235.0]
const AUTUMN := [238.0, 258.0, 300.0, 320.0]
const WINTER := [60.0, 90.0, 318.0, 345.0]
## Still air (Frostrøyk's mirror): under CALM_FROM, gone by CALM_TO.
const CALM_FROM := 3.0
const CALM_TO := 6.0
## Frost smoke lifts once the sun is high enough to warm the air.
const SMOKE_LIFT_FROM := 6.0
const SMOKE_LIFT_TO := 18.0


static func _band(x: float, edges: Array) -> float:
	return smoothstep(edges[0], edges[1], x) * (1.0 - smoothstep(edges[2], edges[3], x))


## Weights (one per Id, summing to 1) of the moods for the inputs:
##   sun_elevation (deg), sun_direction (unit, north +X, east +Z),
##   day_of_year (1-365), cloud (0-1), rain (mm/h, snow included),
##   wind (m/s).
static func weights(inputs: Dictionary) -> PackedFloat32Array:
	var e := float(inputs.get("sun_elevation", 30.0))
	var sun_dir: Vector3 = inputs.get("sun_direction", Vector3(0.0, 0.5, -0.866))
	var doy := float(inputs.get("day_of_year", 172))
	var cloud := float(inputs.get("cloud", 0.2))
	var rain := float(inputs.get("rain", 0.0))
	var wind := float(inputs.get("wind", 4.0))

	var overcast := smoothstep(OVERCAST_FROM, OVERCAST_FULL, cloud)
	var storm := maxf(smoothstep(STORM_WIND_FROM, STORM_WIND_FULL, wind) * smoothstep(0.4, 0.8, cloud),
		smoothstep(STORM_RAIN_FROM, STORM_RAIN_FULL, rain) * overcast)
	var clear := (1.0 - overcast) * (1.0 - storm)
	var grey := overcast * (1.0 - storm)

	var dusk := smoothstep(NIGHT_FROM, NIGHT_TO, e)
	var night := 1.0 - dusk
	var daylight := smoothstep(DAY_FROM, DAY_TO, e)
	var blue_hour := dusk * (1.0 - daylight)

	var flat := Vector2(sun_dir.x, sun_dir.z)
	var north := smoothstep(0.1, 0.6, flat.normalized().x) if flat.length() > 1e-4 else 0.0
	var low := (1.0 - smoothstep(LOW_SUN_TO - 6.0, LOW_SUN_TO, e)) * smoothstep(-7.0, -2.0, e)
	var midnight_sun := _band(doy, SUMMER) * north * low
	var autumn := _band(doy, AUTUMN)
	var winter := clampf(1.0 - smoothstep(WINTER[0], WINTER[1], doy) + smoothstep(WINTER[2], WINTER[3], doy), 0.0, 1.0)
	var calm := 1.0 - smoothstep(CALM_FROM, CALM_TO, wind)

	var w := PackedFloat32Array()
	w.resize(COUNT)
	w[Id.STORM] = storm
	w[Id.GRAAVAER] = grey
	w[Id.MIDNATTSSOL] = clear * midnight_sun
	var rest := clear * (1.0 - midnight_sun)
	w[Id.NORDLYS] = rest * night
	w[Id.BLAATIME] = rest * blue_hour
	var day := rest * daylight
	w[Id.HOEST] = day * autumn
	w[Id.FROSTROYK] = day * winter * calm * (1.0 - autumn)
	w[Id.SOMMERDAG] = day * (1.0 - autumn) * (1.0 - winter * calm)
	var total := 0.0
	for v in w:
		total += v
	if total <= 1e-6:
		w[Id.SOMMERDAG] = 1.0
		return w
	for i in COUNT:
		w[i] /= total
	return w


## The mood with the largest weight.
static func dominant(w: PackedFloat32Array) -> int:
	var best := 0
	for i in w.size():
		if w[i] > w[best]:
			best = i
	return best


static var _linear_profiles: Array = []


## PROFILES with colours in linear light (cached).
static func linear_profiles() -> Array:
	if not _linear_profiles.is_empty():
		return _linear_profiles
	for p: Dictionary in PROFILES:
		var lin := p.duplicate()
		for key in COLOR_KEYS:
			lin[key] = Color(String(p[key])).srgb_to_linear()
		_linear_profiles.append(lin)
	return _linear_profiles


## The profiles blended by weight: colours in linear light (Color), scalars,
## and the water scatter.
static func blend(w: PackedFloat32Array) -> Dictionary:
	var profiles := linear_profiles()
	var out := {}
	for key in COLOR_KEYS:
		out[key] = Color(0.0, 0.0, 0.0)
	for key in SCALAR_KEYS:
		out[key] = 0.0
	out["water"] = Color(0.0, 0.0, 0.0)
	for i in mini(w.size(), profiles.size()):
		var k := w[i]
		if k <= 0.0:
			continue
		var p: Dictionary = profiles[i]
		for key in COLOR_KEYS:
			out[key] = (out[key] as Color) + (p[key] as Color) * k
		for key in SCALAR_KEYS:
			out[key] = float(out[key]) + float(p[key]) * k
		out["water"] = (out["water"] as Color) + (p["water"] as Color) * k
	# A cloud base is the weighted base of the moods that have one.
	var base_weight := 0.0
	var base := 0.0
	for i in mini(w.size(), profiles.size()):
		var b := float(profiles[i]["cloud_base_m"])
		if b > 0.0:
			base_weight += w[i]
			base += b * w[i]
	out["cloud_base_m"] = base / base_weight if base_weight > 1e-4 else 0.0
	return out


## How dense the frost smoke on the water is shown (0-1): the sim's frost
## smoke (AtmosphereModel.FrostSmoke), lifting as the sun climbs high
## enough to warm the air.
static func frost_smoke_shown(sim_frost_smoke: float, sun_elevation: float) -> float:
	return clampf(sim_frost_smoke, 0.0, 1.0) * (1.0 - smoothstep(SMOKE_LIFT_FROM, SMOKE_LIFT_TO, sun_elevation))


## Re-colour a scene-linear colour toward `target` (linear) by `amount`,
## keeping its luminance: the light stays the physical model's, the hue
## becomes the mood's.
static func grade_rgb(rgb: Vector3, target: Color, amount: float) -> Vector3:
	if amount <= 0.0:
		return rgb
	var t := Vector3(target.r, target.g, target.b)
	var lt := luminance(t)
	if lt <= 1e-6:
		return rgb
	var graded := t * (luminance(rgb) / lt)
	return rgb.lerp(graded, clampf(amount, 0.0, 1.0))


static func luminance(v: Vector3) -> float:
	return 0.2126 * v.x + 0.7152 * v.y + 0.0722 * v.z


static func vec(c: Color) -> Vector3:
	return Vector3(c.r, c.g, c.b)
