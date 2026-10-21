class_name FirstSightDirector
extends Node
## First sight (roadmap F2-23; farm-types spec §6.4): when the company's
## first unit of a variant comes into operation, one sequence of at most 20 s
## shows it, a reward the player earned over a build of months:
##
## - land (the LAND 2 mood, ref L2): dusk with a pink-violet sky (the Blåtime
##   mood); a 3-5 s construction timelapse on the pad, then the camera low
##   over the quay follows the orange pipe bridge to the tanks as the lid
##   work-lights come on, and pulls up to the campus's own -40 deg framing;
## - offshore (ref O3): low sun; the unit rises part by part, then the camera
##   starts behind the orange service vessel and rises to the low oblique
##   framing of the yellow ring on deep blue, the fjord mouth and its hills on
##   the horizon, while a wellboat comes in;
## both end on the unit's card with "Lagre bilde" (the photo-mode prompt,
## UX-F3: the frame without the overlay to user://screenshots) and
## "Fortsett". Esc or a click skips it. A repeat completion of the variant
## gets a 3 s toast that flies the camera there. With reduced motion the
## sequence is a still: the frame cross-fades in 2 s to the unit's framing
## in the same light, and holds.
##
## It changes nothing in the game: it borrows the world through a
## CinematicStage (the clock held and released exactly, the presentation
## hour pinned (D20), clear weather staged, the mood leaned through
## EnvironmentDirector.set_mood_hook, the UI hidden), replays the
## construction through the presenter's own drawing (the land lane's phases,
## in order; the offshore part groups), and gives every one of them back.
## The sim day and every stream stand where they stood (UiFlowTests).
##
## Its timeline lives in godot/data/first_sight.json. The state at a time t
## is a pure function of t (seek), so captures and tests stand it at any
## moment.

signal started(unit_id: String, family: String)
signal finished(unit_id: String, skipped: bool)

const DATA_PATH := "res://data/first_sight.json"
const CLOCK_HOLD := "first_sight"
const WELLBOAT_SCENE := "res://assets/models/wellboat/wellboat.tscn"
const SCREENSHOT_DIR := "user://screenshots"
## The construction phases the timelapse replays, the land lane's (LandCampusBuilder.PHASES).
const LAND_PHASES := LandCampusBuilder.PHASES
## The wellboat comes in from this far out (m) to this far off the ring.
const WELLBOAT_FROM_M := 760.0
const WELLBOAT_TO_M := 95.0
## A light wave's radius that lights everything.
const LIGHTS_ALL_ON := 10000000.0

## Test seam: -1 plays on a completion only in the player's own game (the
## main scene; never in a test or a QA capture run by scene path), 0 never,
## 1 always.
static var _auto_forced := -1

var manager: FarmFacilityVisualManager = null
var data: Dictionary = {}
var unit_id := ""
var family := ""
var playing := false
## True while the reduced-motion still plays instead of the camera path.
var reduced := false
## Seconds into the sequence; its length (the plan's end).
var time := 0.0
var duration := 0.0
## When true the sequence does not advance by itself (captures and tests seek).
var manual := false
## Completions waiting their turn: [unit id].
var queue: Array[String] = []
## The sequences played this session, and the toasts shown (tests read them).
var played: Array[String] = []
var toasts: Array[String] = []
var overlay: FirstSightOverlay = null

var _plan: Dictionary = {}
## How far the unit's own build had come when its sequence began (1 built):
## the timelapse replays up to it, so it ends on the real unit.
var _build_end := 1.0
var _keys: Array = []
var _final_eye := Vector3.ZERO
var _final_look := Vector3.ZERO
var _final_fov := 45.0
## What the sequence borrows from the game and gives back.
var _stage: CinematicStage = null
var _real_row: Dictionary = {}
var _wellboat: Node3D = null
var _wellboat_from := Vector3.ZERO
var _wellboat_to := Vector3.ZERO
## The light wave's origin (the bridge foot on the quay) and how far it runs
## to light the whole campus (m).
var _wave_origin := Vector2.ZERO
var _wave_reach := 800.0
## True while the overlay stands aside for a card or menu (_under_modal).
var _stepped_aside := false


static func force_auto_play(value: Variant) -> void:
	_auto_forced = -1 if value == null else (1 if bool(value) else 0)


