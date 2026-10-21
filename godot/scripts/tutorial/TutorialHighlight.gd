class_name TutorialHighlight
extends Control

## TutorialHighlight — the first season's pointer (roadmap U2-02): a salmon
## ring around the control the current step asks for, drawn over the game
## (the host keeps it the last child of the event layer, above the planner
## and the report sheets) and never taking a click. A small chevron above or
## below the ring points at it; a control scrolled out of its list gets a
## chevron on the list's edge instead, pointing the way to scroll. "Vis meg"
## (emphasize) sends a ring in from wide to the target once. Reduced motion
## holds the ring still.

## Ring around the target, outside its edge (px).
const RING_GAP := 6.0
const RING_WIDTH := 3.0
const GLOW_WIDTH := 9.0
const CORNER_RADIUS := 8.0
## Breathing of the ring (seconds per cycle, px of extra gap at the peak).
const PULSE_S := 1.4
const PULSE_PX := 4.0
## The chevron beside the ring.
const CHEVRON_SIZE := 12.0
const CHEVRON_GAP := 8.0
## "Vis meg": the ring flies in from this many px wide, over this long.
const EMPHASIZE_FROM_PX := 120.0
const EMPHASIZE_S := 0.6

var _target: Control = null
var _route := false
var _time := 0.0
var _emphasis := 0.0   # 1 → 0 while "Vis meg" plays


func _ready() -> void:
	name = "TutorialHighlight"
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	visible = false


## Point at `control` ("route" = the way to the target, e.g. its tab). Null clears.
func point_at(control: Control, route: bool = false) -> void:
	if control != _target:
		_time = 0.0
	_target = control
	_route = route
	visible = control != null
	queue_redraw()


func clear() -> void:
	point_at(null)


## "Vis meg": a ring flies in to the target once.
func emphasize() -> void:
	_emphasis = 1.0
	queue_redraw()


func target() -> Control:
	return _target if is_pointing() else null


func is_pointing() -> bool:
	return visible and _target != null and is_instance_valid(_target) and _target.is_visible_in_tree()


## The ring's rectangle on screen now (Rect2() when not pointing).
func ring_rect() -> Rect2:
	if not is_pointing():
		return Rect2()
	return _target.get_global_rect().grow(RING_GAP)


## Where the guide card must keep clear (Rect2() when not pointing): the ring
## now or, while the target lies outside its scroll list's visible part, the
## spot it comes into view at. ensure_control_visible ("Vis meg", a click that
## scrolls first) scrolls the least, so a control below the list lands on its
## bottom edge and one above on its top edge. The card re-docks once per
## frame, so avoiding only the ring left it one frame over a target that had
## just scrolled in under it (SHIP_PLAN cycle 2; the playtest's presses at
## steps 2 and 13 landed on the card).
func keep_clear_rect() -> Rect2:
	if not is_pointing():
		return Rect2()
	var r := _target.get_global_rect()
	var clip := _clip_rect()
	if clip.has_area():
		r.position = Vector2(
			clampf(r.position.x, clip.position.x, maxf(clip.position.x, clip.end.x - r.size.x)),
			clampf(r.position.y, clip.position.y, maxf(clip.position.y, clip.end.y - r.size.y)))
	return r.grow(RING_GAP)


## True when the target lies outside its scroll list's visible part.
func is_scrolled_away() -> bool:
	var clip := _clip_rect()
	return is_pointing() and clip.has_area() and not clip.intersects(_target.get_global_rect())


func _process(delta: float) -> void:
	if not visible:
		return
	if not is_pointing():
		visible = false
		return
	_time += delta
	if _emphasis > 0.0:
		_emphasis = maxf(0.0, _emphasis - delta / EMPHASIZE_S)
	queue_redraw()


