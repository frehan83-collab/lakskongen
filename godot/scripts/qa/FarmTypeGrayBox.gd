extends Node
## F2-18 gray-box read gate (farm-types spec §5.5 and §7.2).
##
## Boots the real game world (GameScene: terrain, water, sky, weather,
## camera rig), starts the QA scenario, puts primitive farm types at the
## spec §4 plot positions (GrayBoxBuilder) and captures the shots that
## godot/qa/shots.json lists for this scene (36_graybox_*): the shipped home
## view in six weathers, the land framing and the OF1 framing.
##
## Each shot is a beauty frame plus class-mask frames: every gray-box node
## drawn unshaded in a class colour, with the camera's own plain environment
## (no fog, no light, no tonemap, no DOF) and every other visual hidden but
## the terrain and the water, so the class pixels are exactly what the
## geometry covers on screen, occlusion included. The class colours are the
## seven pure colours (channels 0 or 255) over two passes: an anti-aliased
## edge between two classes then never matches a third. The same passes with
## the terrain hidden give the unoccluded cover, so the terrain's share is
## measured too. Per target it records the pixel size (mask and projection),
## the lid ellipse of one grow-out tank, distance, depression angle, the
## fog's distance transmittance, the silhouette contrast (mean |dL*| against
## the surroundings, CIE L*) and its retention against the clear frame; for
## shots with "perf", the draw calls (opaque, shadow) and frame time the gray
## box adds (in-run A/B, vsync off).
##
## Needs a display (headless renders nothing). Run:
##   tools/capture_screenshots.sh --scene res://qa/scenes/FarmTypeGrayBox.tscn
## Frames and 36_graybox_metrics.json go to res://screenshots/game/; pass
## `-- --only <id prefix>` to capture a subset.

const BUILDER := preload("res://scripts/qa/GrayBoxBuilder.gd")
const LAYOUT := preload("res://scripts/qa/LandCampusLayout.gd")
const GAME_SCENE := "res://scenes/GameScene.tscn"
const SCENE_PATH := "res://qa/scenes/FarmTypeGrayBox.tscn"
const SHOTS_PATH := "res://qa/shots.json"
const OUT_DIR := "res://screenshots/game"
const METRICS_FILE := "36_graybox_metrics.json"

## WeatherController.WeatherState ordinals.
const WEATHER := {"clear": 0, "overcast": 1, "rain": 2, "snow": 3, "storm": 4, "fog": 5}
const ZOOM_FJORD := 1
## Spec §5.5 rule 2: below this transmittance a unit needs its status chip.
const CHIP_TRANSMITTANCE := 0.4

## Class colours per mask pass (pure colours only; see the header).
const MASK_PASSES := [
	{
		"pad_a_tank_wall": Color(1, 0, 0), "pad_a_tank_lid": Color(0, 1, 1), "pad_a_campus": Color(0, 1, 0),
		"pad_b_tank_wall": Color(1, 0, 1), "pad_b_tank_lid": Color(1, 1, 0), "pad_b_campus": Color(0, 0, 1),
		"off_1_unit": Color(1, 1, 1),
	},
	{"off_2_unit": Color(1, 0, 0), "pad_delta_campus": Color(0, 1, 0)},
]
## A mask pixel belongs to a class within this L1 distance (0-765 scale).
const CLASS_TOLERANCE := 60
## Measured targets: the classes that make up each.
const TARGETS := {
	"pad_a": ["pad_a_tank_wall", "pad_a_tank_lid", "pad_a_campus"],
	"pad_b": ["pad_b_tank_wall", "pad_b_tank_lid", "pad_b_campus"],
	"pad_delta": ["pad_delta_campus"],
	"off_1": ["off_1_unit"],
	"off_2": ["off_2_unit"],
}
## Spec §5.5 rule 4 (LOD distances) and §5.1/§5.2 LOD0/1/2 triangle budgets
## (tanks include their rotor feeder).
const TANK_LOD_M := Vector2(350.0, 1200.0)
const UNIT_LOD_M := Vector2(500.0, 2000.0)
const TRI_BUDGET := {
	"tank_growout": [2620, 660, 96], "tank_pregrow": [1820, 460, 64],
	"process_hall_bay": [900, 160, 12], "harvest_hall": [3000, 450, 24], "pipe_bridge_span": [1800, 320, 24],
	"quay": [2500, 400, 30], "intake_basin": [2200, 380, 40], "river_intake": [600, 120, 12],
	"lox_tank": [640, 120, 12], "genset_container": [320, 60, 12], "substation": [1500, 220, 12],
	"feed_silo_land": [900, 160, 12], "sludge_station": [800, 150, 12],
	"of1_class": [60000, 12000, 1500], "havfarm_class": [60000, 12000, 1500],
}
const PERF_FRAMES := 90

