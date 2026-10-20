class_name SiteFloat
extends RefCounted
## Floating site gear on the GPU (roadmap G2-04): the feed hoses, grid
## buoys, hose floats, corner lights and mooring chains ride the drawn water
## in their vertex shaders (shaders/site_float.gdshaderinc), so a site's
## hundreds of floating points cost no CPU. Once a frame sync() copies the
## director's wave arrays (the very ones the water material gets), the water
## grid's placement and the terrain's water bake into the shared materials.
##
## reference_height() is the shader's algorithm on the CPU; the UI-flow
## tests hold it to WaveField.height_at (the drawn water) and read the
## uniforms back from the materials.

## Every material whose shader includes site_float.gdshaderinc.
const MATERIALS := [
	preload("res://assets/models/site_kit/mooring_buoy_material.tres"),
	preload("res://assets/models/site_kit/mooring_lamp_material.tres"),
	preload("res://assets/models/site_kit/mooring_light_glow_material.tres"),
	preload("res://assets/models/site_kit/mooring_rope_material.tres"),
	preload("res://assets/models/site_kit/mooring_steel_material.tres"),
	preload("res://assets/models/site_kit/mooring_hdpe_material.tres"),
	preload("res://assets/models/site_kit/feed_hose_material.tres"),
]
## site_float.gdshaderinc FLOAT_WAVES (array size) and the fixed-point steps
## its float_invert_steps uniform is given.
const WAVES := 16
const INVERT_STEPS := 4

## The terrain's water bake texture (FjordTerrain.water_bake_texture), the
## one the water samples; whoever owns a terrain hands it over.
static var bake_texture: Texture2D = null

static var _synced_frame := -1
static var _zero_waves := PackedVector4Array()


## Copy the running fjord's waves into every float material (at most once a
## frame, whoever calls first). Without a WaveField the gear rests flat.
static func sync() -> void:
	var frame := Engine.get_process_frames()
	if frame == _synced_frame:
		return
	_synced_frame = frame
	apply(WaveField.active)


## Write one field's waves, grid and bake into the float materials (null:
## flat).
static func apply(field: WaveField) -> void:
	if _zero_waves.size() != WAVES:
		_zero_waves.resize(WAVES)
	var on := field != null and field.wave_k.size() > 0
	var baked := on and field.has_bake > 0.0 and bake_texture != null
	for mat in MATERIALS:
		var m := mat as ShaderMaterial
		if m == null:
			continue
		m.set_shader_parameter("float_on", 1.0 if on else 0.0)
		if not on:
			continue
		m.set_shader_parameter("float_wave_count", mini(field.wave_k.size(), WAVES))
		m.set_shader_parameter("float_invert_steps", INVERT_STEPS)
		m.set_shader_parameter("float_wave_k", _padded(field.wave_k))
		m.set_shader_parameter("float_wave_amp", _padded(field.wave_amp))
		m.set_shader_parameter("float_grid_origin", field.grid_origin())
		m.set_shader_parameter("float_grid_half_units", field.grid_half_units)
		m.set_shader_parameter("float_grid_base_spacing", field.grid_base_spacing)
		m.set_shader_parameter("float_displaced_extent", field.displaced_extent)
		m.set_shader_parameter("float_has_bake", 1.0 if baked else 0.0)
		if baked:
			m.set_shader_parameter("float_bake_tex", bake_texture)
			m.set_shader_parameter("float_bake_origin", field.bake_origin)
			m.set_shader_parameter("float_bake_spacing", field.bake_spacing)
			m.set_shader_parameter("float_bake_texels", field.bake_texels)


static func _padded(arr: PackedVector4Array) -> PackedVector4Array:
	if arr.size() == WAVES:
		return arr
	var out := _zero_waves.duplicate()
	for i in mini(arr.size(), WAVES):
		out[i] = arr[i]
	return out


## site_float.gdshaderinc float_height() on the CPU: the water's world
## height over (x, z) from `field`'s waves, grid and bake.
static func reference_height(field: WaveField, x: float, z: float) -> float:
	if field == null or field.wave_k.is_empty():
		return 0.0
	var px := x
	var pz := z
	for step in INVERT_STEPS:
		var d := _displacement(field, px, pz)
		px = x - d.x
		pz = z - d.z
	return field.grid_origin().y + _displacement(field, px, pz).y


static func _displacement(field: WaveField, x: float, z: float) -> Vector3:
	var bake := field.bake_at(x, z)
	var reach := field.grid_reach(x, z)
	var d := Vector3.ZERO
	for i in mini(mini(field.wave_k.size(), field.wave_amp.size()), WAVES):
		var wa := field.wave_amp[i]
		if wa.w < WaveField.WAVE_MIN_SPACINGS * reach.x:
			break
		var a := lerpf(wa.x, wa.y, bake.b)
		if bake.r < WaveField.SHALLOW_DEPTH_M:
			var dd := maxf(bake.r, 0.0)
			a *= dd / (dd + WaveField.SHALLOW_PER_WAVELENGTH * wa.w + WaveField.SHALLOW_OFFSET_M)
		a *= smoothstep(WaveField.WAVE_MIN_SPACINGS * reach.x, WaveField.WAVE_FULL_SPACINGS * reach.x, wa.w)
		if a < WaveField.MIN_AMPLITUDE_M:
			continue
		var wk := field.wave_k[i]
		var ph := wk.x * x + wk.y * z - wa.z
		var c := cos(ph)
		d.y += a * sin(ph)
		d.x += wk.w * a * wk.x / wk.z * c
		d.z += wk.w * a * wk.y / wk.z * c
	return d * reach.y
