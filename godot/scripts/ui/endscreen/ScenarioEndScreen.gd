extends CanvasLayer

## ScenarioEndScreen — how the game ended and why (roadmap U2-03).
##
## When a verdict lands (SimBridge.scenario_ended: "won", "lost" or
## "bankrupt") the game steps aside and this layer (CanvasLayer 50, above the
## game UI, its cards and the pause menu) shows an Ink-glass column with the
## verdict, the medal and its three distinctions and the way on, beside a
## Chart-paper report: the key figures, the P&L story with its three turning
## points, where the fish went and the event log (EndScreenReport).
## Successful sims close a scenario the same way (Two Point's level results,
## RollerCoaster Tycoon's scenario tiers, Football Manager's season review):
## a verdict, a reason and the season told back in a few moments.
##
## While it is open it is the only thing that takes input: every key goes to
## it (Tab and the arrows move between its buttons, Enter presses one; the
## game's keys, Esc and the camera wait), its full-screen stop takes every
## click the buttons do not, and the camera's keys are off. A won game's
## clock is held until "Fortsett å spille". A won or lost campaign game
## records its result and medal in CampaignProgress (user://campaign.json).

signal end_screen_shown(verdict: String)
## "continue" (the only way back into the same game).
signal end_screen_closed(choice: String)

const GROUP := &"scenario_end_screen"
const CLOCK_HOLD := "end_screen"
const GAME_SCENE := "res://scenes/GameScene.tscn"
const MAIN_MENU_SCENE := "res://scenes/MainMenu.tscn"
const AUTOSAVE_PREFIX := "autosave"
const VERDICTS := ["won", "lost", "bankrupt"]
const FADE_IN_S := 0.4
const SLIDE_PX := 40.0
const REVEAL_S := 1.2
const BUTTON_H := 46.0
const PRIMARY_H := 54.0
## Layers of the game scene that step aside while the screen is open (and the
## toast layer above the game).
const ASIDE_NODES := ["UILayer", "EventLayer", "AttentionInbox", "/root/ToastNotification"]

@onready var _screen: Control = %Screen
@onready var _column: Control = %Column
@onready var _overline: Label = %Overline
@onready var _title: Label = %VerdictTitle
@onready var _sub: Label = %VerdictSub
@onready var _medal_badge: MedalBadge = %MedalBadge
@onready var _medal_title: Label = %MedalTitle
@onready var _medal_sub: Label = %MedalSub
@onready var _distinctions: VBoxContainer = %Distinctions
@onready var _campaign_note: Control = %CampaignNote
@onready var _campaign_text: Label = %CampaignText
@onready var _primary_slot: VBoxContainer = %PrimarySlot
@onready var _grid: GridContainer = %ButtonGrid
@onready var _error: Label = %ErrorLabel
@onready var _sheet: PanelContainer = %Sheet
@onready var _report_box: VBoxContainer = %Report

var continue_button: Button
var autosave_button: Button
var retry_button: Button
var next_button: Button
var menu_button: Button

## Tests replace the scene change (they host the game scene themselves).
var scene_change_override: Callable = Callable()

var _sim: Node = null
var _open: bool = false
var _verdict: String = ""
var _report: Dictionary = {}
var _campaign: Dictionary = {}
var _recorded_key: String = ""
var _next_id: String = ""
var _autosave_file: String = ""
var _built: Dictionary = {}
var _hidden: Array[Node] = []
var _camera_input: Dictionary = {}
var _tween: Tween = null
## Times the screen was shown (tests).
var shown_count: int = 0


func _ready() -> void:
	add_to_group(GROUP)
	_sim = get_node_or_null("/root/SimBridge")
	LakskongenTheme.apply_chart(_sheet)
	_build_buttons()
	_screen.mouse_filter = Control.MOUSE_FILTER_STOP
	visible = false
	if _sim != null:
		_sim.scenario_ended.connect(_on_scenario_ended)
	var bridge := get_node_or_null("/root/GameBridge")
	if bridge != null:
		bridge.GameStarted.connect(_on_game_started)


func is_open() -> bool:
	return _open


func verdict() -> String:
	return _verdict


func report() -> Dictionary:
	return _report


## The report sheet's nodes (EndScreenReport.build): chart, points, kpis, rows.
func built() -> Dictionary:
	return _built


## The campaign's answer to this result (CampaignProgress.record_result).
func campaign_outcome() -> Dictionary:
	return _campaign


