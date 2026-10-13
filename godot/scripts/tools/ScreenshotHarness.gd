extends Node
## ScreenshotHarness — drives Lakskongen and captures PNGs for human visual QA.
##
## This harness is a normal scene (not a SceneTree / --script tool) so the
## project autoloads (SimBridge, GameBridge, TutorialState, …) are live and the
## renderer is active. It REQUIRES a real display/session: headless cannot render.
##
## Run it through tools/capture_screenshots.sh, or directly:
##   godot --path godot res://scenes/tools/ScreenshotHarness.tscn
##
## Output: every shot is written to user://screenshots/ AND copied into the
## project's screenshots/game/ directory. The harness quits when done.
##
## Every node/property lookup is guarded so a scene-shape change degrades to a
## skipped shot instead of a crash.

const MAIN_MENU_SCENE := "res://scenes/MainMenu.tscn"
const GAME_SCENE := "res://scenes/GameScene.tscn"
const SCENARIO_PANEL_SCRIPT := "res://scripts/ui/ScenarioSelectPanel.gd"
const GENETICS_PANEL_SCENE := "res://scenes/ui/GeneticsPanel.tscn"

const USER_SCREENSHOT_DIR := "user://screenshots"
const PROJECT_SCREENSHOT_DIR := "res://screenshots/game"

# WeatherController.WeatherState enum ordinals (kept local so this tool does not
# depend on global-class resolution order): CLEAR, OVERCAST, RAIN, SNOW, STORM, FOG.
const WEATHER_CLEAR := 0
const WEATHER_STORM := 4

# FjordCamera.ZoomLevel enum ordinals: COAST, FJORD, FARM, UNDERWATER.
const ZOOM_FJORD := 1
const ZOOM_FARM := 2
const ZOOM_UNDERWATER := 3

# Scenario id from ScenarioSelectPanel / ScenarioSystem (unlocked by default;
# campaign scenarios can be gated).
const QA_SCENARIO_ID := "po3_red_sone"
const QA_STARTING_CAPITAL := 10_000_000
const QA_DAYS_TO_SIMULATE := 12
# Fixed RNG seed for the QA pass: makes every harness run replay identically
# (lice trajectory, weather, market) so screenshot diffs are meaningful.
const QA_SEED := 20261012

# S1 living-fjord forced-state shots: set the QA lice load to the red-collar
# threshold (FishPenManager: >=0.2 amber, >=0.5 critical/red).
const LICE_FORCE_TARGET := 0.6
const LICE_STEP_DAYS := 10
const LICE_MAX_DAYS := 150

var _project_dir_abs: String = ""
var _captured: Array[String] = []
## Content checks that failed (the run exits non-zero when any did).
var _content_failures: PackedStringArray = []
## F957: the tilt-shift pass divided 0/0 underwater and blacked out most of the
## frame, and the baseline enshrined it. A fjord shot may not be more than this
## fraction pure black.
const MAX_BLACK_FRACTION := 0.30
var _game: Node = null
var _ui_layer: Node = null
var _env: Node = null
var _menu_instance: Node = null


func _ready() -> void:
	if DisplayServer.get_name() == "headless":
		push_error(
			"ScreenshotHarness: no display available — headless cannot render. " +
			"Run tools/capture_screenshots.sh from a desktop session instead."
		)
		get_tree().quit(1)
		return

	_project_dir_abs = ProjectSettings.globalize_path(PROJECT_SCREENSHOT_DIR)
	_prepare_dirs()
	print("[ScreenshotHarness] starting — output: %s" % _project_dir_abs)
	_run_sequence()


# ── Capture sequence ──────────────────────────────────────────────────────────

