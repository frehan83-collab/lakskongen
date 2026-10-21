extends "res://scripts/qa/FacilityPresenterCapture.gd"
## F2-21 / F2-23 capture: the farm types' showcase shots and the first-sight
## sequences on the real game world (godot/qa/shots.json, the shots naming
## res://qa/scenes/ShowcaseCapture.tscn), with their content checks.
##
## Staging on top of the facility presenter's (FacilityPresenterCapture):
##   "sun_elevation_deg": n   the presentation hour of the shot's day when the
##     sun, going down, stands there (FirstSightDirector.hour_for_sun), for
##     the dusk and low-sun shots on any day;
##   "open_sea": {...}        the open sea's target (EnvironmentDirector.
##     set_target_sea: hs, wind_sea_hs, swell_total_hs, swell_tp), staged as
##     a weather is; the presenter's own target from the engine is held;
##   camera "target": "unit:<key>" | "plot:<id>"  the camera rig's own
##     framing of a unit or a plot, as G or a click takes the player there;
##   camera "first_sight": {"target": "unit:<key>", "t": s, "reduced": b}
##     the unit's first-sight sequence stood at s seconds;
##   camera "teaser": {"t": s, "reduced": b} the teaser stood at s seconds.
## Content checks ("checks"): "campus" (every corner of the campus in the
## frame; its screen box's pHash), "orange" (the pipe bridge's orange in the
## campus's box), "lights_ab" (the frame with the lights held off and on: lit
## pixels and the campus box's luminance), "sky" (the frame's top band),
## "of1" (hull-yellow pixels, the open water's saturation), "storm" (the
## drawn open sea, whitecaps), "chip" (the unit's sea chip on screen),
## "landmarks" (at the home view: OF1 hull pixels and the campus
## silhouette's lightness against the strandflat), "first_sight" (the
## sequence's state), "teaser" (the rival's hull on screen: its box and
## hull-yellow pixels; the teaser's state).
##
## Output: res://screenshots/game/<id>.png and f2_23_showcase_metrics.json
## (merged over runs). Run: tools/capture_screenshots.sh --scene
## res://qa/scenes/ShowcaseCapture.tscn [-- --only <prefix>]

const SHOWCASE_SCENE := "res://qa/scenes/ShowcaseCapture.tscn"
const SHOWCASE_METRICS := "f2_23_showcase_metrics.json"
## The pipe bridge's orange (#D9622B, hue 0.05) and the lamps' warm light.
const ORANGE_HUE := Vector2(0.015, 0.085)
const ORANGE_MIN_SV := Vector2(0.45, 0.3)
const LIT_HUE := Vector2(0.03, 0.17)
const LIT_MIN_SV := Vector2(0.2, 0.72)
## The sky band the "sky" check reads (share of the frame from the top).
const SKY_BAND := 0.1
## A whitecap is water this light (CIE L*).
const FOAM_L := 62.0
## Margin (share) round a unit's box kept out of the open-water reads.
const BOX_MARGIN := 0.12
## The hull's hue as it reads far off (spec F2-23 accept 5's hull-hue
## pixels): the swatch's hue (0.13) within a band, tinted at least 0.15 (the
## fogged grey sea and sky read 0.03-0.08).
const HOME_HULL_HUE := Vector2(0.08, 0.18)
const HOME_HULL_MIN_SV := Vector2(0.15, 0.3)

var _showcase := {}


func scene_path() -> String:
	return SHOWCASE_SCENE


