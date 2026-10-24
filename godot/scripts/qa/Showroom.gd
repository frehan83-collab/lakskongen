extends "res://scripts/qa/QaWorldCapture.gd"
## Showroom: the real game world, playable, with every farm type and the new
## models standing in it, for the owner to look around (not a capture: it
## stays open until closed).
##
## It boots po3_red_sone with a fixed seed as the capture scenes do, then
## stands up, through the bridge's QA verbs (the engine's own build verbs;
## gates waived): a grow-out land campus on pad_a (4 lots) and a post-smolt
## site on pad_delta (F2-19/F2-20/F2-25), an OF1-class unit on off_1 and a
## Havfarm-class unit on off_2 (F2-26/F2-29), PO3 Sør upgraded to closed
## floating pens (F2-30; PO3 Nord starts semi-closed in the scenario), and on
## pad_b the slaughterhouse kit's Medium tier as a static display (not wired
## into the game yet: docs/proposals/player_placement.md).
##
## The game's own HUD, panels and camera stay live. The tour panel (top
## right) flies the camera to each piece; F1 hides it; F2 weather, F3 time
## of day, F4 season. 1-4 speed and Space pause are the game's.
##
## Run: tools/showroom.sh (windowed, an isolated user dir).
const SCENE_PATH := "res://qa/scenes/Showroom.tscn"
const SLAUGHTER_DIR := "res://assets/models/slaughterhouse_kit/"
const LAND_DIR := "res://assets/models/land_kit/"
const KIT_MATERIAL := preload("res://assets/models/facility_kit/LK_FacilityKit.tres")
const SEED := 20261024
const SCENARIO := "po3_red_sone"
const DISPLAY_PAD := "pad_b"
const CLOSED_SITE := "po3_sor"
const WEATHER_CYCLE := ["clear", "overcast", "rain", "storm", "fog", "snow"]
const HOURS := [7.0, 10.0, 13.0, 16.5, 19.5, 22.5]
const SEASONS := [0.0, 0.25, 0.5, 0.75]
const SEASON_NAMES := ["vinter", "vår", "sommer", "høst"]

var _manager: Node = null
var _terrain: Node = null
var _units := {}
var _stops: Array[Dictionary] = []
var _panel: PanelContainer = null
var _status: Label = null
var _weather_i := 0
var _hour_i := 2
var _season_i := 2
var _display: Node3D = null


func _ready() -> void:
	start()


func scene_path() -> String:
	return SCENE_PATH


## QaWorldCapture boots from shots.json; the showroom has no shots, so it
## hands boot() one synthetic shot (scenario, seed) and never quits.
func _start() -> void:
	if not await boot([{"scenario": SCENARIO, "seed": SEED}]):
		get_tree().quit(1)
		return
	await run_shots([])


func run_shots(_shots: Array) -> void:
	_terrain = _env.get_node_or_null("Terrain")
	_manager = _env.get_node_or_null("FarmFacilityVisualManager")
	await _stage_units()
	_place_slaughterhouse_display()
	_build_stops()
	_give_back_the_game()
	_build_panel()
	_apply_environment()
	_go_home()
	print("[Showroom] ready: %d stops %s" % [_stops.size(), _units])
	var shots_dir := _tour_shots_dir()
	if shots_dir != "":
		await _capture_tour(shots_dir)


## `-- --tour-shots=<abs dir>`: fly to every stop, save a frame of each, then
## stay open (a check that every stop shows its piece; the owner's run has none).
func _tour_shots_dir() -> String:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--tour-shots="):
			return arg.trim_prefix("--tour-shots=")
	return ""


func _capture_tour(dir: String) -> void:
	DirAccess.make_dir_recursive_absolute(dir)
	await wait(2.0)
	for i in _stops.size():
		_fly(_stops[i])
		await wait(3.0)
		var img := await grab()
		var path := dir.path_join("%02d_%s.png" % [i, str(_stops[i]["label"]).to_snake_case().validate_filename().left(40)])
		img.save_png(path)
		print("[Showroom] shot %s" % path)
	_fly(_stops[0])


# ── The units, through the bridge's QA verbs ──────────────────────────────────

