extends Node

const CROSSFADE_DURATION: float = 1.5   
const AMBIENT_FADE_DURATION: float = 2.0 
const WEATHER_LAYER_VOLUME: float = 0.6  

const BUS_MASTER  := "Master"
const BUS_MUSIC   := "Music"
const BUS_AMBIENT := "Ambient"
const BUS_SFX     := "SFX"

var _music_volume:   float = 0.7
var _ambient_volume: float = 0.5
var _sfx_volume:     float = 0.8

var _bgm_active:  AudioStreamPlayer   
var _bgm_pending: AudioStreamPlayer   
var _bgm_fade_t:  float = 0.0         
var _bgm_fading:  bool  = false
var _current_bgm_track: String = ""

var _amb_base_active:    AudioStreamPlayer   
var _amb_base_pending:   AudioStreamPlayer
var _amb_base_fade_t:    float = 0.0
var _amb_base_fading:    bool = false

var _amb_weather: AudioStreamPlayer   
var _current_base_track:    String = ""
var _current_weather_track: String = ""

const SFX_POOL_SIZE: int = 8
var _sfx_pool: Array[AudioStreamPlayer] = []

var _sim_bridge: Node = null

func _ready() -> void:
	_create_players()
	_connect_signals()

func _process(delta: float) -> void:
	if _bgm_fading:
		_tick_crossfade(delta)
	if _amb_base_fading:
		_tick_ambient_crossfade(delta)

func _create_players() -> void:
	_bgm_active  = _make_player(BUS_MUSIC)
	_bgm_pending = _make_player(BUS_MUSIC)
	_bgm_pending.volume_db = linear_to_db(0.0)

	_amb_base_active    = _make_player(BUS_AMBIENT)
	_amb_base_pending   = _make_player(BUS_AMBIENT)
	_amb_base_pending.volume_db = linear_to_db(0.0)
	
	_amb_weather = _make_player(BUS_AMBIENT)
	_amb_weather.volume_db = linear_to_db(WEATHER_LAYER_VOLUME * _ambient_volume)

	for i in SFX_POOL_SIZE:
		var p := _make_player(BUS_SFX)
		p.finished.connect(_on_sfx_finished.bind(p))
		_sfx_pool.append(p)

func _make_player(bus: String) -> AudioStreamPlayer:
	var p := AudioStreamPlayer.new()
	p.bus = bus
	add_child(p)
	return p

func _connect_signals() -> void:
	if has_node("/root/SimBridge"):
		_sim_bridge = get_node("/root/SimBridge")
		_sim_bridge.state_updated.connect(_on_state_updated)
		_sim_bridge.audio_alert_triggered.connect(_on_alert_triggered)
	else:
		push_warning("AudioController: SimBridge not found")

func _on_state_updated() -> void:
	if not _sim_bridge:
		return
		
	# T32: Music crossfades based on game tension
	var day = _sim_bridge.get_current_day() if _sim_bridge.has_method("get_current_day") else _sim_bridge.current_day
	var is_tense = day > 30 or _sim_bridge.active_alerts.size() > 0 or _sim_bridge.sea_lice_per_fish > 2.0
	var bgm_path = "res://assets/audio/theme_music_tense.ogg" if is_tense else "res://assets/audio/theme_music_calm.ogg"
	_update_bgm_direct(bgm_path)

	# T32: Dynamic ambient soundscapes based on weather state
	var weather = _sim_bridge.get_current_weather_state().to_lower() if _sim_bridge.has_method("get_current_weather_state") else _sim_bridge.audio_weather_state.to_lower()
	var amb_path = "res://assets/audio/calm_sea.ogg"
	if weather == "storm" or weather == "severe":
		amb_path = "res://assets/audio/storm_wind.ogg"
	elif weather == "rain" or weather == "wind" or weather == "moderate":
		amb_path = "res://assets/audio/rain.ogg"
	
	_update_ambient_base_direct(amb_path)
	_update_ambient_weather(_sim_bridge.audio_weather_layer)
	
	# T32: Subtle temperature-based sound variations
	var temp = _sim_bridge.get_current_temperature() if _sim_bridge.has_method("get_current_temperature") else _sim_bridge.temperature_c
	# Base pitch 1.0 at 10 degrees, slight variation
	var pitch = 1.0 + (temp - 10.0) * 0.005
	_amb_base_active.pitch_scale = clampf(pitch, 0.8, 1.2)
	_amb_base_pending.pitch_scale = clampf(pitch, 0.8, 1.2)

func _on_alert_triggered(alert_key: String) -> void:
	_play_alert_sfx(alert_key)

func _update_bgm_direct(path: String) -> void:
	if path == _current_bgm_track:
		return
	var stream := load(path) as AudioStream
	if stream == null:
		return

	_current_bgm_track = path

	if not _bgm_active.playing:
		_bgm_active.stream = stream
		_bgm_active.volume_db = linear_to_db(_music_volume)
		_bgm_active.play()
		return

	_bgm_pending.stream = stream
	_bgm_pending.volume_db = linear_to_db(0.0)
	_bgm_pending.play()
	_bgm_fade_t = 0.0
	_bgm_fading = true