func run_shots(shots: Array) -> void:
	_terrain = _env.get_node_or_null("Terrain")
	_manager = _env.get_node_or_null("FarmFacilityVisualManager")
	for shot in shots:
		var sid := str(shot["id"])
		print("[Showcase] shot %s" % sid)
		var cam_spec: Dictionary = shot.get("camera", {"mode": "home"})
		var mode := str(cam_spec.get("mode", ""))
		var sequence := mode == "first_sight" or mode == "teaser"
		var fs: FirstSightDirector = _manager.get("first_sight")
		var td: TeaserDirector = _manager.get("teaser")
		var continues := mode == "first_sight" and fs.playing and fs.unit_id == _target_id(str(cam_spec.get("target", ""))) \
			or mode == "teaser" and td.playing and td.reduced == bool(cam_spec.get("reduced", false))
		if not continues:
			_end_sequence()
			stage_environment(shot)
		if shot.has("facilities") and not await _stage_units(shot):
			push_error("[Showcase] %s: the QA set could not be staged; skipped" % sid)
			continue
		if not sequence:
			_stage_sun(shot)
			_stage_open_sea(shot)
			hide_overlays()
		await _stage_showcase_camera(cam_spec)
		await wait(2.0 if not sequence else 0.6)
		_director.stage_cloud_drift(Vector2.ZERO)
		var beauty := await grab()
		var rec := {"camera": {"spec": cam_spec, "position": [_cam.global_position.x, _cam.global_position.y, _cam.global_position.z],
			"fov_deg": snappedf(_cam.fov, 0.01)},
			"environment": {"sun_elevation_deg": snappedf(float(_director.call("get_composed").get("sun_elevation", 0.0)), 0.01),
				"mood": str(_director.call("get_composed").get("mood", "")), "weather": str(shot.get("weather", "clear")),
				"light_level": snappedf(float(_manager.call("light_level")), 0.01)},
			"phash": phash_hex(beauty)}
		if shot.has("facilities"):
			rec["units"] = _unit_record(beauty)
		for what: String in shot.get("checks", []):
			match what:
				"campus":
					rec["campus"] = _measure_campus(str(cam_spec.get("target", "unit:land")), beauty)
				"orange":
					rec["orange"] = _count_in_campus(str(cam_spec.get("target", "unit:land")), beauty, ORANGE_HUE, ORANGE_MIN_SV)
				"lights_ab":
					var ab := await _lights_ab(str(cam_spec.get("target", "unit:land")))
					rec["lights_ab"] = ab["metrics"]
					beauty = ab["on"]
				"sky":
					rec["sky"] = _sky(beauty)
				"of1":
					rec["of1"] = _measure_of1(beauty)
				"storm":
					rec["storm"] = _measure_storm(beauty)
				"chip":
					rec["chip"] = _measure_chip()
				"landmarks":
					rec["landmarks"] = await _measure_landmarks(beauty)
				"first_sight":
					rec["first_sight"] = _measure_sequence(beauty)
				"teaser":
					rec["teaser"] = _measure_teaser(beauty)
		save(beauty, sid + ".png")
		_showcase[sid] = rec
	_end_sequence()
	_compare_showcase(shots)
	var size := get_viewport().get_visible_rect().size
	var merged := _previous_showcase()
	merged.merge(_showcase, true)
	var path := write_json(SHOWCASE_METRICS, {
		"items": ["F2-21", "F2-23"], "generated_by": "res://scripts/qa/ShowcaseCapture.gd",
		"resolution": [int(size.x), int(size.y)], "shots": merged,
	})
	print("[Showcase] metrics: %s" % path)
	print("SHOWCASE_RESULT shots=%d" % _showcase.size())
	get_tree().quit(0)


func _previous_showcase() -> Dictionary:
	var path := ProjectSettings.globalize_path(OUT_DIR).path_join(SHOWCASE_METRICS)
	if not FileAccess.file_exists(path):
		return {}
	var data = JSON.parse_string(FileAccess.get_file_as_string(path))
	return (data.get("shots", {}) as Dictionary).duplicate(true) if data is Dictionary else {}


# ── Staging ──────────────────────────────────────────────────────────────────

func _stage_sun(shot: Dictionary) -> void:
	if not shot.has("sun_elevation_deg"):
		return
	var ts: TimeOfDay = _env.get("time_system")
	var fs: FirstSightDirector = _manager.get("first_sight")
	_env.call("set_time", fs.hour_for_sun(ts.latitude_deg, ts.sim_day, float(shot["sun_elevation_deg"])))


