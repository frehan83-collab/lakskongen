extends Control

signal settings_changed
signal close_requested

@onready var tab_container = $Panel/TabContainer
@onready var close_button = $Panel/CloseButton

# Audio Settings
@onready var master_slider = $Panel/TabContainer/Audio/VBoxContainer/MasterVolume/HSlider
@onready var bgm_slider = $Panel/TabContainer/Audio/VBoxContainer/BGMVolume/HSlider
@onready var sfx_slider = $Panel/TabContainer/Audio/VBoxContainer/SFXVolume/HSlider
@onready var ambient_slider = $Panel/TabContainer/Audio/VBoxContainer/AmbientVolume/HSlider

# Graphics Settings
@onready var fullscreen_toggle = $Panel/TabContainer/Graphics/VBoxContainer/Fullscreen/CheckBox
@onready var vsync_toggle = $Panel/TabContainer/Graphics/VBoxContainer/VSync/CheckBox
@onready var resolution_option = $Panel/TabContainer/Graphics/VBoxContainer/Resolution/OptionButton

# Gameplay Settings
@onready var sim_speed_slider = $Panel/TabContainer/Gameplay/VBoxContainer/SimSpeed/HSlider
@onready var autosave_option = $Panel/TabContainer/Gameplay/VBoxContainer/Autosave/OptionButton
@onready var tutorial_toggle = $Panel/TabContainer/Gameplay/VBoxContainer/Tutorial/CheckBox

func _ready():
    close_button.pressed.connect(_on_close_button_pressed)
    _load_current_settings()
    _connect_signals()

func _load_current_settings():
    # Load settings from a global settings manager or config file
    pass

func _connect_signals():
    master_slider.value_changed.connect(func(v): _on_audio_changed("master", v))
    bgm_slider.value_changed.connect(func(v): _on_audio_changed("bgm", v))
    sfx_slider.value_changed.connect(func(v): _on_audio_changed("sfx", v))
    ambient_slider.value_changed.connect(func(v): _on_audio_changed("ambient", v))

    fullscreen_toggle.toggled.connect(_on_fullscreen_toggled)
    vsync_toggle.toggled.connect(_on_vsync_toggled)
    resolution_option.item_selected.connect(_on_resolution_selected)

    sim_speed_slider.value_changed.connect(_on_sim_speed_changed)
    autosave_option.item_selected.connect(_on_autosave_selected)
    tutorial_toggle.toggled.connect(_on_tutorial_toggled)

func _on_audio_changed(bus_name: String, value: float):
    # Apply to audio server and save
    emit_signal("settings_changed")

func _on_fullscreen_toggled(toggled_on: bool):
    # Apply fullscreen setting
    emit_signal("settings_changed")

func _on_vsync_toggled(toggled_on: bool):
    # Apply vsync setting
    emit_signal("settings_changed")

func _on_resolution_selected(index: int):
    # Apply resolution setting
    emit_signal("settings_changed")

func _on_sim_speed_changed(value: float):
    emit_signal("settings_changed")

func _on_autosave_selected(index: int):
    emit_signal("settings_changed")

func _on_tutorial_toggled(toggled_on: bool):
    emit_signal("settings_changed")

func _on_close_button_pressed():
    emit_signal("close_requested")
    hide()
