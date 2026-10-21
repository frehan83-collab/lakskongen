class_name MapMarker
extends PanelContainer
## One production area's plate on the national map (geo stages 2-3 plan,
## Track C): a Chart-paper plate with the engine's traffic light as a badge,
## the area's code and, when there is room, its name in a child Label named
## "NameLabel". The plate sits where MapLabelPlacer put it; its anchor dot
## marks the area's label point (label_25833) and a leader line joins the
## two when they are apart. The company's farms in the area ring the dot.
## Where a cluster badge covers the label point, the badge marks it: no dot
## over the numeral, the line stops at the badge's rim and the ring goes
## round the badge (set_cover, GEO-VIS).
##
## MapPanel keeps the plates in _region_nodes ("po01".."po13"); a click
## anywhere on the plate selects the area (pressed), a double-click or Enter
## on the focused plate asks to explore it (drill_requested, Track D).

signal pressed(area: int)
signal drill_requested(area: int)

enum { STATE_NORMAL, STATE_HOVERED, STATE_SELECTED }

## The dot's radius; MapLabelPlacer.ANCHOR_CLEAR_PX keeps plates clear of it.
const DOT_RADIUS := 4.0
const RING_RADIUS := 7.5

var area: int = 0
var region_id: String = ""
## The area's label point, in the parent's (the map's) coordinates.
var anchor := Vector2.ZERO
var leader := false
var state: int = STATE_NORMAL
## The engine's traffic light for the area (the badge), from the overview.
var has_light := false
var light_color := Color.GRAY
## The company's farms in the area ring the dot.
var player_sites: int = 0
var ring_color := Color.WHITE
## A data overlay (SiteOverlayLegend) tints the plate's left edge.
var overlay_active := false
var overlay_color := Color.GRAY
var compact := false
## A cluster badge over the anchor (GEO-VIS), in the parent's coordinates:
## the leader stops at the badge's rim, no diamond hides its numeral, and the
## company's ring goes round the badge. cover_radius 0: nothing covers it.
var cover_centre := Vector2.ZERO
var cover_radius := 0.0
var cover_ring_radius := 0.0

var _code: Label = null
var _name: Label = null
var _badge: Control = null
var _hovered := false


## A plate for production area `area_id` named `area_name`.
func setup(area_id: int, area_name: String) -> void:
	area = area_id
	region_id = "po%02d" % area_id
	name = "Plate_" + region_id
	mouse_filter = Control.MOUSE_FILTER_STOP
	focus_mode = Control.FOCUS_ALL
	mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	theme_type_variation = &"CardCompact"
	var row := HBoxContainer.new()
	row.name = "Row"
	row.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_theme_constant_override("separation", 6)
	add_child(row)
	_badge = _Badge.new()
	_badge.name = "Badge"
	_badge.custom_minimum_size = Vector2(10, 10)
	_badge.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	_badge.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_child(_badge)
	_code = Label.new()
	_code.name = "CodeLabel"
	_code.theme_type_variation = &"BodySmallStrong"
	_code.text = tr("MAP_GEO_PLATE_CODE") % area_id
	_code.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_child(_code)
	_name = Label.new()
	_name.name = "NameLabel"
	_name.theme_type_variation = &"BodySmall"
	_name.text = area_name
	_name.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_child(_name)
	focus_entered.connect(refresh)
	focus_exited.connect(refresh)
	mouse_entered.connect(func() -> void: _set_hovered(true))
	mouse_exited.connect(func() -> void: _set_hovered(false))
	refresh()


## Code only (compact) or code and name.
func set_compact(on: bool) -> void:
	compact = on
	if _name != null:
		_name.visible = not on
	reset_size()


## The engine's light and the company's farms here; redraws only on a change.
func set_status(light_on: bool, light: Color, sites: int, ring: Color) -> bool:
	if light_on == has_light and light == light_color and sites == player_sites and ring == ring_color:
		return false
	has_light = light_on
	light_color = light
	player_sites = sites
	ring_color = ring
	refresh()
	return true


## The data overlay's tint (active = false: none); redraws only on a change.
func set_overlay(active: bool, color: Color) -> bool:
	if active == overlay_active and (not active or color == overlay_color):
		return false
	overlay_active = active
	overlay_color = color
	refresh()
	return true


## The badge drawn over the anchor (radius 0: none) and where the company's
## ring goes round it; redraws only on a change.
func set_cover(centre: Vector2, radius: float, ring_radius: float) -> bool:
	if centre == cover_centre and is_equal_approx(radius, cover_radius) and is_equal_approx(ring_radius, cover_ring_radius):
		return false
	cover_centre = centre
	cover_radius = radius
	cover_ring_radius = ring_radius
	queue_redraw()
	return true


## Is the anchor's diamond drawn (no badge covers the anchor)?
func draws_dot() -> bool:
	return cover_radius <= 0.0


## The plate's size for the placer, in its current mode.
func plate_size() -> Vector2:
	return get_combined_minimum_size()