func _stage_open_sea(shot: Dictionary) -> void:
	_manager.set("sea_target_locked", shot.has("open_sea"))
	if shot.has("open_sea"):
		var sea: Dictionary = (shot["open_sea"] as Dictionary).duplicate()
		sea["plot_id"] = "off_1"
		_director.call("set_target_sea", sea)
		_director.call("compose", 0.0, true)
		# The unit's chip reads the staged sea too (the engine's own is checked headless).
		var unit := _manager.call("visual_of", str(_units.get("offshore", ""))) as OffshoreUnitVisual
		if unit != null and not unit.sea.is_empty():
			var staged: Dictionary = unit.sea.duplicate(true)
			var today: Dictionary = staged.get("today", {})
			today["hs"] = float(sea["hs"])
			today["tp"] = float(sea["swell_tp"])
			today["swell_hs"] = float(sea["swell_total_hs"])
			staged["today"] = today
			var days: Array = staged.get("days", [])
			if not days.is_empty():
				(days[0] as Dictionary)["hs_p50"] = float(sea["hs"]) * 0.85
			unit.set_sea(staged)
	else:
		_manager.set("_seas_dirty", true)


## A unit or plot target: the camera rig's own framing, settled.
func _target_id(target: String) -> String:
	if target.begins_with("unit:"):
		return str(_units.get(target.trim_prefix("unit:"), ""))
	return target


func _stage_showcase_camera(spec: Dictionary) -> void:
	var mode := str(spec.get("mode", "home"))
	if mode == "target":
		_cam.set_process(true)
		_cam.call("set_top_down", false)
		if not _cam.call("focus_target", _target_id(str(spec.get("target", ""))), 0.0):
			push_error("[Showcase] no camera target %s" % spec.get("target", ""))
		for i in 8:
			_cam.call("_process", 2.0)
		return
	if mode == "first_sight":
		var fs: FirstSightDirector = _manager.get("first_sight")
		var id := _target_id(str(spec.get("target", "unit:land")))
		if not fs.playing or fs.unit_id != id:
			_end_sequence()
			MotionPrefs.force(bool(spec.get("reduced", false)))
			_cam.set_process(true)
			if not fs.play(id):
				push_error("[Showcase] the first sight of %s did not start" % id)
				return
			fs.manual = true
			hide_overlays()
		fs.seek(float(spec.get("t", 0.0)))
		return
	if mode == "teaser":
		var td: TeaserDirector = _manager.get("teaser")
		if not td.playing or td.reduced != bool(spec.get("reduced", false)):
			_end_sequence()
			MotionPrefs.force(bool(spec.get("reduced", false)))
			_cam.set_process(true)
			if not td.play():
				push_error("[Showcase] the teaser did not start")
				return
			td.manual = true
			hide_overlays()
		td.seek(float(spec.get("t", 0.0)))
		return
	var focus := _plot_focus(str(spec.get("target", LAND_PLOT)))
	stage_camera(spec, focus)


func _end_sequence() -> void:
	var fs: FirstSightDirector = _manager.get("first_sight") if _manager != null else null
	if fs != null and fs.playing:
		fs._finish(true)
		MotionPrefs.force(null)
		hide_overlays()
	var td: TeaserDirector = _manager.get("teaser") if _manager != null else null
	if td != null and td.playing:
		td._finish(true)
		MotionPrefs.force(null)
		hide_overlays()


# ── Measurements ─────────────────────────────────────────────────────────────

func _campus(target: String) -> LandCampusBuilder:
	return _manager.call("visual_of", _target_id(target)) as LandCampusBuilder


func _measure_campus(target: String, img: Image) -> Dictionary:
	var campus := _campus(target)
	if campus == null:
		return {}
	var size := Vector2(img.get_width(), img.get_height())
	var inside := 0
	var corners := FarmFacilityVisualManager.box_corners(campus.blast_rect(), LandCampusBuilder.PAD_TOP_Y, 16.0)
	for p: Vector3 in corners:
		var s := _cam.unproject_position(p)
		if not _cam.is_position_behind(p) and s.x >= 0.0 and s.y >= 0.0 and s.x <= size.x and s.y <= size.y:
			inside += 1
	var out := {"corners_in_frame": inside, "corners": corners.size(),
		"pitch_deg": snappedf(rad_to_deg(asin((-_cam.global_transform.basis.z).y)), 0.1),
		"distance_m": snappedf(_cam.global_position.distance_to(campus.focus_point()), 0.1)}
	var roi := _campus_roi_of(campus, img)
	if roi.has_area():
		out["roi"] = [roi.position.x, roi.position.y, roi.end.x, roi.end.y]
		out["phash_roi"] = phash_hex(img.get_region(roi))
	return out


