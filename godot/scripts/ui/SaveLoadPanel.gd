
extends Control

class_name SaveLoadPanel

# Theme constants (assuming LakskongenTheme is globally accessible or autoloaded)
# For this example, we'll define them directly as if they were from LakskongenTheme
const BG = Color("#1A1A1A")
const PANEL = Color("#2A2A2A")
const PANEL_ALT = Color("#3A3A3A")
const ACCENT = Color("#00A0B0")
const ACCENT2 = Color("#00B0A0")
const TEXT = Color("#FFFFFF")
const MUTED = Color("#AAAAAA")
const DANGER = Color("#FF0000")
const SUCCESS = Color("#00FF00")
const WARNING = Color("#FFFF00")
const GOLD = Color("#FFD700")

const FONT_SM = 14
const FONT_MD = 16
const FONT_LG = 20
const FONT_XL = 24
const FONT_XXL = 32
const FONT_TITLE = 40

const SPACE_SM = 8
const SPACE_MD = 16
const SPACE_LG = 24

var save_slots_container: VBoxContainer

func _init():
    name = "SaveLoadPanel"
    size_flags_horizontal = SIZE_EXPAND_FILL
    size_flags_vertical = SIZE_EXPAND_FILL

    set_anchors_and_offsets_preset(PRESET_FULL_RECT)

    # Main container for everything
    var main_vbox = VBoxContainer.new()
    main_vbox.size_flags_horizontal = SIZE_EXPAND_FILL
    main_vbox.size_flags_vertical = SIZE_EXPAND_FILL
    main_vbox.add_theme_constant_override("separation", SPACE_MD)
    add_child(main_vbox)

    # Top Section: Title and Quick Save/Load
    var header_section = create_section_header("💾 SAVE & LOAD")
    main_vbox.add_child(header_section)

    var quick_save_load_hbox = HBoxContainer.new()
    quick_save_load_hbox.add_theme_constant_override("separation", SPACE_SM)
    main_vbox.add_child(quick_save_load_hbox)

    var quick_save_button = create_button("QUICK SAVE", true, false)
    quick_save_button.pressed.connect(Callable(self, "_on_quick_save"))
    quick_save_load_hbox.add_child(quick_save_button)

    var quick_load_button = create_button("QUICK LOAD", false, false)
    quick_load_button.pressed.connect(Callable(self, "_on_quick_load"))
    quick_save_load_hbox.add_child(quick_load_button)

    main_vbox.add_child(create_divider(MUTED))

    # Save Slots Section
    var save_slots_header = create_section_header("Save Slots")
    main_vbox.add_child(save_slots_header)

    var scroll_container = ScrollContainer.new()
    scroll_container.size_flags_horizontal = SIZE_EXPAND_FILL
    scroll_container.size_flags_vertical = SIZE_EXPAND_FILL
    main_vbox.add_child(scroll_container)

    save_slots_container = VBoxContainer.new()
    save_slots_container.size_flags_horizontal = SIZE_EXPAND_FILL
    save_slots_container.add_theme_constant_override("separation", SPACE_SM)
    scroll_container.add_child(save_slots_container)

    _create_save_slots_ui()


func _ready():
    _update_all_save_slots_ui()

#region UI Helper Functions (mimicking LakskongenTheme)
func create_label(text: String, font_size: int, color: Color) -> Label:
    var label = Label.new()
    label.text = text
    label.add_theme_color_override("font_color", color)
    label.add_theme_font_size_override("font_size", font_size)
    return label

