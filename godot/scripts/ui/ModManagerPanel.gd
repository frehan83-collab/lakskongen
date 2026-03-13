## ModManagerPanel.gd
## Lists installed mods with enable/disable toggles, load-order dragging,
## and a detail pane showing mod metadata.
##
## Signals:
##   mods_changed()   — emitted whenever enable state or order changes
##   panel_close_requested

extends Control

# ── Signals ──────────────────────────────────────────────────────────────────

signal mods_changed()
signal panel_close_requested

# ── Colors ────────────────────────────────────────────────────────────────────

const COLOR_BG          := Color(0.08, 0.10, 0.14)
const COLOR_HEADER      := Color(0.05, 0.07, 0.10)
const COLOR_ROW_EVEN    := Color(0.10, 0.12, 0.17)
const COLOR_ROW_ODD     := Color(0.12, 0.14, 0.20)
const COLOR_ROW_HOVER   := Color(0.15, 0.20, 0.32)
const COLOR_ROW_SEL     := Color(0.10, 0.25, 0.55)
const COLOR_ENABLED     := Color(0.15, 0.70, 0.35)
const COLOR_DISABLED    := Color(0.55, 0.20, 0.20)
const COLOR_TEXT_BRIGHT := Color(0.92, 0.92, 0.95)
const COLOR_TEXT_DIM    := Color(0.55, 0.55, 0.60)
const COLOR_ACCENT      := Color(0.25, 0.55, 1.00)
const COLOR_DETAIL_BG   := Color(0.06, 0.08, 0.12)

# ── Constants ─────────────────────────────────────────────────────────────────

const ROW_H        := 44
const LIST_WIDTH   := 460
const DETAIL_WIDTH := 320
const PANEL_PAD    := 12

# ── Runtime state ─────────────────────────────────────────────────────────────

## Array of mod data dicts:
## { id, name, author, version, description, enabled, event_count,
##   research_count, species_count, scenario_count, errors }
var _mods: Array = []

var _selected_index: int = -1
var _drag_index: int     = -1
var _drag_offset_y: float = 0.0

# UI node references
var _list_container:   VBoxContainer  = null
var _detail_panel:     PanelContainer = null
var _detail_name:      Label          = null
var _detail_author:    Label          = null
var _detail_version:   Label          = null
var _detail_desc:      RichTextLabel  = null
var _detail_stats:     Label          = null
var _detail_errors:    Label          = null
var _status_label:     Label          = null

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	_build_ui()
	_refresh_from_engine()

# ── Public API ────────────────────────────────────────────────────────────────

## Reload mod list from the simulation engine / GameBridge.
func refresh() -> void:
	_refresh_from_engine()

# ── UI construction ───────────────────────────────────────────────────────────

