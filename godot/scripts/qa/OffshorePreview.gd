extends "res://scripts/qa/QaWorldCapture.gd"
## F2-26/F2-27 offshore preview: the OF1-class unit built by
## blender/build_offshore.py (godot/assets/models/offshore/of1_class.tscn)
## floats at plot off_1 of the real game world (spec §4), with the offshore
## fleet built by blender/build_vessels.py beside it: the DP service vessel
## at the unit's walk-to-work landing, a crew boat at the boat landing and
## an anchor tug standing off (shot "fleet": true), or the whole fleet in a
## row for scale (shot "lineup": true).
##
## The shots in godot/qa/shots.json for this scene (40_offshore_*) set
## season, hour and weather (ref O3's low winter sun is the hero mood), the
## camera (spec §4's OF1 framing 220 m at -18 deg, closer looks, the home
## view), the unit's build stage (the yard reveal) and the nav lights.
## Per shot it records the camera, the sun, the unit's LOD, the ring's share
## of the frame width from a mask pass (spec §4: 25-45% at the framing), and
## with "perf" the draw calls the unit and its fleet add (A/B).
## Output: res://screenshots/game/40_offshore_*.png and
## 40_offshore_metrics.json.
## Run: tools/capture_screenshots.sh --scene res://qa/scenes/OffshorePreview.tscn

const LAYOUT := preload("res://scripts/qa/LandCampusLayout.gd")
const UNIT_SCENE := preload("res://assets/models/offshore/of1_class.tscn")
const KIT := preload("res://assets/models/facility_kit/LK_OffshoreKit.tres")
const SCENE_PATH := "res://qa/scenes/OffshorePreview.tscn"
const METRICS_FILE := "40_offshore_metrics.json"
const FLEET := {
	"offshore_service": "res://assets/models/offshore_service/offshore_service.tscn",
	"anchor_tug": "res://assets/models/offshore_service/anchor_tug.tscn",
	"crew_boat": "res://assets/models/crew_boat/crew_boat.tscn",
}
const MASK_UNIT := Color(1, 1, 0)

@export var auto_run := true

var _unit: OffshoreUnitModel = null
var _fleet: Node3D = null
var _results := {}


func _ready() -> void:
	if auto_run:
		start()


func scene_path() -> String:
	return SCENE_PATH


func run_shots(shots: Array) -> void:
	var site := LAYOUT.plot_centre("off_1")
	for shot in shots:
		var sid := str(shot["id"])
		print("[Offshore] shot %s" % sid)
		stage_environment(shot)
		_place(site, shot)
		var cam_spec: Dictionary = shot.get("camera", {"mode": "home"})
		var focus := site
		if cam_spec.has("offset"):
			focus += Vector3(float(cam_spec["offset"][0]), 0.0, float(cam_spec["offset"][1]))
		focus = stage_camera(cam_spec, focus)
		KIT.set_shader_parameter("nav_lights", float(shot.get("nav_lights", 0.0)))
		KIT.set_shader_parameter("night_light", float(shot.get("night_light", 0.0)))
		await wait(2.0)
		var beauty := await grab()
		save(beauty, sid + ".png")
		var dist := _cam.global_position.distance_to(site)
		var rec := {
			"camera": {"spec": cam_spec, "position": [_cam.global_position.x, _cam.global_position.y, _cam.global_position.z],
				"fov_deg": snappedf(_cam.fov, 0.01), "distance_to_unit_m": snappedf(dist, 0.1)},
			"environment": {"sun_elevation_deg": snappedf(float(_director.call("get_composed").get("sun_elevation", 0.0)), 0.01),
				"weather": str(shot.get("weather", "clear")), "nav_lights": float(shot.get("nav_lights", 0.0))},
			"unit": {"lod": 0 if dist < _unit.lod_switch.x else (1 if dist < _unit.lod_switch.y else 2),
				"build_stage": _unit.get_build_stage()},
		}
		if bool(shot.get("measure", false)):
			rec["ring"] = await _measure_ring()
		if bool(shot.get("perf", false)):
			rec["perf"] = await _perf()
		_results[sid] = rec
	KIT.set_shader_parameter("nav_lights", 0.0)
	KIT.set_shader_parameter("night_light", 0.0)
	var size := get_viewport().get_visible_rect().size
	var path := write_json(METRICS_FILE, {
		"item": "F2-26/F2-27", "generated_by": "res://scripts/qa/OffshorePreview.gd",
		"resolution": [int(size.x), int(size.y)], "shots": _results,
	})
	print("[Offshore] metrics: %s" % path)
	print("OFFSHORE_RESULT shots=%d" % _results.size())
	get_tree().quit(0)


