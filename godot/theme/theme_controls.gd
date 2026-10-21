extends "res://theme/theme_style.gd"

## Kyst & Stål styles for interactive controls: toggles, option buttons,
## inputs, popups, ranges, scrollbars, tabs, lists, rich text, tooltips,
## windows and separators.


static func _toggles(t: Theme, c: _Ctx) -> void:
	var on := c.col("info")
	var mark := c.col("sunken") if c.ink else c.col("raised")
	var stroke := c.col("stroke")
	for cls in ["CheckBox", "CheckButton"]:
		var box := _empty(4.0, 4.0)
		for state in ["normal", "pressed", "hover", "hover_pressed", "disabled"]:
			t.set_stylebox(state, cls, box)
		var hover := _flat(Color(c.col("raised"), 0.6) if c.ink else c.col("sunken"), c.radius("sm"), 4.0, 4.0)
		t.set_stylebox("hover", cls, hover)
		t.set_stylebox("hover_pressed", cls, hover)
		t.set_stylebox("focus", cls, _focus_ring(c, c.radius("sm")))
		t.set_color("font_color", cls, c.col("text"))
		t.set_color("font_pressed_color", cls, c.col("text"))
		t.set_color("font_hover_color", cls, c.col("text"))
		t.set_color("font_hover_pressed_color", cls, c.col("text"))
		t.set_color("font_focus_color", cls, c.col("text"))
		t.set_color("font_disabled_color", cls, Color(c.col("muted"), 0.7))
		t.set_color("font_outline_color", cls, Color(0, 0, 0, 0))
		t.set_font("font", cls, c.font("ui_regular"))
		t.set_font_size("font_size", cls, c.size("body"))
		t.set_constant("h_separation", cls, c.space("sm"))
	t.set_color("checkbox_checked_color", "CheckBox", on)
	t.set_color("checkbox_unchecked_color", "CheckBox", stroke)
	t.set_icon("checked", "CheckBox", _svg(_checkbox_svg(on, on, mark, true, 1.0)))
	t.set_icon("checked_disabled", "CheckBox", _svg(_checkbox_svg(on, on, mark, true, 0.45)))
	t.set_icon("unchecked", "CheckBox", _svg(_checkbox_svg(on, stroke, mark, false, 1.0)))
	t.set_icon("unchecked_disabled", "CheckBox", _svg(_checkbox_svg(on, stroke, mark, false, 0.45)))
	t.set_icon("radio_checked", "CheckBox", _svg(_radio_svg(on, on, mark, true, 1.0)))
	t.set_icon("radio_checked_disabled", "CheckBox", _svg(_radio_svg(on, on, mark, true, 0.45)))
	t.set_icon("radio_unchecked", "CheckBox", _svg(_radio_svg(on, stroke, mark, false, 1.0)))
	t.set_icon("radio_unchecked_disabled", "CheckBox", _svg(_radio_svg(on, stroke, mark, false, 0.45)))
	var knob := c.col("text") if c.ink else c.col("raised")
	var off_track := c.col("stroke")
	t.set_color("button_checked_color", "CheckButton", on)
	t.set_color("button_unchecked_color", "CheckButton", off_track)
	for suffix in ["", "_mirrored"]:
		t.set_icon("checked" + suffix, "CheckButton", _svg(_switch_svg(on, knob, true, 1.0)))
		t.set_icon("checked_disabled" + suffix, "CheckButton", _svg(_switch_svg(on, knob, true, 0.45)))
		t.set_icon("unchecked" + suffix, "CheckButton", _svg(_switch_svg(off_track, knob, false, 1.0)))
		t.set_icon("unchecked_disabled" + suffix, "CheckButton", _svg(_switch_svg(off_track, knob, false, 0.45)))


