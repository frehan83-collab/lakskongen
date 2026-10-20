extends Node
## P0-04 performance smoke test: repeatable camera paths through the fjord.
##
## Boots the real GameScene on a seeded QA scenario (po3_red_sone, seed 91),
## stages a fixed day (summer noon, clear, sim and clock paused) and records
## every frame of a few segments:
##
## - farm / fjord / coast: the camera focused on the first pen at that zoom
##   (FjordCamera.ZOOM_DISTANCE), sweeping its yaw 50 degrees round the home
##   view (the fjord axis toward the mouth) over the segment, the HUD on the
##   Fjord tab as the player sees it;
## - panel: the finance tab open, an opaque full-screen panel over the world
##   (shot 06), the camera at the FJORD default.
##
## Per frame: wall-clock frame time (vsync off, no fps cap), the viewport's
## GPU and CPU render time (RenderingServer timestamps), the script process
## time, draw calls, shadow draw calls, primitives and video memory. Per
## segment: p50 / p95 / p99. Variants (quality presets and knob overrides)
## run interleaved, ABAB, so a background load on a shared machine hits
## every variant alike: compare variants inside one run, not across runs.
##
## Run it through tools/perf_smoke.sh, or directly (a display is required):
##   godot --path godot --resolution 2560x1440 res://scenes/tools/PerfHarness.tscn \
##     -- --out=/abs/dir --seconds=10 --variants="high;ultra" --repeats=2
##
## Arguments (after --):
##   --out=DIR          where perf_<label>.json goes (default user://perf)
##   --label=NAME       file label (default "smoke")
##   --seconds=S        recorded seconds per segment (default 10)
##   --warmup=S         settle time before each segment (default 2)
##   --segments=LIST    comma list of farm,fjord,coast,panel (default all)
##   --variants=LIST    ';' list of variants; each is a preset name and
##                      optional knob overrides: "high,sky_res=full,ssao=false"
##                      (default: the director's default preset)
##   --variants-file=F  the same, one variant per line
##   --repeats=N        passes over every variant (default 1)
##   --paired           run the first variant before every other one (A B A C
##                      A D ...): tools/perf_report.py then compares each
##                      variant with the reference block next to it
##   --weather=NAME     clear | overcast | storm (default clear)
##   --hour=H           presentation hour (default 12)
##   --pitch=DEG        tilt the view from the zoom's pitch (positive looks up)
##   --season=S         fraction of the year from 1 January (default 0.5)
##   --latitude=DEG     the sky's latitude (default: the scenario's)
##   --shots            save a frame per segment and variant next to the JSON
##   --burst=N          with --shots, also N consecutive frames of a slow orbit
##   --series           keep every frame's time, phases and draw counts
##   --boot-only        record the boot (first frames, terrain upload) and quit
##   --no-prewarm       boot without the terrain pipeline warm-up (its A/B)
##
## It prints one PERF_SEGMENT line per segment and PERF_RESULT at the end,
## and exits non-zero only when it could not measure (no display, no scene).

const GAME_SCENE := "res://scenes/GameScene.tscn"
const QA_SCENARIO_ID := "po3_red_sone"
const QA_SEED := 91
## The frame budget (60 fps): the gate's line, reported per segment.
const BUDGET_MS := 16.7
## Yaw swept over a zoom segment (degrees, centred on the home view).
const SWEEP_DEG := 50.0
## Frames slower than this during the boot count as spikes.
const SPIKE_MS := 50.0
## Boot frames recorded (from the scene entering the tree).
const BOOT_FRAMES := 900
const SEGMENT_NAMES := ["farm", "fjord", "coast", "panel"]
## Quality tier names (RenderQuality.NAMES, kept local so the harness runs on
## builds that predate the tiers).
const TIERS := ["low", "medium", "high", "ultra"]
## FjordCamera.ZoomLevel ordinals (kept local, like ScreenshotHarness).
const ZOOM_COAST := 0
const ZOOM_FJORD := 1
const ZOOM_FARM := 2
const SEGMENT_ZOOM := {"farm": ZOOM_FARM, "fjord": ZOOM_FJORD, "coast": ZOOM_COAST, "panel": ZOOM_FJORD}
## WeatherController.WeatherState ordinals.
const WEATHER := {"clear": 0, "overcast": 1, "rain": 2, "snow": 3, "storm": 4, "fog": 5}

