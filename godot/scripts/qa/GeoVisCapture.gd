extends Node
## GEO-VIS, the windowed review of geo stages 2-3 (docs/geo/STAGE23_PLAN.md
## §4 step 5), captured in one batch from the real GameScene: the map's
## national, region and site levels, the build dialog's 'why here' (the map's
## 'Bygg her') and its Hav lens (hav_1's card), each at 1280x720 and
## 1920x1080 with the UI at 100 % and 125 %; then the 3D fjord's rival farms in
## the order GetCompetitorCompanies deals them (po3_red_sone, laksebaronene,
## lofoten_eventyr), and the lens and ghost with hav_1 at the far clamp (a
## home area 250 km from any Havbruk til havs area). Not part of
## godot/qa/shots.json: there is no baseline and nothing is compared. It
## writes PNGs to res://screenshots/game/geovis_*.png and prints what it
## staged, for the review in docs/run/evidence/geo_vis_review.md.
## Run (windowed; a display is required):
##   tools/capture_screenshots.sh --scene res://qa/scenes/GeoVisCapture.tscn
## Optional: -- <frame id prefix> ... captures only the frames that match.

const OUT_DIR := "res://screenshots/game/"
## [window size, UI scale]
const SIZES := [
	[Vector2i(1280, 720), 1.0],
	[Vector2i(1280, 720), 1.25],
	[Vector2i(1920, 1080), 1.0],
	[Vector2i(1920, 1080), 1.25],
]
## The map's levels and the 'why here', in the order a player meets them.
const STEPS := ["national", "region", "site", "why_here"]
## The Hav lens needs an offshore unit the company may start: the research,
## a first harvest and a year without an escape (BuildFlowChecks' setup).
const HAV_RESEARCH := "deep-sea-farming"
const ESCAPE_FREE_DAYS := 365
## The 3D rival order: scenarios whose home areas deal different orders.
const RIVAL_SCENARIOS := [["po3_red_sone", 91], ["laksebaronene", 7], ["lofoten_eventyr", 7]]
## Distance (m) from the camera to a rival farm: inside its label's range.
const RIVAL_VIEW_M := 620.0
## Games whose hav_1 sits at the far clamp (Z -9000): [scenario, seed, home PO].
const FAR_CLAMP_SCENARIOS := [["barekraft_mester", 7, 4], ["algekatastrofe", 7, 9], ["ressursrente", 5, 4],
	["lofoten_eventyr", 7, 9]]

var _sim: Node = null
var _scene: Node = null
var _ui: Control = null
var _map: Control = null
var _only: PackedStringArray = []
var _failures := 0
var _frames := 0


func _ready() -> void:
	_run.call_deferred()


func _run() -> void:
	if DisplayServer.get_name() == "headless":
		push_error("[GeoVisCapture] no display; headless renders nothing. Run it windowed.")
		get_tree().quit(1)
		return
	_sim = get_node("/root/SimBridge")
	_only = OS.get_cmdline_user_args()
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUT_DIR))
	if _wants("geovis_map_"):
		await _map_frames()
	if _wants("geovis_map_hav_lens"):
		await _hav_lens_frames()
	if _wants("geovis_rivals3d_"):
		await _rival_frames()
	if _wants("geovis_hav_far"):
		await _far_clamp_frames()
	if _scene != null:
		_scene.queue_free()
	print("[GeoVisCapture] done, %d frame(s), %d failure(s)" % [_frames, _failures])
	get_tree().quit(1 if _failures > 0 else 0)


## True when no prefix was given or one of them starts `id` (or `id` starts it).
func _wants(id: String) -> bool:
	if _only.is_empty():
		return true
	for p in _only:
		if id.begins_with(p) or p.begins_with(id):
			return true
	return false


# ── The map's levels, the 'why here' and the Hav lens ───────────────────────

func _map_frames() -> void:
	await _start("po3_red_sone", 91)
	for spec: Array in SIZES:
		var size: Vector2i = spec[0]
		var scale: float = spec[1]
		var tag := "%dx%d_%d" % [size.x, size.y, int(round(scale * 100.0))]
		get_window().size = size
		get_window().content_scale_factor = scale
		await _wait(0.7)
		for step: String in STEPS:
			var id := "geovis_map_%s_%s" % [step, tag]
			if not _wants(id):
				continue
			if await _stage_map(step):
				await _capture(id)
			await _unstage_map()
	get_window().size = Vector2i(1920, 1080)
	get_window().content_scale_factor = 1.0