func _stage_units() -> void:
	var gb := get_node_or_null("/root/GameBridge")
	var sim := get_node_or_null("/root/SimBridge")
	if gb == null or sim == null:
		push_error("[Showroom] GameBridge or SimBridge missing")
		return
	gb.QaGrantCash(500_000_000.0)
	var placed: Dictionary = gb.QaPlaceFarmTypeSet(4, 2, 2)
	if not bool(placed.get("success", false)):
		push_warning("[Showroom] farm-type set: %s" % placed.get("message", ""))
	_units["land"] = str(placed.get("land_id", ""))
	_units["post_smolt"] = str(placed.get("post_smolt_id", ""))
	var offshore: Array = placed.get("offshore_ids", [])
	_units["of1"] = str(offshore[0]) if offshore.size() > 0 else ""
	_units["havfarm"] = str(offshore[1]) if offshore.size() > 1 else ""
	if not sim.upgrade_containment(CLOSED_SITE, "ClosedFloat"):
		push_warning("[Showroom] %s did not take closed pens" % CLOSED_SITE)
	# One sim day commissions the offshore projects stood complete.
	sim.advance_days(1)
	if _manager != null:
		_manager.visible = true
		_manager.call("_on_state_updated")
	await wait(1.0)


# ── The slaughterhouse kit's Medium tier, a static display on pad_b ─────────

func _place_slaughterhouse_display() -> void:
	var pad := _pad(DISPLAY_PAD)
	if pad.is_empty():
		push_warning("[Showroom] no %s for the slaughterhouse display" % DISPLAY_PAD)
		return
	_terrain.call("set_pad_state", DISPLAY_PAD, 1.0)
	var c: Vector3 = pad["centre"]
	var q: Vector3 = pad.get("quay", c + Vector3.RIGHT)
	var to_quay := Vector3(q.x - c.x, 0.0, q.z - c.z).normalized()
	# The display's frame as LandCampusBuilder's: +X toward the quay, +Z along it.
	var phi := atan2(-to_quay.z, to_quay.x)
	_display = Node3D.new()
	_display.name = "SlaughterhouseDisplay"
	_env.add_child(_display)
	_display.global_transform = Transform3D(Basis(Vector3.UP, phi), Vector3(c.x, 3.6, c.z))
	var size: Vector2 = pad.get("size", Vector2(640.0, 420.0))
	var edge := (size.x if absf(q.x - c.x) > absf(q.z - c.z) else size.y) * 0.5 - 6.0
	# As blender/render_slaughterhouse_preview.py lays the Medium tier: the
	# models' fronts turned +90 deg to face the sea.
	var front := PI * 0.5
	var hall_half := 20.0
	var hall_u := edge - 18.0 - hall_half
	_piece(SLAUGHTER_DIR + "slaughter_hall_m_LOD0.glb", hall_u, 0.0, 0.0, front)
	_piece(SLAUGHTER_DIR + "reception_house_LOD0.glb", hall_u + hall_half + 6.5, 0.0, 0.0, front)
	for k in 2:
		_piece(SLAUGHTER_DIR + "chilled_loading_bay_LOD0.glb", hall_u - hall_half - 6.0, -7.0 + 14.0 * k, 0.0, front)
	_piece(LAND_DIR + "quay_LOD0.glb", edge + 12.5, 0.0, -0.6, front)
	_piece(SLAUGHTER_DIR + "wellboat_intake_LOD0.glb", edge + 16.0, 0.0, -0.6, front)


func _piece(path: String, u: float, v: float, y: float, yaw: float) -> void:
	if not ResourceLoader.exists(path):
		push_warning("[Showroom] missing %s" % path)
		return
	var node := (load(path) as PackedScene).instantiate() as Node3D
	node.transform = Transform3D(Basis(Vector3.UP, yaw), Vector3(u, y, v))
	for mi in node.find_children("*", "MeshInstance3D", true, false):
		(mi as MeshInstance3D).material_override = KIT_MATERIAL
	_display.add_child(node)


func _pad(pad_id: String) -> Dictionary:
	if _terrain == null:
		return {}
	for pad: Dictionary in _terrain.call("land_pad_slots"):
		if str(pad["id"]) == pad_id:
			return pad
	return {}


# ── The tour ─────────────────────────────────────────────────────────────────

func _build_stops() -> void:
	_stops.clear()
	_stops.append({"label": "Hjem: fjorden", "home": true})
	var pm := _game.find_child("FishPenManager", true, false)
	if pm != null:
		for pen_id: String in pm.get_pen_ids():
			var farm: Node = pm.call("get_site", pen_id)
			var kind := "åpen not"
			if farm != null and farm.has_method("get_containment"):
				kind = {"SemiClosed": "semi-lukket", "ClosedFloat": "lukket merd"}.get(str(farm.call("get_containment")), kind)
			var lbl: Label3D = pm.call("get_site_label", pen_id)
			_stops.append({"label": "Sjø: %s (%s)" % [lbl.text if lbl else pen_id, kind],
				"pos": pm.call("get_pen_position", pen_id), "distance": 260.0, "pitch": -32.0})
	if _units.get("land", "") != "":
		_stops.append({"label": "Land: matfisk-campus (4 lodd)", "target": _units["land"]})
	if _units.get("post_smolt", "") != "":
		_stops.append({"label": "Land: postsmolt-anlegg", "target": _units["post_smolt"]})
	if _display != null:
		_stops.append({"label": "Slakteri, Medium (kun modell)", "pos": _display.global_position + _display.global_basis.x * 140.0,
			"distance": 240.0, "pitch": -28.0})
	if _units.get("of1", "") != "":
		_stops.append({"label": "Hav: OF1-klasse", "target": _units["of1"]})
	if _units.get("havfarm", "") != "":
		_stops.append({"label": "Hav: Havfarm-klasse", "target": _units["havfarm"]})


