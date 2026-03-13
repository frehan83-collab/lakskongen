extends Node

## AudioController — Godot audio layer for Lakskongen.
##
## Reads ambience state from SimBridge (which mirrors C# AudioManager via GameBridge)
## and drives three independent AudioStreamPlayer layers:
##
##   BGM layer    — seasonal background music, crossfaded between tracks (1–2 sec)
##   Ambient layer — weather-reactive looping ambient (base + weather overlay)
##   SFX layer    — one-shot event sounds (alerts, achievements, harvest)
##
## Volume follows player settings exposed via set_*_volume() methods.
##
## Registration: project.godot → [autoload] AudioController = "res://scripts/AudioController.gd"
## Depends on:   SimBridge, MusicLibrary (both registered as autoloads before this node)

# ── Constants ──────────────────────────────────────────────────────────────────

const CROSSFADE_DURATION: float = 1.5   # seconds for BGM crossfade
const AMBIENT_FADE_DURATION: float = 2.0 # seconds for ambient transitions
const WEATHER_LAYER_VOLUME: float = 0.6  # relative volume for weather overlay

# ── Audio buses ───────────────────────────────────────────────────────────────

const BUS_MASTER  := "Master"
const BUS_MUSIC   := "Music"
const BUS_AMBIENT := "Ambient"
const BUS_SFX     := "SFX"

# ── Volume settings (saved by SettingsMenu) ───────────────────────────────────

var _music_volume:   float = 0.7
var _ambient_volume: float = 0.5
var _sfx_volume:     float = 0.8

# ── BGM crossfade state ───────────────────────────────────────────────────────

var _bgm_active:  AudioStreamPlayer   # currently audible BGM player
var _bgm_pending: AudioStreamPlayer   # fading in
var _bgm_fade_t:  float = 0.0         # 0 → CROSSFADE_DURATION
var _bgm_fading:  bool  = false
var _current_bgm_track: String = ""

# ── Ambient state ─────────────────────────────────────────────────────────────

var _amb_base:    AudioStreamPlayer   # primary ambient loop
var _amb_weather: AudioStreamPlayer   # weather overlay loop
var _current_base_track:    String = ""
var _current_weather_track: String = ""

# ── SFX pool ──────────────────────────────────────────────────────────────────

const SFX_POOL_SIZE: int = 8
var _sfx_pool: Array[AudioStreamPlayer] = []

# ── Internal refs ─────────────────────────────────────────────────────────────

var _sim_bridge: Node = null
var _last_seasonal_track: String = ""
var _last_base_track:     String = ""
var _last_weather_layer:  String = ""

# ── Godot lifecycle ───────────────────────────────────────────────────────────

func _ready() -> void:
	_create_players()
	_connect_signals()
	print("AudioController: ready")

func _process(delta: float) -> void:
	if _bgm_fading:
		_tick_crossfade(delta)

# ── Setup ─────────────────────────────────────────────────────────────────────

func _create_players() -> void:
	# BGM — two players for crossfade
	_bgm_active  = _make_player(BUS_MUSIC)
	_bgm_pending = _make_player(BUS_MUSIC)
	_bgm_pending.volume_db = linear_to_db(0.0)

	# Ambient — base + weather overlay
	_amb_base    = _make_player(BUS_AMBIENT)
	_amb_weather = _make_player(BUS_AMBIENT)
	_amb_weather.volume_db = linear_to_db(WEATHER_LAYER_VOLUME * _ambient_volume)

	# SFX pool
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
		print("AudioController: connected to SimBridge")
	else:
		push_warning("AudioController: SimBridge not found — audio driven manually only")

# ── SimBridge signal handlers ─────────────────────────────────────────────────

func _on_state_updated() -> void:
	if not _sim_bridge:
		return
	_update_bgm(_sim_bridge.audio_seasonal_track)
	_update_ambient(_sim_bridge.audio_base_track, _sim_bridge.audio_weather_layer)
	# Weather mapping: C# WeatherState → audio keys (AudioManager.cs):
	#   Calm → "" (no weather layer), Moderate → "wind", Storm/Severe → "storm"
	# Base track mapping: context + weather (AudioManager.GetAmbienceProfile)

func _on_alert_triggered(alert_key: String) -> void:
	_play_alert_sfx(alert_key)

# ── BGM crossfade ─────────────────────────────────────────────────────────────

