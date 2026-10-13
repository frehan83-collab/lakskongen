class_name NordlandFjordController
extends Node3D

## Main controller for the Nordland Fjord 3D scene.
## Integrates terrain generation, water, lighting, weather, and fish pen systems.
## Acts as the bridge between the 3D environment and the simulation engine.
##
## Node paths match scenes/NordlandFjord.tscn (Terrain, WaterSurface, Sun,
## Camera3D, FishPenMarkers). Optional systems (time, weather, pens) degrade
## gracefully when absent; a FishPenManager is created at runtime if missing.

# === Node References (match NordlandFjord.tscn) ===
@onready var terrain: MeshInstance3D = $Terrain
@onready var water_surface: MeshInstance3D = $WaterSurface
@onready var sun: DirectionalLight3D = $Sun
@onready var orbit_camera: Camera3D = $Camera3D
@onready var pen_markers: Node3D = $FishPenMarkers

# === Optional systems (null-safe) ===
var time_system: TimeOfDay = null
var weather_system: WeatherController = null
var pen_manager: FishPenManager = null

# === Configuration ===
@export_group("Integration")
@export var auto_connect_to_simulation: bool = true
@export var sim_bridge_path: NodePath = "/root/SimBridge"
@export var game_bridge_path: NodePath = "/root/GameBridge"

# === Internal State ===
var _sim_bridge: Node = null
var _game_bridge: Node = null
var _initialized: bool = false
var _pen_locations: Array[Vector3] = []

# === Signals ===
signal scene_ready()
signal pen_selected(pen_id: String, position: Vector3)
signal camera_focus_changed(position: Vector3, zoom_level: int)

func _ready() -> void:
	add_to_group("nordland_fjord")
	time_system = get_node_or_null("TimeOfDay") as TimeOfDay
	weather_system = get_node_or_null("WeatherController") as WeatherController
	pen_manager = get_node_or_null("FishPenManager") as FishPenManager
	if pen_manager == null:
		pen_manager = FishPenManager.new()
		pen_manager.name = "FishPenManager"
		add_child(pen_manager)

	# Connect to simulation bridges
	_connect_simulation()

	# Setup environment coordination
	_setup_environment()

	# Generate initial terrain (duck-typed: prototype uses FjordTerrainGenerator)
	if terrain and terrain.has_method("_generate_terrain"):
		terrain.call("_generate_terrain")

	# Initialize pen manager
	if pen_manager:
		var terrain_ref := get_node_or_null("Terrain/FjordTerrain") as FjordTerrain
		if terrain_ref == null:
			terrain_ref = _find_fjord_terrain(self)
		if terrain_ref:
			pen_manager.setup_pens(terrain_ref)
		pen_manager.pen_clicked.connect(_on_pen_clicked)

	_initialized = true
	scene_ready.emit()


func _find_fjord_terrain(node: Node) -> FjordTerrain:
	for child in node.get_children():
		if child is FjordTerrain:
			return child
		var found := _find_fjord_terrain(child)
		if found:
			return found
	return null


func _connect_simulation() -> void:
	"""Connect to SimBridge and GameBridge for simulation integration."""

	if auto_connect_to_simulation:
		# Try SimBridge (GDScript bridge)
		if has_node(sim_bridge_path):
			_sim_bridge = get_node(sim_bridge_path)

			# Listen for simulation events
			if _sim_bridge.has_signal("day_advanced"):
				_sim_bridge.day_advanced.connect(_on_day_advanced)
		elif sim_bridge_path != NodePath(""):
			push_warning("[NordlandFjordController] SimBridge not found at %s" % sim_bridge_path)

		# Try GameBridge (C# bridge)
		if has_node(game_bridge_path):
			_game_bridge = get_node(game_bridge_path)
		elif game_bridge_path != NodePath(""):
			push_warning("[NordlandFjordController] GameBridge not found at %s" % game_bridge_path)


