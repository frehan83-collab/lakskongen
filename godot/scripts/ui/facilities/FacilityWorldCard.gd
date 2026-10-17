class_name FacilityWorldCard
extends PanelContainer

## The anchored card of a land campus or an offshore unit (roadmap F2-20;
## farm-types spec §6.3 "Construction shows as world state with a progress
## ring on the anchored card, never as a percentage in a list row"). Opened by
## a click on a tank or a unit in the 3D world (FarmFacilityVisualManager), it
## follows the clicked point on screen, clamped into the viewport, and hides
## while that point is behind the camera.
##
## It shows the unit's name, its family's variant and its plot, the clicked
## tank (and its lot or module), and either the construction phase with a
## progress ring or, once built, its fish and biomass. Every number is the
## engine's (SimBridge.get_facility_visuals); every word a translation key.
## Esc or the close button closes it. Built in code, like DriftsplanCard.

signal closed

## Screen gap (px) between the anchor and the card's bottom edge.
const ANCHOR_GAP_PX := 18.0
## Margin (px) the card keeps from the viewport's edges.
const EDGE_PX := 12.0
## The ring: diameter and stroke (px).
const RING_PX := 46.0
const RING_STROKE_PX := 5.0
## Ink palette (roadmap D13): the ring's track and fill.
const RING_TRACK := Color(0.624, 0.706, 0.733, 0.35)
const RING_FILL := Color(0.549, 0.780, 0.855)

var unit_id := ""
var tank_index := -1
## The world point the card hangs from (set by the presenter each frame it moves).
var anchor := Vector3.ZERO

var _title: Label
var _subtitle: Label
var _detail: Label
var _status: Label
var _ring: Control
var _ring_value := 0.0
var _close: Button


func _init() -> void:
	name = "FacilityWorldCard"
	theme_type_variation = &"Card"
	mouse_filter = Control.MOUSE_FILTER_STOP
	custom_minimum_size = Vector2(300.0, 0.0)
	var pad := MarginContainer.new()
	for side in ["left", "right", "top", "bottom"]:
		pad.add_theme_constant_override("margin_" + side, 14)
	add_child(pad)
	var col := VBoxContainer.new()
	col.add_theme_constant_override("separation", 6)
	pad.add_child(col)
	var head := HBoxContainer.new()
	head.add_theme_constant_override("separation", 10)
	col.add_child(head)
	_title = Label.new()
	_title.name = "Title"
	_title.theme_type_variation = &"Subheading"
	_title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_title.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	head.add_child(_title)
	_close = Button.new()
	_close.name = "Close"
	_close.flat = true
	_close.focus_mode = Control.FOCUS_ALL
	_close.pressed.connect(close)
	head.add_child(_close)
	_subtitle = Label.new()
	_subtitle.name = "Subtitle"
	_subtitle.theme_type_variation = &"Caption"
	col.add_child(_subtitle)
	_detail = Label.new()
	_detail.name = "Detail"
	_detail.theme_type_variation = &"BodySmall"
	col.add_child(_detail)
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 12)
	col.add_child(row)
	_ring = Control.new()
	_ring.name = "ProgressRing"
	_ring.custom_minimum_size = Vector2(RING_PX, RING_PX)
	_ring.draw.connect(_draw_ring)
	row.add_child(_ring)
	_status = Label.new()
	_status.name = "Status"
	_status.theme_type_variation = &"BodySmall"
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_status.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_status.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	row.add_child(_status)
	visible = false


## Show the card for `view` (a get_facility_visuals row), the clicked tank
## (-1: the unit), the plot's name and the world point it hangs from.
func open(view: Dictionary, tank: int, plot_name: String, at: Vector3) -> void:
	unit_id = str(view.get("unit_id", ""))
	tank_index = tank
	anchor = at
	refresh(view, plot_name)
	visible = true
	_close.grab_focus()
	reposition()


