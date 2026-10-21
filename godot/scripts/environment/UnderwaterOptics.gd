class_name UnderwaterOptics
extends RefCounted
## The optics of the underwater view (roadmap G3-04), in one place for the
## EnvironmentDirector (which writes them), the underwater presenter
## (UnderwaterView) and the tests. Pure functions of their inputs.
##
## - Visibility: the sim's horizontal sighting range of a black target
##   (LakskongenSim.Environment.WaterClarity, y = 4.8 / alpha, Zaneveld &
##   Pegau 2003), per site (GetSeaSiteDetails water_visibility_m). The fog is
##   alpha: a line of sight loses e^(-alpha r) of its contrast. The drawn
##   visibility keeps a readable floor (READABLE_FLOOR_M), strictly rising
##   with the water's, so a bloom always reads murkier than clear water.
## - Light: the sun refracts into the water (Snell, n = 1.333: its zenith
##   angle shrinks), the surface passes 1 - F of it (Schlick, F0 0.02), and
##   the water above the camera filters it by Beer-Lambert with the water
##   shader's own diffuse attenuation (absorption, bloom included): red goes
##   first, the light turns green-teal with depth.
## - The water's own light (what the fog and the background show, and what
##   lights the fish from below): the light at the camera's depth scattered
##   back by the water, horizontally HORIZONTAL_RADIANCE of the downwelling
##   light, NADIR_SHARE of that straight down, the colour the light at depth
##   times the water's scatter-to-attenuation ratio (green fjord water).
## - Snell's window: everything above the surface shows inside a cone of
##   48.6 deg round the zenith; outside it the surface mirrors the water below
##   (total internal reflection).

const IOR_WATER := 1.333
## Snell's window's half-angle: asin(1 / 1.333).
const SNELL_HALF_ANGLE_DEG := 48.6
## Black-target visibility x photopic attenuation (WaterClarity.BlackTargetConstant).
const BLACK_TARGET := 4.8
## Readability (with the user in mind, as the air's READABLE_VIS_M): the
## view is a camera's, not an eye's. The black-target constant 4.8 is the
## eye's contrast threshold (0.8 %); a camera that stretches the water's
## low contrast (the way an ROV's or a diver's camera is graded) sees about
## CAMERA_GAIN times as far, and the drawn visibility never falls under
## READABLE_FLOOR_M: sqrt((gain v)^2 + floor^2), strictly rising with the
## water's own, so a bloom always reads murkier than clear water.
const CAMERA_GAIN := 1.5
const READABLE_FLOOR_M := 4.5
## Without a site's number (a scene without the sim): a summer fjord's.
const FALLBACK_VISIBILITY_M := 6.0
## Water's normal-incidence reflectance (the water shader's F0).
const WATER_F0 := 0.02
## The water's diffuse attenuation per metre (water_shader absorption) and
## what a bloom adds per unit of severity: the algae's pigments take the
## blue first and some red, the cells' scattering spreads every colour. Seen
## from inside the pen, bloom water is a deep, dark green (11b: mean sRGB
## (17, 81, 31), hue 133 degrees): the water over the camera has already taken
## the red (0.40 /m), which no bloom gives back (wave4b int review: "olive"
## here was the bloom's look from above, the water shader's own surface tint).
const ABSORPTION := Vector3(0.40, 0.09, 0.14)
const BLOOM_ABSORPTION := Vector3(0.16, 0.06, 0.30)
## Chlorophyll a (ug/L) of a bloom at full severity
## (WaterClarity.BloomChlorophyllAtFullSeverityUgL).
const BLOOM_FULL_UG_L := 20.0
## Scattering's share of the attenuation, per channel, for fjord water
## (b / c with b about 0.6 /m of particles, flat across the visible): the
## water's own colour, green where red is absorbed and blue goes to
## dissolved organic matter. With a bloom, more green and less blue.
const SCATTER_SHARE := Vector3(0.55, 0.86, 0.86)
const BLOOM_SCATTER_SHARE := Vector3(0.95, 0.90, 0.60)
## Horizontal water radiance per unit of downwelling light at the camera
## (sr^-1, scene-linear): a fjord's water reads as a 10 % grey card under the
## light of its depth. Straight down: NADIR_SHARE of it.
const HORIZONTAL_RADIANCE := 0.032
const NADIR_SHARE := 0.3