func _run_sequence() -> void:
	await _wait(0.4)

	await _capture_main_menu()
	await _capture_scenario_selection()
	await _boot_game()

	await _capture_dashboard()
	await _capture_ui_scale_pairs()
	await _capture_telemetry()
	await _capture_map_overlay()
	await _capture_finance()
	await _capture_genetics()
	await _capture_crew()
	await _capture_contracts()
	await _capture_offshore()
	await _capture_carbon_ledger()
	await _capture_pen_management()
	await _capture_attention_inbox()

	await _capture_fjord_day()
	await _capture_fjord_farm()
	await _capture_fjord_night_aurora()
	await _capture_fjord_storm()
	await _capture_fjord_lice_red_zone()
	await _capture_fjord_bloom()
	await _capture_fjord_underwater()
	await _capture_fjord_diagnostics()
	await _audit_a11y_targets()

	_print_summary()
	for failure in _content_failures:
		push_error("[ScreenshotHarness] content check failed: %s" % failure)
	get_tree().quit(0 if _content_failures.is_empty() else 1)


# ── S3 accessibility: ui_scale regression pair + interactive-target audit ─────

const A11Y_MIN_TARGET := 44.0
const CLICKABLE_CLASSES := ["Button", "CheckBox", "CheckButton", "OptionButton", "LinkButton", "TextureButton", "MenuButton"]

## Implements Phase 5.5 #4's accessibility floor checks on the live UI:
##  * dashboard at 80% / 125% UI scale (layout must not break),
##  * every tab's clickable controls measured against the 44x44 target rule;
##    violations are printed and written to screenshots/game/a11y_targets.json.
func _capture_ui_scale_pairs() -> void:
	var win := get_window()
	if win == null:
		return
	win.content_scale_factor = 0.8
	await _wait(0.8)
	await _shoot("03b_scale80_dashboard")
	win.content_scale_factor = 1.25
	await _wait(0.8)
	await _shoot("03c_scale125_dashboard")
	win.content_scale_factor = 1.0
	await _wait(0.5)


func _audit_a11y_targets() -> void:
	var tab_ids := ["dashboard", "telemetry", "map", "finance", "crew", "contracts", "offshore", "carbon", "pens"]
	var total := 0
	var violations: Array = []
	for tab_id in tab_ids:
		await _show_tab(tab_id)
		await _wait(0.4)
		var root: Node = _find(_game, "Panel_" + tab_id)
		if root == null:
			root = _game
		var controls: Array[Control] = []
		_collect_clickables(root, controls)
		for c in controls:
			total += 1
			var s := c.size
			if minf(s.x, s.y) < A11Y_MIN_TARGET and c.is_visible_in_tree():
				violations.append({
					"panel": tab_id, "class": c.get_class(), "name": str(c.name),
					"w": int(s.x), "h": int(s.y),
				})
	# HUD buttons (always present).
	if _ui_layer != null:
		var hud_controls: Array[Control] = []
		var hud := _find(_game, "HUD")
		if hud != null:
			_collect_clickables(hud, hud_controls)
		for c in hud_controls:
			total += 1
			if minf(c.size.x, c.size.y) < A11Y_MIN_TARGET and c.is_visible_in_tree():
				violations.append({
					"panel": "hud", "class": c.get_class(), "name": str(c.name),
					"w": int(c.size.x), "h": int(c.size.y),
				})
	# GameUI chrome (tab bar, footer buttons) — panels are children of GameUI,
	# so scanning it also re-covers the active panel; duplicates are harmless.
	var gameui := _find(_game, "GameUI")
	if gameui != null:
		var ui_controls: Array[Control] = []
		_collect_clickables(gameui, ui_controls)
		for c in ui_controls:
			total += 1
			if minf(c.size.x, c.size.y) < A11Y_MIN_TARGET and c.is_visible_in_tree():
				violations.append({
					"panel": "gameui", "class": c.get_class(), "name": str(c.name),
					"w": int(c.size.x), "h": int(c.size.y),
				})
	print("[ScreenshotHarness] a11y targets: %d interactive controls, %d under %.0fpx" % [total, violations.size(), A11Y_MIN_TARGET])
	for v in violations.slice(0, 20):
		print("[ScreenshotHarness] a11y small target: %s/%s %s %dx%d" % [v["panel"], v["name"], v["class"], v["w"], v["h"]])
	var f := FileAccess.open(PROJECT_SCREENSHOT_DIR.path_join("a11y_targets.json"), FileAccess.WRITE)
	if f != null:
		f.store_string(JSON.stringify({"min_target": A11Y_MIN_TARGET, "total": total, "violations": violations}, "\t"))
		f.close()


