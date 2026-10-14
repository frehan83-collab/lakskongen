## ScenarioSelectPanel.gd — the scenario picker (roadmap U1-07).
##
## A full-screen layer over the main menu: a grid of scenario cards on the left
## and the selected scenario in full on the right. The cards come from the
## engine's roster (SimBridge.get_available_scenarios(): the built-in
## scenarios and every mod scenario), so nothing about a scenario is typed in
## here: name, region, difficulty, capital (formatted from the data), goal and
## time limit all come from the roster. The grid takes as many columns as fit
## (1280–2560 px windows, UI scale 80–125 %) and scrolls when the cards do not.
##
## A card is selected by a click, by keyboard focus or select(); double-click,
## Enter on the selected card or the Start button starts it. A padlock tag shows
## only on a scenario the roster marks locked (none until progression, U2-03).
## Esc and Tilbake emit panel_close_requested; the main menu closes the layer.
##
## Signals:
##   scenario_selected(scenario_id: String)  — the player starts a scenario
##   panel_close_requested                   — Esc or Tilbake

extends Control

signal scenario_selected(scenario_id: String)
signal panel_close_requested

const CARD_MIN_W := 268.0
const CARD_H := 188.0
## Height of a card's region / tag row.
const TOP_ROW_H := 22.0
const GRID_GAP := 16
const DETAIL_W := 460.0
const PAGE_MARGIN := 48.0
## Backdrop behind the picker (an Ink layer over the menu).
const SCRIM_ALPHA := 1.0

## Difficulty 1–5 → stripe / pip tone.
const DIFFICULTY_TONES := [&"Ok", &"Ok", &"Info", &"Warn", &"Crit"]
const DIFFICULTY_KEYS := ["UI_DIFFICULTY_1", "UI_DIFFICULTY_2", "UI_DIFFICULTY_3", "UI_DIFFICULTY_4", "UI_DIFFICULTY_5"]
## ProductionRegion names from the roster → translation keys.
const REGION_KEYS := {
	"Nordland": "UI_REGION_NORDLAND", "Troms": "UI_REGION_TROMS", "Finnmark": "UI_REGION_FINNMARK",
	"Trøndelag": "UI_REGION_TRONDELAG", "Vestland": "UI_REGION_VESTLAND", "Rogaland": "UI_REGION_ROGALAND",
	"MøreOgRomsdal": "UI_REGION_MORE_OG_ROMSDAL", "Sørlandet": "UI_REGION_SORLANDET",
}

var _scenarios: Array[Dictionary] = []
var _cards: Dictionary = {}  # id -> Button
var _selected_id: String = ""
var _starting: bool = false

var _grid: GridContainer = null
var _grid_scroll: ScrollContainer = null
var _count_label: Label = null
var _back_button: Button = null
var _detail: Dictionary = {}  # role -> Control
var _start_button: Button = null

# ── Lifecycle ───────────────────────────────────────────────────────────────────

func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	mouse_filter = Control.MOUSE_FILTER_STOP
	_scenarios = _load_roster()
	_build()
	if not _scenarios.is_empty():
		select(str(_scenarios[0].get("id", "")))
	_grid_scroll.resized.connect(_fit_columns)
	_fit_columns.call_deferred()
	modulate.a = 0.0
	create_tween().tween_property(self, "modulate:a", 1.0, 0.25)
	_focus_selected.call_deferred()

func _input(event: InputEvent) -> void:
	if not is_visible_in_tree():
		return
	if event.is_action_pressed("ui_cancel"):
		panel_close_requested.emit()
		get_viewport().set_input_as_handled()

# ── Public API ──────────────────────────────────────────────────────────────────

## The roster the picker shows (one entry per card, in order).
func get_scenarios() -> Array[Dictionary]:
	return _scenarios.duplicate(true)

## The card for a scenario id (null when absent).
func get_card(scenario_id: String) -> Button:
	return _cards.get(scenario_id)

func get_selected_id() -> String:
	return _selected_id

func get_start_button() -> Button:
	return _start_button

## Show a scenario in the detail pane and mark its card.
func select(scenario_id: String) -> void:
	var entry := _entry(scenario_id)
	if entry.is_empty():
		return
	_selected_id = scenario_id
	for id in _cards:
		var card: Button = _cards[id]
		card.set_pressed_no_signal(id == scenario_id)
	_show_detail(entry)