var _args := {}
var _game: Node = null
var _env: Node = null
var _director: Node = null
var _camera: Node = null
var _ui_layer: CanvasLayer = null
var _game_ui: Node = null
var _focus := Vector3.ZERO
var _results: Array = []
var _boot := {}
var _out_dir := "user://perf"
## Frame phases (usec ticks): the scene tree's process step starts
## (process_frame), the renderer starts (frame_pre_draw) and finishes
## (frame_post_draw). Script time = process -> pre_draw; render submission =
## pre_draw -> post_draw; the rest of the frame is waiting (on the GPU's
## swap chain, physics, the OS).
var _t_process := 0
var _t_pre_draw := 0
var _phase_script := PackedFloat32Array()
var _phase_render := PackedFloat32Array()
var _recording := false


func _ready() -> void:
	if DisplayServer.get_name() == "headless":
		push_error("PerfHarness: headless cannot render; run tools/perf_smoke.sh from a desktop session.")
		get_tree().quit(2)
		return
	_args = parse_args(OS.get_cmdline_user_args())
	_out_dir = String(_args.get("out", "user://perf"))
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(_out_dir) if _out_dir.begins_with("user://") else _out_dir)
	# Measure the work, not the display: no vsync, no frame cap.
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	Engine.max_fps = 0
	RenderingServer.viewport_set_measure_render_time(get_viewport().get_viewport_rid(), true)
	get_tree().process_frame.connect(_on_process_frame)
	RenderingServer.frame_pre_draw.connect(_on_pre_draw)
	RenderingServer.frame_post_draw.connect(_on_post_draw)
	_run.call_deferred()


func _on_process_frame() -> void:
	_t_process = Time.get_ticks_usec()


func _on_pre_draw() -> void:
	_apply_raw_overrides()
	_t_pre_draw = Time.get_ticks_usec()
	if _recording and _t_process > 0:
		_phase_script.append((_t_pre_draw - _t_process) / 1000.0)


func _on_post_draw() -> void:
	if _recording and _t_pre_draw > 0:
		_phase_render.append((Time.get_ticks_usec() - _t_pre_draw) / 1000.0)


## Parse "--key=value" and bare "--flag" arguments.
static func parse_args(argv: PackedStringArray) -> Dictionary:
	var out := {}
	for a in argv:
		if not a.begins_with("--"):
			continue
		var body := a.substr(2)
		var eq := body.find("=")
		if eq < 0:
			out[body] = true
		else:
			out[body.substr(0, eq)] = body.substr(eq + 1)
	return out


## A variant string "high,sky_res=full,ssao=false" -> {"preset": "high",
## "overrides": {"sky_res": "full", "ssao": false}}. Values that read as
## numbers or booleans become numbers or booleans.
static func parse_variant(text: String) -> Dictionary:
	var parts := text.strip_edges().split(",", false)
	var preset := ""
	var overrides := {}
	for p in parts:
		var kv := p.strip_edges().split("=", false, 1)
		if kv.size() == 1:
			preset = kv[0]
		else:
			overrides[kv[0]] = _parse_value(kv[1])
	return {"name": text.strip_edges(), "preset": preset, "overrides": overrides}


static func _parse_value(v: String) -> Variant:
	if v == "true":
		return true
	if v == "false":
		return false
	if v.is_valid_int():
		return v.to_int()
	if v.is_valid_float():
		return v.to_float()
	return v


## Percentile (nearest rank on a sorted copy), 0-100.
static func percentile(values: PackedFloat32Array, p: float) -> float:
	if values.is_empty():
		return 0.0
	var sorted := values.duplicate()
	sorted.sort()
	var rank := clampi(int(ceil(p / 100.0 * sorted.size())) - 1, 0, sorted.size() - 1)
	return sorted[rank]


static func stats(values: PackedFloat32Array) -> Dictionary:
	if values.is_empty():
		return {"p10": 0.0, "p50": 0.0, "p95": 0.0, "p99": 0.0, "mean": 0.0, "max": 0.0}
	var total := 0.0
	var top := 0.0
	for v in values:
		total += v
		top = maxf(top, v)
	return {
		# p10: the frames least disturbed by other work on a shared machine,
		# the in-run A/B estimate of a variant's own cost.
		"p10": snappedf(percentile(values, 10.0), 0.01),
		"p50": snappedf(percentile(values, 50.0), 0.01),
		"p95": snappedf(percentile(values, 95.0), 0.01),
		"p99": snappedf(percentile(values, 99.0), 0.01),
		"mean": snappedf(total / values.size(), 0.01),
		"max": snappedf(top, 0.01),
	}