static func _option_buttons(t: Theme, c: _Ctx) -> void:
	var spec := _button_kind(c, "", 7.0, 12.0)
	for i in 4:
		var state: String = ["normal", "hover", "pressed", "disabled"][i]
		t.set_stylebox(state, "OptionButton", spec[0][i])
		t.set_stylebox(state + "_mirrored", "OptionButton", spec[0][i])
	t.set_stylebox("focus", "OptionButton", _focus_ring(c, c.radius("sm")))
	_button_colors(t, "OptionButton", spec[1][0], spec[1][1], spec[1][2], spec[1][3])
	t.set_font("font", "OptionButton", c.font("ui_medium"))
	t.set_font_size("font_size", "OptionButton", c.size("body"))
	t.set_icon("arrow", "OptionButton", _svg(_chevron_svg(Color.WHITE, "down")))
	t.set_constant("modulate_arrow", "OptionButton", 1)
	t.set_constant("arrow_margin", "OptionButton", c.space("sm"))
	t.set_constant("h_separation", "OptionButton", c.space("sm"))


static func _inputs(t: Theme, c: _Ctx) -> void:
	var r := c.radius("sm")
	var field_bg := c.col("sunken") if c.ink else c.col("raised")
	var normal := _bordered(field_bg, c.col("stroke"), 1, r, 6.0, 10.0)
	var focus := _focus_ring(c, r)
	var read_only := _bordered(Color(field_bg, 0.6), c.col("hairline"), 1, r, 6.0, 10.0)
	for cls in ["LineEdit", "TextEdit"]:
		t.set_stylebox("normal", cls, normal)
		t.set_stylebox("focus", cls, focus)
		t.set_stylebox("read_only", cls, read_only)
		t.set_color("font_color", cls, c.col("text"))
		t.set_color("font_selected_color", cls, c.col("text"))
		t.set_color("font_placeholder_color", cls, c.col("muted"))
		t.set_color("font_outline_color", cls, Color(0, 0, 0, 0))
		t.set_color("caret_color", cls, c.col("focus"))
		t.set_color("selection_color", cls, Color(c.col("info"), 0.32))
		t.set_font("font", cls, c.font("ui_regular"))
		t.set_font_size("font_size", cls, c.size("body"))
	t.set_color("font_uneditable_color", "LineEdit", c.col("muted"))
	t.set_color("clear_button_color", "LineEdit", c.col("muted"))
	t.set_color("clear_button_color_pressed", "LineEdit", c.col("text"))
	t.set_icon("clear", "LineEdit", _svg(_cross_svg(Color.WHITE, 12)))
	t.set_color("font_readonly_color", "TextEdit", c.col("muted"))
	t.set_color("background_color", "TextEdit", Color(0, 0, 0, 0))
	t.set_color("current_line_color", "TextEdit", Color(c.col("info"), 0.08))
	t.set_color("caret_background_color", "TextEdit", c.col("base"))
	t.set_color("word_highlighted_color", "TextEdit", Color(c.col("info"), 0.18))
	t.set_color("search_result_color", "TextEdit", Color(c.col("warn"), 0.25))
	t.set_color("search_result_border_color", "TextEdit", Color(c.col("warn"), 0.6))
	t.set_icon("tab", "TextEdit", _svg(_blank_svg(8, 8)))
	t.set_icon("space", "TextEdit", _svg(_blank_svg(8, 8)))
	# SpinBox: compact chevron buttons inside the field's right edge.
	var arrow := Color.WHITE
	for icon in ["up", "up_hover", "up_pressed", "up_disabled"]:
		t.set_icon(icon, "SpinBox", _svg(_chevron_svg(arrow, "up", 10)))
	for icon in ["down", "down_hover", "down_pressed", "down_disabled"]:
		t.set_icon(icon, "SpinBox", _svg(_chevron_svg(arrow, "down", 10)))
	t.set_icon("updown", "SpinBox", _svg(_blank_svg(10, 20)))
	for pre in ["up", "down"]:
		t.set_color(pre + "_icon_modulate", "SpinBox", c.col("muted"))
		t.set_color(pre + "_hover_icon_modulate", "SpinBox", c.col("text"))
		t.set_color(pre + "_pressed_icon_modulate", "SpinBox", c.col("focus"))
		t.set_color(pre + "_disabled_icon_modulate", "SpinBox", Color(c.col("muted"), 0.4))
		t.set_stylebox(pre + "_background", "SpinBox", _empty())
		t.set_stylebox(pre + "_background_hovered", "SpinBox", _flat(Color(c.col("info"), 0.12), r))
		t.set_stylebox(pre + "_background_pressed", "SpinBox", _flat(Color(c.col("info"), 0.24), r))
		t.set_stylebox(pre + "_background_disabled", "SpinBox", _empty())
	t.set_stylebox("field_and_buttons_separator", "SpinBox", _empty())
	t.set_stylebox("up_down_buttons_separator", "SpinBox", _empty())
	t.set_constant("buttons_width", "SpinBox", 18)
	t.set_constant("field_and_buttons_separation", "SpinBox", 2)
	t.set_constant("buttons_vertical_separation", "SpinBox", 0)
	t.set_constant("set_min_buttons_width_from_icons", "SpinBox", 0)


