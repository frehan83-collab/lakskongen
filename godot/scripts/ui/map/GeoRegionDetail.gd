class_name GeoRegionDetail
extends VBoxContainer
## The detail column at the region level (geo stages 2-3 plan Track D): the
## company's own places in the area (its sites and, when it lies on this
## map, the open-sea plot hav_1 is bound to), who farms the area
## (RivalPresenceList), and the area's 12 open-pen candidates as focusable
## buttons in rank order, each with its score bar and state. They are the
## keyboard's way to every card the map opens: Up and Down cycle within a
## list, Tab moves on, Enter opens the card. A disabled button says why in
## its tooltip.

signal candidate_chosen(row: Dictionary)
signal player_site_chosen(row: Dictionary)
signal offshore_chosen(row: Dictionary)

var _yours_header: Label
var _yours: VBoxContainer
var _rivals: RivalPresenceList
var _header: Label
var _list: VBoxContainer
var _note: Label


func _ready() -> void:
	name = "GeoRegionDetail"
	add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)
	_yours_header = Label.new()
	_yours_header.name = "YoursHeader"
	_yours_header.theme_type_variation = &"Overline"
	_yours_header.text = tr("MAP_REGION_YOURS_HEADER")
	add_child(_yours_header)
	_yours = VBoxContainer.new()
	_yours.name = "YoursList"
	_yours.add_theme_constant_override("separation", 2)
	add_child(_yours)
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


## Show an area: the company's places on the view's map, its rival summary
## and its candidate rows (rank order).
func show_area(summary: Dictionary, rows: Array, view: GeoRegionView) -> void:
	_show_yours(view)
	_rivals.show_summary(summary)
	show_candidates(rows, view)


## Rebuild the candidate buttons (states change on a build or a licence).
func show_candidates(rows: Array, view: GeoRegionView) -> void:
	var focused_name := _focused_child_name(_list)
	_clear(_list)
	var buttons: Array[Button] = []
	for row: Dictionary in rows:
		var b := _CandidateButton.new()
		b.name = "MapCandidate_" + String(row.get("id", ""))
		b.score = float(row.get("score", 0.0))
		b.state = view.state_of(row)
		b.text = tr("MAP_REGION_CANDIDATE_BUTTON") % [int(row.get("rank", 0)), String(row.get("label", "")),
			SimFormatters.num(float(row.get("score", 0.0)), 3), tr(GeoRegionView.STATE_KEYS[b.state])]
		b.tooltip_text = tr("MAP_REGION_CANDIDATE_BUTTON_TIP") % String(row.get("label", ""))
		_style_row(b)
		b.pressed.connect(func() -> void: candidate_chosen.emit(row))
		_list.add_child(b)
		buttons.append(b)
	_cycle(buttons)
	_refocus(_list, focused_name)


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


## The row of one of the company's sites, or null.
func site_button(site_id: String) -> Button:
	return _yours.get_node_or_null("MapSiteRow_" + site_id.validate_node_name()) as Button


## The row of the open-sea plot hav_1 is bound to, or null (not on this map).
func hav_button() -> Button:
	return _yours.get_node_or_null("MapHavRow") as Button


## The company's places shown, in order (site rows, then hav_1's).
func yours_buttons() -> Array[Button]:
	var out: Array[Button] = []
	for c in _yours.get_children():
		if c is Button:
			out.append(c)
	return out


# ── Internals ─────────────────────────────────────────────────────────────────

func _show_yours(view: GeoRegionView) -> void:
	var focused_name := _focused_child_name(_yours)
	_clear(_yours)
	var buttons: Array[Button] = []
	for row: Dictionary in view.player_sites():
		var site := String(row.get("site_label", ""))
		var b := Button.new()
		b.name = "MapSiteRow_" + String(row.get("site_id", "")).validate_node_name()
		b.text = tr("MAP_REGION_SITE_ROW") % [site, String(row.get("label", ""))]
		b.tooltip_text = tr("MAP_REGION_CANDIDATE_BUTTON_TIP") % site
		_style_row(b)
		b.pressed.connect(func() -> void: player_site_chosen.emit(row))
		_yours.add_child(b)
		buttons.append(b)
	for row: Dictionary in view.offshore():
		if String(row.get("bound_plot_id", "")) == "":
			continue
		var b := Button.new()
		b.name = "MapHavRow"
		b.text = tr("MAP_REGION_HAV_ROW") % String(row.get("area", ""))
		b.tooltip_text = tr("MAP_REGION_CANDIDATE_BUTTON_TIP") % String(row.get("area", ""))
		_style_row(b)
		b.pressed.connect(func() -> void: offshore_chosen.emit(row))
		_yours.add_child(b)
		buttons.append(b)
	_yours_header.visible = not buttons.is_empty()
	_yours.visible = not buttons.is_empty()
	_cycle(buttons)
	_refocus(_yours, focused_name)


func _style_row(b: Button) -> void:
	b.alignment = HORIZONTAL_ALIGNMENT_LEFT
	b.theme_type_variation = &"ListItemButton"
	b.custom_minimum_size = Vector2(0, 34)
	b.focus_mode = Control.FOCUS_ALL


## Up and Down cycle through a list, the last wrapping to the first; Tab
## keeps its own order, so the keyboard can leave the list.
static func _cycle(buttons: Array[Button]) -> void:
	for i in buttons.size():
		var prev := buttons[(i - 1 + buttons.size()) % buttons.size()]
		var next := buttons[(i + 1) % buttons.size()]
		buttons[i].focus_neighbor_top = buttons[i].get_path_to(prev)
		buttons[i].focus_neighbor_bottom = buttons[i].get_path_to(next)


func _focused_child_name(list: Control) -> String:
	var focused := get_viewport().gui_get_focus_owner() if is_inside_tree() else null
	return String(focused.name) if focused != null and list.is_ancestor_of(focused) else ""


static func _clear(list: Control) -> void:
	for c in list.get_children():
		list.remove_child(c)
		c.queue_free()


static func _refocus(list: Control, focused_name: String) -> void:
	if focused_name == "":
		return
	var again := list.get_node_or_null(focused_name) as Control
	if again != null:
		again.grab_focus.call_deferred()


## A candidate's button: its text, and the score bar along its foot.
class _CandidateButton extends Button:
	var score := 0.0
	var state := "free"

	func _draw() -> void:
		var bar := Rect2(Vector2(8, size.y - 5), Vector2(size.x - 16, 3))
		draw_rect(bar, LakskongenTheme.palette(self, &"sunken"))
		draw_rect(Rect2(bar.position, Vector2(bar.size.x * clampf(score, 0.0, 1.0), bar.size.y)), GeoRegionMath.ramp(score))
		if state == "yours" or state == "suggested":
			draw_rect(Rect2(Vector2(0, 4), Vector2(3, size.y - 8)), GeoRegionIcons.SALMON)