# ── Run ───────────────────────────────────────────────────────────────────────

func _run() -> void:
	var win := get_window()
	print("PerfHarness: window %s px, adapter %s" % [str(win.size), RenderingServer.get_video_adapter_name()])
	if not await _boot_game():
		get_tree().quit(3)
		return
	if _args.has("boot-only"):
		_write_report()
		get_tree().quit(0)
		return
	var segments: Array = String(_args.get("segments", ",".join(SEGMENT_NAMES))).split(",", false)
	var variants: Array = []
	var variant_text := String(_args.get("variants", ""))
	if _args.has("variants-file"):
		# One variant per line ('#' starts a comment).
		variant_text = ";".join(Array(FileAccess.get_file_as_string(String(_args["variants-file"])).split("\n")).filter(
			func(line: String) -> bool: return line.strip_edges() != "" and not line.strip_edges().begins_with("#")))
	for v in variant_text.split(";", false):
		variants.append(parse_variant(v))
	if variants.is_empty():
		variants.append({"name": "default", "preset": "", "overrides": {}})
	var repeats := int(_args.get("repeats", 1))
	var seconds := float(_args.get("seconds", 10.0))
	var warmup := float(_args.get("warmup", 2.0))
	# Paired: every variant runs right after the first (the reference), so
	# each comparison sees the same moment's background load.
	var order: Array = variants
	if _args.has("paired") and variants.size() > 1:
		order = []
		for v in variants.slice(1):
			order.append(variants[0])
			order.append(v)
	for r in repeats:
		for seg in segments:
			if not seg in SEGMENT_NAMES:
				push_warning("PerfHarness: unknown segment '%s'" % seg)
				continue
			for i in order.size():
				var v: Dictionary = order[i]
				_apply_variant(v)
				var res: Dictionary = await _run_segment(seg, seconds, warmup, v, r)
				res["block"] = i
				_results.append(res)
	_set_panel(false)
	_write_report()
	print("PERF_RESULT segments=%d variants=%d repeats=%d" % [segments.size(), variants.size(), repeats])
	win.title = "PerfHarness done"
	get_tree().quit(0)


