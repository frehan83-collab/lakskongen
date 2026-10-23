extends RefCounted
## Review probe: a first sight and the teaser that start while the player is
## on an opaque tab (the dashboard, the game's default): is the world
## rendered as a covered world (half scale, sky paused, no mirror)?

var t: Node = null

func _state(director: EnvironmentDirector) -> String:
	return "world_visible=%s scaling_3d_scale=%.2f" % [director.is_world_visible(), t.get_viewport().scaling_3d_scale]

func run(runner: Node) -> void:
	t = runner
	var sim: Node = t._sim
	FirstSightDirector.force_auto_play(true)
	TeaserDirector.force_auto_play(true)
	MotionPrefs.force(false)
	t._check(sim.start_qa_scenario("po3_red_sone", 91), "probe6: scenario")
	sim.set_paused(true)
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	t.add_child(scene)
	for i in 5:
		await t.get_tree().process_frame
	sim.set_paused(true)
	var env: Node = scene.get_node("FjordEnvironment")
	var manager: FarmFacilityVisualManager = env.get_node("FarmFacilityVisualManager")
	var director: EnvironmentDirector = env.get("director")
	var fs: FirstSightDirector = manager.first_sight
	var bridge := t.get_node("/root/GameBridge")
	var ui: Control = scene.get_node("UILayer/GameUI")
	(env.get_node("FjordCamera") as FjordCamera).auto_orbit_enabled = false
	ui.show_tab("finance")
	for i in 3:
		await t.get_tree().process_frame
	print("PROBE6 on the finance tab: " + _state(director))
	var contracted: Dictionary = bridge.QaContractOffshore("off_1", "of1")
	var uid := str(contracted.get("unit_id", ""))
	bridge.QaStageConstruction(uid, "commissioning", 0.9)
	sim.state_updated.emit()
	for i in 3:
		await t.get_tree().process_frame
	bridge.QaStageConstruction(uid, "operational", 1.0)
	sim.state_updated.emit()
	for i in 5:
		await t.get_tree().process_frame
	print("PROBE6 first sight playing=%s, UILayer visible=%s: %s" % [fs.playing, (scene.get_node("UILayer") as CanvasLayer).visible, _state(director)])
	fs.skip()
	for i in 3:
		await t.get_tree().process_frame
	print("PROBE6 after the first sight (tab %s): %s" % [ui.get("_active_tab_id"), _state(director)])
	scene.queue_free()
	await t.get_tree().process_frame
	# The teaser from the dashboard.
	t._check(sim.start_qa_scenario("forste_sesong", 42), "probe6: forste_sesong")
	sim.set_paused(true)
	sim.skip_tutorial()
	scene = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	t.add_child(scene)
	for i in 5:
		await t.get_tree().process_frame
	sim.set_paused(true)
	sim._refresh_state()
	await t.get_tree().process_frame
	env = scene.get_node("FjordEnvironment")
	manager = env.get_node("FarmFacilityVisualManager")
	director = env.get("director")
	ui = scene.get_node("UILayer/GameUI")
	(env.get_node("FjordCamera") as FjordCamera).auto_orbit_enabled = false
	print("PROBE6 teaser scene on tab %s: %s" % [ui.get("_active_tab_id"), _state(director)])
	sim.advance_days(151)
	for i in 5:
		await t.get_tree().process_frame
	print("PROBE6 teaser playing=%s day %d: %s" % [manager.teaser.playing, sim.current_day, _state(director)])
	if manager.teaser.playing:
		manager.teaser.skip()
	TeaserDirector.force_auto_play(null)
	FirstSightDirector.force_auto_play(null)
	MotionPrefs.force(null)
	scene.queue_free()
	await t.get_tree().process_frame
