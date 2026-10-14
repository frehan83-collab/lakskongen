extends RefCounted

## ThemeLint — runtime checks behind roadmap U1-01/U1-02 (theme resolution and
## contrast), run by UiFlowTests on every screen the player can open.
##
##  * unthemed(root): visible Controls whose look still comes from Godot's
##    default theme (a stylebox, icon or font resolved from ThemeDB's default
##    theme, or the engine fallback font). The project theme
##    (gui/theme/custom) must reach every Control under every CanvasLayer.
##  * low_contrast(root): visible text (Labels, Buttons, CheckBoxes …) whose
##    resolved colour against what is actually drawn behind it is below
##    4.5:1, or 3:1 for large text (≥ 24 px, or ≥ 18 px in a bold face).
##    The backdrop is found by painter's order: the backgrounds (panels,
##    colour rects, buttons, tag pills) drawn before the text and covering
##    its centre, composited top-down. Translucent glass with nothing opaque
##    behind it is judged over both a white and a black world (worst case).

const TEXT_MIN := 4.5
const LARGE_MIN := 3.0
const LARGE_PX := 24
const LARGE_BOLD_PX := 18

## Classes checked for default-theme leaks: item kinds that define the look.
const LEAK_TYPES := ["Label", "Button", "CheckBox", "CheckButton", "OptionButton", "MenuButton", "LinkButton",
	"LineEdit", "TextEdit", "SpinBox", "PanelContainer", "Panel", "ProgressBar", "HSlider", "VSlider",
	"VScrollBar", "HScrollBar", "TabContainer", "TabBar", "ItemList", "Tree", "RichTextLabel", "HSeparator",
	"VSeparator", "PopupMenu"]


## Visible controls styled by Godot's default theme: ["path: item", …].
static func unthemed(root: Node) -> PackedStringArray:
	var out := PackedStringArray()
	var default_theme := ThemeDB.get_default_theme()
	for c in _visible_controls(root):
		var cls := _leak_type(c)
		if cls == "":
			continue
		var leaks := PackedStringArray()
		for item in default_theme.get_stylebox_list(cls):
			if c.get_theme_stylebox(item) == default_theme.get_stylebox(item, cls):
				leaks.append("stylebox " + item)
		for item in default_theme.get_icon_list(cls):
			if c.get_theme_icon(item) == default_theme.get_icon(item, cls):
				leaks.append("icon " + item)
		for item in default_theme.get_font_list(cls):
			var f := c.get_theme_font(item)
			if f == ThemeDB.fallback_font or f == default_theme.get_font(item, cls):
				leaks.append("font " + item)
		if not leaks.is_empty():
			out.append("%s (%s): %s" % [_path(c), cls, ", ".join(leaks)])
	return out


## Visible text below its contrast minimum: ["path 'text' 3.2:1 < 4.5", …].
static func low_contrast(root: Node) -> PackedStringArray:
	var out := PackedStringArray()
	var drawers := _drawers(root)
	var view := Rect2(Vector2.ZERO, root.get_viewport().get_visible_rect().size)
	for c in _visible_controls(root):
		var spec := _text_spec(c)
		if spec.is_empty():
			continue
		var rect := c.get_global_rect()
		var centre := rect.get_center()
		if not view.has_point(centre) or not _unclipped(c, centre):
			continue
		var fg: Color = spec["color"] * _modulate_chain(c)
		if fg.a < 0.05:
			continue
		var ratio := _worst_ratio(fg, c, centre, drawers, spec.get("own_bg"))
		var minimum := LARGE_MIN if _is_large(c, spec) else TEXT_MIN
		if ratio + 0.005 < minimum:
			out.append("%s '%s' %.2f:1 < %.1f" % [_path(c), str(spec["text"]).left(40), ratio, minimum])
	return out


# ── Text extraction ───────────────────────────────────────────────────────────