## The campus's screen box (its blasted ground up to ROI_HEIGHT_M), clamped.
func _campus_roi_of(campus: LandCampusBuilder, img: Image) -> Rect2i:
	var r := campus.blast_rect()
	var lo := Vector2(INF, INF)
	var hi := Vector2(-INF, -INF)
	for x: float in [r.x, r.z]:
		for z: float in [r.y, r.w]:
			for y: float in [LandCampusBuilder.PAD_TOP_Y, LandCampusBuilder.PAD_TOP_Y + ROI_HEIGHT_M]:
				var p := Vector3(x, y, z)
				if _cam.is_position_behind(p):
					continue
				var s := _cam.unproject_position(p)
				lo = Vector2(minf(lo.x, s.x), minf(lo.y, s.y))
				hi = Vector2(maxf(hi.x, s.x), maxf(hi.y, s.y))
	if not lo.is_finite():
		return Rect2i()
	var x0 := clampi(int(lo.x), 0, img.get_width() - 1)
	var y0 := clampi(int(lo.y), 0, img.get_height() - 1)
	var x1 := clampi(int(hi.x), 0, img.get_width())
	var y1 := clampi(int(hi.y), 0, img.get_height())
	return Rect2i(x0, y0, maxi(0, x1 - x0), maxi(0, y1 - y0))


## Pixels in the campus's box whose hue lies in `hue` with at least `min_sv`.
func _count_in_campus(target: String, img: Image, hue: Vector2, min_sv: Vector2) -> Dictionary:
	var campus := _campus(target)
	if campus == null:
		return {"pixels": 0}
	var roi := _campus_roi_of(campus, img)
	var n := 0
	for y in range(roi.position.y, roi.end.y):
		for x in range(roi.position.x, roi.end.x):
			var c := img.get_pixel(x, y)
			if c.h >= hue.x and c.h <= hue.y and c.s >= min_sv.x and c.v >= min_sv.y:
				n += 1
	return {"pixels": n, "roi_pixels": roi.get_area()}


## The campus with its lights held off, then on: lit pixels and the box's
## mean luminance in each; the "on" frame is the shot's beauty.
func _lights_ab(target: String) -> Dictionary:
	var out := {}
	var frames := {}
	for mode: String in ["off", "on"]:
		_manager.set("light_override", 0.0 if mode == "off" else 1.0)
		_manager.call("_update_lights")
		for i in 6:
			await RenderingServer.frame_post_draw
		var img := await grab()
		frames[mode] = img
		var lit := _count_in_campus(target, img, LIT_HUE, LIT_MIN_SV)
		out[mode] = {"lit_pixels": lit["pixels"], "roi_luminance": snappedf(_roi_luminance(target, img), 0.0001)}
	_manager.set("light_override", -1.0)
	_manager.call("_update_lights")
	out["lit_pixels_added"] = int(out["on"]["lit_pixels"]) - int(out["off"]["lit_pixels"])
	return {"metrics": out, "on": frames["on"]}


func _roi_luminance(target: String, img: Image) -> float:
	var campus := _campus(target)
	if campus == null:
		return 0.0
	var roi := _campus_roi_of(campus, img)
	var sum := 0.0
	var n := 0
	for y in range(roi.position.y, roi.end.y, 2):
		for x in range(roi.position.x, roi.end.x, 2):
			var c := img.get_pixel(x, y)
			sum += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b
			n += 1
	return sum / float(maxi(n, 1))


## The frame's top band: its mean colour (sRGB 0-255) and that colour's hue,
## saturation and value.
func _sky(img: Image) -> Dictionary:
	var h := int(img.get_height() * SKY_BAND)
	var sum := Vector3.ZERO
	var n := 0
	for y in range(0, h, 2):
		for x in range(0, img.get_width(), 4):
			var c := img.get_pixel(x, y)
			sum += Vector3(c.r, c.g, c.b)
			n += 1
	var m := sum / float(maxi(n, 1))
	var col := Color(m.x, m.y, m.z)
	return {"mean_rgb": [roundi(m.x * 255.0), roundi(m.y * 255.0), roundi(m.z * 255.0)],
		"hue": snappedf(col.h, 0.001), "saturation": snappedf(col.s, 0.001), "value": snappedf(col.v, 0.001)}


