extends Node
class_name PhotoMode

# Photo Mode Manager for Lakskongen
# Provides free camera, filters, time control, and screenshot capture
# Author: Ingrid Lunde, UI/UX Designer

signal photo_mode_toggled(active: bool)
signal screenshot_captured(path: String)
signal filter_changed(filter_name: String)

# Camera state
var is_active := false
var free_camera: Camera3D
var original_camera: Camera3D
var camera_speed := 10.0
var camera_speed_multiplier := 1.0
var mouse_sensitivity := 0.003
var camera_rotation := Vector3.ZERO

# UI visibility
var hud_visible := true
var original_hud_visibility := true

# Filters
enum Filter {
	NONE,
	GOLDEN_HOUR,
	STORM_DRAMA,
	UNDERWATER,
	NORTHERN_LIGHTS
}

var current_filter := Filter.NONE
var filter_environment: Environment

# Time control
var time_paused := false
var time_scale := 1.0
var replay_buffer := []
const REPLAY_BUFFER_SIZE = 1800  # 30 seconds at 60fps

# Screenshot
var screenshot_dir := "user://screenshots"
var screenshot_count := 0

# References
@onready var game_manager = get_node_or_null("/root/GameManager")

func _ready():
	# Create screenshot directory
	var dir = DirAccess.open("user://")
	if not dir.dir_exists("screenshots"):
		dir.make_dir("screenshots")
	
	# Create free camera (will be activated when entering photo mode)
	free_camera = Camera3D.new()
	free_camera.name = "PhotoModeCamera"
	add_child(free_camera)
	free_camera.current = false
	
	# Setup environment for filters
	filter_environment = Environment.new()
	
	print("✅ Photo Mode initialized - Press P to toggle")

func _input(event):
	if event.is_action_pressed("toggle_photo_mode"):
		toggle_photo_mode()
	
	if not is_active:
		return
	
	# Screenshot capture
	if event.is_action_pressed("capture_screenshot"):
		capture_screenshot()
	
	# Filter toggle
	if event.is_action_pressed("toggle_filters"):
		cycle_filter()
	
	# HUD toggle
	if event.is_action_pressed("toggle_hud"):
		toggle_hud()
	
	# Time controls
	if event.is_action_pressed("pause_time"):
		toggle_time_pause()
	if event.is_action_pressed("rewind_time"):
		rewind_time()
	
	# Speed adjustments
	if event.is_key_pressed(KEY_SHIFT):
		camera_speed_multiplier = 3.0
	elif event.is_key_pressed(KEY_CTRL):
		camera_speed_multiplier = 0.3
	else:
		camera_speed_multiplier = 1.0
	
	# Mouse look (only when right mouse button held)
	if event is InputEventMouseMotion and Input.is_mouse_button_pressed(MOUSE_BUTTON_RIGHT):
		camera_rotation.y -= event.relative.x * mouse_sensitivity
		camera_rotation.x -= event.relative.y * mouse_sensitivity
		camera_rotation.x = clamp(camera_rotation.x, -PI/2, PI/2)
	
	# Zoom with scroll wheel
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_WHEEL_UP:
			camera_speed = min(camera_speed * 1.2, 100.0)
		elif event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			camera_speed = max(camera_speed * 0.8, 1.0)

func _process(delta):
	if not is_active or not free_camera:
		return
	
	# Update camera rotation
	free_camera.rotation = camera_rotation
	
	# Camera movement (WASD + QE for up/down)
	var movement := Vector3.ZERO
	
	if Input.is_key_pressed(KEY_W):
		movement -= free_camera.global_transform.basis.z
	if Input.is_key_pressed(KEY_S):
		movement += free_camera.global_transform.basis.z
	if Input.is_key_pressed(KEY_A):
		movement -= free_camera.global_transform.basis.x
	if Input.is_key_pressed(KEY_D):
		movement += free_camera.global_transform.basis.x
	if Input.is_key_pressed(KEY_Q):
		movement.y -= 1.0
	if Input.is_key_pressed(KEY_E):
		movement.y += 1.0
	
	if movement.length() > 0:
		movement = movement.normalized()
		var speed = camera_speed * camera_speed_multiplier
		# Smooth interpolation for buttery movement
		var target_pos = free_camera.global_position + movement * speed * delta
		free_camera.global_position = free_camera.global_position.lerp(target_pos, 0.5)

func toggle_photo_mode():
	is_active = not is_active
	
	if is_active:
		enter_photo_mode()
	else:
		exit_photo_mode()
	
	photo_mode_toggled.emit(is_active)

func enter_photo_mode():
	print("📸 Entering Photo Mode")
	
	# Find and store original camera
	var viewport = get_viewport()
	original_camera = viewport.get_camera_3d()
	
	if original_camera:
		# Copy position and rotation from current camera
		free_camera.global_position = original_camera.global_position
		free_camera.global_rotation = original_camera.global_rotation
		camera_rotation = free_camera.global_rotation
		
		# Make free camera active
		free_camera.current = true
	
	# Pause game
	if game_manager:
		time_paused = true
		Engine.time_scale = 0.0
	
	# Store HUD visibility
	var hud = get_node_or_null("/root/Main/HUD")
	if hud:
		original_hud_visibility = hud.visible
	
	# Capture mouse for camera control
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED

