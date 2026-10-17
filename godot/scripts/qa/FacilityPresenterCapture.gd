extends "res://scripts/qa/QaWorldCapture.gd"
## F2-19 / F2-20 capture: the carved land pads and the facility presenter on
## the real game world.
##
## F2-19 shots ("pad_state": {pad id: 0..1}) paint the pads through the
## terrain's pad_state (0 heath, 1 blasted granite) with no facility shown;
## with "measure_pads" the frame's mean CIE Lab over each pad's top (a grid
## of points on the pad projected through the shot's camera) is recorded, and
## the heath and granite shots of one framing are compared (ΔE76).
##
## F2-20 shots ("facilities": true) build the farm-type QA set through the
## engine (GameBridge.QaPlaceFarmTypes: one HFS lot on pad_a, one post-smolt
## site on pad_delta, one OF1-class unit on off_1), stand each unit at the
## shot's build progress ("construction": {"land", "post_smolt", "offshore":
## 0..1}, GameBridge.QaSetFacilityConstruction) and let the presenter
## (FarmFacilityVisualManager in GameScene) draw them. Per shot it records the
## units' phases and the tanks and units the presenter draws against the
## engine's counts; with "perf" the draw calls the facilities add (A/B, the
## presenter hidden and shown); and a 64-bit perceptual hash of the frame, so
## shots of different phases can be compared (pHash Hamming distance, the
## variant_of pairs).
##
## Output: res://screenshots/game/<id>.png and 43_facilities_metrics.json.
## Run: tools/capture_screenshots.sh --scene res://qa/scenes/FacilityPresenterCapture.tscn

const SCENE_PATH := "res://qa/scenes/FacilityPresenterCapture.tscn"
const METRICS_FILE := "43_facilities_metrics.json"
## Points per pad side sampled for the pad colour (inset 12 m from the edge).
const PAD_SAMPLES := Vector2i(24, 16)
const PAD_INSET_M := 12.0
## pHash: the frame's luma at 32 x 32, its DCT's 8 x 8 low corner (DC out).
const PHASH_SIZE := 32
const PHASH_LOW := 8

@export var auto_run := true

var _terrain: Node = null
var _manager: Node = null
var _results := {}
var _units := {}
## Lots the QA set's grow-out site stands at (QaPlaceFarmTypes builds one).
var _land_lots := 1


func _ready() -> void:
	if auto_run:
		start()


func scene_path() -> String:
	return SCENE_PATH


func run_shots(shots: Array) -> void:
	_terrain = _env.get_node_or_null("Terrain")
	_manager = _env.get_node_or_null("FarmFacilityVisualManager")
	for shot in shots:
		var sid := str(shot["id"])
		print("[Facilities] shot %s" % sid)
		stage_environment(shot)
		var with_units := bool(shot.get("facilities", false))
		if with_units:
			await _stage_units(shot)
		elif _manager != null:
			_manager.visible = false
		if not with_units:
			_stage_pads(shot)
		var cam_spec: Dictionary = shot.get("camera", {"mode": "home"})
		var focus := _plot_focus(str(cam_spec.get("target", "pad_a")))
		if cam_spec.has("offset"):
			focus += Vector3(float(cam_spec["offset"][0]), 0.0, float(cam_spec["offset"][1]))
		focus = stage_camera(cam_spec, focus)
		await wait(2.0)
		var beauty := await grab()
		save(beauty, sid + ".png")
		var rec := {
			"camera": {"spec": cam_spec, "position": [_cam.global_position.x, _cam.global_position.y, _cam.global_position.z],
				"fov_deg": snappedf(_cam.fov, 0.01)},
			"environment": {"sun_elevation_deg": snappedf(float(_director.call("get_composed").get("sun_elevation", 0.0)), 0.01),
				"weather": str(shot.get("weather", "clear"))},
			"phash": phash_hex(beauty),
		}
		if shot.has("pad_state"):
			rec["pad_state"] = shot["pad_state"]
		if bool(shot.get("measure_pads", false)):
			rec["pads"] = _measure_pads(beauty)
		if with_units:
			rec["units"] = _unit_record()
			if bool(shot.get("perf", false)):
				rec["perf"] = await _perf(shot.get("perf_units", []))
		_results[sid] = rec
	_compare_pads()
	_compare_variants(shots)
	var size := get_viewport().get_visible_rect().size
	var path := write_json(METRICS_FILE, {
		"items": ["F2-19", "F2-20"], "generated_by": "res://scripts/qa/FacilityPresenterCapture.gd",
		"resolution": [int(size.x), int(size.y)], "shots": _results,
	})
	print("[Facilities] metrics: %s" % path)
	print("FACILITIES_RESULT shots=%d" % _results.size())
	get_tree().quit(0)