static func _popups(t: Theme, c: _Ctx) -> void:
	var r := c.radius("md")
	var panel := _bordered(c.col("raised"), c.col("hairline"), 1, r, 6.0, 6.0)
	_shadow(panel, Color.BLACK, 0.18 if not c.ink else 0.45, 10, 4.0)
	t.set_stylebox("panel", "PopupMenu", panel)
	t.set_stylebox("hover", "PopupMenu", _flat(Color(c.col("info"), 0.18), c.radius("sm"), 4.0, 8.0))
	t.set_stylebox("separator", "PopupMenu", _line(c.col("hairline"), false))
	t.set_stylebox("labeled_separator_left", "PopupMenu", _line(c.col("hairline"), false))
	t.set_stylebox("labeled_separator_right", "PopupMenu", _line(c.col("hairline"), false))
	t.set_color("font_color", "PopupMenu", c.col("text"))
	t.set_color("font_hover_color", "PopupMenu", c.col("text"))
	t.set_color("font_accelerator_color", "PopupMenu", c.col("muted"))
	t.set_color("font_disabled_color", "PopupMenu", Color(c.col("muted"), 0.6))
	t.set_color("font_separator_color", "PopupMenu", c.col("muted"))
	t.set_color("font_outline_color", "PopupMenu", Color(0, 0, 0, 0))
	t.set_color("font_separator_outline_color", "PopupMenu", Color(0, 0, 0, 0))
	t.set_font("font", "PopupMenu", c.font("ui_regular"))
	t.set_font("font_separator", "PopupMenu", c.font("ui_semibold"))
	t.set_font_size("font_size", "PopupMenu", c.size("body"))
	t.set_font_size("font_separator_size", "PopupMenu", c.size("caption"))
	t.set_constant("v_separation", "PopupMenu", 6)
	t.set_constant("h_separation", "PopupMenu", 8)
	t.set_constant("item_start_padding", "PopupMenu", 8)
	t.set_constant("item_end_padding", "PopupMenu", 12)
	var on := c.col("info")
	var mark := c.col("sunken") if c.ink else c.col("raised")
	var stroke := c.col("stroke")
	t.set_icon("checked", "PopupMenu", _svg(_checkbox_svg(on, on, mark, true, 1.0)))
	t.set_icon("checked_disabled", "PopupMenu", _svg(_checkbox_svg(on, on, mark, true, 0.45)))
	t.set_icon("unchecked", "PopupMenu", _svg(_checkbox_svg(on, stroke, mark, false, 1.0)))
	t.set_icon("unchecked_disabled", "PopupMenu", _svg(_checkbox_svg(on, stroke, mark, false, 0.45)))
	t.set_icon("radio_checked", "PopupMenu", _svg(_radio_svg(on, on, mark, true, 1.0)))
	t.set_icon("radio_checked_disabled", "PopupMenu", _svg(_radio_svg(on, on, mark, true, 0.45)))
	t.set_icon("radio_unchecked", "PopupMenu", _svg(_radio_svg(on, stroke, mark, false, 1.0)))
	t.set_icon("radio_unchecked_disabled", "PopupMenu", _svg(_radio_svg(on, stroke, mark, false, 0.45)))
	t.set_icon("submenu", "PopupMenu", _svg(_chevron_svg(c.col("muted"), "right")))
	t.set_icon("submenu_mirrored", "PopupMenu", _svg(_chevron_svg(c.col("muted"), "left")))
	t.set_icon("search", "PopupMenu", _svg(_blank_svg(12, 12)))


