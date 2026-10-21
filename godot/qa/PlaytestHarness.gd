extends Node
## PlaytestHarness — the experience gate (docs/SHIP_PLAN.md §4).
##
## Boots the project's real main scene beside itself, plays a data-driven act
## list with real input at human pacing, captures frames on a cadence and on
## every window/dialog/toast that appears, and leaves crash-safe evidence
## (frames + timeline.jsonl + summary.json) for a judging agent.
##
## Run windowed from the repo root, ALWAYS through the wrapper (it isolates
## the Godot user dir so a session never touches the player's saves):
##   bash tools/playtest.sh res://qa/playtests/smoke.json /abs/evidence/dir \
##       [--turbo] [--max-min=30] [--w=1280 --h=720]
##
## Contract and edge cases:
## - The harness never passes or fails the game. It exits 0 with findings for
##   the judge; it exits 1 only when the harness itself cannot run.
## - Crash-safe: the timeline is append-flushed per line; a window close writes
##   the summary before quitting. A hard kill loses at most the summary.
## - Pause-safe: the harness runs PROCESS_MODE_ALWAYS and its timers ignore
##   both pause and Engine.time_scale, so a tutorial clock-hold or a 30x game
##   speed never warps the session pacing.
## - Render-safe: every capture awaits frame_post_draw; the first frames of a
##   fresh window are skipped (they render black). Headless runs still produce
##   a timeline but no frames, and say so once.
## - Layout-safe: controls are found by visible text, never by node path, so
##   UI refactors do not break act lists. A found-but-disabled control is a
##   finding (with its tooltip), not an error. Scroll containers between the
##   control and the root are scrolled so the click lands on screen.
## - Inputs are real events through Input.parse_input_event at 100% UI scale;
##   scaled-UI (125%) runs are judged from frames only.
## - Disk-safe: frames form a ring buffer; pinned (event) frames are never
##   evicted; everything is JPEG at most MAX_FRAME_W wide.

const CADENCE_SEC := 2.0          # idle frame cadence
const FRAME_BUDGET := 400         # unpinned frames beyond this are evicted
const MAX_FRAME_W := 1280
const DEFAULT_TIMEOUT_SEC := 20.0 # per act unless the act sets its own
const INPUT_HOLD_MS := 40         # press -> release gap
const PLAYTEST_USER_DIR := "LakskongenPlaytest"  # see tools/playtest.sh
const BLANK_SPREAD := 0.02        # luminance spread under which a frame is blank
const BLANK_STREAK_FINDING := 3   # blank frames in a row that make one finding

var _out_dir := ""
var _acts: Array = []
var _acts_name := "unnamed"
var _turbo := false
var _timeline: FileAccess = null
var _frames: Array[Dictionary] = []   # {path: String, pinned: bool}
var _frame_seq := 0
var _findings: Array[Dictionary] = []
var _finished := false
var _capturing := false
var _idle_accum := 0.0
var _hard_stop_ms := 0
var _headless_noted := false
var _blank_streak := 0


func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS
	var args := _parse_args(OS.get_cmdline_user_args())
	# Never play inside the player's own user dir: the game writes saves,
	# settings, language and campaign progress there. tools/playtest.sh sets
	# up the isolated dir; running the scene any other way is refused.
	if not OS.get_user_data_dir().ends_with(PLAYTEST_USER_DIR) and not args.has("allow-shared-userdir"):
		push_error("PlaytestHarness: refusing to run in %s; use tools/playtest.sh" % OS.get_user_data_dir())
		get_tree().quit(1)
		return
	_turbo = args.has("turbo")
	_hard_stop_ms = Time.get_ticks_msec() + int(float(args.get("max-min", "30")) * 60_000.0)
	if args.has("w") and args.has("h"):
		DisplayServer.window_set_size(Vector2i(int(args["w"]), int(args["h"])))
	_out_dir = String(args.get("out", ""))
	if _out_dir.begins_with("res://") or _out_dir.begins_with("user://"):
		_out_dir = ProjectSettings.globalize_path(_out_dir)
	if _out_dir.is_empty() or not _out_dir.is_absolute_path():
		_out_dir = OS.get_user_data_dir() + "/playtest/" \
				+ Time.get_datetime_string_from_system(true).replace(":", "-")
	if DirAccess.make_dir_recursive_absolute(_out_dir) != OK:
		push_error("PlaytestHarness: cannot create out dir " + _out_dir)
		get_tree().quit(1)
		return
	_timeline = FileAccess.open(_out_dir + "/timeline.jsonl", FileAccess.WRITE)
	if _timeline == null:
		push_error("PlaytestHarness: cannot open timeline in " + _out_dir)
		get_tree().quit(1)
		return
	_log("session", {"turbo": _turbo, "driver": DisplayServer.get_name(),
			"window": DisplayServer.window_get_size()})
	if not _load_acts(String(args.get("acts", "res://qa/playtests/smoke.json"))):
		_finish("harness_error", 1)
		return
	get_tree().node_added.connect(_on_node_added)
	if not await _boot_game():
		_finish("harness_error", 1)
		return
	await _session()


