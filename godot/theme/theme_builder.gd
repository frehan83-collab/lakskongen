extends "res://theme/theme_controls.gd"

## Kyst & Stål theme builder (roadmap D13/D14; items U1-01, U1-02).
##
## A pure function of res://theme/tokens.json: build(tokens, surface, fonts)
## returns a complete Theme for one surface.
##   * "ink"   — Ink glass: HUD, the tab rail, world cards, toasts, menus. Saved
##               as the PROJECT theme (gui/theme/custom), so every Control under
##               every CanvasLayer resolves it.
##   * "chart" — Chart paper: document panels. Saved separately and assigned to
##               the subtree that hosts documents (GameUI's panel area).
##
## scripts/tools/build_theme.gd (run by tools/sync_tokens.py --write) saves the
## fonts and both themes. Do not hand-edit the generated .tres files.
##
## Type variations are generated as a matrix so a node never needs a per-node
## size or colour override:
##   Label:  <Role><Tone>   e.g. "Heading", "KpiCrit", "CaptionMuted", "BodyOk"
##   Button: <Size><Kind>Button  e.g. "PrimaryButton", "SmallGhostButton"
##   Panel:  "Card", "Inset", "Sheet", "Glass", "KpiCard<Tone>", "Badge<Tone>" ...
## Colours that code needs at runtime resolve per surface through the
## "Palette" pseudo-type: control.get_theme_color("crit", "Palette").


static func build(tokens: Dictionary, surface: String, fonts: Dictionary) -> Theme:
	var c := _Ctx.new(tokens, surface, fonts, palette(tokens, surface))
	var t := Theme.new()
	t.default_font = fonts["ui_regular"]
	t.default_font_size = c.size("body")
	t.set_meta(META_SURFACE, surface)

	_palette_type(t, c)
	_containers(t, c)
	_labels(t, c)
	_buttons(t, c)
	_toggles(t, c)
	_option_buttons(t, c)
	_inputs(t, c)
	_popups(t, c)
	_ranges(t, c)
	_scroll(t, c)
	_tabs(t, c)
	_lists(t, c)
	_rich_text(t, c)
	_tooltips_and_windows(t, c)
	_separators(t, c)
	_panel_variations(t, c)
	_sized_variations(t, c)
	return t


## Colours code resolves per surface: get_theme_color("crit", "Palette").
static func _palette_type(t: Theme, c: _Ctx) -> void:
	for key in ["base", "raised", "sunken", "hairline", "stroke", "text", "muted", "ok", "warn",
			"crit", "info", "accent", "focus", "glass", "salmon", "salmon_hover", "salmon_pressed",
			"salmon_deep", "on_salmon"]:
		t.set_color(key, PALETTE_TYPE, c.col(key))
	# Data-visualisation series (charts), distinct under CVD with dual encoding.
	var series: Array[Color] = [c.col("info"), c.col("salmon") if c.ink else c.col("salmon_deep"),
		c.col("ok"), c.col("warn"), c.col("muted"), c.col("crit")]
	for i in series.size():
		t.set_color("series_%d" % (i + 1), PALETTE_TYPE, series[i])
	t.set_color("grid", PALETTE_TYPE, Color(c.col("hairline"), 0.8))


static func _containers(t: Theme, c: _Ctx) -> void:
	var panel: StyleBoxFlat
	if c.ink:
		panel = _bordered(c.col("glass"), c.col("hairline"), 1, c.radius("md"), c.space("md"), c.space("md"))
		_shadow(panel, Color.BLACK, c.elev("glass-shadow-alpha", 0.35), int(c.elev("glass-shadow-size", 14)), 4.0)
	else:
		panel = _card(c, c.space("md"))
	t.set_stylebox("panel", "Panel", panel)
	t.set_stylebox("panel", "PanelContainer", panel)
	t.set_stylebox("panel", "PopupPanel", panel)
	t.set_stylebox("panel", "ScrollContainer", _empty())
	t.set_stylebox("focus", "ScrollContainer", _empty())
	t.set_color("scroll_hint_vertical_color", "ScrollContainer", Color(c.col("base"), 0.6))
	t.set_color("scroll_hint_horizontal_color", "ScrollContainer", Color(c.col("base"), 0.6))
	t.set_stylebox("split_bar_background", "SplitContainer", _empty())
	t.set_stylebox("split_bar_background", "HSplitContainer", _empty())
	t.set_stylebox("split_bar_background", "VSplitContainer", _empty())
	for split in ["HSplitContainer", "VSplitContainer"]:
		t.set_icon("grabber", split, _svg(_blank_svg(4, 4)))
		t.set_icon("touch_dragger", split, _svg(_blank_svg(4, 4)))
	t.set_constant("separation", "BoxContainer", c.space("xs"))
	t.set_constant("separation", "HBoxContainer", c.space("xs"))
	t.set_constant("separation", "VBoxContainer", c.space("xs"))