## Start the selected scenario (the Start button, a double-click or Enter).
func start_selected() -> void:
	var entry := _entry(_selected_id)
	if entry.is_empty() or _starting or not _is_unlocked(entry):
		return
	_starting = true
	_start_button.disabled = true
	scenario_selected.emit(_selected_id)

## The main menu could not start the scenario: say why and offer it again.
func start_failed(reason: String) -> void:
	_starting = false
	_start_button.disabled = false
	var toast := get_node_or_null("/root/ToastNotification")
	if toast != null:
		toast.show_toast(tr("UI_SCENARIO_START_FAILED") % reason, "WARNING", 6.0)

## The capital a card shows for a roster entry (formatted from the data).
static func capital_text(entry: Dictionary) -> String:
	return SimFormatters.format_nok(float(entry.get("starting_capital", 0.0)))

# ── Roster ──────────────────────────────────────────────────────────────────────

func _load_roster() -> Array[Dictionary]:
	var out: Array[Dictionary] = []
	var sim := get_node_or_null("/root/SimBridge")
	if sim == null or not sim.has_method("get_available_scenarios"):
		return out
	for entry in sim.get_available_scenarios():
		if entry is Dictionary and not str(entry.get("id", "")).is_empty():
			out.append(entry)
	return out

func _entry(scenario_id: String) -> Dictionary:
	for entry in _scenarios:
		if str(entry.get("id", "")) == scenario_id:
			return entry
	return {}

static func _is_unlocked(entry: Dictionary) -> bool:
	return bool(entry.get("unlocked", entry.get("is_unlocked", true)))

static func _is_mod(entry: Dictionary) -> bool:
	return bool(entry.get("is_mod", false))

func _difficulty(entry: Dictionary) -> int:
	return clampi(int(entry.get("difficulty", 3)), 1, 5)

func _tone(entry: Dictionary) -> StringName:
	return DIFFICULTY_TONES[_difficulty(entry) - 1]

func _region_name(entry: Dictionary) -> String:
	var raw := str(entry.get("region", ""))
	return tr(REGION_KEYS[raw]) if REGION_KEYS.has(raw) else raw

func _difficulty_text(entry: Dictionary) -> String:
	return tr(DIFFICULTY_KEYS[_difficulty(entry) - 1])

## "●●●○○" — the difficulty as five pips.
func _pips(entry: Dictionary) -> String:
	var lit := _difficulty(entry)
	return "●".repeat(lit) + "○".repeat(5 - lit)

func _time_limit_text(entry: Dictionary) -> String:
	var days := int(entry.get("time_limit_days", 0))
	if days <= 0:
		return tr("UI_SCENARIO_NO_LIMIT")
	if days % 365 == 0:
		return tr("UI_SCENARIO_YEARS") % SimFormatters.num(days / 365.0, 0)
	return tr("UI_SCENARIO_DAYS") % days

# ── Layout ──────────────────────────────────────────────────────────────────────

func _build() -> void:
	var scrim := ColorRect.new()
	scrim.name = "Scrim"
	scrim.color = Color(LakskongenTheme.INK, SCRIM_ALPHA)
	scrim.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	scrim.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(scrim)

	var page := MarginContainer.new()
	page.name = "Page"
	page.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	for side in ["margin_left", "margin_right", "margin_top", "margin_bottom"]:
		page.add_theme_constant_override(side, int(PAGE_MARGIN))
	add_child(page)

	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 24)
	page.add_child(column)
	column.add_child(_build_header())

	var body := HBoxContainer.new()
	body.name = "Body"
	body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	body.add_theme_constant_override("separation", 24)
	column.add_child(body)

	_grid_scroll = ScrollContainer.new()
	_grid_scroll.name = "CardScroll"
	_grid_scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_grid_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	_grid_scroll.follow_focus = true
	body.add_child(_grid_scroll)
	_grid = GridContainer.new()
	_grid.name = "CardGrid"
	_grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_grid.add_theme_constant_override("h_separation", GRID_GAP)
	_grid.add_theme_constant_override("v_separation", GRID_GAP)
	_grid_scroll.add_child(_grid)
	for entry in _scenarios:
		var card := _build_card(entry)
		_grid.add_child(card)
		_cards[str(entry.get("id", ""))] = card
	if _scenarios.is_empty():
		var empty := Label.new()
		empty.text = tr("UI_SCENARIO_EMPTY")
		empty.theme_type_variation = &"BodyMuted"
		_grid.add_child(empty)

	body.add_child(_build_detail())

