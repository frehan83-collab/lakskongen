extends Node
## U2-03 frames: the end screen for a won, a bankrupt and a lost game, the
## bank's card with a rescue offer, and the scenario picker with a campaign,
## captured from the real GameScene and menu. The shots in godot/qa/shots.json
## for this scene (42_end_*) name the state; the frames go to
## res://screenshots/game/42_end_*.png. The campaign is a QA file of its own,
## never the player's.
## Run: tools/capture_screenshots.sh --scene res://qa/scenes/EndScreenCapture.tscn

const SCENE_PATH := "res://qa/scenes/EndScreenCapture.tscn"
const SHOTS_FILE := "res://qa/shots.json"
const OUT_DIR := "res://screenshots/game/"
const CAMPAIGN_FILE := "user://qa_capture/campaign.json"
const MAX_DAYS := 900
const HARVEST_KG := 4.5

var _sim: Node = null
var _scene: Node = null
var _menu: Node = null
var _failures := 0


func _ready() -> void:
	_run.call_deferred()


func _run() -> void:
	_sim = get_node("/root/SimBridge")
	CampaignProgress.path_override = CAMPAIGN_FILE
	CampaignProgress.reset()
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUT_DIR))
	for shot in _shots():
		var res: Array = shot.get("resolution", [1920, 1080])
		get_window().size = Vector2i(int(res[0]), int(res[1]))
		await _stage(str(shot.get("state", "")), shot)
		await _capture(str(shot["id"]))
	_clear()
	CampaignProgress.reset()
	print("[EndScreenCapture] done, %d failure(s)" % _failures)
	get_tree().quit(1 if _failures > 0 else 0)


func _shots() -> Array:
	var data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(SHOTS_FILE))
	return (data.get("shots", []) as Array).filter(func(s: Dictionary) -> bool: return str(s.get("scene", "")) == SCENE_PATH)


func _clear() -> void:
	for node in [_scene, _menu]:
		if node != null and is_instance_valid(node):
			node.queue_free()
	_scene = null
	_menu = null


func _new_scene() -> void:
	_clear()
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
	var screen: Node = _scene.get_node("%ScenarioEndScreen")
	screen.scene_change_override = func(_path: String) -> void: pass


func _day(harvest: bool) -> void:
	_sim.advance_days(1)
	if harvest:
		for site in _sim.get_all_pens():
			if int(site.get("fish_count", 0)) > 0 and float(site.get("mean_weight_kg", 0.0)) >= HARVEST_KG:
				_sim.harvest_pen(str(site.get("id", "")))


## Play without the scene to `stop`, then three days with it (the verdict or
## the bank call lands live).
func _play_to(scenario: String, seed: int, harvest: bool, rescue: bool, stop: Callable) -> void:
	_sim.start_qa_scenario(scenario, seed)
	_sim.set_paused(true)
	var days := 0
	for i in MAX_DAYS:
		if stop.call():
			break
		_day(harvest)
		if rescue and str((_sim.get_liquidity_status().get("offer", {}) as Dictionary).get("status", "")) == "open":
			_sim.accept_rescue_loan()
		days += 1
	var target := int(_sim.current_day)
	var verdict := str(_sim.get_current_verdict())
	_sim.start_qa_scenario(scenario, seed)
	_sim.set_paused(true)
	while int(_sim.current_day) < target - 3:
		_day(harvest)
		if rescue and str((_sim.get_liquidity_status().get("offer", {}) as Dictionary).get("status", "")) == "open":
			_sim.accept_rescue_loan()
	await _new_scene()
	for i in 6:
		if stop.call() or (verdict != "running" and str(_sim.get_current_verdict()) == verdict):
			break
		_day(harvest)
		if rescue and str((_sim.get_liquidity_status().get("offer", {}) as Dictionary).get("status", "")) == "open":
			_sim.accept_rescue_loan()


func _ended() -> bool:
	return str(_sim.get_current_verdict()) != "running"


func _bank_called() -> bool:
	return str(_sim.get_liquidity_status().get("stage", "")) == "bank_call"


## won, bankrupt, lost (ressursrente seed 5 harvesting / passive;
## algekatastrofe seed 1 passive with the rescue loan), bank_offer
## (algekatastrofe at the bank call, the Finance tab) and picker (the picker
## after the win: two scenarios opened, the medal, padlocks).
func _stage(state: String, shot: Dictionary) -> void:
	var scenario := str(shot.get("scenario", "ressursrente"))
	var seed := int(shot.get("seed", 5))
	match state:
		"won":
			await _play_to(scenario, seed, true, false, _ended)
		"bankrupt":
			await _play_to(scenario, seed, false, false, _ended)
		"lost":
			await _play_to(scenario, seed, false, true, _ended)
		"bank_offer":
			await _play_to(scenario, seed, false, false, _bank_called)
			var card: Node = _scene.get_node("%CriticalEventCard")
			for i in 20:
				if not card.is_open():
					break
				if str(card.current_event().get("type", "")) == "bank_call":
					card.verb_button.pressed.emit()
				else:
					card.continue_button.pressed.emit()
			var ui: Node = _scene.get_node("UILayer/GameUI")
			ui.show_tab("finance")
		"picker":
			_clear()
			await get_tree().process_frame
			var toasts := get_node_or_null("/root/ToastNotification")
			if toasts != null and toasts.has_method("clear_all"):
				toasts.clear_all()  # the last game's toasts do not belong to the menu frame
			_menu = (load("res://scenes/MainMenu.tscn") as PackedScene).instantiate()
			add_child(_menu)
			await _wait(1.0)
			_menu.btn_new_game.pressed.emit()
			await _wait(0.8)
			if _menu.scenario_panel != null:
				_menu.scenario_panel.select(scenario)
		_:
			push_warning("[EndScreenCapture] unknown state '%s'" % state)
			_failures += 1
	await _wait(2.2)


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
		push_error("[EndScreenCapture] could not save %s" % shot_id)
		_failures += 1
		return
	print("[EndScreenCapture] captured %s -> %s" % [shot_id, path])


func _wait(seconds: float) -> void:
	await get_tree().create_timer(seconds).timeout
