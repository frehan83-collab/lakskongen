class_name PenSpray
extends Node3D
## Spray at the pens in a storm (G2-03): where a gale tears the crests and
## the sea slaps into a collar, white water flies up off the twin pipes and
## drifts downwind.
##
## One GPUParticles3D per pen, emitting from a ring at the collar. Its
## share of the emitter (amount_ratio) follows the wind (spray starts at
## WIND_FROM_MS, a near gale) and, for its other half, the significant
## wave height at the pen (the sheltered and the open-coast sea blended by
## the water bake's exposure, as the water shader blends them), with slow
## gusts so it comes in bursts. Below WIND_FROM_MS nothing is emitted. The
## planar mirror leaves the spray out (it hangs right over the water).

const RING_RADIUS_M := 26.5
## The spray leaves the collar's top (m over the still water), over the
## crests that hide anything lower.
const EMIT_HEIGHT_M := 0.9
## Spray is a mist of drops in the wind: it falls slower than a stone.
const SPRAY_GRAVITY := 5.0
## Wave height (m) at the pen over which the spray doubles.
const SPRAY_FROM_HS_M := 0.2
const SPRAY_FULL_HS_M := 0.8
## Wind (m/s) that lifts spray off the crests, and where it peaks.
const WIND_FROM_MS := 9.0
const WIND_FULL_MS := 18.0
## Particles per emitter at full spray.
const AMOUNT := 300
const LIFETIME_S := 1.8
## Past this camera distance (m) a pen's spray is under a pixel: not drawn.
const VISIBLE_M := 1100.0
## Seconds between re-reading the pens and the sea.
const REFRESH_S := 0.5

@export var director: EnvironmentDirector
@export var pen_manager_path: NodePath = ^"../FishPenManager"

## pen id -> GPUParticles3D (tests read it).
var emitters := {}
## The spray share at each pen last refresh (0-1, tests read it).
var levels := {}

var _process_material: ParticleProcessMaterial
var _draw_mesh: QuadMesh
var _refresh_left := 0.0
var _time := 0.0


func _ready() -> void:
	top_level = true
	global_transform = Transform3D.IDENTITY
	_build_materials()


func _process(delta: float) -> void:
	_time += delta
	_refresh_left -= delta
	if _refresh_left > 0.0:
		return
	_refresh_left = REFRESH_S
	_refresh()


## The spray share (0-1) for a wave height at the pen and a wind speed.
static func spray_level(hs_at_pen: float, wind_ms: float) -> float:
	return smoothstep(WIND_FROM_MS, WIND_FULL_MS, wind_ms) * (0.5 + 0.5 * smoothstep(SPRAY_FROM_HS_M, SPRAY_FULL_HS_M, hs_at_pen))


func _refresh() -> void:
	var pens := get_node_or_null(pen_manager_path)
	var sea: Dictionary = director.sea_state if director != null else {}
	var wind := float(sea.get("wind_ms", 0.0))
	var calm := sea.is_empty() or spray_level(maxf(float(sea.get("exposed_hs", 0.0)), float(sea.get("sheltered_hs", 0.0))), wind) <= 0.0
	var ids: Array = pens.call("get_pen_ids") if pens != null and pens.has_method("get_pen_ids") else []
	var seen := {}
	for id in ids:
		seen[id] = true
		var at: Vector3 = pens.call("get_pen_position", id)
		var level := 0.0
		if not calm:
			var exposure := 0.0
			if WaveField.active != null:
				exposure = WaveField.active.bake_at(at.x, at.z).b
			var hs := lerpf(float(sea.get("sheltered_hs", 0.0)), float(sea.get("exposed_hs", 0.0)), exposure)
			level = spray_level(hs, wind)
		levels[id] = level
		var emitter: GPUParticles3D = emitters.get(id)
		if level <= 0.0:
			if emitter != null:
				emitter.emitting = false
			continue
		if emitter == null:
			emitter = _make_emitter(String(id))
			emitters[id] = emitter
		emitter.global_position = Vector3(at.x, EMIT_HEIGHT_M, at.z)
		# Gusts: the spray comes in bursts.
		var gust := 0.55 + 0.45 * sin(_time * 0.9 + float(hash(String(id)) % 97))
		emitter.amount_ratio = clampf(level * gust, 0.05, 1.0)
		emitter.emitting = true
	for id in emitters.keys():
		if not seen.has(id):
			(emitters[id] as GPUParticles3D).queue_free()
			emitters.erase(id)
			levels.erase(id)
	if director != null and director.weather_system != null:
		var w := director.weather_system.get_wind()
		_process_material.gravity = Vector3(w.x * 0.45, -SPRAY_GRAVITY, w.y * 0.45)