func _collect_clickables(node: Node, out: Array[Control]) -> void:
	for child in node.get_children():
		if child is Control:
			var c := child as Control
			if c is BaseButton or c.get_class() in CLICKABLE_CLASSES:
				if c.mouse_filter != Control.MOUSE_FILTER_IGNORE:
					out.append(c)
		_collect_clickables(child, out)


func _capture_main_menu() -> void:
	var menu := _instance_scene(MAIN_MENU_SCENE)
	if menu == null:
		push_warning("[ScreenshotHarness] main menu unavailable — skipped")
		return
	add_child(menu)
	await _wait(1.6)
	await _shoot("01_main_menu")
	_menu_instance = menu


func _capture_scenario_selection() -> void:
	var panel := _instance_script(SCENARIO_PANEL_SCRIPT)
	if panel == null:
		push_warning("[ScreenshotHarness] scenario panel unavailable — skipped")
		return
	add_child(panel)
	await _wait(1.2)
	await _shoot("02_scenario_selection")
	panel.queue_free()
	await _wait(0.3)
	if _menu_instance != null and is_instance_valid(_menu_instance):
		_menu_instance.queue_free()
		_menu_instance = null
	await _wait(0.5)


func _boot_game() -> void:
	_game = _instance_scene(GAME_SCENE)
	if _game == null:
		push_error("[ScreenshotHarness] GameScene unavailable — aborting capture")
		return
	add_child(_game)
	await _wait(2.0)

	var sim := get_node_or_null("/root/SimBridge")
	if sim != null:
		# Deterministic QA start (fixed seed); fall back to the unseeded path
		# when the QA bridge is unavailable (older builds).
		var started := false
		if sim.has_method("start_qa_scenario"):
			started = sim.start_qa_scenario(QA_SCENARIO_ID, QA_SEED)
		if not started:
			sim.start_new_game("Player", QA_STARTING_CAPITAL, QA_SCENARIO_ID)

	# Unlock advanced panels for this QA pass: skip the C# tutorial so TutorialState
	# reports complete (it re-derives phase/complete from SimBridge every frame,
	# so setting the properties directly does not stick).
	if sim != null and sim.has_method("skip_tutorial"):
		sim.skip_tutorial()

	# Advance a few simulated days so the dashboard/telemetry have history.
	if sim != null:
		sim.advance_days(QA_DAYS_TO_SIMULATE)
	# Ensure at least one stocked site so the 3D fjord shows pens + fish.
	_ensure_qa_site()
	# Frame the farm: point the fjord camera at the first pen so the QA
	# shots show cages + fish instead of empty water (zoom presets apply
	# per shot on top of this focus point).
	_focus_camera_on_first_pen()
	# Pinned-state QA (S3 control): freeze the sim clock after the boot state so
	# UI numbers / market / alerts are reproducible run-to-run. Sections that
	# need days move it explicitly (advance_days / QA forcing).
	if sim != null and sim.has_method("set_paused"):
		sim.set_paused(true)
	await _wait(2.0)


func _focus_camera_on_first_pen() -> void:
	var pm := _find(_game, "FishPenManager")
	if pm == null or not pm.has_method("get_pen_ids"):
		return
	# Pens arrive via SimBridge.state_updated ticks, a few frames after the
	# scenario starts — poll briefly instead of assuming they exist already.
	var ids: Array = []
	for i in range(50):
		ids = pm.call("get_pen_ids")
		if not ids.is_empty():
			break
		await _wait(0.2)
	if ids.is_empty():
		push_warning("[ScreenshotHarness] no pens spawned — camera stays default")
		return
	var pos: Vector3 = pm.call("get_pen_position", ids[0])
	# NOTE: _env is assigned after this runs in _boot_game — resolve locally.
	var env := _env if _env != null else _find(_game, "FjordEnvironment")
	if env != null and env.has_method("focus_camera_on"):
		env.call("focus_camera_on", pos, 1)  # FjordCamera.ZoomLevel.FJORD
		print("[diag] camera focused on pen %s at %s" % [str(ids[0]), str(pos)])
	else:
		push_warning("[ScreenshotHarness] could not focus camera (env=%s)" % str(env != null))

	_ui_layer = _find(_game, "UILayer")
	_env = _find(_game, "FjordEnvironment")