func _draw() -> void:
	if not is_pointing():
		return
	var still := MotionPrefs.reduced_motion()
	var target_rect := _target.get_global_rect()
	var clip := _clip_rect()
	var local := Transform2D(0.0, -get_global_rect().position)
	if clip.has_area() and not clip.intersects(target_rect):
		_draw_scroll_chevron(local, clip, target_rect)
		return
	if clip.has_area():
		target_rect = target_rect.intersection(clip)
	var pulse := 0.0 if still else (0.5 + 0.5 * sin(_time * TAU / PULSE_S)) * PULSE_PX
	var accent := LakskongenTheme.INK_ACCENT
	var rect := local * target_rect.grow(RING_GAP + pulse)
	var glow := Color(accent, 0.18 if _route else 0.28)
	_draw_round_rect(rect.grow(GLOW_WIDTH * 0.5), glow, GLOW_WIDTH)
	_draw_round_rect(rect, Color(accent, 0.8 if _route else 1.0), RING_WIDTH)
	if _emphasis > 0.0 and not still:
		var fly := rect.grow(EMPHASIZE_FROM_PX * _emphasis)
		_draw_round_rect(fly, Color(accent, 1.0 - _emphasis), RING_WIDTH)
	_draw_chevron(rect, accent)


## A chevron above the ring pointing down at it, or below pointing up when
## there is no room above.
func _draw_chevron(rect: Rect2, color: Color) -> void:
	var above := rect.position.y - CHEVRON_GAP - CHEVRON_SIZE > 0.0
	var x := rect.get_center().x
	var tip_y := rect.position.y - CHEVRON_GAP if above else rect.end.y + CHEVRON_GAP
	var base_y := tip_y - CHEVRON_SIZE if above else tip_y + CHEVRON_SIZE
	draw_colored_polygon(PackedVector2Array([
		Vector2(x, tip_y), Vector2(x - CHEVRON_SIZE, base_y), Vector2(x + CHEVRON_SIZE, base_y)]), color)


## The target is scrolled out of its list: a chevron on the list's edge,
## pointing the way to scroll.
func _draw_scroll_chevron(local: Transform2D, clip: Rect2, target_rect: Rect2) -> void:
	var down := target_rect.position.y >= clip.end.y
	var edge := local * clip
	var x := edge.get_center().x
	var y := edge.end.y - CHEVRON_GAP if down else edge.position.y + CHEVRON_GAP
	var dir := 1.0 if down else -1.0
	var bob := 0.0 if MotionPrefs.reduced_motion() else sin(_time * TAU / PULSE_S) * 3.0
	var tip := Vector2(x, y + bob)
	var color := LakskongenTheme.INK_ACCENT
	draw_circle(tip - Vector2(0.0, dir * CHEVRON_SIZE * 0.6), CHEVRON_SIZE * 1.6, Color(LakskongenTheme.INK_SUNKEN, 0.85))
	draw_colored_polygon(PackedVector2Array([
		tip, tip + Vector2(-CHEVRON_SIZE, -dir * CHEVRON_SIZE), tip + Vector2(CHEVRON_SIZE, -dir * CHEVRON_SIZE)]), color)


## One outline box, restyled per stroke (draw_style_box draws at once).
var _box: StyleBoxFlat = null


func _draw_round_rect(rect: Rect2, color: Color, width: float) -> void:
	if _box == null:
		_box = StyleBoxFlat.new()
		_box.draw_center = false
		_box.anti_aliasing = true
	_box.border_color = color
	_box.set_border_width_all(int(round(width)))
	_box.set_corner_radius_all(int(CORNER_RADIUS + width))
	draw_style_box(_box, rect.grow(width * 0.5))


## The visible part of the nearest scroll list around the target (Rect2() when none).
func _clip_rect() -> Rect2:
	if _target == null or not is_instance_valid(_target):
		return Rect2()
	var node := _target.get_parent()
	while node != null:
		if node is ScrollContainer:
			return (node as ScrollContainer).get_global_rect()
		node = node.get_parent()
	return Rect2()