## Buttons in focus order (the primary first), enabled or not.
func buttons() -> Array[Button]:
	var out: Array[Button] = []
	for b in _primary_slot.get_children() + _grid.get_children():
		if b is Button:
			out.append(b)
	return out


## Show the screen for `verdict_key` ("won", "lost", "bankrupt").
func present(verdict_key: String) -> void:
	if _sim == null or not VERDICTS.has(verdict_key):
		return
	_report = _sim.get_scenario_end_report()
	var reported := str(_report.get("verdict", verdict_key))
	_verdict = reported if VERDICTS.has(reported) else verdict_key
	_record_campaign()
	_fill_column()
	_built = EndScreenReport.build(_report_box, _report, _sim.get_mortality_breakdown())
	_arrange_buttons()
	if not _open:
		_open = true
		shown_count += 1
		var toasts := get_node_or_null("/root/ToastNotification")
		if toasts != null and toasts.has_method("clear_all"):
			toasts.clear_all()
		_step_aside(true)
		visible = true
		_animate_in()
		_play_cue()
	_focus_primary.call_deferred()
	end_screen_shown.emit(_verdict)


# ── Signals ────────────────────────────────────────────────────────────────────

func _on_scenario_ended(verdict_key: String) -> void:
	present(verdict_key)


## A new or loaded game: a screen left from the game before closes.
func _on_game_started() -> void:
	if _open:
		_close()


# ── Input: only this screen takes it while open ────────────────────────────────

func _input(event: InputEvent) -> void:
	if not _open:
		return
	if event is InputEventMouse or event is InputEventScreenTouch or event is InputEventScreenDrag \
			or event is InputEventGesture:
		return  # the GUI: the buttons, the report's scrolling, else the screen's stop
	if event.is_pressed() and not event.is_echo():
		_handle_key(event)
	get_viewport().set_input_as_handled()


func _handle_key(event: InputEvent) -> void:
	if event.is_action("ui_focus_next") or event.is_action("ui_down") or event.is_action("ui_right"):
		_move_focus(1)
	elif event.is_action("ui_focus_prev") or event.is_action("ui_up") or event.is_action("ui_left"):
		_move_focus(-1)
	elif event.is_action("ui_accept"):
		var focused := get_viewport().gui_get_focus_owner()
		if focused is Button and buttons().has(focused) and not (focused as Button).disabled:
			(focused as Button).pressed.emit()


func _move_focus(step: int) -> void:
	var usable := buttons().filter(func(b: Button) -> bool: return b.visible and not b.disabled)
	if usable.is_empty():
		return
	var at := usable.find(get_viewport().gui_get_focus_owner())
	var next := posmod(at + step, usable.size()) if at >= 0 else 0
	(usable[next] as Button).grab_focus()


func _focus_primary() -> void:
	for b in buttons():
		if b.visible and not b.disabled and is_inside_tree():
			b.grab_focus()
			return


# ── The column ─────────────────────────────────────────────────────────────────

func _fill_column() -> void:
	var name_text := str(_report.get("scenario_name", ""))
	_overline.text = name_text if name_text != "" else tr("UI_ENDSCREEN_FREE_GAME")
	_title.text = tr("UI_ENDSCREEN_VERDICT_" + _verdict.to_upper())
	_title.theme_type_variation = &"DisplayOk" if _verdict == "won" else &"DisplayCrit"
	_sub.text = _verdict_text()
	var marks: Array = _report.get("distinctions", [])
	var met := marks.filter(func(d: Dictionary) -> bool: return bool(d.get("met", false))).size()
	var medal := str(_report.get("medal", "none"))
	_medal_badge.medal = medal if _verdict == "won" else "none"
	_medal_badge.stars = met if _verdict == "won" else 0
	_medal_title.text = tr("UI_ENDSCREEN_MEDAL_" + _medal_badge.medal.to_upper())
	_medal_sub.text = _medal_text(met)
	_fill_distinctions(marks)
	_fill_campaign_note()
	_error.hide()


func _verdict_text() -> String:
	var days := SimFormatters.integer(int(_report.get("days_played", 0)))
	match _verdict:
		"won":
			return tr("UI_ENDSCREEN_SUB_WON") % [days, _goal_text()]
		"bankrupt":
			var text := tr("UI_ENDSCREEN_SUB_BANKRUPT") % [SimFormatters.format_nok(float(_report.get("bankruptcy_floor", 0.0))),
				SimFormatters.integer(int(_report.get("end_day", 0)))]
			var ladder: Dictionary = _sim.get_liquidity_status()
			var warned := int(ladder.get("warning_day", -1))
			var called := int(ladder.get("bank_call_day", -1))
			if warned >= 0 and called >= 0:
				text += " " + tr("UI_ENDSCREEN_SUB_BANK_LADDER") % [SimFormatters.integer(warned), SimFormatters.integer(called)]
			return text
		_:
			var reason := str(_report.get("loss_reason", ""))
			return (reason + " " if reason != "" else "") + tr("UI_ENDSCREEN_SUB_LOST") % days


