extends Node

const CROSSFADE_DURATION: float = 1.5   
const AMBIENT_FADE_DURATION: float = 2.0 
const WEATHER_LAYER_VOLUME: float = 0.6  
const OCEAN_CALM_VOLUME: float = 0.4
const OCEAN_ROUGH_VOLUME: float = 0.8

const BUS_MASTER  := "Master"
const BUS_MUSIC   := "Music"
const BUS_AMBIENT := "Ambient"
const BUS_SFX     := "SFX"
const BUS_UNDERWATER := "Underwater"
const BUS_SEAGULLS := "Seagulls"

var _music_volume:   float = 0.7
var _ambient_volume: float = 0.5
var _sfx_volume:     float = 0.8
var _master_volume:  float = 1.0

var _bgm_active:  AudioStreamPlayer   
var _bgm_pending: AudioStreamPlayer   
var _bgm_fade_t:  float = 0.0         
var _bgm_fading:  bool  = false
var _current_bgm_track: String = ""

# Ambient base (weather-based)
var _amb_base_active:    AudioStreamPlayer   
var _amb_base_pending:   AudioStreamPlayer
var _amb_base_fade_t:    float = 0.0
var _amb_base_fading:    bool = false

# Ocean ambient layer (always playing, volume varies)
var _ocean_calm:  AudioStreamPlayer
var _ocean_rough: AudioStreamPlayer
var _ocean_blend: float = 0.0  # 0.0 = calm, 1.0 = rough
var _target_ocean_blend: float = 0.0

var _amb_weather: AudioStreamPlayer   
var _current_base_track:    String = ""
var _current_weather_track: String = ""

# Underwater low-pass
var _underwater_active: bool = false

# Seagull sounds
var _seagull_players: Array[AudioStreamPlayer3D] = []
var _seagull_active: bool = false

# Positional barge/boat sounds
var _barge_loops: Dictionary = {}  # job_id -> AudioStreamPlayer3D
var _wellboat_loops: Dictionary = {}  # job_id -> AudioStreamPlayer3D
var _feed_barge_loops: Dictionary = {}  # site_id -> AudioStreamPlayer3D

# Wind layer (replaces single storm.ogg with layered wind)
var _wind_light_player: AudioStreamPlayer
var _wind_strong_player: AudioStreamPlayer
var _wind_target_volume: float = 0.0

const SFX_POOL_SIZE: int = 8
var _sfx_pool: Array[AudioStreamPlayer] = []

var _sim_bridge: Node = null
var _fjord_env = null
var _camera = null

## Safe linear->dB: -inf (from linear_to_db(0.0)) is rejected by Godot's
## volume_db setter and spams "Volume can't be set to NaN". Clamp to silence.
const SILENT_DB := -80.0
func _l2db(v: float) -> float:
	return SILENT_DB if v <= 0.0001 else linear_to_db(v)

func _ready() -> void:
	MusicLibrary.validate_catalog()
	_create_players()
	_connect_signals()
	_start_ocean_ambient()
	_init_seagull_emitters()
	_init_underwater_bus()
	_init_wind_layers()

func _process(delta: float) -> void:
	if _bgm_fading:
		_tick_crossfade(delta)
	if _amb_base_fading:
		_tick_ambient_crossfade(delta)
	_tick_ocean_blend(delta)
	_update_underwater_lowpass(delta)
	_update_seagull_emitters(delta)
	_update_wind_layers(delta)

