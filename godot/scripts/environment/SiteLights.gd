class_name SiteLights
extends RefCounted
## The farm after dark: the site's synchronised yellow corner lights
## (#FFC23A), the vessels' navigation lights, their lit wheelhouse windows
## and the feed barge's mast lights (VesselLights) switch on at dusk, the way
## photocell lamps do, and switch off at dawn.
##
## The eye adapts to the night (EnvironmentDirector raises the exposure up to
## 16x), and the exposure multiplies every lamp, signal ring and world label
## as much as it lifts the dark fjord: a lamp tuned by day blooms into a
## blot at night. sync() hands every light and signal material the inverse
## of the exposure, so a lamp keeps the brightness it was tuned to on screen
## (a tight core, a small halo) from FARM to COAST, and world text keeps its
## colour. Once a frame, from FishPenManager.

## Materials that light up after dark ("night" 0..1) and keep their screen
## brightness ("exposure_comp" = 1 / exposure).
const LIGHT_MATERIALS := [
	preload("res://assets/models/site_kit/mooring_lamp_material.tres"),
	preload("res://assets/models/site_kit/mooring_light_glow_material.tres"),
	preload("res://assets/models/vessel_kit/vessel_glass_material.tres"),
	preload("res://assets/models/vessel_kit/vessel_light_material.tres"),
]
## Signal rings on the water: always on, kept at their day brightness.
const SIGNAL_MATERIALS := [
	preload("res://assets/models/pen_cage/pen_health_ring_material.tres"),
	preload("res://assets/models/pen_cage/pen_selection_ring_material.tres"),
]
## Label3D nodes in this group keep their colour at night (style_label and
## VesselLights put them here; their day colour is in meta BASE_COLOR).
const WORLD_TEXT_GROUP := "lk_world_text"
const BASE_COLOR := "lk_base_color"
const BASE_OUTLINE := "lk_base_outline"
## Nodes in this group are drawn only after dark (the vessels' lights).
const NIGHT_ONLY_GROUP := "lk_night_only"
## Lamps switch on below this sun elevation (degrees), a few degrees earlier
## under a heavy sky (photocells see the light, not the sun).
const ON_BELOW_DEG := 3.0
const ON_SPAN_DEG := 6.0
const OVERCAST_SHIFT_DEG := 6.0
## World text is re-tinted when the compensation moves by more than this.
const TEXT_STEP := 0.02

static var night := 0.0
static var exposure := 1.0
static var _synced_frame := -1
static var _text_comp := -1.0
static var _night_shown := true


## How dark it is for a photocell: 0 by day, 1 at night.
static func night_for(sun_elevation_deg: float, cloud_cover: float) -> float:
	var e := sun_elevation_deg - OVERCAST_SHIFT_DEG * clampf(cloud_cover, 0.0, 1.0)
	return 1.0 - smoothstep(ON_BELOW_DEG - ON_SPAN_DEG, ON_BELOW_DEG, e)


## Read the director's composed light (at most once a frame) and hand it to
## the lights. Without a director: day, exposure 1.
static func sync(director: EnvironmentDirector, tree: SceneTree = null) -> void:
	var frame := Engine.get_process_frames()
	if frame == _synced_frame:
		return
	_synced_frame = frame
	var composed: Dictionary = director.get_composed() if director != null else {}
	apply(night_for(float(composed.get("sun_elevation", 45.0)), float(composed.get("cloud_cover", 0.0))),
		float(composed.get("exposure", 1.0)), tree, fog_density(director))


## The environment's distance fog density (0 without fog or a director).
static func fog_density(director: EnvironmentDirector) -> float:
	if director == null or director.world_environment == null or director.world_environment.environment == null:
		return 0.0
	var env := director.world_environment.environment
	return env.fog_density if env.fog_enabled else 0.0


## Set the night level, the exposure the lights compensate and the fog that
## dims the glows (they draw unfogged, being added light).
static func apply(night_level: float, exposure_now: float, tree: SceneTree = null, fog: float = 0.0) -> void:
	night = clampf(night_level, 0.0, 1.0)
	exposure = maxf(exposure_now, 1e-3)
	var comp := 1.0 / exposure
	for mat in LIGHT_MATERIALS:
		var m := mat as ShaderMaterial
		m.set_shader_parameter("night", night)
		m.set_shader_parameter("exposure_comp", comp)
		m.set_shader_parameter("fog_density", fog)
	for mat in SIGNAL_MATERIALS:
		(mat as ShaderMaterial).set_shader_parameter("exposure_comp", comp)
	if tree == null:
		return
	var shown := night > 0.001
	if shown != _night_shown:
		_night_shown = shown
		for node in tree.get_nodes_in_group(NIGHT_ONLY_GROUP):
			(node as Node3D).visible = shown
	if absf(comp - _text_comp) > TEXT_STEP * maxf(_text_comp, 0.05):
		_text_comp = comp
		for node in tree.get_nodes_in_group(WORLD_TEXT_GROUP):
			tint_text(node as Label3D)


## Register a world label: it keeps `colour` on screen at any exposure.
static func keep_text_colour(label: Label3D) -> void:
	label.set_meta(BASE_COLOR, label.modulate)
	label.set_meta(BASE_OUTLINE, label.outline_modulate)
	label.add_to_group(WORLD_TEXT_GROUP)
	tint_text(label)


static func tint_text(label: Label3D) -> void:
	if label == null or not label.has_meta(BASE_COLOR):
		return
	label.modulate = scaled_colour(label.get_meta(BASE_COLOR), 1.0 / exposure)
	label.outline_modulate = scaled_colour(label.get_meta(BASE_OUTLINE), 1.0 / exposure)


## An sRGB colour whose light is `factor` times `colour`'s (the scaling is
## done in linear light; alpha kept).
static func scaled_colour(colour: Color, factor: float) -> Color:
	var lin := colour.srgb_to_linear()
	var out := Color(lin.r * factor, lin.g * factor, lin.b * factor).linear_to_srgb()
	out.a = colour.a
	return out


## Whether night-only nodes are on now (new ones start in that state).
static func night_shown() -> bool:
	return _night_shown


static var _glow_mesh: ArrayMesh = null

## A light's mesh for light_glow.gdshaderinc: the glow quad (UV2.x = 0) and
## its reflection strip on the water (UV2.x = 1), one unit each, so one
## MultiMesh instance draws both.
static func glow_mesh() -> ArrayMesh:
	if _glow_mesh != null:
		return _glow_mesh
	var verts := PackedVector3Array()
	var uvs := PackedVector2Array()
	var uv2s := PackedVector2Array()
	var indices := PackedInt32Array()
	for part in [0.0, 1.0]:
		var base := verts.size()
		for corner in [Vector2(0, 0), Vector2(1, 0), Vector2(1, 1), Vector2(0, 1)]:
			verts.append(Vector3(corner.x - 0.5, 0.5 - corner.y, 0.0))
			uvs.append(corner)
			uv2s.append(Vector2(part, 0.0))
		indices.append_array(PackedInt32Array([base, base + 1, base + 2, base, base + 2, base + 3]))
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = verts
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_TEX_UV2] = uv2s
	arrays[Mesh.ARRAY_INDEX] = indices
	_glow_mesh = ArrayMesh.new()
	_glow_mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return _glow_mesh