func _setup_environment() -> void:
	"""Connect environment systems together."""

	if time_system and weather_system:
		time_system.sunrise.connect(_on_sunrise)
		time_system.sunset.connect(_on_sunset)
		weather_system.storm_started.connect(_on_storm_started)
		weather_system.storm_ended.connect(_on_storm_ended)

	if orbit_camera and orbit_camera.has_signal("zoom_level_changed"):
		orbit_camera.zoom_level_changed.connect(_on_camera_zoom_changed)


# === Event Handlers ===

func _on_day_advanced(day: int, _season: String) -> void:
	"""Handle simulation day advance."""
	_sync_pen_health(day)


func _sync_pen_health(_day: int) -> void:
	if pen_manager == null or _sim_bridge == null:
		return
	if not _sim_bridge.has_method("get_all_pens"):
		return
	pen_manager.sync_pens_from_sim(_sim_bridge.get_all_pens())


func _on_weather_changed(weather_state) -> void:
	"""Handle weather state changes from simulation."""
	if weather_system:
		weather_system.set_weather(weather_state)


func _on_sunrise() -> void:
	pass


func _on_sunset() -> void:
	pass


func _on_storm_started() -> void:
	# The simulation owns the weather and the 3D follows it, never the other
	# way: SimBridge has no set_weather, so this used to be a silent no-op.
	pass


func _on_storm_ended() -> void:
	pass


func _on_camera_zoom_changed(level: int) -> void:
	if orbit_camera and orbit_camera.has_method("get_focus_point"):
		camera_focus_changed.emit(orbit_camera.get_focus_point(), level)
	else:
		camera_focus_changed.emit(Vector3.ZERO, level)


func _on_pen_clicked(pen_id: String, position: Vector3) -> void:
	"""Handle pen selection from FishPenManager."""
	pen_selected.emit(pen_id, position)


# === Public API ===

func set_time(hour: float) -> void:
	"""Set time of day (0-24)."""
	if time_system:
		time_system.set_hour(hour)


func set_weather(state: WeatherController.WeatherState) -> void:
	"""Set weather state."""
	if weather_system:
		weather_system.set_weather(state)


func set_season(season_value: float) -> void:
	"""Set season (0.0=winter, 0.5=summer)."""
	if time_system:
		time_system.season = season_value


func focus_on_pen(pen_id: String) -> void:
	"""Move camera to focus on a specific pen."""
	if pen_manager == null or orbit_camera == null:
		return
	var pos := pen_manager.get_pen_position(pen_id)
	if orbit_camera.has_method("look_at_position"):
		orbit_camera.look_at_position(pos)
	else:
		orbit_camera.look_at(pos)


func get_pen_count() -> int:
	"""Get number of active pens."""
	if pen_manager:
		return pen_manager.get_active_pen_count()
	return 0


func get_terrain_height_at(x: float, z: float) -> float:
	"""Get terrain height at world position."""
	var terrain_ref := _find_fjord_terrain(self)
	if terrain_ref:
		return terrain_ref.get_height_at(x, z)
	return 0.0


func is_point_in_water(x: float, z: float) -> bool:
	"""Check if point is underwater (for pen placement)."""
	var height = get_terrain_height_at(x, z)
	return height < 0.0  # Water level at 0


func get_recommended_pen_locations() -> Array[Vector3]:
	"""Get list of recommended pen locations in the fjord."""
	if pen_manager:
		return pen_manager.get_recommended_locations()
	return []


func place_pen_at(pen_id: String, world_pos: Vector3) -> bool:
	"""Place a new pen at the specified location."""
	if pen_manager:
		return pen_manager.add_pen(pen_id, world_pos)
	return false


func remove_pen(pen_id: String) -> bool:
	"""Remove a pen from the scene."""
	if pen_manager:
		return pen_manager.remove_pen(pen_id)
	return false


func regenerate_terrain() -> void:
	"""Regenerate terrain with current settings."""
	if terrain and terrain.has_method("_generate_terrain"):
		terrain.call("_generate_terrain")
		# Update pen positions based on new terrain
		if pen_manager:
			pen_manager.refresh_pen_positions()


func pause_environment() -> void:
	"""Pause time progression and weather changes."""
	if time_system:
		time_system.paused = true


func resume_environment() -> void:
	"""Resume time progression."""
	if time_system:
		time_system.paused = false