@export var auto_run := true

var _game: Node = null
var _env: Node = null
var _cam: Camera3D = null
var _director: Node = null
var _box: Node3D = null
var _only := ""
var _results: Dictionary = {}
var _classes: Array[String] = []
var _srgb_lut := PackedFloat32Array()


func _ready() -> void:
	if not auto_run:
		return
	if DisplayServer.get_name() == "headless":
		push_error("FarmTypeGrayBox: no display; headless renders nothing. Run it windowed.")
		get_tree().quit(1)
		return
	var args := OS.get_cmdline_user_args()
	var i := args.find("--only")
	if i >= 0 and i + 1 < args.size():
		_only = args[i + 1]
	for k in 256:
		var c := float(k) / 255.0
		_srgb_lut.append(c / 12.92 if c <= 0.04045 else pow((c + 0.055) / 1.055, 2.4))
	for p in MASK_PASSES:
		for cls in p:
			_classes.append(cls)
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUT_DIR))
	_run.call_deferred()


## The shots in shots.json that belong to this scene.
static func load_shots() -> Array:
	var data = JSON.parse_string(FileAccess.get_file_as_string(SHOTS_PATH))
	if not (data is Dictionary) or not (data.get("shots") is Array):
		return []
	var out: Array = []
	for s in data["shots"]:
		if s is Dictionary and str(s.get("scene", "")) == SCENE_PATH:
			out.append(s)
	return out


func _run() -> void:
	if not await _boot():
		get_tree().quit(1)
		return
	for shot in load_shots():
		var sid := str(shot["id"])
		if _only != "" and not sid.begins_with(_only):
			continue
		print("[GrayBox] shot %s" % sid)
		_results[sid] = await _do_shot(shot)
	_finish_contrast()
	var size := get_viewport().get_visible_rect().size
	var out := {
		"item": "F2-18",
		"generated_by": "res://scripts/qa/FarmTypeGrayBox.gd",
		"resolution": [int(size.x), int(size.y)],
		"chip_transmittance": CHIP_TRANSMITTANCE,
		"shots": _results,
	}
	var path := ProjectSettings.globalize_path(OUT_DIR).path_join(METRICS_FILE)
	var f := FileAccess.open(path, FileAccess.WRITE)
	f.store_string(JSON.stringify(out, "  ", true) + "\n")
	f.close()
	print("[GrayBox] metrics: %s" % path)
	print("GRAYBOX_RESULT shots=%d" % _results.size())
	get_tree().quit(0)


# ── Boot ─────────────────────────────────────────────────────────────────────

func _boot() -> bool:
	_game = load(GAME_SCENE).instantiate()
	add_child(_game)
	await _wait(2.0)
	var sim := get_node_or_null("/root/SimBridge")
	if sim == null or not sim.has_method("start_qa_scenario"):
		push_error("[GrayBox] SimBridge unavailable")
		return false
	var shots := load_shots()
	var first: Dictionary = shots[0] if not shots.is_empty() else {}
	var scenario := str(first.get("scenario", "po3_red_sone"))
	if not sim.start_qa_scenario(scenario, int(first.get("seed", 20261012))):
		push_error("[GrayBox] scenario %s did not start" % scenario)
		return false
	if sim.has_method("skip_tutorial"):
		sim.skip_tutorial()
	_env = _game.find_child("FjordEnvironment", true, false)
	_cam = _env.get("camera") if _env else null
	_director = _env.get("director") if _env else null
	if _cam == null or _director == null:
		push_error("[GrayBox] camera or director missing")
		return false
	# Pens arrive with the state ticks a few frames after the start; the
	# vista then makes the first pen the home focus, as in a real game.
	var pm := _game.find_child("FishPenManager", true, false)
	for i in 50:
		if pm and not pm.get_pen_ids().is_empty():
			break
		await _wait(0.2)
	var vista := _game.find_child("FjordVistaBridge", true, false)
	if vista:
		vista.set("sim_weather_enabled", false)
		vista.call("_focus_first_pen")
	sim.set_paused(true)
	var ts = _env.get("time_system")
	if ts:
		ts.set("paused", true)
	var ws = _env.get("weather_system")
	if ws:
		ws.set("transition_duration", 0.15)
	_cam.set("auto_orbit_enabled", false)
	_hide_overlays()
	await _wait(1.0)
	return true


