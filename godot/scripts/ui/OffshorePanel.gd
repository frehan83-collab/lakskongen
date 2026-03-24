# OffshorePanel.gd
# Offshore farm management UI
# Godot 4 GDScript

extends PanelContainer
class_name OffshorePanel

@onready var offshore_header_label: Label
@onready var farm_list_container: VBoxContainer
@onready var weather_waves_label: Label
@onready var crew_rotation_label: Label
@onready var build_new_offshore_button: Button

# Autoload for theme
var LakskongenTheme = null

func _ready():
    LakskongenTheme = get_node("/root/LakskongenTheme") # Access the autoloaded theme

    if not offshore_header_label:
        _create_ui()

    refresh()

func _create_ui():
    var margin = MarginContainer.new()
    margin.add_theme_constant_override("margin_top", 16)
    margin.add_theme_constant_override("margin_bottom", 16)
    margin.add_theme_constant_override("margin_left", 16)
    margin.add_theme_constant_override("margin_right", 16)
    add_child(margin)

    var vbox = VBoxContainer.new()
    vbox.add_theme_constant_override("separation", 12)
    margin.add_child(vbox)

    offshore_header_label = Label.new()
    offshore_header_label.text = "Offshore Merd Anlegg Oversikt" # Norwegian label
    offshore_header_label.add_theme_font_size_override("font_size", LakskongenTheme.FONT_HEADER)
    offshore_header_label.add_theme_color_override("font_color", LakskongenTheme.TEXT_PRIMARY)
    vbox.add_child(offshore_header_label)

    # Farm List
    var farm_list_header = Label.new()
    farm_list_header.text = "Merd Anlegg" # Norwegian label
    farm_list_header.add_theme_font_size_override("font_size", LakskongenTheme.FONT_BODY)
    farm_list_header.add_theme_color_override("font_color", LakskongenTheme.TEXT_ACCENT)
    vbox.add_child(farm_list_header)

    farm_list_container = VBoxContainer.new()
    farm_list_container.add_theme_constant_override("separation", 8)
    vbox.add_child(farm_list_container)

    # Weather & Waves
    weather_waves_label = Label.new()
    weather_waves_label.text = "Vær & Bølger: Rolig, 1.2m bølger" # Example, dynamic
    weather_waves_label.add_theme_font_size_override("font_size", LakskongenTheme.FONT_BODY)
    weather_waves_label.add_theme_color_override("font_color", LakskongenTheme.TEXT_PRIMARY)
    vbox.add_child(weather_waves_label)

    # Crew Rotation
    crew_rotation_label = Label.new()
    crew_rotation_label.text = "Mannskapsrotasjon: Neste rotasjon om 3 dager" # Example, dynamic
    crew_rotation_label.add_theme_font_size_override("font_size", LakskongenTheme.FONT_BODY)
    crew_rotation_label.add_theme_color_override("font_color", LakskongenTheme.TEXT_PRIMARY)
    vbox.add_child(crew_rotation_label)

    # Button
    build_new_offshore_button = Button.new()
    build_new_offshore_button.text = "Bygg Nytt Offshore Anlegg" # Norwegian label
    build_new_offshore_button.add_theme_font_size_override("font_size", LakskongenTheme.FONT_BODY)
    build_new_offshore_button.add_theme_color_override("font_color", LakskongenTheme.TEXT_PRIMARY)
    build_new_offshore_button.connect("pressed", Callable(self, "_on_build_new_offshore_pressed"))
    vbox.add_child(build_new_offshore_button)

func refresh():
    # Placeholder for actual data refresh logic
    print("OffshorePanel: Refreshing UI data...")
    _update_farm_list()
    _update_weather_waves()
    _update_crew_rotation()

func _update_farm_list():
    # Clear existing list items
    for child in farm_list_container.get_children():
        child.queue_free()

    # Add placeholder farm items
    for i in range(2): # Example: 2 offshore farms
        var farm_label = Label.new()
        farm_label.text = "Offshore Anlegg %d (Aktiv)" % (i + 1)
        farm_label.add_theme_font_size_override("font_size", LakskongenTheme.FONT_SMALL)
        farm_label.add_theme_color_override("font_color", LakskongenTheme.TEXT_SECONDARY)
        farm_list_container.add_child(farm_label)

func _update_weather_waves():
    weather_waves_label.text = "Vær & Bølger: Kraftig vind, 3.5m bølger" # Example dynamic data

func _update_crew_rotation():
    crew_rotation_label.text = "Mannskapsrotasjon: Planlagt i morgen" # Example dynamic data

func _on_build_new_offshore_pressed():
    print("OffshorePanel: 'Bygg Nytt Offshore Anlegg' button pressed")
    # Emit signal or call manager to build new offshore farm

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		_on_close_requested()

func _on_close_requested() -> void:
	hide()

tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
    if event.is_action_pressed("ui_cancel"):
        get_tree().call_group_flags(2, "tooltips", "hide")
