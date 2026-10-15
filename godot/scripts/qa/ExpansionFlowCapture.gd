extends Node
## U2-01 expansion flow frames: the region view, the build dialog and the
## licence auction card, in the states a player meets them, captured from the
## real GameScene. The shots in godot/qa/shots.json for this scene
## (41_expansion_*) name the state; the frames go to
## res://screenshots/game/41_expansion_*.png.
## Run: tools/capture_screenshots.sh --scene res://qa/scenes/ExpansionFlowCapture.tscn

const SCENE_PATH := "res://qa/scenes/ExpansionFlowCapture.tscn"
const SHOTS_FILE := "res://qa/shots.json"
const OUT_DIR := "res://screenshots/game/"

var _sim: Node = null
var _scene: Node = null
var _ui: Control = null
var _map: Control = null
var _failures := 0


func _ready() -> void:
	_run.call_deferred()


func _run() -> void:
	_sim = get_node("/root/SimBridge")
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUT_DIR))
	for shot in _shots():
		var res: Array = shot.get("resolution", [1920, 1080])
		get_window().size = Vector2i(int(res[0]), int(res[1]))
		await _stage(str(shot.get("state", "")), shot)
		await _capture(str(shot["id"]))
	if _scene != null:
		_scene.queue_free()
	print("[ExpansionCapture] done, %d failure(s)" % _failures)
	get_tree().quit(1 if _failures > 0 else 0)


func _shots() -> Array:
	var data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(SHOTS_FILE))
	return (data.get("shots", []) as Array).filter(func(s: Dictionary) -> bool: return str(s.get("scene", "")) == SCENE_PATH)


func _new_scene() -> void:
	if _scene != null:
		_scene.queue_free()
		await get_tree().process_frame
	var toasts := get_node_or_null("/root/ToastNotification")
	if toasts != null and toasts.has_method("clear_all"):
		toasts.clear_all()
	_scene = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(_scene)
	for i in 8:
		await get_tree().process_frame
	_sim.set_paused(true)
	var card: Node = _scene.get_node_or_null("%CriticalEventCard")
	for i in 20:
		if card == null or not card.is_open():
			break
		card.continue_button.pressed.emit()
	_ui = _scene.get_node("UILayer/GameUI")
	_ui.show_tab("map")
	await _wait(0.6)
	_map = _ui.find_child("Panel_map", true, false)


## Stage one state: "map" and "build_refused" on a new Første sesong (4 MNOK),
## "auction_live", "auction_won" and "build_new_zone" on a 600 MNOK company
## bidding for the Solund licence in PO 4.
func _stage(state: String, shot: Dictionary) -> void:
	var seed := int(shot.get("seed", 4))
	match state:
		"map", "build_refused":
			_sim.start_qa_scenario(str(shot.get("scenario", "forste_sesong")), seed)
			await _new_scene()
			_map.select_area(3)
			if state == "build_refused":
				_map.open_build("fjord_west")
		"auction_preview":
			_sim.start_qa_new_game("Player", 600_000_000, seed)
			await _new_scene()
			_map.select_area(4)
			_map.open_auction("po04_solund")
		"auction_live":
			_sim.start_qa_new_game("Player", 600_000_000, seed)
			_sim.open_licence_auction("po04_solund")
			_sim.bid_on_licence_auction("po04_solund", 150_000_000.0)
			_sim.advance_days(4)
			await _new_scene()
			_map.select_area(4)
			_map.open_auction("po04_solund")
		"auction_won":
			_sim.start_qa_new_game("Player", 600_000_000, seed)
			_sim.open_licence_auction("po04_solund")
			_sim.bid_on_licence_auction("po04_solund", 150_000_000.0)
			_sim.advance_days(2)
			var a: Dictionary = _sim.get_licence_auction("po04_solund")
			_sim.bid_on_licence_auction("po04_solund", float(a.get("min_bid_nok", 0.0)) + 100_000_000.0)
			_sim.advance_days(8)
			await _new_scene()
			_map.select_area(4)
			_map.open_auction("po04_solund")
		"build_new_zone":
			_map.open_build("po04_solund")
		_:
			push_warning("[ExpansionCapture] unknown state '%s'" % state)
			_failures += 1
	await _wait(0.9)


func _capture(shot_id: String) -> void:
	DisplayServer.window_move_to_foreground()
	var done := [false]
	RenderingServer.frame_post_draw.connect(func() -> void: done[0] = true, CONNECT_ONE_SHOT)
	var waited := 0.0
	while not done[0] and waited < 3.0:
		await get_tree().create_timer(0.1).timeout
		waited += 0.1
	var img := get_viewport().get_texture().get_image()
	var path := ProjectSettings.globalize_path(OUT_DIR + shot_id + ".png")
	if img == null or img.save_png(path) != OK:
		push_error("[ExpansionCapture] could not save %s" % shot_id)
		_failures += 1
		return
	print("[ExpansionCapture] captured %s -> %s" % [shot_id, path])


func _wait(seconds: float) -> void:
	await get_tree().create_timer(seconds).timeout