func _hide_overlays() -> void:
	var ui := _game.find_child("UILayer", true, false)
	if ui:
		ui.visible = false
	var toasts := get_node_or_null("/root/ToastNotification") as CanvasLayer
	if toasts:
		toasts.visible = false


# ── One shot ─────────────────────────────────────────────────────────────────

func _do_shot(shot: Dictionary) -> Dictionary:
	_hide_overlays()
	var raw: Dictionary = shot.get("plot_overrides", {})
	var overrides := {}
	for plot_id in raw:
		overrides[plot_id] = Vector2(float(raw[plot_id][0]), float(raw[plot_id][1]))
	LAYOUT.centre_overrides = overrides
	_env.call("set_season", float(shot.get("season", 0.5)))
	_env.call("set_time", float(shot.get("hour", 12.0)))
	_env.call("set_weather", int(WEATHER.get(str(shot.get("weather", "clear")), 0)))
	var cam_spec: Dictionary = shot.get("camera", {"mode": "home"})
	var focus := _stage_camera(cam_spec)
	await _wait(0.3)
	# Sample tanks: the grow-out tank nearest the camera (home) or the focus.
	var ref := focus if str(cam_spec.get("mode", "home")) == "focus" else _cam.global_position
	var samples := {}
	for pad in ["pad_a", "pad_b"]:
		samples[pad] = _nearest_index(BUILDER.growout_positions(pad), ref)
	_rebuild(samples)
	if not bool(shot.get("tank_shadows", true)):
		# What the spec's "LOD1 and beyond cast no shadow" leaves of a frame
		# whose tanks all stand in the LOD1 band.
		for gi in _box.find_children("*", "GeometryInstance3D", true, false):
			if str(gi.name) in ["Growout", "Pregrow", "SampleWall", "SampleLid"]:
				gi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	await _wait(2.5)
	var sid := str(shot["id"])
	var dir := ProjectSettings.globalize_path(OUT_DIR)
	var beauty := await _grab()
	beauty.save_png(dir.path_join(sid + ".png"))
	var masks: Array[Image] = []
	var bare: Array[Image] = []
	for p in MASK_PASSES.size():
		masks.append(await _mask_pass(p, true))
		bare.append(await _mask_pass(p, false))
	masks[0].save_png(dir.path_join(sid + "_mask.png"))
	var result := {
		"plot_overrides": raw,
		"weather": str(shot.get("weather", "clear")),
		"camera": _camera_record(cam_spec),
		"environment": _environment_record(),
		"targets": _measure(beauty, masks, bare, samples),
	}
	if bool(shot.get("perf", false)):
		result["perf"] = await _perf_ab()
		_rebuild(samples)
	LAYOUT.centre_overrides = {}
	return result


func _stage_camera(spec: Dictionary) -> Vector3:
	if str(spec.get("mode", "home")) == "home":
		# The shipped home view: FJORD zoom on the first pen, fjord-axis yaw.
		_cam.set_process(true)
		_cam.call("set_top_down", false)
		_cam.call("set_pitch_offset", 0.0)
		_cam.set("_user_rotated", false)
		_cam.call("look_at_position", _cam.get("home_focus"), ZOOM_FJORD)
		_cam.call("_snap")
		return _cam.get("home_focus")
	var focus := LAYOUT.plot_centre(str(spec.get("target", "pad_a")))
	if spec.has("focus_y"):
		focus.y = float(spec["focus_y"])
	var d := float(spec.get("distance_m", 520.0))
	var pitch := deg_to_rad(float(spec.get("pitch_deg", -40.0)))
	var yaw: float = deg_to_rad(float(spec["yaw_deg"])) if spec.has("yaw_deg") else float(_cam.call("home_yaw", focus))
	# The rig would clamp offshore focus points to the fjord (F2-21 extends
	# its clamps): hold it still and place it by hand. The director reads the
	# framing distance for shadows and depth of field.
	_cam.set_process(false)
	_cam.set("_distance", d)
	_cam.set("_distance_target", d)
	var horiz := Vector3(sin(yaw), 0.0, cos(yaw))
	_cam.global_position = focus + horiz * d * cos(pitch) + Vector3(0.0, -d * sin(pitch), 0.0)
	_cam.look_at(focus, Vector3.UP)
	_cam.fov = FjordCamera.view_for_distance(d).y
	return focus