## The card's text and ring from the unit's view now.
func refresh(view: Dictionary, plot_name: String) -> void:
	_title.text = str(view.get("name", unit_id))
	_close.text = tr("FACILITY_CARD_CLOSE")
	var variant := tr("FARM_VARIANT_" + str(view.get("variant", "")).to_upper())
	_subtitle.text = "%s · %s" % [variant, plot_name] if plot_name != "" else variant
	_detail.text = _tank_text(view)
	_detail.visible = _detail.text != ""
	var operational := bool(view.get("operational", false))
	var count := int(view.get("phase_count", 1))
	var index := mini(int(view.get("phase_index", 0)), count)
	if operational:
		_ring_value = 1.0
		var fish := SimFormatters.format_fish(int(view.get("fish_count", 0)))
		var biomass := SimFormatters.format_biomass(float(view.get("biomass_tonnes", 0.0)) * 1000.0)
		_status.text = "%s\n%s" % [tr("FACILITY_PHASE_OPERATIONAL"), tr("FACILITY_CARD_STOCK") % [fish, biomass]]
	else:
		# The whole build, phase by phase: the ring is the share done.
		_ring_value = clampf((float(index) + float(view.get("phase_progress", 0.0))) / float(maxi(count, 1)), 0.0, 1.0)
		var phase := tr("FACILITY_PHASE_" + str(view.get("phase_key", "")).to_upper())
		_status.text = "%s\n%s" % [phase, tr("FACILITY_CARD_PHASE") % [index + 1, count]]
	_ring.queue_redraw()


func _tank_text(view: Dictionary) -> String:
	if tank_index < 0:
		return ""
	var tank := tr("FACILITY_CARD_TANK") % (tank_index + 1)
	var per_lot := int(view.get("grow_out_tanks_per_lot", 0)) + int(view.get("pre_grow_tanks_per_lot", 0))
	if int(view.get("lots", 0)) > 0 and per_lot > 0:
		return "%s · %s" % [tank, tr("FACILITY_CARD_LOT") % (tank_index / per_lot + 1)]
	var per_module := int(view.get("tanks_per_module", 0))
	if int(view.get("modules", 0)) > 0 and per_module > 0:
		return "%s · %s" % [tank, tr("FACILITY_CARD_MODULE") % (tank_index / per_module + 1)]
	return tank


## The ring's share done (0-1).
func get_ring_value() -> float:
	return _ring_value


func _draw_ring() -> void:
	var c := _ring.size * 0.5
	var r := RING_PX * 0.5 - RING_STROKE_PX
	_ring.draw_arc(c, r, 0.0, TAU, 48, RING_TRACK, RING_STROKE_PX, true)
	if _ring_value > 0.0:
		_ring.draw_arc(c, r, -PI * 0.5, -PI * 0.5 + TAU * _ring_value, 48, RING_FILL, RING_STROKE_PX, true)


func close() -> void:
	if not visible:
		return
	visible = false
	unit_id = ""
	tank_index = -1
	closed.emit()


func _unhandled_input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		close()
		get_viewport().set_input_as_handled()


func _process(_delta: float) -> void:
	if visible:
		reposition()


## Hang the card above its anchor's screen point, inside the viewport; hide
## it while the anchor is behind the camera.
func reposition() -> void:
	var cam := get_viewport().get_camera_3d()
	if cam == null:
		return
	if cam.is_position_behind(anchor):
		modulate.a = 0.0
		return
	modulate.a = 1.0
	var p := cam.unproject_position(anchor)
	var vp := get_viewport().get_visible_rect().size
	var s := get_combined_minimum_size()
	size = s
	var x := clampf(p.x - s.x * 0.5, EDGE_PX, vp.x - s.x - EDGE_PX)
	var y := clampf(p.y - s.y - ANCHOR_GAP_PX, EDGE_PX, vp.y - s.y - EDGE_PX)
	position = Vector2(x, y)