func _build_ui() -> void:
	# Root background
	var bg := _make_panel(COLOR_BG)
	add_child(bg)
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

	var root_vbox := VBoxContainer.new()
	root_vbox.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	root_vbox.add_theme_constant_override("separation", 0)
	bg.add_child(root_vbox)

	# ── Title bar ──
	var title_bar := _make_panel(COLOR_HEADER)
	title_bar.custom_minimum_size.y = 48
	root_vbox.add_child(title_bar)

	var title_hbox := HBoxContainer.new()
	title_hbox.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	title_hbox.add_theme_constant_override("separation", 8)
	title_bar.add_child(title_hbox)

	var margin := MarginContainer.new()
	margin.add_theme_constant_override("margin_left", PANEL_PAD)
	margin.add_theme_constant_override("margin_right", PANEL_PAD)
	title_hbox.add_child(margin)
	margin.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	var inner_hbox := HBoxContainer.new()
	inner_hbox.add_theme_constant_override("separation", 8)
	margin.add_child(inner_hbox)

	var title_lbl := Label.new()
	title_lbl.text = "Modbehandler"
	title_lbl.add_theme_color_override("font_color", COLOR_TEXT_BRIGHT)
	title_lbl.add_theme_font_size_override("font_size", 18)
	title_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	inner_hbox.add_child(title_lbl)

	_status_label = Label.new()
	_status_label.add_theme_color_override("font_color", COLOR_TEXT_DIM)
	_status_label.add_theme_font_size_override("font_size", 13)
	inner_hbox.add_child(_status_label)

	var close_btn := Button.new()
	close_btn.text = "✕"
	close_btn.flat = true
	close_btn.add_theme_color_override("font_color", COLOR_TEXT_DIM)
	close_btn.add_theme_font_size_override("font_size", 18)
	close_btn.pressed.connect(_on_close_pressed)
	inner_hbox.add_child(close_btn)

	# ── Body ──
	var body_hbox := HBoxContainer.new()
	body_hbox.size_flags_vertical = Control.SIZE_EXPAND_FILL
	body_hbox.add_theme_constant_override("separation", 0)
	root_vbox.add_child(body_hbox)

	# Left: scrollable mod list
	var list_scroll := ScrollContainer.new()
	list_scroll.custom_minimum_size.x = LIST_WIDTH
	list_scroll.size_flags_vertical   = Control.SIZE_EXPAND_FILL
	list_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	body_hbox.add_child(list_scroll)

	_list_container = VBoxContainer.new()
	_list_container.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_list_container.add_theme_constant_override("separation", 1)
	list_scroll.add_child(_list_container)

	# Separator
	var sep := ColorRect.new()
	sep.custom_minimum_size.x = 1
	sep.color = Color(0.25, 0.28, 0.35)
	body_hbox.add_child(sep)

	# Right: detail pane
	_detail_panel = _make_panel(COLOR_DETAIL_BG)
	_detail_panel.custom_minimum_size.x = DETAIL_WIDTH
	_detail_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	body_hbox.add_child(_detail_panel)

	_build_detail_pane()

	# ── Bottom toolbar ──
	var toolbar := _make_panel(COLOR_HEADER)
	toolbar.custom_minimum_size.y = 42
	root_vbox.add_child(toolbar)

	var toolbar_margin := MarginContainer.new()
	toolbar_margin.add_theme_constant_override("margin_left", PANEL_PAD)
	toolbar_margin.add_theme_constant_override("margin_right", PANEL_PAD)
	toolbar_margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	toolbar.add_child(toolbar_margin)

	var toolbar_hbox := HBoxContainer.new()
	toolbar_hbox.add_theme_constant_override("separation", 8)
	toolbar_margin.add_child(toolbar_hbox)

	var open_btn := _make_button("Åpne modmappe", COLOR_ACCENT)
	open_btn.pressed.connect(_on_open_folder_pressed)
	toolbar_hbox.add_child(open_btn)

	var reload_btn := _make_button("Last inn på nytt", Color(0.25, 0.45, 0.25))
	reload_btn.pressed.connect(_on_reload_pressed)
	toolbar_hbox.add_child(reload_btn)

	toolbar_hbox.add_child(_spacer())

	var apply_btn := _make_button("Bruk endringer", COLOR(0.65, 0.45, 0.10))
	apply_btn.pressed.connect(_on_apply_pressed)
	toolbar_hbox.add_child(apply_btn)

func _build_detail_pane() -> void:
	var margin := MarginContainer.new()
	margin.add_theme_constant_override("margin_left", PANEL_PAD + 4)
	margin.add_theme_constant_override("margin_right", PANEL_PAD + 4)
	margin.add_theme_constant_override("margin_top", PANEL_PAD)
	margin.add_theme_constant_override("margin_bottom", PANEL_PAD)
	margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_detail_panel.add_child(margin)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 6)
	margin.add_child(vbox)

	_detail_name = Label.new()
	_detail_name.text = "Ingen mod valgt"
	_detail_name.add_theme_color_override("font_color", COLOR_TEXT_BRIGHT)
	_detail_name.add_theme_font_size_override("font_size", 16)
	_detail_name.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	vbox.add_child(_detail_name)

	_detail_author = Label.new()
	_detail_author.add_theme_color_override("font_color", COLOR_TEXT_DIM)
	_detail_author.add_theme_font_size_override("font_size", 12)
	vbox.add_child(_detail_author)

	_detail_version = Label.new()
	_detail_version.add_theme_color_override("font_color", COLOR_TEXT_DIM)
	_detail_version.add_theme_font_size_override("font_size", 12)
	vbox.add_child(_detail_version)

	var sep_line := ColorRect.new()
	sep_line.custom_minimum_size.y = 1
	sep_line.color = Color(0.25, 0.28, 0.35)
	vbox.add_child(sep_line)

	_detail_desc = RichTextLabel.new()
	_detail_desc.bbcode_enabled = true
	_detail_desc.add_theme_color_override("default_color", COLOR_TEXT_DIM)
	_detail_desc.add_theme_font_size_override("normal_font_size", 13)
	_detail_desc.fit_content = true
	_detail_desc.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	vbox.add_child(_detail_desc)

	var sep_line2 := ColorRect.new()
	sep_line2.custom_minimum_size.y = 1
	sep_line2.color = Color(0.25, 0.28, 0.35)
	vbox.add_child(sep_line2)

	_detail_stats = Label.new()
	_detail_stats.add_theme_color_override("font_color", COLOR_ACCENT)
	_detail_stats.add_theme_font_size_override("font_size", 12)
	_detail_stats.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	vbox.add_child(_detail_stats)

	_detail_errors = Label.new()
	_detail_errors.add_theme_color_override("font_color", Color(1.0, 0.45, 0.35))
	_detail_errors.add_theme_font_size_override("font_size", 11)
	_detail_errors.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	vbox.add_child(_detail_errors)

