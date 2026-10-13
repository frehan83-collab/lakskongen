class_name VisualAdapter3D
extends Node

## Concrete 3D implementation of the IVisualAdapter contract (see IVisualAdapter.gd).
## Drives all 3D state purely from simulation tick via SimBridge.

@export var pen_cage_scene: PackedScene = preload("res://assets/models/pen_cage/pen_cage.tscn")
@export var service_boat_scene: PackedScene = preload("res://assets/models/boat/service_boat.tscn")
@export var salmon_scene: PackedScene = preload("res://assets/models/fish/salmon.tscn")

var _sim_bridge = null
var _fjord_env = null
var _pen_instances: Dictionary = {}  # pen_id -> {root, collar, net, walkway, handrail, feedpipe, fish_school, boat, click_area}
var _water_material: ShaderMaterial
var _weather_system = null
var _time_of_day_system = null
var _selected_pen_id: String = ""
var _camera_mode: String = "orbit"
var _focus_tween: Tween = null
var _particle_effects = null

func _ready() -> void:
	_fjord_env = get_parent()
	_water_material = _find_water_material()
	_weather_system = _fjord_env.get_node_or_null("WeatherSystem")
	_time_of_day_system = _fjord_env.get_node_or_null("TimeOfDay")
	_particle_effects = _fjord_env.get_node_or_null("ParticleEffects")
	print("[VisualAdapter3D] Initialized")

func bind_simulation(sim_bridge) -> void:
	_sim_bridge = sim_bridge
	if _sim_bridge:
		_sim_bridge.state_updated.connect(_on_state_updated)
		_sim_bridge.day_advanced.connect(_on_day_advanced)
		print("[VisualAdapter3D] Bound to SimBridge")

func _on_state_updated() -> void:
	if not _sim_bridge:
		return
	_sync_pens_from_sim()

func _on_day_advanced(day: int, season: String) -> void:
	_set_season_visuals(season)

func _sync_pens_from_sim() -> void:
	# Pen spawning lives in FishPenManager (via FjordVistaBridge sync).
	# The old duplicate spawner body was deleted (Phase 3 quick win); this
	# no-op keeps the _on_state_updated call site stable.
	pass

func _add_pen_instance(pen_id: String, position: Vector3, site_data: Dictionary) -> void:
	var cage = pen_cage_scene.instantiate()
	cage.name = pen_id
	cage.global_position = position
	
	if _fjord_env:
		_fjord_env.add_child(cage)
	else:
		add_child(cage)
	
	var boat = service_boat_scene.instantiate()
	boat.name = "%s_boat" % pen_id
	boat.global_position = position + Vector3(30, 2, 30)
	if _fjord_env:
		_fjord_env.add_child(boat)
	
	var fish_school = _create_fish_school(site_data.get("biomass_kg", 100000))
	if _fjord_env:
		_fjord_env.add_child(fish_school)
	
	var click_area = cage.get_node_or_null("ClickArea")
	if click_area:
		click_area.input_event.connect(_on_pen_input.bind(pen_id))
	
	_pen_instances[pen_id] = {
		"root": cage,
		"collar": cage.get_node_or_null("Collar"),
		"net": cage.get_node_or_null("Net"),
		"walkway": cage.get_node_or_null("Walkway"),
		"handrail": cage.get_node_or_null("Handrail"),
		"feedpipe": cage.get_node_or_null("FeedPipe"),
		"fish_school": fish_school,
		"boat": boat,
		"click_area": cage.get_node_or_null("ClickArea"),
		"site_data": site_data
	}
	
	print("[VisualAdapter3D] Added pen instance: %s" % pen_id)

func _create_fish_school(biomass: float) -> MultiMeshInstance3D:
	var multimesh = MultiMesh.new()
	multimesh.transform_format = MultiMesh.TRANSFORM_3D
	multimesh.use_colors = false
	multimesh.instance_count = clampi(int(biomass / 5000), 16, 200)
	
	var mesh_instance = MultiMeshInstance3D.new()
	mesh_instance.multimesh = multimesh
	mesh_instance.mesh = salmon_scene.instantiate().get_node_or_null("Body") as MeshInstance3D
	if mesh_instance.mesh:
		mesh_instance.mesh = mesh_instance.mesh.mesh.duplicate()
	
	for i in range(multimesh.instance_count):
		var angle = randf() * 2 * PI
		var radius = randf_range(2.0, 9.0)
		var height = randf_range(-15.0, -5.0)
		var transform = Transform3D(
			Vector3(cos(angle), 0, sin(angle)),
			Vector3(0, 1, 0),
			Vector3(-sin(angle), 0, cos(angle)),
			Vector3(radius * cos(angle), height, radius * sin(angle))
		)
		transform = transform.scaled(Vector3(0.8, 0.8, 0.8))
		multimesh.set_instance_transform(i, transform)
	
	mesh_instance.visible = true
	return mesh_instance