## Stage one step on the map tab from the national level; false when the
## step cannot be reached (a failure is counted).
func _stage_map(step: String) -> bool:
	await _show_map()
	_map.select_area(3)
	await _wait(0.5)
	if step == "national":
		_report_national()
		return true
	if not _map.drill_into(3):
		return _fail("cannot drill into PO3")
	await _wait(0.6)
	var drill: GeoDrillStack = _map.geo_drill()
	var view := drill.region_view()
	if step == "region":
		print("[GeoVisCapture] region PO3: %d candidates, %d offshore, %d of the company's sites" % [
			view.candidates().size(), view.offshore().size(), view.player_sites().size()])
		return true
	var rows := view.candidates()
	if step == "site" or step == "why_here":
		if rows.is_empty():
			return _fail("PO3 lists no candidate")
		drill.open_candidate(rows[0])
		await _wait(0.6)
		var card := drill.site_card()
		print("[GeoVisCapture] site card %s '%s': action %s (%s)" % [str(rows[0].get("id", "")),
			card.title_text(), card.action(), "on" if not card.action_button().disabled else "off: " + card.action_button().tooltip_text])
		if step == "site":
			return true
		# The map's 'Bygg her' (refused or not, the 'why here' is the same).
		_map.open_build_at(String(rows[0].get("build_zone_id", "")), String(rows[0].get("id", "")))
		await _wait(1.2)
		return _report_dialog("why_here")
	return _fail("unknown step '%s'" % step)


## hav_1's card, then its 'Bygg her (hav)': the Hav step with hav_1 chosen.
## The national map's route, a double-click on the ringed hexagon, which
## drills into the hexagon's own area (in a home area with no Havbruk til
## havs area of its own, hav_1 lies in a neighbour's).
func _open_hav_from_the_map(area: int) -> bool:
	await _show_map()
	_map.select_area(area)
	await _wait(0.4)
	var bound := ""
	for r: Dictionary in (_map.chart() as GeoMapView).bridge.offshore_candidates():
		if String(r.get("bound_plot_id", "")) != "":
			bound = String(r.get("id", ""))
	if bound == "":
		return _fail("no offshore candidate is bound to hav_1")
	_map._on_feature_drill_requested("hexagon", bound)
	await _wait(0.9)
	var drill: GeoDrillStack = _map.geo_drill()
	var hav := drill.site_card()
	if hav.action() != "hav":
		return _fail("hav_1's card offers no Hav build (action '%s')" % hav.action())
	hav.action_button().pressed.emit()
	await _wait(1.4)
	return _report_dialog("hav_lens")


## The company may start an offshore unit (the lens is shown only then).
func _open_the_hav_family() -> bool:
	if not _sim.qa_open_farm_family(HAV_RESEARCH, "", true):
		return _fail("could not research %s" % HAV_RESEARCH)
	# The unit's first payment and the company's share, and a passive year
	# survived: a bank loan first, as a player would take one.
	for amount: float in [300_000_000.0, 200_000_000.0, 120_000_000.0]:
		var loan: Dictionary = _sim.take_loan(amount, 120, 0.0)
		print("[GeoVisCapture] loan %.0f MNOK: %s" % [amount / 1e6, str(loan.get("success", false))])
		if bool(loan.get("success", false)):
			break
	_sim.advance_days(ESCAPE_FREE_DAYS)
	_sim.set_paused(true)
	await _wait(0.5)
	_dismiss_cards()
	var starts: Dictionary = _sim.get_farm_type_starts()
	var open := starts.keys().filter(func(k: Variant) -> bool: return bool((starts[k] as Dictionary).get("startable", false)))
	print("[GeoVisCapture] day %d: startable %s" % [int(_sim.current_day), str(open)])
	return open.has("of1")


## The Hav lens from the map at every size: forste_sesong (home PO3, hav_1 on
## Indrebakken) a year on, with the offshore unit open.
func _hav_lens_frames() -> void:
	await _start("forste_sesong", 7)
	if not await _open_the_hav_family():
		_fail("no offshore unit can be started a year into forste_sesong")
	for spec: Array in SIZES:
		var size: Vector2i = spec[0]
		var scale: float = spec[1]
		var id := "geovis_map_hav_lens_%dx%d_%d" % [size.x, size.y, int(round(scale * 100.0))]
		if not _wants(id):
			continue
		get_window().size = size
		get_window().content_scale_factor = scale
		await _wait(0.7)
		if await _open_hav_from_the_map(3):
			await _capture(id)
		await _unstage_map()
	get_window().size = Vector2i(1920, 1080)
	get_window().content_scale_factor = 1.0


func _unstage_map() -> void:
	var dialog: BuildSiteDialog = _map.build_dialog()
	if dialog != null and dialog.visible:
		dialog.close()
	var drill: GeoDrillStack = _map.geo_drill()
	if drill != null and drill.level() != "national":
		drill.go_national()
	await _wait(0.3)


