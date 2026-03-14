extends Control

signal save_selected(slot_id: String)
signal load_selected(slot_id: String)
signal delete_selected(slot_id: String)
signal close_requested

@onready var save_slots_container = $Panel/ScrollContainer/SaveSlotsContainer
@onready var close_button = $Panel/CloseButton
@onready var mode_label = $Panel/TitleLabel # E.g. "Save Game" or "Load Game"

# Reference to the C# SaveManager via an autoload/Node if available
# e.g., var save_manager = get_node("/root/SaveManager")

enum MenuMode { SAVE, LOAD }
var current_mode = MenuMode.LOAD

func _ready():
    close_button.pressed.connect(_on_close_button_pressed)
    _setup_tooltips()
    refresh_slots()


func _input(event: InputEvent) -> void:
    if visible and event.is_action_pressed("ui_cancel"):
        _on_close_button_pressed()
        get_viewport().set_input_as_handled()

func open(mode: MenuMode):
    current_mode = mode
    mode_label.text = tr("ui.saveload.save") if mode == MenuMode.SAVE else tr("ui.saveload.load")
    refresh_slots()
    show()

func refresh_slots():
    # Clear existing slots
    for child in save_slots_container.get_children():
        child.queue_free()

    # Fetch from C# SaveManager
    # Example mock data:
    var saves = [
        {"id": "slot1", "name": "Farm Alpha", "date": "2026-03-10", "day": 45, "cash": 150000},
        {"id": "autosave", "name": "Farm Alpha (Autosave)", "date": "2026-03-11", "day": 46, "cash": 152000}
    ]

    for save in saves:
        var slot_ui = _create_slot_ui(save)
        save_slots_container.add_child(slot_ui)

    # In SAVE mode, add a "New Save" slot at the top
    if current_mode == MenuMode.SAVE:
        var new_slot_ui = _create_new_save_slot_ui()
        save_slots_container.add_child(new_slot_ui)
        save_slots_container.move_child(new_slot_ui, 0)

func _create_slot_ui(data: Dictionary) -> Control:
    var slot = HBoxContainer.new()
    var info_label = Label.new()
    info_label.text = "%s - Day %d - $%d\n%s" % [data.name, data.day, data.cash, data.date]
    info_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    slot.add_child(info_label)

    var action_btn = Button.new()
    action_btn.text = tr("ui.saveload.overwrite") if current_mode == MenuMode.SAVE else tr("ui.saveload.load")
    action_btn.pressed.connect(func(): _on_slot_action(data.id))
    slot.add_child(action_btn)

    var delete_btn = Button.new()
    delete_btn.text = tr("ui.saveload.delete")
    delete_btn.pressed.connect(func(): _on_delete_action(data.id))
    slot.add_child(delete_btn)

    return slot

func _create_new_save_slot_ui() -> Control:
    var btn = Button.new()
    btn.text = tr("ui.saveload.create_new")
    btn.pressed.connect(func(): _on_slot_action("new"))
    return btn

func _on_slot_action(slot_id: String):
    if current_mode == MenuMode.SAVE:
        emit_signal("save_selected", slot_id)
    else:
        emit_signal("load_selected", slot_id)

func _on_delete_action(slot_id: String):
    # Should open a confirmation dialog, but for now just emit
    emit_signal("delete_selected", slot_id)

func _on_close_button_pressed():
    emit_signal("close_requested")
    hide()


func _setup_tooltips() -> void:
    close_button.tooltip_text = tr("ui.common.close")
