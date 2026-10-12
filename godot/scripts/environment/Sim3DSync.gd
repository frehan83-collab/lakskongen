extends Node
class_name Sim3DSync

## Sim3DSync — Reads simulation state at ~1 Hz and updates 3D visuals.
## Decouples simulation tick rate from visual update rate.

@export var update_interval: float = 1.0  # Seconds between syncs
@export var sim_bridge_path: NodePath = "/root/SimBridge"
@export var fjord_env_path: NodePath = ".."

@export var pen_manager_path: NodePath = "FishPenManager"
@export var weather_system_path: NodePath = "WeatherSystem"
@export var time_of_day_path: NodePath = "TimeOfDay"
@export var particle_effects_path: NodePath = "ParticleEffects"
@export var fjord_camera_path: NodePath = "FjordCamera"
@export var wellboat_manager_path: NodePath = "WellboatVisualManager"
@export var feed_barge_manager_path: NodePath = "FeedBargeVisualManager"

var _sim_bridge: Node = null
var _fjord_env = null
var _pen_manager = null
var _weather_system = null
var _time_of_day = null
var _particle_effects = null
var _fjord_camera = null
var _wellboat_manager = null
var _feed_barge_manager = null

var _accumulator: float = 0.0
var _last_sync_day: int = -1

func _ready() -> void:
	_fjord_env = get_node_or_null(fjord_env_path)
	_pen_manager = get_node_or_null(pen_manager_path)
	_weather_system = get_node_or_null(weather_system_path)
	_time_of_day = get_node_or_null(time_of_day_path)
	_particle_effects = get_node_or_null(particle_effects_path)
	_fjord_camera = get_node_or_null(fjord_camera_path)
	_wellboat_manager = get_node_or_null(wellboat_manager_path)
	_feed_barge_manager = get_node_or_null(feed_barge_manager_path)
	
	var sim = get_node_or_null(sim_bridge_path)
	if sim:
		_sim_bridge = sim
		print("[Sim3DSync] Bound to SimBridge at ", sim_bridge_path)
	else:
		push_warning("[Sim3DSync] SimBridge not found at ", sim_bridge_path)

func _process(delta: float) -> void:
	if not _sim_bridge:
		return
	
	_accumulator += delta
	if _accumulator < update_interval:
		return
	_accumulator = 0.0
	
	_sync_all()

func _sync_all() -> void:
	var day: int = 0
	var day_val: Variant = _sim_bridge.get("day")
	if day_val:
		day = int(day_val)
	
	var season: float = 0.5
	var season_val: Variant = _sim_bridge.get("season01")
	if season_val:
		season = float(season_val)
	else:
		season = 0.5
	
	var weather_state: String = "Calm"
	var weather_val: Variant = _sim_bridge.get("weather_state")
	if weather_val:
		weather_state = str(weather_val)
	else:
		weather_state = "Calm"
	
	var temperature_c: float = 8.0
	var temp_val: Variant = _sim_bridge.get("temperature_c")
	if temp_val:
		temperature_c = float(temp_val)
	else:
		temperature_c = 8.0
	
	var wind_ms: float = 5.0
	var wind_val: Variant = _sim_bridge.get("wind_ms")
	if wind_val:
		wind_ms = float(wind_val)
	else:
		wind_ms = 5.0
	
	var wave_m: float = 0.0
	var wave_val: Variant = _sim_bridge.get("wave_m")
	if wave_val:
		wave_m = float(wave_val)
	else:
		wave_m = 0.0
	
	# Only do heavy sync once per day
	if day != _last_sync_day:
		_last_sync_day = day
		_sync_seasonal(season)
		_sync_pens_from_sim()
	
	# Every tick (1 Hz)
	_sync_weather(weather_state, temperature_c, wind_ms, wave_m)
	_sync_bloom()
	_sync_feeding_particles()
	_sync_wellboats()
	_sync_feed_barges()

func _sync_seasonal(season: float) -> void:
	# Season → snow line & grass color via terrain shader
	if _fjord_env and _fjord_env.has_method("set_season"):
		_fjord_env.set_season(season)
	
	if _time_of_day:
		_time_of_day.season = season