func _session() -> void:
	for i in 3:                       # a fresh window's first frames are black
		await RenderingServer.frame_post_draw
	get_window().grab_focus()
	await _capture("boot", true)
	for i in _acts.size():
		if _finished:
			return
		await _run_act(_acts[i], i)
	await _capture("end", true)
	_finish("completed", 0)


func _process(delta: float) -> void:
	if _finished:
		return
	if Time.get_ticks_msec() > _hard_stop_ms:
		_finding("harness_error", "session hard cap reached", {})
		_finish("hard_cap", 0)
		return
	_idle_accum += delta
	if _idle_accum >= CADENCE_SEC:
		_idle_accum = 0.0
		_capture("idle", false)     # fire and forget; re-entry guarded


func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_CLOSE_REQUEST:
		_finish("window_closed", 0)


# --- boot ------------------------------------------------------------------

func _boot_game() -> Variant:
	var main_path := String(ProjectSettings.get_setting("application/run/main_scene", ""))
	if main_path.is_empty():
		_finding("harness_error", "project has no main scene setting", {})
		return false
	var packed := load(main_path)
	if packed == null or not (packed is PackedScene):
		_finding("harness_error", "cannot load main scene " + main_path, {})
		return false
	var game: Node = (packed as PackedScene).instantiate()
	game.name = "GameUnderTest"
	# The root is still setting up its children while this scene's _ready runs,
	# so a direct add_child is refused (the first real session played an empty
	# grey window and reported a clean run). Defer it, then VERIFY it landed.
	get_tree().root.add_child.call_deferred(game)
	for i in 30:
		await get_tree().process_frame
		if game.is_inside_tree():
			break
	if not game.is_inside_tree():
		_finding("harness_error", "main scene never entered the tree: " + main_path, {})
		game.free()
		return false
	# Much project code assumes it is the current scene (reload, change_scene).
	get_tree().current_scene = game
	_log("boot", {"main_scene": main_path, "children": game.get_child_count()})
	return true


# --- acts ------------------------------------------------------------------

func _run_act(act: Dictionary, idx: int) -> void:
	_log("act", {"i": idx, "act": act})
	match String(act.get("do", "")):
		"wait":
			await _sleep(float(act.get("sec", 1.0)))
		"frame":
			await _capture(_safe(String(act.get("note", "act%d" % idx))), true)
		"key":
			await _press_key(String(act.get("key", "Space")), int(act.get("times", 1)))
		"click_text":
			await _click_text(act)
		"wait_text":
			await _wait_text(act)
		"wait_dialog":
			await _wait_dialog(act)
		_:
			_finding("harness_error", "unknown act '%s'" % String(act.get("do", "")), {"i": idx})


func _press_key(key_name: String, times: int) -> void:
	var code := OS.find_keycode_from_string(key_name)
	if code == KEY_NONE:
		_finding("harness_error", "unknown key '%s'" % key_name, {})
		return
	for i in maxi(1, times):
		get_window().grab_focus()
		var down := InputEventKey.new()
		down.keycode = code
		down.physical_keycode = code
		down.pressed = true
		Input.parse_input_event(down)
		await _sleep_ms(INPUT_HOLD_MS)
		var up := InputEventKey.new()
		up.keycode = code
		up.physical_keycode = code
		up.pressed = false
		Input.parse_input_event(up)
		await _sleep_ms(120)
	await _capture("key_" + _safe(key_name), true)


