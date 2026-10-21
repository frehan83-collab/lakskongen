extends Node
## Geo stages 2-3 Track D frames (the GEO-VIS review): the map's region and
## site levels from the real GameScene, at 1280x720 and 1920x1080, at 100 %
## and 125 % UI scale. The frames go to res://screenshots/game/geo_region_*.png.
## Run (windowed; a display is required):
##   tools/capture_screenshots.sh --scene res://qa/scenes/GeoRegionCapture.tscn
## Optional: -- <shot id> ... captures only those shots.

const OUT_DIR := "res://screenshots/game/"
## [id, window size, UI scale, area, what to open]
const SHOTS := [
	["geo_region_po3_1920", Vector2i(1920, 1080), 1.0, 3, "region"],
	["geo_region_po3_1280", Vector2i(1280, 720), 1.0, 3, "region"],
	["geo_region_po3_1280_125", Vector2i(1280, 720), 1.25, 3, "region"],
	["geo_site_candidate_1920", Vector2i(1920, 1080), 1.0, 3, "candidate"],
	["geo_site_candidate_1280", Vector2i(1280, 720), 1.0, 3, "candidate"],
	["geo_site_candidate_1280_125", Vector2i(1280, 720), 1.25, 3, "candidate"],
	["geo_site_licence_po4_1280", Vector2i(1280, 720), 1.0, 4, "candidate"],
	["geo_site_hav_1920", Vector2i(1920, 1080), 1.0, 3, "hav"],
	["geo_site_yours_1920", Vector2i(1920, 1080), 1.0, 3, "site"],
	["geo_region_po6_1920", Vector2i(1920, 1080), 1.0, 6, "region"],
	["geo_region_po11_1920", Vector2i(1920, 1080), 1.0, 11, "region"],
	["geo_site_rival_1920", Vector2i(1920, 1080), 1.0, 3, "rival"],
	["geo_national_explore_1280", Vector2i(1280, 720), 1.0, 3, "national"],
	# The captain's D merge: a click names a national feature (the detail
	# column's button to its card), the company's places as rows, PO10's
	# river rings at the map's edge, the crop's shaded heat.
	["geo_national_named_1280", Vector2i(1280, 720), 1.0, 4, "named"],
	["geo_national_named_1920", Vector2i(1920, 1080), 1.0, 4, "named"],
	["geo_region_po10_1920", Vector2i(1920, 1080), 1.0, 10, "region"],
	["geo_site_rival_1280", Vector2i(1280, 720), 1.0, 3, "rival"],
	["geo_site_candidate_po6_1920", Vector2i(1920, 1080), 1.0, 6, "candidate"],
]

var _sim: Node = null
var _scene: Node = null
var _map: Control = null
var _failures := 0


func _ready() -> void:
	_run.call_deferred()


func _run() -> void:
	_sim = get_node("/root/SimBridge")
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUT_DIR))
	var only := OS.get_cmdline_user_args()
	_sim.start_qa_scenario("po3_red_sone", 91)
	_sim.skip_tutorial()
	await _new_scene()
	for shot: Array in SHOTS:
		if not only.is_empty() and not only.has(shot[0]):
			continue
		get_window().size = shot[1]
		get_window().content_scale_factor = shot[2]
		await _wait(0.4)
		await _stage(shot[3], shot[4])
		await _capture(shot[0])
	if _scene != null:
		_scene.queue_free()
	print("[GeoRegionCapture] done, %d failure(s)" % _failures)
	get_tree().quit(1 if _failures > 0 else 0)


func _new_scene() -> void:
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
	var ui: Control = _scene.get_node("UILayer/GameUI")
	ui.show_tab("map")
	await _wait(0.6)
	_map = ui.find_child("Panel_map", true, false)


func _stage(area: int, what: String) -> void:
	var ui: Control = _scene.get_node("UILayer/GameUI")
	if ui.get_active_tab() != "map":
		ui.show_tab("map")
		await _wait(0.5)
		_map = ui.find_child("Panel_map", true, false)
	if not _map.drill_into(area):
		push_warning("[GeoRegionCapture] cannot drill into %d" % area)
		_failures += 1
		return
	await _wait(0.4)
	var drill: GeoDrillStack = _map.geo_drill()
	var view := drill.region_view()
	match what:
		"candidate":
			var rows := view.candidates()
			drill.open_candidate(rows[0] if not rows.is_empty() else {})
		"hav":
			for r: Dictionary in view.offshore():
				if String(r.get("bound_plot_id", "")) != "":
					drill.open_offshore(r)
		"rival":
			var icons: Array = (view.rival_layer() as Object).get("icons")
			if not icons.is_empty():
				drill.open_rival(icons[0])
		"national":
			drill.go_national()
			_map.select_area(area)
		"named":
			drill.go_national()
			_map.select_area(area)
			for s: Dictionary in (_map.chart() as GeoMapView).candidate_layer().star_groups():
				if int(s["po"]) == area:
					_map._on_feature_clicked("candidates", String(s["best"].get("id", "")))
		"site":
			var sites := view.player_sites()
			if not sites.is_empty():
				drill.open_player_site(sites[0])
		_:
			# The pointer over the suggested candidate's water: the readout.
			var rows := view.candidates()
			if not rows.is_empty():
				var at := view.to_view(GeoRegionMath.map_px(area, rows[0]["x"], rows[0]["y"])) + Vector2(30, 0)
				view.readout().text = view.readout_at(at)
	await _wait(0.6)


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
		push_error("[GeoRegionCapture] could not save %s" % shot_id)
		_failures += 1
		return
	print("[GeoRegionCapture] captured %s -> %s" % [shot_id, path])


func _wait(seconds: float) -> void:
	await get_tree().create_timer(seconds).timeout