# ── Data loading ──────────────────────────────────────────────────────────────

func _refresh_from_engine() -> void:
	_mods.clear()
	_selected_index = -1

	# Try to fetch mod data from the C# engine via GameBridge
	if Engine.has_singleton("GameBridge"):
		var bridge = Engine.get_singleton("GameBridge")
		if bridge.has_method("GetModList"):
			var raw = bridge.GetModList()
			if raw is Array:
				_mods = raw

	# If no engine data, show placeholder mods for UI testing
	if _mods.is_empty():
		_mods = _get_placeholder_mods()

	_rebuild_list()
	_update_status_label()

func _get_placeholder_mods() -> Array:
	return [
		{
			"id": "no_mods_found",
			"name": "Ingen mods funnet",
			"author": "—",
			"version": "—",
			"description": "Legg mods i mappen mods/ i spillkatalogen.\nHver mod skal ha en manifest.json-fil.",
			"enabled": false,
			"event_count": 0,
			"research_count": 0,
			"species_count": 0,
			"scenario_count": 0,
			"errors": []
		}
	]

# ── List rendering ────────────────────────────────────────────────────────────

func _rebuild_list() -> void:
	for child in _list_container.get_children():
		child.queue_free()

	if _mods.is_empty():
		var empty_lbl := Label.new()
		empty_lbl.text = "Ingen mods funnet.\nPlasser mods i mappen mods/."
		empty_lbl.add_theme_color_override("font_color", COLOR_TEXT_DIM)
		empty_lbl.add_theme_font_size_override("font_size", 13)
		empty_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		_list_container.add_child(empty_lbl)
		return

	# Column header
	var header := _make_panel(COLOR_HEADER)
	header.custom_minimum_size.y = 28
	_list_container.add_child(header)

	var header_margin := MarginContainer.new()
	header_margin.add_theme_constant_override("margin_left", 48)
	header_margin.add_theme_constant_override("margin_right", PANEL_PAD)
	header_margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	header.add_child(header_margin)

	var hdr_hbox := HBoxContainer.new()
	hdr_hbox.add_theme_constant_override("separation", 8)
	header_margin.add_child(hdr_hbox)

	for col_text in ["Mod", "Versjon", "Forfatter", "Status"]:
		var lbl := Label.new()
		lbl.text = col_text
		lbl.add_theme_color_override("font_color", COLOR_TEXT_DIM)
		lbl.add_theme_font_size_override("font_size", 11)
		if col_text == "Mod":
			lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		hdr_hbox.add_child(lbl)

	# Mod rows
	for i in range(_mods.size()):
		var row := _build_mod_row(i)
		_list_container.add_child(row)

