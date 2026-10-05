extends Control
class_name EventLog

@onready var scroll := $ScrollContainer
@onready var list_vbox := $ScrollContainer/VBoxContainer
var max_entries: int = 50

func _ready():
    # Ensure the VBox exists
    if not list_vbox:
        push_error("EventLog: missing VBoxContainer at $ScrollContainer/VBoxContainer")

# type: info, warning, danger, success
func add_event(text: String, type: String = "info", day_stamp: int = -1) -> void:
    var entry = Label.new()
    var ts = ""
    if day_stamp >= 0:
        ts = "[Day %d] " % day_stamp
    entry.text = ts + text
    entry.autowrap = true
    match type:
        "warning":
            entry.add_theme_color_override("font_color", Color(1, 0.8, 0))
        "danger":
            entry.add_theme_color_override("font_color", Color(1, 0.2, 0.2))
        "success":
            entry.add_theme_color_override("font_color", Color(0.4, 1, 0.4))
        _:
            entry.add_theme_color_override("font_color", Color(1, 1, 1))

    list_vbox.add_child(entry)
    # Trim old entries
    while list_vbox.get_child_count() > max_entries:
        var old = list_vbox.get_child(0)
        list_vbox.remove_child(old)
        old.queue_free()

    # Auto-scroll to bottom
    call_deferred("_scroll_to_bottom")

func _scroll_to_bottom() -> void:
    if scroll:
        var bar = scroll.get_v_scroll()
        bar.value = bar.max_value

func clear() -> void:
    for child in list_vbox.get_children():
        list_vbox.remove_child(child)
        child.queue_free()

func set_max_entries(n: int) -> void:
    max_entries = n
    # Trim if needed
    while list_vbox.get_child_count() > max_entries:
        var old = list_vbox.get_child(0)
        list_vbox.remove_child(old)
        old.queue_free()
