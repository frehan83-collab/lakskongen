extends Control

## BriefingCard — a new game opens paused on its briefing (roadmap U2-07).
##
## The game UI steps aside and the fjord shows behind an Ink-glass column (the
## main menu's): what the scenario is, the date it starts on, what it asks of
## the player and by when, what they start with, and how the clock works.
## "Start sesongen" (Enter) starts the clock at the default speed;
## "Se deg rundt først" (Esc) closes the card and leaves the game paused.
## Successful sims open the same way (a mission briefing with the world
## stopped behind it: Anno, Frostpunk 2, Two Point); the clock never runs
## behind text the player is still reading.
##
## Shown for a player's new game only (SimBridge.is_briefing_pending()); QA
## games and loaded games have none. While it is open the game's keys wait.

signal briefing_shown
## "start" or "look".
signal briefing_closed(choice: String)

const GROUP := &"briefing_card"
## Game keys that wait while the card is open.
const BLOCKED_ACTIONS := [&"toggle_pause", &"toggle_notifications", &"toggle_attention_inbox",
	&"next_event", &"speed_1", &"speed_2", &"speed_3", &"speed_4", &"quick_save"]
const FADE_IN_S := 0.35
const FADE_OUT_S := 0.2
const SLIDE_PX := 36.0
## FjordCamera.ZoomLevel.FJORD, and how long to wait for the pens to appear.
const FJORD_ZOOM := 1
const FRAME_TRIES := 30
const FRAME_RETRY_S := 0.1
const Picker := preload("res://scripts/ui/ScenarioSelectPanel.gd")

@onready var _column: Control = %Column
@onready var _overline: Label = %Overline
@onready var _title: Label = %TitleLabel
@onready var _date: Label = %DateLabel
@onready var _level_row: Control = %LevelRow
@onready var _pips: Label = %PipsLabel
@onready var _level: Label = %LevelLabel
@onready var _story: Label = %StoryLabel
@onready var _rules: HFlowContainer = %Rules
@onready var _facts: GridContainer = %Facts
@onready var _keys: HFlowContainer = %Keys
@onready var start_button: Button = %StartButton
@onready var look_button: Button = %LookButton

var _sim: Node = null
var _open: bool = false
var _hidden_ui: Array[Node] = []
var _tween: Tween = null
## Times the card was shown (tests).
var shown_count: int = 0


func _ready() -> void:
	add_to_group(GROUP)
	_sim = get_node_or_null("/root/SimBridge")
	start_button.pressed.connect(_on_start)
	look_button.pressed.connect(_on_look)
	UIPolish.attach_press_feedback(start_button)
	UIPolish.attach_press_feedback(look_button)
	_build_keys()
	var bridge := get_node_or_null("/root/GameBridge")
	if bridge != null:
		bridge.GameStarted.connect(_on_game_started)
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	hide()
	# The menu started the game before this scene existed.
	_check_pending.call_deferred()


func is_open() -> bool:
	return _open


## Open the card for the running game (its briefing is pending).
func present() -> void:
	_fill()
	if not _open:
		_open = true
		shown_count += 1
		_step_aside(true)
		mouse_filter = Control.MOUSE_FILTER_STOP
		show()
		_animate_in()
		_frame_the_farm()
		briefing_shown.emit()
	start_button.grab_focus.call_deferred()


## The camera shows the player's own farm beside the column (FJORD zoom on
## the first site's pens); the pens arrive a few frames after the start.
func _frame_the_farm() -> void:
	var scene := owner if owner != null else get_parent()
	if scene == null:
		return
	var pens := scene.find_child("FishPenManager", true, false)
	var env := scene.find_child("FjordEnvironment", true, false)
	if pens == null or env == null or not pens.has_method("get_pen_ids") or not env.has_method("focus_camera_on"):
		return
	for i in FRAME_TRIES:
		if not is_inside_tree() or not _open:
			return
		var ids: Array = pens.call("get_pen_ids")
		if not ids.is_empty():
			env.call("focus_camera_on", pens.call("get_pen_position", ids[0]), FJORD_ZOOM)
			return
		await get_tree().create_timer(FRAME_RETRY_S).timeout


## A new game: its card, after the scenario is in place (a scenario start
## activates the scenario right after the game starts). A game without a
## briefing (QA, loaded) closes a card left from the game before.
func _on_game_started() -> void:
	_check_pending.call_deferred()


