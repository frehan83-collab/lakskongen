class_name TeaserDirector
extends Node
## The teaser (roadmap F2-23 acceptance 6; farm-types spec §6.1 item 1): in
## Første sesong, once, at the first moment after day 150 when no tutorial
## step asks the player for anything, a parody rival's yellow hull crosses
## the horizon beyond the mouth under tow and sails on toward hav_1; a news
## card follows. It is the demo's wishlist hook for the offshore family.
##
## Presentation only: no sim state, nothing saved. It borrows the world
## through a CinematicStage (the clock held and released exactly, the
## presentation hour, clear weather, the mood, the UI hidden) and gives it
## all back, the camera on the player's own view as it was. The engine state
## hash stands where it stood (UiFlowTests: TeaserChecks), and its card
## offers "Fortsett" only: it never offers a build.
##
## When (due()): the tutorial not running, or its step a wait for the world
## ("grow", "sale": the clock runs and the card asks for nothing). A "do"
## step holds the teaser back; past day 300 it never plays. Once: the
## director arms when it sees the game at or before day 150 (a new game, or
## a save loaded from before it) and plays at most once on that timeline, so
## a save loaded after the teaser does not play it again and a game first
## seen after day 150 never does (a load of an earlier day arms it again; a
## new or loaded game is a new timeline whatever its day: the game's
## generation, SimBridge.get_game_generation). A due teaser waits for an open
## card or menu (the critical event card, the briefing, the pause menu, the
## end screen, the inbox) or the first sight to close (CinematicStage
## blocker), and stands still under a card that comes up while it plays.
##
## Its timeline is the "teaser" section of godot/data/first_sight.json; the
## state at a time t is a pure function of t (seek), as the first sight's.
## The Leksikon entry and the locked "Hav" card that follow the news card
## are F2-35's (UnlockNewsCard), hooked on `finished`.

signal started()
signal finished(skipped: bool)

enum Due { WAIT, PLAY, NEVER }

const DATA_PATH := FirstSightDirector.DATA_PATH
const CLOCK_HOLD := "teaser"
const UNIT_SCENE := "res://assets/models/offshore/of1_class.tscn"
const TUG_SCENE := "res://assets/models/offshore_service/anchor_tug.tscn"
## The rival's hull keeps LOD1 out to the horizon: its LOD2 is cut at the
## operating waterline, which a hull towed at its transit draft lifts clear
## of the water.
const RIVAL_LOD_SWITCH := Vector2(500.0, 40000.0)
## The anchor tugs on the tow, each [ahead of the ring, to the side] (m),
## when the data names none: a lead tug on the long line, two on the bow's
## quarters.
const TUGS := [[400.0, 0.0], [250.0, -90.0], [250.0, 90.0]]
## The tow lines: hawser thickness (as it reads far off), the tug's stern
## (half its 75 m length, its deck's height) and where they meet the ring.
const TOW_LINE_M := 0.9
const TUG_STERN := Vector2(37.5, 2.5)
const FAIRLEAD_Y_M := 2.0
const TOW_LINE_COLOR := Color(0.11, 0.11, 0.12)
## The rival's hull under tow: no net yet (the last yard stage goes in at
## the plot).
const TOW_BUILD_STAGE := 5.0

## Test seam: -1 plays only in the player's own game (the main scene; never
## in a test or a QA capture run by scene path), 0 never, 1 always.
static var _auto_forced := -1

var manager: FarmFacilityVisualManager = null
## The "teaser" section of first_sight.json.
var data: Dictionary = {}
var playing := false
var reduced := false
## When true the sequence does not advance by itself (captures and tests seek).
var manual := false
var time := 0.0
var duration := 0.0
## The timeline this director watches: armed when it saw the game at or
## before after_day; played once on it.
var armed := false
var played := false
## Sequences played since the scene started (tests).
var plays := 0
var overlay: FirstSightOverlay = null
var tow: Node3D = null

var _seen := false
var _last_day := -1
## The game of the timeline it watches (SimBridge.get_game_generation).
var _generation := -1
## Due, waiting for a card, a menu or the first sight to close (polled).
var _pending := false
var _rival: OffshoreUnitModel = null
var _tugs: Array[Node3D] = []
var _lines: Array[MeshInstance3D] = []
var _tug_layout: Array = []
var _path: Array[Vector3] = []
var _path_span := Vector2(0.0, 13.0)
var _keys: Array = []
var _stage: CinematicStage = null
## True while the overlay stands aside for a card or menu (_under_modal).
var _stepped_aside := false


