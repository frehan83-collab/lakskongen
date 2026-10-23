extends RefCounted
## Review probe: the weather before a first sight, right after it, and a few
## seconds later, on a day the sim's weather is wet.

var t: Node = null

func _wx(ws: WeatherController) -> String:
	return "state=%d rain=%.2f snow=%.2f vis=%.0f m cloud=%.2f" % [ws.current_weather, ws.get_rain(), ws.get_snow(), ws.get_visibility_m(), ws.get_cloud_cover()]

func run(runner: Node) -> void:
	t = runner
	var sim: Node = t._sim
	FirstSightDirector.force_auto_play(true)
	MotionPrefs.force(false)
	t._check(sim.start_qa_scenario("po3_red_sone", 91), "probe5: scenario")
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
	var ws: WeatherController = env.get("weather_system")
	(env.get_node("FjordCamera") as FjordCamera).auto_orbit_enabled = false
	var wet := false
	for d in 120:
		var ctx: Dictionary = sim.get_sky_context()
		if float(ctx.get("precip_mm_h", 0.0)) > 0.5 or str(ctx.get("weather_state", "")) in ["Storm", "Severe"]:
			wet = true
			break
		sim.advance_days(1)
	print("PROBE5 day %d wet=%s ctx=%s" % [sim.current_day, wet, str(sim.get_sky_context())])
	ws.transition_duration = 0.0
	sim.state_updated.emit()
	for i in 3:
		await t.get_tree().process_frame
	ws.transition_duration = 10.0
	print("PROBE5 before: " + _wx(ws))
	var contracted: Dictionary = bridge.QaContractOffshore("off_1", "of1")
	var uid := str(contracted.get("unit_id", ""))
	bridge.QaStageConstruction(uid, "commissioning", 0.9)
	sim.state_updated.emit()
	for i in 3:
		await t.get_tree().process_frame
	bridge.QaStageConstruction(uid, "operational", 1.0)
	sim.state_updated.emit()
	for i in 3:
		await t.get_tree().process_frame
	print("PROBE5 during (playing=%s): %s" % [director.playing, _wx(ws)])
	director.skip()
	await t.get_tree().process_frame
	print("PROBE5 right after: " + _wx(ws))
	await t.get_tree().create_timer(2.0).timeout
	print("PROBE5 2 s after: " + _wx(ws))
	await t.get_tree().create_timer(9.0).timeout
	print("PROBE5 11 s after: " + _wx(ws))
	FirstSightDirector.force_auto_play(null)
	MotionPrefs.force(null)
	scene.queue_free()
	await t.get_tree().process_frame