func _ensure_qa_site() -> void:
	var sim := get_node_or_null("/root/SimBridge")
	var gb := get_node_or_null("/root/GameBridge")
	if sim == null:
		return
	var pens: Array = sim.get_all_pens() if sim.has_method("get_all_pens") else []
	if not pens.is_empty():
		return
	var zone_id := ""
	if sim.has_method("get_zone_traffic_lights"):
		var zs: Array = sim.get_zone_traffic_lights()
		if not zs.is_empty():
			zone_id = str(zs[0].get("zone", ""))
	if zone_id == "" or gb == null or not gb.has_method("BuildSeaSite"):
		push_warning("[ScreenshotHarness] could not create a QA site (zone=%s, gb=%s)" % [zone_id, str(gb != null)])
		return
	var site_id := "QA-Site"
	gb.BuildSeaSite(zone_id, site_id)
	if sim.has_method("stock_smolts"):
		sim.stock_smolts(site_id, 50000, "standard", 0)
	sim.advance_days(3)
	print("[ScreenshotHarness] built + stocked QA site '%s' in zone '%s'" % [site_id, zone_id])


func _capture_dashboard() -> void:
	await _show_tab("dashboard")
	await _shoot("03_dashboard")


func _capture_telemetry() -> void:
	await _show_tab("telemetry")
	await _shoot("04_telemetry")


func _capture_map_overlay() -> void:
	await _show_tab("map")
	var legend := _find(_game, "SiteOverlayLegend")
	if legend != null and legend.has_method("set_metric"):
		legend.call("set_metric", "oxygen")
	await _wait(0.9)
	await _shoot("05_map_overlay")


func _capture_finance() -> void:
	await _show_tab("finance")
	await _shoot("06_finance")
	# S2: bottom-of-panel frame showing the time-series trend block (Trender).
	var panel := _find(_game, "Panel_finance")
	if panel != null:
		var scroll := panel.find_child("MainScroll", true, false) as ScrollContainer
		if scroll != null:
			scroll.scroll_vertical = 100000
			await _wait(0.8)
			await _shoot("06b_finance_trends")


func _capture_genetics() -> void:
	var panel := _instance_scene(GENETICS_PANEL_SCENE)
	if panel == null:
		push_warning("[ScreenshotHarness] GeneticsPanel unavailable — skipped")
		return
	# Above the game's UILayer (layer 2): as a plain child the panel drew under
	# the active tab, and the shot (and its baseline) showed the Finance tab.
	var layer := CanvasLayer.new()
	layer.layer = 10
	add_child(layer)
	layer.add_child(panel)
	await _wait(1.2)
	await _shoot("07_genetics")
	layer.queue_free()
	await _wait(0.4)


func _capture_crew() -> void:
	await _show_tab("crew")
	await _shoot("07b_crew")


func _capture_contracts() -> void:
	await _show_tab("contracts")
	await _shoot("07c_contracts")


func _capture_offshore() -> void:
	await _show_tab("offshore")
	await _shoot("07d_offshore")


func _capture_carbon_ledger() -> void:
	await _show_tab("carbon")
	await _shoot("07e_carbon_ledger")


func _capture_pen_management() -> void:
	await _show_tab("pens")
	await _shoot("07f_pen_management")


## Opens the Attention Inbox overlay (Phase 4.5 wiring QA).
func _capture_attention_inbox() -> void:
	var inbox := _find(_game, "AttentionInbox")
	if inbox == null:
		push_warning("[ScreenshotHarness] AttentionInbox unavailable — skipped")
		return
	if inbox.has_method("open_inbox"):
		inbox.call("open_inbox")
	else:
		inbox.visible = true
	await _wait(1.0)
	await _shoot("07g_attention_inbox")
	inbox.visible = false
	await _wait(0.3)


# ── Fjord environment shots ───────────────────────────────────────────────────

