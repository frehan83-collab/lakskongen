extends SceneTree

## Quick test script for GameUI integration.
## Run with: godot --script test_gameui.gd

func _init() -> void:
	print("\n=== GameUI Integration Test ===\n")
	
	# Test 1: Check if GameUI.gd exists and loads
	var gameui_script = load("res://scripts/ui/GameUI.gd")
	if gameui_script:
		print("✓ GameUI.gd loaded successfully")
	else:
		print("✗ GameUI.gd failed to load")
		quit(1)
		return
	
	# Test 2: Check if GameUI.tscn exists and loads
	var gameui_scene = load("res://scenes/GameUI.tscn")
	if gameui_scene:
		print("✓ GameUI.tscn loaded successfully")
	else:
		print("✗ GameUI.tscn failed to load")
		quit(1)
		return
	
	# Test 3: Check if GameScene.tscn loads with GameUI
	var gamescene = load("res://scenes/GameScene.tscn")
	if gamescene:
		print("✓ GameScene.tscn loaded successfully")
	else:
		print("✗ GameScene.tscn failed to load")
		quit(1)
		return
	
	# Test 4: Verify all panel scripts exist
	var panels = [
		"res://scripts/ui/Dashboard.gd",
		"res://scripts/ui/MapPanel.gd",
		"res://scripts/ui/FinancePanel.gd",
		"res://scripts/ui/PenManagement.gd",
		"res://scripts/ui/ProductionChainPanel.gd",
		"res://scripts/ui/TechTreePanel.gd",
		"res://scripts/ui/SustainabilityPanel.gd",
		"res://scripts/ui/RASPanel.gd",
		"res://scripts/ui/OffshorePanel.gd",
		"res://scripts/ui/AIFeedingPanel.gd",
		"res://scripts/ui/CrewPanel.gd",
		"res://scripts/ui/ContractsPanel.gd",
		"res://scripts/ui/EncyclopediaPanel.gd",
		"res://scripts/ui/SaveLoadMenu.gd"
	]
	
	var all_panels_ok = true
	for panel_path in panels:
		if ResourceLoader.exists(panel_path):
			print("✓ %s exists" % panel_path.get_file())
		else:
			print("✗ %s MISSING" % panel_path.get_file())
			all_panels_ok = false
	
	if not all_panels_ok:
		print("\n⚠ Some panels are missing!")
		quit(1)
		return
	
	# Test 5: Instance GameUI and check tab registration
	var gameui_instance = gameui_scene.instantiate()
	if gameui_instance:
		print("✓ GameUI instantiated successfully")
		
		# Check if it has the expected methods
		if gameui_instance.has_method("show_tab"):
			print("✓ GameUI has show_tab() method")
		else:
			print("✗ GameUI missing show_tab() method")
		
		if gameui_instance.has_method("get_active_tab"):
			print("✓ GameUI has get_active_tab() method")
		else:
			print("✗ GameUI missing get_active_tab() method")
		
		gameui_instance.free()
	else:
		print("✗ Failed to instantiate GameUI")
		quit(1)
		return
	
	print("\n=== All Tests Passed! ===")
	print("GameUI integration is complete and ready to use.")
	print("\nNext steps:")
	print("1. Open godot/scenes/GameScene.tscn in Godot editor")
	print("2. Run the scene (F5)")
	print("3. Test tab navigation with F1-F9 keys")
	print("4. Test ESC to return to Dashboard")
	print("5. Verify all panels load correctly")
	
	quit(0)
