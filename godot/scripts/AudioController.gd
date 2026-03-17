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
var _current_season: String = ""

const SFX_POOL_SIZE: int = 8
var _sfx_pool: Array[AudioStreamPlayer] = []

var _sim_bridge: Node = null

func _ready() -> void:
	_create_players()
	_connect_signals()
	_start_ocean_ambient()

func _process(delta: float) -> void:
	if _bgm_fading:
		_tick_crossfade(delta)
	if _amb_base_fading:
		_tick_ambient_crossfade(delta)
	_tick_ocean_blend(delta)

func _create_players() -> void:
	_bgm_active  = _make_player(BUS_MUSIC)
	_bgm_pending = _make_player(BUS_MUSIC)
	_bgm_pending.volume_db = linear_to_db(0.0)

	_amb_base_active    = _make_player(BUS_AMBIENT)
	_amb_base_pending   = _make_player(BUS_AMBIENT)
	_amb_base_pending.volume_db = linear_to_db(0.0)
	
	# Ocean ambient layers (always playing)
	_ocean_calm  = _make_player(BUS_AMBIENT)
	_ocean_rough = _make_player(BUS_AMBIENT)
	
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

func _start_ocean_ambient() -> void:
	# Load and start ocean ambient layers
	var calm_path := "res://assets/audio/ambient/ocean_calm.ogg"
	var rough_path := "res://assets/audio/ambient/ocean_rough.ogg"
	
	if ResourceLoader.exists(calm_path):
		var stream := load(calm_path) as AudioStream
		if stream:
			_ocean_calm.stream = stream
			_ocean_calm.volume_db = linear_to_db(OCEAN_CALM_VOLUME * _ambient_volume)
			_ocean_calm.play()
	else:
		# Fallback to existing calm_sea.ogg
		calm_path = "res://assets/audio/ambient/calm_sea.ogg"
		if ResourceLoader.exists(calm_path):
			var stream := load(calm_path) as AudioStream
			if stream:
				_ocean_calm.stream = stream
				_ocean_calm.volume_db = linear_to_db(OCEAN_CALM_VOLUME * _ambient_volume)
				_ocean_calm.play()
	
	if ResourceLoader.exists(rough_path):
		var stream := load(rough_path) as AudioStream
		if stream:
			_ocean_rough.stream = stream
			_ocean_rough.volume_db = linear_to_db(0.0)  # Start silent
			_ocean_rough.play()
	else:
		# Fallback to existing storm.ogg
		rough_path = "res://assets/audio/ambient/storm.ogg"
		if ResourceLoader.exists(rough_path):
			var stream := load(rough_path) as AudioStream
			if stream:
				_ocean_rough.stream = stream
				_ocean_rough.volume_db = linear_to_db(0.0)
				_ocean_rough.play()

func _tick_ocean_blend(delta: float) -> void:
	# Smooth blend between calm and rough ocean
	var blend_speed := 0.5  # Takes ~2 seconds to fully transition
	_ocean_blend = move_toward(_ocean_blend, _target_ocean_blend, delta * blend_speed)
	
	var calm_vol := OCEAN_CALM_VOLUME * (1.0 - _ocean_blend) * _ambient_volume
	var rough_vol := OCEAN_ROUGH_VOLUME * _ocean_blend * _ambient_volume
	
	_ocean_calm.volume_db = linear_to_db(calm_vol) if calm_vol > 0.001 else -80.0
	_ocean_rough.volume_db = linear_to_db(rough_vol) if rough_vol > 0.001 else -80.0