static func force_auto_play(value: Variant) -> void:
	_auto_forced = -1 if value == null else (1 if bool(value) else 0)


## Whether the teaser plays by itself when it is due.
static func auto_play() -> bool:
	if _auto_forced >= 0:
		return _auto_forced == 1
	return not SettingsStore.launched_by_path()


## When the teaser is due (spec §6.1 item 1): in its scenario, after
## `after_day`, while no tutorial step asks anything of the player (the
## tutorial not running, or its step a wait), before `skip_from_day`, once
## on a timeline it saw begin before after_day. `tutorial` is
## SimBridge.get_tutorial_status().
static func due(cfg: Dictionary, scenario_id: String, day: int, tutorial: Dictionary, is_armed: bool, has_played: bool) -> Due:
	if has_played or not is_armed or scenario_id != str(cfg.get("scenario", "forste_sesong")):
		return Due.NEVER
	if day >= int(cfg.get("skip_from_day", 300)):
		return Due.NEVER
	if day <= int(cfg.get("after_day", 150)):
		return Due.WAIT
	if bool(tutorial.get("active", false)) and str(tutorial.get("kind", "")) != "wait":
		return Due.WAIT
	return Due.PLAY


func _ready() -> void:
	name = "TeaserDirector"
	var parsed = JSON.parse_string(FileAccess.get_file_as_string(DATA_PATH))
	data = (parsed as Dictionary).get("teaser", {}) if parsed is Dictionary else {}
	set_process(false)
	var sim := get_node_or_null("/root/SimBridge")
	if sim != null and sim.has_signal("day_advanced"):
		sim.day_advanced.connect(_on_day)


func stage() -> CinematicStage:
	if _stage == null:
		_stage = CinematicStage.new(manager, CLOCK_HOLD)
	return _stage


## Every state refresh: a new timeline arms the teaser (or not), a due one
## plays it on the next frame, once nothing holds it back.
func _on_day(day: int, _season: String) -> void:
	var sim := get_node_or_null("/root/SimBridge")
	var generation: int = sim.get_game_generation() if sim != null and sim.has_method("get_game_generation") else _generation
	if not _seen or day < _last_day or generation != _generation:
		# A new game, a save loaded, or a save of an earlier day loaded: this
		# timeline's teaser has not played; it can only if the timeline
		# starts in time.
		_seen = true
		_generation = generation
		armed = day <= int(data.get("after_day", 150))
		played = false
		_pending = false
	_last_day = day
	if playing or not auto_play() or manager == null or sim == null:
		return
	var verdict := _verdict(sim, day)
	if verdict == Due.PLAY:
		# On the next frame (the refresh that made it due finishes first), or
		# once a card, a menu or the first sight has closed.
		_pending = true
		set_process(true)
	elif verdict == Due.NEVER and armed and day >= int(data.get("skip_from_day", 300)):
		armed = false


func _verdict(sim: Node, day: int) -> Due:
	return due(data, str(sim.get_active_scenario_id()), day, sim.get_tutorial_status(), armed, played)


## A due teaser starts when nothing holds it back (CinematicStage blocker),
## if it is still due then.
func _start_pending() -> void:
	if not _pending:
		set_process(false)
		return
	if stage().blocker() != "":
		return
	_pending = false
	set_process(false)
	var sim := get_node_or_null("/root/SimBridge")
	if sim != null and auto_play() and _verdict(sim, _last_day) == Due.PLAY:
		play()


## Play the teaser now; false when it cannot (no data, no camera, playing).
func play() -> bool:
	if playing or manager == null or manager.camera == null or data.is_empty():
		return false
	if manager.first_sight != null and manager.first_sight.playing:
		return false
	played = true
	plays += 1
	reduced = MotionPrefs.reduced_motion()
	playing = true
	time = 0.0
	_stepped_aside = false
	_build_overlay()
	if reduced:
		overlay.set_still(_grab_frame())
	stage().enter(data)
	_build_tow()
	_plan_camera()
	duration = _reduced_duration() if reduced else float(data.get("seconds", 13.0))
	set_process(true)
	seek(0.0)
	started.emit()
	return true


