class_name CloudShadows
extends Decal
## Cloud shadows moving over the land (G3-05).
##
## One Decal over the whole view, projected along the sunlight (its +Y
## points at the sun), darkens what it falls on where a cloud stands
## between it and the sun. Decals reach every land material the engine
## draws (terrain, trees, shore props, buildings, pens) without touching
## their shaders; the water reads the same map through its own uniforms
## (water_mood.gdshaderinc), so a shadow crosses from the hills onto the
## water. Only surfaces facing the sun take it (normal_fade): a slope
## already in its own shadow gets no darker.
##
## The map is a seamless noise, two periods by two, thresholded at the
## cloud cover (a cover of 0.4 shades about 40 % of the ground). The decal
## sits over the camera's focus and moves with the clouds' drift, snapped
## by whole periods so the pattern never jumps. EnvironmentDirector calls
## update() every frame with the sun, the cover, the strength and the
## drift; it is the only caller.

## One period of the noise: texels, and metres on the ground.
const PERIOD_TEXELS := 192
const PERIOD_M := 6000.0
## The box reaches this far along the sunlight either side of the focus
## (it must hold the land in view and the peaks).
const DEPTH_M := 16000.0
## Re-threshold the map when the cover moves by this much.
const COVER_STEP := 0.04
## Edge softness of a shadow, in noise units.
const EDGE := 0.07
## Darkening of a shadowed, sunlit surface's albedo at full strength: the
## direct sun's share of its light on a clear day.
const MAX_DARKEN := 0.6
## Only surfaces facing the sun take the shadow (Decal.normal_fade).
const SUN_FACING := 0.12
## Render layers the shadows fall on: the land and what stands on it (the
## water, layer 20, reads the map itself; world labels and the mirror-only
## parts sit on 18 and 19).
const LAND_LAYERS := 1

var _noise := FastNoiseLite.new()
var _base: Image = null
## The noise's distribution: _above[v] is the share of texels brighter
## than v (0-255), so a cover maps to the threshold that shades that share.
var _above := PackedFloat32Array()
var _built_cover := -1.0
var _map: ImageTexture = null
## The map's frame (the water samples it with these): centre, and the
## projected axes scaled by 1 / the decal's size.
var map_origin := Vector3.ZERO
var map_axis_u := Vector3(1.0 / (2.0 * PERIOD_M), 0.0, 0.0)
var map_axis_v := Vector3(0.0, 0.0, 1.0 / (2.0 * PERIOD_M))
## The strength applied last (0: no shadows drawn) and the cover the map
## holds (tests read them).
var strength := 0.0
var cover := 0.0


func _ready() -> void:
	_noise.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	_noise.seed = 4127
	# About eight cycles a period: clouds a few hundred metres to 1.5 km across.
	_noise.frequency = 0.04
	_noise.fractal_type = FastNoiseLite.FRACTAL_FBM
	_noise.fractal_octaves = 4
	_noise.fractal_gain = 0.5
	size = Vector3(2.0 * PERIOD_M, DEPTH_M, 2.0 * PERIOD_M)
	cull_mask = LAND_LAYERS
	normal_fade = SUN_FACING
	upper_fade = 0.0
	lower_fade = 0.0
	distance_fade_enabled = false
	albedo_mix = 1.0
	modulate = Color(0.0, 0.0, 0.0, 0.0)
	visible = false


## The shadow map (alpha = shadow), for the water.
func get_map() -> Texture2D:
	return _map