func _create_players() -> void:
	_bgm_active  = _make_player(BUS_MUSIC)
	_bgm_pending = _make_player(BUS_MUSIC)
	_bgm_pending.volume_db = _l2db(0.0)

	_amb_base_active    = _make_player(BUS_AMBIENT)
	_amb_base_pending   = _make_player(BUS_AMBIENT)
	_amb_base_pending.volume_db = _l2db(0.0)
	
	# Ocean ambient layers (always playing)
	_ocean_calm  = _make_player(BUS_AMBIENT)
	_ocean_rough = _make_player(BUS_AMBIENT)
	
	_amb_weather = _make_player(BUS_AMBIENT)
	_amb_weather.volume_db = _l2db(WEATHER_LAYER_VOLUME * _ambient_volume)

	# Wind layer (replaces single storm.ogg with layered wind)
	_wind_light_player = _make_player(BUS_AMBIENT)
	_wind_strong_player = _make_player(BUS_AMBIENT)
	
	for i in SFX_POOL_SIZE:
		var p := _make_player(BUS_SFX)
		p.finished.connect(_on_sfx_finished.bind(p))
		_sfx_pool.append(p)

func _make_player(bus: String) -> AudioStreamPlayer:
	var p := AudioStreamPlayer.new()
	p.bus = bus
	add_child(p)
	return p

func _make_3d_player(bus: String) -> AudioStreamPlayer3D:
	var p := AudioStreamPlayer3D.new()
	p.bus = bus
	p.volume_db = 0.0
	p.max_distance = 2000.0
	p.attenuation_model = AudioStreamPlayer3D.ATTENUATION_INVERSE_DISTANCE
	p.pitch_scale = 1.0
	add_child(p)
	return p

func _connect_signals() -> void:
	if has_node("/root/SimBridge"):
		_sim_bridge = get_node("/root/SimBridge")
		_sim_bridge.state_updated.connect(_on_state_updated)
		_sim_bridge.audio_alert_triggered.connect(_on_alert_triggered)
	else:
		push_warning("AudioController: SimBridge not found")

func _init_underwater_bus() -> void:
	# Ensure the Underwater bus has the low-pass filter enabled
	var idx := AudioServer.get_bus_index(BUS_UNDERWATER)
	if idx >= 0:
		var fx := AudioServer.get_bus_effect(idx, 0)
		if fx and fx is AudioEffectLowPassFilter:
			fx.cutoff_hz = 800.0
			fx.resonance = 1.0
			fx.gain = 0.0
			fx.enabled = true

func _init_seagull_emitters() -> void:
	# Create 8 seagull emitters around the camera
	for i in range(8):
		var emitter := AudioStreamPlayer3D.new()
		emitter.bus = BUS_SEAGULLS
		emitter.volume_db = 0.0
		emitter.max_distance = 1500.0
		emitter.attenuation_model = AudioStreamPlayer3D.ATTENUATION_INVERSE_DISTANCE
		emitter.stream = load("res://assets/audio/ambient/seagulls.ogg")
		emitter.playing = false
		emitter.pitch_scale = 1.0
		add_child(emitter)
		_seagull_players.append(emitter)

func _update_seagull_emitters(delta: float) -> void:
	# Seagulls only at FARM zoom and above water
	if not _camera or not _camera.has_method("current_zoom"):
		return
	
	var zoom: int = _camera.current_zoom
	var is_farm_zoom: bool = zoom == 3  # FARM = 3
	
	# Only emit seagulls at FARM zoom and not underwater
	var should_be_active: bool = is_farm_zoom and not _underwater_active
	
	if should_be_active != _seagull_active:
		_seagull_active = should_be_active
		if should_be_active:
			_start_seagull_emitters()
		else:
			_stop_seagull_emitters()
	
	if _seagull_active and _camera:
		# Distribute emitters in a circle around camera
		var cam_pos: Vector3 = _camera.global_position
		for i in range(_seagull_players.size()):
			var emitter := _seagull_players[i]
			var angle: float = float(i) / _seagull_players.size() * TAU
			var radius: float = 800.0 + randf() * 400.0
			emitter.global_position = cam_pos + Vector3(cos(angle) * radius, 50.0 + randf() * 100.0, sin(angle) * radius)
			if not emitter.playing:
				emitter.play()

func _start_seagull_emitters() -> void:
	for emitter in _seagull_players:
		emitter.stream = load("res://assets/audio/ambient/seagulls.ogg")
		emitter.playing = true
		emitter.volume_db = linear_to_db(0.3)