static func _ranges(t: Theme, c: _Ctx) -> void:
	var track := _flat(c.col("sunken") if not c.ink else Color(c.col("sunken"), 0.9), c.radius("sm"))
	track.border_color = c.col("hairline")
	track.set_border_width_all(1)
	t.set_stylebox("background", "ProgressBar", track)
	t.set_stylebox("fill", "ProgressBar", _flat(c.col("info"), c.radius("sm")))
	t.set_color("font_color", "ProgressBar", c.col("text"))
	t.set_color("font_outline_color", "ProgressBar", c.col("base"))
	t.set_font("font", "ProgressBar", c.font("ui_medium"))
	t.set_font_size("font_size", "ProgressBar", c.size("caption"))
	t.set_constant("outline_size", "ProgressBar", 0)
	for tone in TONES:
		if tone == "":
			continue
		var name := "ProgressBar" + tone
		t.set_type_variation(name, "ProgressBar")
		t.set_stylebox("fill", name, _flat(c.col(TONE_KEYS[tone]), c.radius("sm")))
	# Sliders: thin track, filled part in info, a round knob.
	for cls in ["HSlider", "VSlider"]:
		var rail := _flat(c.col("stroke") if c.ink else c.col("hairline"), 2, 2.0, 2.0)
		var filled := _flat(c.col("info"), 2, 2.0, 2.0)
		t.set_stylebox("slider", cls, rail)
		t.set_stylebox("grabber_area", cls, filled)
		t.set_stylebox("grabber_area_highlight", cls, filled)
		var knob_fill := c.col("raised") if not c.ink else c.col("text")
		t.set_icon("grabber", cls, _svg(_knob_svg(knob_fill, c.col("info"), 16)))
		t.set_icon("grabber_highlight", cls, _svg(_knob_svg(knob_fill, c.col("focus"), 18)))
		t.set_icon("grabber_disabled", cls, _svg(_knob_svg(Color(knob_fill, 0.5), c.col("stroke"), 16)))
		t.set_icon("tick", cls, _svg(_blank_svg(2, 6)))


static func _scroll(t: Theme, c: _Ctx) -> void:
	for cls in ["VScrollBar", "HScrollBar"]:
		var vertical: bool = cls == "VScrollBar"
		var track := _empty(2.0, 2.0)
		var grab := _flat(Color(c.col("stroke"), 0.55), 4, 0.0, 0.0)
		var grab_hl := _flat(Color(c.col("stroke"), 0.85), 4, 0.0, 0.0)
		var grab_press := _flat(c.col("muted"), 4, 0.0, 0.0)
		for sb in [grab, grab_hl, grab_press]:
			if vertical:
				sb.content_margin_left = 3
				sb.content_margin_right = 3
			else:
				sb.content_margin_top = 3
				sb.content_margin_bottom = 3
		t.set_stylebox("scroll", cls, track)
		t.set_stylebox("scroll_focus", cls, track)
		t.set_stylebox("grabber", cls, grab)
		t.set_stylebox("grabber_highlight", cls, grab_hl)
		t.set_stylebox("grabber_pressed", cls, grab_press)
		for icon in ["increment", "increment_highlight", "increment_pressed", "decrement", "decrement_highlight", "decrement_pressed"]:
			t.set_icon(icon, cls, _svg(_blank_svg(1, 1)))