## Begin a crossfade to a new BGM track. No-op if track is already playing.
func _update_bgm(track_key: String) -> void:
	if track_key == _current_bgm_track:
		return
	var path := MusicLibrary.music(track_key)
	if path.is_empty():
		return
	var stream := load(path) as AudioStream
	if stream == null:
		push_warning("AudioController: could not load BGM '%s'" % path)
		return

	_current_bgm_track = track_key

	if not _bgm_active.playing:
		# Nothing playing yet — start immediately
		_bgm_active.stream = stream
		_bgm_active.volume_db = linear_to_db(_music_volume)
		_bgm_active.play()
		return

	# Start crossfade: pending fades in while active fades out
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
		# Swap roles: pending becomes active
		var tmp := _bgm_active
		_bgm_active  = _bgm_pending
		_bgm_pending = tmp

# ── Ambient layer update ──────────────────────────────────────────────────────

func _update_ambient(base_key: String, weather_key: String) -> void:
	_update_ambient_base(base_key)
	_update_ambient_weather(weather_key)

func _update_ambient_base(base_key: String) -> void:
	if base_key == _current_base_track:
		return
	var path := MusicLibrary.ambient_base(base_key)
	if path.is_empty():
		return
	var stream := load(path) as AudioStream
	if stream == null:
		push_warning("AudioController: could not load ambient base '%s'" % path)
		return
	_current_base_track = base_key
	_amb_base.stream = stream
	_amb_base.volume_db = linear_to_db(_ambient_volume)
	_amb_base.play()

func _update_ambient_weather(weather_key: String) -> void:
	if weather_key == _current_weather_track:
		return
	_current_weather_track = weather_key

	if weather_key.is_empty():
		# Fade out weather overlay
		var tween := create_tween()
		tween.tween_property(_amb_weather, "volume_db",
			linear_to_db(0.0), AMBIENT_FADE_DURATION)
		tween.tween_callback(_amb_weather.stop)
		return

	var path := MusicLibrary.ambient_weather(weather_key)
	if path.is_empty():
		return
	var stream := load(path) as AudioStream
	if stream == null:
		push_warning("AudioController: could not load weather layer '%s'" % path)
		return
	_amb_weather.stream = stream
	_amb_weather.volume_db = linear_to_db(0.0)
	_amb_weather.play()
	# Fade in weather overlay
	var tween := create_tween()
	tween.tween_property(_amb_weather, "volume_db",
		linear_to_db(WEATHER_LAYER_VOLUME * _ambient_volume), AMBIENT_FADE_DURATION)

# ── Alert SFX ─────────────────────────────────────────────────────────────────

func _play_alert_sfx(alert_key: String) -> void:
	var path := MusicLibrary.alert_sfx(alert_key)
	if path.is_empty():
		push_warning("AudioController: unknown alert key '%s'" % alert_key)
		return
	play_sfx(path)

# ── Public SFX API ────────────────────────────────────────────────────────────

## Play a one-shot SFX from a resource path. Returns the player used (for pitch control).
func play_sfx(path: String, volume_db: float = 0.0, pitch: float = 1.0) -> AudioStreamPlayer:
	var player := _get_free_sfx_player()
	if player == null:
		push_warning("AudioController: SFX pool exhausted, dropping '%s'" % path)
		return null
	var stream := load(path) as AudioStream
	if stream == null:
		push_warning("AudioController: could not load SFX '%s'" % path)
		return null
	player.stream = stream
	player.volume_db = volume_db
	player.pitch_scale = pitch
	player.play()
	return player

## Play a named UI sound.
func play_ui(sound_key: String) -> void:
	var path := MusicLibrary.ui_sfx(sound_key)
	if not path.is_empty():
		play_sfx(path)

# ── Volume control (called by SettingsMenu) ───────────────────────────────────

func set_music_volume(v: float) -> void:
	_music_volume = clampf(v, 0.0, 1.0)
	if not _bgm_fading:
		_bgm_active.volume_db = linear_to_db(_music_volume)

func set_ambient_volume(v: float) -> void:
	_ambient_volume = clampf(v, 0.0, 1.0)
	_amb_base.volume_db    = linear_to_db(_ambient_volume)
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

## Mute / unmute the music bus.
func set_music_muted(muted: bool) -> void:
	var idx: int = AudioServer.get_bus_index(BUS_MUSIC)
	if idx >= 0:
		AudioServer.set_bus_mute(idx, muted)

# ── SFX pool helpers ──────────────────────────────────────────────────────────

func _get_free_sfx_player() -> AudioStreamPlayer:
	for p in _sfx_pool:
		if not p.playing:
			return p
	return null

func _on_sfx_finished(player: AudioStreamPlayer) -> void:
	player.stream = null  # release reference; player returns to pool automatically