func _build_mod_row(index: int) -> Control:
	var mod: Dictionary = _mods[index]
	var is_enabled: bool = mod.get("enabled", false)
	var is_selected: bool = (index == _selected_index)

	var row_color := COLOR_ROW_EVEN if (index % 2 == 0) else COLOR_ROW_ODD
	if is_selected:
		row_color = COLOR_ROW_SEL

	var row := _make_panel(row_color)
	row.custom_minimum_size.y = ROW_H
	row.set_meta("mod_index", index)

	var margin := MarginContainer.new()
	margin.add_theme_constant_override("margin_left", 8)
	margin.add_theme_constant_override("margin_right", 8)
	margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	row.add_child(margin)

	var hbox := HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 8)
	margin.add_child(hbox)

	# Drag handle
	var drag_lbl := Label.new()
	drag_lbl.text = "⠿"
	drag_lbl.add_theme_color_override("font_color", COLOR_TEXT_DIM)
	drag_lbl.add_theme_font_size_override("font_size", 16)
	drag_lbl.custom_minimum_size.x = 20
	drag_lbl.mouse_default_cursor_shape = Control.CURSOR_VSIZE
	drag_lbl.set_meta("drag_row", index)
	hbox.add_child(drag_lbl)

	# Enable/disable toggle
	var toggle := CheckBox.new()
	toggle.button_pressed = is_enabled
	toggle.text = ""
	toggle.custom_minimum_size.x = 24
	toggle.toggled.connect(_on_mod_toggle.bind(index))
	hbox.add_child(toggle)

	# Mod name
	var name_lbl := Label.new()
	name_lbl.text = mod.get("name", "?")
	var has_errors := not (mod.get("errors", []) as Array).is_empty()
	var name_color := COLOR_TEXT_BRIGHT
	if not is_enabled:
		name_color = COLOR_TEXT_DIM
	elif has_errors:
		name_color = Color(1.0, 0.65, 0.35)
	name_lbl.add_theme_color_override("font_color", name_color)
	name_lbl.add_theme_font_size_override("font_size", 14)
	name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	hbox.add_child(name_lbl)

	# Version
	var ver_lbl := Label.new()
	ver_lbl.text = mod.get("version", "?")
	ver_lbl.add_theme_color_override("font_color", COLOR_TEXT_DIM)
	ver_lbl.add_theme_font_size_override("font_size", 12)
	ver_lbl.custom_minimum_size.x = 60
	hbox.add_child(ver_lbl)

	# Author
	var auth_lbl := Label.new()
	auth_lbl.text = mod.get("author", "?")
	auth_lbl.add_theme_color_override("font_color", COLOR_TEXT_DIM)
	auth_lbl.add_theme_font_size_override("font_size", 12)
	auth_lbl.custom_minimum_size.x = 90
	hbox.add_child(auth_lbl)

	# Status badge
	var status_lbl := Label.new()
	if has_errors:
		status_lbl.text = "⚠ Feil"
		status_lbl.add_theme_color_override("font_color", Color(1.0, 0.55, 0.25))
	elif is_enabled:
		status_lbl.text = "● Aktiv"
		status_lbl.add_theme_color_override("font_color", COLOR_ENABLED)
	else:
		status_lbl.text = "○ Deaktivert"
		status_lbl.add_theme_color_override("font_color", COLOR_DISABLED)
	status_lbl.add_theme_font_size_override("font_size", 12)
	status_lbl.custom_minimum_size.x = 90
	hbox.add_child(status_lbl)

	# Make row clickable via a transparent button overlay
	var click_area := Button.new()
	click_area.flat = true
	click_area.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	click_area.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	click_area.pressed.connect(_on_row_clicked.bind(index))
	click_area.z_index = 1
	row.add_child(click_area)

	return row

# ── Detail pane ───────────────────────────────────────────────────────────────

func _show_detail(index: int) -> void:
	if index < 0 or index >= _mods.size():
		_detail_name.text  = "Ingen mod valgt"
		_detail_author.text = ""
		_detail_version.text = ""
		_detail_desc.text  = ""
		_detail_stats.text = ""
		_detail_errors.text = ""
		return

	var mod: Dictionary = _mods[index]
	_detail_name.text    = mod.get("name", "Ukjent mod")
	_detail_author.text  = "Forfatter: " + mod.get("author", "Ukjent")
	_detail_version.text = "Versjon: "   + mod.get("version", "?")
	_detail_desc.text    = "[color=#8899aa]" + mod.get("description", "") + "[/color]"

	var stats_parts: Array = []
	var ec: int = mod.get("event_count",    0)
	var rc: int = mod.get("research_count", 0)
	var sc: int = mod.get("species_count",  0)
	var nc: int = mod.get("scenario_count", 0)
	if ec > 0: stats_parts.append("%d hendelse%s" % [ec, "r" if ec != 1 else ""])
	if rc > 0: stats_parts.append("%d forskningsnoder" % rc)
	if sc > 0: stats_parts.append("%d fiskearter" % sc)
	if nc > 0: stats_parts.append("%d scenario%s" % [nc, "er" if nc != 1 else ""])
	_detail_stats.text = "Innhold: " + (", ".join(stats_parts) if not stats_parts.is_empty() else "ingen data")

	var errors: Array = mod.get("errors", [])
	if errors.is_empty():
		_detail_errors.text = ""
	else:
		_detail_errors.text = "⚠ Feil:\n" + "\n".join(errors)