func _capture_fjord_day() -> void:
	_hide_ui()
	await _wait(0.4)
	# Live frame FIRST: the sim->3D sync is still enabled here, so this shot
	# proves the live weather/wave path in a real frame (whatever regime the
	# sim is in). Afterwards QA takes over staging.
	await _wait(1.5)
	await _shoot("08a_fjord_live_sync", true)
	var vista := _find(_game, "FjordVistaBridge")
	if vista != null:
		vista.set("sim_weather_enabled", false)
	_configure_env(0.5, 12.0, WEATHER_CLEAR, 0.0, ZOOM_FJORD)
	await _wait(1.5)
	await _shoot("08_fjord_day", true)


## Asset-QA closeup: FARM zoom on the focused pen. Every Blender asset
## (fish, pens, boats, barges) gets its proof frame here.
func _capture_fjord_farm() -> void:
	_hide_ui()
	await _wait(0.4)
	_configure_env(0.5, 12.0, WEATHER_CLEAR, 0.0, ZOOM_FARM)
	await _wait(2.5)
	await _shoot("08b_fjord_farm", true)


func _capture_fjord_night_aurora() -> void:
	_configure_env(0.0, 23.0, WEATHER_CLEAR, 1.0, ZOOM_FJORD)
	await _wait(2.0)
	await _shoot("09_fjord_night_aurora", true)


func _capture_fjord_storm() -> void:
	_configure_env(0.5, 12.0, WEATHER_STORM, 0.0, ZOOM_FJORD)
	await _wait(2.5)
	await _shoot("10_fjord_storm", true)


func _capture_fjord_underwater() -> void:
	_configure_env(0.5, 12.0, WEATHER_CLEAR, 0.0, ZOOM_UNDERWATER)
	var cam = _env.get("camera") if _env != null else null
	if cam != null:
		cam.set("auto_orbit_enabled", false)
		cam.set("global_position", Vector3(0.0, -60.0, 0.0))
	await _wait(2.6)
	_check_not_black("11_fjord_underwater", await _shoot("11_fjord_underwater", true))


# ── S1 forced-state shots (living fjord readout) ──────────────────────────────

## Max lice_per_fish across pens — the same value FishPenManager reads for the
## collar tint (tints at >=0.2, stronger at >=0.5).
func _max_lice_per_fish() -> float:
	var sim := get_node_or_null("/root/SimBridge")
	if sim == null or not sim.has_method("get_all_pens"):
		return 0.0
	var best := 0.0
	for pen in sim.get_all_pens():
		best = maxf(best, float(pen.get("lice_per_fish", 0.0)))
	return best


## First player site id (the harness stages state on the first pen site).
func _first_pen_site_id() -> String:
	var sim := get_node_or_null("/root/SimBridge")
	if sim == null or not sim.has_method("get_all_pens"):
		return ""
	var pens: Array = sim.get_all_pens()
	return str(pens[0].get("id", "")) if not pens.is_empty() else ""


## Red-zone lice state: pause the sim, set the QA lice load, capture, resume.
## Paused = no decay between set and capture (the live sim auto-treats >0.15).
func _capture_fjord_lice_red_zone() -> void:
	_hide_ui()
	var sim := get_node_or_null("/root/SimBridge")
	if sim == null:
		push_warning("[ScreenshotHarness] lice shot: SimBridge unavailable — skipped")
		return
	var gb := get_node_or_null("/root/GameBridge")
	var site_id := _first_pen_site_id()
	if site_id == "":
		push_warning("[ScreenshotHarness] lice shot: no site — skipped")
		return
	# Live-sync ON: SetQaPenLice emits state_updated → FjordVistaBridge repaints pens.
	var vista := _find(_game, "FjordVistaBridge")
	if vista != null:
		vista.set("sim_weather_enabled", true)
	if sim.has_method("set_paused"):
		sim.set_paused(true)
	var lice := _max_lice_per_fish()
	if gb != null and gb.has_method("SetQaPenLice"):
		var res: Dictionary = gb.SetQaPenLice(site_id, LICE_FORCE_TARGET)
		await _wait(1.2)
		lice = _max_lice_per_fish()
		print("[ScreenshotHarness] lice force: %.3f via QA set (site %s, %s)" % [lice, site_id, str(res.get("message", ""))])
	else:
		var days := 0
		while lice < LICE_FORCE_TARGET and days < LICE_MAX_DAYS:
			sim.advance_days(LICE_STEP_DAYS)
			days += LICE_STEP_DAYS
			lice = _max_lice_per_fish()
		print("[ScreenshotHarness] lice force: max %.3f after +%d days (target %.2f)" % [lice, days, LICE_FORCE_TARGET])
	# Stage weather/time/zoom via the proven path — it also re-frames the camera
	# after the underwater shot (zoom-only calls left it parked).
	_configure_env(0.5, 12.0, WEATHER_CLEAR, 0.0, ZOOM_FARM)
	await _wait(1.0)
	_hide_ui()
	await _shoot("12_fjord_lice_red_zone", true)


