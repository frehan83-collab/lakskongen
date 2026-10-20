extends Node

## The game's sound (autoload). Music, the ambience bed, the event and verb
## cues, the UI's clicks and the vessels' engines all play from here.
##
## Au2-01, the audio floor:
## - the ambience bed is AmbienceMixer: 60 s loops (wind, water lap, gulls,
##   rain, storm sea, underwater) at the gains the world on screen asks for;
## - every Button press clicks, through one global hook (the tree's
##   node_added): no screen wires its own buttons, on the UI bus;
## - the verbs sound: stocking, a feeding plan, a delousing, an auction's
##   verdict and a storm play their cue from the event bus and the player's
##   actions (VerbSounds; the harvest's stinger is the ConsequenceDirector's);
## - the feed barges and wellboats carry their engine as a 3D emitter
##   (attach_loop), so the sound sits on the boat and moves with it.
## Context ambience (the harbour, the processing plant) still crossfades on
## the base player when the sim reports that context.

const CROSSFADE_DURATION: float = 1.5
const AMBIENT_FADE_DURATION: float = 2.0
## Context ambience level (the harbour), linear before the Ambient bus.
const CONTEXT_LAYER_VOLUME: float = 0.7

const BUS_MASTER  := "Master"
const BUS_MUSIC   := "Music"
const BUS_AMBIENT := "Ambient"
const BUS_SFX     := "SFX"
## The UI's clicks (sends to SFX, so the effects slider governs them).
const BUS_UI      := "UI"

## Base-track keys the mixer already plays (the sea's own ambience); any other
## key (harbour, processing plant) plays on the context player.
const SEA_BASE_KEYS := ["calm_sea", "storm", "wind", "rain", ""]

## A verb heard through two signals (the facade's and the bridge's) within
## this window plays once.
const VERB_DEDUP_MS := 1500
## Clicks closer together than this are one click (a press that cascades).
const CLICK_MIN_GAP_MS := 30
## A Button with this meta set true makes no click (it plays its own cue).
const NO_CLICK_META := &"no_click_sound"

## 3D loop emitters (attach_loop): full level within EMITTER_UNIT_M, inverse
## distance beyond, silent past EMITTER_MAX_M.
const EMITTER_UNIT_M := 40.0
const EMITTER_MAX_M := 1600.0
const EMITTER_NAME := "EngineLoop"

var _music_volume:   float = 0.7
var _ambient_volume: float = 0.5
var _sfx_volume:     float = 0.8
var _master_volume:  float = 1.0

var _bgm_active:  AudioStreamPlayer
var _bgm_pending: AudioStreamPlayer
var _bgm_fade_t:  float = 0.0
var _bgm_fading:  bool  = false
var _current_bgm_track: String = ""

# Context ambience (harbour, processing plant)
var _amb_base_active:    AudioStreamPlayer
var _amb_base_pending:   AudioStreamPlayer
var _amb_base_fade_t:    float = 0.0
var _amb_base_fading:    bool = false
var _current_base_track: String = ""

var _ambience: AmbienceMixer = null
var _click_player: AudioStreamPlayer = null

const SFX_POOL_SIZE: int = 8
var _sfx_pool: Array[AudioStreamPlayer] = []

## Streams read once and kept; tracks read off the main thread (no disk
## read inside a sim tick).
var _files := AudioStreamCache.new()

var _sim_bridge: Node = null
var _game_bridge: Node = null
var _verb_last_ms: Dictionary = {}   # cue -> ticks_msec
var _last_click_ms: int = -100000

## Counters the UI tests read (they hear nothing headless).
var ui_clicks_played: int = 0
var verb_cues_played: Dictionary = {}   # cue -> count
var last_verb_cue: String = ""

## Safe linear->dB: -inf (from linear_to_db(0.0)) is rejected by Godot's
## volume_db setter and spams "Volume can't be set to NaN". Clamp to silence.
const SILENT_DB := -80.0
func _l2db(v: float) -> float:
	return SILENT_DB if v <= 0.0001 else linear_to_db(v)

func _ready() -> void:
	MusicLibrary.validate_catalog()
	# The short cues come back all game: read them in the background now.
	_files.prefetch(MusicLibrary.UI_SFX.values() + MusicLibrary.ALERT_SFX.values() + MusicLibrary.VERB_SFX.values())
	_create_players()
	_connect_signals()
	_ambience = AmbienceMixer.new()
	_ambience.name = "AmbienceMixer"
	add_child(_ambience)
	_ambience.setup(_files, BUS_AMBIENT)
	_hook_ui_clicks()