func _fly(stop: Dictionary) -> void:
	_cam.set_process(true)
	if stop.get("home", false):
		_go_home()
	elif stop.has("target"):
		if not _cam.call("focus_target", str(stop["target"]), 1.4):
			push_warning("[Showroom] no camera target %s" % stop["target"])
	else:
		_cam.call("frame_position", stop["pos"], float(stop.get("distance", 300.0)), float(stop.get("pitch", -35.0)), 1.4)


func _go_home() -> void:
	_cam.set_process(true)
	_cam.call("set_top_down", false)
	_cam.call("set_pitch_offset", 0.0)
	_cam.set("_user_rotated", false)
	_cam.call("look_at_position", _cam.get("home_focus"), ZOOM_FJORD)


# ── Give the game back to the player ─────────────────────────────────────────

## boot() holds the world still for a capture; the showroom hands it back:
## the UI, the toasts, the camera's own controls. The sim clock stays paused
## (Space or 1-4 runs it).
func _give_back_the_game() -> void:
	var ui := _game.find_child("UILayer", true, false)
	if ui:
		ui.visible = true
	# The Fjord tab shows the 3D world (the dashboard covers it).
	var game_ui := _game.find_child("GameUI", true, false)
	if game_ui and game_ui.has_method("show_tab"):
		game_ui.call("show_tab", "fjord")
	var toasts := get_node_or_null("/root/ToastNotification") as CanvasLayer
	if toasts:
		toasts.visible = true
	_cam.set_process(true)
	_cam.set("auto_orbit_enabled", false)


# ── Panel and keys ───────────────────────────────────────────────────────────

func _build_panel() -> void:
	var layer := CanvasLayer.new()
	layer.layer = 90
	add_child(layer)
	_panel = PanelContainer.new()
	_panel.anchor_left = 1.0
	_panel.anchor_right = 1.0
	_panel.offset_left = -340.0
	_panel.offset_right = -16.0
	_panel.offset_top = 96.0
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.07, 0.09, 0.11, 0.86)
	style.set_corner_radius_all(8)
	style.set_content_margin_all(12)
	_panel.add_theme_stylebox_override("panel", style)
	layer.add_child(_panel)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 6)
	_panel.add_child(box)
	var title := Label.new()
	title.text = "Showroom: omvisning"
	title.add_theme_font_size_override("font_size", 18)
	box.add_child(title)
	for stop in _stops:
		var b := Button.new()
		b.text = str(stop["label"])
		b.alignment = HORIZONTAL_ALIGNMENT_LEFT
		b.focus_mode = Control.FOCUS_NONE
		b.pressed.connect(_fly.bind(stop))
		box.add_child(b)
	var keys := Label.new()
	keys.text = "F1 skjul · F2 vær · F3 klokke · F4 årstid\nSpace pause · 1-4 fart · mus: dra / rull"
	keys.add_theme_font_size_override("font_size", 12)
	keys.modulate = Color(1, 1, 1, 0.7)
	box.add_child(keys)
	_status = Label.new()
	_status.add_theme_font_size_override("font_size", 12)
	box.add_child(_status)


func _unhandled_key_input(event: InputEvent) -> void:
	var k := event as InputEventKey
	if k == null or not k.pressed or k.echo:
		return
	match k.keycode:
		KEY_F1:
			if _panel:
				_panel.visible = not _panel.visible
		KEY_F2:
			_weather_i = (_weather_i + 1) % WEATHER_CYCLE.size()
			_apply_environment()
		KEY_F3:
			_hour_i = (_hour_i + 1) % HOURS.size()
			_apply_environment()
		KEY_F4:
			_season_i = (_season_i + 1) % SEASONS.size()
			_apply_environment()
		_:
			return
	get_viewport().set_input_as_handled()


func _apply_environment() -> void:
	if _env == null:
		return
	var weather: String = WEATHER_CYCLE[_weather_i]
	_env.call("set_season", SEASONS[_season_i])
	_env.call("set_time", HOURS[_hour_i])
	_env.call("set_weather", int(WEATHER.get(weather, 0)))
	var line := "Vær: %s · kl. %05.2f · %s" % [weather, HOURS[_hour_i], SEASON_NAMES[_season_i]]
	if _status:
		_status.text = line
	print("[Showroom] %s" % line)