func _goal_text() -> String:
	var id := str(_report.get("scenario_id", ""))
	for entry in _sim.get_available_scenarios():
		if str(entry.get("id", "")) == id:
			return str(entry.get("win", ""))
	return ""


func _medal_text(met: int) -> String:
	if _verdict != "won":
		return tr("UI_ENDSCREEN_MEDAL_NONE_SUB")
	var text := tr("UI_ENDSCREEN_MEDAL_SUB") % met
	var best := str(_campaign.get("best_medal", "none"))
	if bool(_campaign.get("improved", false)):
		text += " · " + tr("UI_ENDSCREEN_MEDAL_NEW_BEST")
	elif CampaignProgress.MEDALS.find(best) > CampaignProgress.MEDALS.find(str(_report.get("medal", "none"))):
		text += " · " + tr("UI_ENDSCREEN_MEDAL_BEST") % tr("UI_SCENARIO_MEDAL_" + best.to_upper())
	return text


func _fill_distinctions(marks: Array) -> void:
	for child in _distinctions.get_children():
		child.queue_free()
	for d in marks:
		var id := str(d.get("id", ""))
		var met := bool(d.get("met", false))
		var row := HBoxContainer.new()
		row.name = "Mark_" + id
		row.add_theme_constant_override("separation", 10)
		var text := VBoxContainer.new()
		text.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		text.add_theme_constant_override("separation", 0)
		var title := Label.new()
		title.name = "Title"
		title.text = tr("UI_ENDSCREEN_MARK_" + id.to_upper())
		title.theme_type_variation = &"BodyStrong"
		text.add_child(title)
		var detail := Label.new()
		detail.name = "Detail"
		detail.text = _mark_detail(id, met, d)
		detail.theme_type_variation = &"BodySmallMuted"
		detail.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		detail.custom_minimum_size = Vector2(220, 0)
		text.add_child(detail)
		row.add_child(text)
		var tag := Label.new()
		tag.name = "Tag"
		tag.text = tr("UI_ENDSCREEN_MARK_MET") if met else tr("UI_ENDSCREEN_MARK_MISSED")
		tag.theme_type_variation = &"TagOk" if met else &"Tag"
		tag.size_flags_vertical = Control.SIZE_SHRINK_BEGIN
		row.add_child(tag)
		_distinctions.add_child(row)


func _mark_detail(id: String, met: bool, d: Dictionary) -> String:
	match id:
		"finance":
			if met:
				return tr("UI_ENDSCREEN_MARK_FINANCE_MET")
			return tr("UI_ENDSCREEN_MARK_FINANCE_MISSED") % [int(_report.get("bank_calls", 0)), int(_report.get("rescue_loans", 0))]
		"welfare":
			return tr("UI_ENDSCREEN_MARK_WELFARE_DETAIL") % [SimFormatters.percent(float(d.get("value", 0.0)), 1),
				SimFormatters.percent(float(d.get("threshold", 0.0)), 0)]
		"compliance":
			return tr("UI_ENDSCREEN_MARK_COMPLIANCE_MET") if met \
				else tr("UI_ENDSCREEN_MARK_COMPLIANCE_MISSED") % SimFormatters.format_nok(float(d.get("value", 0.0)))
	return ""


## A campaign game: how far the campaign has come, and what this result opened.
func _fill_campaign_note() -> void:
	var id := str(_report.get("scenario_id", ""))
	_campaign_note.visible = CampaignProgress.is_campaign_scenario(id)
	if not _campaign_note.visible:
		return
	var s := CampaignProgress.summary()
	var text := tr("UI_ENDSCREEN_CAMPAIGN_PROGRESS") % [int(s["won"]), int(s["total"]), int(s["open"])]
	var opened: PackedStringArray = _campaign.get("newly_unlocked", PackedStringArray())
	if not opened.is_empty():
		var names := PackedStringArray()
		for o in opened:
			names.append(_scenario_name(o))
		text = tr("UI_ENDSCREEN_CAMPAIGN_UNLOCKED") % ", ".join(names) + "\n" + text
	_campaign_text.text = text


