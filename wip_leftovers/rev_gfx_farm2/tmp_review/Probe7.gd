extends RefCounted
## Review probe (windowed): a frame of a first sight that started on the
## tick a critical event card opened.

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
	sim.publish_condition_event("oxygen_critical", "critical", "", "CI: oksygen kritisk lavt 3,1 mg/L")
	bridge.QaStageConstruction(uid, "operational", 1.0)
	sim.state_updated.emit()
	for i in 6:
		await t.get_tree().process_frame
	director.manual = true
	director.seek(11.0)
	for i in 30:
		await t.get_tree().process_frame
	var img := t.get_viewport().get_texture().get_image()
	if img != null:
		img.save_png("/private/tmp/claude-501/-Users-fredrik-j-hansen-lakskongen/1a7df52a-cf33-40ac-aaf8-eaf13ebf76df/scratchpad/rgf2_frames/probe7_card_over_first_sight.png")
	print("PROBE7 playing=%s card_open=%s saved" % [director.playing, card.is_open()])
	director.skip()
	await t._drain_cards(card)
	sim.set_paused(true)
	GameplayPrefs.force_auto_pause(null)
	FirstSightDirector.force_auto_play(null)
	MotionPrefs.force(null)
	scene.queue_free()
	await t.get_tree().process_frame