func _build_header() -> Control:
	var header := HBoxContainer.new()
	header.name = "Header"
	var titles := VBoxContainer.new()
	titles.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	titles.add_theme_constant_override("separation", 2)
	header.add_child(titles)
	var overline := Label.new()
	overline.text = tr("UI_SCENARIO_OVERLINE")
	overline.theme_type_variation = &"OverlineAccent"
	titles.add_child(overline)
	var title := Label.new()
	title.name = "Title"
	title.text = tr("UI_SCENARIO_HEADING")
	title.theme_type_variation = &"Title"
	titles.add_child(title)
	_count_label = Label.new()
	_count_label.name = "Count"
	var mods := _scenarios.filter(func(e: Dictionary) -> bool: return _is_mod(e)).size()
	_count_label.text = (tr("UI_SCENARIO_COUNT_MODS") % [_scenarios.size(), mods]) if mods > 0 \
		else (tr("UI_SCENARIO_COUNT") % _scenarios.size())
	_count_label.theme_type_variation = &"BodySmallMuted"
	titles.add_child(_count_label)

	_back_button = Button.new()
	_back_button.name = "BackButton"
	_back_button.text = tr("UI_SCENARIO_BACK")
	_back_button.tooltip_text = tr("UI_SCENARIO_BACK_TIP")
	_back_button.theme_type_variation = &"GhostButton"
	_back_button.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	_back_button.pressed.connect(func() -> void: panel_close_requested.emit())
	header.add_child(_back_button)
	return header

## One card: a toggle button drawn as a Kyst & Stål card (a difficulty-tone
## stripe on raised Ink), a salmon frame when selected; the labels inside let
## every click through to the button.
func _build_card(entry: Dictionary) -> Button:
	var id := str(entry.get("id", ""))
	var tone := _tone(entry)
	var card := Button.new()
	card.name = "Card_" + id.validate_node_name()
	card.set_meta("scenario_id", id)
	card.toggle_mode = true
	card.custom_minimum_size = Vector2(CARD_MIN_W, CARD_H)
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	card.tooltip_text = str(entry.get("description", ""))
	if _is_mod(entry):
		card.set_meta(&"player_text", true)  # a mod's own text (not ours to translate)
	card.focus_mode = Control.FOCUS_ALL
	_style_card(card, tone)

	var margin := MarginContainer.new()
	margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	margin.mouse_filter = Control.MOUSE_FILTER_IGNORE
	for side in ["margin_left", "margin_right"]:
		margin.add_theme_constant_override(side, 16)
	margin.add_theme_constant_override("margin_top", 14)
	margin.add_theme_constant_override("margin_bottom", 12)
	card.add_child(margin)

	var box := VBoxContainer.new()
	box.mouse_filter = Control.MOUSE_FILTER_IGNORE
	box.add_theme_constant_override("separation", 4)
	margin.add_child(box)

	var top := HBoxContainer.new()
	top.mouse_filter = Control.MOUSE_FILTER_IGNORE
	top.custom_minimum_size = Vector2(0, TOP_ROW_H)  # a tag never shifts the name
	box.add_child(top)
	var region := _card_label(_region_name(entry), &"OverlineMuted")
	region.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	top.add_child(region)
	if _is_mod(entry):
		var mod_tag := _card_label(tr("UI_SCENARIO_MOD_TAG"), &"TagInfo", "ModTag")
		mod_tag.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		top.add_child(mod_tag)
	if not _is_unlocked(entry):
		var lock_tag := _card_label(tr("UI_SCENARIO_LOCKED_TAG"), &"TagMuted", "LockTag")
		lock_tag.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		top.add_child(lock_tag)

	var name_label := _card_label(str(entry.get("name", id)), &"Subheading", "Name")
	name_label.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	box.add_child(name_label)
	if _is_mod(entry):
		region.set_meta(&"player_text", true)
		name_label.set_meta(&"player_text", true)  # a mod's own text (not ours to translate)

	var level := HBoxContainer.new()
	level.mouse_filter = Control.MOUSE_FILTER_IGNORE
	level.add_theme_constant_override("separation", 8)
	box.add_child(level)
	level.add_child(_card_label(_pips(entry), StringName("Caption" + String(tone)), "Pips"))
	level.add_child(_card_label(_difficulty_text(entry), &"CaptionMuted"))

	# The pitch in two lines (the detail pane has the whole story).
	var pitch := _card_label(str(entry.get("description", "")), &"BodySmallMuted", "Pitch")
	pitch.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	pitch.max_lines_visible = 2
	pitch.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	pitch.custom_minimum_size = Vector2(CARD_MIN_W - 32.0, 0)
	if _is_mod(entry):
		pitch.set_meta(&"player_text", true)
	box.add_child(pitch)

	var spacer := Control.new()
	spacer.mouse_filter = Control.MOUSE_FILTER_IGNORE
	spacer.size_flags_vertical = Control.SIZE_EXPAND_FILL
	box.add_child(spacer)

	var money := HBoxContainer.new()
	money.mouse_filter = Control.MOUSE_FILTER_IGNORE
	box.add_child(money)
	var caption := _card_label(tr("UI_SCENARIO_CAPITAL_ICON"), &"SmallMuted")
	caption.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	money.add_child(caption)
	var negative := float(entry.get("starting_capital", 0.0)) < 0.0
	money.add_child(_card_label(capital_text(entry), &"FigureSmallCrit" if negative else &"FigureSmall", "Capital"))

	card.pressed.connect(select.bind(id))
	card.focus_entered.connect(select.bind(id))
	card.gui_input.connect(_on_card_input.bind(id))
	return card