## A plot's centre (pad top or waterline) from the terrain's own slots, or
## with "unit:<land|post_smolt|offshore>" the presenter's focus on that unit
## of the QA set (the campus's blasted ground, the unit's deck).
func _plot_focus(plot_id: String) -> Vector3:
	if plot_id.begins_with("unit:") and _manager != null:
		var node: Node = _manager.call("visual_of", str(_units.get(plot_id.trim_prefix("unit:"), "")))
		if node != null and node.has_method("focus_point"):
			return node.call("focus_point")
	if _terrain != null:
		for pad: Dictionary in _terrain.call("land_pad_slots"):
			if str(pad["id"]) == plot_id:
				return pad["centre"]
		for slot: Dictionary in _terrain.call("offshore_slots"):
			if str(slot["id"]) == plot_id:
				return slot["position"]
	return Vector3.ZERO


## Every pad heath unless the shot paints it.
func _stage_pads(shot: Dictionary) -> void:
	if _terrain == null:
		return
	var states: Dictionary = shot.get("pad_state", {})
	for pad: Dictionary in _terrain.call("land_pad_slots"):
		var id := str(pad["id"])
		_terrain.call("set_pad_state", id, float(states.get(id, 0.0)))


## The QA set (built once) at the shot's build progress, drawn by the presenter.
func _stage_units(shot: Dictionary) -> void:
	var gb := get_node_or_null("/root/GameBridge")
	if gb == null or _manager == null:
		push_error("[Facilities] GameBridge or the presenter is missing")
		return
	if _units.is_empty():
		var placed: Dictionary = gb.QaPlaceFarmTypes()
		if not bool(placed.get("success", false)):
			push_error("[Facilities] QaPlaceFarmTypes failed: %s" % placed.get("message", ""))
		_units = {"land": str(placed.get("land_id", "")), "post_smolt": str(placed.get("post_smolt_id", "")),
			"offshore": str(placed.get("offshore_id", ""))}
	var progress: Dictionary = shot.get("construction", {})
	for kind: String in _units:
		gb.QaSetFacilityConstruction(_units[kind], float(progress.get(kind, 1.0)))
	# The grow-out site's lots (1 the QA lot, 4 the full campus of the budget).
	var lots := int(shot.get("land_lots", 1))
	if lots != _land_lots:
		if gb.QaSetLandLots(str(_units.get("land", "")), lots):
			_land_lots = lots
		else:
			push_error("[Facilities] QaSetLandLots refused %d" % lots)
	# Pads nobody holds back to heath (an F2-19 shot may have painted them).
	_stage_pads({})
	_manager.visible = true
	_manager.call("_on_state_updated")
	hide_overlays()
	await wait(0.3)