func _check_pending() -> void:
	if _sim != null and _sim.is_briefing_pending():
		present()
	elif _open:
		_close("")


func _on_start() -> void:
	if not _open:
		return
	if _sim != null:
		_sim.begin_play(float(GameplayPrefs.default_speed()))
	_close("start")


func _on_look() -> void:
	if not _open:
		return
	if _sim != null:
		_sim.dismiss_briefing()
	_close("look")


func _close(choice: String) -> void:
	_open = false
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	_step_aside(false)
	if _tween != null and _tween.is_valid():
		_tween.kill()
	_tween = create_tween()
	_tween.tween_property(self, "modulate:a", 0.0, FADE_OUT_S)
	_tween.tween_callback(func() -> void:
		if not _open:
			hide())
	if choice != "":
		briefing_closed.emit(choice)


## Enter starts (the focused button), Esc looks around; the game's keys wait.
func _input(event: InputEvent) -> void:
	if not _open:
		return
	if event.is_action_pressed("ui_cancel"):
		_on_look()
		get_viewport().set_input_as_handled()
		return
	for action in BLOCKED_ACTIONS:
		if event.is_action(action):
			get_viewport().set_input_as_handled()
			return


# ── The game UI steps aside ────────────────────────────────────────────────────

## The HUD, tabs and hints hide while the card is open, so the world is what
## the player sees behind it; they come back when it closes.
func _step_aside(aside: bool) -> void:
	if aside:
		_hidden_ui.clear()
		var scene := owner if owner != null else get_parent()
		var layer := scene.get_node_or_null("UILayer") if scene != null else null
		if layer is CanvasLayer and (layer as CanvasLayer).visible:
			(layer as CanvasLayer).visible = false
			_hidden_ui.append(layer)
		return
	for item in _hidden_ui:
		if is_instance_valid(item):
			item.visible = true
	_hidden_ui.clear()


# ── Content ────────────────────────────────────────────────────────────────────

func _fill() -> void:
	var day: int = int(_sim.current_day) if _sim != null else 0
	var entry := _scenario_entry()
	var scenario := not entry.is_empty()
	if scenario:
		_overline.text = tr("UI_BRIEFING_CARD_OVERLINE_SCENARIO") % _region_name(entry)
		_title.text = str(entry.get("name", ""))
		_story.text = str(entry.get("story", entry.get("description", "")))
		var lit := clampi(int(entry.get("difficulty", 3)), 1, 5)
		_pips.text = "●".repeat(lit) + "○".repeat(5 - lit)
		_pips.theme_type_variation = StringName("BodyStrong" + String(Picker.DIFFICULTY_TONES[lit - 1]))
		_level.text = tr(Picker.DIFFICULTY_KEYS[lit - 1])
	else:
		_overline.text = tr("UI_BRIEFING_CARD_OVERLINE_FREE")
		_title.text = tr("UI_BRIEFING_CARD_FREE_TITLE")
		_story.text = tr("UI_BRIEFING_CARD_FREE_STORY")
	_level_row.visible = scenario
	var date := GameCalendar.long_date(day)
	_date.text = tr("UI_BRIEFING_CARD_DATE") % [date.left(1).to_upper() + date.substr(1),
		GameCalendar.iso_week(day), GameCalendar.game_year(day)]
	_fill_rules(entry.get("special", []) if scenario else [])
	_fill_facts(entry, day)
	var speed := GameplayPrefs.default_speed()
	start_button.text = tr("UI_BRIEFING_CARD_START")
	start_button.tooltip_text = tr("UI_BRIEFING_CARD_START_TIP") % ("%d×" % speed)
	look_button.text = tr("UI_BRIEFING_CARD_LOOK")
	look_button.tooltip_text = tr("UI_BRIEFING_CARD_LOOK_TIP")


func _scenario_entry() -> Dictionary:
	if _sim == null:
		return {}
	var id := str(_sim.get_active_scenario_id())
	if id == "":
		return {}
	for entry in _sim.get_available_scenarios():
		if str(entry.get("id", "")) == id:
			return entry
	return {}


func _region_name(entry: Dictionary) -> String:
	var raw := str(entry.get("region", ""))
	return tr(Picker.REGION_KEYS[raw]) if Picker.REGION_KEYS.has(raw) else raw