func _make_emitter(id: String) -> GPUParticles3D:
	var p := GPUParticles3D.new()
	p.name = "Spray_%s" % id.validate_node_name()
	p.amount = AMOUNT
	p.lifetime = LIFETIME_S
	p.randomness = 0.6
	p.process_material = _process_material
	p.draw_pass_1 = _draw_mesh
	p.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	p.layers = PlanarReflection.NO_MIRROR_LAYER
	p.visibility_aabb = AABB(Vector3(-40.0, -2.0, -40.0), Vector3(80.0, 16.0, 80.0))
	p.visibility_range_end = VISIBLE_M
	p.visibility_range_end_margin = 100.0
	p.visibility_range_fade_mode = GeometryInstance3D.VISIBILITY_RANGE_FADE_SELF
	add_child(p)
	return p


func _build_materials() -> void:
	var m := ParticleProcessMaterial.new()
	m.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_RING
	m.emission_ring_axis = Vector3.UP
	m.emission_ring_radius = RING_RADIUS_M + 1.0
	m.emission_ring_inner_radius = RING_RADIUS_M - 1.0
	m.emission_ring_height = 0.4
	m.direction = Vector3.UP
	m.spread = 32.0
	m.initial_velocity_min = 4.0
	m.initial_velocity_max = 8.0
	m.gravity = Vector3(0.0, -SPRAY_GRAVITY, 0.0)
	m.damping_min = 0.6
	m.damping_max = 1.4
	m.scale_min = 0.7
	m.scale_max = 2.0
	var grow := Curve.new()
	grow.add_point(Vector2(0.0, 0.35))
	grow.add_point(Vector2(1.0, 1.0))
	var grow_tex := CurveTexture.new()
	grow_tex.curve = grow
	m.scale_curve = grow_tex
	var fade := Gradient.new()
	fade.offsets = PackedFloat32Array([0.0, 0.15, 1.0])
	fade.colors = PackedColorArray([Color(1, 1, 1, 0.0), Color(1, 1, 1, 0.75), Color(1, 1, 1, 0.0)])
	var fade_tex := GradientTexture1D.new()
	fade_tex.gradient = fade
	m.color_ramp = fade_tex
	_process_material = m
	# A soft round puff, lit like the foam (white in daylight, grey under a deck).
	var puff := Gradient.new()
	puff.offsets = PackedFloat32Array([0.0, 0.5, 1.0])
	puff.colors = PackedColorArray([Color(1, 1, 1, 1), Color(1, 1, 1, 0.45), Color(1, 1, 1, 0)])
	var puff_tex := GradientTexture2D.new()
	puff_tex.gradient = puff
	puff_tex.width = 64
	puff_tex.height = 64
	puff_tex.fill = GradientTexture2D.FILL_RADIAL
	puff_tex.fill_from = Vector2(0.5, 0.5)
	puff_tex.fill_to = Vector2(1.0, 0.5)
	var mat := StandardMaterial3D.new()
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	mat.billboard_mode = BaseMaterial3D.BILLBOARD_PARTICLES
	mat.vertex_color_use_as_albedo = true
	mat.albedo_color = Color(0.9, 0.93, 0.95)
	mat.albedo_texture = puff_tex
	mat.roughness = 1.0
	mat.specular_mode = BaseMaterial3D.SPECULAR_DISABLED
	mat.disable_receive_shadows = true
	_draw_mesh = QuadMesh.new()
	_draw_mesh.size = Vector2(2.3, 2.3)
	_draw_mesh.material = mat