## {"text", "color", "size", "font", "own_bg"} for a text-bearing control, or {}.
static func _text_spec(c: Control) -> Dictionary:
	if c is Label:
		var l := c as Label
		if not _has_words(l.text):
			return {}
		var own: Variant = null
		var sb := l.get_theme_stylebox("normal")
		if sb is StyleBoxFlat and (sb as StyleBoxFlat).draw_center:
			own = (sb as StyleBoxFlat).bg_color
		return {"text": l.text, "color": l.get_theme_color("font_color"), "size": l.get_theme_font_size("font_size"),
			"font": l.get_theme_font("font"), "own_bg": own}
	if c is RichTextLabel:
		var r := c as RichTextLabel
		if not _has_words(r.get_parsed_text()):
			return {}
		return {"text": r.get_parsed_text(), "color": r.get_theme_color("default_color"),
			"size": r.get_theme_font_size("normal_font_size"), "font": r.get_theme_font("normal_font"), "own_bg": null}
	if c is BaseButton and c.has_method("get_text"):
		var b := c as BaseButton
		var text: String = b.call("get_text")
		if b.disabled or not _has_words(text):
			return {}  # WCAG 1.4.3 exempts inactive components.
		var state := "normal"
		var col_name := "font_color"
		if b.toggle_mode and b.button_pressed:
			state = "pressed"
			col_name = "font_pressed_color"
		var own: Variant = null
		if not (c is CheckBox or c is CheckButton):
			var sb := b.get_theme_stylebox(state)
			if sb is StyleBoxFlat and (sb as StyleBoxFlat).draw_center:
				own = (sb as StyleBoxFlat).bg_color
			if b is Button and (b as Button).flat:
				own = null
		return {"text": text, "color": b.get_theme_color(col_name), "size": b.get_theme_font_size("font_size"),
			"font": b.get_theme_font("font"), "own_bg": own}
	return {}


static func _has_words(text: String) -> bool:
	for i in text.length():
		var u := text.unicode_at(i)
		if (u >= 48 and u <= 57) or (u >= 65 and u <= 90) or (u >= 97 and u <= 122) or (u >= 0xC0 and u <= 0x24F):
			return true
	return false


static func _is_large(c: Control, spec: Dictionary) -> bool:
	var px := int(spec["size"])
	if px >= LARGE_PX:
		return true
	var f: Font = spec["font"]
	var name := (f.resource_path if f != null else "").to_lower()
	var bold := name.contains("bold") or name.contains("display")
	return bold and px >= LARGE_BOLD_PX


# ── Backdrop ──────────────────────────────────────────────────────────────────

## Every visible background drawer in painter's order: [{order, node, rect, color}].
static func _drawers(root: Node) -> Array:
	var out: Array = []
	var counter := [0]
	_collect_drawers(root, 0, counter, out)
	return out


static func _collect_drawers(n: Node, layer: int, counter: Array, out: Array) -> void:
	if n is CanvasLayer:
		if not (n as CanvasLayer).visible:
			return
		layer = (n as CanvasLayer).layer
	if n is CanvasItem and not (n as CanvasItem).visible:
		return
	counter[0] += 1
	if n is Control:
		var c := n as Control
		var col: Variant = _drawn_bg(c)
		if col != null:
			out.append({"order": Vector2i(layer, counter[0]), "node": c, "rect": c.get_global_rect(), "color": col})
		c.set_meta(&"_lint_order", Vector2i(layer, counter[0]))
	for child in n.get_children():
		_collect_drawers(child, layer, counter, out)


## The colour a control paints behind its children, or null.
static func _drawn_bg(c: Control) -> Variant:
	var col: Variant = null
	if c is ColorRect:
		col = (c as ColorRect).color
	elif c is PanelContainer or c is Panel:
		var sb := c.get_theme_stylebox("panel")
		if sb is StyleBoxFlat and (sb as StyleBoxFlat).draw_center:
			col = (sb as StyleBoxFlat).bg_color
	elif c is Button and not (c as Button).flat and not (c is CheckBox or c is CheckButton):
		var b := c as Button
		var state := "disabled" if b.disabled else ("pressed" if b.toggle_mode and b.button_pressed else "normal")
		var sb := b.get_theme_stylebox(state)
		if sb is StyleBoxFlat and (sb as StyleBoxFlat).draw_center:
			col = (sb as StyleBoxFlat).bg_color
	if col == null:
		return null
	var out: Color = (col as Color) * _modulate_chain(c)
	return out if out.a > 0.02 else null


