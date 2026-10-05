extends Control

## SaveLoadMenu v2.0 — Nordic Minimalist, fully programmatic.

signal close_requested
signal game_loaded(slot_name: String)
signal game_saved(slot_name: String)

var _mode: String = "load"  # "save" or "load"
var _slots_container: VBoxContainer
var _title_label: Label

func _ready() -> void:
	_build_ui()
	hide()

func _build_ui() -> void:
	for child in get_children():
		child.queue_free()

	var overlay = ColorRect.new()
	overlay.color = Color(0.18, 0.20, 0.25, 0.65)
	overlay.set_anchors_and_offsets_preset(PRESET_FULL_RECT)
	overlay.mouse_filter = MOUSE_FILTER_STOP
	add_child(overlay)

	var panel = PanelContainer.new()
	panel.custom_minimum_size = Vector2(600, 450)
	panel.anchor_left = 0.5; panel.anchor_right = 0.5
	panel.anchor_top = 0.5; panel.anchor_bottom = 0.5
	panel.offset_left = -300; panel.offset_right = 300
	panel.offset_top = -225; panel.offset_bottom = 225
	var sb = StyleBoxFlat.new()
	sb.bg_color = Color("#FFFFFF")
	sb.set_corner_radius_all(12)
	sb.set_content_margin_all(20)
	panel.add_theme_stylebox_override("panel", sb)
	add_child(panel)

	var vbox = VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 12)
	panel.add_child(vbox)

	# Header
	var header = HBoxContainer.new()
	vbox.add_child(header)
	_title_label = Label.new()
	_title_label.text = tr("UI_SAVELOAD_TITLE_LOAD") if tr("UI_SAVELOAD_TITLE_LOAD") != "ui.saveload.title_load" else "Last Spill"
	_title_label.add_theme_font_size_override("font_size", 22)
	_title_label.add_theme_color_override("font_color", Color("#2E3440"))
	_title_label.size_flags_horizontal = SIZE_EXPAND_FILL
	header.add_child(_title_label)
	var close_btn = Button.new()
	close_btn.text = "✕"
	close_btn.pressed.connect(func(): close_requested.emit(); hide())
	header.add_child(close_btn)

	# Scroll + slots
	var scroll = ScrollContainer.new()
	scroll.size_flags_vertical = SIZE_EXPAND_FILL
	vbox.add_child(scroll)
	_slots_container = VBoxContainer.new()
	_slots_container.size_flags_horizontal = SIZE_EXPAND_FILL
	_slots_container.add_theme_constant_override("separation", 8)
	scroll.add_child(_slots_container)

	# New save button (only shown in save mode)
	var new_save_btn = Button.new()
	new_save_btn.text = tr("UI_SAVELOAD_NEW_SAVE") if tr("UI_SAVELOAD_NEW_SAVE") != "ui.saveload.new_save" else "💾 Nytt Lagringspunkt"
	new_save_btn.name = "NewSaveButton"
	var btn_sb = StyleBoxFlat.new()
	btn_sb.bg_color = Color("#0A4D68")
	btn_sb.set_corner_radius_all(4)
	btn_sb.set_content_margin_all(10)
	new_save_btn.add_theme_stylebox_override("normal", btn_sb)
	new_save_btn.add_theme_color_override("font_color", Color.WHITE)
	new_save_btn.pressed.connect(_on_new_save)
	vbox.add_child(new_save_btn)

func show_load() -> void:
	_mode = "load"
	if _title_label: _title_label.text = tr("UI_SAVELOAD_TITLE_LOAD") if tr("UI_SAVELOAD_TITLE_LOAD") != "ui.saveload.title_load" else "Last Spill"
	_refresh_slots()
	show()

func show_save() -> void:
	_mode = "save"
	if _title_label: _title_label.text = tr("UI_SAVELOAD_TITLE_SAVE") if tr("UI_SAVELOAD_TITLE_SAVE") != "ui.saveload.title_save" else "Lagre Spill"
	_refresh_slots()
	show()