func _stop_seagull_emitters() -> void:
	for emitter in _seagull_players:
		emitter.stop()
		emitter.playing = false

func _start_ocean_ambient() -> void:
	# Load and start ocean ambient layers from the real asset paths.
	var calm_path := "res://assets/audio/ambient/ocean_waves.ogg"
	var rough_path := "res://assets/audio/ambient/storm.ogg"

	if ResourceLoader.exists(calm_path):
		var stream := load(calm_path) as AudioStream
		if stream:
			_ocean_calm.stream = stream
			_ocean_calm.volume_db = _l2db(OCEAN_CALM_VOLUME * _ambient_volume)
			_ocean_calm.play()

	if ResourceLoader.exists(rough_path):
		var stream := load(rough_path) as AudioStream
		if stream:
			_ocean_rough.stream = stream
			# Volume is driven every frame by _tick_ocean_blend; no silent init.
			_ocean_rough.play()

func _tick_ocean_blend(delta: float) -> void:
	# Smooth blend between calm and rough ocean
	var blend_speed := 0.5  # Takes ~2 seconds to fully transition
	_ocean_blend = move_toward(_ocean_blend, _target_ocean_blend, delta * blend_speed)
	
	var calm_vol := OCEAN_CALM_VOLUME * (1.0 - _ocean_blend) * _ambient_volume
	var rough_vol := OCEAN_ROUGH_VOLUME * _ocean_blend * _ambient_volume
	
	_ocean_calm.volume_db = _l2db(calm_vol) if calm_vol > 0.001 else -80.0
	_ocean_rough.volume_db = _l2db(rough_vol) if rough_vol > 0.001 else -80.0

func _init_wind_layers() -> void:
	# Initialize wind layer players (replaces single storm.ogg with layered wind)
	_wind_light_player.stream = load("res://assets/audio/ambient/wind_light.ogg")
	_wind_light_player.playing = true
	_wind_light_player.volume_db = linear_to_db(0.0)
	
	_wind_strong_player.stream = load("res://assets/audio/ambient/wind_strong.ogg")
	_wind_strong_player.playing = true
	_wind_strong_player.volume_db = linear_to_db(0.0)

func _update_underwater_lowpass(delta: float) -> void:
	if not _camera:
		return
	
	var level: float = 0.0
	if _fjord_env and _camera:
		var level_water: float = _fjord_env.terrain.water_level if _fjord_env.has_method("terrain") else 0.0
		if _camera.global_position.y < level_water:
			level = 1.0
	
	var idx: int = AudioServer.get_bus_index(BUS_UNDERWATER)
	if idx >= 0:
		var fx := AudioServer.get_bus_effect(idx, 0)
		if fx and fx is AudioEffectLowPassFilter:
			if level > 0.5 and not _underwater_active:
				fx.enabled = true
				_underwater_active = true
			elif level < 0.1 and _underwater_active:
				fx.enabled = false
				_underwater_active = false

func _update_wind_layers(delta: float) -> void:
	# Crossfade between light and strong wind based on target volume
	_wind_light_player.volume_db = move_toward(_wind_light_player.volume_db, _wind_target_volume, delta * 6.0)
	_wind_strong_player.volume_db = move_toward(_wind_strong_player.volume_db, _wind_target_volume, delta * 6.0)

func _on_sfx_finished(player: AudioStreamPlayer) -> void:
	player.stream = null

## Positional barge/boat sound methods
func start_wellboat_sound(job_id: String, position: Vector3, kind: String) -> void:
	if _wellboat_loops.has(job_id):
		return
	var player := AudioStreamPlayer3D.new()
	player.bus = BUS_SFX
	player.volume_db = 0.0
	player.max_distance = 1000.0
	player.attenuation_model = AudioStreamPlayer3D.ATTENUATION_INVERSE_DISTANCE
	var stream := load("res://assets/audio/ambient/boat_engine.ogg")
	if not stream:
		stream = load("res://assets/audio/ambient/ocean_waves.ogg")
	player.stream = stream
	player.global_position = position
	player.playing = true
	player.volume_db = linear_to_db(0.4)
	_wellboat_loops[job_id] = player
	add_child(player)

