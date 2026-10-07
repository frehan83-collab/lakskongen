class_name IVisualAdapter
extends RefCounted

## Interface for simulation-driven 3D visuals.
## Concrete implementations (VisualAdapter3D, headless, etc.) drive 3D state
## purely from simulation tick — no independent animation systems.

# === Pen Visuals ===
func bind_simulation(sim_bridge) -> void: pass
func update_pen_visual(pen_id: String, state: Dictionary) -> void: pass
func add_pen(pen_id: String, position: Vector3, site_data: Dictionary) -> void: pass
func remove_pen(pen_id: String) -> void: pass
func select_pen(pen_id: String) -> void: pass

# === Water Quality ===
func update_water_quality(zone: String, do_mg_l: float, temp_c: float, salinity: float = 34.0) -> void: pass
func set_water_clarity(clarity: float) -> void: pass  # 0.0 = murky, 1.0 = clear

# === Time & Weather ===
func set_time_of_day(hour: float) -> void: pass  # 0-24
func set_weather(weather_type: String, intensity: float) -> void: pass

# === Events ===
func trigger_event_visual(event_type: String, params: Dictionary) -> void: pass

# === Camera ===
func focus_on_pen(pen_id: String, duration: float = 1.2) -> void: pass
func set_camera_mode(mode: String) -> void: pass  # orbit, fly, fixed

# === Performance ===
func set_lod_distance(near: float, far: float) -> void: pass
func get_draw_call_stats() -> Dictionary: return {}