## Place and shade the shadows. `sun_dir` points at the sun, `cloud_cover`
## is the sky's (0-1), `shadow_strength` 0-1 (0 hides them), `drift` the
## clouds' horizontal drift so far (m, x/z) and `focus` the camera's focus.
func update(sun_dir: Vector3, cloud_cover: float, shadow_strength: float, drift: Vector2, focus: Vector3) -> void:
	strength = clampf(shadow_strength, 0.0, 1.0)
	visible = strength > 0.001
	if not visible:
		return
	if absf(cloud_cover - _built_cover) >= COVER_STEP or _map == null:
		_build_map(cloud_cover)
	var basis := sun_basis(sun_dir)
	var d := Vector3(drift.x, 0.0, drift.y)
	# The pattern's phase on the decal's plane, moved to the period nearest
	# the focus (whole periods leave the tiled map unchanged).
	var cu := d.dot(basis.x)
	var cv := d.dot(basis.z)
	var fu := focus.dot(basis.x)
	var fv := focus.dot(basis.z)
	cu += PERIOD_M * roundf((fu - cu) / PERIOD_M)
	cv += PERIOD_M * roundf((fv - cv) / PERIOD_M)
	var origin := basis.x * cu + basis.z * cv + basis.y * focus.dot(basis.y)
	global_transform = Transform3D(basis, origin)
	modulate = Color(0.0, 0.0, 0.0, MAX_DARKEN * strength)
	map_origin = origin
	map_axis_u = basis.x / size.x
	map_axis_v = basis.z / size.z


## A frame whose +Y points at the sun and whose X lies level.
static func sun_basis(sun_dir: Vector3) -> Basis:
	var y := sun_dir.normalized() if sun_dir.length() > 1e-4 else Vector3.UP
	var level := Vector3(-y.z, 0.0, y.x)
	var x := level.normalized() if level.length() > 0.05 else Vector3.RIGHT
	var z := x.cross(y).normalized()
	x = y.cross(z).normalized()
	return Basis(x, y, z)


## Threshold the noise at the cover: the map's alpha is the shadow.
func _build_map(cloud_cover: float) -> void:
	cover = clampf(cloud_cover, 0.0, 1.0)
	_built_cover = cover
	if _base == null:
		_base = _tiled_noise()
		_above = _distribution(_base)
	var img := _base.duplicate() as Image
	# Shadow where the noise is above the threshold t that leaves the cover's
	# share of texels above it. adjust_bcs maps n to 0.5 + (b n - 0.5) c:
	# b = 0.5 / t and c = t / EDGE put t at 0.5 with an edge 2 EDGE wide.
	var t := clampf(threshold_for(cover), 0.02, 0.98)
	img.adjust_bcs(0.5 / t, t / EDGE, 1.0)
	img.convert(Image.FORMAT_LA8)
	# LA8 holds (L, A) per texel; moving every byte one place on puts each
	# texel's L into its alpha (and 255 into the luminance, which the black
	# modulate hides).
	var bytes := img.get_data()
	var shifted := PackedByteArray([0])
	shifted.append_array(bytes.slice(0, bytes.size() - 1))
	var map := Image.create_from_data(img.get_width(), img.get_height(), false, Image.FORMAT_LA8, shifted)
	map.convert(Image.FORMAT_RGBA8)
	if _map == null or _map.get_width() != map.get_width():
		_map = ImageTexture.create_from_image(map)
	else:
		_map.update(map)
	texture_albedo = _map


## The noise level (0-1) above which the share `share` of the map lies.
func threshold_for(share: float) -> float:
	if _above.is_empty():
		return 1.0 - share
	for v in 256:
		if _above[v] <= share:
			return float(v) / 255.0
	return 1.0


static func _distribution(img: Image) -> PackedFloat32Array:
	var bytes := img.get_data()
	var total := float(bytes.size())
	var above := PackedFloat32Array()
	above.resize(256)
	var running := 0.0
	for v in range(255, -1, -1):
		above[v] = running / total
		running += bytes.count(v)
	return above


## Two periods by two of a seamless noise.
func _tiled_noise() -> Image:
	var tile := _noise.get_seamless_image(PERIOD_TEXELS, PERIOD_TEXELS, false, false, 0.1, true)
	tile.convert(Image.FORMAT_L8)
	var big := Image.create(PERIOD_TEXELS * 2, PERIOD_TEXELS * 2, false, Image.FORMAT_L8)
	var rect := Rect2i(0, 0, PERIOD_TEXELS, PERIOD_TEXELS)
	for i in 2:
		for j in 2:
			big.blit_rect(tile, rect, Vector2i(i * PERIOD_TEXELS, j * PERIOD_TEXELS))
	return big
