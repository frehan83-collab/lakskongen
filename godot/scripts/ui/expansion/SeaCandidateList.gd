class_name SeaCandidateList
extends VBoxContainer

## The Sjø step's places (geo stages 2-3, track B; docs/geo/STAGE23_PLAN.md
## §2 B): a production area's open-pen candidates by rank in a themed
## ScrollContainer, one toggle row each (Candidate_<id>) with its rank, place,
## depth, suitability bar and score, and the term that costs it most. A place
## one of the company's sites already holds is disabled, says so in its chip,
## and its tooltip names the site. Every figure is the bridge's
## (SimBridge.get_sea_candidates); the column heads stay above the scroll.
##
## Keyboard: Up and Down choose the previous or next free place (the 'why
## here' follows), Enter or Space on a row chooses it and hands on to the
## confirm. A refresh with the same places updates the rows in place, so the
## focused row keeps its focus.

## The player chose a place (a click, an arrow key or Enter).
signal candidate_chosen(candidate_id: String)
## The player confirmed the choice on a row (a click, Enter or Space): the
## dialog moves on to its confirm.
signal candidate_activated(candidate_id: String)

## Rows the list shows before it scrolls.
const VISIBLE_ROWS := 5.5
const ROW_HEIGHT := 42.0
## Column widths, shared by the heads and the rows so the columns line up.
const W_RANK := 24.0
const W_PLACE := 92.0
const W_DEPTH := 46.0
const W_BAR := 40.0
const W_SCORE := 26.0
const W_CHIP := 86.0
const GAP := 8
const PAD := 10.0

var _scroll: ScrollContainer = null
var _rows: VBoxContainer = null
## The bridge's rows shown, by rank.
var candidates: Array = []
var _selected := ""


func _init() -> void:
	add_theme_constant_override("separation", 4)
	size_flags_horizontal = Control.SIZE_EXPAND_FILL   # shares the row with the 'why here'
	custom_minimum_size = Vector2(PAD * 2.0 + W_RANK + W_PLACE + W_DEPTH + W_BAR + W_SCORE + W_CHIP + GAP * 5, 0)
	var head := HBoxContainer.new()
	head.name = "Heads"
	head.add_theme_constant_override("separation", GAP)
	add_child(head)
	head.add_child(_cell("Rank", &"CaptionMuted", W_RANK + PAD, HORIZONTAL_ALIGNMENT_RIGHT))
	var place := _cell("Place", &"CaptionMuted", W_PLACE, HORIZONTAL_ALIGNMENT_LEFT)
	place.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	head.add_child(place)
	head.add_child(_cell("Depth", &"CaptionMuted", W_DEPTH, HORIZONTAL_ALIGNMENT_RIGHT))
	head.add_child(_cell("Score", &"CaptionMuted", W_BAR + GAP + W_SCORE, HORIZONTAL_ALIGNMENT_RIGHT))
	# The term that costs the place most (the binding term, which is not always
	# the lowest-scoring one: captain's review of track B, finding 7).
	head.add_child(_cell("Binding", &"CaptionMuted", W_CHIP + PAD, HORIZONTAL_ALIGNMENT_CENTER))
	_scroll = ScrollContainer.new()
	_scroll.name = "Scroll"
	_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	_scroll.follow_focus = true
	_scroll.custom_minimum_size = Vector2(0, ROW_HEIGHT * VISIBLE_ROWS)
	# The list grows to the 'why here' beside it (more places in view).
	_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_child(_scroll)
	_rows = VBoxContainer.new()
	_rows.name = "Rows"
	_rows.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_rows.add_theme_constant_override("separation", 2)
	_scroll.add_child(_rows)


func _ready() -> void:
	var head := get_node("Heads")
	(head.get_node("Rank") as Label).text = tr("SEA_CANDIDATE_COL_RANK")
	(head.get_node("Place") as Label).text = tr("SEA_CANDIDATE_COL_PLACE")
	(head.get_node("Depth") as Label).text = tr("SEA_CANDIDATE_COL_DEPTH")
	(head.get_node("Score") as Label).text = tr("SEA_CANDIDATE_COL_SCORE")
	(head.get_node("Binding") as Label).text = tr("SEA_CANDIDATE_COL_BINDING")


## Show the candidates (the bridge's rows, by rank) with `selected` chosen ("" none).
func show_candidates(list: Array, selected: String) -> void:
	var same := list.size() == candidates.size()
	for i in mini(list.size(), candidates.size()):
		same = same and str(list[i].get("id", "")) == str(candidates[i].get("id", ""))
	candidates = list
	_selected = selected
	if not same:
		for child in _rows.get_children():
			_rows.remove_child(child)
			child.queue_free()
		for c in list:
			_rows.add_child(_make_row(str(c.get("id", ""))))
	for c in list:
		_fill_row(row_button(str(c.get("id", ""))), c)


## The row of candidate `id`, or null.
func row_button(id: String) -> Button:
	return _rows.get_node_or_null("Candidate_" + id) as Button


## The rows' scroll container.
func scroll() -> ScrollContainer:
	return _scroll


## The chosen candidate's id ("" none).
func selected() -> String:
	return _selected


## The bridge's row of `id` ({} when not shown).
func candidate(id: String) -> Dictionary:
	for c in candidates:
		if str(c.get("id", "")) == id:
			return c
	return {}