## Whether a completion plays its sequence by itself.
static func auto_play() -> bool:
	if _auto_forced >= 0:
		return _auto_forced == 1
	return not SettingsStore.launched_by_path()


func _ready() -> void:
	name = "FirstSightDirector"
	var text := FileAccess.get_file_as_string(DATA_PATH)
	var parsed = JSON.parse_string(text)
	data = parsed if parsed is Dictionary else {}
	set_process(false)
	if manager != null:
		# Deferred: the presenter's sync (and the tick that brought it) ends
		# first, so a card that tick opened is up when the sequence would start.
		manager.unit_completed.connect(_on_unit_completed, CONNECT_DEFERRED)
		manager.new_game_seen.connect(on_new_game)


## The family a variant's sequence is ("land" or "hav").
static func family_of(row: Dictionary) -> String:
	return "land" if str(row.get("family", "")) == "land" else "hav"


## A unit came into operation (the presenter's unit_completed): `first`
## plays the sequence, a repeat shows the toast. The sequence waits its
## turn behind another one, an open card or menu (the critical event card,
## the briefing, the pause menu, the end screen, the inbox: CinematicStage
## blocker) and starts when it closes (wave4b int review).
func on_unit_completed(id: String, first: bool) -> void:
	if not auto_play():
		return
	if not first:
		_toast(id)
		return
	queue.append(id)
	_start_queued()


func _on_unit_completed(id: String, _variant: String, first: bool) -> void:
	on_unit_completed(id, first)


## A new or loaded game (the presenter saw the game change): the sequences
## waiting belong to the game that went, and one playing gives the world back.
func on_new_game() -> void:
	queue.clear()
	if playing:
		_finish(true)


## Start the next sequence waiting, unless one plays or a card or menu is up
## (then it is polled every frame until that closes).
func _start_queued() -> void:
	if playing:
		return
	while not queue.is_empty():
		if stage().blocker() != "":
			set_process(true)
			return
		var id: String = queue.pop_front()
		if play(id):
			return
		# No unit to show yet (the Havfarm-class hull is F2-29's): the toast.
		_toast(id)
	set_process(false)


## Play the sequence of unit `id`; false when it cannot (no data, no unit drawn).
func play(id: String) -> bool:
	if playing or manager == null or not manager.views.has(id) or manager.visual_of(id) == null:
		return false
	var row: Dictionary = manager.views[id]
	family = family_of(row)
	_plan = (data.get("families", {}) as Dictionary).get(family, {})
	if _plan.is_empty():
		return false
	unit_id = id
	_real_row = row.duplicate(true)
	_build_end = build_share(_real_row)
	reduced = MotionPrefs.reduced_motion()
	playing = true
	time = 0.0
	played.append(id)
	_stepped_aside = false
	_build_overlay(row)
	if reduced:
		overlay.set_still(_grab_frame())
	_enter()
	_suppress_chip(true)
	_plan_camera()
	duration = _reduced_duration() if reduced else float(data.get("max_seconds", 20.0))
	set_process(true)
	seek(0.0)
	started.emit(id, family)
	return true


## Skip to the end (Esc, a click): the unit framed, the world given back.
func skip() -> void:
	if playing:
		_finish(true)


func _process(delta: float) -> void:
	if not playing:
		_start_queued()
		return
	# A card or menu came up over the sequence (an event between ticks, the
	# window's close button): the sequence stands still and steps aside (its
	# overlay hidden, so the card takes the keys and clicks; the clock still
	# held) until it closes.
	if _under_modal() or manual:
		return
	seek(time + delta)
	if time >= duration:
		_finish(false)


## Stand the sequence at `t` seconds: the camera, the timelapse, the lights,
## the wellboat and the overlay are all functions of t.
func seek(t: float) -> void:
	if not playing:
		return
	time = clampf(t, 0.0, duration)
	if reduced:
		_seek_still(time)
		return
	var tl: Array = _plan.get("timelapse_seconds", [0.0, 4.0])
	var build := clampf(inverse_lerp(float(tl[0]), float(tl[1]), time), 0.0, 1.0)
	_timelapse(build)
	_lights(time)
	_camera(time)
	_wellboat_at(time)
	_overlay_at(time)