func _scenario_name(id: String) -> String:
	for entry in _sim.get_available_scenarios():
		if str(entry.get("id", "")) == id:
			return str(entry.get("name", id))
	return id


# ── Campaign ───────────────────────────────────────────────────────────────────

## Each verdict of a game is recorded once: the campaign skips an end it has
## already recorded (the same key), so a screen shown again for the same end
## (a game scene opened on an ended game) records nothing new.
func _record_campaign() -> void:
	var id := str(_report.get("scenario_id", ""))
	var key := "%s:%s:%d:%d" % [id, _verdict, int(_report.get("start_day", 0)), int(_report.get("end_day", 0))]
	if key != _recorded_key:
		_recorded_key = key
		_campaign = CampaignProgress.record_result(id, _verdict, str(_report.get("medal", "none")),
			float(_report.get("net_result", 0.0)), key)
	_next_id = CampaignProgress.next_scenario(id) if CampaignProgress.is_campaign_scenario(id) else ""


# ── Buttons ────────────────────────────────────────────────────────────────────

func _build_buttons() -> void:
	continue_button = _button("ContinueButton", _on_continue)
	autosave_button = _button("AutosaveButton", _on_load_autosave)
	retry_button = _button("RetryButton", _on_retry)
	next_button = _button("NextButton", _on_next)
	menu_button = _button("MenuButton", _on_main_menu)


func _button(node_name: String, action: Callable) -> Button:
	var b := Button.new()
	b.name = node_name
	b.focus_mode = Control.FOCUS_ALL
	b.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	b.pressed.connect(action)
	UIPolish.attach_press_feedback(b)
	_grid.add_child(b)
	return b


## The primary action on top (won: the next scenario, else play on; lost or
## bankrupt: try again), the four others in a fixed grid below it. A button
## that cannot act is disabled and says why.
func _arrange_buttons() -> void:
	var won := _verdict == "won"
	_autosave_file = _find_autosave()
	continue_button.text = tr("UI_ENDSCREEN_CONTINUE")
	continue_button.disabled = not won
	continue_button.tooltip_text = tr("UI_ENDSCREEN_CONTINUE_TIP") if won else tr("UI_ENDSCREEN_CONTINUE_OFF")
	autosave_button.text = tr("UI_ENDSCREEN_AUTOSAVE")
	autosave_button.disabled = _autosave_file == ""
	autosave_button.tooltip_text = tr("UI_ENDSCREEN_AUTOSAVE_TIP") if _autosave_file != "" else tr("UI_ENDSCREEN_AUTOSAVE_OFF")
	retry_button.text = tr("UI_ENDSCREEN_RETRY")
	retry_button.tooltip_text = tr("UI_ENDSCREEN_RETRY_TIP")
	next_button.disabled = _next_id == ""
	next_button.tooltip_text = tr("UI_ENDSCREEN_NEXT_TIP") % _scenario_name(_next_id) if _next_id != "" else tr("UI_ENDSCREEN_NEXT_OFF")
	menu_button.text = tr("UI_ENDSCREEN_MENU")
	menu_button.tooltip_text = tr("UI_ENDSCREEN_MENU_TIP")

	var primary: Button = (next_button if _next_id != "" else continue_button) if won else retry_button
	var order: Array[Button] = [continue_button, autosave_button, retry_button, next_button, menu_button]
	for b in order:
		if b.get_parent() != null:
			b.get_parent().remove_child(b)
	_primary_slot.add_child(primary)
	# The next scenario's name fits on the primary button; in the grid it is
	# in the tooltip.
	next_button.text = tr("UI_ENDSCREEN_NEXT") % _scenario_name(_next_id) if primary == next_button \
		else tr("UI_ENDSCREEN_NEXT_NONE")
	primary.theme_type_variation = &"LargePrimaryButton"
	primary.custom_minimum_size = Vector2(0, PRIMARY_H)
	for b in order:
		if b == primary:
			continue
		_grid.add_child(b)
		b.theme_type_variation = &"GhostButton"
		b.custom_minimum_size = Vector2(0, BUTTON_H)
		b.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS


## The newest autosave from before the end (a lost or bankrupt game's later
## autosave would load the same end); "" when there is none.
func _find_autosave() -> String:
	var end_day := int(_report.get("end_day", 0))
	for entry in _sim.get_save_list():
		if not str(entry.get("name", "")).begins_with(AUTOSAVE_PREFIX):
			continue
		if _verdict == "won" or int(entry.get("day", 0)) < end_day:
			return str(entry.get("file", ""))
	return ""