## The direction toward the sun seen from under a flat surface (Snell's
## law: the zenith angle shrinks by n; the azimuth stays). `sun_dir` points
## at the sun; under the horizon the sun's light comes in at the window's edge.
static func refracted_sun(sun_dir: Vector3) -> Vector3:
	var d := sun_dir.normalized()
	var flat := Vector2(d.x, d.z)
	var sin_air := clampf(flat.length(), 0.0, 1.0)
	var sin_water := sin_air / IOR_WATER
	var cos_water := sqrt(maxf(1.0 - sin_water * sin_water, 0.0))
	var azimuth := flat.normalized() if flat.length() > 1e-6 else Vector2.ZERO
	return Vector3(azimuth.x * sin_water, cos_water, azimuth.y * sin_water).normalized()


## Share of the sunlight the flat surface lets through at the sun's
## elevation (1 - Schlick's Fresnel, F0 0.02); 0 with the sun down.
static func surface_transmission(sun_dir: Vector3) -> float:
	var cos_i := clampf(sun_dir.normalized().y, 0.0, 1.0)
	if cos_i <= 0.0:
		return 0.0
	return 1.0 - (WATER_F0 + (1.0 - WATER_F0) * pow(1.0 - cos_i, 5.0))


## The drawn visibility (m) for the water's sighting range (CAMERA_GAIN,
## READABLE_FLOOR_M).
static func drawn_visibility(visibility_m: float) -> float:
	var v := maxf(visibility_m, 0.0) * CAMERA_GAIN
	return sqrt(v * v + READABLE_FLOOR_M * READABLE_FLOOR_M)


## The fog density (photopic attenuation, 1/m) the view draws for a sighting range.
static func fog_density(visibility_m: float) -> float:
	return BLACK_TARGET / drawn_visibility(visibility_m)


## Bloom severity (0-1) of a site's bloom chlorophyll (ug/L).
static func bloom_of(chlorophyll_ug_l: float) -> float:
	return clampf(chlorophyll_ug_l / BLOOM_FULL_UG_L, 0.0, 1.0)


## The water's diffuse attenuation per metre with a bloom of `bloom` (0-1).
static func attenuation_rgb(bloom: float) -> Vector3:
	return ABSORPTION + BLOOM_ABSORPTION * clampf(bloom, 0.0, 1.0)


## Light left after `path_m` of water (per channel).
static func transmittance(sigma: Vector3, path_m: float) -> Vector3:
	var p := maxf(path_m, 0.0)
	return Vector3(exp(-sigma.x * p), exp(-sigma.y * p), exp(-sigma.z * p))


## The sunlight's filter at `depth_m` under a surface it crosses at the
## (refracted) direction `sun_water`: Beer-Lambert along the slant path.
static func sun_filter(sigma: Vector3, depth_m: float, sun_water: Vector3) -> Vector3:
	return transmittance(sigma, depth_m / maxf(sun_water.y, 0.2))


## The water's colour (scatter over attenuation, per channel) with a bloom.
static func scatter_share(bloom: float) -> Vector3:
	return SCATTER_SHARE.lerp(BLOOM_SCATTER_SHARE, clampf(bloom, 0.0, 1.0))


## The water's horizontal radiance (scene-linear) under `downwelling`
## (the light at the camera's depth: sun and sky, per channel).
static func horizontal_radiance(downwelling: Vector3, bloom: float) -> Vector3:
	var share := scatter_share(bloom)
	return Vector3(downwelling.x * share.x, downwelling.y * share.y, downwelling.z * share.z) * HORIZONTAL_RADIANCE


## Luminance (Rec. 709) of a linear colour.
static func luminance(v: Vector3) -> float:
	return 0.2126 * v.x + 0.7152 * v.y + 0.0722 * v.z