## Re-apply the state (selection, hover, focus, overlay) to the plate's style.
func refresh() -> void:
	add_theme_stylebox_override("panel", _style())
	if _badge != null:
		_badge.queue_redraw()
	queue_redraw()


## The anchor dot's rect in the parent's coordinates.
func dot_rect() -> Rect2:
	var r := MapLabelPlacer.ANCHOR_CLEAR_PX
	return Rect2(anchor - Vector2(r, r), Vector2(r, r) * 2.0)


func _set_hovered(on: bool) -> void:
	_hovered = on
	refresh()


func _style() -> StyleBoxFlat:
	var sb := StyleBoxFlat.new()
	sb.bg_color = Color(MapPalette.chart("raised"), 0.97)
	sb.set_corner_radius_all(4)
	sb.content_margin_left = 7.0
	sb.content_margin_right = 9.0
	sb.content_margin_top = 3.0
	sb.content_margin_bottom = 3.0
	sb.shadow_color = Color(0.0, 0.0, 0.0, 0.35)
	sb.shadow_size = 5
	sb.shadow_offset = Vector2(0.0, 1.5)
	sb.border_color = Color(MapPalette.chart("hairline"), 0.9)
	sb.set_border_width_all(1)
	if state == STATE_SELECTED:
		sb.border_color = MapPalette.chart("accent")
		sb.set_border_width_all(2)
	elif has_focus():
		sb.border_color = MapPalette.chart("focus")
		sb.set_border_width_all(2)
	elif _hovered or state == STATE_HOVERED:
		sb.border_color = MapPalette.chart("info")
	if overlay_active:
		sb.border_width_left = 5
		sb.border_color = overlay_color if state != STATE_SELECTED else sb.border_color
		sb.content_margin_left = 9.0
	return sb


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		if event.double_click:
			drill_requested.emit(area)
		else:
			pressed.emit(area)
		accept_event()
	elif event.is_action_pressed("ui_accept"):
		pressed.emit(area)
		drill_requested.emit(area)
		accept_event()


## The anchor pin and the leader line, drawn outside the plate: a small
## light diamond on the area's label point (accent when selected), a stem to
## the plate's nearest edge, and the salmon ring of the company's farms.
func _draw() -> void:
	var local := anchor - position
	var deep := MapPalette.deep_water()
	var light := MapPalette.snow()
	var end := Vector2(clampf(local.x, 0.0, size.x), clampf(local.y, 0.0, size.y))
	var covered := cover_radius > 0.0
	var centre := cover_centre - position
	# A badge over the anchor: the stem starts at its rim, clear of the numeral.
	var start := rim_point(local, end, centre, cover_radius) if covered else local
	if start.distance_to(end) > 2.0:
		draw_line(start, end, Color(deep, 0.85), 3.0, true)
		draw_line(start, end, Color(light, 0.95), 1.25, true)
	if player_sites > 0:
		var at := centre if covered else local
		var ring := cover_ring_radius if covered else RING_RADIUS
		draw_arc(at, ring, 0.0, TAU, 40, Color(deep, 0.85), 4.0, true)
		draw_arc(at, ring, 0.0, TAU, 40, ring_color, 2.25, true)
	if covered:
		return
	var sel := state == STATE_SELECTED
	var r := DOT_RADIUS + (1.0 if sel else 0.0)
	draw_colored_polygon(_diamond(local, r + 1.5), deep)
	draw_colored_polygon(_diamond(local, r), MapPalette.chart("accent") if sel else light)


## Where the segment from `from` (inside the circle) towards `to` leaves the
## circle at `centre` with `radius`; `to` itself when it lies inside.
static func rim_point(from: Vector2, to: Vector2, centre: Vector2, radius: float) -> Vector2:
	var d := to - from
	var a := d.length_squared()
	if a <= 0.0 or centre.distance_to(to) <= radius:
		return to
	var f := from - centre
	var b := 2.0 * f.dot(d)
	var c := f.length_squared() - radius * radius
	var disc := b * b - 4.0 * a * c
	if disc < 0.0:
		return from
	var t := (-b + sqrt(disc)) / (2.0 * a)
	return from + d * clampf(t, 0.0, 1.0)


static func _diamond(c: Vector2, r: float) -> PackedVector2Array:
	return PackedVector2Array([c + Vector2(0, -r), c + Vector2(r, 0), c + Vector2(0, r), c + Vector2(-r, 0)])


## The traffic-light badge: a disc in the engine's light, a hairline ring.
class _Badge extends Control:
	func _draw() -> void:
		var m := get_parent().get_parent() as MapMarker
		var c := size * 0.5
		var r := minf(size.x, size.y) * 0.5
		draw_circle(c, r, Color(MapPalette.chart("text"), 0.55))
		draw_circle(c, r - 1.0, m.light_color if m != null and m.has_light else MapPalette.chart("hairline"))