static func _tabs(t: Theme, c: _Ctx) -> void:
	var mv := 8.0
	var mh := 16.0
	var unselected := _flat(Color(0, 0, 0, 0), c.radius("sm"), mv, mh)
	var hovered := _flat(Color(c.col("raised"), 0.7) if c.ink else c.col("sunken"), c.radius("sm"), mv, mh)
	var selected := _flat(c.col("raised"), c.radius("sm"), mv, mh)
	selected.border_color = c.col("salmon") if c.ink else c.col("info")
	selected.border_width_bottom = 2
	selected.corner_radius_bottom_left = 0
	selected.corner_radius_bottom_right = 0
	var disabled := _flat(Color(0, 0, 0, 0), c.radius("sm"), mv, mh)
	for cls in ["TabContainer", "TabBar"]:
		t.set_stylebox("tab_unselected", cls, unselected)
		t.set_stylebox("tab_hovered", cls, hovered)
		t.set_stylebox("tab_selected", cls, selected)
		t.set_stylebox("tab_disabled", cls, disabled)
		t.set_stylebox("tab_focus", cls, _focus_ring(c, c.radius("sm")))
		t.set_color("font_selected_color", cls, c.col("text"))
		t.set_color("font_hovered_color", cls, c.col("text"))
		t.set_color("font_unselected_color", cls, c.col("muted"))
		t.set_color("font_disabled_color", cls, Color(c.col("muted"), 0.5))
		t.set_color("font_outline_color", cls, Color(0, 0, 0, 0))
		t.set_color("drop_mark_color", cls, c.col("focus"))
		t.set_color("icon_selected_color", cls, c.col("text"))
		t.set_color("icon_hovered_color", cls, c.col("text"))
		t.set_color("icon_unselected_color", cls, c.col("muted"))
		t.set_color("icon_disabled_color", cls, Color(c.col("muted"), 0.5))
		t.set_font("font", cls, c.font("ui_semibold"))
		t.set_font_size("font_size", cls, c.size("small"))
		for icon in ["increment", "increment_highlight"]:
			t.set_icon(icon, cls, _svg(_chevron_svg(c.col("muted"), "right")))
		for icon in ["decrement", "decrement_highlight"]:
			t.set_icon(icon, cls, _svg(_chevron_svg(c.col("muted"), "left")))
		t.set_icon("drop_mark", cls, _svg(_blank_svg(4, 16)))
	t.set_stylebox("panel", "TabContainer", _card(c, c.space("md")))
	t.set_stylebox("tabbar_background", "TabContainer", _empty())
	t.set_icon("menu", "TabContainer", _svg(_chevron_svg(c.col("muted"), "down")))
	t.set_icon("menu_highlight", "TabContainer", _svg(_chevron_svg(c.col("text"), "down")))
	t.set_stylebox("button_pressed", "TabBar", _flat(c.col("sunken"), c.radius("sm")))
	t.set_stylebox("button_highlight", "TabBar", _flat(Color(c.col("info"), 0.15), c.radius("sm")))
	t.set_icon("close", "TabBar", _svg(_cross_svg(c.col("muted"), 12)))