func stop_wellboat_sound(job_id: String) -> void:
	if _wellboat_loops.has(job_id):
		_wellboat_loops[job_id].stop()
		_wellboat_loops.erase(job_id)

func start_feed_barge_sound(site_id: String, position: Vector3) -> void:
	if _feed_barge_loops.has(site_id):
		return
	var player := AudioStreamPlayer3D.new()
	player.bus = BUS_SFX
	player.volume_db = 0.0
	player.max_distance = 800.0
	player.attenuation_model = AudioStreamPlayer3D.ATTENUATION_INVERSE_DISTANCE
	var stream := load("res://assets/audio/ambient/feed_barge.ogg")
	if not stream:
		stream = load("res://assets/audio/ambient/boat_engine.ogg")
	if not stream:
		stream = load("res://assets/audio/ambient/ocean_waves.ogg")
	player.stream = stream
	player.global_position = position
	player.playing = true
	player.volume_db = linear_to_db(0.5)
	_feed_barge_loops[site_id] = player
	add_child(player)

func stop_feed_barge_sound(site_id: String) -> void:
	if _feed_barge_loops.has(site_id):
		_feed_barge_loops[site_id].stop()
		_feed_barge_loops.erase(site_id)

func update_wellboat_position(job_id: String, position: Vector3) -> void:
	if _wellboat_loops.has(job_id):
		_wellboat_loops[job_id].global_position = position

func update_feed_barge_position(site_id: String, position: Vector3) -> void:
	if _feed_barge_loops.has(site_id):
		_feed_barge_loops[site_id].global_position = position

func _on_state_updated() -> void:
	if not _sim_bridge:
		return

	# Seasonal BGM — authoritative key from the simulation bridge.
	var seasonal_key := str(_sim_bridge.audio_seasonal_track)
	if not seasonal_key.is_empty():
		var music_path := MusicLibrary.music(seasonal_key)
		if music_path.is_empty() or not ResourceLoader.exists(music_path):
			music_path = MusicLibrary.music("main_theme")
		if not music_path.is_empty():
			_update_bgm_direct(music_path)

	# Location / weather-derived base ambience.
	_update_ambient_base_direct(MusicLibrary.ambient_base(_sim_bridge.audio_base_track))

	# Additive weather overlay ("" fades the layer out).
	_update_ambient_weather(_sim_bridge.audio_weather_layer)

	# Ocean blend based on weather state.
	var weather := str(_sim_bridge.audio_weather_state).to_lower()
	if weather == "storm" or weather == "severe":
		_target_ocean_blend = 1.0  # Full rough
	elif weather == "rain" or weather == "wind" or weather == "moderate":
		_target_ocean_blend = 0.5  # Half blend
	else:
		_target_ocean_blend = 0.0  # Calm

	# Temperature-based pitch variation on the ambient base layer.
	var temp := 10.0
	if "temperature_c" in _sim_bridge:
		temp = float(_sim_bridge.temperature_c)
	var pitch := 1.0 + (temp - 10.0) * 0.005
	_amb_base_active.pitch_scale = clampf(pitch, 0.8, 1.2)
	_amb_base_pending.pitch_scale = clampf(pitch, 0.8, 1.2)
	
	# Wind layer: drive wind layer volume based on wind speed
	var wind_ms := 5.0
	if "wind_ms" in _sim_bridge:
		wind_ms = float(_sim_bridge.wind_ms)
	# Map wind speed (0-30 m/s) to volume (-80 to -6 dB)
	var target_vol := linear_to_db(clampf(wind_ms / 25.0, 0.0, 1.0) * 0.7)
	_wind_target_volume = target_vol

func _on_alert_triggered(alert_key: String) -> void:
	_play_alert_sfx(alert_key)