func _rebuild(samples: Dictionary) -> void:
	if _box and is_instance_valid(_box):
		_box.get_parent().remove_child(_box)
		_box.free()
	_box = BUILDER.build(_env, samples)


# ── Capture ──────────────────────────────────────────────────────────────────

func _grab() -> Image:
	DisplayServer.window_move_to_foreground()
	for i in 3:
		await RenderingServer.frame_post_draw
	var img := get_viewport().get_texture().get_image()
	img.convert(Image.FORMAT_RGBA8)
	return img


## A class-mask frame for MASK_PASSES[pass]: those classes unshaded in their
## colours, every other gray-box node unshaded black (it still occludes),
## everything else unlit or hidden; with_terrain = false hides the terrain.
func _mask_pass(pass_index: int, with_terrain: bool) -> Image:
	var plain := Environment.new()
	plain.background_mode = Environment.BG_COLOR
	plain.background_color = Color.BLACK
	plain.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	plain.ambient_light_color = Color.BLACK
	plain.ambient_light_energy = 0.0
	plain.reflected_light_source = Environment.REFLECTION_SOURCE_DISABLED
	# A new Environment already tonemaps linearly with no fog or glow, and it
	# is only ever this camera's own: the world's look stays the director's.
	var saved_env: Environment = _cam.environment
	var saved_attrs: CameraAttributes = _cam.attributes
	var keep: Array[Node] = [_env.get_node_or_null("WaterGrid"), _box]
	if with_terrain:
		keep.append(_env.get_node_or_null("Terrain"))
	var hidden: Array[Node3D] = []
	for node in _game.find_children("*", "VisualInstance3D", true, false):
		if not node.visible:
			continue
		var kept := false
		for k in keep:
			if k != null and (node == k or k.is_ancestor_of(node)):
				kept = true
				break
		if not kept:
			node.visible = false
			hidden.append(node)
	_director.set_process(false)
	_cam.environment = plain
	_cam.attributes = CameraAttributesPractical.new()
	var colours: Dictionary = MASK_PASSES[pass_index]
	var black := _unshaded(Color.BLACK)
	for gi in _box.find_children("*", "GeometryInstance3D", true, false):
		var cls := str(gi.get_meta("graybox_class", ""))
		gi.material_override = _unshaded(colours[cls]) if colours.has(cls) else black
	var img := await _grab()
	for gi in _box.find_children("*", "GeometryInstance3D", true, false):
		gi.material_override = BUILDER.material()
	_cam.environment = saved_env
	_cam.attributes = saved_attrs
	for node in hidden:
		node.visible = true
	_director.set_process(true)
	return img


static func _unshaded(c: Color) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	m.albedo_color = c
	return m


# ── Measurement ──────────────────────────────────────────────────────────────

## Per-class pixel statistics from the mask passes: count, bbox, sum of L*.
class Stats:
	var count := PackedInt32Array()
	var box := PackedInt32Array()
	var lsum := PackedFloat64Array()
	var cmap := PackedByteArray()

	func _init(n: int, w: int, h: int) -> void:
		count.resize(n)
		lsum.resize(n)
		box.resize(n * 4)
		for k in n:
			box[k * 4] = w
			box[k * 4 + 1] = h
			box[k * 4 + 2] = -1
			box[k * 4 + 3] = -1
		cmap.resize(w * h)