## Bloom pair (cleared vs injected), sim pinned so the tint holds between shots.
func _capture_fjord_bloom() -> void:
	_hide_ui()
	var sim := get_node_or_null("/root/SimBridge")
	var gb := get_node_or_null("/root/GameBridge")
	var site_id := _first_pen_site_id()
	if gb == null or site_id == "" or not gb.has_method("SpawnQaBloom"):
		push_warning("[ScreenshotHarness] bloom shot: QA injection unavailable (site=%s) — skipped" % site_id)
		return
	var vista := _find(_game, "FjordVistaBridge")
	if vista != null:
		vista.set("sim_weather_enabled", true)
	if sim != null and sim.has_method("set_paused"):
		sim.set_paused(true)
	var sev := float(gb.GetMaxBloomSeverity()) if gb.has_method("GetMaxBloomSeverity") else 0.0
	if gb.has_method("ClearQaBlooms"):
		var cleared: Dictionary = gb.ClearQaBlooms()
		await _wait(1.5)
		sev = float(gb.GetMaxBloomSeverity()) if gb.has_method("GetMaxBloomSeverity") else 0.0
		print("[ScreenshotHarness] bloom clear: removed %s -> severity %.2f" % [str(cleared.get("removed", "?")), sev])
	_configure_env(0.5, 12.0, WEATHER_CLEAR, 0.0, ZOOM_FJORD)
	await _wait(0.8)
	_hide_ui()
	await _shoot("13_fjord_no_bloom", true)
	var res: Dictionary = gb.SpawnQaBloom(site_id, 0.8, 21)
	await _wait(1.5)
	var after := float(gb.GetMaxBloomSeverity()) if gb.has_method("GetMaxBloomSeverity") else 0.0
	print("[ScreenshotHarness] bloom force: %.2f -> %.2f (site %s, %s)" % [sev, after, site_id, str(res.get("message", ""))])
	await _wait(0.8)
	_hide_ui()
	await _shoot("13b_fjord_bloom", true)


func _configure_env(season: float, hour: float, weather: int, aurora: float, zoom: int) -> void:
	if _env == null or not is_instance_valid(_env):
		return
	if _env.has_method("set_season"):
		_env.call("set_season", season)
	if _env.has_method("set_time"):
		_env.call("set_time", hour)
	_set_weather(weather)
	_set_aurora(aurora)
	_set_camera_zoom(zoom)


func _set_weather(state: int) -> void:
	if _env == null:
		return
	var ws = _env.get("weather_system")
	if ws != null and is_instance_valid(ws):
		# Skip the default 10s blend so the QA shot is fully transitioned.
		ws.set("transition_duration", 0.15)
	if _env.has_method("set_weather"):
		_env.call("set_weather", state)
	await get_tree().process_frame
	var ws2 = _env.get("weather_system")
	if ws2 != null and "current_weather" in ws2:
		print("[diag] weather set to %d -> now %s" % [state, str(ws2.get("current_weather"))])


func _set_aurora(intensity: float) -> void:
	if _env == null:
		return
	var ts = _env.get("time_system")
	if ts == null:
		return
	var sky = ts.get("sky_material")
	if sky is ShaderMaterial:
		(sky as ShaderMaterial).set_shader_parameter("aurora_intensity", intensity)


