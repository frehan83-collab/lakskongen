extends SceneTree
# Automated Screenshot Capture Tool for Lakskongen
# Captures all required Steam store screenshots
# Usage: godot --script screenshot_automation.gd --headless

# Screenshot configuration
const SCREENSHOTS = [
	{
		"name": "01_dashboard_overview",
		"scene": "res://scenes/GameScene.tscn",
		"description": "Dashboard overview with all UI panels",
		"camera_pos": Vector3(0, 50, 100),
		"camera_rot": Vector3(-30, 0, 0),
		"setup": "show_dashboard"
	},
	{
		"name": "02_fish_pens",
		"scene": "res://scenes/GameScene.tscn",
		"description": "Fish pen management view",
		"camera_pos": Vector3(20, 15, 30),
		"camera_rot": Vector3(-45, 30, 0),
		"setup": "focus_pens"
	},
	{
		"name": "03_market_trading",
		"scene": "res://scenes/GameScene.tscn",
		"description": "Market trading interface",
		"camera_pos": Vector3(0, 50, 100),
		"camera_rot": Vector3(-30, 0, 0),
		"setup": "open_market_panel"
	},
	{
		"name": "04_processing_plant",
		"scene": "res://scenes/GameScene.tscn",
		"description": "Processing plant view",
		"camera_pos": Vector3(50, 20, 50),
		"camera_rot": Vector3(-40, 45, 0),
		"setup": "show_processing"
	},
	{
		"name": "05_map_norwegian_coast",
		"scene": "res://scenes/GameScene.tscn",
		"description": "Map view of Norwegian coast",
		"camera_pos": Vector3(0, 200, 0),
		"camera_rot": Vector3(-90, 0, 0),
		"setup": "show_map"
	},
	{
		"name": "06_tutorial",
		"scene": "res://scenes/GameScene.tscn",
		"description": "Tutorial sequence",
		"camera_pos": Vector3(0, 40, 80),
		"camera_rot": Vector3(-25, 0, 0),
		"setup": "show_tutorial"
	},
	{
		"name": "07_disease_management",
		"scene": "res://scenes/GameScene.tscn",
		"description": "Disease management panel",
		"camera_pos": Vector3(0, 50, 100),
		"camera_rot": Vector3(-30, 0, 0),
		"setup": "show_disease_panel"
	},
	{
		"name": "08_harvest",
		"scene": "res://scenes/GameScene.tscn",
		"description": "Harvest operation",
		"camera_pos": Vector3(30, 10, 40),
		"camera_rot": Vector3(-35, 20, 0),
		"setup": "show_harvest"
	},
	{
		"name": "09_tech_tree",
		"scene": "res://scenes/GameScene.tscn",
		"description": "Technology tree",
		"camera_pos": Vector3(0, 50, 100),
		"camera_rot": Vector3(-30, 0, 0),
		"setup": "show_tech_tree"
	},
	{
		"name": "10_seasonal_weather",
		"scene": "res://scenes/GameScene.tscn",
		"description": "Seasonal variation (winter)",
		"camera_pos": Vector3(0, 60, 120),
		"camera_rot": Vector3(-35, 0, 0),
		"setup": "set_winter"
	}
]

var current_index = 0
var screenshot_dir = "user://screenshots/steam_store"
var main_scene: Node

func _init():
	print("=== Lakskongen Screenshot Automation ===")
	print("Capturing 10 screenshots for Steam store page")
	print("")
	
	# Create output directory
	create_screenshot_directory()
	
	# Start capturing
	capture_next_screenshot()

func create_screenshot_directory():
	var dir = DirAccess.open("user://")
	if not dir.dir_exists("screenshots"):
		dir.make_dir("screenshots")
	
	dir = DirAccess.open("user://screenshots")
	if not dir.dir_exists("steam_store"):
		dir.make_dir("steam_store")
	
	print("📁 Output directory: " + screenshot_dir)
	print("")

