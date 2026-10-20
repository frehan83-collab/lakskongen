class_name GeoRegionDetail
extends VBoxContainer
## The detail column at the region level (geo stages 2-3 plan Track D): who
## farms the area (RivalPresenceList) and the area's 12 open-pen candidates
## as focusable buttons in rank order, each with its score bar and state, the
## keyboard's way to every star on the map: Up and Down cycle, Enter opens
## the candidate's card. A disabled button says why in its tooltip.

signal candidate_chosen(row: Dictionary)

var _rivals: RivalPresenceList
var _header: Label
var _list: VBoxContainer
var _note: Label


func _ready() -> void:
	name = "GeoRegionDetail"
	add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)
	_rivals = RivalPresenceList.new()
	_rivals.name = "RivalPresenceList"
	add_child(_rivals)
	var sep := HSeparator.new()
	sep.name = "Sep"
	add_child(sep)
	_header = Label.new()
	_header.name = "CandidatesHeader"
	_header.theme_type_variation = &"Overline"
	_header.text = tr("MAP_REGION_CANDIDATES_HEADER")
	add_child(_header)
	_note = Label.new()
	_note.name = "CandidatesNote"
	_note.theme_type_variation = &"SmallMuted"
	_note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_note.text = tr("MAP_REGION_CANDIDATES_NOTE")
	add_child(_note)
	_list = VBoxContainer.new()
	_list.name = "CandidateList"
	_list.add_theme_constant_override("separation", 2)
	add_child(_list)


func rivals() -> RivalPresenceList:
	return _rivals


## Show an area: its rival summary and its candidate rows (rank order).
func show_area(summary: Dictionary, rows: Array, view: GeoRegionView) -> void:
	_rivals.show_summary(summary)
	show_candidates(rows, view)


## Rebuild the candidate buttons (states change on a build or a licence).
func show_candidates(rows: Array, view: GeoRegionView) -> void:
	var focused := get_viewport().gui_get_focus_owner() if is_inside_tree() else null
	var focused_name := String(focused.name) if focused != null and _list.is_ancestor_of(focused) else ""
	for c in _list.get_children():
		_list.remove_child(c)
		c.queue_free()
	var buttons: Array[Button] = []
	for row: Dictionary in rows:
		var b := _CandidateButton.new()
		b.name = "MapCandidate_" + String(row.get("id", ""))
		b.score = float(row.get("score", 0.0))
		b.state = view.state_of(row)
		b.text = tr("MAP_REGION_CANDIDATE_BUTTON") % [int(row.get("rank", 0)), String(row.get("label", "")),
			SimFormatters.num(float(row.get("score", 0.0)), 3), tr(GeoRegionView.STATE_KEYS[b.state])]
		b.tooltip_text = tr("MAP_REGION_CANDIDATE_BUTTON_TIP") % String(row.get("label", ""))
		b.alignment = HORIZONTAL_ALIGNMENT_LEFT
		b.theme_type_variation = &"ListItemButton"
		b.custom_minimum_size = Vector2(0, 34)
		b.focus_mode = Control.FOCUS_ALL
		b.pressed.connect(func() -> void: candidate_chosen.emit(row))
		_list.add_child(b)
		buttons.append(b)
	# Up and Down cycle through the list: the last wraps to the first.
	for i in buttons.size():
		var prev := buttons[(i - 1 + buttons.size()) % buttons.size()]
		var next := buttons[(i + 1) % buttons.size()]
		buttons[i].focus_neighbor_top = buttons[i].get_path_to(prev)
		buttons[i].focus_neighbor_bottom = buttons[i].get_path_to(next)
		buttons[i].focus_previous = buttons[i].get_path_to(prev)
		buttons[i].focus_next = buttons[i].get_path_to(next)
	if focused_name != "":
		var again := _list.get_node_or_null(focused_name) as Control
		if again != null:
			again.grab_focus.call_deferred()


## The button of a candidate id, or null.
func candidate_button(candidate_id: String) -> Button:
	return _list.get_node_or_null("MapCandidate_" + candidate_id) as Button


## The candidate buttons in the order shown.
func candidate_buttons() -> Array[Button]:
	var out: Array[Button] = []
	for c in _list.get_children():
		if c is Button:
			out.append(c)
	return out


## A candidate's button: its text, and the score bar along its foot.
class _CandidateButton extends Button:
	var score := 0.0
	var state := "free"

	func _draw() -> void:
		var bar := Rect2(Vector2(8, size.y - 5), Vector2(size.x - 16, 3))
		draw_rect(bar, LakskongenTheme.palette(self, &"sunken"))
		draw_rect(Rect2(bar.position, Vector2(bar.size.x * clampf(score, 0.0, 1.0), bar.size.y)), GeoRegionMath.ramp(score))
		if state == "yours" or state == "suggested":
			draw_rect(Rect2(Vector2(0, 4), Vector2(3, size.y - 8)), GeoRegionView.SALMON)