func _set_camera_zoom(level: int) -> void:
	if _env == null:
		return
	var cam = _env.get("camera")
	if cam != null and is_instance_valid(cam) and cam.has_method("set_zoom_level"):
		# Deterministic framing: the vista auto-orbit would rotate a
		# different amount before every shot.
		cam.set("auto_orbit_enabled", false)
		cam.call("set_zoom_level", level)


## Isolate the "black band": capture the fjord with the water hidden, then with
## the terrain hidden, and log the camera + mesh state. One run tells us what
## the black region actually is.
func _capture_fjord_diagnostics() -> void:
	if _env == null or not is_instance_valid(_env):
		return
	_configure_env(0.5, 12.0, WEATHER_CLEAR, 0.0, ZOOM_FJORD)
	await _wait(1.2)
	_log_3d_state()
	# Hide each full-screen overlay / the farm, one at a time, so we can see
	# which element produces the black band.
	var targets := [
		["TiltShiftCanvas", "14_diag_no_tilt"],
		["UnderwaterCanvas", "15_diag_no_underwater"],
		["FishPenManager", "16_diag_no_farm"],
	]
	for t in targets:
		var node := _find(_env, t[0])
		if node == null:
			node = _find(self, t[0])
		if node is CanvasLayer or node is CanvasItem or node is Node3D:
			var was_visible: bool = node.visible
			node.visible = false
			await _wait(0.6)
			await _shoot(t[1], true)
			node.visible = was_visible
			await _wait(0.3)


func _log_3d_state() -> void:
	if _env == null:
		return
	var cam = _env.get("camera")
	if cam is Camera3D:
		var c := cam as Camera3D
		print("[diag] camera pos=%s rot=%s fov=%.1f current=%s" % [
			c.global_position, c.global_rotation, c.fov, c.current])
	# Enumerate every 3D mesh under the environment with its world AABB size.
	for n in _env.find_children("*", "MeshInstance3D", true, false):
		var m := n as MeshInstance3D
		var s := Vector3.ZERO
		if m.mesh != null:
			s = m.mesh.get_aabb().size
		print("[diag] Mesh %-24s visible=%s size=%s pos=%s" % [
			m.name, m.visible, s, m.global_position])
	for n in _env.find_children("*", "MultiMeshInstance3D", true, false):
		var mm := n as MultiMeshInstance3D
		var inst := mm.multimesh.instance_count if mm.multimesh != null else 0
		print("[diag] MultiMesh %-20s visible=%s instances=%d pos=%s" % [
			mm.name, mm.visible, inst, mm.global_position])


func _hide_ui() -> void:
	if _ui_layer != null and is_instance_valid(_ui_layer):
		_ui_layer.visible = false


# ── Helpers ───────────────────────────────────────────────────────────────────

func _show_tab(tab_id: String) -> void:
	var ui := _find(_game, "GameUI")
	if ui != null and ui.has_method("show_tab"):
		ui.call("show_tab", tab_id)
	else:
		push_warning("[ScreenshotHarness] GameUI/show_tab unavailable for '%s'" % tab_id)
	# Staging: a critical alert auto-opens the notification history, which
	# then covers the right of every later tab shot; close it (as its X does).
	var center := _find(_game, "NotificationCenter")
	if center != null and center.has_method("hide_panel"):
		center.call("hide_panel")
	await _wait(1.0)


