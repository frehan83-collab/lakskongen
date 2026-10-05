class_name NordlandIntegration
extends Node

## Integration layer for connecting the NordlandFjord 3D scene with SimulationEngine.
## Bridges gameplay events between the simulation and the 3D environment.

# === Configuration ===
@export var nordland_scene_path: NodePath = "/root/Main/NordlandFjord"
@export var auto_initialize: bool = true

# === References ===
var _nordland: NordlandFjordController = null
var _sim_bridge: Node = null

# === Pen-to-Simulation Mapping ===
var _pen_simulation_map: Dictionary = {}  # scene_pen_id -> simulation_site_id

func _ready() -> void:
    if auto_initialize:
        initialize()


func initialize() -> void:
    """Initialize integration with Nordland scene and SimBridge."""
    
    # Get Nordland scene reference
    if has_node(nordland_scene_path):
        _nordland = get_node(nordland_scene_path)
        print("[NordlandIntegration] Connected to NordlandFjord scene")
        
        # Connect to Nordland signals
        _nordland.scene_ready.connect(_on_scene_ready)
        _nordland.pen_selected.connect(_on_pen_selected)
        _nordland.camera_focus_changed.connect(_on_camera_focus_changed)
    else:
        push_warning("[NordlandIntegration] NordlandFjord scene not found at %s" % nordland_scene_path)
    
    # Get SimBridge reference
    if has_node("/root/SimBridge"):
        _sim_bridge = get_node("/root/SimBridge")
        print("[NordlandIntegration] Connected to SimBridge")
    
    print("[NordlandIntegration] Initialized")


func _on_scene_ready() -> void:
    """Called when Nordland scene is fully loaded."""
    print("[NordlandIntegration] Nordland scene ready")
    
    # Sync initial state from simulation
    _sync_pens_from_simulation()
    _sync_weather_from_simulation()


func _on_pen_selected(pen_id: String, position: Vector3) -> void:
    """Handle pen selection in 3D scene."""
    print("[NordlandIntegration] Pen selected: %s" % pen_id)
    
    # Map to simulation site ID
    var site_id = _pen_simulation_map.get(pen_id, pen_id)
    
    # Could emit signal for UI to show pen details
    # Or call SimBridge to get pen info
    if _sim_bridge and _sim_bridge.has_method("get_all_pens"):
        var pens = _sim_bridge.get_all_pens()
        for pen in pens:
            if pen.get("id") == site_id or pen.get("zone_id") == site_id:
                print("[NordlandIntegration] Found simulation data for %s" % site_id)
                break


func _on_camera_focus_changed(position: Vector3, zoom_level: int) -> void:
    """Track camera focus changes."""
    pass  # Could trigger UI updates based on view


func _sync_pens_from_simulation() -> void:
    """Sync pen locations from simulation state."""
    if not _sim_bridge:
        return
    
    var sim_pens = _sim_bridge.get_all_pens()
    print("[NordlandIntegration] Syncing %d pens from simulation" % sim_pens.size())
    
    # Update mapping
    for i in range(sim_pens.size()):
        var sim_pen = sim_pens[i]
        var scene_pen_id = "pen_%d" % (i + 1)
        var sim_id = sim_pen.get("id", sim_pen.get("zone_id", str(i)))
        _pen_simulation_map[scene_pen_id] = sim_id


func _sync_weather_from_simulation() -> void:
    """Sync weather state from simulation."""
    if not _sim_bridge or not _nordland:
        return
    
    var weather_state = _sim_bridge.get_current_weather_state()
    print("[NordlandIntegration] Syncing weather: %s" % weather_state)
    
    # Convert string to enum
    var weather_enum = _string_to_weather_enum(weather_state)
    _nordland.set_weather(weather_enum)


func _string_to_weather_enum(weather: String) -> int:
    """Convert weather string to WeatherController enum."""
    match weather.to_lower():
        "clear", "calm":
            return WeatherController.WeatherState.CLEAR
        "overcast", "cloudy":
            return WeatherController.WeatherState.OVERCAST
        "rain", "raining":
            return WeatherController.WeatherState.RAIN
        "snow", "snowing":
            return WeatherController.WeatherState.SNOW
        "storm", "stormy":
            return WeatherController.WeatherState.STORM
        "fog", "foggy":
            return WeatherController.WeatherState.FOG
        _:
            return WeatherController.WeatherState.CLEAR


# === Public API ===

func focus_camera_on_pen(pen_id: String) -> void:
    """Move camera to focus on a specific pen."""
    if _nordland:
        _nordland.focus_on_pen(pen_id)


func set_weather(weather: String) -> void:
    """Set weather from string name."""
    if _nordland:
        var weather_enum = _string_to_weather_enum(weather)
        _nordland.set_weather(weather_enum)


func get_recommended_pen_location() -> Vector3:
    """Get the next recommended pen location."""
    if _nordland:
        var locations = _nordland.get_recommended_pen_locations()
        var current_count = _nordland.get_pen_count()
        if current_count < locations.size():
            return locations[current_count]
    return Vector3.ZERO


func notify_simulation_event(event_type: String, data: Dictionary) -> void:
    """Send event notification to simulation."""
    match event_type:
        "pen_clicked":
            var pen_id = data.get("pen_id", "")
            print("[NordlandIntegration] Pen clicked: %s" % pen_id)
        "weather_changed":
            var weather = data.get("weather", "clear")
            set_weather(weather)
        _:
            print("[NordlandIntegration] Unknown event: %s" % event_type)
