class_name FacilityWorldCard
extends PanelContainer

## The anchored card of a land campus or an offshore unit (roadmap F2-20;
## farm-types spec §6.3 "Construction shows as world state with a progress
## ring on the anchored card, never as a percentage in a list row"). Opened by
## a click on a tank or a unit in the 3D world (FarmFacilityVisualManager), it
## follows the clicked point on screen, clamped into the viewport, and hides
## while that point is behind the camera.
##
## It shows the unit's name, its variant and its plot, the clicked tank (its
## number, its lot or module, its fish), and either the construction phase
## under way (the engine's phase name, "phase n of m", why it waits or stands
## still) with a progress ring of the whole build, or, once built, its fish
## and biomass. Every number is the engine's (SimBridge.get_facility_visuals);
## every word a translation key. Esc or the close button closes it. Built in
## code, like DriftsplanCard.

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
## The clicked tank (a LandCampusBuilder tank: id, number, slot, k), or {}.
var tank: Dictionary = {}
## The world point the card hangs from.
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
## (`index` -1: the unit; `clicked` the campus's tank entry), the plot's
## name and the world point it hangs from.
func open(view: Dictionary, index: int, clicked: Dictionary, plot_name: String, at: Vector3) -> void:
	unit_id = str(view.get("unit_id", ""))
	tank_index = index
	tank = clicked
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
	var build: Dictionary = _build_of(view)
	var count := int(build.get("phase_count", 1))
	var index := mini(int(build.get("phase_index", 0)), count)
	if index >= count:
		_ring_value = 1.0
		var fish := SimFormatters.format_fish(int(view.get("fish_count", 0)))
		var biomass := SimFormatters.format_biomass(float(view.get("biomass_tonnes", 0.0)) * 1000.0)
		_status.text = "%s\n%s" % [tr("FACILITY_CARD_OPERATIONAL"), tr("FACILITY_CARD_STOCK") % [fish, biomass]]
	else:
		_ring_value = clampf(float(build.get("progress", 0.0)), 0.0, 1.0)
		var prefix := "LAND_PHASE_" if str(view.get("family", "")) == "land" else "BUILD_SEA_PHASE_"
		var lines: Array[String] = [tr(prefix + str(build.get("phase_key", "")).to_upper()),
			tr("FACILITY_CARD_PHASE") % [index + 1, count]]
		var waiting := str(build.get("waiting_reason", ""))
		if waiting != "":
			lines.append(tr("FACILITY_CARD_WAITING_" + waiting.to_upper()))
		elif bool(build.get("suspended", false)):
			lines.append(tr("FACILITY_CARD_SUSPENDED"))
		_status.text = "\n".join(lines)
	_ring.queue_redraw()


## The build the card shows: the clicked tank's lot's, else the unit's own.
func _build_of(view: Dictionary) -> Dictionary:
	var lots: Array = view.get("lots", [])
	if tank_index >= 0 and int(tank.get("slot", -1)) >= 0 and int(tank.get("slot", -1)) < lots.size():
		return (lots[int(tank["slot"])] as Dictionary).get("build", {})
	return view.get("build", {})


func _tank_text(view: Dictionary) -> String:
	if tank_index < 0 or tank.is_empty():
		return ""
	var parts: Array[String] = [tr("FACILITY_CARD_TANK") % int(tank.get("number", tank_index + 1))]
	var slot := int(tank.get("slot", 0))
	parts.append((tr("FACILITY_CARD_MODULE") if str(view.get("variant", "")) == "post_smolt" else tr("FACILITY_CARD_LOT")) % (slot + 1))
	var lots: Array = view.get("lots", [])
	if slot < lots.size():
		var rows: Array = (lots[slot] as Dictionary).get("tanks", [])
		var k := int(tank.get("k", -1))
		if k >= 0 and k < rows.size() and bool((rows[k] as Dictionary).get("stocked", false)):
			parts.append(tr("FACILITY_CARD_TANK_FISH") % SimFormatters.format_fish(int((rows[k] as Dictionary).get("fish_count", 0))))
	return " · ".join(parts)


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
	tank = {}
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