func _on_continue() -> void:
	if _verdict != "won":
		return
	_close()
	end_screen_closed.emit("continue")


func _on_load_autosave() -> void:
	if _autosave_file == "" or not _sim.load_game(_autosave_file):
		_error.text = tr("UI_ENDSCREEN_LOAD_FAILED") % _sim.get_last_save_error()
		_error.show()
		return
	_leave(GAME_SCENE)


func _on_retry() -> void:
	_start(str(_report.get("scenario_id", "")))


func _on_next() -> void:
	if _next_id != "":
		_start(_next_id)


func _start(scenario_id: String) -> void:
	var ok: bool = _sim.start_new_game("Player", 10_000_000, scenario_id) if scenario_id != "" \
		else _sim.start_new_game("Player")
	if not ok:
		_error.text = tr("UI_ENDSCREEN_START_FAILED")
		_error.show()
		return
	_leave(GAME_SCENE)


func _on_main_menu() -> void:
	# The ended game must not tick (or autosave) behind the menu.
	_sim.set_paused(true)
	_leave(MAIN_MENU_SCENE)


func _leave(scene_path: String) -> void:
	_close()
	if scene_change_override.is_valid():
		scene_change_override.call(scene_path)
	else:
		get_tree().change_scene_to_file(scene_path)


func _close() -> void:
	_open = false
	visible = false
	_step_aside(false)


## A game scene freed while the screen is open (a scene change that is not the
## screen's own, a test's scene) gives back what the screen took: the toast
## layer is an autoload that outlives the scene, and the clock hold is the
## bridge's. Without this the next game ran with hidden toasts and a held clock.
func _exit_tree() -> void:
	if _open:
		_close()


# ── The game steps aside ───────────────────────────────────────────────────────

## The HUD, tabs, cards and inbox hide and the camera's keys stop while the
## screen is open; a won game's clock is held. They come back when it closes.
func _step_aside(aside: bool) -> void:
	var scene := get_parent()
	if aside:
		_hidden.clear()
		for path in ASIDE_NODES:
			var node: Node = scene.get_node_or_null(path) if scene != null else null
			if node != null and node.get("visible") == true:
				node.set("visible", false)
				_hidden.append(node)
		var pause_menu := scene.get_node_or_null("%PauseMenu") if scene != null else null
		if pause_menu != null and pause_menu.has_method("is_open") and pause_menu.is_open():
			pause_menu.hide_menu()
		var camera := scene.find_child("FjordCamera", true, false) if scene != null else null
		_camera_input = {}
		if camera != null and "input_enabled" in camera:
			_camera_input = {"camera": camera, "enabled": camera.input_enabled}
			camera.input_enabled = false
		_sim.hold_clock(CLOCK_HOLD)
		return
	for node in _hidden:
		if is_instance_valid(node):
			node.set("visible", true)
	_hidden.clear()
	var cam: Object = _camera_input.get("camera")
	if cam != null and is_instance_valid(cam):
		cam.set("input_enabled", bool(_camera_input.get("enabled", true)))
	_camera_input = {}
	_sim.release_clock(CLOCK_HOLD)


# ── Motion and sound ───────────────────────────────────────────────────────────

## The column eases in from the left and the cash line draws itself; with
## reduced motion everything is simply there.
func _animate_in() -> void:
	if _tween != null and _tween.is_valid():
		_tween.kill()
	var chart: PnlStoryChart = _built.get("chart")
	if MotionPrefs.reduced_motion():
		_screen.modulate.a = 1.0
		_column.position.x = 0.0
		if chart != null:
			chart.reveal = 1.0
		return
	_screen.modulate.a = 0.0
	_column.position.x = -SLIDE_PX
	_tween = create_tween().set_parallel(true)
	_tween.tween_property(_screen, "modulate:a", 1.0, FADE_IN_S).set_ease(Tween.EASE_OUT)
	_tween.tween_property(_column, "position:x", 0.0, FADE_IN_S).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	if chart != null:
		chart.reveal = 0.0
		_tween.tween_property(chart, "reveal", 1.0, REVEAL_S).set_delay(FADE_IN_S * 0.5) \
			.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)


func _play_cue() -> void:
	var audio := get_node_or_null("/root/AudioController")
	if audio == null:
		return
	if _verdict == "won" and audio.has_method("play_achievement"):
		audio.play_achievement()
	elif audio.has_method("play_ui"):
		audio.play_ui("notification")
