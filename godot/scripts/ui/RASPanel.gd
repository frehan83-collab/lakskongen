# RASPanel.gd
# RAS facility management UI (Recirculating Aquaculture System)
# Godot 4 GDScript

extends PanelContainer
class_name RASPanel

@onready var ras_header_label: Label
@onready var facility_list_container: VBoxContainer
@onready var water_chemistry_label: Label
@onready var build_new_button: Button
@onready var maintenance_button: Button

# Autoload for theme
var LakskongenTheme = null

func _ready():
    LakskongenTheme = get_node("/root/LakskongenTheme") # Access the autoloaded theme

    if not ras_header_label:
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

    ras_header_label = Label.new()
    ras_header_label.text = "RAS-anlegg Oversikt" # Norwegian label
    ras_header_label.add_theme_font_size_override("font_size", LakskongenTheme.FONT_HEADER)
    ras_header_label.add_theme_color_override("font_color", LakskongenTheme.TEXT_PRIMARY)
    vbox.add_child(ras_header_label)

    # Facility List
    var facility_list_header = Label.new()
    facility_list_header.text = "Fasiliteter" # Norwegian label
    facility_list_header.add_theme_font_size_override("font_size", LakskongenTheme.FONT_BODY)
    facility_list_header.add_theme_color_override("font_color", LakskongenTheme.TEXT_ACCENT)
    vbox.add_child(facility_list_header)

    facility_list_container = VBoxContainer.new()
    facility_list_container.add_theme_constant_override("separation", 8)
    vbox.add_child(facility_list_container)

    # Water Chemistry
    water_chemistry_label = Label.new()
    water_chemistry_label.text = "Vannkjemi: Optimal" # Example, will be dynamic
    water_chemistry_label.add_theme_font_size_override("font_size", LakskongenTheme.FONT_BODY)
    water_chemistry_label.add_theme_color_override("font_color", LakskongenTheme.TEXT_PRIMARY)
    vbox.add_child(water_chemistry_label)

    # Buttons
    var button_hbox = HBoxContainer.new()
    button_hbox.add_theme_constant_override("separation", 8)
    vbox.add_child(button_hbox)

    build_new_button = Button.new()
    build_new_button.text = "Bygg Ny RAS" # Norwegian label
    build_new_button.add_theme_font_size_override("font_size", LakskongenTheme.FONT_BODY)
    build_new_button.add_theme_color_override("font_color", LakskongenTheme.TEXT_PRIMARY)
    build_new_button.connect("pressed", Callable(self, "_on_build_new_pressed"))
    button_hbox.add_child(build_new_button)

    maintenance_button = Button.new()
    maintenance_button.text = "Vedlikehold" # Norwegian label
    maintenance_button.add_theme_font_size_override("font_size", LakskongenTheme.FONT_BODY)
    maintenance_button.add_theme_color_override("font_color", LakskongenTheme.TEXT_PRIMARY)
    maintenance_button.connect("pressed", Callable(self, "_on_maintenance_pressed"))
    button_hbox.add_child(maintenance_button)

func refresh():
    # Placeholder for actual data refresh logic
    # This method will be called to update the UI with current game state
    print("RASPanel: Refreshing UI data...")
    _update_facility_list()
    _update_water_chemistry()

func _update_facility_list():
    # Clear existing list items
    for child in facility_list_container.get_children():
        child.queue_free()

    # Add placeholder facility items
    for i in range(3): # Example: 3 facilities
        var facility_label = Label.new()
        facility_label.text = "RAS Anlegg %d (Aktiv)" % (i + 1)
        facility_label.add_theme_font_size_override("font_size", LakskongenTheme.FONT_SMALL)
        facility_label.add_theme_color_override("font_color", LakskongenTheme.TEXT_SECONDARY)
        facility_list_container.add_child(facility_label)

func _update_water_chemistry():
    # Placeholder for water chemistry data
    water_chemistry_label.text = "Vannkjemi: pH 7.2, Temp 12°C, O2 8mg/L" # Example data

func _on_build_new_pressed():
    print("RASPanel: 'Bygg Ny RAS' button pressed")
    # Emit signal or call manager to build new RAS facility

func _on_maintenance_pressed():
    print("RASPanel: 'Vedlikehold' button pressed")
    # Emit signal or call manager for RAS maintenance

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		_on_close_requested()

func _on_close_requested() -> void:
	hide()