## Skip to the end (Esc, a click): the world given back.
func skip() -> void:
	if playing:
		_finish(true)


func _process(delta: float) -> void:
	if not playing:
		_start_pending()
		return
	# A card or menu came up over it (an event between ticks, the window's
	# close button): the teaser stands still and steps aside (its overlay
	# hidden, the clock still held) until it closes.
	if _under_modal() or manual:
		return
	seek(time + delta)
	if time >= duration:
		_finish(false)


## Stand the teaser at `t` seconds: the tow, the camera and the overlay are
## functions of t.
func seek(t: float) -> void:
	if not playing:
		return
	time = clampf(t, 0.0, duration)
	if reduced:
		_seek_still(time)
		return
	_tow_at(time)
	_camera(time)
	_overlay_at(time)


func _finish(skipped: bool) -> void:
	_clear_tow()
	stage().leave()
	var cam := manager.camera as FjordCamera
	if cam != null:
		# The player's own view, as it stood: the rig's targets never moved.
		cam._snap()
	if overlay != null:
		overlay.queue_free()
		overlay = null
	playing = false
	set_process(false)
	finished.emit(skipped)


## True while a card or menu is up over the playing teaser: the overlay
## steps aside for it (and comes back when it closes; only then is its
## visibility touched).
func _under_modal() -> bool:
	var under := stage().modal() != ""
	if under != _stepped_aside and overlay != null:
		overlay.visible = not under
	_stepped_aside = under
	return under


# ── The tow ───────────────────────────────────────────────────────────────────

## The rival's hull at its transit draft, no net, and its three tugs.
func _build_tow() -> void:
	_clear_tow()
	var spec: Dictionary = data.get("tow", {})
	_path.clear()
	for p: Array in spec.get("path", []):
		_path.append(Vector3(float(p[0]), 0.0, float(p[1])))
	var span: Array = spec.get("seconds", [0.0, float(data.get("seconds", 13.0))])
	_path_span = Vector2(float(span[0]), float(span[1]))
	tow = Node3D.new()
	tow.name = "TeaserTow"
	manager.add_child(tow)
	if ResourceLoader.exists(UNIT_SCENE):
		_rival = (load(UNIT_SCENE) as PackedScene).instantiate() as OffshoreUnitModel
		_rival.name = "RivalHull"
		_rival.lod_switch = RIVAL_LOD_SWITCH
		tow.add_child(_rival)
		_rival.set_build_stage(TOW_BUILD_STAGE)
	_tugs.clear()
	_lines.clear()
	_tug_layout = spec.get("tugs", TUGS)
	var line_material := StandardMaterial3D.new()
	line_material.albedo_color = TOW_LINE_COLOR
	line_material.roughness = 1.0
	var line_mesh := BoxMesh.new()
	line_mesh.size = Vector3(TOW_LINE_M, TOW_LINE_M, 1.0)
	line_mesh.material = line_material
	for k in _tug_layout.size():
		var tug: Node3D = null
		if ResourceLoader.exists(TUG_SCENE):
			tug = (load(TUG_SCENE) as PackedScene).instantiate() as Node3D
		else:
			tug = Node3D.new()
		tug.name = "Tug%d" % k
		tow.add_child(tug)
		_tugs.append(tug)
		var line := MeshInstance3D.new()
		line.name = "TowLine%d" % k
		line.mesh = line_mesh
		line.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		tow.add_child(line)
		_lines.append(line)


func _clear_tow() -> void:
	if tow != null:
		tow.queue_free()
	tow = null
	_rival = null
	_tugs.clear()
	_lines.clear()


## Where the tow's hull stands at t (on the water, its axis), and its heading.
func tow_pose(t: float) -> Dictionary:
	if _path.is_empty():
		return {"pos": Vector3.ZERO, "heading": Vector3.FORWARD}
	var s := clampf(inverse_lerp(_path_span.x, _path_span.y, t), 0.0, 1.0)
	var at := _along(s)
	var ahead := _along(minf(s + 0.01, 1.0)) - _along(maxf(s - 0.01, 0.0))
	ahead.y = 0.0
	return {"pos": at, "heading": ahead.normalized() if ahead.length() > 0.01 else Vector3.FORWARD}