## True while a card or menu is up over the playing sequence: the overlay
## steps aside for it (and comes back when it closes; only then is its
## visibility touched).
func _under_modal() -> bool:
	var under := stage().modal() != ""
	if under != _stepped_aside and overlay != null:
		overlay.visible = not under
	_stepped_aside = under
	return under


# ── Environment ──────────────────────────────────────────────────────────────

func stage() -> CinematicStage:
	if _stage == null:
		_stage = CinematicStage.new(manager, CLOCK_HOLD)
	return _stage


## Hold the clock, pin the hour to the sequence's sun, clear the weather,
## lean the mood, hide the game's UI (CinematicStage).
func _enter() -> void:
	stage().enter(_plan)


## Give everything back (CinematicStage), the player landing in the world:
## the camera on the unit's own framing, no jump.
func _leave() -> void:
	var cam := manager.camera as FjordCamera
	stage().leave()
	var game_ui := stage().game_ui()
	if game_ui != null and game_ui.has_method("show_tab"):
		# The player lands in the world, on the unit they have just seen.
		game_ui.call("show_tab", "fjord")
	if cam != null:
		cam.focus_target(unit_id, 0.0)
		cam._snap()


func _finish(skipped: bool) -> void:
	_timelapse(1.0)
	manager.light_override = -1.0
	manager.set_light_wave(Vector2.ZERO, LIGHTS_ALL_ON)
	_suppress_chip(false)
	_clear_wellboat()
	_leave()
	if overlay != null:
		overlay.queue_free()
		overlay = null
	playing = false
	set_process(false)
	var id := unit_id
	finished.emit(id, skipped)
	_start_queued()


## The hour of today when the sun, going down after noon, stands at
## `elevation_deg` (CinematicStage.hour_for_sun).
func hour_for_sun(latitude: float, day: int, elevation_deg: float) -> float:
	return stage().hour_for_sun(latitude, day, elevation_deg)


# ── The camera path ──────────────────────────────────────────────────────────

## The keys in world space: each anchor (data's anchor names) plus its
## offset in the unit's own frame; the last key stands where the camera's
## own framing of the unit does (its rig's solution), so handing it back
## does not move it.
func _plan_camera() -> void:
	var cam := manager.camera as FjordCamera
	var target := cam.get_focus_target(unit_id)
	var focus: Vector3 = target.get("pos", Vector3.ZERO)
	var distance := float(target.get("distance", 520.0))
	var pitch := float(target.get("pitch", -40.0))
	var yaw := float(target.get("yaw", NAN))
	if not is_finite(yaw):
		yaw = cam.home_yaw(focus)
	_final_look = Vector3(focus.x, 0.0, focus.z)
	_final_eye = cam.solve_rig(_final_look, yaw, distance, clampf(pitch, FjordCamera.MIN_PITCH_DEG, FjordCamera.MAX_PITCH_DEG))
	_final_fov = FjordCamera.view_for_distance(distance).y
	var frame := _unit_frame()
	var anchors: Dictionary = frame["anchors"]
	if anchors.has("quay"):
		var q: Vector3 = anchors["quay"]
		_wave_origin = Vector2(q.x, q.z)
		var campus := manager.visual_of(unit_id) as LandCampusBuilder
		var r := campus.blast_rect()
		_wave_reach = 0.0
		for corner: Vector2 in [Vector2(r.x, r.y), Vector2(r.z, r.y), Vector2(r.x, r.w), Vector2(r.z, r.w)]:
			_wave_reach = maxf(_wave_reach, corner.distance_to(_wave_origin))
		_wave_reach += manager.LIGHT_WAVE_WIDTH_M
	_keys.clear()
	for key: Dictionary in _plan.get("keys", []):
		_keys.append({"t": float(key["t"]), "eye": _anchor(key["eye"], frame), "look": _anchor(key["look"], frame)})