func _update_bgm_direct(path: String) -> void:
	if path == _current_bgm_track:
		return
	
	if not ResourceLoader.exists(path):
		push_warning("AudioController: Missing BGM file: " + path)
		return
	
	var stream := load(path) as AudioStream
	if stream == null:
		push_warning("AudioController: Failed to load BGM: " + path)
		return

	_current_bgm_track = path

	if not _bgm_active.playing:
		_bgm_active.stream = stream
		_bgm_active.volume_db = _l2db(_music_volume)
		_bgm_active.play()
		return

	_bgm_pending.stream = stream
	_bgm_pending.volume_db = _l2db(0.0)
	_bgm_pending.play()
	_bgm_fade_t = 0.0
	_bgm_fading = true

func _tick_crossfade(delta: float) -> void:
	_bgm_fade_t += delta
	var t: float = clampf(_bgm_fade_t / CROSSFADE_DURATION, 0.0, 1.0)
	_bgm_active.volume_db  = _l2db(_music_volume * (1.0 - t))
	_bgm_pending.volume_db = _l2db(_music_volume * t)
	if t >= 1.0:
		_bgm_fading = false
		_bgm_active.stop()
		var tmp := _bgm_active
		_bgm_active  = _bgm_pending
		_bgm_pending = tmp

func _update_ambient_base_direct(path: String) -> void:
	if path == _current_base_track:
		return
	
	if not ResourceLoader.exists(path):
		push_warning("AudioController: Missing ambient file: " + path)
		return
	
	var stream := load(path) as AudioStream
	if stream == null:
		push_warning("AudioController: Failed to load ambient: " + path)
		return

	_current_base_track = path

	if not _amb_base_active.playing:
		_amb_base_active.stream = stream
		_amb_base_active.volume_db = _l2db(_ambient_volume)
		_amb_base_active.play()
		return

	_amb_base_pending.stream = stream
	_amb_base_pending.volume_db = _l2db(0.0)
	_amb_base_pending.play()
	_amb_base_fade_t = 0.0
	_amb_base_fading = true

func _tick_ambient_crossfade(delta: float) -> void:
	_amb_base_fade_t += delta
	var t: float = clampf(_amb_base_fade_t / AMBIENT_FADE_DURATION, 0.0, 1.0)
	_amb_base_active.volume_db  = _l2db(_ambient_volume * (1.0 - t))
	_amb_base_pending.volume_db = _l2db(_ambient_volume * t)
	if t >= 1.0:
		_amb_base_fading = false
		_amb_base_active.stop()
		var tmp := _amb_base_active
		_amb_base_active  = _amb_base_pending
		_amb_base_pending = tmp

func _update_ambient_weather(weather_key: String) -> void:
	if weather_key == _current_weather_track:
		return
	_current_weather_track = weather_key

	if weather_key.is_empty():
		var tween := create_tween()
		tween.tween_property(_amb_weather, "volume_db",
			_l2db(0.0), AMBIENT_FADE_DURATION)
		tween.tween_callback(_amb_weather.stop)
		return

	var path := MusicLibrary.ambient_weather(weather_key)
	if path.is_empty() or not ResourceLoader.exists(path):
		return
	
	var stream := load(path) as AudioStream
	if stream == null:
		push_warning("AudioController: Missing weather overlay: " + path)
		return
	
	_amb_weather.stream = stream
	_amb_weather.volume_db = _l2db(0.0)
	_amb_weather.play()
	var tween := create_tween()
	tween.tween_property(_amb_weather, "volume_db",
		_l2db(WEATHER_LAYER_VOLUME * _ambient_volume), AMBIENT_FADE_DURATION)

func _play_alert_sfx(alert_key: String) -> void:
	var path := MusicLibrary.alert_sfx(alert_key)
	if path.is_empty():
		return
	play_sfx(path)