func _click_text(act: Dictionary) -> void:
	var wanted := String(act.get("text", ""))
	var optional := bool(act.get("optional", false))
	var deadline := Time.get_ticks_msec() \
			+ int(float(act.get("timeout_sec", DEFAULT_TIMEOUT_SEC)) * 1000.0)
	while not _finished:
		var hit := _find_clickable_by_text(wanted)
		if hit != null:
			if hit is BaseButton and (hit as BaseButton).disabled:
				_finding("ui_disabled", "'%s' is visible but disabled" % wanted,
						{"path": String(hit.get_path()), "tooltip": hit.tooltip_text})
				await _capture("disabled_" + _safe(wanted), true)
				return
			await _click_control(hit, wanted)
			return
		if Time.get_ticks_msec() > deadline:
			if optional:
				_log("skip", {"text": wanted, "why": "not found (optional act)"})
			else:
				_finding("ui_not_found", "no visible control with text '%s'" % wanted, {})
				await _capture("notfound_" + _safe(wanted), true)
			return
		await _sleep(0.3)


func _click_control(c: Control, label: String) -> void:
	_ensure_on_screen(c)
	await RenderingServer.frame_post_draw
	if not is_instance_valid(c) or not c.is_visible_in_tree():
		_finding("ui_vanished", "'%s' disappeared before the click landed" % label, {})
		return
	get_window().grab_focus()
	# Injected events arrive in WINDOW pixels and the window maps them through
	# the content stretch; control rects are in viewport units. The first real
	# session (1920x1080 base in a 1280x720 window) clicked 1.5x too far down
	# and hit the wrong menu button. Map viewport -> window first.
	var center := get_viewport().get_final_transform() * c.get_global_rect().get_center()
	# Prove the press landed: a button that does not emit `pressed` was
	# covered, moved, or the mapping is wrong — a finding, never silent.
	var landed := [false]
	var on_press := func() -> void: landed[0] = true
	var is_button := c is BaseButton   # read now: the press may free `c`
	if is_button:
		(c as BaseButton).pressed.connect(on_press, CONNECT_ONE_SHOT)
	var down := InputEventMouseButton.new()
	down.button_index = MOUSE_BUTTON_LEFT
	down.button_mask = MOUSE_BUTTON_MASK_LEFT
	down.pressed = true
	down.position = center
	down.global_position = center
	Input.parse_input_event(down)
	await _sleep_ms(INPUT_HOLD_MS)
	var up := InputEventMouseButton.new()
	up.button_index = MOUSE_BUTTON_LEFT
	up.pressed = false
	up.position = center
	up.global_position = center
	Input.parse_input_event(up)
	_log("click", {"text": label, "path": String(c.get_path()), "at": center})
	await _sleep(0.4)                 # let the UI react before judging
	if is_button:
		# A press that opened another scene may have freed the button; the
		# flag was set before that, so it still counts as landed.
		if not landed[0]:
			_finding("click_missed", "'%s' did not receive the press at window %s" % [label, str(center)], {})
		if is_instance_valid(c) and (c as BaseButton).pressed.is_connected(on_press):
			(c as BaseButton).pressed.disconnect(on_press)
	await _capture("click_" + _safe(label), true)


func _wait_text(act: Dictionary) -> void:
	var wanted := String(act.get("text", ""))
	var deadline := Time.get_ticks_msec() \
			+ int(float(act.get("timeout_sec", DEFAULT_TIMEOUT_SEC)) * 1000.0)
	while not _finished:
		if _text_is_visible(wanted):
			_log("saw_text", {"text": wanted})
			await _capture("saw_" + _safe(wanted), true)
			return
		if Time.get_ticks_msec() > deadline:
			_finding("expectation_failed", "text '%s' never became visible" % wanted, {})
			await _capture("missing_" + _safe(wanted), true)
			return
		await _sleep(0.3)


func _wait_dialog(act: Dictionary) -> void:
	var deadline := Time.get_ticks_msec() \
			+ int(float(act.get("timeout_sec", DEFAULT_TIMEOUT_SEC)) * 1000.0)
	while not _finished:
		var w := _top_foreign_window()
		if w != null:
			_log("dialog", {"name": String(w.name), "title": w.title})
			await _capture("dialog_" + _safe(String(w.name)), true)
			return
		if Time.get_ticks_msec() > deadline:
			_finding("expectation_failed", "no dialog appeared within the timeout", {})
			await _capture("nodialog", true)
			return
		await _sleep(0.3)