## The unit's own frame and its anchors: {"basis": Basis (x, up, z), "anchors": {name: Vector3}}.
func _unit_frame() -> Dictionary:
	var node := manager.visual_of(unit_id)
	var anchors := {"final": _final_eye, "focus": _final_look}
	var basis := Basis.IDENTITY
	if node is LandCampusBuilder:
		var campus := node as LandCampusBuilder
		var pf := campus.pad_frame()
		basis = pf.basis
		var h := campus.half_extents()
		var bridge_v := LandCampusBuilder.bridge_line_v(h)
		anchors["campus"] = campus.focus_point()
		anchors["quay"] = pf * Vector3(h.x, 0.0, bridge_v)
		anchors["bridge"] = pf * Vector3(h.x - LandCampusBuilder.BRIDGE_SPAN * LandCampusBuilder.BRIDGE_SPANS * 0.5, 0.0, bridge_v)
		anchors["lots"] = campus.lot_centre(0) if not campus.tanks.is_empty() else campus.focus_point()
	elif node is OffshoreUnitVisual:
		var unit := node as OffshoreUnitVisual
		var centre := unit.global_transform * unit.station
		var vessel := centre + Vector3(1.0, 0.0, 0.0) * 60.0
		for child in unit.fleet.get_children():
			if child is Node3D:
				vessel = (child as Node3D).global_position
				break
		var out := Vector3(vessel.x - centre.x, 0.0, vessel.z - centre.z)
		out = out.normalized() if out.length() > 1.0 else Vector3.RIGHT
		basis = Basis(out, Vector3.UP, out.cross(Vector3.UP))
		anchors["unit"] = centre
		anchors["vessel"] = Vector3(vessel.x, 0.0, vessel.z)
	return {"basis": basis, "anchors": anchors}


## An [anchor, [a, b, y]] key in world space (offsets along the frame's x and z, y up).
static func _anchor(spec: Array, frame: Dictionary) -> Vector3:
	var at: Vector3 = (frame["anchors"] as Dictionary).get(str(spec[0]), Vector3.ZERO)
	var off: Array = spec[1] if spec.size() > 1 else [0.0, 0.0, 0.0]
	var b: Basis = frame["basis"]
	return at + b.x * float(off[0]) + b.z * float(off[1]) + Vector3.UP * float(off[2])


func _camera(t: float) -> void:
	var cam := manager.camera as FjordCamera
	if cam == null or _keys.is_empty():
		return
	var eye := _path(t, "eye")
	var look := _path(t, "look")
	var last: Dictionary = _keys[_keys.size() - 1]
	var before: Dictionary = _keys[maxi(_keys.size() - 2, 0)]
	var fov_k := clampf(inverse_lerp(float(before["t"]), float(last["t"]), t), 0.0, 1.0)
	cam.cinematic_pose(eye, look, lerpf(float(_plan.get("fov_deg", 45.0)), _final_fov, smoothstep(0.0, 1.0, fov_k)))


## Catmull-Rom through the keys' `part` ("eye" or "look") at time t, eased
## out of the first key and into the last.
func _path(t: float, part: String) -> Vector3:
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
	var s := clampf((t - t0) / maxf(t1 - t0, 1e-4), 0.0, 1.0)
	# Eased out of the first key and into the last; the keys between keep their pace.
	if n == 2:
		s = smoothstep(0.0, 1.0, s)
	elif i == 0:
		s = s * s
	elif i == n - 2:
		s = 1.0 - (1.0 - s) * (1.0 - s)
	var p0: Vector3 = _keys[maxi(i - 1, 0)][part]
	var p1: Vector3 = _keys[i][part]
	var p2: Vector3 = _keys[i + 1][part]
	var p3: Vector3 = _keys[mini(i + 2, n - 1)][part]
	return p1.cubic_interpolate(p2, p0, p3, s)


# ── The timelapse, the lights, the wellboat ──────────────────────────────────

## The unit as built to share `s` of its construction (0 nothing .. 1 the
## real unit): a campus through the land lane's phases in order, its tanks
## handed over through the last two; an offshore unit's part groups.
func _timelapse(s: float) -> void:
	var node := manager.visual_of(unit_id)
	if node is LandCampusBuilder:
		var campus := node as LandCampusBuilder
		# The engine's row as the presenter holds it now (a sync while the
		# sequence plays brings the day's newest state).
		var real: Dictionary = manager.views.get(unit_id, _real_row)
		if s >= 1.0:
			campus.apply_state(real)
		else:
			campus.apply_state(replay_row(real, s * _build_end))
		manager._paint_pads()
	elif node is OffshoreUnitVisual:
		var unit := node as OffshoreUnitVisual
		unit.unit.set_build_stage(6.0 * s)
		unit.fleet.visible = s >= 1.0


