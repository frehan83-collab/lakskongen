class_name BuildSiteDialog
extends Control

## "Bygg lokalitet" (roadmap U2-01): what a new locality costs, holds and adds
## before the player commits, from the engine's build plan
## (SimBridge.plan_site_build): the cost against the cash, the running cost
## it adds per month, the locality's own MTB, the 0 t of licence MTB a build
## brings, and the pens of the standard mooring frame, drawn. Confirming
## builds through SimBridge.build_expansion_site; a refusal is shown as text.
##
## The dialog is the future home of the family-first build flow
## (docs/FARM_TYPES_SPEC.md §6.1-6.2, F2-31): its first step is "Anleggstype".
## Sjø is the working family; Land and Hav say where those builds live today
## (the RAS and Offshore tabs) and nothing else: no figures until the engine's
## farm-type catalogue (F2-04) supplies them.

signal built(site_id: String, zone_id: String)
signal view_site_requested(site_id: String)
signal tab_requested(tab_id: String)
signal closed

const FAMILIES := [
	{"id": "sea", "title": "EXPANSION_FAMILY_SEA", "body": "EXPANSION_FAMILY_SEA_BODY", "tab": ""},
	{"id": "land", "title": "EXPANSION_FAMILY_LAND", "body": "EXPANSION_FAMILY_LAND_BODY", "tab": "ras", "button": "EXPANSION_FAMILY_LAND_BUTTON"},
	{"id": "offshore", "title": "EXPANSION_FAMILY_OFFSHORE", "body": "EXPANSION_FAMILY_OFFSHORE_BODY", "tab": "offshore", "button": "EXPANSION_FAMILY_OFFSHORE_BUTTON"},
]

@onready var _dialog: PanelContainer = %Dialog
@onready var _overline: Label = %Overline
@onready var _title: Label = %TitleLabel
@onready var _subtitle: Label = %SubtitleLabel
@onready var _close_btn: Button = %CloseBtn
@onready var _family_header: Label = %FamilyHeader
@onready var _family_row: HBoxContainer = %FamilyRow
@onready var _preview_header: Label = %PreviewHeader
@onready var _preview_row: HBoxContainer = %PreviewRow
@onready var _grid_slot: PanelContainer = %GridSlot
@onready var _ledger: GridContainer = %Ledger
@onready var _explain_callout: PanelContainer = %ExplainCallout
@onready var _explain: Label = %ExplainLabel
@onready var _verdict: Label = %VerdictLabel
@onready var _result_box: VBoxContainer = %ResultBox
@onready var _result_title: Label = %ResultTitle
@onready var _result_body: Label = %ResultBody
@onready var _hint: Label = %HintLabel
@onready var _cancel_btn: Button = %CancelBtn
@onready var _confirm_btn: Button = %ConfirmBtn

var _sim: Node = null
var _zone_id := ""
var _plan: Dictionary = {}
var _built_site_id := ""
var _pen_grid: PenGridPreview = null
## Answers whether a tab may be opened (GameUI's tutorial gate); set by the host.
var _tab_probe: Callable = func(_tab: String) -> bool: return true


func _ready() -> void:
	hide()
	_sim = get_node_or_null("/root/SimBridge")
	_overline.text = tr("EXPANSION_BUILD_OVERLINE")
	_family_header.text = tr("EXPANSION_FAMILY_HEADER")
	_preview_header.text = tr("EXPANSION_PREVIEW_HEADER")
	_close_btn.tooltip_text = tr("UI_CLOSE")
	_close_btn.pressed.connect(close)
	_cancel_btn.pressed.connect(close)
	_confirm_btn.pressed.connect(_on_confirm)
	_pen_grid = PenGridPreview.new()
	_pen_grid.name = "PenGrid"
	_pen_grid.custom_minimum_size = Vector2(260, 180)
	_grid_slot.add_child(_pen_grid)


## The host's tutorial gate for the Land and Hav cards' tab links.
func set_tab_probe(probe: Callable) -> void:
	_tab_probe = probe


## Open the dialog for a zone (the plan is re-read from the engine).
func open_for_zone(zone_id: String) -> void:
	_zone_id = zone_id
	_built_site_id = ""
	show()
	_refresh()
	JuiceEffects.panel_enter(_dialog)
	if _confirm_btn.disabled:
		_cancel_btn.grab_focus.call_deferred()
	else:
		_confirm_btn.grab_focus.call_deferred()


func close() -> void:
	if not visible:
		return
	hide()
	closed.emit()


func zone_id() -> String:
	return _zone_id


## The plan the dialog shows (the bridge's dictionary).
func plan() -> Dictionary:
	return _plan


func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		close()
		get_viewport().set_input_as_handled()