static func _lists(t: Theme, c: _Ctx) -> void:
	var panel := _bordered(c.col("raised"), c.col("hairline"), 1, c.radius("sm"), 6.0, 6.0)
	var sel := _flat(Color(c.col("info"), 0.20), c.radius("sm"))
	var sel_focus := _bordered(Color(c.col("info"), 0.26), c.col("focus"), 1, c.radius("sm"))
	var hover := _flat(Color(c.col("info"), 0.10), c.radius("sm"))
	var cursor := _focus_ring(c, c.radius("sm"))
	for cls in ["ItemList", "Tree"]:
		t.set_stylebox("panel", cls, panel)
		t.set_stylebox("focus", cls, _empty())
		t.set_stylebox("selected", cls, sel)
		t.set_stylebox("selected_focus", cls, sel_focus)
		t.set_stylebox("hovered", cls, hover)
		t.set_stylebox("hovered_selected", cls, sel)
		t.set_stylebox("hovered_selected_focus", cls, sel_focus)
		t.set_stylebox("cursor", cls, cursor)
		t.set_stylebox("cursor_unfocused", cls, _empty())
		t.set_color("font_color", cls, c.col("text"))
		t.set_color("font_hovered_color", cls, c.col("text"))
		t.set_color("font_hovered_selected_color", cls, c.col("text"))
		t.set_color("font_selected_color", cls, c.col("text"))
		t.set_color("font_outline_color", cls, Color(0, 0, 0, 0))
		t.set_color("guide_color", cls, Color(c.col("hairline"), 0.6))
		t.set_color("scroll_hint_color", cls, Color(c.col("base"), 0.6))
		t.set_font("font", cls, c.font("ui_regular"))
		t.set_font_size("font_size", cls, c.size("body"))
		t.set_icon("scroll_hint", cls, _svg(_blank_svg(4, 4)))
	t.set_stylebox("hovered_dimmed", "Tree", hover)
	t.set_color("font_hovered_dimmed_color", "Tree", c.col("text"))
	t.set_color("font_disabled_color", "Tree", Color(c.col("muted"), 0.6))
	t.set_color("title_button_color", "Tree", c.col("text"))
	t.set_color("drop_on_item_color", "Tree", c.col("focus"))
	t.set_color("drop_position_color", "Tree", c.col("focus"))
	t.set_color("relationship_line_color", "Tree", c.col("hairline"))
	t.set_color("parent_hl_line_color", "Tree", c.col("stroke"))
	t.set_color("children_hl_line_color", "Tree", c.col("hairline"))
	t.set_color("custom_button_font_highlight", "Tree", c.col("text"))
	for box in ["button_hover", "button_pressed", "title_button_normal", "title_button_pressed", "title_button_hover",
			"custom_button", "custom_button_pressed", "custom_button_hover"]:
		t.set_stylebox(box, "Tree", _flat(c.col("sunken"), c.radius("sm"), 4.0, 6.0))
	t.set_font("title_button_font", "Tree", c.font("ui_semibold"))
	t.set_font_size("title_button_font_size", "Tree", c.size("small"))
	var on := c.col("info")
	var mark := c.col("sunken") if c.ink else c.col("raised")
	t.set_icon("checked", "Tree", _svg(_checkbox_svg(on, on, mark, true, 1.0)))
	t.set_icon("checked_disabled", "Tree", _svg(_checkbox_svg(on, on, mark, true, 0.45)))
	t.set_icon("unchecked", "Tree", _svg(_checkbox_svg(on, c.col("stroke"), mark, false, 1.0)))
	t.set_icon("unchecked_disabled", "Tree", _svg(_checkbox_svg(on, c.col("stroke"), mark, false, 0.45)))
	t.set_icon("indeterminate", "Tree", _svg(_checkbox_svg(on, on, mark, false, 1.0)))
	t.set_icon("indeterminate_disabled", "Tree", _svg(_checkbox_svg(on, on, mark, false, 0.45)))
	t.set_icon("arrow", "Tree", _svg(_chevron_svg(c.col("muted"), "down")))
	t.set_icon("arrow_collapsed", "Tree", _svg(_chevron_svg(c.col("muted"), "right")))
	t.set_icon("arrow_collapsed_mirrored", "Tree", _svg(_chevron_svg(c.col("muted"), "left")))
	t.set_icon("updown", "Tree", _svg(_chevron_svg(c.col("muted"), "down")))
	t.set_icon("select_arrow", "Tree", _svg(_chevron_svg(c.col("muted"), "down")))