# ── Staging ──────────────────────────────────────────────────────────────────

func _place(site: Vector3, shot: Dictionary) -> void:
	if _unit == null:
		_unit = UNIT_SCENE.instantiate() as OffshoreUnitModel
		_unit.name = "OF1Class"
		_env.add_child(_unit)
	_unit.global_position = site
	_unit.rotation.y = deg_to_rad(float(shot.get("unit_yaw_deg", 0.0)))
	_unit.set_build_stage(float(shot.get("build_stage", 6.0)))
	_unit.set_bulkhead_angle(float(shot.get("bulkhead_deg", 0.0)))
	if _fleet and is_instance_valid(_fleet):
		_fleet.get_parent().remove_child(_fleet)
		_fleet.free()
	_fleet = Node3D.new()
	_fleet.name = "OffshoreFleet"
	_env.add_child(_fleet)
	if bool(shot.get("lineup", false)):
		_lineup(site)
	elif bool(shot.get("fleet", false)):
		_moor_fleet(bool(shot.get("tug", false)))


## The service vessel lies alongside the walk-to-work landing, bow along
## the ring; the crew boat noses into the boat landing; with "tug" the
## anchor tug stands off.
func _moor_fleet(tug: bool) -> void:
	var landing := _unit.to_global(_unit.get_w2w_landing())
	var out := (landing - _unit.global_position)
	out.y = 0.0
	out = out.normalized()
	var along := out.cross(Vector3.UP)
	var service := _vessel("offshore_service")
	# Beam 17 m: the hull's side 6 m off the ring, the gangway spans the gap.
	service.global_position = Vector3(landing.x, 0.0, landing.z) + out * (6.0 + 8.5) + along * 6.0
	service.look_at(service.global_position - along, Vector3.UP)
	var crew := _vessel("crew_boat")
	var boat_landing := _unit.global_position + (Vector3(-_unit.circumradius_m, 0.0, 0.0)).rotated(Vector3.UP, _unit.rotation.y)
	var b_out := (boat_landing - _unit.global_position).normalized()
	# Bow fender on the landing's fender posts, square to the column.
	crew.global_position = boat_landing + b_out * 15.0
	crew.look_at(crew.global_position - b_out, Vector3.UP)
	if tug:
		var t := _vessel("anchor_tug")
		t.global_position = _unit.global_position + out.rotated(Vector3.UP, 0.9) * 230.0
		t.look_at(_unit.global_position, Vector3.UP)


## The fleet broadside beside the unit, one behind the other (scale read:
## the 75 m service vessel against the 110 m ring, the red anchor tug on the
## same mesh, the 20 m crew boat).
func _lineup(site: Vector3) -> void:
	var rows := {"offshore_service": 30.0, "anchor_tug": -30.0, "crew_boat": 75.0}
	for id in rows:
		var v := _vessel(id)
		var length: float = v.get("length_m") if v.get("length_m") != null else 20.0
		v.global_position = Vector3(site.x + 55.0 + 20.0 + length * 0.5, 0.0, site.z + float(rows[id]))
		v.look_at(v.global_position + Vector3.RIGHT, Vector3.UP)