func _boot_game() -> bool:
	var sim := get_node_or_null("/root/SimBridge")
	if sim == null or not sim.start_qa_scenario(QA_SCENARIO_ID, QA_SEED):
		push_error("PerfHarness: the QA scenario did not start")
		return false
	sim.set_paused(true)
	var packed := load(GAME_SCENE) as PackedScene
	if packed == null:
		push_error("PerfHarness: GameScene unavailable")
		return false
	# Dynamic: the harness also measures builds from before the quality tiers
	# (the P0-04 baseline), where this switch does not exist.
	var director_script: Script = load("res://scripts/environment/EnvironmentDirector.gd")
	if director_script != null and "prewarm_enabled" in director_script:
		director_script.set("prewarm_enabled", not _args.has("no-prewarm"))
	_game = packed.instantiate()
	var started := Time.get_ticks_usec()
	add_child(_game)
	sim.set_paused(true)
	_env = _game.get_node_or_null("FjordEnvironment")
	_director = _env.get_node_or_null("EnvironmentDirector") if _env else null
	_camera = _env.get_node_or_null("FjordCamera") if _env else null
	_ui_layer = _game.get_node_or_null("UILayer") as CanvasLayer
	_game_ui = _game.get_node_or_null("UILayer/GameUI")
	var terrain: Node = _env.get_node_or_null("Terrain") if _env else null
	if _env == null or _camera == null or terrain == null:
		push_error("PerfHarness: the fjord scene is missing its environment")
		return false
	_camera.set("auto_orbit_enabled", false)
	# Boot: every frame until the terrain is up and the frame times settle.
	var boot_frames := PackedFloat32Array()
	# Terrain upload steps done by each frame (which frame carried which step).
	var steps_at := PackedInt32Array()
	var ready_frame := -1
	_phase_script = PackedFloat32Array()
	_phase_render = PackedFloat32Array()
	_recording = true
	var last := Time.get_ticks_usec()
	for i in BOOT_FRAMES:
		await get_tree().process_frame
		var now := Time.get_ticks_usec()
		boot_frames.append((now - last) / 1000.0)
		steps_at.append((terrain.get("upload_steps_ms") as PackedFloat32Array).size())
		last = now
		if ready_frame < 0 and terrain.call("is_ready"):
			ready_frame = i
		if ready_frame >= 0 and i > ready_frame + 120:
			break
	_recording = false
	var spikes := []
	for i in boot_frames.size():
		if boot_frames[i] > SPIKE_MS:
			spikes.append({"frame": i, "ms": snappedf(boot_frames[i], 0.1), "terrain_steps": steps_at[i]})
	_boot = {
		"frames": boot_frames.size(),
		"terrain_ready_frame": ready_frame,
		"terrain_ready_s": snappedf((Time.get_ticks_usec() - started) / 1e6, 0.01),
		"terrain_upload_steps_ms": Array(terrain.get("upload_steps_ms")),
		"terrain_max_upload_ms": terrain.get("max_upload_frame_ms"),
		"spikes_over_%d_ms" % SPIKE_MS: spikes,
		"max_frame_ms": snappedf(stats(boot_frames)["max"], 0.1),
		"max_frame_after_first_10": snappedf(stats(boot_frames.slice(10))["max"], 0.1),
		"first_frames_ms": Array(boot_frames.slice(0, 40)).map(func(v: float) -> float: return snappedf(v, 0.1)),
		"terrain_steps_at_frame": Array(steps_at.slice(0, 40)),
		# Per frame: script time (process) and render submission (draw).
		"first_script_ms": Array(_phase_script.slice(0, 40)).map(func(v: float) -> float: return snappedf(v, 0.1)),
		"first_render_ms": Array(_phase_render.slice(0, 40)).map(func(v: float) -> float: return snappedf(v, 0.1)),
	}
	print("PERF_BOOT terrain_ready_frame=%d spikes=%d max_after_10=%.1f ms" % [ready_frame, spikes.size(), _boot["max_frame_after_first_10"]])
	# Stage a fixed day: the sim stops driving the sky.
	var vista := _game.get_node_or_null("FjordVistaBridge")
	if vista:
		vista.set("sim_weather_enabled", false)
	var ts: Node = _env.get("time_system")
	if ts:
		ts.set("paused", true)
	var weather: Node = _env.get("weather_system")
	if weather:
		weather.set("transition_duration", 0.0)
	if _args.has("latitude"):
		_env.call("set_latitude", float(_args["latitude"]))
	_env.call("set_season", float(_args.get("season", 0.5)))
	_env.call("set_time", float(_args.get("hour", 12.0)))
	_env.call("set_weather", WEATHER.get(String(_args.get("weather", "clear")), 0))
	# Focus: the first pen (the site the player starts on).
	var pens: Node = _env.get_node_or_null("FishPenManager")
	for i in 100:
		if pens and not pens.call("get_pen_ids").is_empty():
			break
		await get_tree().process_frame
	if pens and not pens.call("get_pen_ids").is_empty():
		_focus = pens.call("get_pen_position", pens.call("get_pen_ids")[0])
	var toasts := get_node_or_null("/root/ToastNotification") as CanvasLayer
	if toasts:
		toasts.visible = false
	return true


## A variant's knobs without a dot go to the director's quality system; keys
## with a dot are raw profiling overrides (below).
func _apply_variant(v: Dictionary) -> void:
	_restore_raw()
	_raw = {}
	var knobs := {}
	var overrides: Dictionary = v.get("overrides", {})
	for key in overrides:
		if String(key).contains("."):
			_raw[key] = overrides[key]
		else:
			knobs[key] = overrides[key]
	if _director == null or not _director.has_method("set_quality"):
		return
	var preset := String(v.get("preset", ""))
	if not preset in TIERS:
		# A label ("base"): the tier in force.
		preset = String(_director.call("get_quality_name"))
	_director.call("set_quality", preset, knobs)


## Raw overrides, for profiling only: "env.ssao_enabled=false" (the
## Environment), "vp.msaa_3d=0" (the root viewport), "sun.shadow_enabled=false",
## "camattr.dof_blur_far_enabled=false", "sky.<uniform>" (the sky material),
## "water.<uniform>" (the water material), "shoal.<uniform>" (the schools
## seen through the surface, one shared material), "hide.<path under the
## FjordEnvironment>=true" (visible = false), "mirrorenv.<property>",
## "mirrorcam.<property>" and "mirrorvp.<property>" (the planar mirror's
## environment, camera and viewport), and "hideall.<name prefix>=true" (every
## GeometryInstance3D under the FjordEnvironment whose name begins with the
## prefix: "Shoal", "FishSchool", "Net"... to split a node's cost by part).
## They are set right before every draw, after the director composed the
## frame, so they win.
var _raw := {}
## Values the raw overrides replaced: [object, property, value, is_uniform].
var _raw_saved := {}
## hideall: the nodes each prefix matched (found once per variant).
var _hideall_nodes := {}


