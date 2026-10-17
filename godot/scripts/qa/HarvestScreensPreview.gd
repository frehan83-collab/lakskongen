extends Node
## U2-05 capture: the harvest planner and the harvest report in the running
## game, and the harvest carrier alongside. po3_red_sone (seed 91) is grown
## until a site's fish reach 4.5 kg; then the planner opens on it (41a), the
## eight-week column is shown (41b), the other site is sold express and its
## report opens (41c), the first site's planned order sends the carrier out
## (41d, the fjord with the UI hidden), the carrier lies alongside with the
## harvest's net on its chip once the slaughter days are done (41f), and the
## report follows (41e). Frames go to res://screenshots/game/41*_harvest_*.png.
## Run: tools/capture_screenshots.sh --scene res://qa/scenes/HarvestScreensPreview.tscn

const OUT_DIR := "res://screenshots/game"
const SCENARIO := "po3_red_sone"
const SEED := 91
const HARVEST_KG := 4.5
## FjordCamera.ZoomLevel.FARM.
const FARM_ZOOM := 2

var _sim: Node = null
var _game: Node = null


func _ready() -> void:
	_run.call_deferred()


func _run() -> void:
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUT_DIR))
	TranslationServer.set_locale("no")
	_sim = get_node("/root/SimBridge")
	_sim.start_qa_scenario(SCENARIO, SEED)
	_sim.skip_tutorial()
	_sim.set_paused(true)
	var site := _site_at_weight()
	for i in 60:
		if site != "":
			break
		_sim.advance_days(7)
		site = _site_at_weight()
	var other := ""
	for s in _sim.get_all_pens():
		if str(s.get("id", "")) != site and int(s.get("fish_count", 0)) > 0:
			other = str(s.get("id", ""))
	_game = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(_game)
	await _wait(2.5)
	_sim.set_paused(true)
	_close_cards()
	var ui := _game.find_child("GameUI", true, false)
	ui.call("show_tab", "pens")
	await _wait(0.5)
	_hide_notifications()

	var planner := HarvestScreens.planner(get_tree()) as HarvestPlanner
	planner.open_for(site, str(_sim.site_name(site)))
	await _wait(1.0)
	await _shoot("41a_harvest_planner")
	planner.wait_buttons[3].pressed.emit()
	await _wait(0.5)
	await _shoot("41b_harvest_planner_wait")
	planner.close()

	if other != "":
		planner.open_for(other, str(_sim.site_name(other)))
		await _wait(0.4)
		planner.express_button.pressed.emit()
		await _wait(1.6)
		await _shoot("41c_harvest_report_express")
		(HarvestScreens.report(get_tree()) as HarvestReport).close()

	planner.open_for(site, str(_sim.site_name(site)))
	await _wait(0.4)
	planner.order_button.pressed.emit()
	await _wait(5.0)
	_game.get_node("UILayer").visible = false
	var env := _game.get_node("FjordEnvironment")
	var pens := env.get_node("FishPenManager")
	env.call("focus_camera_on", pens.call("get_pen_position", site), FARM_ZOOM)
	await _wait(3.0)
	await _shoot("41d_harvest_wellboat")
	_game.get_node("UILayer").visible = true

	# The carrier lingers alongside long enough to read its payoff chip.
	var boats := env.get_node("WellboatVisualManager")
	boats.set("return_delay", 6.0)
	for d in 30:
		if _sim.get_harvest_orders().is_empty():
			break
		_sim.advance_days(1)
	_game.get_node("UILayer").visible = false
	_hide_notifications()
	await _wait(1.0)
	await _shoot("41f_harvest_wellboat_payoff")
	_game.get_node("UILayer").visible = true
	await _wait(0.5)
	_close_cards()
	(HarvestScreens.report(get_tree()) as HarvestReport).show_latest_for(site)
	await _wait(1.6)
	await _shoot("41e_harvest_report_planned")
	get_tree().quit(0)


func _site_at_weight() -> String:
	for s in _sim.get_all_pens():
		var fish := int(s.get("fish_count", 0))
		if fish > 0 and float(s.get("biomass_kg", 0.0)) / fish >= HARVEST_KG:
			return str(s.get("id", ""))
	return ""


func _close_cards() -> void:
	var card := get_tree().get_first_node_in_group(&"critical_event_presenter")
	var guard := 0
	while card != null and card.has_method("is_open") and card.is_open() and guard < 50:
		card.continue_button.pressed.emit()
		guard += 1


func _hide_notifications() -> void:
	var center := _game.find_child("NotificationCenter", true, false)
	if center != null and center.has_method("hide_panel"):
		center.call("hide_panel")
	var toasts := get_node_or_null("/root/ToastNotification")
	if toasts != null and toasts.has_method("clear_all"):
		toasts.clear_all()


func _shoot(shot: String) -> void:
	DisplayServer.window_move_to_foreground()
	var done := [false]
	RenderingServer.frame_post_draw.connect(func() -> void: done[0] = true, CONNECT_ONE_SHOT)
	var waited := 0.0
	while not done[0] and waited < 3.0:
		await get_tree().create_timer(0.1).timeout
		waited += 0.1
	var img := get_viewport().get_texture().get_image()
	var path := ProjectSettings.globalize_path(OUT_DIR).path_join(shot + ".png")
	img.save_png(path)
	print("[HarvestScreensPreview] captured %s" % path)


func _wait(seconds: float) -> void:
	await get_tree().create_timer(seconds).timeout