## A point at share s of the tow's path (Catmull-Rom through its points).
func _along(s: float) -> Vector3:
	var n := _path.size()
	if n == 1:
		return _path[0]
	var f := s * float(n - 1)
	var i := clampi(int(floor(f)), 0, n - 2)
	var u := f - float(i)
	return _path[i].cubic_interpolate(_path[i + 1], _path[maxi(i - 1, 0)], _path[mini(i + 2, n - 1)], u)


func _tow_at(t: float) -> void:
	if tow == null:
		return
	var pose := tow_pose(t)
	var at: Vector3 = pose["pos"]
	var heading: Vector3 = pose["heading"]
	var lift := 0.0
	if _rival != null:
		lift = _rival.draft_m - float((data.get("tow", {}) as Dictionary).get("transit_draft_m", _rival.draft_m))
		_rival.global_position = at + Vector3.UP * lift
		_rival.global_basis = Basis(Vector3.UP, atan2(-heading.x, -heading.z))
	var side := heading.cross(Vector3.UP)
	var radius := _rival.circumradius_m if _rival != null else 55.0
	for k in _tugs.size():
		var tug := _tugs[k]
		var off: Array = _tug_layout[k]
		var p := at + heading * (radius + float(off[0])) + side * float(off[1])
		tug.global_position = Vector3(p.x, tug.global_position.y, p.z)
		tug.look_at(Vector3(p.x, tug.global_position.y, p.z) + heading, Vector3.UP)
		# The hawser from the tug's stern to the ring's leading column.
		var stern := p - heading * TUG_STERN.x + Vector3.UP * TUG_STERN.y
		var fair := at + heading * radius + side * clampf(float(off[1]) * 0.2, -radius * 0.4, radius * 0.4) + Vector3.UP * FAIRLEAD_Y_M
		_place_line(_lines[k], fair, stern)


## A tow line's box stretched from `a` to `b`.
static func _place_line(line: MeshInstance3D, a: Vector3, b: Vector3) -> void:
	var length := a.distance_to(b)
	if length < 0.01:
		line.visible = false
		return
	line.visible = true
	var basis := Basis.looking_at((b - a) / length, Vector3.UP)
	line.global_transform = Transform3D(basis.scaled_local(Vector3(1.0, 1.0, length)), (a + b) * 0.5)


# ── The camera ────────────────────────────────────────────────────────────────

func _plan_camera() -> void:
	_keys.clear()
	for key: Dictionary in data.get("keys", []):
		var eye: Array = key.get("eye", [0.0, 30.0, 0.0])
		_keys.append({"t": float(key.get("t", 0.0)), "eye": Vector3(float(eye[0]), float(eye[1]), float(eye[2])),
			"look": key.get("look", ["tow", [0.0, 0.0, 0.0]])})


## The look of a key at t: ["tow", [x, y, z]] the tow's hull plus an offset,
## or [x, y, z] a place in the world.
func _look_of(spec: Array, t: float) -> Vector3:
	if spec.size() == 2 and spec[0] is String:
		var off: Array = spec[1]
		var pose := tow_pose(t)
		return (pose["pos"] as Vector3) + Vector3(float(off[0]), float(off[1]), float(off[2]))
	return Vector3(float(spec[0]), float(spec[1]), float(spec[2]))


func _camera(t: float) -> void:
	var cam := manager.camera as FjordCamera
	if cam == null or _keys.is_empty():
		return
	var eye := _key_path(t, "eye")
	var look := _look_at_time(t)
	var fov: Array = data.get("fov_deg", [22.0, 22.0])
	var k := smoothstep(0.0, 1.0, clampf(t / maxf(duration, 1e-3), 0.0, 1.0))
	cam.cinematic_pose(eye, look, lerpf(float(fov[0]), float(fov[1]), k))


## The eyes through the keys (Catmull-Rom, eased out of the first and into
## the last).
func _key_path(t: float, part: String) -> Vector3:
	var n := _keys.size()
	if n == 1 or t <= float(_keys[0]["t"]):
		return _keys[0][part]
	if t >= float(_keys[n - 1]["t"]):
		return _keys[n - 1][part]
	var i := 0
	while i < n - 2 and t > float(_keys[i + 1]["t"]):
		i += 1
	var t0 := float(_keys[i]["t"])
	var t1 := float(_keys[i + 1]["t"])
	var s := smoothstep(0.0, 1.0, clampf((t - t0) / maxf(t1 - t0, 1e-4), 0.0, 1.0)) if n == 2 else clampf((t - t0) / maxf(t1 - t0, 1e-4), 0.0, 1.0)
	var p0: Vector3 = _keys[maxi(i - 1, 0)][part]
	var p1: Vector3 = _keys[i][part]
	var p2: Vector3 = _keys[i + 1][part]
	var p3: Vector3 = _keys[mini(i + 2, n - 1)][part]
	return p1.cubic_interpolate(p2, p0, p3, s)