func _apply_raw_overrides() -> void:
	if _raw.is_empty() or _env == null:
		return
	for key: String in _raw:
		if scope_of(key) == "hideall":
			_hide_all(key.substr(key.find(".") + 1), bool(_raw[key]))
			continue
		var target := _raw_target(key)
		if target.is_empty():
			continue
		var obj: Object = target[0]
		var prop: String = target[1]
		var uniform: bool = target[2]
		if not _raw_saved.has(key):
			_raw_saved[key] = [obj, prop, (obj as ShaderMaterial).get_shader_parameter(prop) if uniform else obj.get(prop), uniform]
		var value: Variant = _raw[key]
		if scope_of(key) == "hide":
			value = not bool(value)
		if uniform:
			(obj as ShaderMaterial).set_shader_parameter(prop, value)
		else:
			obj.set(prop, value)


## Hide (or show) every GeometryInstance3D under the environment whose name
## begins with `prefix`, remembering each one's visibility for the restore.
## The nodes are found once per variant (the walk would cost script time
## every frame).
func _hide_all(prefix: String, hidden: bool) -> void:
	if not _hideall_nodes.has(prefix):
		var found: Array[Node3D] = []
		var stack: Array[Node] = [_env]
		while not stack.is_empty():
			var node: Node = stack.pop_back()
			for child in node.get_children():
				stack.append(child)
			if node is GeometryInstance3D and String(node.name).begins_with(prefix):
				found.append(node as Node3D)
				_raw_saved["hideall:%d" % node.get_instance_id()] = [node, "visible", (node as Node3D).visible, false]
		_hideall_nodes[prefix] = found
		print("PERF_HIDEALL prefix=%s nodes=%d" % [prefix, found.size()])
	for node: Node3D in _hideall_nodes[prefix]:
		if is_instance_valid(node):
			node.visible = not hidden


func _restore_raw() -> void:
	_hideall_nodes = {}
	for key in _raw_saved:
		var saved: Array = _raw_saved[key]
		if not is_instance_valid(saved[0]):
			continue
		if saved[3]:
			(saved[0] as ShaderMaterial).set_shader_parameter(saved[1], saved[2])
		else:
			(saved[0] as Object).set(saved[1], saved[2])
	_raw_saved = {}


static func scope_of(key: String) -> String:
	return key.substr(0, key.find("."))


## [object, property, is_shader_uniform] for a raw key, or [] if absent.
func _raw_target(key: String) -> Array:
	var world_env := _env.get_node_or_null("WorldEnvironment") as WorldEnvironment
	var prop := key.substr(key.find(".") + 1)
	match scope_of(key):
		"env":
			return [world_env.environment, prop, false] if world_env else []
		"vp":
			return [get_viewport(), prop, false]
		"sun":
			var sun := _env.get_node_or_null("Sun")
			return [sun, prop, false] if sun else []
		"camattr":
			var attrs: Object = _camera.get("attributes") if _camera else null
			return [attrs, prop, false] if attrs else []
		"sky":
			if world_env and world_env.environment.sky:
				return [world_env.environment.sky.sky_material, prop, true]
		"skyres":
			if world_env and world_env.environment.sky:
				return [world_env.environment.sky, prop, false]
		"water":
			var grid := _env.get_node_or_null("WaterGrid")
			if grid and grid.get("material"):
				return [grid.get("material"), prop, true]
		"shoal":
			# The salmon schools seen through the surface (G3-01): one shared
			# material, so "shoal.fish_range=0" splits the fish from the pens'
			# water column (gfx-polish-1).
			return [FishSchool.SHOAL_MATERIAL, prop, true]
		"terrain":
			var terrain := _env.get_node_or_null("Terrain")
			var chunks: Array = terrain.call("get_chunk_meshes") if terrain else []
			if not chunks.is_empty() and (chunks[0] as MeshInstance3D).material_override is ShaderMaterial:
				return [(chunks[0] as MeshInstance3D).material_override, prop, true]
		"hide":
			var node := _env.get_node_or_null(prop)
			return [node, "visible", false] if node else []
		"probe":
			var probe := _env.get_node_or_null("ReflectionProbe")
			return [probe, prop, false] if probe else []
		"mirrorenv":
			# The planar mirror's environment (G2-03).
			var mirror := _env.get_node_or_null("PlanarReflection")
			return [mirror.get("environment"), prop, false] if mirror else []
		"mirrorcam":
			var mirror_node := _env.get_node_or_null("PlanarReflection")
			var mirror_cam: Object = mirror_node.call("get_mirror_camera") if mirror_node else null
			return [mirror_cam, prop, false] if mirror_cam else []
		"mirrorvp":
			var mirror_owner := _env.get_node_or_null("PlanarReflection")
			var mirror_vp: Object = mirror_owner.get_node_or_null("MirrorViewport") if mirror_owner else null
			return [mirror_vp, prop, false] if mirror_vp else []
	return []


