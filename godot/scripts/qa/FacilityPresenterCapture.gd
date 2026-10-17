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
## Output: res://screenshots/game/<id>.png and 43_facilities_metrics.json.
## Run: tools/capture_screenshots.sh --scene res://qa/scenes/FacilityPresenterCapture.tscn

const SCENE_PATH := "res://qa/scenes/FacilityPresenterCapture.tscn"
const METRICS_FILE := "43_facilities_metrics.json"
## Points per pad side sampled for the pad colour (inset 12 m from the edge).
const PAD_SAMPLES := Vector2i(24, 16)
const PAD_INSET_M := 12.0

@export var auto_run := true

var _terrain: Node = null
var _results := {}


func _ready() -> void:
	if auto_run:
		start()


func scene_path() -> String:
	return SCENE_PATH


func run_shots(shots: Array) -> void:
	_terrain = _env.get_node_or_null("Terrain")
	for shot in shots:
		var sid := str(shot["id"])
		print("[Facilities] shot %s" % sid)
		stage_environment(shot)
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
		}
		if shot.has("pad_state"):
			rec["pad_state"] = shot["pad_state"]
		if bool(shot.get("measure_pads", false)):
			rec["pads"] = _measure_pads(beauty)
		_results[sid] = rec
	_compare_pads()
	var size := get_viewport().get_visible_rect().size
	var path := write_json(METRICS_FILE, {
		"items": ["F2-19", "F2-20"], "generated_by": "res://scripts/qa/FacilityPresenterCapture.gd",
		"resolution": [int(size.x), int(size.y)], "shots": _results,
	})
	print("[Facilities] metrics: %s" % path)
	print("FACILITIES_RESULT shots=%d" % _results.size())
	get_tree().quit(0)


## A plot's centre (pad top or waterline) from the terrain's own slots.
func _plot_focus(plot_id: String) -> Vector3:
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