static func _rich_text(t: Theme, c: _Ctx) -> void:
	var cls := "RichTextLabel"
	t.set_stylebox("normal", cls, _empty())
	t.set_stylebox("focus", cls, _empty())
	t.set_color("default_color", cls, c.col("text"))
	t.set_color("font_selected_color", cls, c.col("text"))
	t.set_color("selection_color", cls, Color(c.col("info"), 0.3))
	t.set_color("font_shadow_color", cls, Color(0, 0, 0, 0))
	t.set_color("font_outline_color", cls, c.col("base"))
	t.set_color("table_odd_row_bg", cls, Color(c.col("sunken"), 0.5))
	t.set_color("table_even_row_bg", cls, Color(0, 0, 0, 0))
	t.set_color("table_border", cls, c.col("hairline"))
	t.set_font("normal_font", cls, c.font("ui_regular"))
	t.set_font("bold_font", cls, c.font("ui_semibold"))
	t.set_font("italics_font", cls, c.font("ui_italic"))
	t.set_font("bold_italics_font", cls, c.font("ui_semibold"))
	t.set_font("mono_font", cls, c.font("ui_medium"))
	for size_name in ["normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size", "mono_font_size"]:
		t.set_font_size(size_name, cls, c.size("body"))
	t.set_constant("line_separation", cls, 3)
	t.set_icon("horizontal_rule", cls, _svg(_blank_svg(1, 1)))
	# Smaller rich-text roles (logs, descriptions) without per-node size overrides.
	for v in [["RichTextSmall", "caption"], ["RichTextBodySmall", "small"]]:
		t.set_type_variation(v[0], cls)
		for size_name in ["normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size", "mono_font_size"]:
			t.set_font_size(size_name, v[0], c.size(v[1]))


static func _tooltips_and_windows(t: Theme, c: _Ctx) -> void:
	# Tooltips are always Ink, whatever surface the hovered control sits on
	# (high-contrast Ink, opaque, on the high-contrast surfaces).
	var ink := ink_palette_for(c.tokens, c.surface)
	var tip := _bordered(Color(ink["raised"], 1.0 if c.high_contrast else 0.97), ink["hairline"], 1, c.radius("sm"), 8.0, 10.0)
	_shadow(tip, Color.BLACK, 0.35, 8, 3.0)
	t.set_stylebox("panel", "TooltipPanel", tip)
	t.set_color("font_color", "TooltipLabel", ink["text"])
	t.set_color("font_shadow_color", "TooltipLabel", Color(0, 0, 0, 0))
	t.set_color("font_outline_color", "TooltipLabel", Color(0, 0, 0, 0))
	t.set_font("font", "TooltipLabel", c.font("ui_regular"))
	t.set_font_size("font_size", "TooltipLabel", c.size("small"))
	var frame := _bordered(c.col("base"), c.col("hairline"), 1, c.radius("md"))
	_shadow(frame, Color.BLACK, 0.4, 16, 6.0)
	frame.expand_margin_top = 32.0
	frame.set_border_width_all(1)
	t.set_stylebox("embedded_border", "Window", frame)
	t.set_stylebox("embedded_unfocused_border", "Window", frame)
	t.set_color("title_color", "Window", c.col("text"))
	t.set_color("title_outline_modulate", "Window", Color(0, 0, 0, 0))
	t.set_font("title_font", "Window", c.font("ui_semibold"))
	t.set_font_size("title_font_size", "Window", c.size("body"))
	t.set_constant("title_height", "Window", 32)
	t.set_icon("close", "Window", _svg(_cross_svg(c.col("muted"))))
	t.set_icon("close_pressed", "Window", _svg(_cross_svg(c.col("text"))))
	t.set_stylebox("panel", "AcceptDialog", _flat(c.col("base"), 0, 12.0, 12.0))
	t.set_constant("buttons_separation", "AcceptDialog", c.space("sm"))


static func _separators(t: Theme, c: _Ctx) -> void:
	t.set_stylebox("separator", "HSeparator", _line(c.col("hairline"), false))
	t.set_stylebox("separator", "VSeparator", _line(c.col("hairline"), true))
	t.set_constant("separation", "HSeparator", c.space("sm"))
	t.set_constant("separation", "VSeparator", c.space("sm"))


static func _sized_variations(t: Theme, c: _Ctx) -> void:
	for cls in SIZED_CLASSES:
		for size_prefix in CONTROL_SIZES:
			if size_prefix == "" or cls == "LinkButton":
				continue
			var name: String = size_prefix + cls
			t.set_type_variation(name, cls)
			t.set_font_size("font_size", name, c.size(CONTROL_SIZES[size_prefix]))
