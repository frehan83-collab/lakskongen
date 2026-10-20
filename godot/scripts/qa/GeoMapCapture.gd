extends Node
## Geo stages 2-3 Track C: frames of the national map (MapPanel v2) for the
## visual review (GEO-VIS), captured from the real GameScene. Not part of
## godot/qa/shots.json: no baseline, nothing is compared; it writes PNGs and
## prints what it laid out.
## Run (windowed): tools/capture_screenshots.sh --scene res://qa/scenes/GeoMapCapture.tscn
##   -- [--out=<absolute dir>] [--scenario=po3_red_sone] [--seed=7]
## Frames: map_1920x1080_100, map_1280x720_100, map_1280x720_150 (the UI at
## 150 %, a 1280x720 layout), map_1920x1080_po4 (PO4 picked) and
## map_1920x1080_facts (the detail column at PO3's facts).
## Headless it only lays the map out and prints the layout (no images).

const SIZES: Array = [[Vector2i(1920, 1080), 1.0], [Vector2i(1280, 720), 1.0], [Vector2i(1280, 720), 1.5]]

var _sim: Node = null
var _scene: Node = null
var _failures := 0


func _ready() -> void:
	_run.call_deferred()


func _run() -> void:
	_sim = get_node("/root/SimBridge")
	var args := _args()
	var out_dir: String = args.get("out", ProjectSettings.globalize_path("res://screenshots/game/"))
	DirAccess.make_dir_recursive_absolute(out_dir)
	_sim.start_qa_scenario(str(args.get("scenario", "po3_red_sone")), int(args.get("seed", "7")))
	_sim.set_paused(true)
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
	await _wait(0.8)
	var map: Control = ui.find_child("Panel_map", true, false)
	var win := get_window()
	for spec: Array in SIZES:
		win.size = spec[0]
		win.content_scale_factor = spec[1]
		await _wait(0.9)
		var tag := "map_%dx%d_%d" % [spec[0].x, spec[0].y, int(round(float(spec[1]) * 100.0))]
		_report(tag, map)
		await _capture(out_dir, tag)
	win.size = Vector2i(1920, 1080)
	win.content_scale_factor = 1.0
	await _wait(0.6)
	map.select_area(4)
	await _wait(0.5)
	await _capture(out_dir, "map_1920x1080_po4")
	# The detail column scrolled to the area's facts.
	map.select_area(3)
	await _wait(0.4)
	var facts: Control = map.area_facts()
	var scroll: ScrollContainer = map.find_child("DetailScroll", true, false)
	if facts != null and scroll != null:
		scroll.ensure_control_visible(facts)
	await _wait(0.5)
	await _capture(out_dir, "map_1920x1080_facts")
	print("[GeoMapCapture] done, %d failure(s)" % _failures)
	get_tree().quit(1 if _failures > 0 else 0)


func _report(tag: String, map: Control) -> void:
	var chart: GeoMapView = map.chart()
	var plates: Array = chart.plates().values().map(func(p: MapMarker) -> String:
		return "%s@%s%s" % [p.region_id, str(p.get_rect()), " leader" if p.leader else ""])
	print("[GeoMapCapture] %s chart %s drawn %s compact %s missing reads %s" % [tag, str(chart.size),
		str(chart.drawn_rect()), str(chart.compact), str(chart.bridge.missing())])
	print("[GeoMapCapture]   plates %s" % ", ".join(plates))
	print("[GeoMapCapture]   cities %s" % str(chart.city_label_rects()))
	print("[GeoMapCapture]   clusters %d totals %s" % [chart.site_layer().clusters().size(),
		str(GeoClusterer.totals(chart.site_layer().clusters()))])


func _capture(out_dir: String, tag: String) -> void:
	if DisplayServer.get_name() == "headless":
		return
	DisplayServer.window_move_to_foreground()
	var done := [false]
	RenderingServer.frame_post_draw.connect(func() -> void: done[0] = true, CONNECT_ONE_SHOT)
	var waited := 0.0
	while not done[0] and waited < 3.0:
		await get_tree().create_timer(0.1).timeout
		waited += 0.1
	var img := get_viewport().get_texture().get_image()
	var path := out_dir.path_join(tag + ".png")
	if img == null or img.save_png(path) != OK:
		push_error("[GeoMapCapture] could not save %s" % path)
		_failures += 1
		return
	print("[GeoMapCapture] captured %s" % path)


func _args() -> Dictionary:
	var out := {}
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--") and "=" in a:
			out[a.substr(2, a.find("=") - 2)] = a.substr(a.find("=") + 1)
	return out


func _wait(seconds: float) -> void:
	await get_tree().create_timer(seconds).timeout