func capture_next_screenshot():
	if current_index >= SCREENSHOTS.size():
		finish_capture()
		return
	
	var config = SCREENSHOTS[current_index]
	print("[%d/10] Capturing: %s" % [current_index + 1, config.name])
	print("        %s" % config.description)
	
	# Load the scene
	var scene_path = config.scene
	var packed_scene = load(scene_path)
	
	if not packed_scene:
		print("❌ Failed to load scene: " + scene_path)
		current_index += 1
		call_deferred("capture_next_screenshot")
		return
	
	# Clear current scene
	if main_scene:
		main_scene.queue_free()
	
	# Instance new scene
	main_scene = packed_scene.instantiate()
	root.add_child(main_scene)
	
	# Wait for scene to be ready
	await get_tree().process_frame
	await get_tree().process_frame
	
	# Setup the scene
	setup_scene(config)
	
	# Wait for setup
	await get_tree().create_timer(0.5).timeout
	
	# Position camera
	setup_camera(config)
	
	# Wait for render
	await get_tree().process_frame
	await get_tree().process_frame
	
	# Capture screenshot
	save_screenshot(config.name)
	
	print("✅ Captured: %s.png" % config.name)
	print("")
	
	# Move to next
	current_index += 1
	call_deferred("capture_next_screenshot")

func setup_scene(config: Dictionary):
	# This would need to be customized based on actual scene structure
	# For now, just basic setup
	
	match config.setup:
		"show_dashboard":
			# Show all UI panels
			show_ui_element("HUD")
			show_ui_element("Dashboard")
		
		"focus_pens":
			# Focus on fish pens area
			show_ui_element("HUD")
		
		"open_market_panel":
			# Open market panel
			show_ui_element("FinancePanel")
		
		"show_processing":
			# Show processing plant
			show_ui_element("ProductionChainPanel")
		
		"show_map":
			# Show map view
			pass
		
		"show_tutorial":
			# Show tutorial overlay
			pass
		
		"show_disease_panel":
			# Show disease management
			pass
		
		"show_harvest":
			# Setup harvest scene
			pass
		
		"show_tech_tree":
			# Show technology tree
			pass
		
		"set_winter":
			# Set winter season/weather
			pass

func show_ui_element(node_name: String):
	var node = main_scene.find_child(node_name, true, false)
	if node:
		node.visible = true

func setup_camera(config: Dictionary):
	var camera = get_camera()
	if not camera:
		# Create camera if none exists
		camera = Camera3D.new()
		camera.name = "ScreenshotCamera"
		main_scene.add_child(camera)
		camera.make_current()
	
	# Position camera
	camera.global_position = config.camera_pos
	camera.rotation_degrees = config.camera_rot

func get_camera() -> Camera3D:
	if not main_scene:
		return null
	
	# Try to find existing camera
	var camera = main_scene.find_child("Camera3D", true, false)
	if not camera:
		camera = main_scene.find_child("MainCamera", true, false)
	
	return camera

func save_screenshot(filename: String):
	var viewport = root.get_viewport()
	var img = viewport.get_texture().get_image()
	
	# Ensure 1920x1080
	if img.get_width() != 1920 or img.get_height() != 1080:
		img.resize(1920, 1080, Image.INTERPOLATE_LANCZOS)
	
	var path = screenshot_dir.path_join(filename + ".png")
	var err = img.save_png(path)
	
	if err != OK:
		print("❌ Failed to save: " + path + " (error: " + str(err) + ")")

func finish_capture():
	print("=== Screenshot Capture Complete ===")
	print("📸 Captured 10/10 screenshots")
	print("")
	print("Output location:")
	var real_path = ProjectSettings.globalize_path(screenshot_dir)
	print("  " + real_path)
	print("")
	print("Next steps:")
	print("  1. Review screenshots for quality")
	print("  2. Copy to salmon-sim/screenshots/steam_store/")
	print("  3. Upload to Steam store page")
	print("")
	
	quit()
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