func _on_state_updated() -> void:
	if not _sim_bridge:
		return
	
	# Get current season and update music
	var season := ""
	if _sim_bridge.has_method("get_current_season"):
		season = _sim_bridge.get_current_season().to_lower()
	elif "current_season" in _sim_bridge:
		season = _sim_bridge.current_season.to_lower()
	
	if season != _current_season and not season.is_empty():
		_current_season = season
		_update_seasonal_music(season)
	
	# Get weather state for ocean blend and music transitions
	var weather := ""
	if _sim_bridge.has_method("get_current_weather_state"):
		weather = _sim_bridge.get_current_weather_state().to_lower()
	elif "audio_weather_state" in _sim_bridge:
		weather = _sim_bridge.audio_weather_state.to_lower()
	
	# Update ocean ambient blend based on weather
	if weather == "storm" or weather == "severe":
		_target_ocean_blend = 1.0  # Full rough
	elif weather == "rain" or weather == "wind" or weather == "moderate":
		_target_ocean_blend = 0.5  # Half blend
	else:
		_target_ocean_blend = 0.0  # Calm
	
	# Storm music override
	var day := 0
	if _sim_bridge.has_method("get_current_day"):
		day = _sim_bridge.get_current_day()
	elif "current_day" in _sim_bridge:
		day = _sim_bridge.current_day
	
	var active_alerts := []
	if "active_alerts" in _sim_bridge:
		active_alerts = _sim_bridge.active_alerts
	
	var sea_lice := 0.0
	if "sea_lice_per_fish" in _sim_bridge:
		sea_lice = _sim_bridge.sea_lice_per_fish
	
	# Storm overlay music during severe weather
	if weather == "storm" or weather == "severe":
		var storm_track := MusicLibrary.music("storm_music")
		if not storm_track.is_empty() and ResourceLoader.exists(storm_track):
			_update_bgm_direct(storm_track)
	elif day > 30 or active_alerts.size() > 0 or sea_lice > 2.0:
		# Tense situation - keep seasonal music but could add tension layer
		pass
	
	# Update ambient weather overlay
	var weather_layer := ""
	if "audio_weather_layer" in _sim_bridge:
		weather_layer = _sim_bridge.audio_weather_layer
	_update_ambient_weather(weather_layer)
	
	# Temperature-based sound variations
	var temp := 10.0
	if _sim_bridge.has_method("get_current_temperature"):
		temp = _sim_bridge.get_current_temperature()
	elif "temperature_c" in _sim_bridge:
		temp = _sim_bridge.temperature_c
	
	var pitch := 1.0 + (temp - 10.0) * 0.005
	_amb_base_active.pitch_scale = clampf(pitch, 0.8, 1.2)
	_amb_base_pending.pitch_scale = clampf(pitch, 0.8, 1.2)

func _update_seasonal_music(season: String) -> void:
	var track_key := season + "_theme"
	var path := MusicLibrary.music(track_key)
	
	if path.is_empty() or not ResourceLoader.exists(path):
		# Fallback to main theme
		path = MusicLibrary.music("main_theme")
	
	if not path.is_empty() and ResourceLoader.exists(path):
		_update_bgm_direct(path)

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

	var path := MusicLibrary.ambient_weather(weather_key)
	if path.is_empty() or not ResourceLoader.exists(path):
		return
	
	var stream := load(path) as AudioStream
	if stream == null:
		push_warning("AudioController: Missing weather overlay: " + path)
		return
	
	_amb_weather.stream = stream
	_amb_weather.volume_db = linear_to_db(0.0)
	_amb_weather.play()
	var tween := create_tween()
	tween.tween_property(_amb_weather, "volume_db",
		linear_to_db(WEATHER_LAYER_VOLUME * _ambient_volume), AMBIENT_FADE_DURATION)

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

## Volume control methods
func set_master_volume(vol: float) -> void:
	_master_volume = clampf(vol, 0.0, 1.0)
	var idx: int = AudioServer.get_bus_index(BUS_MASTER)
	if idx >= 0:
		AudioServer.set_bus_volume_db(idx, linear_to_db(_master_volume))

func set_music_volume(vol: float) -> void:
	_music_volume = clampf(vol, 0.0, 1.0)
	if not _bgm_fading:
		_bgm_active.volume_db = linear_to_db(_music_volume)

func set_ambient_volume(vol: float) -> void:
	_ambient_volume = clampf(vol, 0.0, 1.0)
	if not _amb_base_fading:
		_amb_base_active.volume_db = linear_to_db(_ambient_volume)
	_amb_weather.volume_db = linear_to_db(WEATHER_LAYER_VOLUME * _ambient_volume)
	
	# Update ocean layers
	_tick_ocean_blend(0.0)

func set_sfx_volume(vol: float) -> void:
	_sfx_volume = clampf(vol, 0.0, 1.0)
	var idx: int = AudioServer.get_bus_index(BUS_SFX)
	if idx >= 0:
		AudioServer.set_bus_volume_db(idx, linear_to_db(_sfx_volume))

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