# --- discovery (never by node path: survives UI refactors) ------------------

func _find_clickable_by_text(wanted: String) -> Control:
	var want := wanted.strip_edges().to_lower()
	if want.is_empty():
		return null
	var exact: Control = null
	var partial: Control = null
	var stack: Array[Node] = [get_tree().root]
	while not stack.is_empty():
		var n: Node = stack.pop_back()
		if n == self:
			continue
		for c in n.get_children(true):
			stack.push_back(c)
		if n is BaseButton and (n as Control).is_visible_in_tree():
			var tv: Variant = n.get("text")
			var t: String = tv if tv is String else ""
			t = t.strip_edges().to_lower()
			if t.is_empty():
				t = String(n.name).to_lower()   # TextureButton and friends
			if t == want:
				exact = n
			elif want in t and partial == null:
				partial = n
	return exact if exact != null else partial


func _text_is_visible(wanted: String) -> bool:
	var want := wanted.strip_edges().to_lower()
	var stack: Array[Node] = [get_tree().root]
	while not stack.is_empty():
		var n: Node = stack.pop_back()
		for c in n.get_children(true):
			stack.push_back(c)
		if n is Control and (n as Control).is_visible_in_tree():
			if n is Label and want in (n as Label).text.to_lower():
				return true
			if n is RichTextLabel and want in (n as RichTextLabel).get_parsed_text().to_lower():
				return true
	return false


func _top_foreign_window() -> Window:
	var best: Window = null
	var stack: Array[Node] = [get_tree().root]
	while not stack.is_empty():
		var n: Node = stack.pop_back()
		for c in n.get_children(true):
			stack.push_back(c)
		if n is Window and n != get_window() and (n as Window).visible:
			best = n
	return best


func _ensure_on_screen(c: Control) -> void:
	var p := c.get_parent()
	while p != null:
		if p is ScrollContainer:
			(p as ScrollContainer).ensure_control_visible(c)
		p = p.get_parent()


func _on_node_added(n: Node) -> void:
	if _finished:
		return
	# Dialogs flip visible after being added; tabs re-add nodes, so guard the hook.
	if n is Window and n != get_window():
		if not n.visibility_changed.is_connected(_on_foreign_window_changed):
			n.visibility_changed.connect(_on_foreign_window_changed)
	elif n is Control and "toast" in String(n.name).to_lower():
		_capture_soon("toast_" + _safe(String(n.name)))


func _on_foreign_window_changed() -> void:
	var w := _top_foreign_window()
	if w != null:
		_log("window", {"name": String(w.name), "title": w.title})
		_capture_soon("window_" + _safe(String(w.name)))


func _capture_soon(tag: String) -> void:
	await _sleep_ms(150)              # let the toast/dialog finish appearing
	await _capture(tag, true)


# --- evidence ---------------------------------------------------------------

func _capture(tag: String, pinned: bool) -> String:
	if _capturing or _finished:
		return ""
	_capturing = true
	await RenderingServer.frame_post_draw
	var img: Image = get_viewport().get_texture().get_image()
	_capturing = false
	if img == null or img.is_empty():
		if not _headless_noted:
			_headless_noted = true
			_finding("headless_no_frames", "no frames available (headless or hidden window)", {})
		return ""
	_note_blankness(img, tag)
	if img.get_width() > MAX_FRAME_W:
		var s := float(MAX_FRAME_W) / float(img.get_width())
		img.resize(MAX_FRAME_W, int(round(img.get_height() * s)), Image.INTERPOLATE_LANCZOS)
	_frame_seq += 1
	var path := "%s/f%04d_%s.jpg" % [_out_dir, _frame_seq, _safe(tag)]
	if img.save_jpg(path, 0.85) != OK:
		_log("frame_failed", {"tag": tag})
		return ""
	_frames.append({"path": path, "pinned": pinned})
	_evict_frames()
	_log("frame", {"tag": tag, "file": path.get_file(), "pinned": pinned})
	return path