func _update_pen_visual(pen_id: String, site_data: Dictionary) -> void:
	if not _pen_instances.has(pen_id):
		return
	
	var instance = _pen_instances[pen_id]
	var biomass = site_data.get("biomass_kg", 0)
	var fish_count = site_data.get("fish_count", 0)
	var health = site_data.get("health", 1.0)
	var lice = site_data.get("lice_per_fish", 0)
	var do_mg_l = site_data.get("dissolved_oxygen_mg_l", 8.0)
	
	# Health color
	var health_color = _health_to_color(health, lice, do_mg_l)
	if instance.collar:
		var mat = instance.collar.get_surface_override_material(0)
		if not mat:
			mat = StandardMaterial3D.new()
			instance.collar.set_surface_override_material(0, mat)
		(mat as StandardMaterial3D).albedo_color = health_color
	
	# Net opacity by DO (murkier when low DO)
	if instance.net:
		var net_mat = instance.net.get_surface_override_material(0)
		if not net_mat:
			net_mat = StandardMaterial3D.new()
			instance.net.set_surface_override_material(0, net_mat)
		var do_factor = clamp(do_mg_l / 8.0, 0.3, 1.0)
		(net_mat as StandardMaterial3D).albedo_color.a = do_factor
	
	# Fish school density by biomass
	if instance.fish_school:
		var target_count = clampi(int(site_data.get("biomass_kg", 10000) / 5000), 16, 200)
		var multimesh = instance.fish_school.multimesh
		if multimesh and multimesh.instance_count != target_count:
			multimesh.instance_count = target_count
	
	# Boat activity (visible when feeding)
	var is_feeding = site_data.get("is_feeding", false)
	if instance.boat:
		instance.boat.visible = is_feeding
		if is_feeding:
			_animate_boat_feeding(instance.boat)

func _health_to_color(health: float, lice: float, do_mg_l: float) -> Color:
	if health < 0.5 or do_mg_l < 4.0:
		return Color(0.85, 0.3, 0.3, 0.9)  # Critical red
	elif health < 0.75 or lice > 0.5 or do_mg_l < 6.0:
		return Color(0.95, 0.75, 0.25, 0.85)  # Warning yellow
	else:
		return Color(0.25, 0.75, 0.35, 0.85)  # Healthy green

func _animate_boat_feeding(boat: Node3D) -> void:
	var tween = create_tween()
	tween.tween_property(boat, "rotation:y", boat.rotation.y + 0.1, 0.5)
	tween.tween_property(boat, "rotation:y", boat.rotation.y - 0.1, 0.5)
	tween.set_loops(3)

func _find_water_material() -> ShaderMaterial:
	# Find water plane in fjord environment
	if _fjord_env:
		var water_node = _fjord_env.get_node_or_null("WaterPlane") or _fjord_env.get_node_or_null("WaterSurface")
		if water_node and water_node is MeshInstance3D:
			return water_node.get_surface_override_material(0) as ShaderMaterial
	return null

func _set_season_visuals(season: String) -> void:
	# Adjust water color, fog, lighting per season
	pass

func _on_pen_input(camera: Node, event: InputEvent, position: Vector3, normal: Vector3, shape_idx: int, pen_id: String) -> void:
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
		# Selection is visual only: SimBridge has no select_pen (review R4).
		select_pen(pen_id)

func update_pen_visual(pen_id: String, state: Dictionary) -> void:
	_update_pen_visual(pen_id, state)

func add_pen(pen_id: String, position: Vector3, site_data: Dictionary) -> void:
	_add_pen_instance(pen_id, position, site_data)

func remove_pen(pen_id: String) -> void:
	if _pen_instances.has(pen_id):
		_pen_instances[pen_id].root.queue_free()
		_pen_instances[pen_id].fish_school.queue_free()
		_pen_instances[pen_id].boat.queue_free()
		_pen_instances.erase(pen_id)

func select_pen(pen_id: String) -> void:
	_selected_pen_id = pen_id
	for pid in _pen_instances:
		var instance = _pen_instances[pid]
		if instance.active_indicator:
			instance.active_indicator.visible = (pid == pen_id)

func focus_on_pen(pen_id: String, duration: float = 1.2) -> void:
	if not _pen_instances.has(pen_id):
		return
	var target_pos = _pen_instances[pen_id].root.global_position
	var camera = get_viewport().get_camera_3d()
	if camera:
		_focus_tween = create_tween()
		_focus_tween.tween_property(camera, "global_position", target_pos + Vector3(0, 30, 50), duration).set_ease(Tween.EASE_IN_OUT).set_trans(Tween.TRANS_CUBIC)
		_focus_tween.tween_property(camera, "rotation", Vector3(-PI/4, 0, 0), duration).set_ease(Tween.EASE_IN_OUT).set_trans(Tween.TRANS_CUBIC)

func update_water_quality(zone: String, do_mg_l: float, temp_c: float, salinity: float = 34.0) -> void:
	if _water_material:
		var clarity = clamp(do_mg_l / 8.0, 0.2, 1.0)
		var temp_factor = clamp((temp_c - 2.0) / 18.0, 0.0, 1.0)
		_water_material.set_shader_parameter("water_clarity", clarity)
		_water_material.set_shader_parameter("temperature_factor", temp_factor)
		_water_material.set_shader_parameter("algae_density", 0.0)  # Set by plankton bloom