## How far a land row's build has come (0-1): 1 once built, else its phase
## and the phase's share over the land lane's phases.
static func build_share(row: Dictionary) -> float:
	var build: Dictionary = row.get("build", {})
	var key := str(build.get("phase_key", "operational"))
	var at := LAND_PHASES.find(key)
	if key == "operational" or str(build.get("status", "")) == "complete" or at < 0:
		return 1.0
	return clampf((float(at) + float(build.get("phase_progress", 0.0))) / float(LAND_PHASES.size()), 0.0, 1.0)


## A land row stood at share `s` of its build: the land lane's phases in
## order (equal shares), each lot with the site, its tanks handed over one by
## one through first_tanks and completion, nothing fed yet.
static func replay_row(row: Dictionary, s: float) -> Dictionary:
	var out := row.duplicate(true)
	var p := clampf(s, 0.0, 0.9999) * float(LAND_PHASES.size())
	var index := int(floor(p))
	var build := {"phase_key": LAND_PHASES[index], "phase_progress": p - float(index), "status": "building",
		"phase_index": index, "phase_count": LAND_PHASES.size()}
	out["build"] = build
	out["operational"] = false
	out["feeding"] = false
	for lot: Dictionary in out.get("lots", []):
		lot["build"] = build.duplicate()
		var tanks: Array = lot.get("tanks", [])
		for k in tanks.size():
			var hand := 4.0 + 2.0 * float(k + 1) / float(tanks.size() + 1)
			tanks[k]["ready"] = p >= hand
			tanks[k]["stocked"] = false
	return out


## The campus's lights come on as the camera passes (land): the level held
## at full night while a wave from the quay switches the lamps, the
## windows and the lids' work-lights on, over the campus in the lights-on
## window; offshore the dusk's own level.
func _lights(t: float) -> void:
	var on: Array = _plan.get("lights_on_seconds", [])
	if on.size() < 2:
		manager.light_override = -1.0
		return
	manager.light_override = 1.0
	var share := clampf(inverse_lerp(float(on[0]), float(on[1]), t), 0.0, 1.0)
	manager.set_light_wave(_wave_origin, LIGHTS_ALL_ON if share >= 1.0 else share * _wave_reach)


func _wellboat_at(t: float) -> void:
	var span: Array = _plan.get("wellboat_seconds", [])
	if span.size() < 2:
		return
	if _wellboat == null:
		_spawn_wellboat()
	if _wellboat == null:
		return
	var k := smoothstep(0.0, 1.0, clampf(inverse_lerp(float(span[0]), float(span[1]), t), 0.0, 1.0))
	var at := _wellboat_from.lerp(_wellboat_to, k)
	_wellboat.global_position = Vector3(at.x, _wellboat.global_position.y, at.z)


## A wellboat to come in to the unit: from the open sea on the unit's far
## side from its service vessel, bow to the ring.
func _spawn_wellboat() -> void:
	var unit := manager.visual_of(unit_id) as OffshoreUnitVisual
	if unit == null or not ResourceLoader.exists(WELLBOAT_SCENE):
		return
	var centre := unit.global_transform * unit.station
	var frame := _unit_frame()
	var b: Basis = frame["basis"]
	var dir := (-b.x * 0.55 - b.z).normalized()
	_wellboat_from = centre + dir * WELLBOAT_FROM_M
	_wellboat_to = centre + dir * (unit.unit.circumradius_m + WELLBOAT_TO_M)
	_wellboat = (load(WELLBOAT_SCENE) as PackedScene).instantiate() as Node3D
	_wellboat.name = "FirstSightWellboat"
	manager.add_child(_wellboat)
	_wellboat.global_position = _wellboat_from
	_wellboat.look_at(centre, Vector3.UP)


## A cinematic carries no world UI: every unit's sea chip waits.
func _suppress_chip(on: bool) -> void:
	for id: String in manager.visuals:
		var unit := manager.visuals[id] as OffshoreUnitVisual
		if unit != null:
			unit.chip_suppressed = on


func _clear_wellboat() -> void:
	if _wellboat != null:
		_wellboat.queue_free()
		_wellboat = null


