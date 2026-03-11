extends Node

# AudioManager.gd
# The central Audio Manager Singleton for Lakskongen salmon-sim
# Jonas Karlsen, Game Systems Programmer

# Audio buses
var master_bus_idx: int
var bgm_bus_idx: int
var sfx_bus_idx: int
var ambient_bus_idx: int

# Active streams
var bgm_player: AudioStreamPlayer
var bgm_player_alt: AudioStreamPlayer
var ambient_player: AudioStreamPlayer
var ambient_storm_player: AudioStreamPlayer

var is_crossfading: bool = false
var crossfade_duration: float = 2.0
var _current_bgm: AudioStreamPlayer

func _ready():
	# Retrieve bus indices
	master_bus_idx = AudioServer.get_bus_index("Master")
	bgm_bus_idx = AudioServer.get_bus_index("BGM")
	sfx_bus_idx = AudioServer.get_bus_index("SFX")
	ambient_bus_idx = AudioServer.get_bus_index("Ambient")
	
	# Initialize music players for crossfading
	bgm_player = AudioStreamPlayer.new()
	bgm_player.bus = "BGM"
	add_child(bgm_player)
	
	bgm_player_alt = AudioStreamPlayer.new()
	bgm_player_alt.bus = "BGM"
	add_child(bgm_player_alt)
	
	_current_bgm = bgm_player
	
	# Initialize ambient players
	ambient_player = AudioStreamPlayer.new()
	ambient_player.bus = "Ambient"
	add_child(ambient_player)
	
	ambient_storm_player = AudioStreamPlayer.new()
	ambient_storm_player.bus = "Ambient"
	ambient_storm_player.volume_db = -80.0
	add_child(ambient_storm_player)

# --- Volume Controls ---

func set_bus_volume(bus_name: String, volume_db: float):
	var bus_idx = AudioServer.get_bus_index(bus_name)
	if bus_idx != -1:
		AudioServer.set_bus_volume_db(bus_idx, volume_db)

func set_bus_mute(bus_name: String, muted: bool):
	var bus_idx = AudioServer.get_bus_index(bus_name)
	if bus_idx != -1:
		AudioServer.set_bus_mute(bus_idx, muted)

# --- Music & Crossfading ---

func play_bgm(stream: AudioStream, crossfade: bool = true):
	if _current_bgm.stream == stream and _current_bgm.playing:
		return
		
	var next_bgm = bgm_player_alt if _current_bgm == bgm_player else bgm_player
	next_bgm.stream = stream
	next_bgm.play()
	
	if crossfade:
		var tween = create_tween().set_parallel(true)
		tween.tween_property(_current_bgm, "volume_db", -80.0, crossfade_duration)
		next_bgm.volume_db = -80.0
		tween.tween_property(next_bgm, "volume_db", 0.0, crossfade_duration)
		tween.chain().tween_callback(_current_bgm.stop)
	else:
		_current_bgm.stop()
		next_bgm.volume_db = 0.0
		
	_current_bgm = next_bgm

# --- Weather & Ambient Mixing ---

func update_weather_ambient(weather_state: String, intensity: float):
	# intensity 0.0 to 1.0 (calm to storm)
	match weather_state:
		"Clear":
			_fade_ambient(ambient_player, 0.0, 2.0)
			_fade_ambient(ambient_storm_player, -80.0, 2.0)
		"Rain":
			_fade_ambient(ambient_player, -10.0, 2.0)
			_fade_ambient(ambient_storm_player, lerp(-40.0, -10.0, intensity), 2.0)
		"Storm":
			_fade_ambient(ambient_player, -20.0, 3.0)
			_fade_ambient(ambient_storm_player, lerp(-10.0, 0.0, intensity), 3.0)

func _fade_ambient(player: AudioStreamPlayer, target_db: float, duration: float):
	if not player.playing:
		player.play()
	var tween = create_tween()
	tween.tween_property(player, "volume_db", target_db, duration)

# --- SFX ---

func play_sfx(stream: AudioStream, pitch_variance: float = 0.0):
	var sfx_player = AudioStreamPlayer.new()
	sfx_player.stream = stream
	sfx_player.bus = "SFX"
	if pitch_variance > 0.0:
		sfx_player.pitch_scale = randf_range(1.0 - pitch_variance, 1.0 + pitch_variance)
	
	add_child(sfx_player)
	sfx_player.play()
	sfx_player.finished.connect(sfx_player.queue_free)