# ── Status label ──────────────────────────────────────────────────────────────

func _update_status_label() -> void:
	var total   := _mods.size()
	var enabled := _mods.filter(func(m): return m.get("enabled", false)).size()
	_status_label.text = "%d av %d aktive" % [enabled, total]

# ── Event handlers ────────────────────────────────────────────────────────────

func _on_row_clicked(index: int) -> void:
	_selected_index = index
	_rebuild_list()
	_show_detail(index)

func _on_mod_toggle(is_on: bool, index: int) -> void:
	if index < 0 or index >= _mods.size(): return
	_mods[index]["enabled"] = is_on
	_rebuild_list()
	_update_status_label()

func _on_close_pressed() -> void:
	emit_signal("panel_close_requested")

func _on_open_folder_pressed() -> void:
	# Open the mods directory in the OS file manager
	var mods_path := OS.get_executable_path().get_base_dir().path_join("mods")
	if DirAccess.dir_exists_absolute(mods_path):
		OS.shell_open(mods_path)
	else:
		OS.shell_open(OS.get_executable_path().get_base_dir())

func _on_reload_pressed() -> void:
	if Engine.has_singleton("GameBridge"):
		var bridge = Engine.get_singleton("GameBridge")
		if bridge.has_method("ReloadMods"):
			bridge.ReloadMods()
	_refresh_from_engine()

func _on_apply_pressed() -> void:
	# Push enabled/order state back to the engine
	if Engine.has_singleton("GameBridge"):
		var bridge = Engine.get_singleton("GameBridge")
		if bridge.has_method("ApplyModSettings"):
			var settings := []
			for mod in _mods:
				settings.append({
					"id":      mod.get("id", ""),
					"enabled": mod.get("enabled", false)
				})
			bridge.ApplyModSettings(settings)
	emit_signal("mods_changed")

# ── Drag-to-reorder ───────────────────────────────────────────────────────────
# Lightweight drag: press on drag handle, move mouse, release to drop.

func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_LEFT and not event.pressed:
			_drag_index = -1

	if event is InputEventMouseMotion and _drag_index >= 0:
		_handle_drag_move(event.global_position.y)

func _handle_drag_move(global_y: float) -> void:
	if _list_container == null: return
	var local_y := _list_container.get_local_mouse_position().y
	var target  := int(local_y / ROW_H)
	target = clamp(target, 0, _mods.size() - 1)
	if target != _drag_index:
		var item = _mods[_drag_index]
		_mods.remove_at(_drag_index)
		_mods.insert(target, item)
		if _selected_index == _drag_index:
			_selected_index = target
		_drag_index = target
		_rebuild_list()

# ── Helpers ───────────────────────────────────────────────────────────────────

func _make_panel(bg_color: Color) -> PanelContainer:
	var pc := PanelContainer.new()
	var style := StyleBoxFlat.new()
	style.bg_color = bg_color
	pc.add_theme_stylebox_override("panel", style)
	return pc

func _make_button(label_text: String, color: Color) -> Button:
	var btn := Button.new()
	btn.text = label_text
	btn.add_theme_color_override("font_color", COLOR_TEXT_BRIGHT)
	btn.add_theme_font_size_override("font_size", 13)
	var style := StyleBoxFlat.new()
	style.bg_color = color.darkened(0.2)
	style.set_corner_radius_all(4)
	style.set_content_margin_all(6)
	btn.add_theme_stylebox_override("normal", style)
	var hover_style := style.duplicate() as StyleBoxFlat
	hover_style.bg_color = color
	btn.add_theme_stylebox_override("hover", hover_style)
	return btn

func _spacer() -> Control:
	var s := Control.new()
	s.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	return s

# Workaround: Color() constructor inside a const initializer can't call static
func COLOR(r: float, g: float, b: float) -> Color:
	return Color(r, g, b)