func _set_panel(open: bool) -> void:
	if _game_ui and _game_ui.has_method("show_tab"):
		_game_ui.call("show_tab", "finance" if open else "fjord")
	var center := _game.get_node_or_null("UILayer/NotificationCenter") if _game else null
	if center and center.has_method("hide_panel"):
		center.call("hide_panel")


func _run_segment(seg: String, seconds: float, warmup: float, variant: Dictionary, repeat: int) -> Dictionary:
	var panel := seg == "panel"
	_set_panel(panel)
	_camera.call("look_at_position", _focus, SEGMENT_ZOOM[seg])
	var home: float = _camera.call("home_yaw", _focus)
	var start_yaw := home if panel else home - deg_to_rad(SWEEP_DEG * 0.5)
	_camera.set("_yaw_target", start_yaw)
	_camera.call("set_pitch_offset", float(_args.get("pitch", 0.0)))
	_camera.call("_snap")
	var t := 0.0
	while t < warmup:
		await get_tree().process_frame
		t += get_process_delta_time()
	var vp := get_viewport()
	var rid := vp.get_viewport_rid()
	var frame_ms := PackedFloat32Array()
	var gpu_ms := PackedFloat32Array()
	var cpu_ms := PackedFloat32Array()
	var process_ms := PackedFloat32Array()
	var draws := PackedFloat32Array()
	var shadow_draws := PackedFloat32Array()
	var prims := PackedFloat32Array()
	var last := Time.get_ticks_usec()
	var elapsed := 0.0
	var yaw_done := 0.0
	var sweep := 0.0 if panel else deg_to_rad(SWEEP_DEG)
	_phase_script = PackedFloat32Array()
	_phase_render = PackedFloat32Array()
	_recording = true
	while elapsed < seconds:
		await get_tree().process_frame
		var now := Time.get_ticks_usec()
		var dt := (now - last) / 1e6
		last = now
		elapsed += dt
		# The path follows the clock, so every run sweeps the same arc.
		var want := sweep * clampf(elapsed / seconds, 0.0, 1.0)
		_camera.call("rotate_view", want - yaw_done)
		yaw_done = want
		frame_ms.append(dt * 1000.0)
		gpu_ms.append(RenderingServer.viewport_get_measured_render_time_gpu(rid))
		cpu_ms.append(RenderingServer.viewport_get_measured_render_time_cpu(rid) + RenderingServer.get_frame_setup_time_cpu())
		process_ms.append(Performance.get_monitor(Performance.TIME_PROCESS) * 1000.0)
		draws.append(Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME))
		shadow_draws.append(vp.get_render_info(Viewport.RENDER_INFO_TYPE_SHADOW, Viewport.RENDER_INFO_DRAW_CALLS_IN_FRAME))
		prims.append(Performance.get_monitor(Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME))
	_recording = false
	var res := {
		"segment": seg,
		"variant": variant.get("name", "default"),
		"repeat": repeat,
		"quality": _director.call("get_quality_knobs") if _director and _director.has_method("get_quality_knobs") else {},
		"frames": frame_ms.size(),
		"seconds": snappedf(elapsed, 0.01),
		"frame_ms": stats(frame_ms),
		"gpu_ms": stats(gpu_ms),
		"cpu_render_ms": stats(cpu_ms),
		"process_ms": stats(process_ms),
		"script_phase_ms": stats(_phase_script),
		"render_phase_ms": stats(_phase_render),
		"draw_calls": stats(draws)["p50"],
		"draw_calls_max": stats(draws)["max"],
		"shadow_draw_calls": stats(shadow_draws)["p50"],
		"primitives": stats(prims)["p50"],
		"vram_mb": snappedf(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED) / 1048576.0, 0.1),
		"render_scale": vp.scaling_3d_scale,
		"msaa_3d": vp.msaa_3d,
		"screen_space_aa": vp.screen_space_aa,
		"over_budget": float(frame_ms.size() - _count_under(frame_ms, BUDGET_MS)) / maxf(frame_ms.size(), 1.0),
	}
	if _args.has("series"):
		# Every frame's time and phases, for finding what a tail is made of.
		res["series"] = {
			"frame_ms": Array(frame_ms).map(func(v: float) -> float: return snappedf(v, 0.01)),
			"script_ms": Array(_phase_script).map(func(v: float) -> float: return snappedf(v, 0.01)),
			"render_ms": Array(_phase_render).map(func(v: float) -> float: return snappedf(v, 0.01)),
			"draws": Array(draws), "shadow_draws": Array(shadow_draws), "primitives": Array(prims),
		}
	print("PERF_SEGMENT %s variant=%s repeat=%d frames=%d frame_p50=%.2f p95=%.2f p99=%.2f gpu_p50=%.2f script_p50=%.2f render_p50=%.2f draws=%d shadow_draws=%d" % [
		seg, res["variant"], repeat, res["frames"], res["frame_ms"]["p50"], res["frame_ms"]["p95"], res["frame_ms"]["p99"],
		res["gpu_ms"]["p50"], res["script_phase_ms"]["p50"], res["render_phase_ms"]["p50"], int(res["draw_calls"]), int(res["shadow_draw_calls"])])
	if _args.has("shots") and repeat == 0:
		var dir := _out_dir if not _out_dir.begins_with("user://") else ProjectSettings.globalize_path(_out_dir)
		var stem := "perf_%s_%s_%s_r%d" % [_label(), seg, _slug(String(res["variant"])), repeat]
		await RenderingServer.frame_post_draw
		var img := vp.get_texture().get_image()
		if img:
			img.save_png(dir.path_join(stem + ".png"))
		# A short burst while the camera keeps orbiting slowly (temporal
		# stability checks: the net's shimmer, G1-06).
		var burst := int(_args.get("burst", 0))
		for i in burst:
			_camera.call("rotate_view", deg_to_rad(0.05))
			await RenderingServer.frame_post_draw
			var frame := vp.get_texture().get_image()
			if frame:
				frame.save_png(dir.path_join("%s_b%02d.png" % [stem, i]))
	return res


