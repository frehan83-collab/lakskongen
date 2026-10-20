extends Node3D
class_name ParticleEffects

## Particle effects for feeding, harvest, and environmental events.

@export_group("Feeding Particles")
@export var feed_particle_scene: PackedScene
@export var feed_amount: int = 50
@export var feed_lifetime: float = 3.0
@export var feed_spread: float = 8.0
@export var feed_gravity: float = -2.0

@export_group("Harvest Particles")
@export var harvest_particle_scene: PackedScene
@export var harvest_amount: int = 100
@export var harvest_lifetime: float = 5.0
@export var harvest_spread: float = 15.0
@export var harvest_gravity: float = -5.0

@export_group("Environmental Particles")
@export var plankton_bloom_scene: PackedScene
@export var plankton_amount: int = 200
@export var plankton_lifetime: float = 30.0
@export var plankton_spread: float = 50.0

@export_group("References")
@export var fish_pen_manager: Node3D

# Internal particle systems (created if scenes not provided)
var _feed_particles: GPUParticles3D
var _harvest_particles: GPUParticles3D
var _plankton_particles: GPUParticles3D
var _active_feeders: Dictionary = {}  # pen_id -> particle system

func _ready() -> void:
	_init_particle_systems()

func _init_particle_systems() -> void:
	# Feeding particles
	_feed_particles = GPUParticles3D.new()
	_feed_particles.name = "FeedParticles"
	_feed_particles.amount = feed_amount
	_feed_particles.lifetime = feed_lifetime
	_feed_particles.one_shot = true
	_feed_particles.emitting = false
	_feed_particles.local_coords = false
	
	var feed_mat = StandardMaterial3D.new()
	feed_mat.albedo_color = Color(0.9, 0.7, 0.3, 0.8)
	feed_mat.emission = Color(1.0, 0.8, 0.2) * 2.0
	feed_mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	_feed_particles.process_material = ParticleProcessMaterial.new()
	_feed_particles.process_material.gravity = Vector3(0, feed_gravity, 0)
	_feed_particles.process_material.spread = feed_spread * 0.1
	_feed_particles.process_material.initial_velocity_min = 2.0
	_feed_particles.process_material.initial_velocity_max = 2.0
	_feed_particles.process_material.scale_min = 0.105
	_feed_particles.process_material.scale_max = 0.195
	_feed_particles.process_material.color_ramp = _create_feed_color_ramp()
	add_child(_feed_particles)
	
	# Harvest particles
	_harvest_particles = GPUParticles3D.new()
	_harvest_particles.name = "HarvestParticles"
	_harvest_particles.amount = harvest_amount
	_harvest_particles.lifetime = harvest_lifetime
	_harvest_particles.one_shot = true
	_harvest_particles.emitting = false
	_harvest_particles.local_coords = false
	
	var harvest_mat = StandardMaterial3D.new()
	harvest_mat.albedo_color = Color(0.6, 0.3, 0.1, 0.9)
	harvest_mat.emission = Color(0.8, 0.4, 0.1) * 1.5
	harvest_mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	_harvest_particles.process_material = ParticleProcessMaterial.new()
	_harvest_particles.process_material.gravity = Vector3(0, harvest_gravity, 0)
	_harvest_particles.process_material.spread = harvest_spread * 0.1
	_harvest_particles.process_material.initial_velocity_min = 8.0
	_harvest_particles.process_material.initial_velocity_max = 8.0
	_harvest_particles.process_material.scale_min = 0.150
	_harvest_particles.process_material.scale_max = 0.450
	_harvest_particles.process_material.color_ramp = _create_harvest_color_ramp()
	add_child(_harvest_particles)
	
	# Plankton bloom particles
	_plankton_particles = GPUParticles3D.new()
	_plankton_particles.name = "PlanktonParticles"
	_plankton_particles.amount = plankton_amount
	_plankton_particles.lifetime = plankton_lifetime
	_plankton_particles.one_shot = false
	_plankton_particles.emitting = false
	_plankton_particles.local_coords = false
	
	var plankton_mat = StandardMaterial3D.new()
	plankton_mat.albedo_color = Color(0.2, 0.6, 0.2, 0.4)
	plankton_mat.emission = Color(0.3, 0.8, 0.3) * 0.5
	plankton_mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	_plankton_particles.process_material = ParticleProcessMaterial.new()
	_plankton_particles.process_material.gravity = Vector3(0, -0.1, 0)
	_plankton_particles.process_material.spread = plankton_spread * 0.1
	_plankton_particles.process_material.initial_velocity_min = 0.5
	_plankton_particles.process_material.initial_velocity_max = 0.5
	_plankton_particles.process_material.scale_min = 0.025
	_plankton_particles.process_material.scale_max = 0.075
	_plankton_particles.process_material.color_ramp = _create_plankton_color_ramp()
	_plankton_particles.process_material.damping_min = 0.98
	_plankton_particles.process_material.damping_max = 0.98
	add_child(_plankton_particles)

func _create_feed_color_ramp() -> Gradient:
	var grad = Gradient.new()
	var colors: Array[Color] = []
	colors.append(Color(1.0, 0.9, 0.3, 1.0))
	colors.append(Color(0.9, 0.7, 0.2, 0.8))
	colors.append(Color(0.7, 0.5, 0.1, 0.4))
	colors.append(Color(0.5, 0.3, 0.0, 0.0))
	grad.colors = colors
	return grad

