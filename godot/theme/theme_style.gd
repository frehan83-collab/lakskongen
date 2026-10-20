extends "res://theme/theme_tokens.gd"

## StyleBox and SVG-icon primitives for the Kyst & Stål theme builder.


static func _flat(bg: Color, radius: int, margin_v: float = 0.0, margin_h: float = 0.0) -> StyleBoxFlat:
	var sb := StyleBoxFlat.new()
	sb.bg_color = bg
	sb.set_corner_radius_all(radius)
	sb.content_margin_top = margin_v
	sb.content_margin_bottom = margin_v
	sb.content_margin_left = margin_h
	sb.content_margin_right = margin_h
	sb.anti_aliasing = true
	return sb


static func _bordered(bg: Color, border: Color, width: int, radius: int, margin_v: float = 0.0, margin_h: float = 0.0) -> StyleBoxFlat:
	var sb := _flat(bg, radius, margin_v, margin_h)
	sb.border_color = border
	sb.set_border_width_all(width)
	return sb


static func _shadow(sb: StyleBoxFlat, color: Color, alpha: float, size: int, offset_y: float) -> StyleBoxFlat:
	sb.shadow_color = Color(color, alpha)
	sb.shadow_size = size
	sb.shadow_offset = Vector2(0.0, offset_y)
	return sb


static func _empty(margin_v: float = 0.0, margin_h: float = 0.0) -> StyleBoxEmpty:
	var sb := StyleBoxEmpty.new()
	sb.content_margin_top = margin_v
	sb.content_margin_bottom = margin_v
	sb.content_margin_left = margin_h
	sb.content_margin_right = margin_h
	return sb


## A 2 px focus ring (3 px on a high-contrast surface) drawn over the
## control's normal box (no fill).
static func _focus_ring(c: _Ctx, radius: int) -> StyleBoxFlat:
	var sb := StyleBoxFlat.new()
	var width := FOCUS_RING_HC_PX if c.high_contrast else FOCUS_RING_PX
	sb.draw_center = false
	sb.border_color = c.col("focus")
	sb.set_border_width_all(width)
	sb.set_corner_radius_all(radius + width)
	sb.set_expand_margin_all(width)
	sb.anti_aliasing = true
	return sb


static func _line(color: Color, vertical: bool, grow: float = 0.0) -> StyleBoxLine:
	var sb := StyleBoxLine.new()
	sb.color = color
	sb.thickness = 1
	sb.vertical = vertical
	sb.grow_begin = grow
	sb.grow_end = grow
	return sb


## Card on the surface: raised fill, hairline edge, soft shadow.
static func _card(c: _Ctx, margin: float) -> StyleBoxFlat:
	var sb := _bordered(c.col("raised"), c.col("hairline"), 1, c.radius("md"), margin, margin)
	var shadow_col: Color = Color.BLACK if c.ink else c.shadow_color()
	var alpha := c.elev("card-shadow-alpha", 0.08) * (3.0 if c.ink else 1.0)
	return _shadow(sb, shadow_col, alpha, int(c.elev("card-shadow-size", 6)), 2.0)


static func _svg(source: String) -> Texture2D:
	var tex := DPITexture.new()
	tex.set_source(source)
	return tex


static func _hex(color: Color) -> String:
	return "#" + color.to_html(false)


static func _checkbox_svg(fill: Color, stroke: Color, mark: Color, checked: bool, alpha: float) -> String:
	var body := '<rect x="1" y="1" width="16" height="16" rx="3.5" fill="%s" stroke="%s" stroke-width="1.5"/>' % [
		_hex(fill) if checked else "none", _hex(stroke)]
	if checked:
		body += '<path d="M4.5 9.2l3 3 6-6.4" fill="none" stroke="%s" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>' % _hex(mark)
	return '<svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" opacity="%.2f">%s</svg>' % [alpha, body]


static func _radio_svg(fill: Color, stroke: Color, mark: Color, checked: bool, alpha: float) -> String:
	var body := '<circle cx="9" cy="9" r="7.5" fill="%s" stroke="%s" stroke-width="1.5"/>' % [
		_hex(fill) if checked else "none", _hex(stroke)]
	if checked:
		body += '<circle cx="9" cy="9" r="3" fill="%s"/>' % _hex(mark)
	return '<svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" opacity="%.2f">%s</svg>' % [alpha, body]


static func _switch_svg(track: Color, knob: Color, on: bool, alpha: float) -> String:
	var cx := 25.0 if on else 11.0
	return ('<svg xmlns="http://www.w3.org/2000/svg" width="36" height="22" opacity="%.2f">' +
		'<rect x="1" y="2" width="34" height="18" rx="9" fill="%s"/>' +
		'<circle cx="%.1f" cy="11" r="7" fill="%s"/></svg>') % [alpha, _hex(track), cx, _hex(knob)]