func _refresh() -> void:
	if _sim == null:
		return
	_plan = _sim.plan_site_build(_zone_id)
	var area := int(_plan.get("production_area", 0))
	_title.text = tr("EXPANSION_BUILD_TITLE") % SimLabels.zone(_zone_id)
	var where := tr("EXPANSION_BUILD_FIRST_SITE")
	if bool(_plan.get("new_zone", false)):
		where = tr("EXPANSION_BUILD_NEW_ZONE")
	elif int(_plan.get("player_sites_in_zone", 0)) > 0:
		where = tr("EXPANSION_BUILD_YOUR_ZONE")
	_subtitle.text = tr("EXPANSION_BUILD_SUBTITLE") % [area, where]
	_build_families()
	_build_ledger()
	_pen_grid.pens = int(_plan.get("pens", 0))
	_pen_grid.queue_redraw()
	_explain.text = tr("EXPANSION_BUILD_EXPLAIN") % [SimFormatters.num(float(_plan.get("area_licence_mtb_t", 0.0))), area]
	var built_now := _built_site_id != ""
	_result_box.visible = built_now
	_family_header.visible = not built_now
	_family_row.visible = not built_now
	var allowed := bool(_plan.get("allowed", false))
	var reason := "" if allowed else tr(str(_plan.get("label_key", "")))
	if str(_plan.get("reason_key", "")) == "insufficient_funds":
		# "Ikke nok penger: dette koster 6,00 MNOK, og du har 4,00 MNOK."
		reason = UIPolish.funds_blocker(float(_plan.get("cost_nok", 0.0)), float(_plan.get("balance_nok", 0.0)))
	_verdict.text = reason
	_verdict.visible = reason != "" and not built_now
	if built_now:
		_confirm_btn.text = tr("EXPANSION_BUILD_VIEW_BUTTON")
		_cancel_btn.text = tr("UI_CLOSE")
		UIPolish.set_available(_confirm_btn, "")
		_hint.text = ""
		return
	_cancel_btn.text = tr("EXPANSION_CANCEL")
	_confirm_btn.text = tr("EXPANSION_BUILD_CONFIRM") % SimFormatters.format_nok(float(_plan.get("cost_nok", 0.0)))
	UIPolish.set_available(_confirm_btn, reason)
	_hint.text = tr("EXPANSION_BUILD_HINT")


# ── Step 1: the farm family ───────────────────────────────────────────────────

func _build_families() -> void:
	for child in _family_row.get_children():
		_family_row.remove_child(child)
		child.queue_free()
	for family in FAMILIES:
		_family_row.add_child(_family_card(family))


func _family_card(family: Dictionary) -> Control:
	var selected := str(family["id"]) == "sea"
	var card := PanelContainer.new()
	card.name = "Family_" + str(family["id"])
	card.theme_type_variation = &"KpiCardAccent" if selected else &"Tile"
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	card.custom_minimum_size = Vector2(0, 118)
	var col := VBoxContainer.new()
	col.add_theme_constant_override("separation", 6)
	card.add_child(col)
	var head := HBoxContainer.new()
	head.add_theme_constant_override("separation", 8)
	var title := Label.new()
	title.text = tr(str(family["title"]))
	title.theme_type_variation = &"Subheading" if selected else &"SubheadingMuted"
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	head.add_child(title)
	if selected:
		var chip := Label.new()
		chip.text = tr("EXPANSION_FAMILY_SELECTED")
		chip.theme_type_variation = &"TagInfo"
		head.add_child(chip)
	col.add_child(head)
	var body := Label.new()
	body.text = tr(str(family["body"]))
	body.theme_type_variation = &"BodySmall" if selected else &"BodySmallMuted"
	body.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	body.custom_minimum_size = Vector2(200, 0)
	col.add_child(body)
	var tab := str(family.get("tab", ""))
	if tab != "":
		var btn := Button.new()
		btn.name = "OpenTab"
		btn.text = tr(str(family["button"]))
		btn.theme_type_variation = &"SmallGhostButton"
		btn.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
		btn.pressed.connect(func() -> void:
			close()
			tab_requested.emit(tab))
		var open := bool(_tab_probe.call(tab))
		UIPolish.set_available(btn, "" if open else tr("EXPANSION_FAMILY_TAB_LOCKED"))
		col.add_child(btn)
		if not open:
			var locked := Label.new()
			locked.name = "Locked"
			locked.text = tr("EXPANSION_FAMILY_TAB_LOCKED")
			locked.theme_type_variation = &"SmallMuted"
			col.add_child(locked)
	return card


# ── Step 2: the preview ───────────────────────────────────────────────────────

