# AIFeedingPanel.gd
# AI Feeding Dashboard UI
# Godot 4 GDScript

extends PanelContainer
class_name AIFeedingPanel

@onready var ai_feeding_header_label: Label
@onready var calibration_status_label: Label
@onready var cameras_status_label: Label
@onready var appetite_level_label: Label
@onready var fcr_savings_label: Label
@onready var install_ai_button: Button

# Autoload for theme
var LakskongenTheme = null

func _ready():
    LakskongenTheme = get_node("/root/LakskongenTheme") # Access the autoloaded theme

    if not ai_feeding_header_label:
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

    ai_feeding_header_label = Label.new()
    ai_feeding_header_label.text = "AI Fôrings Dashboard" # Norwegian label
    ai_feeding_header_label.add_theme_font_size_override("font_size", LakskongenTheme.FONT_HEADER)
    ai_feeding_header_label.add_theme_color_override("font_color", LakskongenTheme.TEXT_PRIMARY)
    vbox.add_child(ai_feeding_header_label)

    # Calibration Status
    calibration_status_label = Label.new()
    calibration_status_label.text = "Kalibrering: OK" # Example, dynamic
    calibration_status_label.add_theme_font_size_override("font_size", LakskongenTheme.FONT_BODY)
    calibration_status_label.add_theme_color_override("font_color", LakskongenTheme.TEXT_PRIMARY)
    vbox.add_child(calibration_status_label)

    # Cameras Status
    cameras_status_label = Label.new()
    cameras_status_label.text = "Kameraer: 4/4 Aktive" # Example, dynamic
    cameras_status_label.add_theme_font_size_override("font_size", LakskongenTheme.FONT_BODY)
    cameras_status_label.add_theme_color_override("font_color", LakskongenTheme.TEXT_PRIMARY)
    vbox.add_child(cameras_status_label)

    # Appetite Level
    appetite_level_label = Label.new()
    appetite_level_label.text = "Appetittnivå: Høyt (75%)" # Example, dynamic
    appetite_level_label.add_theme_font_size_override("font_size", LakskongenTheme.FONT_BODY)
    appetite_level_label.add_theme_color_override("font_color", LakskongenTheme.TEXT_PRIMARY)
    vbox.add_child(appetite_level_label)

    # FCR Savings
    fcr_savings_label = Label.new()
    fcr_savings_label.text = "FCR Besparelser: 8.2%" # Example, dynamic
    fcr_savings_label.add_theme_font_size_override("font_size", LakskongenTheme.FONT_BODY)
    fcr_savings_label.add_theme_color_override("font_color", LakskongenTheme.TEXT_ACCENT)
    vbox.add_child(fcr_savings_label)

    # Install AI Button
    install_ai_button = Button.new()
    install_ai_button.text = "Installer AI Fôringssystem" # Norwegian label
    install_ai_button.add_theme_font_size_override("font_size", LakskongenTheme.FONT_BODY)
    install_ai_button.add_theme_color_override("font_color", LakskongenTheme.TEXT_PRIMARY)
    install_ai_button.connect("pressed", Callable(self, "_on_install_ai_pressed"))
    vbox.add_child(install_ai_button)

func refresh():
    # Placeholder for actual data refresh logic
    print("AIFeedingPanel: Refreshing UI data...")
    _update_calibration_status()
    _update_cameras_status()
    _update_appetite_level()
    _update_fcr_savings()

func _update_calibration_status():
    calibration_status_label.text = "Kalibrering: Utført" # Example dynamic data

func _update_cameras_status():
    cameras_status_label.text = "Kameraer: 4/4 Aktive, Strømmer Data" # Example dynamic data

func _update_appetite_level():
    appetite_level_label.text = "Appetittnivå: Middels (60%)" # Example dynamic data

func _update_fcr_savings():
    fcr_savings_label.text = "FCR Besparelser: 8.5% (Estimert)" # Example dynamic data

func _on_install_ai_pressed():
    print("AIFeedingPanel: 'Installer AI Fôringssystem' button pressed")
    # Emit signal or call manager to install AI feeding system

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		_on_close_requested()

func _on_close_requested() -> void:
	hide()

tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
    if event.is_action_pressed("ui_cancel"):
        get_tree().call_group_flags(2, "tooltips", "hide")