func _classify(masks: Array[Image], beauty: Image) -> Stats:
	var w := masks[0].get_width()
	var h := masks[0].get_height()
	var st := Stats.new(_classes.size(), w, h)
	var bd := beauty.get_data() if beauty else PackedByteArray()
	for p in masks.size():
		var cols := PackedInt32Array()
		var ids := PackedInt32Array()
		for cls in MASK_PASSES[p]:
			var c: Color = MASK_PASSES[p][cls]
			cols.append_array([c.r8, c.g8, c.b8])
			ids.append(_classes.find(cls))
		var md := masks[p].get_data()
		var n := ids.size()
		for y in h:
			var row := y * w
			for x in w:
				var o := (row + x) * 4
				var r := md[o]
				var g := md[o + 1]
				var b := md[o + 2]
				if maxi(r, maxi(g, b)) < 200:
					continue
				for k in n:
					if absi(r - cols[k * 3]) + absi(g - cols[k * 3 + 1]) + absi(b - cols[k * 3 + 2]) < CLASS_TOLERANCE:
						var id := ids[k]
						st.cmap[row + x] = id + 1
						st.count[id] += 1
						st.box[id * 4] = mini(st.box[id * 4], x)
						st.box[id * 4 + 1] = mini(st.box[id * 4 + 1], y)
						st.box[id * 4 + 2] = maxi(st.box[id * 4 + 2], x)
						st.box[id * 4 + 3] = maxi(st.box[id * 4 + 3], y)
						if not bd.is_empty():
							st.lsum[id] += _lstar(bd, o)
						break
	return st


func _measure(beauty: Image, masks: Array[Image], bare: Array[Image], samples: Dictionary) -> Dictionary:
	var w := beauty.get_width()
	var h := beauty.get_height()
	var seen := _classify(masks, beauty)
	var all := _classify(bare, null)
	var bd := beauty.get_data()
	var out := {}
	for target in TARGETS:
		var ids: Array[int] = []
		for cls in TARGETS[target]:
			ids.append(_classes.find(cls))
		var t := {"mask": _union(ids, seen, bd, w, h)}
		var unoccluded := 0
		for k in ids:
			unoccluded += all.count[k]
		t["mask"]["pixels_without_terrain"] = unoccluded
		t["mask"]["visible_fraction"] = snappedf(float(t["mask"]["pixels"]) / unoccluded, 0.001) if unoccluded > 0 else 0.0
		for cls in TARGETS[target]:
			if cls.ends_with("_tank_wall") or cls.ends_with("_tank_lid"):
				t["mask_" + cls.trim_prefix(target + "_")] = _union([_classes.find(cls)], seen, bd, w, h)
		t.merge(_geometry(target, samples), true)
		out[target] = t
	return out


## Pixel count, bbox and silhouette contrast of a set of classes.
func _union(ids: Array, st: Stats, bd: PackedByteArray, w: int, h: int) -> Dictionary:
	var px := 0
	var x0 := w
	var y0 := h
	var x1 := -1
	var y1 := -1
	var ls := 0.0
	for k in ids:
		if st.count[k] == 0:
			continue
		px += st.count[k]
		x0 = mini(x0, st.box[k * 4])
		y0 = mini(y0, st.box[k * 4 + 1])
		x1 = maxi(x1, st.box[k * 4 + 2])
		y1 = maxi(y1, st.box[k * 4 + 3])
		ls += st.lsum[k]
	if px == 0:
		return {"pixels": 0, "visible": false}
	var bw := x1 - x0 + 1
	var bh := y1 - y0 + 1
	# Surround: the bbox grown by 30% (at least 6 px), class pixels excluded.
	var m := maxi(6, int(0.3 * maxi(bw, bh)))
	var s_sum := 0.0
	var lab_sum := Vector3.ZERO
	var s_n := 0
	for y in range(maxi(0, y0 - m), mini(h - 1, y1 + m) + 1):
		for x in range(maxi(0, x0 - m), mini(w - 1, x1 + m) + 1):
			if st.cmap[y * w + x] == 0:
				var lab := _lab(bd, (y * w + x) * 4)
				s_sum += lab.x
				lab_sum += lab
				s_n += 1
	var l_bg := s_sum / maxi(s_n, 1)
	var lab_bg := lab_sum / maxi(s_n, 1)
	# Mean |L* - surround| and mean dE76 (CIE Lab) over the object: a pale
	# wall and a dark lid both stand out, where a plain mean lets them cancel;
	# dE also counts hue (a yellow hull on grey-blue water).
	var dev := 0.0
	var de := 0.0
	var idset := {}
	for k in ids:
		idset[k + 1] = true
	for y in range(y0, y1 + 1):
		for x in range(x0, x1 + 1):
			if idset.has(st.cmap[y * w + x]):
				var lab := _lab(bd, (y * w + x) * 4)
				dev += absf(lab.x - l_bg)
				de += lab.distance_to(lab_bg)
	return {
		"pixels": px, "visible": true,
		"bbox": [x0, y0, x1, y1], "width_px": bw, "height_px": bh,
		"l_object": snappedf(ls / px, 0.01), "l_surround": snappedf(l_bg, 0.01),
		"delta_l": snappedf(absf(ls / px - l_bg), 0.01),
		"mean_abs_delta_l": snappedf(dev / px, 0.01),
		"mean_delta_e": snappedf(de / px, 0.01),
	}