static func _labels(t: Theme, c: _Ctx) -> void:
	t.set_stylebox("normal", "Label", _empty())
	t.set_stylebox("focus", "Label", _empty())
	t.set_color("font_shadow_color", "Label", Color(0, 0, 0, 0))
	t.set_color("font_outline_color", "Label", c.col("base"))
	t.set_constant("line_spacing", "Label", 3)
	for role in LABEL_ROLES:
		for tone in TONES:
			var name := _label_variation(role, tone)
			if name != "Label":
				t.set_type_variation(name, "Label")
			_style_label(t, c, name, role, tone)
	for alias in LABEL_ALIASES:
		t.set_type_variation(alias, "Label")
		_style_label(t, c, alias, LABEL_ALIASES[alias][0], LABEL_ALIASES[alias][1])
	# "Body" names the base role explicitly (code may reset a label to it).
	t.set_type_variation("Body", "Label")
	_style_label(t, c, "Body", "Body", "")


## "Body" + "" is the base Label; every other combination is a variation.
static func _label_variation(role: String, tone: String) -> String:
	if role == "Body" and tone == "":
		return "Label"
	return role + tone


static func _style_label(t: Theme, c: _Ctx, type_name: String, role: String, tone: String) -> void:
	var spec: Array = LABEL_ROLES[role]
	t.set_color("font_color", type_name, c.col(TONE_KEYS[tone]))
	t.set_font_size("font_size", type_name, c.size(spec[0]))
	t.set_font("font", type_name, c.font(spec[1]))


static func _style_button(t: Theme, c: _Ctx, type_name: String, kind: String, size_key: String) -> void:
	var mv := 8.0
	var mh := 14.0
	if size_key == "caption":
		mv = 5.0
		mh = 10.0
	elif size_key == "lg":
		mv = 12.0
		mh = 22.0
	var spec := _button_kind(c, kind, mv, mh)
	var boxes: Array = spec[0]
	var fg: Array = spec[1]
	for i in 4:
		var state: String = ["normal", "hover", "pressed", "disabled"][i]
		t.set_stylebox(state, type_name, boxes[i])
		t.set_stylebox(state + "_mirrored", type_name, boxes[i])
	t.set_stylebox("hover_pressed", type_name, boxes[2])
	t.set_stylebox("hover_pressed_mirrored", type_name, boxes[2])
	t.set_stylebox("focus", type_name, _focus_ring(c, c.radius("sm")))
	_button_colors(t, type_name, fg[0], fg[1], fg[2], fg[3])
	t.set_font("font", type_name, c.font("ui_semibold"))
	t.set_font_size("font_size", type_name, c.size(size_key))
	t.set_constant("h_separation", type_name, c.space("sm"))
	t.set_constant("outline_size", type_name, 0)


static func _buttons(t: Theme, c: _Ctx) -> void:
	for size_prefix in CONTROL_SIZES:
		for kind in BUTTON_KINDS:
			var name: String = size_prefix + kind + "Button"
			if name != "Button":
				t.set_type_variation(name, "Button")
			_style_button(t, c, name, kind, CONTROL_SIZES[size_prefix])
	t.set_type_variation("FlatButton", "Button")
	_style_button(t, c, "FlatButton", "Ghost", "body")
	_rail_tab(t, c)
	_list_item(t, c)
	# LinkButton / MenuButton / ColorPickerButton share the neutral look.
	for size_prefix in CONTROL_SIZES:
		var link: String = size_prefix + "LinkButton"
		if link != "LinkButton":
			t.set_type_variation(link, "LinkButton")
		t.set_color("font_color", link, c.col("info"))
		t.set_color("font_hover_color", link, c.col("text"))
		t.set_color("font_pressed_color", link, c.col("text"))
		t.set_color("font_focus_color", link, c.col("info"))
		t.set_color("font_outline_color", link, Color(0, 0, 0, 0))
		t.set_stylebox("focus", link, _focus_ring(c, c.radius("sm")))
		t.set_font("font", link, c.font("ui_medium"))
		t.set_font_size("font_size", link, c.size(CONTROL_SIZES[size_prefix]))
	for cls in ["MenuButton", "ColorPickerButton", "MenuBar"]:
		var spec := _button_kind(c, "", 8.0, 14.0)
		for i in 4:
			t.set_stylebox(["normal", "hover", "pressed", "disabled"][i], cls, spec[0][i])
		t.set_stylebox("focus", cls, _focus_ring(c, c.radius("sm")))
		_button_colors(t, cls, spec[1][0], spec[1][1], spec[1][2], spec[1][3])
		t.set_font("font", cls, c.font("ui_semibold"))
		t.set_font_size("font_size", cls, c.size("body"))