static func _count_under(values: PackedFloat32Array, limit: float) -> int:
	var n := 0
	for v in values:
		if v <= limit:
			n += 1
	return n


func _label() -> String:
	return String(_args.get("label", "smoke"))


static func _slug(text: String) -> String:
	var out := ""
	for ch in text:
		out += ch if (ch >= "a" and ch <= "z") or (ch >= "0" and ch <= "9") or ch == "-" else "_"
	return out


func _write_report() -> void:
	# The 3D frame renders at the window's pixel size (canvas_items stretch
	# scales only the 2D layer).
	var size := Vector2(get_window().size)
	var report := {
		"harness": "P0-04 PerfHarness",
		"label": _label(),
		"timestamp": Time.get_datetime_string_from_system(true),
		"godot": Engine.get_version_info().get("string", ""),
		"adapter": RenderingServer.get_video_adapter_name(),
		"api": RenderingServer.get_video_adapter_api_version(),
		"resolution": [int(size.x), int(size.y)],
		"budget_ms": BUDGET_MS,
		"scenario": QA_SCENARIO_ID,
		"seed": QA_SEED,
		"args": _args,
		"boot": _boot,
		"runs": _results,
	}
	var dir := _out_dir if not _out_dir.begins_with("user://") else ProjectSettings.globalize_path(_out_dir)
	var path := dir.path_join("perf_%s.json" % _label())
	var f := FileAccess.open(path, FileAccess.WRITE)
	if f == null:
		push_error("PerfHarness: cannot write %s" % path)
		return
	# Compact: the markdown table from tools/perf_report.py is the readable form.
	f.store_string(JSON.stringify(report) + "\n")
	f.close()
	print("PERF_REPORT %s" % path)