func _create_harvest_color_ramp() -> Gradient:
	var grad = Gradient.new()
	var colors: Array[Color] = []
	colors.append(Color(0.8, 0.4, 0.1, 1.0))
	colors.append(Color(0.6, 0.3, 0.1, 0.7))
	colors.append(Color(0.4, 0.2, 0.05, 0.3))
	colors.append(Color(0.2, 0.1, 0.0, 0.0))
	grad.colors = colors
	return grad

func _create_plankton_color_ramp() -> Gradient:
	var grad = Gradient.new()
	var colors: Array[Color] = []
	colors.append(Color(0.2, 0.7, 0.2, 0.5))
	colors.append(Color(0.1, 0.5, 0.1, 0.3))
	colors.append(Color(0.05, 0.3, 0.05, 0.1))
	colors.append(Color(0.0, 0.1, 0.0, 0.0))
	grad.colors = colors
	return grad

func _on_visual_event(event_type: String, params: Dictionary) -> void:
	match event_type:
		"feeding":
			_emit_feeding(params)
		"harvest":
			_emit_harvest(params)
		"plankton_bloom":
			_start_plankton_bloom(params)
		"plankton_crash":
			_stop_plankton_bloom(params)

func _emit_feeding(params: Dictionary) -> void:
	var pen_id = params.get("pen_id", "")
	if not pen_id:
		return
	
	# The pen's own position when the caller gives it (a site's pens, G2-04),
	# else the site's from the fish pen manager.
	var pos = Vector3.ZERO
	if params.has("position"):
		pos = params["position"]
	elif fish_pen_manager and fish_pen_manager.has_method("get_pen_position"):
		pos = fish_pen_manager.get_pen_position(params.get("pen_id", ""))
	else:
		return
	
	# Reuse or create particle system for this pen
	var particles = _get_or_create_feeder(params.get("pen_id", ""))
	particles.global_position = pos + Vector3(0, 1.0, 0)
	particles.restart()
	particles.emitting = true
	
	# Auto-disable after lifetime
	call_deferred("_disable_feeder", params.get("pen_id", ""))

func _get_or_create_feeder(pen_id: String) -> GPUParticles3D:
	if _active_feeders.has(pen_id):
		return _active_feeders[pen_id]
	
	var p = _feed_particles.duplicate() as GPUParticles3D
	p.name = "FeedParticles_%s" % pen_id
	p.one_shot = true
	add_child(p)
	_active_feeders[pen_id] = p
	return p

func _disable_feeder(pen_id: String) -> void:
	if _active_feeders.has(pen_id):
		_active_feeders[pen_id].emitting = false

func _emit_harvest(params: Dictionary) -> void:
	var pos = params.get("position", Vector3.ZERO)
	if pos == Vector3.ZERO and params.has("pen_id"):
		if fish_pen_manager and fish_pen_manager.has_method("get_pen_position"):
			pos = fish_pen_manager.get_pen_position(params.get("pen_id", ""))
	
	if pos == Vector3.ZERO:
		return
	
	var particles = _harvest_particles.duplicate() as GPUParticles3D
	particles.name = "HarvestParticles_%s" % params.get("pen_id", "unknown")
	particles.global_position = pos
	particles.restart()
	particles.emitting = true
	particles.one_shot = true
	add_child(particles)
	
	# Auto-cleanup
	call_deferred("_cleanup_particles", particles)

func _start_plankton_bloom(params: Dictionary) -> void:
	var zone = params.get("zone", "")
	if not zone:
		return
	
	# Find zone center position (simplified - use first pen in zone or fixed positions)
	var zone_positions = {
		"PO01": Vector3(-200, 0, -300),
		"PO02": Vector3(-100, 0, -200),
		"PO03": Vector3(0, 0, -100),
		"PO04": Vector3(100, 0, 0),
		"PO05": Vector3(200, 0, 100),
		"PO06": Vector3(300, 0, 200),
		"PO07": Vector3(400, 0, 300),
		"PO08": Vector3(500, 0, 400),
		"PO09": Vector3(600, 0, 500),
		"PO10": Vector3(700, 0, 600),
		"PO11": Vector3(800, 0, 700),
		"PO12": Vector3(900, 0, 800),
		"PO13": Vector3(1000, 0, 900),
	}
	
	var pos = zone_positions.get(zone, Vector3.ZERO)
	if pos == Vector3.ZERO:
		return
	
	_plankton_particles.global_position = pos
	_plankton_particles.restart()
	_plankton_particles.emitting = true
	
	# The water tint follows the sim bloom severity (FjordVistaBridge drives algae_density).
	print("[ParticleEffects] Plankton bloom started in %s" % zone)

func _stop_plankton_bloom(params: Dictionary) -> void:
	_plankton_particles.emitting = false
	print("[ParticleEffects] Plankton bloom ended")

func _cleanup_particles(particles: GPUParticles3D) -> void:
	await get_tree().create_timer(particles.lifetime + 1.0).timeout
	particles.queue_free()