## The tab rail (GameUI): quiet tabs, a salmon underline on the active one.
static func _rail_tab(t: Theme, c: _Ctx) -> void:
	var name := "RailTab"
	t.set_type_variation(name, "Button")
	var mv := 8.0
	var mh := 8.0  # 19 tabs share the rail (U1-04 reworks it into hubs)
	var normal := _flat(Color(0, 0, 0, 0), c.radius("sm"), mv, mh)
	var hover := _flat(Color(c.col("raised"), 0.85), c.radius("sm"), mv, mh)
	var active := _flat(c.col("raised"), c.radius("sm"), mv, mh)
	active.border_color = c.col("salmon")
	active.border_width_bottom = 3
	active.corner_radius_bottom_left = 0
	active.corner_radius_bottom_right = 0
	var disabled := _flat(Color(0, 0, 0, 0), c.radius("sm"), mv, mh)
	for state in ["normal", "normal_mirrored"]:
		t.set_stylebox(state, name, normal)
	for state in ["hover", "hover_mirrored"]:
		t.set_stylebox(state, name, hover)
	for state in ["pressed", "pressed_mirrored", "hover_pressed", "hover_pressed_mirrored"]:
		t.set_stylebox(state, name, active)
	for state in ["disabled", "disabled_mirrored"]:
		t.set_stylebox(state, name, disabled)
	t.set_stylebox("focus", name, _focus_ring(c, c.radius("sm")))
	_button_colors(t, name, c.col("muted"), c.col("text"), c.col("text"), Color(c.col("muted"), 0.5))
	t.set_font("font", name, c.font("ui_medium"))
	t.set_font_size("font_size", name, c.size("caption"))


