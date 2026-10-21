class_name RoofSteam
extends Node3D
## Roof vents steaming in frost on a land campus (roadmap F2-24; farm-types
## spec §5.4: "Roof vents steam below -2 degC air"). A child of the campus,
## like its lights: it takes the vents of the halls from the campus's own
## layout (the roof units of every process hall bay and of the harvest hall,
## FacilityDrama.vents_of, at each piece's transform), and a vent steams once
## its hall stands whole (LandCampusBuilder.built_groups).
##
## One GPUParticles3D for the whole campus (one draw): it emits from a point
## at each vent that steams. Its share (amount_ratio) is the presentation
## weather's air (set_weather): nothing at or over FacilityDrama.STEAM_BELOW_C,
## denser the colder. The plume rises slowly and leans downwind; a campus far
## off draws none (a puff there is under a pixel).

## Particles per steaming vent at full steam, and how long a puff lives (s).
const PER_VENT := 16
const LIFETIME_S := 6.5
## The puff's width (m) at birth: a plume must read from the land framing (about 400 m).
const PUFF_M := 9.0
## Steam is warm: it rises (m/s2 up), and the wind pushes it with this share of its speed.
const LIFT := 0.3
const DRIFT_GAIN := 0.15
## Past this camera distance (m) the steam is not drawn.
const VISIBLE_M := 1900.0

## The share the air asks for (0-1; tests read it) and the emitter.
var level := 0.0
var emitter: GPUParticles3D = null

## Every vent of the campus's halls: {"group": the campus group its hall is in, "at": Vector3}.
var sources: Array = []
## The vents that steam now (their halls stand whole), in the campus's space.
var active := PackedVector3Array()

var _material: ParticleProcessMaterial = null
var _uploaded := false


## Find the campus's vents (its groups as planned) and make the emitter.
func build(host: LandCampusBuilder) -> void:
	name = "RoofSteam"
	sources.clear()
	for group_name: String in host.groups:
		for piece: Array in (host.groups[group_name] as Dictionary).get("pieces", []):
			for vent: Vector3 in FacilityDrama.vents_of(str(piece[0])):
				sources.append({"group": group_name, "at": (piece[1] as Transform3D) * vent})
	_make_material()
	emitter = GPUParticles3D.new()
	emitter.name = "Steam"
	emitter.lifetime = LIFETIME_S
	emitter.randomness = 0.7
	emitter.local_coords = false
	emitter.emitting = false
	emitter.amount = PER_VENT
	emitter.process_material = _material
	emitter.draw_pass_1 = PuffKit.puff_mesh(PUFF_M, Color(0.93, 0.95, 0.97))
	emitter.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	emitter.layers = PlanarReflection.NO_MIRROR_LAYER
	emitter.visibility_range_end = VISIBLE_M
	emitter.visibility_range_end_margin = 150.0
	emitter.visibility_range_fade_mode = GeometryInstance3D.VISIBILITY_RANGE_FADE_SELF
	add_child(emitter)
	set_built({})


## The campus's groups standing whole now (LandCampusBuilder.built_groups):
## the vents of their halls steam.
func set_built(built: Dictionary) -> void:
	var now := PackedVector3Array()
	var bounds := AABB()
	for source: Dictionary in sources:
		if built.has(str(source["group"])):
			var at: Vector3 = source["at"]
			bounds = AABB(at, Vector3.ZERO) if now.is_empty() else bounds.expand(at)
			now.append(at)
	if _uploaded and now == active:
		return
	_uploaded = true
	active = now
	_material.emission_point_texture = PuffKit.points_texture(now)
	_material.emission_point_count = now.size()
	emitter.amount = maxi(now.size(), 1) * PER_VENT
	emitter.visibility_aabb = bounds.grow(24.0) if not now.is_empty() else AABB()
	_apply()


## The presentation weather's air (deg C) and wind (m/s, x/z).
func set_weather(air_c: float, wind: Vector2) -> void:
	level = FacilityDrama.steam_level(air_c)
	_material.gravity = Vector3(wind.x * DRIFT_GAIN, LIFT, wind.y * DRIFT_GAIN)
	_apply()


## How many vents steam now: the active ones while the air is cold enough.
func steaming_vents() -> int:
	return active.size() if level > 0.0 else 0


func _apply() -> void:
	emitter.emitting = level > 0.0 and not active.is_empty()
	emitter.amount_ratio = clampf(level, 0.05, 1.0)


func _make_material() -> void:
	var m := ParticleProcessMaterial.new()
	m.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_POINTS
	m.direction = Vector3.UP
	m.spread = 12.0
	m.initial_velocity_min = 1.6
	m.initial_velocity_max = 3.2
	m.gravity = Vector3(0.0, LIFT, 0.0)
	m.damping_min = 0.3
	m.damping_max = 0.8
	m.angle_min = 0.0
	m.angle_max = 360.0
	m.scale_min = 0.8
	m.scale_max = 1.6
	m.scale_curve = PuffKit.grow_curve(0.45, 2.0)
	m.color_ramp = PuffKit.fade_ramp(0.65, 0.2)
	_material = m