func _process(delta: float) -> void:
	_files.poll()
	if _bgm_fading:
		_tick_crossfade(delta)
	if _amb_base_fading:
		_tick_ambient_crossfade(delta)
	if _ambience != null:
		_ambience.sense(get_tree(), _sim_bridge)
		_ambience.step(delta)

func _create_players() -> void:
	_bgm_active  = _make_player(BUS_MUSIC)
	_bgm_pending = _make_player(BUS_MUSIC)
	_bgm_pending.volume_db = _l2db(0.0)

	_amb_base_active    = _make_player(BUS_AMBIENT)
	_amb_base_pending   = _make_player(BUS_AMBIENT)
	_amb_base_pending.volume_db = _l2db(0.0)

	# The UI bus sends to SFX (the effects slider governs the clicks). The
	# project's bus layout has it; a headless or dummy-driver boot that skips
	# the layout gets it here (AudioManager makes Music/SFX/Ambient likewise).
	if AudioServer.get_bus_index(BUS_UI) < 0 and AudioServer.get_bus_index(BUS_SFX) >= 0:
		AudioServer.add_bus()
		AudioServer.set_bus_name(AudioServer.bus_count - 1, BUS_UI)
		AudioServer.set_bus_send(AudioServer.bus_count - 1, BUS_SFX)
	_click_player = _make_player(BUS_UI if AudioServer.get_bus_index(BUS_UI) >= 0 else BUS_SFX)
	_click_player.name = "UiClick"
	_click_player.max_polyphony = 4

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
	_game_bridge = get_node_or_null("/root/GameBridge")
	if has_node("/root/SimBridge"):
		_sim_bridge = get_node("/root/SimBridge")
		_sim_bridge.state_updated.connect(_on_state_updated)
		_sim_bridge.audio_alert_triggered.connect(_on_alert_triggered)
		_connect_if(_sim_bridge, "game_event_raised", _on_game_event)
		_connect_if(_sim_bridge, "pen_action_completed", _on_action_completed)
	else:
		push_warning("AudioController: SimBridge not found")
	_connect_if(_game_bridge, "FarmActionCompleted", _on_action_completed)

func _connect_if(source: Object, signal_name: String, fn: Callable) -> void:
	if source != null and source.has_signal(signal_name) and not source.is_connected(signal_name, fn):
		source.connect(signal_name, fn)

# ── The ambience ──────────────────────────────────────────────────────────────

## The ambience bed (tests and the debug overlay read its layers).
func ambience() -> AmbienceMixer:
	return _ambience

## A looping engine (MusicLibrary.AMBIENT_BASE key, e.g. "feed_barge",
## "boat_engine") on `owner` as a 3D emitter: it moves with the boat and goes
## with it. One per owner; returns the player (null when the file is missing).
func attach_loop(owner: Node3D, key: String, level_db: float = 0.0) -> AudioStreamPlayer3D:
	if owner == null or not is_instance_valid(owner):
		return null
	var existing := owner.get_node_or_null(EMITTER_NAME) as AudioStreamPlayer3D
	if existing != null:
		return existing
	var path := MusicLibrary.ambient_base(key)
	if not _files.exists(path):
		push_warning("AudioController: missing engine loop " + key)
		return null
	var p := AudioStreamPlayer3D.new()
	p.name = EMITTER_NAME
	p.bus = BUS_SFX
	p.unit_size = EMITTER_UNIT_M
	p.max_distance = EMITTER_MAX_M
	p.attenuation_model = AudioStreamPlayer3D.ATTENUATION_INVERSE_DISTANCE
	p.volume_db = level_db
	p.set_meta(&"loop_key", key)
	owner.add_child(p)
	_files.later(path, func(stream: AudioStream) -> void:
		if is_instance_valid(p) and p.is_inside_tree():
			p.stream = stream
			# Start somewhere in the loop, so two barges never pulse in step.
			p.play(fmod(float(owner.get_instance_id() % 997) * 0.061, maxf(stream.get_length() - 1.0, 1.0))))
	return p

## Older positional API (kept for callers): a loop on its own node at a point.
var _loose_emitters: Dictionary = {}  # id -> Node3D

func start_wellboat_sound(job_id: String, position: Vector3, _kind: String) -> void:
	_start_loose("wellboat:" + job_id, position, "boat_engine")

func stop_wellboat_sound(job_id: String) -> void:
	_stop_loose("wellboat:" + job_id)

func start_feed_barge_sound(site_id: String, position: Vector3) -> void:
	_start_loose("barge:" + site_id, position, "feed_barge")

func stop_feed_barge_sound(site_id: String) -> void:
	_stop_loose("barge:" + site_id)