## The look at t: between the two keys round t, each key's look read at t.
func _look_at_time(t: float) -> Vector3:
	var n := _keys.size()
	if n == 1 or t <= float(_keys[0]["t"]):
		return _look_of(_keys[0]["look"], t)
	if t >= float(_keys[n - 1]["t"]):
		return _look_of(_keys[n - 1]["look"], t)
	var i := 0
	while i < n - 2 and t > float(_keys[i + 1]["t"]):
		i += 1
	var s := smoothstep(0.0, 1.0, clampf(inverse_lerp(float(_keys[i]["t"]), float(_keys[i + 1]["t"]), t), 0.0, 1.0))
	return _look_of(_keys[i]["look"], t).lerp(_look_of(_keys[i + 1]["look"], t), s)


# ── The overlay: the bars, the news card ─────────────────────────────────────

func _build_overlay() -> void:
	overlay = FirstSightOverlay.new()
	overlay.name = "TeaserOverlay"
	var root := manager.owner if manager.owner != null else manager.get_tree().current_scene
	root.add_child(overlay)
	overlay.offer_save(false)
	var plot_id := str(data.get("plot", "hav_1"))
	var sim := get_node_or_null("/root/SimBridge")
	if not manager.plot_names.has(plot_id) and sim != null:
		manager._refresh_plot_names(sim)
	var plot := str(manager.plot_names.get(plot_id, ""))
	if plot == "":
		plot = tr("TEASER_PLOT_FALLBACK")
	overlay.set_card(tr("TEASER_KICKER"), tr("TEASER_TITLE") % [tr("TEASER_RIVAL"), plot], tr("TEASER_TEXT"))
	overlay.skip_requested.connect(func() -> void:
		if overlay != null and overlay.card.visible:
			_finish(false)
		else:
			skip())
	overlay.continue_requested.connect(func() -> void: _finish(false))


func _overlay_at(t: float) -> void:
	if overlay == null:
		return
	var fade_in := float(data.get("fade_in_seconds", 0.6))
	var restore := float(data.get("restore_fade_seconds", 0.45))
	var card: Array = data.get("card_seconds", [duration - 4.0, duration - 1.0])
	var black := maxf(1.0 - smoothstep(0.0, fade_in, t), smoothstep(duration - restore, duration, t))
	var card_alpha := smoothstep(float(card[0]), float(card[0]) + 0.8, t)
	var bars := smoothstep(0.0, 0.8, t) * (1.0 - smoothstep(duration - restore, duration, t))
	overlay.show_state(bars, black, card_alpha, 0.0, smoothstep(0.6, 1.4, t) * (1.0 - card_alpha))


# ── Reduced motion: a still and a cross-fade ─────────────────────────────────

func _reduced_duration() -> float:
	var rm: Dictionary = data.get("reduced_motion", {})
	return float(rm.get("crossfade_seconds", 2.0)) + float(rm.get("hold_seconds", 4.5)) + float(data.get("restore_fade_seconds", 0.45))


## The tow stood at its still moment, the camera there; the frame the
## player was on cross-fades out over it, then the card.
func _seek_still(t: float) -> void:
	var rm: Dictionary = data.get("reduced_motion", {})
	var at := float(rm.get("still_at_seconds", 6.0))
	_tow_at(at)
	_camera(at)
	var fade := float(rm.get("crossfade_seconds", 2.0))
	var restore := float(data.get("restore_fade_seconds", 0.45))
	var still := 1.0 - clampf(t / maxf(fade, 1e-3), 0.0, 1.0)
	var black := smoothstep(duration - restore, duration, t)
	overlay.show_state(1.0 - black, black, clampf((t - fade) / 0.6, 0.0, 1.0), still, 0.0)


func _grab_frame() -> Texture2D:
	var vp := get_viewport()
	if vp == null or DisplayServer.get_name() == "headless":
		return null
	var img := vp.get_texture().get_image()
	return ImageTexture.create_from_image(img) if img != null else null