func _card_label(text: String, variation: StringName, node_name: String = "") -> Label:
	var lbl := Label.new()
	if node_name != "":
		lbl.name = node_name
	lbl.text = text
	lbl.theme_type_variation = variation
	lbl.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return lbl

## The card's button states from the theme's KpiCard surface: hover lifts the
## fill, the selected (pressed) card gets a salmon frame.
func _style_card(card: Button, tone: StringName) -> void:
	var base := card.get_theme_stylebox("panel", StringName("KpiCard" + String(tone))) as StyleBoxFlat
	if base == null:
		return
	var normal := base.duplicate() as StyleBoxFlat
	# Hover lifts the fill a little; the selected card keeps the card fill (a
	# lighter one took the muted text under 4.5:1) and gets a salmon frame.
	var hover := base.duplicate() as StyleBoxFlat
	hover.bg_color = base.bg_color.lightened(0.03)
	var selected := base.duplicate() as StyleBoxFlat
	selected.border_color = LakskongenTheme.SALMON
	selected.set_border_width_all(2)
	selected.border_width_top = 3
	for state in ["normal", "disabled"]:
		card.add_theme_stylebox_override(state, normal)
	card.add_theme_stylebox_override("hover", hover)
	for state in ["pressed", "hover_pressed"]:
		card.add_theme_stylebox_override(state, selected)

func _on_card_input(event: InputEvent, scenario_id: String) -> void:
	var mb := event as InputEventMouseButton
	if mb != null and mb.double_click and mb.button_index == MOUSE_BUTTON_LEFT:
		select(scenario_id)
		start_selected()
		accept_event()
	elif event.is_action_pressed("ui_accept") and scenario_id == _selected_id \
			and _cards.get(scenario_id) != null and (_cards[scenario_id] as Button).has_focus():
		start_selected()
		accept_event()

## Pick as many columns as fit, every card at least CARD_MIN_W wide.
func _fit_columns() -> void:
	if _grid == null or _grid_scroll == null:
		return
	var width := _grid_scroll.size.x - 12.0  # room for the scroll bar
	var cols := clampi(int((width + GRID_GAP) / (CARD_MIN_W + GRID_GAP)), 1, 4)
	if _grid.columns != cols:
		_grid.columns = cols

func _focus_selected() -> void:
	var card: Button = _cards.get(_selected_id)
	if card != null and is_inside_tree():
		card.grab_focus()

# ── Detail pane ─────────────────────────────────────────────────────────────────