func _sync_pens_from_sim() -> void:
	if not _pen_manager or not _sim_bridge.has_method("get_all_pens"):
		return
	var pens: Array = _sim_bridge.get_all_pens()
	if _pen_manager.has_method("sync_pens_from_sim"):
		_pen_manager.sync_pens_from_sim(pens)

func _sync_weather(weather_state: String, temperature_c: float, wind_ms: float, wave_m: float) -> void:
	# Weather state → waves, foam and fog
	if _weather_system and _weather_system.has_method("set_weather"):
		var wstate := 0
		match weather_state.to_lower():
			"calm": wstate = 0
			"moderate": wstate = 1
			"storm": wstate = 4
			"severe": wstate = 4
			_: wstate = 1
		_weather_system.set_weather(wstate)
	
	# Water shader params
	var env: Node = _fjord_env
	if env and "water_plane" in env:
		var water: MeshInstance3D = env.get("water_plane") as MeshInstance3D
		if water and water.material_override is ShaderMaterial:
			var wmat: ShaderMaterial = water.material_override as ShaderMaterial
			wmat.set_shader_parameter("wave_amplitude", clampf(wave_m * 0.5, 0.05, 2.0))
			wmat.set_shader_parameter("wind_strength", clampf(wind_ms / 8.0, 0.0, 3.0))
			wmat.set_shader_parameter("temperature_factor", clampf((temperature_c - 2.0) / 18.0, 0.0, 1.0))

func _sync_bloom() -> void:
	# Bloom tint around affected sites
	if _sim_bridge.has_method("GetMaxBloomSeverity"):
		var max_severity: float = float(_sim_bridge.GetMaxBloomSeverity())
		var env2: Node = _fjord_env
		if env2 and "water_plane" in env2:
			var water: MeshInstance3D = env2.get("water_plane") as MeshInstance3D
			if water and water.material_override is ShaderMaterial:
				var wmat: ShaderMaterial = water.material_override as ShaderMaterial
				wmat.set_shader_parameter("algae_density", clampf(max_severity, 0.0, 1.0))

func _sync_feeding_particles() -> void:
	# Feeding particles at daylight feeding times
	if not _particle_effects or not _pen_manager or not _sim_bridge.has_method("get_all_pens"):
		return
	
	var hour: float = 12.0
	if _time_of_day and _time_of_day.has_method("get_hour"):
		hour = _time_of_day.get_hour()
	
	# Daylight feeding: 06:00-20:00
	if hour < 6.0 or hour > 20.0:
		return
	
	var pens: Array = _sim_bridge.get_all_pens()
	for site_dict in pens:
		var site_id: String = str(site_dict.get("id", ""))
		var is_feeding: bool = bool(site_dict.get("is_feeding", false))
		if is_feeding and _particle_effects.has_method("_emit_feeding"):
			_particle_effects.call("_emit_feeding", {"pen_id": site_id})

func _sync_wellboats() -> void:
	# Wellboats travelling along Path3D according to eta_days
	if not _wellboat_manager or not _sim_bridge.has_method("get_all_wellboat_jobs"):
		return
	var jobs: Array = _sim_bridge.get_all_wellboat_jobs()
	# WellboatVisualManager handles spawning/animation via its own sync
	# Just ensure it's bound to the sim
	if _wellboat_manager.has_method("bind_simulation") and not _wellboat_manager.get("_sim_bridge"):
		_wellboat_manager.bind_simulation(_sim_bridge)

func _sync_feed_barges() -> void:
	# Feed barge mooring layout per site
	if not _feed_barge_manager or not _sim_bridge.has_method("get_all_pens"):
		return
	var sites: Array = _sim_bridge.get_all_pens()
	# FeedBargeVisualManager handles spawning/animation via its own sync
	if _feed_barge_manager.has_method("bind_simulation") and not _feed_barge_manager.get("_sim_bridge"):
		_feed_barge_manager.bind_simulation(_sim_bridge)