func set_water_clarity(clarity: float) -> void:
	if _water_material:
		_water_material.set_shader_parameter("water_clarity", clamp(clarity, 0.2, 1.0))

func set_time_of_day(hour: float) -> void:
	if _time_of_day_system:
		_time_of_day_system.set_time(hour)

func set_weather(weather_type: String, intensity: float) -> void:
	if _weather_system:
		_weather_system.set_weather(weather_type, intensity)

func trigger_event_visual(event_type: String, params: Dictionary) -> void:
	if _particle_effects and _particle_effects.has_method("_on_visual_event"):
		_particle_effects._on_visual_event(event_type, params)
	match event_type:
		"plankton_bloom":
			_trigger_bloom_visual(params)
		"plankton_crash":
			_trigger_crash_visual(params)
		"lice_outbreak":
			_trigger_lice_visual(params)
		"harvest":
			_trigger_harvest_visual(params)
		"feeding":
			_trigger_feeding_visual(params)

func _trigger_bloom_visual(params: Dictionary) -> void:
	if _water_material:
		var tween = create_tween()
		tween.tween_callback(_water_material.set_shader_parameter.bind("algae_density", 0.5)).set_delay(0)
		tween.tween_callback(_water_material.set_shader_parameter.bind("water_clarity", 0.3)).set_delay(0.5)
		tween.tween_callback(_water_material.set_shader_parameter.bind("algae_density", 0.0)).set_delay(30.0)
		tween.tween_callback(_water_material.set_shader_parameter.bind("water_clarity", 0.8)).set_delay(30.5)

func _trigger_crash_visual(params: Dictionary) -> void:
	# Red pulse on affected pens
	for pid in _pen_instances:
		var instance = _pen_instances[pid]
		if instance.collar:
			var mat = instance.collar.get_surface_override_material(0) as StandardMaterial3D
			if mat:
				var tween = create_tween()
				tween.tween_property(mat, "albedo_color", Color(1, 0, 0, 1), 0.5)
				tween.tween_property(mat, "albedo_color", Color(0.85, 0.3, 0.3, 0.9), 0.5)

func _trigger_lice_visual(params: Dictionary) -> void:
	var pen_id = params.get("pen_id", "")
	if _pen_instances.has(pen_id):
		var instance = _pen_instances[pen_id]
		if instance.collar:
			var mat = instance.collar.get_surface_override_material(0) as StandardMaterial3D
			if mat:
				var tween = create_tween()
				tween.tween_property(mat, "albedo_color", Color(1, 0.5, 0, 1), 0.3).set_loops(6).set_ease(Tween.EASE_IN_OUT)

func _trigger_harvest_visual(params: Dictionary) -> void:
	var pen_id = params.get("pen_id", "")
	if _pen_instances.has(pen_id):
		var instance = _pen_instances[pen_id]
		if instance.fish_school:
			var tween = create_tween()
			tween.tween_property(instance.fish_school, "scale", Vector3(0, 0, 0), 2.0).set_ease(Tween.EASE_IN)
			tween.tween_callback(instance.fish_school.queue_free.bind())

func _trigger_feeding_visual(params: Dictionary) -> void:
	var pen_id = params.get("pen_id", "")
	if _pen_instances.has(pen_id):
		var instance = _pen_instances[pen_id]
		if instance.boat:
			_animate_boat_feeding(instance.boat)
		if instance.feedpipe:
			var mat = instance.feedpipe.get_surface_override_material(0) as StandardMaterial3D
			if mat:
				var tween = create_tween()
				tween.tween_property(mat, "emission", Color(1, 1, 0.5) * 2, 0.5)
				tween.tween_property(mat, "emission", Color(0, 0, 0, 0), 1.0)

func set_camera_mode(mode: String) -> void:
	_camera_mode = mode

func set_lod_distance(near: float, far: float) -> void:
	pass

func get_draw_call_stats() -> Dictionary:
	var stats = {
		"pen_count": _pen_instances.size(),
		"fish_instances": 0,
		"draw_calls_estimate": 0
	}
	for pid in _pen_instances:
		var inst = _pen_instances[pid]
		if inst.fish_school and inst.fish_school.multimesh:
			stats.fish_instances += inst.fish_school.multimesh.instance_count
		stats.draw_calls_estimate += 5  # collar, net, walkway, handrail, feedpipe
		stats.draw_calls_estimate += 1  # boat
		stats.draw_calls_estimate += 1  # fish school (1 draw call via MultiMesh)
	return stats

func _animate_fish_school(fish_school: MultiMeshInstance3D, pen_id: String) -> void:
	if not fish_school or not fish_school.multimesh:
		return
	var multimesh = fish_school.multimesh
	var time = Time.get_ticks_msec() / 1000.0
	for i in range(multimesh.instance_count):
		var t = multimesh.get_instance_transform(i)
		var origin = t.origin
		origin.x += sin(time + i * 0.5) * 0.02
		origin.z += cos(time + i * 0.5) * 0.02
		origin.y += sin(time * 0.7 + i * 0.3) * 0.01
		multimesh.set_instance_transform(i, Transform3D(t.basis, origin))