func _unit_record() -> Dictionary:
	var sim := get_node_or_null("/root/SimBridge")
	var out := {}
	for row: Dictionary in sim.get_facility_visuals():
		var id := str(row["unit_id"])
		var node: Node = _manager.call("visual_of", id)
		var rec := {"variant": row["variant"], "plot_id": row["plot_id"], "phase_key": row["phase_key"],
			"phase_index": row["phase_index"], "phase_progress": snappedf(float(row["phase_progress"]), 0.001),
			"engine_tanks": row["tanks"]}
		if node is LandCampusBuilder:
			var campus := node as LandCampusBuilder
			rec["lots"] = row["lots"]
			rec["drawn_tanks"] = campus.drawn_tanks("tank_growout") + campus.drawn_tanks("tank_pregrow")
			rec["tank_progress"] = snappedf(campus.get_tank_progress(), 0.001)
			rec["lod0_triangles"] = campus.lod0_triangles()
			rec["shadow_rule_ok"] = shadow_rule_ok(campus.shadow_report())
		elif node is OffshoreUnitVisual:
			var unit := node as OffshoreUnitVisual
			rec["drawn_phase"] = unit.get_phase()
			rec["unit_visible"] = unit.unit.visible
			rec["fleet"] = unit.fleet_assets()
			rec["shadow_rule_ok"] = shadow_rule_ok(unit.shadow_report())
		out[id] = rec
	return out


## Mean CIE Lab over each pad's top in the frame: a grid of points on the pad
## (y = pad level) projected through the camera; points off screen or behind
## the camera are skipped.
func _measure_pads(img: Image) -> Dictionary:
	var out := {}
	var data := img.get_data()
	var w := img.get_width()
	var h := img.get_height()
	for pad: Dictionary in _terrain.call("land_pad_slots"):
		var c: Vector3 = pad["centre"]
		var size: Vector2 = pad["size"]
		var sum := Vector3.ZERO
		var n := 0
		for i in PAD_SAMPLES.x:
			for j in PAD_SAMPLES.y:
				var p := Vector3(
					c.x - size.x * 0.5 + PAD_INSET_M + (size.x - 2.0 * PAD_INSET_M) * (float(i) + 0.5) / float(PAD_SAMPLES.x),
					c.y,
					c.z - size.y * 0.5 + PAD_INSET_M + (size.y - 2.0 * PAD_INSET_M) * (float(j) + 0.5) / float(PAD_SAMPLES.y))
				if _cam.is_position_behind(p):
					continue
				var s := _cam.unproject_position(p)
				var x := int(s.x)
				var y := int(s.y)
				if x < 0 or y < 0 or x >= w or y >= h:
					continue
				sum += lab(data, (y * w + x) * 4)
				n += 1
		if n > 0:
			var m := sum / float(n)
			out[str(pad["id"])] = {"samples": n, "lab": [snappedf(m.x, 0.01), snappedf(m.y, 0.01), snappedf(m.z, 0.01)]}
	return out


## For every shot pair (X_heath, X_granite) the ΔE76 of each pad's mean colour.
func _compare_pads() -> void:
	for sid: String in _results.keys():
		if not sid.ends_with("_heath"):
			continue
		var other := sid.trim_suffix("_heath") + "_granite"
		if not _results.has(other) or not _results[sid].has("pads") or not _results[other].has("pads"):
			continue
		var delta := {}
		for pad_id: String in _results[sid]["pads"]:
			if not _results[other]["pads"].has(pad_id):
				continue
			var a: Array = _results[sid]["pads"][pad_id]["lab"]
			var b: Array = _results[other]["pads"][pad_id]["lab"]
			delta[pad_id] = snappedf(Vector3(a[0], a[1], a[2]).distance_to(Vector3(b[0], b[1], b[2])), 0.01)
		_results[other]["delta_e_vs_heath"] = delta


## Each shot that is a variant_of another: the Hamming distance of their pHashes.
func _compare_variants(shots: Array) -> void:
	for shot in shots:
		var sid := str(shot["id"])
		var base := str(shot.get("variant_of", ""))
		if base == "" or not _results.has(sid) or not _results.has(base):
			continue
		_results[sid]["phash_distance_vs_" + base] = phash_distance(str(_results[sid]["phash"]), str(_results[base]["phash"]))