func create_button(text: String, accent: bool, danger: bool) -> Button:
    var button = Button.new()
    button.text = text
    button.set_h_size_flags(SIZE_EXPAND_FILL)
    var stylebox = StyleBoxFlat.new()
    stylebox.border_width = 2
    stylebox.border_color = TEXT
    stylebox.corner_radius_top_left = 5
    stylebox.corner_radius_top_right = 5
    stylebox.corner_radius_bottom_left = 5
    stylebox.corner_radius_bottom_right = 5
    stylebox.content_margin_left = SPACE_SM
    stylebox.content_margin_right = SPACE_SM
    stylebox.content_margin_top = SPACE_SM / 2
    stylebox.content_margin_bottom = SPACE_SM / 2

    if accent:
        stylebox.bg_color = ACCENT
        stylebox.border_color = ACCENT
        button.add_theme_color_override("font_color", TEXT)
    elif danger:
        stylebox.bg_color = DANGER
        stylebox.border_color = DANGER
        button.add_theme_color_override("font_color", TEXT)
    else:
        stylebox.bg_color = PANEL_ALT
        stylebox.border_color = MUTED
        button.add_theme_color_override("font_color", TEXT)

    button.add_theme_stylebox_override("normal", stylebox)

    var hovered_stylebox = stylebox.duplicate()
    hovered_stylebox.bg_color = (ACCENT if accent else (DANGER if danger else PANEL_ALT)).lightened(0.2)
    hovered_stylebox.border_color = (ACCENT if accent else (DANGER if danger else MUTED)).lightened(0.4)
    button.add_theme_stylebox_override("hover", hovered_stylebox)
    
    var pressed_stylebox = stylebox.duplicate()
    pressed_stylebox.bg_color = (ACCENT if accent else (DANGER if danger else PANEL_ALT)).darkened(0.2)
    pressed_stylebox.border_color = (ACCENT if accent else (DANGER if danger else MUTED)).darkened(0.4)
    button.add_theme_stylebox_override("pressed", pressed_stylebox)

    return button

func create_section_header(text: String) -> VBoxContainer:
    var section = VBoxContainer.new()
    section.add_child(create_label(text, FONT_LG, ACCENT))
    section.add_child(create_divider(ACCENT))
    section.add_theme_constant_override("separation", SPACE_SM / 2)
    return section

func create_styled_panel() -> PanelContainer:
    var panel = PanelContainer.new()
    var stylebox = StyleBoxFlat.new()
    stylebox.bg_color = PANEL
    stylebox.border_width = 1
    stylebox.border_color = MUTED
    stylebox.corner_radius_top_left = 5
    stylebox.corner_radius_top_right = 5
    stylebox.corner_radius_bottom_left = 5
    stylebox.corner_radius_bottom_right = 5
    panel.add_theme_stylebox_override("panel", stylebox)
    panel.add_theme_constant_override("content_margin_left", SPACE_SM)
    panel.add_theme_constant_override("content_margin_right", SPACE_SM)
    panel.add_theme_constant_override("content_margin_top", SPACE_SM)
    panel.add_theme_constant_override("content_margin_bottom", SPACE_SM)
    return panel

func create_stat(label_text: String, value_text: String, color: Color) -> HBoxContainer:
    var hbox = HBoxContainer.new()
    hbox.add_child(create_label(label_text, FONT_SM, MUTED))
    hbox.add_child(create_label(value_text, FONT_SM, color))
    return hbox

func create_divider(color: Color) -> ColorRect:
    var divider = ColorRect.new()
    divider.custom_minimum_size = Vector2(0, 2) # Height of 2 pixels
    divider.size_flags_horizontal = SIZE_EXPAND_FILL
    divider.color = color
    return divider
#endregion

func _create_save_slots_ui():
    for i in range(1, 6): # Slots 1-5
        var slot_hbox = HBoxContainer.new()
        slot_hbox.add_theme_constant_override("separation", SPACE_SM)
        slot_hbox.size_flags_horizontal = SIZE_EXPAND_FILL
        save_slots_container.add_child(slot_hbox)

        var slot_panel = create_styled_panel()
        slot_panel.size_flags_horizontal = SIZE_EXPAND_FILL
        slot_hbox.add_child(slot_panel)

        var slot_vbox_info = VBoxContainer.new()
        slot_vbox_info.add_theme_constant_override("separation", 2)
        slot_panel.add_child(slot_vbox_info)

        var slot_num_label = create_label("Slot %d" % i, FONT_LG, TEXT)
        slot_num_label.name = "SlotNumLabel_%d" % i
        slot_vbox_info.add_child(slot_num_label)

        var slot_info_label = create_label("Empty", FONT_SM, MUTED)
        slot_info_label.name = "SlotInfoLabel_%d" % i
        slot_vbox_info.add_child(slot_info_label)

        var slot_date_label = create_label("", FONT_SM, MUTED)
        slot_date_label.name = "SlotDateLabel_%d" % i
        slot_vbox_info.add_child(slot_date_label)

        var slot_buttons_vbox = VBoxContainer.new()
        slot_buttons_vbox.add_theme_constant_override("separation", SPACE_SM / 2)
        slot_hbox.add_child(slot_buttons_vbox)

        var save_button = create_button("SAVE", true, false)
        save_button.pressed.connect(Callable(self, "_on_save").bind(i))
        slot_buttons_vbox.add_child(save_button)

        var load_button = create_button("LOAD", false, false)
        load_button.pressed.connect(Callable(self, "_on_load").bind(i))
        slot_buttons_vbox.add_child(load_button)

        var delete_button = create_button("DELETE", false, true)
        delete_button.pressed.connect(Callable(self, "_on_delete").bind(i))
        slot_buttons_vbox.add_child(delete_button)