## The OF1 unit's screen box (ring and topsides) grown by `margin`.
func _of1_box(img: Image, margin: float) -> Rect2i:
	var unit := _manager.call("visual_of", str(_units.get("offshore", ""))) as OffshoreUnitVisual
	if unit == null or not unit.unit.visible:
		return Rect2i()
	var c: Vector3 = unit.unit.global_position
	var r: float = unit.unit.circumradius_m
	var lo := Vector2(INF, INF)
	var hi := Vector2(-INF, -INF)
	for k in 24:
		var a := TAU * float(k) / 24.0
		for y: float in [-2.0, unit.unit.air_draught_m]:
			var p := c + Vector3(cos(a) * r, y, sin(a) * r)
			if _cam.is_position_behind(p):
				continue
			var s := _cam.unproject_position(p)
			lo = Vector2(minf(lo.x, s.x), minf(lo.y, s.y))
			hi = Vector2(maxf(hi.x, s.x), maxf(hi.y, s.y))
	for child in unit.fleet.get_children():
		if child is Node3D:
			var vp := (child as Node3D).global_position
			for d: Vector3 in [Vector3(45, 0, 0), Vector3(-45, 0, 0), Vector3(0, 0, 45), Vector3(0, 0, -45), Vector3(0, 25, 0)]:
				if not _cam.is_position_behind(vp + d):
					var s := _cam.unproject_position(vp + d)
					lo = Vector2(minf(lo.x, s.x), minf(lo.y, s.y))
					hi = Vector2(maxf(hi.x, s.x), maxf(hi.y, s.y))
	if not lo.is_finite():
		return Rect2i()
	var grow := (hi - lo) * margin
	lo -= grow
	hi += grow
	var x0 := clampi(int(lo.x), 0, img.get_width() - 1)
	var y0 := clampi(int(lo.y), 0, img.get_height() - 1)
	return Rect2i(x0, y0, maxi(0, clampi(int(hi.x), 0, img.get_width()) - x0), maxi(0, clampi(int(hi.y), 0, img.get_height()) - y0))


## Hull-yellow pixels (HULL_HUE, HULL_MIN_SV) inside the OF1's box, counted
## one by one; their share of the frame. Also the hull-hue pixels as they
## read far off through the fog (HOME_HULL_HUE, HOME_HULL_MIN_SV: the
## yellow's hue, tinted at least twice the grey sea and sky round it) and the
## mean saturation of the rest of the box.
func _hull_pixels(img: Image) -> Dictionary:
	var box := _of1_box(img, 0.0)
	var n := 0
	var far := 0
	var rest := 0.0
	var n_rest := 0
	for y in range(box.position.y, box.end.y):
		for x in range(box.position.x, box.end.x):
			var c := img.get_pixel(x, y)
			if c.h >= HULL_HUE.x and c.h <= HULL_HUE.y and c.s >= HULL_MIN_SV.x and c.v >= HULL_MIN_SV.y:
				n += 1
			if c.h >= HOME_HULL_HUE.x and c.h <= HOME_HULL_HUE.y and c.s >= HOME_HULL_MIN_SV.x and c.v >= HOME_HULL_MIN_SV.y:
				far += 1
			else:
				rest += c.s
				n_rest += 1
	return {"pixels": n, "frame_share": snappedf(float(n) / float(img.get_width() * img.get_height()), 0.00001),
		"hull_hue_pixels": far, "background_saturation": snappedf(rest / float(maxi(n_rest, 1)), 0.001),
		"box": [box.position.x, box.position.y, box.end.x, box.end.y]}


