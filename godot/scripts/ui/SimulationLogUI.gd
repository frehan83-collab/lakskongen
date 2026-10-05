
extends Control

var day_label: Label
var log_text_label: RichTextLabel

func _ready():
    # Opprett dag-teller
    day_label = Label.new()
    day_label.name = "DayLabel"
    day_label.text = tr("DAY: 0")
    add_child(day_label)

    # Opprett logg-konsoll
    log_text_label = RichTextLabel.new()
    log_text_label.name = "LogTextLabel"
    log_text_label.set_anchors_and_offsets_preset(PRESET_WIDE) # Fyller bredden
    log_text_label.set_position(Vector2(0, 30)) # Juster posisjon under day_label
    log_text_label.set_size(Vector2(size.x, size.y - 30)) # Juster størrelse
    log_text_label.fit_content = false # Viktig for å kunne scrolle
    add_child(log_text_label)

    # Finn C# SimulationCore noden
    var simulation_core = get_node_or_null("/root/SimulationCore")
    if simulation_core:
        if simulation_core.has_signal("DayTicked"):
            simulation_core.DayTicked.connect(_on_day_ticked)
            print("Connected to SimulationCore.DayTicked signal.")
        else:
            print("Error: SimulationCore does not have 'DayTicked' signal.")
    else:
        print("Error: SimulationCore node not found at /root/SimulationCore. Make sure it's an autoload or correctly instanced.")

func _on_day_ticked(day: int, log_summary: String):
    day_label.text = "DAY: " + str(day)
    log_text_label.append_text(log_summary + "\n")
    log_text_label.scroll_to_line(log_text_label.get_line_count() - 1) # Scroll til bunnen

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		_on_close_requested()

func _on_close_requested() -> void:
	hide()

