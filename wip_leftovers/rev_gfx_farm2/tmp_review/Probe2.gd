extends RefCounted
## Review probe: a save loaded in the running game scene (the pause menu's
## load) where a unit is already in operation, over a game where the same
## unit id is still under construction.

var t: Node = null

func run(runner: Node) -> void:
	t = runner
	var sim: Node = t._sim
	FirstSightDirector.force_auto_play(false)
	MotionPrefs.force(false)
	t._check(sim.start_qa_scenario("po3_red_sone", 91), "probe2: scenario")
	sim.set_paused(true)
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	t.add_child(scene)
	for i in 5:
		await t.get_tree().process_frame
	sim.set_paused(true)
	var env: Node = scene.get_node("FjordEnvironment")
	var manager: FarmFacilityVisualManager = env.get_node("FarmFacilityVisualManager")
	var director: FirstSightDirector = manager.first_sight
	var bridge := t.get_node("/root/GameBridge")
	(env.get_node("FjordCamera") as FjordCamera).auto_orbit_enabled = false
	bridge.QaPlaceFarmTypeSet(0, 2, 0)
	sim.state_updated.emit()
	for i in 3:
		await t.get_tree().process_frame
	var started: Dictionary = bridge.QaStartLandBuild("pad_a", "hfs_lot")
	var uid := str(started.get("unit_id", ""))
	sim.state_updated.emit()
	for i in 3:
		await t.get_tree().process_frame
	print("PROBE2 %s building: op=%s phase=%s" % [uid, manager.views[uid].get("operational"), manager.views[uid].get("build", {}).get("phase_key")])
	t._check(sim.save_game("probe_s0"), "probe2: save s0 (building)")
	var s0 := str(t._find_save("probe_s0").get("file", ""))
	bridge.QaStageConstruction(uid, "operational", 1.0)
	sim.state_updated.emit()
	for i in 3:
		await t.get_tree().process_frame
	print("PROBE2 %s staged: op=%s phase=%s playing=%s" % [uid, manager.views[uid].get("operational"), manager.views[uid].get("build", {}).get("phase_key"), director.playing])
	t._check(sim.save_game("probe_s1"), "probe2: save s1 (operational)")
	var s1 := str(t._find_save("probe_s1").get("file", ""))
	# The player loads the older save (the unit under construction) ...
	t._check(sim.load_game(s0), "probe2: load s0")
	for i in 3:
		await t.get_tree().process_frame
	print("PROBE2 after load s0: op=%s phase=%s playing=%s" % [manager.views[uid].get("operational"), manager.views[uid].get("build", {}).get("phase_key"), director.playing])
	# ... then, in the player's own game, the later one (the unit long in operation).
	FirstSightDirector.force_auto_play(true)
	t._check(sim.load_game(s1), "probe2: load s1")
	for i in 3:
		await t.get_tree().process_frame
	print("PROBE2 after load s1: op=%s phase=%s playing=%s unit=%s played=%s clock_held=%s" % [manager.views[uid].get("operational"), manager.views[uid].get("build", {}).get("phase_key"), director.playing, director.unit_id, str(director.played), sim.is_clock_held("first_sight")])
	if director.playing:
		director.skip()
	FirstSightDirector.force_auto_play(null)
	MotionPrefs.force(null)
	scene.queue_free()
	await t.get_tree().process_frame