func _show_map() -> void:
	if _ui.get_active_tab() != "map":
		_ui.show_tab("map")
		await _wait(0.6)
	_map = _ui.find_child("Panel_map", true, false)


func _report_national() -> void:
	var chart: GeoMapView = _map.chart()
	print("[GeoVisCapture] national: chart %s drawn %s compact %s, %d plates, %d clusters" % [
		str(chart.size), str(chart.drawn_rect()), str(chart.compact), chart.plates().size(),
		chart.site_layer().clusters().size()])


## Print where the dialog and its confirm stand in the window; false when the
## dialog is not open.
func _report_dialog(what: String) -> bool:
	var dialog: BuildSiteDialog = _map.build_dialog()
	if dialog == null or not dialog.visible:
		return _fail("%s: the build dialog is not open" % what)
	var view := get_viewport().get_visible_rect()
	var panel := dialog.find_child("Dialog", true, false) as Control
	var confirm := dialog.find_child("ConfirmBtn", true, false) as Control
	var panel_rect := panel.get_global_rect() if panel != null else Rect2()
	var confirm_rect := confirm.get_global_rect() if confirm != null else Rect2()
	print("[GeoVisCapture] %s: family %s plot '%s' candidate '%s'; panel %s in %s; confirm %s inside %s" % [
		what, dialog.family(), dialog.plot_id(), dialog.selected_candidate(), str(panel_rect), str(view.size),
		str(confirm_rect), str(view.encloses(confirm_rect))])
	if what == "hav_lens":
		var lens := dialog.plot_lens()
		var b := lens.geo_binding("hav_1") if lens != null else {}
		var scroll := dialog.find_child("StepScroll", true, false) as ScrollContainer
		var seen := scroll.get_global_rect().intersection(lens.get_global_rect()) if scroll != null and lens != null else Rect2()
		print("[GeoVisCapture]   hav_1 binding: %s" % str(b))
		print("[GeoVisCapture]   lens shown %s, %.0f of %.0f px tall in the steps' view (scroll %d)" % [
			str(lens != null and lens.is_visible_in_tree()), seen.size.y, lens.size.y if lens != null else 0.0,
			scroll.scroll_vertical if scroll != null else -1])
	return true


# ── The 3D fjord: rival farms in GetCompetitorCompanies' order ──────────────

func _rival_frames() -> void:
	for sc: Array in RIVAL_SCENARIOS:
		var scenario: String = sc[0]
		if not _wants("geovis_rivals3d_" + scenario):
			continue
		await _start(scenario, int(sc[1]))
		_ui.show_tab("fjord")
		_sim.state_updated.emit()
		await _wait(1.5)
		var env: Node = _scene.get_node("FjordEnvironment")
		var rivals: CompetitorFarmVisualManager = env.get_node("CompetitorFarmVisualManager")
		var farms: Array = _sim.get_competitor_farms()
		var pens := env.get_node("FishPenManager") as FishPenManager
		var slots := pens.get_recommended_locations()
		var order: PackedStringArray = []
		for i in farms.size():
			var f: Dictionary = farms[i]
			var slot := CompetitorFarmVisualManager.farm_slot(i, pens.slots_used(), slots.size())
			order.append("%d %s '%s' home_po_sites %d slot %d%s" % [i, str(f.get("id", "")), str(f.get("name", "")),
				int(f.get("home_po_sites", -1)), slot, " drawn" if rivals.farm_ids().has(str(f.get("id", ""))) else ""])
		print("[GeoVisCapture] %s rivals (home area %d): %s" % [scenario, int(_map.home_area()), " | ".join(order)])
		var first: Node3D = env.get_node_or_null("CompetitorFarm_%s" % str(farms[0].get("id", ""))) if not farms.is_empty() else null
		if first == null:
			_fail("%s: the first rival has no farm in the fjord" % scenario)
			continue
		var cam: FjordCamera = env.get("camera")
		for size: Vector2i in [Vector2i(1280, 720), Vector2i(1920, 1080)]:
			var id := "geovis_rivals3d_%s_%dx%d" % [scenario, size.x, size.y]
			if not _wants(id):
				continue
			get_window().size = size
			get_window().content_scale_factor = 1.0
			cam.hold_view(first.global_position, cam.home_yaw(first.global_position), RIVAL_VIEW_M, 0.0, true)
			await _wait(1.6)
			await _capture(id)
		cam.recentre()


# ── hav_1 at the far clamp (home PO9): the lens and the ghost ───────────────