## The open water: every pixel under the horizon outside the unit's and its
## vessels' box (grown BOX_MARGIN): mean saturation, and the share as light
## as foam.
func _open_water(img: Image) -> Dictionary:
	var box := _of1_box(img, BOX_MARGIN)
	var forward := -_cam.global_transform.basis.z
	forward.y = 0.0
	var horizon := int(clampf(_cam.unproject_position(_cam.global_position + forward.normalized() * 100000.0).y + 6.0, 0.0, img.get_height() - 1))
	var data := img.get_data()
	var w := img.get_width()
	var sat := 0.0
	var foam := 0
	var n := 0
	for y in range(horizon, img.get_height(), 2):
		for x in range(0, w, 2):
			if box.has_point(Vector2i(x, y)):
				continue
			var c := img.get_pixel(x, y)
			sat += c.s
			if lab(data, (y * w + x) * 4).x >= FOAM_L:
				foam += 1
			n += 1
	return {"pixels_sampled": n, "mean_saturation": snappedf(sat / float(maxi(n, 1)), 0.001),
		"foam_share": snappedf(float(foam) / float(maxi(n, 1)), 0.0001), "horizon_row": horizon}


func _measure_of1(img: Image) -> Dictionary:
	var out := _hull_pixels(img)
	out["water"] = _open_water(img)
	return out


func _measure_storm(img: Image) -> Dictionary:
	var composed: Dictionary = _director.call("get_composed")
	var water := _open_water(img)
	return {"exposed_hs_m": snappedf(float(composed.get("sea_hs_exposed", 0.0)), 0.01),
		"swell_hs_m": snappedf(float(composed.get("swell_hs", 0.0)), 0.01),
		"open_sea_plot": str(composed.get("open_sea_plot", "")),
		"foam_share": water["foam_share"], "wind_ms": snappedf(float((_env.get("weather_system") as WeatherController).get_wind_speed_ms()), 0.1)}


func _measure_chip() -> Dictionary:
	var unit := _manager.call("visual_of", str(_units.get("offshore", ""))) as OffshoreUnitVisual
	if unit == null or unit.chip == null:
		return {"visible": false}
	unit._refresh_chip(unit._camera_distance())
	var at := unit.chip.global_position
	var s := _cam.unproject_position(at)
	var size := get_viewport().get_visible_rect().size
	return {"visible": unit.chip.visible, "text": unit.chip.get_text(), "shape": int(unit.chip.get_shape()),
		"on_screen": not _cam.is_position_behind(at) and s.x >= 0.0 and s.y >= 0.0 and s.x <= size.x and s.y <= size.y,
		"screen": [roundi(s.x), roundi(s.y)], "distance_m": snappedf(unit._camera_distance(), 1.0),
		"transmittance": snappedf(float(_director.call("transmittance", unit._camera_distance())), 0.0001)}