## A frame whose 12x8 luminance samples span less than BLANK_SPREAD is blank
## (a clear colour, a stuck loading screen, a scene that never booted). Three
## blank frames in a row are one finding per streak, so an empty window can
## never pass for a clean session.
func _note_blankness(img: Image, tag: String) -> void:
	var lo := 1.0
	var hi := 0.0
	var w := img.get_width()
	var h := img.get_height()
	for gy in 8:
		for gx in 12:
			var c := img.get_pixel(int((gx + 0.5) * w / 12.0), int((gy + 0.5) * h / 8.0))
			var lum := c.get_luminance()
			lo = minf(lo, lum)
			hi = maxf(hi, lum)
	if hi - lo < BLANK_SPREAD:
		_blank_streak += 1
		if _blank_streak == BLANK_STREAK_FINDING:
			_finding("blank_screen", "%d frames in a row show a uniform screen (spread %.3f) up to '%s'"
					% [BLANK_STREAK_FINDING, hi - lo, tag], {})
	else:
		_blank_streak = 0


func _evict_frames() -> void:
	var over := _frames.size() - FRAME_BUDGET
	var i := 0
	while over > 0 and i < _frames.size():
		if not bool(_frames[i]["pinned"]):
			DirAccess.remove_absolute(String(_frames[i]["path"]))
			_frames.remove_at(i)
			over -= 1
		else:
			i += 1


func _finding(kind: String, msg: String, extra: Dictionary) -> void:
	var f := {"kind": kind, "msg": msg, "t_ms": Time.get_ticks_msec()}
	f.merge(extra)
	_findings.append(f)
	_log("finding", f)


func _log(type: String, data: Dictionary) -> void:
	if _timeline == null:
		return
	_timeline.store_line(JSON.stringify({"t_ms": Time.get_ticks_msec(), "type": type, "data": data}))
	_timeline.flush()                 # crash-safe: at most the summary is lost


func _finish(reason: String, code: int) -> void:
	if _finished:
		return
	_finished = true
	var summary := {
		"acts_file": _acts_name,
		"reason": reason,
		"duration_ms": Time.get_ticks_msec(),
		"frames": _frames.size(),
		"findings": _findings,
		"out_dir": _out_dir,
	}
	var fa := FileAccess.open(_out_dir + "/summary.json", FileAccess.WRITE)
	if fa != null:
		fa.store_string(JSON.stringify(summary, "  "))
		fa.flush()
	if _timeline != null:
		_timeline.flush()
	print("PLAYTEST_RESULT reason=%s findings=%d frames=%d dir=%s"
			% [reason, _findings.size(), _frames.size(), _out_dir])
	get_tree().quit(code)


# --- plumbing ---------------------------------------------------------------

func _load_acts(path: String) -> bool:
	var text := FileAccess.get_file_as_string(path)
	if text.is_empty():
		_finding("harness_error", "acts file unreadable or empty: " + path,
				{"err": FileAccess.get_open_error()})
		return false
	var data: Variant = JSON.parse_string(text)
	if not (data is Dictionary):
		_finding("harness_error", "acts file is not a JSON object: " + path, {})
		return false
	var acts_v: Variant = (data as Dictionary).get("acts")
	if not (acts_v is Array):
		_finding("harness_error", "acts file has no 'acts' array: " + path, {})
		return false
	for i in (acts_v as Array).size():
		var a: Variant = (acts_v as Array)[i]
		if not (a is Dictionary) or not (a as Dictionary).has("do"):
			_finding("harness_error", "act %d is not an object with 'do'" % i, {})
			return false
	_acts = acts_v
	_acts_name = String((data as Dictionary).get("name", path.get_file()))
	_log("acts", {"name": _acts_name, "count": _acts.size()})
	return true


func _parse_args(raw: PackedStringArray) -> Dictionary:
	var out := {}
	for a in raw:
		if not a.begins_with("--"):
			continue
		var body := a.substr(2)
		var eq := body.find("=")
		if eq < 0:
			out[body] = "true"
		else:
			out[body.left(eq)] = body.substr(eq + 1)
	return out


func _sleep(sec: float) -> void:
	# ignore_time_scale + process_always: 30x game speed or a paused tree
	# never warps the session pacing.
	var s := maxf(0.05, sec * (0.25 if _turbo else 1.0))
	await get_tree().create_timer(s, true, false, true).timeout


func _sleep_ms(ms: int) -> void:
	await get_tree().create_timer(float(ms) / 1000.0, true, false, true).timeout


func _safe(tag: String) -> String:
	var s := tag.to_lower()
	for ch in [" ", "/", "\\", ":", "'", "\"", "\n", "\t"]:
		s = s.replace(ch, "_")
	return s.substr(0, 40)