func _far_clamp_frames() -> void:
	# A home area whose hav_1 lies at the far clamp (PO4, PO9): the first game
	# a year into which an offshore unit may start (a storm escape closes it).
	var home := 0
	for sc: Array in FAR_CLAMP_SCENARIOS:
		await _start(str(sc[0]), int(sc[1]))
		if await _open_the_hav_family():
			home = int(sc[2])
			print("[GeoVisCapture] far clamp in %s" % str(sc[0]))
			break
	if home == 0:
		_fail("no far-clamp game opens an offshore unit within a year")
		return
	for size: Vector2i in [Vector2i(1280, 720), Vector2i(1920, 1080)]:
		get_window().size = size
		get_window().content_scale_factor = 1.0
		await _wait(0.6)
		if not await _open_hav_from_the_map(home):
			continue
		var dialog: BuildSiteDialog = _map.build_dialog()
		var lens_id := "geovis_hav_far_lens_%dx%d" % [size.x, size.y]
		if _wants(lens_id):
			await _capture(lens_id)
		var ghost_id := "geovis_hav_far_ghost_%dx%d" % [size.x, size.y]
		if _wants(ghost_id):
			await _ghost_frame(ghost_id, dialog)
		await _unstage_map()


## The ghost standing on hav_1: the UI layer hidden, the camera held still
## over the plot (the rig would clamp a focus 9 km out), as QaWorldCapture
## frames an offshore focus.
func _ghost_frame(id: String, dialog: BuildSiteDialog) -> void:
	var plot := dialog.plot_lens().plot_by_id("hav_1")
	var env: Node = _scene.get_node("FjordEnvironment")
	var cam: FjordCamera = env.get("camera")
	var ghost := get_tree().get_first_node_in_group(FarmGhost.GROUP) as FarmGhost
	print("[GeoVisCapture] ghost on %s at x %.0f z %.0f depth %.0f: %s children" % [
		str(plot.get("id", "")), float(plot.get("x", 0.0)), float(plot.get("z", 0.0)),
		float(plot.get("water_depth_m", 0.0)), str(ghost.get_child_count()) if ghost != null else "no ghost"])
	var layer := _scene.get_node("UILayer") as CanvasLayer
	layer.visible = false
	var focus := Vector3(float(plot.get("x", 0.0)), 6.0, float(plot.get("z", 0.0)))
	var d := 420.0
	var pitch := deg_to_rad(-16.0)
	var yaw: float = cam.home_yaw(focus)
	cam.set_process(false)
	cam.set("_distance", d)
	cam.set("_distance_target", d)
	var horiz := Vector3(sin(yaw), 0.0, cos(yaw))
	cam.global_position = focus + horiz * d * cos(pitch) + Vector3(0.0, -d * sin(pitch), 0.0)
	cam.look_at(focus, Vector3.UP)
	cam.fov = FjordCamera.view_for_distance(d).y
	await _wait(1.2)
	await _capture(id)
	cam.set_process(true)
	layer.visible = true
	cam.recentre()


# ── Shared ──────────────────────────────────────────────────────────────────

func _start(scenario: String, seed: int) -> void:
	if _scene != null:
		_scene.queue_free()
		await get_tree().process_frame
	var toasts := get_node_or_null("/root/ToastNotification")
	if toasts != null and toasts.has_method("clear_all"):
		toasts.clear_all()
	_sim.start_qa_scenario(scenario, seed)
	_sim.skip_tutorial()
	_scene = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(_scene)
	for i in 8:
		await get_tree().process_frame
	_sim.set_paused(true)
	_dismiss_cards()
	_ui = _scene.get_node("UILayer/GameUI")
	await _show_map()


func _dismiss_cards() -> void:
	var card: Node = _scene.get_node_or_null("%CriticalEventCard")
	for i in 20:
		if card == null or not card.is_open():
			break
		card.continue_button.pressed.emit()


func _fail(why: String) -> bool:
	push_warning("[GeoVisCapture] " + why)
	_failures += 1
	return false


func _capture(id: String) -> void:
	DisplayServer.window_move_to_foreground()
	var done := [false]
	RenderingServer.frame_post_draw.connect(func() -> void: done[0] = true, CONNECT_ONE_SHOT)
	var waited := 0.0
	while not done[0] and waited < 3.0:
		await get_tree().create_timer(0.1).timeout
		waited += 0.1
	var img := get_viewport().get_texture().get_image()
	var path := ProjectSettings.globalize_path(OUT_DIR + id + ".png")
	if img == null or img.save_png(path) != OK:
		_fail("could not save %s" % id)
		return
	_frames += 1
	print("[GeoVisCapture] captured %s -> %s" % [id, path])


func _wait(seconds: float) -> void:
	await get_tree().create_timer(seconds).timeout