func update_wellboat_position(job_id: String, position: Vector3) -> void:
	var n: Node3D = _loose_emitters.get("wellboat:" + job_id)
	if n != null and is_instance_valid(n):
		n.global_position = position

func update_feed_barge_position(site_id: String, position: Vector3) -> void:
	var n: Node3D = _loose_emitters.get("barge:" + site_id)
	if n != null and is_instance_valid(n):
		n.global_position = position

func _start_loose(id: String, position: Vector3, key: String) -> void:
	if _loose_emitters.has(id):
		return
	var anchor := Node3D.new()
	add_child(anchor)
	anchor.global_position = position
	attach_loop(anchor, key)
	_loose_emitters[id] = anchor

func _stop_loose(id: String) -> void:
	var n: Node3D = _loose_emitters.get(id)
	_loose_emitters.erase(id)
	if n != null and is_instance_valid(n):
		n.queue_free()

# ── The sim's state: music and context ambience ───────────────────────────────

func _on_state_updated() -> void:
	if not _sim_bridge:
		return
	# Seasonal BGM — authoritative key from the simulation bridge.
	var seasonal_key := str(_sim_bridge.audio_seasonal_track)
	if not seasonal_key.is_empty():
		var music_path := MusicLibrary.music(seasonal_key)
		if not _files.exists(music_path):
			music_path = MusicLibrary.music("main_theme")
		if not music_path.is_empty():
			_update_bgm_direct(music_path)
	# The sea's own ambience is the mixer's; a context (harbour, plant) adds its loop.
	var base_key := str(_sim_bridge.audio_base_track)
	_update_ambient_base_direct("" if base_key in SEA_BASE_KEYS else MusicLibrary.ambient_base(base_key))

func _on_alert_triggered(alert_key: String) -> void:
	_play_alert_sfx(alert_key)

## The main menu's music (roadmap U1-09): the main theme, started from the
## menu's _ready so the music bus is live within a second of launch. A game
## hands over to its seasonal track on its first state update.
const MENU_MUSIC_KEY := "main_theme"

func play_menu_music() -> void:
	var path := MusicLibrary.music(MENU_MUSIC_KEY)
	if not path.is_empty():
		_update_bgm_direct(path)

## The music track playing (or fading in) now, as a res:// path.
func get_current_music() -> String:
	return _current_bgm_track

func is_music_playing() -> bool:
	return _bgm_active != null and _bgm_active.playing

## Ask for a music track: it starts (or crossfades in) once it is read, off
## the main thread; a newer request in the meantime wins.
func _update_bgm_direct(path: String) -> void:
	if path == _current_bgm_track:
		return
	if not _files.exists(path):
		push_warning("AudioController: Missing BGM file: " + path)
		return
	_current_bgm_track = path
	_files.later(path, _start_bgm.bind(path))

func _start_bgm(stream: AudioStream, path: String) -> void:
	if path != _current_bgm_track:
		return  # another track was asked for while this one was read
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

## Crossfade the context loop to `path` ("" fades it out); read off the main thread.
func _update_ambient_base_direct(path: String) -> void:
	if path == _current_base_track:
		return
	_current_base_track = path
	if path.is_empty() or not _files.exists(path):
		if _amb_base_active.playing:
			var tween := create_tween()
			tween.tween_property(_amb_base_active, "volume_db", SILENT_DB, AMBIENT_FADE_DURATION)
			tween.tween_callback(_amb_base_active.stop)
		return
	_files.later(path, _start_ambient_base.bind(path))

func _start_ambient_base(stream: AudioStream, path: String) -> void:
	if path != _current_base_track:
		return
	if not _amb_base_active.playing:
		_amb_base_active.stream = stream
		_amb_base_active.volume_db = _l2db(CONTEXT_LAYER_VOLUME)
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
	_amb_base_active.volume_db  = _l2db(CONTEXT_LAYER_VOLUME * (1.0 - t))
	_amb_base_pending.volume_db = _l2db(CONTEXT_LAYER_VOLUME * t)
	if t >= 1.0:
		_amb_base_fading = false
		_amb_base_active.stop()
		var tmp := _amb_base_active
		_amb_base_active  = _amb_base_pending
		_amb_base_pending = tmp

# ── Verbs: the event bus and the player's actions ─────────────────────────────

func _on_game_event(event: Dictionary) -> void:
	var today := int(_game_bridge.CurrentDay) if _game_bridge != null else -1
	play_verb(VerbSounds.for_event(event, today))

func _on_action_completed(action: String, _id: String, success: bool) -> void:
	play_verb(VerbSounds.for_action(action, success))