## The suggested row, else the first free one (the keyboard's way in), or null.
func first_free_row() -> Button:
	for c in candidates:
		if bool(c.get("suggested", false)):
			return row_button(str(c.get("id", "")))
	for c in candidates:
		var b := row_button(str(c.get("id", "")))
		if b != null and not b.disabled:
			return b
	return null


func _make_row(id: String) -> Button:
	var b := Button.new()
	b.name = "Candidate_" + id
	b.toggle_mode = true
	b.theme_type_variation = &"ListItemButton"
	b.custom_minimum_size = Vector2(0, ROW_HEIGHT)
	b.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var line := HBoxContainer.new()
	line.name = "Line"
	line.mouse_filter = Control.MOUSE_FILTER_IGNORE
	line.add_theme_constant_override("separation", GAP)
	b.add_child(line)
	line.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	line.offset_left = PAD
	line.offset_right = -PAD
	line.add_child(_cell("Rank", &"FigureSmall", W_RANK, HORIZONTAL_ALIGNMENT_RIGHT))
	var place := _cell("Place", &"BodySmallStrong", W_PLACE, HORIZONTAL_ALIGNMENT_LEFT)
	place.clip_text = true
	place.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	place.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	line.add_child(place)
	line.add_child(_cell("Depth", &"SmallMuted", W_DEPTH, HORIZONTAL_ALIGNMENT_RIGHT))
	var bar := ProgressBar.new()
	bar.name = "ScoreBar"
	bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	bar.show_percentage = false
	bar.min_value = 0.0
	bar.max_value = 100.0
	bar.custom_minimum_size = Vector2(W_BAR, 6)
	bar.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	line.add_child(bar)
	line.add_child(_cell("Score", &"FigureSmall", W_SCORE, HORIZONTAL_ALIGNMENT_RIGHT))
	line.add_child(_cell("Chip", &"TagMuted", W_CHIP, HORIZONTAL_ALIGNMENT_CENTER))
	b.pressed.connect(_on_pressed.bind(id))
	b.gui_input.connect(_on_row_input.bind(id))
	return b


func _cell(cell_name: String, variation: StringName, width: float, align: HorizontalAlignment) -> Label:
	var l := Label.new()
	l.name = cell_name
	l.theme_type_variation = variation
	l.horizontal_alignment = align
	l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	l.custom_minimum_size = Vector2(width, 0)
	l.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	return l


func _fill_row(b: Button, c: Dictionary) -> void:
	if b == null:
		return
	var id := str(c.get("id", ""))
	var taken := str(c.get("taken_by_site_id", "")) != ""
	var score := int(round(float(c.get("score", 0.0)) * 100.0))
	var depth := SimFormatters.num(float(c.get("depth_m", 0.0)), 0)
	var binding := str(c.get("binding", "none"))
	var line := b.get_node("Line") as HBoxContainer
	(line.get_node("Rank") as Label).text = str(int(c.get("rank", 0)))
	(line.get_node("Place") as Label).text = str(c.get("label", ""))
	(line.get_node("Depth") as Label).text = depth + " m"
	(line.get_node("ScoreBar") as ProgressBar).value = score
	(line.get_node("Score") as Label).text = str(score)
	var chip := line.get_node("Chip") as Label
	chip.text = tr("SEA_CANDIDATE_CHIP_TAKEN") if taken else tr("SEA_CANDIDATE_SHORT_" + binding.to_upper())
	chip.theme_type_variation = &"TagInfo" if taken else (&"TagOk" if binding == "none" else &"TagWarn")
	b.set_pressed_no_signal(id == _selected)
	b.focus_mode = Control.FOCUS_NONE if taken else Control.FOCUS_ALL
	# A held place reads as inactive (the lints judge text at rest only).
	line.modulate.a = 0.55 if taken else 1.0
	if taken:
		UIPolish.set_available(b, tr("SEA_CANDIDATE_TAKEN_TIP") % str(c.get("taken_by_site_label", "")))
	else:
		UIPolish.set_available(b, "")
		b.tooltip_text = tr("SEA_CANDIDATE_ROW_TIP") % [str(c.get("label", "")), score, depth, CandidateWhyHere.binding_text(binding)]


func _on_pressed(id: String) -> void:
	_mark(id)
	candidate_chosen.emit(id)
	candidate_activated.emit(id)


## Up and Down choose the previous or next free place and stop at the first
## and last (Tab leaves the list, Enter hands on to the confirm).
func _on_row_input(event: InputEvent, id: String) -> void:
	var step := 0
	if event.is_action_pressed("ui_down", true):
		step = 1
	elif event.is_action_pressed("ui_up", true):
		step = -1
	if step == 0:
		return
	row_button(id).accept_event()
	var next := _free_neighbour(id, step)
	if next == null:
		return
	next.grab_focus()
	var next_id := str(next.name).trim_prefix("Candidate_")
	_mark(next_id)
	candidate_chosen.emit(next_id)


func _free_neighbour(id: String, step: int) -> Button:
	var ids: Array = candidates.map(func(c: Dictionary) -> String: return str(c.get("id", "")))
	var i: int = ids.find(id) + step
	while i >= 0 and i < ids.size():
		var b := row_button(str(ids[i]))
		if b != null and not b.disabled:
			return b
		i += step
	return null


func _mark(id: String) -> void:
	_selected = id
	for c in candidates:
		var b := row_button(str(c.get("id", "")))
		if b != null:
			b.set_pressed_no_signal(str(c.get("id", "")) == id)