## CIE L*a*b* (D65) of an sRGB pixel.
func _lab(d: PackedByteArray, o: int) -> Vector3:
	var r := _srgb_lut[d[o]]
	var g := _srgb_lut[d[o + 1]]
	var b := _srgb_lut[d[o + 2]]
	var fx := _lab_f((0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047)
	var fy := _lab_f(0.2126 * r + 0.7152 * g + 0.0722 * b)
	var fz := _lab_f((0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883)
	return Vector3(116.0 * fy - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz))


static func _lab_f(t: float) -> float:
	return pow(t, 1.0 / 3.0) if t > 0.008856 else 7.787 * t + 16.0 / 116.0


func _lstar(d: PackedByteArray, o: int) -> float:
	var y := 0.2126 * _srgb_lut[d[o]] + 0.7152 * _srgb_lut[d[o + 1]] + 0.0722 * _srgb_lut[d[o + 2]]
	return 116.0 * pow(y, 1.0 / 3.0) - 16.0 if y > 0.008856 else 903.3 * y


## Camera geometry for a target: distance, depression, transmittance, the
## projected (unoccluded) sizes and the kit's triangle cost from here.
func _geometry(target: String, samples: Dictionary) -> Dictionary:
	var cam_pos := _cam.global_position
	var centre := LAYOUT.plot_centre(target)
	var horiz := Vector2(centre.x - cam_pos.x, centre.z - cam_pos.z).length()
	var dist := cam_pos.distance_to(centre)
	var tr := float(_director.call("transmittance", dist))
	var g := {
		"centre": [centre.x, centre.y, centre.z],
		"distance_m": snappedf(dist, 0.1),
		"horizontal_m": snappedf(horiz, 0.1),
		"depression_deg": snappedf(rad_to_deg(atan2(cam_pos.y - centre.y, horiz)), 0.01),
		"transmittance": snappedf(tr, 0.0001),
		"needs_chip": tr < CHIP_TRANSMITTANCE,
		"in_frame": _in_frame(centre),
	}
	var pts: Array[Vector3] = []
	for p in LAYOUT.plot_placements(target):
		var s: Vector3 = p["size"]
		var xf := Transform3D(Basis(Vector3.UP, float(p["yaw"])), p["pos"])
		if str(p["asset"]) in ["tank_growout", "tank_pregrow", "of1_class"]:
			for k in 24:
				var a := TAU * k / 24.0
				pts.append(xf * Vector3(sin(a) * s.x * 0.5, 0.0, cos(a) * s.z * 0.5))
				pts.append(xf * Vector3(sin(a) * s.x * 0.5, s.y, cos(a) * s.z * 0.5))
			continue
		for c in [Vector2(-0.5, -0.5), Vector2(0.5, -0.5), Vector2(-0.5, 0.5), Vector2(0.5, 0.5)]:
			pts.append(xf * Vector3(c.x * s.x, 0.0, c.y * s.z))
			pts.append(xf * Vector3(c.x * s.x, s.y, c.y * s.z))
	g["projected"] = _project_box(pts)
	if int(samples.get(target, -1)) >= 0:
		var tank: Vector3 = BUILDER.growout_positions(target)[int(samples[target])]
		var r := LAYOUT.GROWOUT_D * 0.5
		var rim: Array[Vector3] = []
		var whole: Array[Vector3] = []
		for k in 64:
			var a := TAU * k / 64.0
			var top := tank + Vector3(sin(a) * r, LAYOUT.GROWOUT_H, cos(a) * r)
			rim.append(top)
			whole.append(top)
			whole.append(tank + Vector3(sin(a) * r, 0.0, cos(a) * r))
		var top_c := tank + Vector3(0, LAYOUT.GROWOUT_H, 0)
		var tank_d := cam_pos.distance_to(top_c)
		g["sample_tank"] = {
			"position": [tank.x, tank.y, tank.z],
			"distance_m": snappedf(tank_d, 0.1),
			"depression_deg": snappedf(rad_to_deg(atan2(cam_pos.y - top_c.y, Vector2(tank.x - cam_pos.x, tank.z - cam_pos.z).length())), 0.01),
			"lid_projected": _project_box(rim),
			"tank_projected": _project_box(whole),
			"lod": _lod(tank_d, TANK_LOD_M),
		}
	g["triangles"] = _triangle_estimate(target)
	return g


func _in_frame(p: Vector3) -> bool:
	if _cam.is_position_behind(p):
		return false
	return get_viewport().get_visible_rect().has_point(_cam.unproject_position(p))


func _project_box(pts: Array[Vector3]) -> Dictionary:
	var lo := Vector2(INF, INF)
	var hi := Vector2(-INF, -INF)
	var behind := 0
	for p in pts:
		if _cam.is_position_behind(p):
			behind += 1
			continue
		var s := _cam.unproject_position(p)
		lo = Vector2(minf(lo.x, s.x), minf(lo.y, s.y))
		hi = Vector2(maxf(hi.x, s.x), maxf(hi.y, s.y))
	if behind == pts.size():
		return {"behind": true}
	return {"width_px": snappedf(hi.x - lo.x, 0.1), "height_px": snappedf(hi.y - lo.y, 0.1),
		"bbox": [snappedf(lo.x, 0.1), snappedf(lo.y, 0.1), snappedf(hi.x, 0.1), snappedf(hi.y, 0.1)]}


static func _lod(d: float, bands: Vector2) -> int:
	return 0 if d < bands.x else (1 if d < bands.y else 2)


## Triangles the kit would draw for this target from this camera with the
## spec's LOD distances and budgets, and its LOD0 triangles inside 600 m.
func _triangle_estimate(target: String) -> Dictionary:
	var total := 0
	var lod0_600 := 0
	var by_lod := [0, 0, 0]
	for p in LAYOUT.plot_placements(target):
		var asset: String = p["asset"]
		var d := _cam.global_position.distance_to(p["pos"])
		var lod := _lod(d, TANK_LOD_M if asset.begins_with("tank_") else UNIT_LOD_M)
		total += int(TRI_BUDGET[asset][lod])
		by_lod[lod] += 1
		if d < 600.0:
			lod0_600 += int(TRI_BUDGET[asset][0])
	return {"total": total, "pieces_per_lod": by_lod, "lod0_tris_inside_600m": lod0_600}


func _camera_record(spec: Dictionary) -> Dictionary:
	var p := _cam.global_position
	var fwd := -_cam.global_basis.z
	var hf: Vector3 = _cam.get("home_focus")
	return {
		"spec": spec,
		"position": [snappedf(p.x, 0.01), snappedf(p.y, 0.01), snappedf(p.z, 0.01)],
		"pitch_deg": snappedf(rad_to_deg(asin(clampf(fwd.y, -1.0, 1.0))), 0.01),
		"yaw_deg": snappedf(rad_to_deg(atan2(-fwd.x, -fwd.z)), 0.01),
		"fov_deg": snappedf(_cam.fov, 0.01),
		"home_focus": [hf.x, hf.y, hf.z],
	}


func _environment_record() -> Dictionary:
	var c: Dictionary = _director.call("get_composed")
	var ws = _env.get("weather_system")
	return {
		"visibility_m": snappedf(float(c.get("visibility_m", 0.0)), 1.0),
		"sea_fog": float(ws.call("get_sea_fog")) if ws else 0.0,
		"fog_density": float(c.get("fog_density", 0.0)),
		"sun_elevation_deg": snappedf(float(c.get("sun_elevation", 0.0)), 0.01),
	}


## Contrast kept in each weather, relative to the clear home view: the
## measured counterpart of the transmittance. It includes the sea-fog layer,
## which the distance-fog transmittance leaves out.
func _finish_contrast() -> void:
	var clear: Dictionary = _results.get("36_graybox_home_clear", {})
	if clear.is_empty():
		return
	for sid in _results:
		if not str(sid).begins_with("36_graybox_home_") or not _results[sid].get("plot_overrides", {}).is_empty():
			continue
		for target in _results[sid]["targets"]:
			var t: Dictionary = _results[sid]["targets"][target]
			var base: Dictionary = clear["targets"].get(target, {}).get("mask", {})
			var now: Dictionary = t.get("mask", {})
			if base.get("visible", false) and now.get("visible", false) and float(base["mean_delta_e"]) > 0.0:
				t["contrast_vs_clear"] = snappedf(float(now["mean_delta_e"]) / float(base["mean_delta_e"]), 0.001)


# ── Performance A/B ──────────────────────────────────────────────────────────

## Draw calls (opaque and shadow) and frame time with the gray box off, land
## only, offshore only and all on (vsync off, A-B-A-B blocks). One build with
## no sample tanks, so the counts are the instanced plan's (spec §5).
func _perf_ab() -> Dictionary:
	_rebuild({})
	var vp := get_viewport()
	var land: Array[Node3D] = []
	var sea: Array[Node3D] = []
	for c in _box.get_children():
		(sea if str(c.name).begins_with("off_") else land).append(c)
	var vsync := DisplayServer.window_get_vsync_mode()
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	var out := {}
	for mode in ["off", "land", "offshore", "all", "off", "land", "offshore", "all"]:
		for c in land:
			c.visible = mode in ["land", "all"]
		for c in sea:
			c.visible = mode in ["offshore", "all"]
		for i in 10:
			await RenderingServer.frame_post_draw
		var t0 := Time.get_ticks_usec()
		for i in PERF_FRAMES:
			await RenderingServer.frame_post_draw
		var ms := float(Time.get_ticks_usec() - t0) / 1000.0 / PERF_FRAMES
		var rec: Dictionary = out.get(mode, {"frame_ms_samples": []})
		rec["opaque_draws"] = vp.get_render_info(Viewport.RENDER_INFO_TYPE_VISIBLE, Viewport.RENDER_INFO_DRAW_CALLS_IN_FRAME)
		rec["shadow_draws"] = vp.get_render_info(Viewport.RENDER_INFO_TYPE_SHADOW, Viewport.RENDER_INFO_DRAW_CALLS_IN_FRAME)
		rec["opaque_primitives"] = vp.get_render_info(Viewport.RENDER_INFO_TYPE_VISIBLE, Viewport.RENDER_INFO_PRIMITIVES_IN_FRAME)
		rec["frame_ms_samples"].append(snappedf(ms, 0.001))
		out[mode] = rec
	DisplayServer.window_set_vsync_mode(vsync)
	for c in _box.get_children():
		c.visible = true
	for k in out:
		var s: Array = out[k]["frame_ms_samples"]
		out[k]["frame_ms"] = snappedf((float(s[0]) + float(s[1])) * 0.5, 0.001)
	var off: Dictionary = out["off"]
	for k in ["land", "offshore", "all"]:
		var r: Dictionary = out[k]
		r["added_opaque_draws"] = int(r["opaque_draws"]) - int(off["opaque_draws"])
		r["added_shadow_draws"] = int(r["shadow_draws"]) - int(off["shadow_draws"])
		r["added_frame_ms"] = snappedf(float(r["frame_ms"]) - float(off["frame_ms"]), 0.001)
	return out


# ── Helpers ──────────────────────────────────────────────────────────────────

static func _nearest_index(points: Array[Vector3], ref: Vector3) -> int:
	var best := -1
	var best_d := INF
	for i in points.size():
		var d := points[i].distance_to(ref)
		if d < best_d:
			best_d = d
			best = i
	return best


func _wait(seconds: float) -> void:
	await get_tree().create_timer(seconds).timeout