## Play a verb's cue (VerbSounds key) once: the same verb heard again within
## VERB_DEDUP_MS (a second signal for the same action) is not repeated.
func play_verb(cue: String) -> void:
	if cue.is_empty():
		return
	var now := Time.get_ticks_msec()
	if now - int(_verb_last_ms.get(cue, -100000)) < VERB_DEDUP_MS:
		return
	_verb_last_ms[cue] = now
	var path := MusicLibrary.verb_sfx(cue)
	if play_sfx(path) != null:
		verb_cues_played[cue] = int(verb_cues_played.get(cue, 0)) + 1
		last_verb_cue = cue

func _play_alert_sfx(alert_key: String) -> void:
	var path := MusicLibrary.alert_sfx(alert_key)
	if path.is_empty():
		return
	play_sfx(path)

func _on_sfx_finished(player: AudioStreamPlayer) -> void:
	player.stream = null

## Play a one-shot sound effect from the SFX pool. When every player is busy
## the one that has played longest gives way (a new cue matters more than a tail).
func play_sfx(path: String, volume_db: float = 0.0, pitch: float = 1.0) -> AudioStreamPlayer:
	if not _files.exists(path):
		push_warning("AudioController: Missing SFX file: " + path)
		return null
	var stream := _files.now(path)
	if stream == null:
		push_warning("AudioController: Failed to load SFX: " + path)
		return null
	var player := _get_free_sfx_player()
	player.stream = stream
	player.volume_db = volume_db
	player.pitch_scale = pitch
	player.play()
	return player

func _get_free_sfx_player() -> AudioStreamPlayer:
	var oldest: AudioStreamPlayer = _sfx_pool[0]
	for p in _sfx_pool:
		if not p.playing:
			return p
		if p.get_playback_position() > oldest.get_playback_position():
			oldest = p
	return oldest

# ── UI sounds ─────────────────────────────────────────────────────────────────

## Every BaseButton that enters the tree clicks when pressed (one global hook,
## so no screen has to remember; NO_CLICK_META opts a button out).
func _hook_ui_clicks() -> void:
	get_tree().node_added.connect(_on_node_added)
	_hook_subtree.call_deferred(get_tree().root)

func _hook_subtree(node: Node) -> void:
	_on_node_added(node)
	for child in node.get_children():
		_hook_subtree(child)

func _on_node_added(node: Node) -> void:
	if node is BaseButton and not (node as BaseButton).pressed.is_connected(play_button_click):
		(node as BaseButton).pressed.connect(play_button_click)

## True when `button` clicks on press (the global hook reached it).
func clicks(button: BaseButton) -> bool:
	return button != null and button.pressed.is_connected(play_button_click)

## Play a UI sound by key (button_click, button_hover, notification, etc.)
func play_ui(sound_key: String) -> void:
	var path := MusicLibrary.ui_sfx(sound_key)
	if not _files.exists(path):
		return  # silently skip missing UI sounds (optional audio)
	play_sfx(path)

## The click: its own player on the UI bus (never takes an effect's slot).
func play_button_click() -> void:
	var now := Time.get_ticks_msec()
	if now - _last_click_ms < CLICK_MIN_GAP_MS:
		return
	_last_click_ms = now
	var stream := _files.now(MusicLibrary.ui_sfx("button_click"))
	if stream == null or _click_player == null:
		return
	_click_player.stream = stream
	_click_player.play()
	ui_clicks_played += 1

## The UI bus the click plays on.
func click_bus() -> String:
	return _click_player.bus if _click_player != null else ""

func play_button_hover() -> void:
	play_ui("button_hover")

func play_notification() -> void:
	play_ui("notification")

func play_achievement() -> void:
	var path := MusicLibrary.alert_sfx("achievement")
	if _files.exists(path):
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

## Hover sounds for a UI subtree (the click is global; this adds the hover).
func attach_ui_sounds(root: Node) -> void:
	if root == null:
		return
	if root is BaseButton:
		var btn := root as BaseButton
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
	var idx: int = AudioServer.get_bus_index(BUS_AMBIENT)
	if idx >= 0:
		AudioServer.set_bus_volume_db(idx, _l2db(_ambient_volume))

func set_sfx_volume(vol: float) -> void:
	_sfx_volume = clampf(vol, 0.0, 1.0)
	var idx: int = AudioServer.get_bus_index(BUS_SFX)
	if idx >= 0:
		AudioServer.set_bus_volume_db(idx, _l2db(_sfx_volume))

func set_music_muted(muted: bool) -> void:
	var idx: int = AudioServer.get_bus_index(BUS_MUSIC)
	if idx >= 0:
		AudioServer.set_bus_mute(idx, muted)
