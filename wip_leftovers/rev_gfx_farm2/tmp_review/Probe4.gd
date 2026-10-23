extends RefCounted
## Review probe: a critical event card open on the tick a unit comes into
## operation (the same refresh): what shows during the first sight?

var t: Node = null

func run(runner: Node) -> void:
	t = runner
	var sim: Node = t._sim
	FirstSightDirector.force_auto_play(true)
	MotionPrefs.force(false)
	GameplayPrefs.force_auto_pause(true)
	var scene: Node = await t._spawn_po3_scene(91)
	var card: Control = scene.get_node("%CriticalEventCard")
	await t._drain_cards(card)
	var env: Node = scene.get_node("FjordEnvironment")
	var manager: FarmFacilityVisualManager = env.get_node("FarmFacilityVisualManager")
	var director: FirstSightDirector = manager.first_sight
	var bridge := t.get_node("/root/GameBridge")
	(env.get_node("FjordCamera") as FjordCamera).auto_orbit_enabled = false
	var contracted: Dictionary = bridge.QaContractOffshore("off_1", "of1")
	var uid := str(contracted.get("unit_id", ""))
	bridge.QaStageConstruction(uid, "commissioning", 0.9)
	sim.state_updated.emit()
	for i in 3:
		await t.get_tree().process_frame
	sim.set_paused(false)
	# The same tick: a critical event and the unit's completion.
	sim.publish_condition_event("oxygen_critical", "critical", "", "CI: oksygen kritisk lavt 3,1 mg/L")
	bridge.QaStageConstruction(uid, "operational", 1.0)
	sim.state_updated.emit()
	for i in 6:
		await t.get_tree().process_frame
	var layer := scene.get_node("EventLayer") as CanvasLayer
	print("PROBE4 first sight playing=%s; critical card open=%s visible_in_tree=%s; EventLayer visible=%s layer=%d; overlay layer=%d; UILayer visible=%s"
		% [director.playing, card.is_open(), card.is_visible_in_tree(), layer.visible, layer.layer,
			director.overlay.layer if director.overlay != null else -1, (scene.get_node("UILayer") as CanvasLayer).visible])
	var r := card.get_global_rect()
	print("PROBE4 card rect %s in view %s" % [str(r), str(t.get_viewport().get_visible_rect().size)])
	if director.playing:
		director.skip()
	await t._drain_cards(card)
	sim.set_paused(true)
	GameplayPrefs.force_auto_pause(null)
	FirstSightDirector.force_auto_play(null)
	MotionPrefs.force(null)
	scene.queue_free()
	await t.get_tree().process_frame