func exit_photo_mode():
	print("📸 Exiting Photo Mode")
	
	# Restore original camera
	if original_camera:
		original_camera.current = true
	
	# Restore time scale
	if time_paused:
		Engine.time_scale = 1.0
		time_paused = false
	
	# Restore HUD
	if hud_visible != original_hud_visibility:
		toggle_hud()
	
	# Release mouse
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	
	# Reset filter
	if current_filter != Filter.NONE:
		apply_filter(Filter.NONE)

func toggle_hud():
	hud_visible = not hud_visible
	
	var hud = get_node_or_null("/root/Main/HUD")
	if hud:
		hud.visible = hud_visible
	
	print("🎨 HUD " + ("visible" if hud_visible else "hidden"))

func capture_screenshot():
	if not is_active:
		return
	
	# Generate filename with timestamp
	var time = Time.get_datetime_dict_from_system()
	var filename = "lakskongen_%04d%02d%02d_%02d%02d%02d.png" % [
		time.year, time.month, time.day,
		time.hour, time.minute, time.second
	]
	
	var path = screenshot_dir.path_join(filename)
	
	# Capture the viewport
	var img = get_viewport().get_texture().get_image()
	
	# Save the image
	var err = img.save_png(path)
	
	if err == OK:
		screenshot_count += 1
		print("📸 Screenshot saved: " + path)
		screenshot_captured.emit(path)
		
		# Show brief confirmation (would trigger UI notification)
		show_screenshot_notification(filename)
	else:
		print("❌ Failed to save screenshot: " + str(err))

func show_screenshot_notification(filename: String):
	# TODO: Connect to UI notification system
	print("✨ Screenshot captured: " + filename)

func cycle_filter():
	current_filter = (current_filter + 1) % 5
	apply_filter(current_filter)
	
	var filter_names = ["None", "Golden Hour", "Storm Drama", "Underwater", "Northern Lights"]
	print("🎨 Filter: " + filter_names[current_filter])
	filter_changed.emit(filter_names[current_filter])

func apply_filter(filter: Filter):
	var world_env = get_viewport().find_world_3d().environment
	
	if not world_env:
		return
	
	match filter:
		Filter.NONE:
			# Reset to default
			world_env.tonemap_mode = Environment.TONE_MAPPER_LINEAR
			world_env.adjustment_enabled = false
			
		Filter.GOLDEN_HOUR:
			# Warm, saturated, high contrast
			world_env.adjustment_enabled = true
			world_env.adjustment_brightness = 1.1
			world_env.adjustment_contrast = 1.15
			world_env.adjustment_saturation = 1.25
			world_env.adjustment_color_correction = create_golden_hour_lut()
			
		Filter.STORM_DRAMA:
			# Dark, desaturated, moody
			world_env.adjustment_enabled = true
			world_env.adjustment_brightness = 0.7
			world_env.adjustment_contrast = 1.3
			world_env.adjustment_saturation = 0.6
			
		Filter.UNDERWATER:
			# Blue tint, hazy
			world_env.adjustment_enabled = true
			world_env.adjustment_brightness = 0.9
			world_env.adjustment_contrast = 0.95
			world_env.adjustment_saturation = 1.1
			world_env.fog_enabled = true
			world_env.fog_light_color = Color(0.2, 0.4, 0.6)
			world_env.fog_density = 0.02
			
		Filter.NORTHERN_LIGHTS:
			# Enhanced green/blue, high contrast
			world_env.adjustment_enabled = true
			world_env.adjustment_brightness = 1.2
			world_env.adjustment_contrast = 1.2
			world_env.adjustment_saturation = 1.4
			world_env.glow_enabled = true
			world_env.glow_intensity = 2.0
			world_env.glow_bloom = 0.3

func create_golden_hour_lut() -> Texture:
	# TODO: Create proper color LUT texture
	# For now, return null and rely on brightness/contrast/saturation
	return null

func toggle_time_pause():
	time_paused = not time_paused
	Engine.time_scale = 0.0 if time_paused else 1.0
	print("⏸️ Time " + ("paused" if time_paused else "resumed"))

func rewind_time():
	# TODO: Implement replay buffer system
	print("⏪ Rewind (not yet implemented)")

func set_time_scale(scale: float):
	time_scale = clamp(scale, 0.1, 5.0)
	if not time_paused:
		Engine.time_scale = time_scale

func get_screenshot_count() -> int:
	return screenshot_count

func get_current_filter_name() -> String:
	var filter_names = ["None", "Golden Hour", "Storm Drama", "Underwater", "Northern Lights"]
	return filter_names[current_filter]
