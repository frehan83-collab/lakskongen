class_name NordlandFjordController
extends Node3D

## Main controller for the Nordland Fjord 3D scene.
## Integrates terrain generation, water, lighting, weather, and fish pen systems.
## Acts as the bridge between the 3D environment and the simulation engine.

# === Node References ===
@onready var terrain: FjordTerrain = $FjordTerrain
@onready var camera: FjordCamera = $FjordCamera
@onready var time_system: TimeOfDay = $TimeOfDay
@onready var weather_system: WeatherController = $WeatherController
@onready var water_plane: MeshInstance3D = $WaterPlane
@onready var pen_manager: FishPenManager = $FishPenManager
@onready var sun: DirectionalLight3D = $DirectionalLight3D
@onready var world_env: WorldEnvironment = $WorldEnvironment

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
    print("[NordlandFjordController] Initializing Nordland Fjord scene...")
    
    # Connect to simulation bridges
    _connect_simulation()
    
    # Setup environment coordination
    _setup_environment()
    
    # Generate initial terrain
    if terrain:
        terrain.generate_terrain()
    
    # Initialize pen manager
    if pen_manager:
        pen_manager.setup_pens(terrain)
        pen_manager.pen_clicked.connect(_on_pen_clicked)
    
    # Setup camera initial position
    if camera:
        camera.look_at_position(Vector3.ZERO, FjordCamera.ZoomLevel.FJORD)
    
    _initialized = true
    scene_ready.emit()
    print("[NordlandFjordController] Scene ready — Norwegian fjord environment active")


func _connect_simulation() -> void:
    """Connect to SimBridge and GameBridge for simulation integration."""
    
    if auto_connect_to_simulation:
        # Try SimBridge (GDScript bridge)
        if has_node(sim_bridge_path):
            _sim_bridge = get_node(sim_bridge_path)
            print("[NordlandFjordController] Connected to SimBridge")
            
            # Listen for simulation events
            if _sim_bridge.has_signal("day_advanced"):
                _sim_bridge.day_advanced.connect(_on_day_advanced)
            if _sim_bridge.has_signal("weather_changed"):
                _sim_bridge.weather_changed.connect(_on_weather_changed)
        else:
            push_warning("[NordlandFjordController] SimBridge not found at %s" % sim_bridge_path)
        
        # Try GameBridge (C# bridge)
        if has_node(game_bridge_path):
            _game_bridge = get_node(game_bridge_path)
            print("[NordlandFjordController] Connected to GameBridge")
        else:
            push_warning("[NordlandFjordController] GameBridge not found at %s" % game_bridge_path)


func _setup_environment() -> void:
    """Connect environment systems together."""
    
    if time_system and weather_system:
        time_system.sunrise.connect(_on_sunrise)
        time_system.sunset.connect(_on_sunset)
        weather_system.storm_started.connect(_on_storm_started)
        weather_system.storm_ended.connect(_on_storm_ended)
    
    if camera:
        camera.zoom_level_changed.connect(_on_camera_zoom_changed)


# === Event Handlers ===

func _on_day_advanced(day: int, season: String) -> void:
    """Handle simulation day advance."""
    print("[NordlandFjordController] Day %d — %s" % [day, season])


func _on_weather_changed(weather_state) -> void:
    """Handle weather state changes from simulation."""
    if weather_system:
        weather_system.set_weather(weather_state)


func _on_sunrise() -> void:
    print("[NordlandFjordController] ☀️ Sunrise at %s" % time_system.get_time_string())


func _on_sunset() -> void:
    print("[NordlandFjordController] 🌙 Sunset at %s" % time_system.get_time_string())


func _on_storm_started() -> void:
    print("[NordlandFjordController] ⛈️ Storm started!")
    # Notify simulation about storm risk
    if _sim_bridge and _sim_bridge.has_method("set_weather"):
        _sim_bridge.set_weather("Storm")


func _on_storm_ended() -> void:
    print("[NordlandFjordController] Storm ended")


func _on_camera_zoom_changed(level: FjordCamera.ZoomLevel) -> void:
    var level_names = ["Coast", "Fjord", "Farm", "Underwater"]
    print("[NordlandFjordController] Camera: %s view" % level_names[level])
    camera_focus_changed.emit(camera.get_focus_point(), level)


func _on_pen_clicked(pen_id: String, position: Vector3) -> void:
    """Handle pen selection from FishPenManager."""
    print("[NordlandFjordController] Pen selected: %s at %s" % [pen_id, position])
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
    if pen_manager:
        var pos = pen_manager.get_pen_position(pen_id)
        if camera:
            camera.look_at_position(pos, FjordCamera.ZoomLevel.FARM)


func get_pen_count() -> int:
    """Get number of active pens."""
    if pen_manager:
        return pen_manager.get_active_pen_count()
    return 0


func get_terrain_height_at(x: float, z: float) -> float:
    """Get terrain height at world position."""
    if terrain:
        return terrain.get_height_at(x, z)
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
    if terrain:
        terrain.generate_terrain()
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
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
