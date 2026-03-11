extends Control

@onready var btn_new_game = $VBoxContainer/BtnNewGame
@onready var btn_continue = $VBoxContainer/BtnContinue
@onready var btn_load_game = $VBoxContainer/BtnLoadGame
@onready var btn_settings = $VBoxContainer/BtnSettings
@onready var btn_quit = $VBoxContainer/BtnQuit

@onready var background_animation = $BackgroundAnimation # Placeholder for gradient+particles
@onready var version_label = $VersionLabel

# Submenus
@onready var settings_menu = $SettingsMenu
@onready var save_load_menu = $SaveLoadMenu

func _ready():
    btn_new_game.pressed.connect(_on_new_game_pressed)
    btn_continue.pressed.connect(_on_continue_pressed)
    btn_load_game.pressed.connect(_on_load_game_pressed)
    btn_settings.pressed.connect(_on_settings_pressed)
    btn_quit.pressed.connect(_on_quit_pressed)

    settings_menu.close_requested.connect(_on_submenu_closed)
    save_load_menu.close_requested.connect(_on_submenu_closed)

    version_label.text = "Lakskongen v0.1.0-alpha"

    # Hide submenus by default
    settings_menu.hide()
    save_load_menu.hide()

func _on_new_game_pressed():
    print("New Game selected")
    # Transition to game scene

func _on_continue_pressed():
    print("Continue selected (Load latest save)")
    # Get latest save from SaveManager and load it

func _on_load_game_pressed():
    print("Load Game selected")
    save_load_menu.open(save_load_menu.MenuMode.LOAD)
    _toggle_main_buttons(false)

func _on_settings_pressed():
    print("Settings selected")
    settings_menu.show()
    _toggle_main_buttons(false)

func _on_quit_pressed():
    get_tree().quit()

func _on_submenu_closed():
    _toggle_main_buttons(true)

func _toggle_main_buttons(enable: bool):
    btn_new_game.disabled = !enable
    btn_continue.disabled = !enable
    btn_load_game.disabled = !enable
    btn_settings.disabled = !enable
    btn_quit.disabled = !enable
