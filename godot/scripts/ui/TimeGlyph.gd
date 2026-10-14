class_name TimeGlyph
extends Control

## TimeGlyph — the HUD's time-control and bell symbols drawn as vectors
## (roadmap U2-07; no emoji, D14): two bars for pause, a triangle for play, a
## triangle against a bar for "Til neste hendelse", and a bell for the event
## inbox. It sits centred in its button and takes the button's own text colour
## for its state (normal, hover, pressed, disabled), so it follows the theme.

enum Kind { PAUSE, PLAY, NEXT, BELL }

@export var kind: Kind = Kind.PAUSE:
	set(value):
		kind = value
		queue_redraw()

## Glyph height in pixels at UI scale 100 %.
@export var glyph_size: float = 13.0:
	set(value):
		glyph_size = value
		queue_redraw()

var _button: BaseButton = null


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_button = get_parent() as BaseButton
	if _button != null:
		# The button redraws on every state change (hover, press, focus,
		# disable); follow it so the glyph's colour does too.
		_button.draw.connect(queue_redraw)


func _draw() -> void:
	var color := _color()
	var s := glyph_size
	var c := size * 0.5
	match kind:
		Kind.PAUSE:
			var w := s * 0.28
			var gap := s * 0.22
			draw_rect(Rect2(c + Vector2(-gap - w, -s * 0.5), Vector2(w, s)), color)
			draw_rect(Rect2(c + Vector2(gap, -s * 0.5), Vector2(w, s)), color)
		Kind.PLAY:
			var h := s * 0.5
			draw_colored_polygon(PackedVector2Array([
				c + Vector2(-h * 0.8, -h), c + Vector2(h * 1.05, 0.0), c + Vector2(-h * 0.8, h)]), color)
		Kind.NEXT:
			var h := s * 0.5
			var tri := PackedVector2Array([
				c + Vector2(-h * 1.15, -h), c + Vector2(h * 0.55, 0.0), c + Vector2(-h * 1.15, h)])
			draw_colored_polygon(tri, color)
			draw_rect(Rect2(c + Vector2(h * 0.62, -h), Vector2(s * 0.2, s)), color)
		Kind.BELL:
			_draw_bell(c, s, color)


## A bell: a dome flaring to a rim, a clapper below and a stub on top.
func _draw_bell(c: Vector2, s: float, color: Color) -> void:
	var top := c.y - s * 0.5
	var rim := c.y + s * 0.28
	var pts := PackedVector2Array()
	var steps := 10
	# Left flank from rim to crown, then the right flank back down.
	for i in steps + 1:
		var t := float(i) / steps
		var y := lerpf(rim, top + s * 0.12, t)
		var half := lerpf(s * 0.5, s * 0.22, ease(t, 0.6))
		pts.append(Vector2(c.x - half, y))
	for i in range(steps, -1, -1):
		var t := float(i) / steps
		var y := lerpf(rim, top + s * 0.12, t)
		var half := lerpf(s * 0.5, s * 0.22, ease(t, 0.6))
		pts.append(Vector2(c.x + half, y))
	draw_colored_polygon(pts, color)
	draw_rect(Rect2(Vector2(c.x - s * 0.56, rim - s * 0.02), Vector2(s * 1.12, s * 0.1)), color)
	draw_circle(Vector2(c.x, rim + s * 0.2), s * 0.12, color)
	draw_circle(Vector2(c.x, top + s * 0.08), s * 0.08, color)


func _color() -> Color:
	if _button == null:
		return get_theme_color("font_color", "Label")
	var state := "font_color"
	match _button.get_draw_mode():
		BaseButton.DRAW_PRESSED:
			state = "font_pressed_color"
		BaseButton.DRAW_HOVER:
			state = "font_hover_color"
		BaseButton.DRAW_HOVER_PRESSED:
			state = "font_hover_pressed_color"
		BaseButton.DRAW_DISABLED:
			state = "font_disabled_color"
	return _button.get_theme_color(state)