## At the home view: the OF1's hull pixels; the campus silhouette's mean
## lightness (L*) against the strandflat round it (land in its box grown by
## half, the campus out), both from masks drawn on the plain environment.
func _measure_landmarks(img: Image) -> Dictionary:
	var out := {"of1": _hull_pixels(img)}
	var campus := _campus("unit:land")
	if campus == null:
		return out
	var terrain := _env.get_node_or_null("Terrain")
	var water := _env.get_node_or_null("WaterGrid")
	var campus_mask := await _mask(campus, terrain, water, false)
	var land_mask := await _mask(campus, terrain, water, true)
	var data := img.get_data()
	var cm := campus_mask.get_data()
	var lm := land_mask.get_data()
	var w := img.get_width()
	var h := img.get_height()
	var lo := Vector2i(w, h)
	var hi := Vector2i(-1, -1)
	var sum_c := 0.0
	var n_c := 0
	for y in h:
		for x in w:
			var o := (y * w + x) * 4
			if cm[o] > 128:
				sum_c += lab(data, o).x
				n_c += 1
				lo = Vector2i(mini(lo.x, x), mini(lo.y, y))
				hi = Vector2i(maxi(hi.x, x), maxi(hi.y, y))
	if n_c == 0:
		out["campus"] = {"pixels": 0}
		return out
	var grow := Vector2i(int((hi.x - lo.x) * 0.5), int((hi.y - lo.y) * 0.5))
	# The strandflat: land round the campus, its own blasted pad left out.
	var pad := PackedVector2Array()
	var r := campus.blast_rect()
	for corner: Vector2 in [Vector2(r.x, r.y), Vector2(r.z, r.y), Vector2(r.z, r.w), Vector2(r.x, r.w)]:
		pad.append(_cam.unproject_position(Vector3(corner.x, LandCampusBuilder.PAD_TOP_Y, corner.y)))
	var sum_r := 0.0
	var n_r := 0
	for y in range(maxi(lo.y - grow.y, 0), mini(hi.y + grow.y, h - 1)):
		for x in range(maxi(lo.x - grow.x, 0), mini(hi.x + grow.x, w - 1)):
			var o := (y * w + x) * 4
			if lm[o] > 128 and cm[o] <= 128 and not Geometry2D.is_point_in_polygon(Vector2(x, y), pad):
				sum_r += lab(data, o).x
				n_r += 1
	var lc := sum_c / float(n_c)
	var lr := sum_r / float(maxi(n_r, 1))
	# The silhouette's contrast as F2-18 measured it: each campus pixel's
	# lightness against the strandflat's mean (light walls and the dark hall
	# both stand out; a difference of means lets them cancel).
	var abs_sum := 0.0
	for y in range(lo.y, hi.y + 1):
		for x in range(lo.x, hi.x + 1):
			var o := (y * w + x) * 4
			if cm[o] > 128:
				abs_sum += absf(lab(data, o).x - lr)
	out["campus"] = {"pixels": n_c, "strandflat_pixels": n_r, "campus_l": snappedf(lc, 0.01), "strandflat_l": snappedf(lr, 0.01),
		"delta_l": snappedf(abs_sum / float(n_c), 0.01), "delta_mean_l": snappedf(absf(lc - lr), 0.01), "box": [lo.x, lo.y, hi.x, hi.y]}
	return out


## A mask frame: the campus unshaded white (or, `land`, the terrain white and
## the campus black), everything else black or hidden; the terrain and the
## water still occlude.
func _mask(campus: LandCampusBuilder, terrain: Node, water: Node, land: bool) -> Image:
	var keep: Array = [campus, terrain, water]
	var saved := begin_plain(keep)
	var white := unshaded(Color.WHITE)
	var black := unshaded(Color.BLACK)
	var restore := {}
	for gi: GeometryInstance3D in campus.find_children("*", "GeometryInstance3D", true, false):
		restore[gi] = gi.material_override
		# The silhouette is what stands: the flat roads are ground, not campus.
		gi.material_override = black if land or String(gi.name).begins_with("roads_") else white
	if land and terrain != null:
		for gi: GeometryInstance3D in terrain.find_children("*", "GeometryInstance3D", true, false):
			restore[gi] = gi.material_override
			gi.material_override = white
	var img := await grab()
	for gi: GeometryInstance3D in restore:
		gi.material_override = restore[gi]
	end_plain(saved)
	return img


func _measure_sequence(img: Image) -> Dictionary:
	var fs: FirstSightDirector = _manager.get("first_sight")
	var o := fs.overlay
	var out := {"playing": fs.playing, "family": fs.family, "time_s": snappedf(fs.time, 0.01), "duration_s": snappedf(fs.duration, 0.01),
		"reduced": fs.reduced, "light_override": snappedf(float(_manager.get("light_override")), 0.01)}
	if o != null:
		out["bars"] = snappedf(o.bars[0].modulate.a, 0.01)
		out["black"] = snappedf(o.black.modulate.a, 0.01)
		out["card"] = snappedf(o.card.modulate.a if o.card.visible else 0.0, 0.01)
		out["still"] = snappedf(o.still.modulate.a if o.still.visible else 0.0, 0.01)
	if fs.family == "land":
		var campus := _manager.call("visual_of", fs.unit_id) as LandCampusBuilder
		if campus != null:
			out["whole_tanks"] = campus.whole_tanks()
			out["blast_state"] = snappedf(campus.blast_state(), 0.001)
	return out