## A fleet vessel under the fleet node (a plain marker if its scene is
## missing, so the unit's shots still run).
func _vessel(id: String) -> Node3D:
	var v: Node3D = null
	if ResourceLoader.exists(FLEET[id]):
		v = (load(FLEET[id]) as PackedScene).instantiate() as Node3D
	else:
		push_warning("[Offshore] %s is not built yet" % FLEET[id])
		v = Node3D.new()
	v.name = id
	_fleet.add_child(v)
	return v


# ── Measuring ────────────────────────────────────────────────────────────────

## The unit's horizontal extent on screen from an unshaded mask pass.
func _measure_ring() -> Dictionary:
	var saved := begin_plain([_env.get_node_or_null("WaterGrid"), _unit])
	var mask := unshaded(MASK_UNIT)
	var black := unshaded(Color.BLACK)
	var water := _env.get_node_or_null("WaterGrid")
	var overrides := []
	for gi in _unit.find_children("*", "GeometryInstance3D", true, false):
		overrides.append([gi, gi.material_override])
		gi.material_override = mask
	if water:
		for gi in water.find_children("*", "GeometryInstance3D", true, false):
			overrides.append([gi, gi.material_override])
			gi.material_override = black
	var img := await grab()
	for o in overrides:
		o[0].material_override = o[1]
	end_plain(saved)
	var w := img.get_width()
	var h := img.get_height()
	var d := img.get_data()
	var x0 := w
	var x1 := -1
	var y0 := h
	var y1 := -1
	var pixels := 0
	for y in h:
		for x in w:
			var o := (y * w + x) * 4
			if d[o] > 200 and d[o + 1] > 200 and d[o + 2] < 60:
				pixels += 1
				x0 = mini(x0, x)
				x1 = maxi(x1, x)
				y0 = mini(y0, y)
				y1 = maxi(y1, y)
	if pixels == 0:
		return {"pixels": 0}
	return {"pixels": pixels, "width_px": x1 - x0 + 1, "height_px": y1 - y0 + 1,
		"width_frac": snappedf(float(x1 - x0 + 1) / float(w), 0.001), "top_px": y0}


## Draw calls (opaque and shadow) and primitives the unit adds alone, with
## its service vessel (the spec §5.5 budget pair), and with the whole fleet
## of the shot: A/B against nothing drawn.
func _perf() -> Dictionary:
	var vp := get_viewport()
	var out := {}
	var service := _fleet.get_node_or_null("offshore_service") as Node3D
	for mode in ["off", "unit", "unit_service", "all"]:
		_unit.visible = mode != "off"
		_fleet.visible = mode in ["unit_service", "all"]
		for v in _fleet.get_children():
			(v as Node3D).visible = mode == "all" or v == service
		for i in 8:
			await RenderingServer.frame_post_draw
		out[mode] = {
			"opaque_draws": vp.get_render_info(Viewport.RENDER_INFO_TYPE_VISIBLE, Viewport.RENDER_INFO_DRAW_CALLS_IN_FRAME),
			"shadow_draws": vp.get_render_info(Viewport.RENDER_INFO_TYPE_SHADOW, Viewport.RENDER_INFO_DRAW_CALLS_IN_FRAME),
			"primitives": vp.get_render_info(Viewport.RENDER_INFO_TYPE_VISIBLE, Viewport.RENDER_INFO_PRIMITIVES_IN_FRAME),
		}
	_unit.visible = true
	_fleet.visible = true
	for v in _fleet.get_children():
		(v as Node3D).visible = true
	for mode in ["unit", "unit_service", "all"]:
		out["added_" + mode] = {
			"opaque_draws": int(out[mode]["opaque_draws"]) - int(out["off"]["opaque_draws"]),
			"shadow_draws": int(out[mode]["shadow_draws"]) - int(out["off"]["shadow_draws"]),
			"primitives": int(out[mode]["primitives"]) - int(out["off"]["primitives"]),
		}
	return out