func _refresh_slots() -> void:
	if not _slots_container: return
	for child in _slots_container.get_children():
		child.queue_free()

	var save_mgr = get_node_or_null("/root/SaveLoadManager")
	if not save_mgr:
		var lbl = Label.new()
		lbl.text = tr("UI_SAVELOAD_MANAGER_UNAVAILABLE") if tr("UI_SAVELOAD_MANAGER_UNAVAILABLE") != "ui.saveload.manager_unavailable" else "SaveLoadManager ikke tilgjengelig"
		lbl.add_theme_color_override("font_color", Color("#7B8794"))
		_slots_container.add_child(lbl)
		return

	var saves = []
	if save_mgr.has_method("get_save_list"):
		saves = save_mgr.get_save_list()

	if saves.is_empty():
		var lbl = Label.new()
		var empty_text = tr("UI_SAVELOAD_EMPTY_LOAD") if tr("UI_SAVELOAD_EMPTY_LOAD") != "ui.saveload.empty_load" else "Ingen lagrede spill funnet"
		if _mode == "save":
			empty_text = tr("UI_SAVELOAD_EMPTY_SAVE") if tr("UI_SAVELOAD_EMPTY_SAVE") != "ui.saveload.empty_save" else "Ingen eksisterende lagringer"
		lbl.text = empty_text
		lbl.add_theme_color_override("font_color", Color("#7B8794"))
		_slots_container.add_child(lbl)
		return

	for save_info in saves:
		var slot = _create_slot(save_info)
		_slots_container.add_child(slot)

func _create_slot(save_info) -> PanelContainer:
	var slot = PanelContainer.new()
	var sb = StyleBoxFlat.new()
	sb.bg_color = Color("#F8F9FB")
	sb.border_color = Color("#D8DEE9")
	sb.set_border_width_all(1)
	sb.set_corner_radius_all(8)
	sb.set_content_margin_all(12)
	slot.add_theme_stylebox_override("panel", sb)

	var hbox = HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 12)
	slot.add_child(hbox)

	var info_vbox = VBoxContainer.new()
	info_vbox.size_flags_horizontal = SIZE_EXPAND_FILL
	hbox.add_child(info_vbox)

	var name_lbl = Label.new()
	var slot_name = save_info.get("name", "Unknown") if save_info is Dictionary else str(save_info)
	name_lbl.text = slot_name
	name_lbl.add_theme_font_size_override("font_size", 16)
	name_lbl.add_theme_color_override("font_color", Color("#2E3440"))
	info_vbox.add_child(name_lbl)

	var details = Label.new()
	var day = save_info.get("day", "?") if save_info is Dictionary else "?"
	var day_fmt = tr("UI_SAVELOAD_DAY_FORMAT") if tr("UI_SAVELOAD_DAY_FORMAT") != "ui.saveload.day_format" else "Dag %s"
	details.text = day_fmt % str(day)
	details.add_theme_font_size_override("font_size", 12)
	details.add_theme_color_override("font_color", Color("#7B8794"))
	info_vbox.add_child(details)

	var action_btn = Button.new()
	var load_text = tr("UI_SAVELOAD_LOAD") if tr("UI_SAVELOAD_LOAD") != "ui.saveload.load" else "Last"
	var save_text = tr("UI_SAVELOAD_SAVE") if tr("UI_SAVELOAD_SAVE") != "ui.saveload.save" else "Lagre"
	action_btn.text = load_text if _mode == "load" else save_text
	action_btn.pressed.connect(func(): _on_slot_action(slot_name))
	hbox.add_child(action_btn)

	return slot

func _on_slot_action(slot_name: String) -> void:
	var save_mgr = get_node_or_null("/root/SaveLoadManager")
	if not save_mgr: return
	if _mode == "load":
		if save_mgr.has_method("load_game"):
			save_mgr.load_game(slot_name)
		game_loaded.emit(slot_name)
	else:
		if save_mgr.has_method("save_game"):
			save_mgr.save_game(slot_name)
		game_saved.emit(slot_name)
	hide()

func _on_new_save() -> void:
	var save_mgr = get_node_or_null("/root/SaveLoadManager")
	if save_mgr and save_mgr.has_method("save_game"):
		var ts = Time.get_datetime_string_from_system().replace(":", "-")
		save_mgr.save_game("save_%s" % ts)
		game_saved.emit("save_%s" % ts)
	_refresh_slots()
