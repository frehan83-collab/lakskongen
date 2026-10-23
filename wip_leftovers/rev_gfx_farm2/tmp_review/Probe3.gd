extends RefCounted
## Review probe: the player holds the right mouse button (orbiting) when a
## first sight starts and lets go while it plays.

var t: Node = null

func _mouse_button(button: MouseButton, pressed: bool, pos: Vector2) -> void:
	var ev := InputEventMouseButton.new()
	ev.button_index = button
	ev.pressed = pressed
	ev.position = pos
	ev.global_position = pos
	ev.button_mask = MOUSE_BUTTON_MASK_RIGHT if pressed else 0
	Input.parse_input_event(ev)
	await t.get_tree().process_frame
	await t.get_tree().process_frame

func _motion(rel: Vector2, pos: Vector2) -> void:
	var ev := InputEventMouseMotion.new()
	ev.relative = rel
	ev.position = pos
	ev.global_position = pos
	ev.button_mask = 0
	Input.parse_input_event(ev)
	await t.get_tree().process_frame
	await t.get_tree().process_frame

func run(runner: Node) -> void:
	t = runner
	var sim: Node = t._sim
	FirstSightDirector.force_auto_play(true)
	MotionPrefs.force(false)
	t._check(sim.start_qa_scenario("po3_red_sone", 91), "probe3: scenario")
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
	cam.auto_orbit_enabled = false
	var ui: Control = scene.get_node("UILayer/GameUI")
	ui.show_tab("fjord")
	await t.get_tree().process_frame
	t.get_viewport().gui_release_focus()
	var contracted: Dictionary = bridge.QaContractOffshore("off_1", "of1")
	var uid := str(contracted.get("unit_id", ""))
	bridge.QaStageConstruction(uid, "commissioning", 0.9)
	sim.state_updated.emit()
	for i in 3:
		await t.get_tree().process_frame
	var centre := t.get_viewport().get_visible_rect().size * 0.5
	# Baseline: a motion with no button held does not orbit.
	var yaw0 := cam._yaw_target
	await _motion(Vector2(120, 0), centre)
	print("PROBE3 baseline: orbiting=%s yaw moved %.4f" % [cam._orbiting, cam._yaw_target - yaw0])
	# The player holds the right button over the world ...
	await _mouse_button(MOUSE_BUTTON_RIGHT, true, centre)
	print("PROBE3 right pressed: orbiting=%s input_enabled=%s" % [cam._orbiting, cam.input_enabled])
	# ... the unit comes into operation and its first sight starts ...
	bridge.QaStageConstruction(uid, "operational", 1.0)
	sim.state_updated.emit()
	for i in 3:
		await t.get_tree().process_frame
	print("PROBE3 first sight playing=%s" % director.playing)
	# ... the player lets go while it plays, then it ends.
	await _mouse_button(MOUSE_BUTTON_RIGHT, false, centre)
	print("PROBE3 right released during the sequence: orbiting=%s" % cam._orbiting)
	director.skip()
	await t.get_tree().process_frame
	var yaw1 := cam._yaw_target
	await _motion(Vector2(120, 0), centre)
	print("PROBE3 after the sequence, a bare mouse move: orbiting=%s yaw moved %.4f rad" % [cam._orbiting, cam._yaw_target - yaw1])
	FirstSightDirector.force_auto_play(null)
	MotionPrefs.force(null)
	scene.queue_free()
	await t.get_tree().process_frame
