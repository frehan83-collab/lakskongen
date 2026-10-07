extends Control

## SaveLoadMenu v2.1 — Nordic Minimalist (scene-based).
## Layout lives in scenes/ui/SaveLoadMenu.tscn; script is wiring only:
## @onready % refs, signal hookups, slot refresh, localization.
## Dynamic content (save slots) is still built in code from SaveLoadManager.

signal close_requested
signal game_loaded(slot_name: String)
signal game_saved(slot_name: String)

var _mode: String = "load"  # "save" or "load"

@onready var _title_label: Label = %TitleLabel
@onready var _close_button: Button = %CloseButton
@onready var _slots_container: VBoxContainer = %SlotsContainer
@onready var _new_save_button: Button = %NewSaveButton

func _ready() -> void:
	_apply_localization()
	_close_button.pressed.connect(func(): close_requested.emit(); hide())
	_new_save_button.pressed.connect(_on_new_save)
	hide()

func _apply_localization() -> void:
	_update_title_text()
	_new_save_button.text = _tr_or("UI_SAVELOAD_NEW_SAVE", "💾 Nytt Lagringspunkt")

func _tr_or(key: String, fallback: String) -> String:
	var t := tr(key)
	return t if t != key else fallback

func show_load() -> void:
	_mode = "load"
	_update_title_text()
	_refresh_slots()
	show()

func show_save() -> void:
	_mode = "save"
	_update_title_text()
	_refresh_slots()
	show()

func _update_title_text() -> void:
	if _mode == "save":
		_title_label.text = _tr_or("UI_SAVELOAD_TITLE_SAVE", "Lagre Spill")
	else:
		_title_label.text = _tr_or("UI_SAVELOAD_TITLE_LOAD", "Last Spill")

func _refresh_slots() -> void:
	if not _slots_container: return
	for child in _slots_container.get_children():
		child.queue_free()

	var save_mgr = get_node_or_null("/root/SaveLoadManager")
	if not save_mgr:
		var lbl := Label.new()
		lbl.text = _tr_or("UI_SAVELOAD_MANAGER_UNAVAILABLE", "SaveLoadManager ikke tilgjengelig")
		lbl.add_theme_color_override("font_color", Color("#7B8794"))
		_slots_container.add_child(lbl)
		return

	var saves = []
	if save_mgr.has_method("get_save_list"):
		saves = save_mgr.get_save_list()

	if saves.is_empty():
		var empty_vbox := VBoxContainer.new()
		empty_vbox.add_theme_constant_override("separation", 4)
		var lbl := Label.new()
		var empty_text := _tr_or("UI_SAVELOAD_EMPTY_LOAD", "Ingen lagrede spill funnet")
		if _mode == "save":
			empty_text = _tr_or("UI_SAVELOAD_EMPTY_SAVE", "Ingen eksisterende lagringer")
		lbl.text = "💾 " + empty_text
		lbl.add_theme_color_override("font_color", Color("#7B8794"))
		lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		empty_vbox.add_child(lbl)
		var hint := Label.new()
		if _mode == "save":
			hint.text = _tr_or("UI_SAVELOAD_EMPTY_SAVE_HINT", "Trykk «Nytt Lagringspunkt» for å sikre framgangen din.")
		else:
			hint.text = _tr_or("UI_SAVELOAD_EMPTY_LOAD_HINT", "Start et nytt spill fra hovedmenyen for å komme i gang.")
		hint.add_theme_font_size_override("font_size", 12)
		hint.add_theme_color_override("font_color", Color("#7B8794"))
		hint.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		empty_vbox.add_child(hint)
		_slots_container.add_child(empty_vbox)
		return

	for save_info in saves:
		var slot = _create_slot(save_info)
		_slots_container.add_child(slot)

func _create_slot(save_info) -> PanelContainer:
	var slot := PanelContainer.new()
	var sb := StyleBoxFlat.new()
	sb.bg_color = Color("#F8F9FB")
	sb.border_color = Color("#D8DEE9")
	sb.set_border_width_all(1)
	sb.set_corner_radius_all(8)
	sb.set_content_margin_all(12)
	slot.add_theme_stylebox_override("panel", sb)

	var hbox := HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 12)
	slot.add_child(hbox)

	var info_vbox := VBoxContainer.new()
	info_vbox.size_flags_horizontal = SIZE_EXPAND_FILL
	hbox.add_child(info_vbox)

	var name_lbl := Label.new()
	var slot_name = save_info.get("name", "Unknown") if save_info is Dictionary else str(save_info)
	name_lbl.text = slot_name
	name_lbl.add_theme_font_size_override("font_size", 16)
	name_lbl.add_theme_color_override("font_color", Color("#2E3440"))
	info_vbox.add_child(name_lbl)

	var details := Label.new()
	var day = save_info.get("day", "?") if save_info is Dictionary else "?"
	var day_fmt := _tr_or("UI_SAVELOAD_DAY_FORMAT", "Dag %s")
	details.text = day_fmt % str(day)
	details.add_theme_font_size_override("font_size", 12)
	details.add_theme_color_override("font_color", Color("#7B8794"))
	info_vbox.add_child(details)

	var action_btn := Button.new()
	var load_text := _tr_or("UI_SAVELOAD_LOAD", "Last")
	var save_text := _tr_or("UI_SAVELOAD_SAVE", "Lagre")
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
		_toast(_tr_or("UI_TOAST_LOADED", "💾 Lagring lastet: %s") % slot_name, "SUCCESS")
	else:
		if save_mgr.has_method("save_game"):
			save_mgr.save_game(slot_name)
		game_saved.emit(slot_name)
		_toast(_tr_or("UI_TOAST_SAVED", "💾 Spill lagret: %s") % slot_name, "SUCCESS")
	hide()

func _toast(message: String, level: String = "INFO") -> void:
	var mgr := get_node_or_null("/root/ToastNotification")
	if mgr and mgr.has_method("show_toast"):
		mgr.show_toast(message, level, 4.0)

func _on_new_save() -> void:
	var save_mgr = get_node_or_null("/root/SaveLoadManager")
	if save_mgr and save_mgr.has_method("save_game"):
		var ts = Time.get_datetime_string_from_system().replace(":", "-")
		save_mgr.save_game("save_%s" % ts)
		game_saved.emit("save_%s" % ts)
		_toast(_tr_or("UI_TOAST_SAVED", "💾 Spill lagret: %s") % ("save_%s" % ts), "SUCCESS")
	_refresh_slots()