func _fill_rules(rules: Array) -> void:
	for child in _rules.get_children():
		child.queue_free()
	for rule in rules:
		var chip := Label.new()
		chip.text = str(rule)
		chip.theme_type_variation = &"TagInfo"
		_rules.add_child(chip)
	_rules.visible = not rules.is_empty()


## Goal, time limit (with the date it ends), starting capital and the farm.
func _fill_facts(entry: Dictionary, day: int) -> void:
	for child in _facts.get_children():
		child.queue_free()
	if not entry.is_empty():
		_fact("goal", tr("UI_BRIEFING_CARD_FACT_GOAL"), str(entry.get("win", "")))
		var limit := int(entry.get("time_limit_days", 0))
		var limit_text := tr("UI_SCENARIO_NO_LIMIT")
		if limit > 0:
			var span := tr("UI_SCENARIO_YEARS") % SimFormatters.num(limit / 365.0, 0) if limit % 365 == 0 \
				else tr("UI_SCENARIO_DAYS") % limit
			limit_text = tr("UI_BRIEFING_CARD_DEADLINE_VALUE") % [span, GameCalendar.long_date(day + limit - 1)]
		_fact("time", tr("UI_SCENARIO_FACT_TIME"), limit_text)
	var capital := float(_sim.balance) if _sim != null else 0.0
	_fact("capital", tr("UI_SCENARIO_CAPITAL_ICON"), SimFormatters.format_nok(capital), capital < 0.0)
	_fact("farm", tr("UI_BRIEFING_CARD_FACT_FARM"), _farm_text())


func _farm_text() -> String:
	var sites: Array = _sim.get_all_pens() if _sim != null else []
	var fish := 0
	for site in sites:
		fish += int(site.get("fish_count", 0))
	if sites.is_empty():
		return tr("UI_BRIEFING_CARD_FARM_NONE")
	if sites.size() == 1:
		return tr("UI_BRIEFING_CARD_FARM_ONE") % SimFormatters.integer(fish)
	return tr("UI_BRIEFING_CARD_FARM_MANY") % [sites.size(), SimFormatters.integer(fish)]


func _fact(key: String, caption: String, value: String, negative: bool = false) -> void:
	var cap := Label.new()
	cap.name = "Caption_" + key
	cap.text = caption
	cap.theme_type_variation = &"OverlineMuted"
	cap.uppercase = true
	cap.size_flags_vertical = Control.SIZE_SHRINK_BEGIN
	_facts.add_child(cap)
	var val := Label.new()
	val.name = "Fact_" + key
	val.text = value
	val.theme_type_variation = &"BodySmallStrongCrit" if negative else &"BodySmallStrong"
	val.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	val.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	val.custom_minimum_size = Vector2(300, 0)
	_facts.add_child(val)


## The clock's keys as key caps: Mellomrom pause · 1–4 fart · N neste hendelse.
func _build_keys() -> void:
	for pair in [["UI_BRIEFING_CARD_KEY_SPACE", "UI_BRIEFING_CARD_KEY_PAUSE"],
			["UI_BRIEFING_CARD_KEY_DIGITS", "UI_BRIEFING_CARD_KEY_SPEEDS"],
			["UI_BRIEFING_CARD_KEY_N", "UI_BRIEFING_CARD_KEY_NEXT"]]:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		var cap := PanelContainer.new()
		cap.theme_type_variation = &"Chip"
		var key := Label.new()
		key.text = tr(pair[0])
		key.theme_type_variation = &"Figure"
		cap.add_child(key)
		row.add_child(cap)
		var what := Label.new()
		what.text = tr(pair[1])
		what.theme_type_variation = &"BodySmall"
		what.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		row.add_child(what)
		_keys.add_child(row)


# ── Motion ─────────────────────────────────────────────────────────────────────

## The column eases in from the left over the fjord; with reduced motion it
## fades in place.
func _animate_in() -> void:
	if _tween != null and _tween.is_valid():
		_tween.kill()
	modulate.a = 0.0
	_tween = create_tween().set_parallel(true)
	_tween.tween_property(self, "modulate:a", 1.0, FADE_IN_S).set_ease(Tween.EASE_OUT)
	if not MotionPrefs.reduced_motion():
		_column.position.x = -SLIDE_PX
		_tween.tween_property(_column, "position:x", 0.0, FADE_IN_S) \
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