# ── The overlay ───────────────────────────────────────────────────────────────

func _build_overlay(row: Dictionary) -> void:
	overlay = FirstSightOverlay.new()
	var root := manager.owner if manager.owner != null else manager.get_tree().current_scene
	root.add_child(overlay)
	var variant := tr("FARM_VARIANT_%s" % str(row.get("variant", "")).to_upper())
	overlay.set_card(tr("FIRST_SIGHT_KICKER"), tr("FIRST_SIGHT_TITLE") % [variant, manager.target_name(unit_id)],
		str(row.get("name", "")))
	overlay.skip_requested.connect(func() -> void:
		if overlay != null and overlay.card.visible:
			_finish(false)
		else:
			skip())
	overlay.continue_requested.connect(func() -> void: _finish(false))
	overlay.save_requested.connect(save_picture)


func _overlay_at(t: float) -> void:
	if overlay == null:
		return
	var fade_in := float(data.get("fade_in_seconds", 0.6))
	var restore := float(data.get("restore_fade_seconds", 0.45))
	var card: Array = _plan.get("end_card_seconds", [duration - 4.0, duration - 1.0])
	var black := 1.0 - smoothstep(0.0, fade_in, t)
	# The dip to black that hides the world being given back.
	black = maxf(black, smoothstep(duration - restore, duration, t))
	var card_alpha := smoothstep(float(card[0]), float(card[0]) + 0.8, t)
	var bars := smoothstep(0.0, 0.8, t) * (1.0 - smoothstep(duration - restore, duration, t))
	overlay.show_state(bars, black, card_alpha, 0.0, smoothstep(0.6, 1.4, t) * (1.0 - card_alpha))


# ── Reduced motion: a still and a cross-fade ─────────────────────────────────

func _reduced_duration() -> float:
	var rm: Dictionary = data.get("reduced_motion", {})
	return float(rm.get("crossfade_seconds", 2.0)) + float(rm.get("hold_seconds", 4.5)) + float(data.get("restore_fade_seconds", 0.45))


## The unit at its own framing in the sequence's light, still; the frame the
## player was on cross-fades out over it.
func _seek_still(t: float) -> void:
	_timelapse(1.0)
	manager.light_override = 1.0 if not (_plan.get("lights_on_seconds", []) as Array).is_empty() else -1.0
	var cam := manager.camera as FjordCamera
	if cam != null:
		cam.cinematic_pose(_final_eye, _final_look, _final_fov)
	var rm: Dictionary = data.get("reduced_motion", {})
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


# ── The card's actions and the repeat toast ──────────────────────────────────

## "Lagre bilde": the frame without the overlay, saved to user://screenshots.
func save_picture() -> void:
	if overlay == null:
		return
	overlay.visible = false
	for i in 2:
		await RenderingServer.frame_post_draw
	var path := ""
	var img := get_viewport().get_texture().get_image() if DisplayServer.get_name() != "headless" else null
	if img != null:
		DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(SCREENSHOT_DIR))
		path = "%s/first_sight_%s.png" % [SCREENSHOT_DIR, unit_id.validate_filename()]
		img.save_png(ProjectSettings.globalize_path(path))
	if overlay != null:
		overlay.visible = not _stepped_aside
	var toast := get_node_or_null("/root/ToastNotification")
	if toast != null and path != "":
		toast.call("show_toast", tr("FIRST_SIGHT_PICTURE_SAVED") % path.get_file(), "SUCCESS", 3.0)


## A repeat completion of a variant: a short toast that flies the camera there.
func _toast(id: String) -> void:
	if manager == null or not manager.views.has(id):
		return
	var row: Dictionary = manager.views[id]
	var text: String = tr("FIRST_SIGHT_REPEAT_TOAST") % [tr("FARM_VARIANT_%s" % str(row.get("variant", "")).to_upper()), manager.target_name(id)]
	toasts.append(text)
	var toast := get_node_or_null("/root/ToastNotification")
	if toast != null:
		var cam := manager.camera as FjordCamera
		toast.call("show_toast", text, "SUCCESS", float(data.get("toast_seconds", 3.0)),
			func() -> void:
				if cam != null:
					cam.focus_target(id))