func _build_detail() -> Control:
	var panel := PanelContainer.new()
	panel.name = "Detail"
	panel.theme_type_variation = &"Dialog"
	panel.custom_minimum_size = Vector2(DETAIL_W, 0)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 12)
	panel.add_child(box)

	_detail["overline"] = _detail_label(box, &"OverlineMuted")
	_detail["name"] = _detail_label(box, &"Title", true)
	var level := HBoxContainer.new()
	level.add_theme_constant_override("separation", 10)
	box.add_child(level)
	_detail["pips"] = _detail_label(level, &"BodyStrong")
	_detail["difficulty"] = _detail_label(level, &"BodySmallMuted")
	box.add_child(HSeparator.new())
	_detail["story"] = _detail_label(box, &"Body", true)
	var rules := HFlowContainer.new()
	rules.add_theme_constant_override("h_separation", 6)
	rules.add_theme_constant_override("v_separation", 6)
	box.add_child(rules)
	_detail["rules"] = rules

	var facts := GridContainer.new()
	facts.name = "Facts"
	facts.columns = 2
	facts.add_theme_constant_override("h_separation", 20)
	facts.add_theme_constant_override("v_separation", 8)
	box.add_child(facts)
	for fact in [["capital", "UI_SCENARIO_CAPITAL_ICON"], ["goal", "UI_SCENARIO_WIN_ICON"],
			["time", "UI_SCENARIO_FACT_TIME"], ["region", "UI_SCENARIO_REGION_ICON"]]:
		var caption := Label.new()
		caption.text = tr(fact[1])
		caption.theme_type_variation = &"SmallMuted"
		caption.size_flags_vertical = Control.SIZE_SHRINK_BEGIN
		facts.add_child(caption)
		var value := Label.new()
		value.name = "Fact_" + fact[0]
		value.theme_type_variation = &"BodySmallStrong"
		value.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		value.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		facts.add_child(value)
		_detail["fact_" + fact[0]] = value

	var spacer := Control.new()
	spacer.size_flags_vertical = Control.SIZE_EXPAND_FILL
	box.add_child(spacer)
	_start_button = Button.new()
	_start_button.name = "StartButton"
	_start_button.theme_type_variation = &"LargePrimaryButton"
	_start_button.custom_minimum_size = Vector2(0, 52)
	_start_button.pressed.connect(start_selected)
	box.add_child(_start_button)
	return panel

func _detail_label(parent: Control, variation: StringName, wrap: bool = false) -> Label:
	var lbl := Label.new()
	lbl.theme_type_variation = variation
	if wrap:
		lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	parent.add_child(lbl)
	return lbl

func _show_detail(entry: Dictionary) -> void:
	var mod := _is_mod(entry)
	var tone := _tone(entry)
	var overline: Label = _detail["overline"]
	overline.text = _region_name(entry) if not mod else "%s · %s" % [_region_name(entry), tr("UI_SCENARIO_MOD_TAG")]
	var name_label: Label = _detail["name"]
	name_label.text = str(entry.get("name", ""))
	var pips: Label = _detail["pips"]
	pips.text = _pips(entry)
	pips.theme_type_variation = StringName("BodyStrong" + String(tone))
	(_detail["difficulty"] as Label).text = _difficulty_text(entry)
	var story: Label = _detail["story"]
	story.text = str(entry.get("story", entry.get("description", "")))
	for lbl: Label in [overline, name_label, story]:
		if mod:
			lbl.set_meta(&"player_text", true)
		elif lbl.has_meta(&"player_text"):
			lbl.remove_meta(&"player_text")

	var rules: HFlowContainer = _detail["rules"]
	for child in rules.get_children():
		child.queue_free()
	for rule in entry.get("special", []):
		var chip := Label.new()
		chip.text = str(rule)
		chip.theme_type_variation = &"TagInfo"
		rules.add_child(chip)
	rules.visible = not (entry.get("special", []) as Array).is_empty()

	var capital: Label = _detail["fact_capital"]
	capital.text = capital_text(entry)
	capital.theme_type_variation = &"BodySmallStrongCrit" if float(entry.get("starting_capital", 0.0)) < 0.0 else &"BodySmallStrong"
	var goal: Label = _detail["fact_goal"]
	goal.text = str(entry.get("win", ""))
	if mod:
		goal.set_meta(&"player_text", true)
	elif goal.has_meta(&"player_text"):
		goal.remove_meta(&"player_text")
	(_detail["fact_time"] as Label).text = _time_limit_text(entry)
	(_detail["fact_region"] as Label).text = _region_name(entry)

	var unlocked := _is_unlocked(entry)
	_start_button.text = tr("UI_SCENARIO_START_BUTTON")
	_start_button.disabled = not unlocked or _starting
	_start_button.tooltip_text = tr("UI_SCENARIO_START_TOOLTIP") if unlocked else tr("UI_SCENARIO_LOCKED_TOOLTIP")