## The teaser at its time: the rival's hull on screen (its box from the ring
## and the topsides, the hull-yellow and far-hull-hue pixels in it, its
## width in px and its distance), and the sequence's state.
func _measure_teaser(img: Image) -> Dictionary:
	var td: TeaserDirector = _manager.get("teaser")
	var out := {"playing": td.playing, "time_s": snappedf(td.time, 0.01), "duration_s": snappedf(td.duration, 0.01),
		"reduced": td.reduced, "plays": td.plays}
	var o := td.overlay
	if o != null:
		out["bars"] = snappedf(o.bars[0].modulate.a, 0.01)
		out["black"] = snappedf(o.black.modulate.a, 0.01)
		out["card"] = snappedf(o.card.modulate.a if o.card.visible else 0.0, 0.01)
		out["still"] = snappedf(o.still.modulate.a if o.still.visible else 0.0, 0.01)
		out["buttons"] = o.offered_buttons().map(func(b: Button) -> String: return String(b.name))
	var hull := td.tow.get_node_or_null("RivalHull") as OffshoreUnitModel if td.tow != null else null
	if hull == null:
		return out
	var c := hull.global_position
	var lo := Vector2(INF, INF)
	var hi := Vector2(-INF, -INF)
	for k in 24:
		var a := TAU * float(k) / 24.0
		for y: float in [-hull.draft_m + float((td.data.get("tow", {}) as Dictionary).get("transit_draft_m", hull.draft_m)), hull.air_draught_m]:
			var p := c + Vector3(cos(a) * hull.circumradius_m, y, sin(a) * hull.circumradius_m)
			if _cam.is_position_behind(p):
				continue
			var s := _cam.unproject_position(p)
			lo = Vector2(minf(lo.x, s.x), minf(lo.y, s.y))
			hi = Vector2(maxf(hi.x, s.x), maxf(hi.y, s.y))
	if not lo.is_finite():
		out["hull"] = {"on_screen": false}
		return out
	var box := Rect2i(Vector2i(clampi(int(lo.x), 0, img.get_width() - 1), clampi(int(lo.y), 0, img.get_height() - 1)), Vector2i.ZERO)
	box.end = Vector2i(clampi(int(hi.x), 0, img.get_width()), clampi(int(hi.y), 0, img.get_height()))
	var yellow := 0
	var far := 0
	for y in range(box.position.y, box.end.y):
		for x in range(box.position.x, box.end.x):
			var col := img.get_pixel(x, y)
			if col.h >= HULL_HUE.x and col.h <= HULL_HUE.y and col.s >= HULL_MIN_SV.x and col.v >= HULL_MIN_SV.y:
				yellow += 1
			if col.h >= HOME_HULL_HUE.x and col.h <= HOME_HULL_HUE.y and col.s >= HOME_HULL_MIN_SV.x and col.v >= HOME_HULL_MIN_SV.y:
				far += 1
	var size := Vector2(img.get_width(), img.get_height())
	out["hull"] = {"on_screen": hi.x > 0.0 and lo.x < size.x and hi.y > 0.0 and lo.y < size.y,
		"box": [box.position.x, box.position.y, box.end.x, box.end.y], "width_px": roundi(hi.x - lo.x),
		"yellow_pixels": yellow, "hull_hue_pixels": far, "distance_m": snappedf(_cam.global_position.distance_to(c), 1.0),
		"horizon_row": int(_cam.unproject_position(_cam.global_position + Vector3(-_cam.global_transform.basis.z.x, 0.0, -_cam.global_transform.basis.z.z).normalized() * 100000.0).y)}
	return out


## Shot pairs ("variant_of"): the Hamming distances of their pHashes and of
## their campus boxes'.
func _compare_showcase(shots: Array) -> void:
	for shot in shots:
		var sid := str(shot["id"])
		var base := str(shot.get("variant_of", ""))
		if base == "" or not _showcase.has(sid) or not _showcase.has(base):
			continue
		_showcase[sid]["phash_distance_vs_" + base] = phash_distance(str(_showcase[sid]["phash"]), str(_showcase[base]["phash"]))
		var a: Dictionary = _showcase[sid].get("campus", {})
		var b: Dictionary = _showcase[base].get("campus", {})
		if a.has("phash_roi") and b.has("phash_roi"):
			_showcase[sid]["phash_roi_distance_vs_" + base] = phash_distance(str(a["phash_roi"]), str(b["phash_roi"]))