## Surface building blocks for PanelContainer / Panel nodes. Margins follow the
## layouts they replace: "Card" has none (its children bring a header bar and a
## padded body), "CardPadded"/"CardCompact"/"Tile" pad their content.
static func _panel_variations(t: Theme, c: _Ctx) -> void:
	var md := float(c.space("md"))
	# Glass, Rail, Chip and the toasts are Ink on every surface; high-contrast
	# Ink (opaque, a hairline at least 3:1) on the high-contrast ones.
	var ink := ink_palette_for(c.tokens, c.surface)
	var edge_alpha := 1.0 if c.high_contrast else 0.8
	var toast_alpha := 1.0 if c.high_contrast else 0.97
	var toast_edge_alpha := 1.0 if c.high_contrast else 0.55
	var add := func(name: String, sb: StyleBox) -> void:
		t.set_type_variation(name, "PanelContainer")
		t.set_stylebox("panel", name, sb)
	add.call("Card", _card(c, 0.0))
	add.call("CardPadded", _card(c, md))
	add.call("CardCompact", _card(c, 12.0))
	add.call("CardFlat", _bordered(c.col("raised"), c.col("hairline"), 1, c.radius("md"), md, md))
	var header := _flat(Color(0, 0, 0, 0), 0, 14.0, 20.0)
	header.border_color = c.col("hairline")
	header.border_width_bottom = 1
	add.call("CardHeader", header)
	var header_compact := header.duplicate() as StyleBoxFlat
	header_compact.content_margin_top = 12.0
	header_compact.content_margin_bottom = 12.0
	header_compact.content_margin_left = md
	header_compact.content_margin_right = md
	add.call("CardHeaderCompact", header_compact)
	add.call("HeaderBar", header_compact)
	add.call("Tile", _bordered(c.col("sunken"), Color(c.col("hairline"), 0.8), 1, c.radius("md"), 12.0, 12.0))
	add.call("Inset", _bordered(c.col("sunken"), Color(c.col("hairline"), 0.7), 1, c.radius("sm"), 10.0, 12.0))
	add.call("Sheet", _flat(c.col("glass"), 0, 0.0, 0.0))
	add.call("Surface", _flat(c.col("raised"), c.radius("md"), 0.0, 0.0))
	var dialog := _bordered(c.col("glass") if c.ink else c.col("raised"), c.col("hairline"), 1, c.radius("lg"), 20.0, 20.0)
	_shadow(dialog, Color.BLACK, 0.45 if c.ink else 0.22, 24, 8.0)
	add.call("Dialog", dialog)
	var glass := _bordered(ink["glass"], ink["hairline"], 1, c.radius("md"), md, md)
	_shadow(glass, Color.BLACK, c.elev("glass-shadow-alpha", 0.35), int(c.elev("glass-shadow-size", 14)), 4.0)
	add.call("Glass", glass)
	var rail := _flat(ink["glass"], 0, 6.0, 8.0)
	rail.border_color = ink["hairline"]
	rail.border_width_bottom = 1
	add.call("Rail", rail)
	add.call("Plain", _empty())
	# A small Ink glass chip for hints drawn straight over the 3D world.
	var chip := _bordered(ink["glass"], Color(ink["hairline"], edge_alpha), 1, c.radius("lg"), 5.0, 12.0)
	add.call("Chip", chip)
	for tone in TONES:
		var key: String = TONE_KEYS[tone]
		var stripe := _card(c, md)
		stripe.border_color = c.col(key if tone != "" else "info")
		stripe.border_width_left = 0
		stripe.border_width_right = 0
		stripe.border_width_bottom = 0
		stripe.border_width_top = 3
		add.call("KpiCard" + tone, stripe)
		var tone_col := c.col(key if tone != "" else "muted")
		var badge := _bordered(Color(tone_col, 0.14), Color(tone_col, 0.45), 1, c.radius("lg"), 2.0, 8.0)
		add.call("Badge" + tone, badge)
		var callout := _bordered(Color(tone_col, 0.10), Color(tone_col, 0.35), 1, c.radius("sm"), 10.0, 14.0)
		callout.border_width_left = 4
		callout.border_color = Color(tone_col, 0.6)
		add.call("Callout" + tone, callout)
		# Toasts: raised Ink glass with a tone accent edge (Ink in both themes).
		var toast_tone: Color = ink[key] if tone != "" else ink["info"]
		var toast := _bordered(Color(ink["raised"], toast_alpha), Color(toast_tone, toast_edge_alpha), 1, c.radius("md"), 10.0, 14.0)
		toast.border_width_left = 4
		_shadow(toast, Color.BLACK, 0.35, 12, 4.0)
		add.call("Toast" + tone, toast)
		# Swatch: a solid tone fill (legend keys, category strips).
		add.call("Swatch" + tone, _flat(tone_col if tone != "" else c.col("info"), 2, 0.0, 0.0))
		# Tag: a Label variation — tone text on a tinted pill.
		var tag_name := "Tag" + tone
		t.set_type_variation(tag_name, "Label")
		t.set_color("font_color", tag_name, c.col(key) if tone != "" else c.col("text"))
		t.set_font("font", tag_name, c.font("ui_semibold"))
		t.set_font_size("font_size", tag_name, c.size("caption"))
		var pill := _bordered(_blend(Color(tone_col, 0.14), c.col("raised")), Color(tone_col, 0.5), 1, c.radius("sm"), 1.0, 6.0)
		t.set_stylebox("normal", tag_name, pill)


## A selectable row in a list (encyclopedia, pickers): quiet until hovered,
## the selected (pressed toggle) row tinted with an info edge.
static func _list_item(t: Theme, c: _Ctx) -> void:
	var name := "ListItemButton"
	t.set_type_variation(name, "Button")
	var mv := 8.0
	var mh := 14.0
	var normal := _flat(Color(0, 0, 0, 0), c.radius("sm"), mv, mh)
	var hover := _flat(c.col("sunken") if not c.ink else Color(c.col("raised"), 0.85), c.radius("sm"), mv, mh)
	var selected := _flat(_blend(Color(c.col("info"), 0.14), c.col("raised")), c.radius("sm"), mv, mh)
	selected.border_color = c.col("info")
	selected.border_width_left = 3
	for state in ["normal", "normal_mirrored", "disabled", "disabled_mirrored"]:
		t.set_stylebox(state, name, normal)
	for state in ["hover", "hover_mirrored"]:
		t.set_stylebox(state, name, hover)
	for state in ["pressed", "pressed_mirrored", "hover_pressed", "hover_pressed_mirrored"]:
		t.set_stylebox(state, name, selected)
	t.set_stylebox("focus", name, _focus_ring(c, c.radius("sm")))
	_button_colors(t, name, c.col("text"), c.col("text"), c.col("text"), Color(c.col("muted"), 0.7))
	t.set_font("font", name, c.font("ui_medium"))
	t.set_font_size("font_size", name, c.size("small"))