## Play a one-shot sound effect from the SFX pool
func play_sfx(path: String, volume_db: float = 0.0, pitch: float = 1.0) -> AudioStreamPlayer:
	var player := _get_free_sfx_player()
	if player == null:
		push_warning("AudioController: SFX pool exhausted (all 8 players busy)")
		return null
	
	if not ResourceLoader.exists(path):
		push_warning("AudioController: Missing SFX file: " + path)
		return null
	
	var stream := load(path) as AudioStream
	if stream == null:
		push_warning("AudioController: Failed to load SFX: " + path)
		return null
	
	player.stream = stream
	player.volume_db = volume_db
	player.pitch_scale = pitch
	player.play()
	return player

## Play a UI sound by key (button_click, button_hover, notification, etc.)
func play_ui(sound_key: String) -> void:
	var path := MusicLibrary.ui_sfx(sound_key)
	if path.is_empty():
		return
	
	if not ResourceLoader.exists(path):
		# Silently skip missing UI sounds (optional audio)
		return
	
	play_sfx(path)

## UI convenience methods
func play_button_click() -> void:
	play_ui("button_click")

func play_button_hover() -> void:
	play_ui("button_hover")

func play_notification() -> void:
	play_ui("notification")

func play_achievement() -> void:
	var path := MusicLibrary.alert_sfx("achievement")
	if not path.is_empty() and ResourceLoader.exists(path):
		play_sfx(path)

func play_panel_open() -> void:
	play_ui("menu_open")

func play_panel_close() -> void:
	play_ui("menu_close")

## Semantic event stingers. Every mapping uses an existing catalogue key and is
## fully guarded, so a missing asset degrades to silence rather than an error.
func play_insurance_payout() -> void:
	play_ui("notification")

func play_ge_deployment() -> void:
	play_ui("menu_open")

func play_mitigation_install() -> void:
	play_ui("button_click")

func play_harvest_stinger() -> void:
	_play_alert_sfx("harvest")

func play_red_zone_alert(is_red: bool = true) -> void:
	if is_red:
		play_ui("error")
	else:
		play_ui("notification")

## Recursively connect every Button under `root` to click/hover micro-sounds.
## Intentionally NOT wired globally — call this explicitly for a UI subtree.
func attach_ui_sounds(root: Node) -> void:
	if root == null:
		return
	if root is Button:
		var btn := root as Button
		if not btn.pressed.is_connected(play_button_click):
			btn.pressed.connect(play_button_click)
		if not btn.mouse_entered.is_connected(play_button_hover):
			btn.mouse_entered.connect(play_button_hover)
	for child in root.get_children():
		attach_ui_sounds(child)

## Volume control methods
func set_master_volume(vol: float) -> void:
	_master_volume = clampf(vol, 0.0, 1.0)
	var idx: int = AudioServer.get_bus_index(BUS_MASTER)
	if idx >= 0:
		AudioServer.set_bus_volume_db(idx, _l2db(_master_volume))

func set_music_volume(vol: float) -> void:
	_music_volume = clampf(vol, 0.0, 1.0)
	if not _bgm_fading:
		_bgm_active.volume_db = _l2db(_music_volume)

func set_ambient_volume(vol: float) -> void:
	_ambient_volume = clampf(vol, 0.0, 1.0)
	if not _amb_base_fading:
		_amb_base_active.volume_db = _l2db(_ambient_volume)
	_amb_weather.volume_db = _l2db(WEATHER_LAYER_VOLUME * _ambient_volume)
	
	# Update ocean layers
	_tick_ocean_blend(0.0)

func set_sfx_volume(vol: float) -> void:
	_sfx_volume = clampf(vol, 0.0, 1.0)
	var idx: int = AudioServer.get_bus_index(BUS_SFX)
	if idx >= 0:
		AudioServer.set_bus_volume_db(idx, _l2db(_sfx_volume))

func set_music_muted(muted: bool) -> void:
	var idx: int = AudioServer.get_bus_index(BUS_MUSIC)
	if idx >= 0:
		AudioServer.set_bus_mute(idx, muted)

func _get_free_sfx_player() -> AudioStreamPlayer:
	for p in _sfx_pool:
		if not p.playing:
			return p
	return null