func _update_save_slot_ui(slot_number: int):
    var slot_info_label = save_slots_container.find_child("SlotInfoLabel_%d" % slot_number, true, false)
    var slot_date_label = save_slots_container.find_child("SlotDateLabel_%d" % slot_number, true, false)

    var save_file_path = "user://save_slot_%d.json" % slot_number
    if FileAccess.file_exists(save_file_path):
        var file = FileAccess.open(save_file_path, FileAccess.READ)
        var content = file.get_as_text()
        file.close()
        var json_result = JSON.parse_string(content)
        if json_result is Dictionary:
            var day = json_result.get("day", 0)
            var cash = json_result.get("cash", 0)
            var save_time = json_result.get("save_time", "")
            slot_info_label.text = "Day %d — Cash: %d NOK" % [day, cash]
            slot_date_label.text = "Saved: %s" % save_time
        else:
            slot_info_label.text = tr("Corrupted Save")
            slot_date_label.text = ""
    else:
        slot_info_label.text = tr("Empty")
        slot_date_label.text = ""

func _update_all_save_slots_ui():
    for i in range(1, 6):
        _update_save_slot_ui(i)

#region Save/Load Logic
func _on_save(slot: int):
    print("Saving to slot %d" % slot)
    var save_data = {
        "day": 142, # Dummy value
        "cash": 25000000, # Dummy value
        "fish_count": 100000, # Dummy value
        "research_points": 500, # Dummy value
        "save_time": Time.get_datetime_string_from_system()
    }

    var json_string = JSON.stringify(save_data, "\t") # Pretty print with tab indentation
    var save_file_path = "user://save_slot_%d.json" % slot
    var file = FileAccess.open(save_file_path, FileAccess.WRITE)
    if file:
        file.store_string(json_string)
        file.close()
        print("Game saved successfully to %s" % save_file_path)
        _update_save_slot_ui(slot)
    else:
        print("Error saving game to %s" % save_file_path)

func _on_load(slot: int):
    print("Loading from slot %d" % slot)
    var save_file_path = "user://save_slot_%d.json" % slot
    if FileAccess.file_exists(save_file_path):
        var file = FileAccess.open(save_file_path, FileAccess.READ)
        var content = file.get_as_text()
        file.close()
        var json_result = JSON.parse_string(content)
        if json_result is Dictionary:
            print("Loaded data: %s" % json_result)
            # Here you would typically apply the loaded data to your game state
            # For now, just print it and update the UI
            _update_save_slot_ui(slot)
            print("Game loaded successfully from %s" % save_file_path)
        else:
            print("Error parsing JSON from %s" % save_file_path)
    else:
        print("No save file found for slot %d" % slot)

func _on_quick_save():
    _on_save(0) # Slot 0 for quick save

func _on_quick_load():
    _on_load(0) # Slot 0 for quick load

func _on_delete(slot: int):
    print("Deleting save from slot %d" % slot)
    var save_file_path = "user://save_slot_%d.json" % slot
    if FileAccess.file_exists(save_file_path):
        var dir = DirAccess.open("user://")
        if dir:
            dir.remove(save_file_path.get_file()) # Only remove the file name, not the path
            print("Save file %s deleted." % save_file_path)
            _update_save_slot_ui(slot)
        else:
            print("Error opening user:// directory to delete file.")
    else:
        print("No save file found to delete for slot %d" % slot)

func _input(event: InputEvent) -> void:
    if event.is_action_pressed("ui_cancel"):
        get_viewport().set_input_as_handled()
        _on_close_requested()

func _on_close_requested() -> void:
    hide()

#endregion
