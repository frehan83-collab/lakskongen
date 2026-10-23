extends Node
## Local look at the F2-21 bookmark chip in the HUD (not committed): the game
## scene with the QA facility set, G pressed as a player does, a frame saved.

const OUT := "/private/tmp/claude-501/-Users-fredrik-j-hansen-lakskongen/1a7df52a-cf33-40ac-aaf8-eaf13ebf76df/scratchpad/hint_shots"


func _ready() -> void:
	_run.call_deferred()


func _run() -> void:
	SettingsStore.path_override = "user://settings_hintshot.json"
	TranslationServer.set_locale("no")
	var sim := get_node("/root/SimBridge")
	sim.start_qa_scenario("po3_red_sone", 91)
	sim.set_paused(true)
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 10:
		await get_tree().process_frame
	var bridge := get_node("/root/GameBridge")
	bridge.QaPlaceFarmTypeSet(1, 2, 1)
	sim.state_updated.emit()
	for i in 10:
		await get_tree().process_frame
	var game_ui := scene.get_node_or_null("UILayer/GameUI")
	if game_ui != null and game_ui.has_method("show_tab"):
		game_ui.call("show_tab", "fjord")
	for i in 30:
		await get_tree().process_frame
	DirAccess.make_dir_recursive_absolute(OUT)
	for k in 3:
		for pressed: bool in [true, false]:
			var ev := InputEventKey.new()
			ev.keycode = KEY_G
			ev.physical_keycode = KEY_G
			ev.unicode = 103 if pressed else 0
			ev.pressed = pressed
			get_viewport().push_input(ev)
			await get_tree().process_frame
		await get_tree().create_timer(1.6).timeout
		await RenderingServer.frame_post_draw
		var img := get_viewport().get_texture().get_image()
		img.save_png(OUT.path_join("hint_g%d.png" % (k + 1)))
		print("[HintShot] saved hint_g%d.png" % (k + 1))
	get_tree().quit(0)