## Contrast of `fg` over the composited backdrop at `point`; the worse of a
## white and a black world when nothing opaque is behind.
static func _worst_ratio(fg: Color, c: Control, point: Vector2, drawers: Array, own_bg: Variant) -> float:
	var order: Vector2i = c.get_meta(&"_lint_order", Vector2i(999, 0))
	var stack: Array = []
	if own_bg != null:
		stack.append((own_bg as Color) * _modulate_chain(c))
	var below: Array = []
	for d in drawers:
		if d["node"] == c or (d["order"] as Vector2i) >= order:
			continue
		if (d["node"] as Control).is_ancestor_of(c) or (d["rect"] as Rect2).has_point(point):
			below.append(d)
	below.sort_custom(func(a, b) -> bool: return (a["order"] as Vector2i) > (b["order"] as Vector2i))
	for d in below:
		stack.append(d["color"])
		if (d["color"] as Color).a >= 0.995:
			break
	var worst := 99.0
	for world in [Color.WHITE, Color.BLACK]:
		var bg: Color = world
		for i in range(stack.size() - 1, -1, -1):
			bg = _over(stack[i], bg)
		worst = minf(worst, _ratio(_over(fg, bg), bg))
		if not stack.is_empty() and (stack[stack.size() - 1] as Color).a >= 0.995:
			break
	return worst


static func _over(top: Color, bottom: Color) -> Color:
	var a := top.a
	return Color(top.r * a + bottom.r * (1.0 - a), top.g * a + bottom.g * (1.0 - a), top.b * a + bottom.b * (1.0 - a), 1.0)


static func _lum(c: Color) -> float:
	var f := func(x: float) -> float: return x / 12.92 if x <= 0.04045 else pow((x + 0.055) / 1.055, 2.4)
	return 0.2126 * f.call(c.r) + 0.7152 * f.call(c.g) + 0.0722 * f.call(c.b)


static func _ratio(a: Color, b: Color) -> float:
	var la := _lum(a)
	var lb := _lum(b)
	return (maxf(la, lb) + 0.05) / (minf(la, lb) + 0.05)


# ── Tree helpers ──────────────────────────────────────────────────────────────

static func _visible_controls(root: Node) -> Array[Control]:
	var out: Array[Control] = []
	_collect_visible(root, out)
	return out


static func _collect_visible(n: Node, out: Array[Control]) -> void:
	if n is CanvasLayer and not (n as CanvasLayer).visible:
		return
	if n is CanvasItem and not (n as CanvasItem).visible:
		return
	if n is Control:
		var c := n as Control
		if c.size.x >= 1.0 and c.size.y >= 1.0:
			out.append(c)
	for child in n.get_children():
		_collect_visible(child, out)


## Product of the modulate colours from the control up to its canvas layer
## (self_modulate counts for the control itself only).
static func _modulate_chain(c: Node) -> Color:
	var m := Color.WHITE
	var n: Node = c
	while n != null and not (n is CanvasLayer) and not (n is Viewport):
		if n is CanvasItem:
			m *= (n as CanvasItem).modulate
			if n == c:
				m *= (n as CanvasItem).self_modulate
		n = n.get_parent()
	return m


## False when a clipping ancestor (ScrollContainer, clip_contents) hides the point.
static func _unclipped(c: Control, point: Vector2) -> bool:
	var n := c.get_parent()
	while n != null:
		if n is Control and (n as Control).clip_contents and not (n as Control).get_global_rect().has_point(point):
			return false
		n = n.get_parent()
	return true


static func _leak_type(c: Control) -> String:
	for t in LEAK_TYPES:
		if c.is_class(t) and c.get_class() == t:
			return t
	for t in LEAK_TYPES:
		if c.is_class(t):
			return t
	return ""


static func _path(c: Node) -> String:
	var parts := PackedStringArray()
	var n: Node = c
	var depth := 0
	while n != null and depth < 4:
		parts.insert(0, String(n.name))
		n = n.get_parent()
		depth += 1
	return "/".join(parts)