## Spec §5 rule 5 / F2-18: LOD2 casts no shadow; of LOD1 only the tanks do.
static func shadow_rule_ok(report: Array) -> bool:
	for e: Dictionary in report:
		if bool(e["casts"]) and (int(e["band"]) >= 2 or (int(e["band"]) == 1 and str(e["kind"]) != "tank")):
			return false
	return true


## Draw calls the presenter adds (opaque and shadow), hidden / shown. With
## `units` (QA-set kinds: "land", "post_smolt", "offshore") only those units
## are shown in the "on" frame, so one unit's own cost is measured.
func _perf(units: Array = []) -> Dictionary:
	var vp := get_viewport()
	var out := {}
	var only := {}
	for kind in units:
		only[str(_units.get(str(kind), ""))] = true
	for mode in ["off", "on"]:
		if only.is_empty():
			_manager.visible = mode == "on"
		else:
			_manager.visible = true
			for id: String in _manager.get("visuals"):
				(_manager.call("visual_of", id) as Node3D).visible = mode == "on" and only.has(id)
		for i in 8:
			await RenderingServer.frame_post_draw
		out[mode] = {
			"opaque_draws": vp.get_render_info(Viewport.RENDER_INFO_TYPE_VISIBLE, Viewport.RENDER_INFO_DRAW_CALLS_IN_FRAME),
			"shadow_draws": vp.get_render_info(Viewport.RENDER_INFO_TYPE_SHADOW, Viewport.RENDER_INFO_DRAW_CALLS_IN_FRAME),
			"primitives": vp.get_render_info(Viewport.RENDER_INFO_TYPE_VISIBLE, Viewport.RENDER_INFO_PRIMITIVES_IN_FRAME),
		}
	_manager.visible = true
	for id: String in _manager.get("visuals"):
		(_manager.call("visual_of", id) as Node3D).visible = true
	if not only.is_empty():
		out["units"] = units
	out["added_opaque_draws"] = int(out["on"]["opaque_draws"]) - int(out["off"]["opaque_draws"])
	out["added_shadow_draws"] = int(out["on"]["shadow_draws"]) - int(out["off"]["shadow_draws"])
	return out


# ── Perceptual hash ──────────────────────────────────────────────────────────

## The frame's 64-bit pHash as 16 hex digits: luma at 32 x 32, the 2-D DCT-II,
## its 8 x 8 low-frequency corner (DC left out of the median), each bit set
## where the coefficient is above that median.
static func phash_hex(img: Image) -> String:
	var small := img.duplicate() as Image
	small.convert(Image.FORMAT_RGB8)
	small.resize(PHASH_SIZE, PHASH_SIZE, Image.INTERPOLATE_BILINEAR)
	var n := PHASH_SIZE
	var lum := PackedFloat32Array()
	lum.resize(n * n)
	for y in n:
		for x in n:
			var c := small.get_pixel(x, y)
			lum[y * n + x] = 0.299 * c.r + 0.587 * c.g + 0.114 * c.b
	var coeffs: Array[float] = []
	for v in PHASH_LOW:
		for u in PHASH_LOW:
			var s := 0.0
			for y in n:
				var cy := cos((2.0 * y + 1.0) * v * PI / (2.0 * n))
				for x in n:
					s += lum[y * n + x] * cos((2.0 * x + 1.0) * u * PI / (2.0 * n)) * cy
			coeffs.append(s)
	var rest := coeffs.slice(1)
	rest.sort()
	var median: float = (float(rest[rest.size() / 2 - 1]) + float(rest[rest.size() / 2])) * 0.5
	var hex := ""
	for nibble in 16:
		var value := 0
		for b in 4:
			if coeffs[nibble * 4 + b] > median:
				value |= 1 << (3 - b)
		hex += "%x" % value
	return hex


## Hamming distance between two 16-hex-digit hashes.
static func phash_distance(a: String, b: String) -> int:
	var d := 0
	for i in mini(a.length(), b.length()):
		var x := a.substr(i, 1).hex_to_int() ^ b.substr(i, 1).hex_to_int()
		while x != 0:
			d += x & 1
			x >>= 1
	return d