func _tick_crossfade(delta: float) -> void:
	_bgm_fade_t += delta
	var t: float = clampf(_bgm_fade_t / CROSSFADE_DURATION, 0.0, 1.0)
	_bgm_active.volume_db  = linear_to_db(_music_volume * (1.0 - t))
	_bgm_pending.volume_db = linear_to_db(_music_volume * t)
	if t >= 1.0:
		_bgm_fading = false
		_bgm_active.stop()
		var tmp := _bgm_active
		_bgm_active  = _bgm_pending
		_bgm_pending = tmp

func _update_ambient_base_direct(path: String) -> void:
	if path == _current_base_track:
		return
	var stream := load(path) as AudioStream
	if stream == null:
		return

	_current_base_track = path

	if not _amb_base_active.playing:
		_amb_base_active.stream = stream
		_amb_base_active.volume_db = linear_to_db(_ambient_volume)
		_amb_base_active.play()
		return

	_amb_base_pending.stream = stream
	_amb_base_pending.volume_db = linear_to_db(0.0)
	_amb_base_pending.play()
	_amb_base_fade_t = 0.0
	_amb_base_fading = true

func _tick_ambient_crossfade(delta: float) -> void:
	_amb_base_fade_t += delta
	var t: float = clampf(_amb_base_fade_t / AMBIENT_FADE_DURATION, 0.0, 1.0)
	_amb_base_active.volume_db  = linear_to_db(_ambient_volume * (1.0 - t))
	_amb_base_pending.volume_db = linear_to_db(_ambient_volume * t)
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
			linear_to_db(0.0), AMBIENT_FADE_DURATION)
		tween.tween_callback(_amb_weather.stop)
		return

	if not has_node("/root/MusicLibrary"): return
	var path := MusicLibrary.ambient_weather(weather_key) if MusicLibrary.has_method("ambient_weather") else ""
	if path.is_empty():
		return
	var stream := load(path) as AudioStream
	if stream == null:
		return
	_amb_weather.stream = stream
	_amb_weather.volume_db = linear_to_db(0.0)
	_amb_weather.play()
	var tween := create_tween()
	tween.tween_property(_amb_weather, "volume_db",
		linear_to_db(WEATHER_LAYER_VOLUME * _ambient_volume), AMBIENT_FADE_DURATION)

func _play_alert_sfx(alert_key: String) -> void:
	if not has_node("/root/MusicLibrary"): return
	var path := MusicLibrary.alert_sfx(alert_key) if MusicLibrary.has_method("alert_sfx") else ""
	if path.is_empty(): return
	play_sfx(path)

func play_sfx(path: String, volume_db: float = 0.0, pitch: float = 1.0) -> AudioStreamPlayer:
	var player := _get_free_sfx_player()
	if player == null: return null
	var stream := load(path) as AudioStream
	if stream == null: return null
	player.stream = stream
	player.volume_db = volume_db
	player.pitch_scale = pitch
	player.play()
	return player

func play_ui(sound_key: String) -> void:
	if not has_node("/root/MusicLibrary"): return
	var path := MusicLibrary.ui_sfx(sound_key) if MusicLibrary.has_method("ui_sfx") else ""
	if not path.is_empty():
		play_sfx(path)

func set_music_volume(v: float) -> void:
	_music_volume = clampf(v, 0.0, 1.0)
	if not _bgm_fading:
		_bgm_active.volume_db = linear_to_db(_music_volume)

func set_ambient_volume(v: float) -> void:
	_ambient_volume = clampf(v, 0.0, 1.0)
	if not _amb_base_fading:
		_amb_base_active.volume_db = linear_to_db(_ambient_volume)
	_amb_weather.volume_db = linear_to_db(WEATHER_LAYER_VOLUME * _ambient_volume)

func set_sfx_volume(v: float) -> void:
	_sfx_volume = clampf(v, 0.0, 1.0)
	var idx: int = AudioServer.get_bus_index(BUS_SFX)
	if idx >= 0:
		AudioServer.set_bus_volume_db(idx, linear_to_db(_sfx_volume))

func set_master_volume(v: float) -> void:
	var idx: int = AudioServer.get_bus_index(BUS_MASTER)
	if idx >= 0:
		AudioServer.set_bus_volume_db(idx, linear_to_db(clampf(v, 0.0, 1.0)))

func set_music_muted(muted: bool) -> void:
	var idx: int = AudioServer.get_bus_index(BUS_MUSIC)
	if idx >= 0:
		AudioServer.set_bus_mute(idx, muted)

func _get_free_sfx_player() -> AudioStreamPlayer:
	for p in _sfx_pool:
		if not p.playing:
			return p
	return null

func _on_sfx_finished(player: AudioStreamPlayer) -> void:
	player.stream = null