static func _chevron_svg(color: Color, direction: String, size: int = 12) -> String:
	var paths := {
		"down": "M2.5 4.5l3.5 3.5 3.5-3.5",
		"up": "M2.5 7.5l3.5-3.5 3.5 3.5",
		"right": "M4.5 2.5l3.5 3.5-3.5 3.5",
		"left": "M7.5 2.5l-3.5 3.5 3.5 3.5",
	}
	return ('<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" viewBox="0 0 12 12">' +
		'<path d="%s" fill="none" stroke="%s" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>') % [
		size, size, paths[direction], _hex(color)]


static func _cross_svg(color: Color, size: int = 14) -> String:
	return ('<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" viewBox="0 0 14 14">' +
		'<path d="M3.5 3.5l7 7M10.5 3.5l-7 7" fill="none" stroke="%s" stroke-width="1.6" stroke-linecap="round"/></svg>') % [
		size, size, _hex(color)]


static func _knob_svg(fill: Color, ring: Color, size: int) -> String:
	var r := size * 0.5 - 1.5
	return ('<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d">' +
		'<circle cx="%.1f" cy="%.1f" r="%.1f" fill="%s" stroke="%s" stroke-width="2"/></svg>') % [
		size, size, size * 0.5, size * 0.5, r, _hex(fill), _hex(ring)]


static func _blank_svg(w: int, h: int) -> String:
	return '<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d"></svg>' % [w, h]


static func _button_colors(t: Theme, type_name: String, normal: Color, hover: Color, pressed: Color, disabled: Color) -> void:
	t.set_color("font_color", type_name, normal)
	t.set_color("font_hover_color", type_name, hover)
	t.set_color("font_focus_color", type_name, normal)
	t.set_color("font_pressed_color", type_name, pressed)
	t.set_color("font_hover_pressed_color", type_name, pressed)
	t.set_color("font_disabled_color", type_name, disabled)
	t.set_color("font_outline_color", type_name, Color(0, 0, 0, 0))
	t.set_color("icon_normal_color", type_name, normal)
	t.set_color("icon_hover_color", type_name, hover)
	t.set_color("icon_focus_color", type_name, normal)
	t.set_color("icon_pressed_color", type_name, pressed)
	t.set_color("icon_hover_pressed_color", type_name, pressed)
	t.set_color("icon_disabled_color", type_name, disabled)


## Returns [normal, hover, pressed, disabled] boxes and [fg, fg_hover, fg_pressed, fg_disabled].
static func _button_kind(c: _Ctx, kind: String, mv: float, mh: float) -> Array:
	var r := c.radius("sm")
	var on_fill := c.col("sunken") if c.ink else c.col("raised")
	# Every filled kind goes quiet the same way when disabled: an outlined,
	# washed-out neutral box with muted text (no pink or pale-green ghosts).
	var inactive := _bordered(Color(c.col("raised"), 0.45), Color(c.col("hairline"), 0.6), 1, r, mv, mh)
	var inactive_fg := Color(c.col("muted"), 0.7)
	match kind:
		"Primary":
			return [
				[_flat(c.col("salmon"), r, mv, mh), _flat(c.col("salmon_hover"), r, mv, mh),
					_flat(c.col("salmon_pressed"), r, mv, mh), inactive],
				[c.col("on_salmon"), c.col("on_salmon"), c.col("on_salmon"), inactive_fg],
			]
		"Danger", "Success":
			var fill := c.col("crit") if kind == "Danger" else c.col("ok")
			return [
				[_flat(fill, r, mv, mh), _flat(fill.lightened(0.08), r, mv, mh),
					_flat(fill.darkened(0.12), r, mv, mh), inactive],
				[on_fill, on_fill, on_fill, inactive_fg],
			]
		"Ghost":
			return [
				[_flat(Color(0, 0, 0, 0), r, mv, mh), _flat(Color(c.col("raised"), 0.7), r, mv, mh),
					_flat(c.col("sunken"), r, mv, mh), _flat(Color(0, 0, 0, 0), r, mv, mh)],
				[c.col("muted"), c.col("text"), c.col("text"), Color(c.col("muted"), 0.55)],
			]
		_:
			# Neutral (Button / SecondaryButton): raised fill, hairline edge.
			var normal := _bordered(c.col("raised"), c.col("hairline"), 1, r, mv, mh)
			var hover := _bordered(c.col("raised").lightened(0.06) if c.ink else Color.WHITE, c.col("stroke"), 1, r, mv, mh)
			var pressed := _bordered(c.col("sunken"), c.col("focus"), 1, r, mv, mh)
			var disabled := _bordered(Color(c.col("raised"), 0.45), Color(c.col("hairline"), 0.6), 1, r, mv, mh)
			if not c.ink:
				_shadow(normal, c.col("text"), 0.06, 2, 1.0)
				_shadow(hover, c.col("text"), 0.10, 3, 1.0)
			return [[normal, hover, pressed, disabled],
				[c.col("text"), c.col("text"), c.col("text"), Color(c.col("muted"), 0.7)]]


## `top` composited over the opaque `bottom` (alpha blend), as an opaque Color.
static func _blend(top: Color, bottom: Color) -> Color:
	var a := top.a
	return Color(top.r * a + bottom.r * (1.0 - a), top.g * a + bottom.g * (1.0 - a), top.b * a + bottom.b * (1.0 - a), 1.0)