func _build_ledger() -> void:
	for child in _ledger.get_children():
		_ledger.remove_child(child)
		child.queue_free()
	var cost := float(_plan.get("cost_nok", 0.0))
	var balance := float(_plan.get("balance_nok", 0.0))
	var after := balance - cost
	var area := int(_plan.get("production_area", 0))
	var area_mtb := float(_plan.get("area_licence_mtb_t", 0.0))
	_row("Cost", "EXPANSION_LEDGER_COST", SimFormatters.format_nok(cost), &"Figure")
	_row("Cash", "EXPANSION_LEDGER_CASH", "%s  →  %s" % [SimFormatters.format_nok(balance), SimFormatters.format_nok(after)],
		&"FigureCrit" if after < 0.0 else &"Figure")
	_row("Running", "EXPANSION_LEDGER_RUNNING", tr("EXPANSION_LEDGER_RUNNING_VALUE") % [
		SimFormatters.format_nok(float(_plan.get("running_cost_per_month_nok", 0.0))), int(round(float(_plan.get("workers", 0.0))))], &"Figure")
	_row("Locality", "EXPANSION_LEDGER_LOCALITY_MTB", tr("EXPANSION_TONNES") % SimFormatters.num(float(_plan.get("locality_mtb_t", 0.0))), &"Figure")
	_row("Licence", "EXPANSION_LEDGER_LICENCE_MTB", tr("EXPANSION_LEDGER_LICENCE_VALUE") % [
		SimFormatters.num(area_mtb), SimFormatters.num(area_mtb + float(_plan.get("licence_mtb_added_t", 0.0))),
		SimFormatters.num(float(_plan.get("licence_mtb_added_t", 0.0)))], &"Figure", area)
	_row("Pens", "EXPANSION_LEDGER_PENS", tr("EXPANSION_LEDGER_PENS_VALUE") % [
		int(_plan.get("pens", 0)), SimFormatters.num(float(_plan.get("pen_circumference_m", 0.0))),
		SimFormatters.num(float(_plan.get("pen_net_depth_m", 0.0))),
		SimFormatters.num(float(_plan.get("pen_volume_m3", 0.0)))], &"Figure")


func _row(id: String, key: String, value: String, variation: StringName, area: int = -1) -> void:
	var k := Label.new()
	k.name = "Key" + id
	k.text = tr(key) % area if area >= 0 else tr(key)
	k.theme_type_variation = &"OverlineMuted"
	k.uppercase = true
	k.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	_ledger.add_child(k)
	var v := Label.new()
	v.name = "Value" + id
	v.text = value
	v.theme_type_variation = variation
	v.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	v.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_ledger.add_child(v)


# ── Confirm ───────────────────────────────────────────────────────────────────

func _on_confirm() -> void:
	if _sim == null:
		return
	if _built_site_id != "":
		var site := _built_site_id
		close()
		view_site_requested.emit(site)
		return
	var result: Dictionary = _sim.build_expansion_site(_zone_id)
	if not bool(result.get("success", false)):
		_refresh()
		_verdict.text = tr(str(result.get("label_key", "EXPANSION_REASON_ENGINE_NOT_READY")))
		_verdict.visible = true
		return
	_built_site_id = str(result.get("site_id", ""))
	_result_title.text = tr("EXPANSION_BUILD_DONE_TITLE") % str(result.get("site_label", ""))
	_result_body.text = tr("EXPANSION_BUILD_DONE_BODY") % int(_plan.get("pens", 0))
	_refresh()
	JuiceEffects.panel_enter(_dialog)
	built.emit(_built_site_id, _zone_id)


## The standard mooring frame seen from above: the pens in two rows inside the
## grid of mooring lines, with the feed barge at the end, in the chart's
## colours. Purely illustrative of the pen count the engine builds.
class PenGridPreview extends Control:
	var pens := 8

	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_IGNORE
		resized.connect(queue_redraw)

	func _draw() -> void:
		if pens <= 0 or size.x < 40.0 or size.y < 40.0:
			return
		var water := LakskongenTheme.palette(self, &"info")
		var line := LakskongenTheme.palette(self, &"stroke")
		var collar := LakskongenTheme.palette(self, &"text")
		var buoy := Color("#E8B92A")
		var rows := 2 if pens > 3 else 1
		var cols := int(ceil(float(pens) / rows))
		var barge_w := 26.0
		var pad := 14.0
		var cell := minf((size.x - pad * 2.0 - barge_w - 10.0) / cols, (size.y - pad * 2.0) / rows)
		var origin := Vector2((size.x - cell * cols - barge_w - 10.0) * 0.5, (size.y - cell * rows) * 0.5)
		draw_rect(Rect2(origin, Vector2(cell * cols, cell * rows)), Color(water, 0.10), true)
		for r in rows + 1:
			draw_line(origin + Vector2(0, r * cell), origin + Vector2(cols * cell, r * cell), Color(line, 0.7), 1.0)
		for c in cols + 1:
			draw_line(origin + Vector2(c * cell, 0), origin + Vector2(c * cell, rows * cell), Color(line, 0.7), 1.0)
		for r in rows + 1:
			for c in cols + 1:
				draw_circle(origin + Vector2(c * cell, r * cell), 2.5, buoy)
		var n := 0
		for r in rows:
			for c in cols:
				if n >= pens:
					break
				var center := origin + Vector2((c + 0.5) * cell, (r + 0.5) * cell)
				var radius := cell * 0.36
				draw_circle(center, radius, Color(water, 0.28))
				draw_arc(center, radius, 0.0, TAU, 40, collar, 2.5, true)
				n += 1
		var barge := Rect2(origin + Vector2(cols * cell + 10.0, rows * cell * 0.5 - 18.0), Vector2(barge_w, 36.0))
		draw_rect(barge, collar, true)
		draw_rect(barge.grow(-5.0), Color.WHITE, true)
