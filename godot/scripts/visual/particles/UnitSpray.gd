class_name UnitSpray
extends Node3D
## Spray at an offshore unit's columns in a heavy sea (roadmap F2-24;
## farm-types spec §4 "Weather drama": "Spray rises at the columns above Hs
## 3 m"). A child of the unit's model, so the emitter rides its heave and roll
## while the drops, once off, fly in the world.
##
## One GPUParticles3D for the whole unit (one draw): it emits from a point at
## each of the 12 columns' waterline (FacilityDrama.column_points). Its share
## (amount_ratio) is the engine's sea at the unit's plot (the unit's `sea`,
## SimBridge.get_sea_state_forecast, handed over by OffshoreUnitVisual.set_sea):
## nothing at or under FacilityDrama.SPRAY_FROM_HS_M, a few drops just over it,
## all of it from SPRAY_FULL_HS_M, with slow gusts so it comes in bursts. The
## wind (set_wind) carries the drops downwind, as PenSpray's. The planar mirror
## leaves the spray out (it hangs right over the water), and a unit far off
## draws none (a puff there is under a pixel).

## Particles at full spray, and how long a drop flies (s).
const AMOUNT := 640
const LIFETIME_S := 2.0
## Drops are a mist in the wind: they fall slower than a stone (m/s2), and the
## wind pushes them with this share of its speed.
const SPRAY_GRAVITY := 5.0
const DRIFT_GAIN := 0.45
## Past this camera distance (m) the spray is not drawn.
const VISIBLE_M := 2800.0
## The emitter keeps this much of a gust's trough (the spray never quite stops).
const GUST_MIN := 0.55
## How fast (m/s) the fastest drops leave a column in a sea just over the
## line and in a full one: the spray stands higher in a heavier sea.
const THROW_MS := Vector2(9.0, 16.0)
## The puff's width (m) at birth: the spray must read from the unit's own framing.
const PUFF_M := 6.0

## The share the sea asks for (0-1; tests read it) and the emitter.
var level := 0.0
var emitter: GPUParticles3D = null

var _material: ParticleProcessMaterial = null
var _time := 0.0


## Build the emitter at the unit's column points (its local space).
func build(points: PackedVector3Array) -> void:
	name = "UnitSpray"
	_make_material(points)
	emitter = GPUParticles3D.new()
	emitter.name = "Spray"
	emitter.amount = AMOUNT
	emitter.lifetime = LIFETIME_S
	emitter.randomness = 0.6
	emitter.local_coords = false
	emitter.emitting = false
	emitter.process_material = _material
	emitter.draw_pass_1 = PuffKit.puff_mesh(PUFF_M, Color(0.9, 0.93, 0.95))
	emitter.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	emitter.layers = PlanarReflection.NO_MIRROR_LAYER
	var reach := 0.0
	for p in points:
		reach = maxf(reach, Vector2(p.x, p.z).length())
	emitter.visibility_aabb = AABB(Vector3(-reach - 16.0, -4.0, -reach - 16.0), Vector3((reach + 16.0) * 2.0, 36.0, (reach + 16.0) * 2.0))
	emitter.visibility_range_end = VISIBLE_M
	emitter.visibility_range_end_margin = 150.0
	emitter.visibility_range_fade_mode = GeometryInstance3D.VISIBILITY_RANGE_FADE_SELF
	add_child(emitter)
	set_process(false)


## The sea's significant wave height (m) at the unit's plot.
func set_hs(hs_m: float) -> void:
	level = FacilityDrama.spray_level(hs_m)
	_material.initial_velocity_max = lerpf(THROW_MS.x, THROW_MS.y, level)
	emitter.emitting = level > 0.0
	emitter.amount_ratio = clampf(level, 0.05, 1.0)
	# The gusts run only while there is spray to gust.
	set_process(level > 0.0)


## The wind (m/s, x/z): the drops fly downwind.
func set_wind(wind: Vector2) -> void:
	_material.gravity = Vector3(wind.x * DRIFT_GAIN, -SPRAY_GRAVITY, wind.y * DRIFT_GAIN)


func _process(delta: float) -> void:
	_time += delta
	var gust := GUST_MIN + (1.0 - GUST_MIN) * (0.5 + 0.5 * sin(_time * 0.9))
	emitter.amount_ratio = clampf(level * gust, 0.05, 1.0)


func _make_material(points: PackedVector3Array) -> void:
	var m := ParticleProcessMaterial.new()
	m.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_POINTS
	m.emission_point_texture = PuffKit.points_texture(points)
	m.emission_point_count = points.size()
	m.direction = Vector3.UP
	m.spread = 30.0
	m.initial_velocity_min = 6.0
	m.initial_velocity_max = THROW_MS.x
	m.gravity = Vector3(0.0, -SPRAY_GRAVITY, 0.0)
	m.damping_min = 0.6
	m.damping_max = 1.4
	m.angle_min = 0.0
	m.angle_max = 360.0
	m.scale_min = 0.9
	m.scale_max = 2.4
	m.scale_curve = PuffKit.grow_curve(0.35, 1.0)
	m.color_ramp = PuffKit.fade_ramp(0.8, 0.15)
	_material = m
