extends Control
class_name PhotoModeUI

# Photo Mode UI Controller
# Manages the photo mode overlay interface
# Author: Ingrid Lunde

var photo_mode: PhotoMode

@onready var overlay_panel = $OverlayPanel
@onready var filter_dropdown = $OverlayPanel/MarginContainer/VBoxContainer/FilterSection/FilterDropdown
@onready var pause_button = $OverlayPanel/MarginContainer/VBoxContainer/TimeControls/TimeButtons/PauseButton
@onready var rewind_button = $OverlayPanel/MarginContainer/VBoxContainer/TimeControls/TimeButtons/RewindButton
@onready var screenshot_button = $OverlayPanel/MarginContainer/VBoxContainer/Actions/ScreenshotButton
@onready var hud_toggle = $OverlayPanel/MarginContainer/VBoxContainer/Actions/HUDToggle
@onready var exit_button = $OverlayPanel/MarginContainer/VBoxContainer/Actions/ExitButton
@onready var tips_overlay = $TipsOverlay
@onready var screenshot_flash = $ScreenshotFlash
@onready var notification_popup = $NotificationPopup
@onready var notification_label = $NotificationPopup/MarginContainer/NotificationLabel

func _ready():
	# Hide UI initially
	visible = false
	
	# Setup filter dropdown
	filter_dropdown.clear()
	filter_dropdown.add_item("None", 0)
	filter_dropdown.add_item("Golden Hour ☀️", 1)
	filter_dropdown.add_item("Storm Drama ⛈️", 2)
	filter_dropdown.add_item("Underwater 🌊", 3)
	filter_dropdown.add_item("Northern Lights ✨", 4)
	
	# Connect signals
	filter_dropdown.item_selected.connect(_on_filter_selected)
	pause_button.toggled.connect(_on_pause_toggled)
	rewind_button.pressed.connect(_on_rewind_pressed)
	screenshot_button.pressed.connect(_on_screenshot_pressed)
	hud_toggle.toggled.connect(_on_hud_toggled)
	exit_button.pressed.connect(_on_exit_pressed)
	
	# Find PhotoMode singleton
	photo_mode = get_node_or_null("/root/PhotoMode")
	if not photo_mode:
		# Try to find it as autoload or in scene tree
		photo_mode = get_tree().root.get_node_or_null("PhotoMode")
	
	if photo_mode:
		photo_mode.photo_mode_toggled.connect(_on_photo_mode_toggled)
		photo_mode.screenshot_captured.connect(_on_screenshot_captured)
		photo_mode.filter_changed.connect(_on_filter_changed)
	
	# Show tips on first load
	await get_tree().create_timer(0.5).timeout
	show_tips()

func _process(_delta):
	# Update pause button state
	if photo_mode and pause_button:
		pause_button.button_pressed = photo_mode.time_paused

func _on_photo_mode_toggled(active: bool):
	visible = active
	
	if active:
		# Show tips briefly
		show_tips()
		# Fade in overlay
		overlay_panel.modulate.a = 0.0
		var tween = create_tween()
		tween.tween_property(overlay_panel, "modulate:a", 1.0, 0.3)
	else:
		# Fade out
		var tween = create_tween()
		tween.tween_property(overlay_panel, "modulate:a", 0.0, 0.2)

func _on_filter_selected(index: int):
	if photo_mode:
		photo_mode.apply_filter(index)

func _on_pause_toggled(pressed: bool):
	if photo_mode:
		if pressed:
			photo_mode.time_paused = true
			Engine.time_scale = 0.0
		else:
			photo_mode.time_paused = false
			Engine.time_scale = 1.0

func _on_rewind_pressed():
	if photo_mode:
		photo_mode.rewind_time()

func _on_screenshot_pressed():
	if photo_mode:
		photo_mode.capture_screenshot()

func _on_hud_toggled(pressed: bool):
	if photo_mode:
		if pressed != photo_mode.hud_visible:
			photo_mode.toggle_hud()

func _on_exit_pressed():
	if photo_mode:
		photo_mode.toggle_photo_mode()

func _on_screenshot_captured(path: String):
	# Flash effect
	screenshot_flash.visible = true
	screenshot_flash.modulate.a = 1.0
	var flash_tween = create_tween()
	flash_tween.tween_property(screenshot_flash, "modulate:a", 0.0, 0.3)
	flash_tween.tween_callback(func(): screenshot_flash.visible = false)
	
	# Show notification
	var filename = path.get_file()
	notification_label.text = "✨ Screenshot Saved!\n" + filename
	show_notification()

func _on_filter_changed(filter_name: String):
	# Update dropdown to match
	for i in filter_dropdown.item_count:
		if filter_dropdown.get_item_text(i).begins_with(filter_name):
			filter_dropdown.select(i)
			break

func show_tips():
	if not tips_overlay:
		return
	
	tips_overlay.visible = true
	tips_overlay.modulate.a = 1.0
	
	# Auto-hide after 5 seconds
	await get_tree().create_timer(5.0).timeout
	
	if tips_overlay:
		var tween = create_tween()
		tween.tween_property(tips_overlay, "modulate:a", 0.0, 0.5)
		tween.tween_callback(func(): tips_overlay.visible = false)

func show_notification():
	if not notification_popup:
		return
	
	notification_popup.visible = true
	notification_popup.modulate.a = 0.0
	notification_popup.position.y = 80
	
	var tween = create_tween()
	tween.tween_property(notification_popup, "modulate:a", 1.0, 0.3)
	tween.parallel().tween_property(notification_popup, "position:y", 100, 0.3)
	
	# Hold for 2 seconds
	await get_tree().create_timer(2.0).timeout
	
	if notification_popup:
		var fade_tween = create_tween()
		fade_tween.tween_property(notification_popup, "modulate:a", 0.0, 0.3)
		fade_tween.tween_callback(func(): notification_popup.visible = false)

func apply_design_tokens():
	# Apply Lakskongen design tokens
	# Colors from design/tokens.json
	var bg_color = Color("#0F1720")
	var panel_color = Color("#121826")
	var text_color = Color("#E6EEF3")
	var accent_color = Color("#2AB7B5")
	
	# Apply to theme
	var theme = Theme.new()
	
	# Panel styling
	var panel_style = StyleBoxFlat.new()
	panel_style.bg_color = panel_color
	panel_style.border_width_left = 2
	panel_style.border_width_top = 2
	panel_style.border_width_right = 2
	panel_style.border_width_bottom = 2
	panel_style.border_color = accent_color
	panel_style.corner_radius_top_left = 10
	panel_style.corner_radius_top_right = 10
	panel_style.corner_radius_bottom_left = 10
	panel_style.corner_radius_bottom_right = 10
	
	theme.set_stylebox("panel", "PanelContainer", panel_style)
	
	# Button styling
	var button_style = StyleBoxFlat.new()
	button_style.bg_color = accent_color
	button_style.corner_radius_top_left = 6
	button_style.corner_radius_top_right = 6
	button_style.corner_radius_bottom_left = 6
	button_style.corner_radius_bottom_right = 6
	
	theme.set_stylebox("normal", "Button", button_style)
	theme.set_color("font_color", "Button", text_color)
	
	# Apply theme to root
	self.theme = theme