func _shoot(shot_name: String, force_hide_ui: bool = false) -> Image:
	# Raise the window and wait (bounded) for a fully drawn frame before
	# grabbing the viewport texture. macOS can throttle rendering for occluded
	# windows — without the bound, frame_post_draw may never fire and the
	# harness hangs (seen in session 1).
	DisplayServer.window_move_to_foreground()
	# Fjord shots only (force_hide_ui): enforce UI-hidden right before the grab.
	# Some path re-showed the UI between the per-shot _hide_ui() and the capture
	# (auto-pause alerts). UI shots (panels/tabs) pass false — hiding there
	# silently produced fjord frames instead of panels (session 1 regression).
	var ui := _find(_game, "UILayer")
	if force_hide_ui and ui != null:
		ui.visible = false
		print("[ScreenshotHarness] _shoot(%s) UILayer.visible=%s after force-hide" % [shot_name, str(ui.visible)])
	var frame_done := [false]
	RenderingServer.frame_post_draw.connect(func(): frame_done[0] = true, CONNECT_ONE_SHOT)
	var waited := 0.0
	while not frame_done[0] and waited < 3.0:
		await get_tree().create_timer(0.1).timeout
		waited += 0.1
	if not frame_done[0]:
		push_warning("[ScreenshotHarness] frame wait timed out for '%s' — capturing last drawn frame" % shot_name)

	var vp := get_viewport()
	if vp == null:
		push_error("[ScreenshotHarness] no viewport for '%s'" % shot_name)
		return null
	var tex := vp.get_texture()
	if tex == null:
		push_error("[ScreenshotHarness] no viewport texture for '%s'" % shot_name)
		return null
	var img := tex.get_image()
	if img == null:
		push_error("[ScreenshotHarness] empty image for '%s'" % shot_name)
		return null

	var filename := shot_name + ".png"
	var user_path := USER_SCREENSHOT_DIR.path_join(filename)
	var err := img.save_png(user_path)
	if err != OK:
		push_error("[ScreenshotHarness] failed user:// save for '%s' (err %d)" % [shot_name, err])
		return null

	var project_path := _project_dir_abs.path_join(filename)
	var copy_err := img.save_png(project_path)
	if copy_err != OK:
		push_warning("[ScreenshotHarness] failed project save for '%s' (err %d)" % [shot_name, copy_err])

	_captured.append(project_path)
	print("[ScreenshotHarness] captured %s -> %s" % [filename, project_path])
	return img


## Fail the run when a captured frame is mostly pure black (sampled every 8 px).
func _check_not_black(shot_name: String, img: Image) -> void:
	if img == null:
		_content_failures.append("%s was not captured" % shot_name)
		return
	var black := 0
	var total := 0
	for y in range(0, img.get_height(), 8):
		for x in range(0, img.get_width(), 8):
			var c := img.get_pixel(x, y)
			total += 1
			if c.r < 0.02 and c.g < 0.02 and c.b < 0.02:
				black += 1
	var fraction := float(black) / float(maxi(total, 1))
	print("[ScreenshotHarness] %s: %.0f%% pure black" % [shot_name, fraction * 100.0])
	if fraction > MAX_BLACK_FRACTION:
		_content_failures.append("%s is %.0f%% pure black (max %.0f%%)" % [shot_name, fraction * 100.0, MAX_BLACK_FRACTION * 100.0])


func _prepare_dirs() -> void:
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(USER_SCREENSHOT_DIR))
	DirAccess.make_dir_recursive_absolute(_project_dir_abs)


func _instance_scene(path: String) -> Node:
	if not ResourceLoader.exists(path):
		push_error("[ScreenshotHarness] scene not found: %s" % path)
		return null
	var res := load(path)
	if res is PackedScene:
		return (res as PackedScene).instantiate()
	push_error("[ScreenshotHarness] not a PackedScene: %s" % path)
	return null


func _instance_script(path: String) -> Node:
	if not ResourceLoader.exists(path):
		push_error("[ScreenshotHarness] script not found: %s" % path)
		return null
	var res := load(path)
	if res is GDScript:
		var obj = (res as GDScript).new()
		if obj is Node:
			return obj as Node
	push_error("[ScreenshotHarness] script root is not a Node: %s" % path)
	return null


func _find(root: Node, node_name: String) -> Node:
	if root == null or not is_instance_valid(root):
		return null
	return root.find_child(node_name, true, false)


func _wait(seconds: float) -> void:
	await get_tree().create_timer(seconds).timeout


func _print_summary() -> void:
	print("")
	print("=== ScreenshotHarness complete: %d shot(s) ===" % _captured.size())
	for path in _captured:
		print("  " + path)
	print("user:// copy: " + ProjectSettings.globalize_path(USER_SCREENSHOT_DIR))
	print("")
