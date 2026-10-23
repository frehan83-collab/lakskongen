extends RefCounted
## Review probe: does the first sight hand the camera back without a jump,
## and does it give back the hour, the weather and the mood?

var t: Node = null

func run(runner: Node) -> void:
	t = runner
	var sim: Node = t._sim
	FirstSightDirector.force_auto_play(true)
	MotionPrefs.force(false)
	t._check(sim.start_qa_scenario("po3_red_sone", 91), "probe: scenario")
	sim.set_paused(true)
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	t.add_child(scene)
	for i in 5:
		await t.get_tree().process_frame
	sim.set_paused(true)
	var env: Node = scene.get_node("FjordEnvironment")
	var cam: FjordCamera = env.get_node("FjordCamera")
	var manager: FarmFacilityVisualManager = env.get_node("FarmFacilityVisualManager")
	var director: FirstSightDirector = manager.first_sight
	var bridge := t.get_node("/root/GameBridge")
	var ts: TimeOfDay = env.get("time_system")
	var ws: WeatherController = env.get("weather_system")
	var ed: EnvironmentDirector = env.get("director")
	var vista: Node = scene.get_node("FjordVistaBridge")
	cam.auto_orbit_enabled = false
	var placed: Dictionary = bridge.QaPlaceFarmTypeSet(0, 2, 0)
	sim.state_updated.emit()
	for i in 3:
		await t.get_tree().process_frame
	print("PROBE placed %s" % str(placed.get("success", false)))
	for mode in ["land", "hav"]:
		var started: Dictionary
		var uid := ""
		if mode == "land":
			started = bridge.QaStartLandBuild("pad_a", "hfs_lot")
			uid = str(started.get("unit_id", ""))
			bridge.QaStageConstruction(uid, "completion", 0.95)
		else:
			started = bridge.QaContractOffshore("off_1", "of1")
			uid = str(started.get("unit_id", ""))
			bridge.QaStageConstruction(uid, "commissioning", 0.9)
		print("PROBE %s started %s" % [mode, str(started)])
		print("PROBE %s view after start: op=%s build=%s" % [mode, str(manager.views.get(uid, {}).get("operational", "none")), str(manager.views.get(uid, {}).get("build", {}).get("phase_key", "none"))])
		sim.state_updated.emit()
		for i in 3:
			await t.get_tree().process_frame
		print("PROBE %s view after stage: op=%s build=%s playing=%s" % [mode, str(manager.views.get(uid, {}).get("operational", "none")), str(manager.views.get(uid, {}).get("build", {}).get("phase_key", "none")), director.playing])
		var before := {"hour": ts.get_hour(), "paused": ts.paused, "weather": ws.current_weather,
			"sim_weather": vista.get("sim_weather_enabled"), "hook": ed.mood_hook, "hook_s": ed.mood_hook_strength,
			"vis": ws.get_visibility_m(), "cloud": ws.get_cloud_cover()}
		bridge.QaStageConstruction(uid, "operational", 1.0)
		sim.state_updated.emit()
		for i in 3:
			await t.get_tree().process_frame
		print("PROBE %s playing=%s unit=%s dur=%.2f" % [mode, director.playing, director.unit_id, director.duration])
		director.manual = true
		director.seek(director.duration)
		await t.get_tree().process_frame
		var end_xf := cam.global_transform
		var end_fov := cam.fov
		director.skip()
		var after_xf := cam.global_transform
		var after_fov := cam.fov
		for i in 30:
			await t.get_tree().process_frame
		var settled_xf := cam.global_transform
		var d1 := end_xf.origin.distance_to(after_xf.origin)
		var a1 := rad_to_deg(end_xf.basis.z.angle_to(after_xf.basis.z))
		var d2 := after_xf.origin.distance_to(settled_xf.origin)
		var a2 := rad_to_deg(after_xf.basis.z.angle_to(settled_xf.basis.z))
		print("PROBE %s handback: last frame -> after leave: %.2f m, %.2f deg, fov %.2f -> %.2f; after leave -> 30 frames later: %.2f m, %.2f deg" % [mode, d1, a1, end_fov, after_fov, d2, a2])
		var after := {"hour": ts.get_hour(), "paused": ts.paused, "weather": ws.current_weather,
			"sim_weather": vista.get("sim_weather_enabled"), "hook": ed.mood_hook, "hook_s": ed.mood_hook_strength,
			"vis": ws.get_visibility_m(), "cloud": ws.get_cloud_cover()}
		print("PROBE %s env before %s" % [mode, str(before)])
		print("PROBE %s env after  %s" % [mode, str(after)])
	FirstSightDirector.force_auto_play(null)
	MotionPrefs.force(null)
	scene.queue_free()
	await t.get_tree().process_frame
