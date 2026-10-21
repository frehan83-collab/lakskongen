class_name UnderwaterShots
extends RefCounted
## The underwater shots (roadmap G3-04): shot 11 and its variants, from inside
## the first stocked pen of the QA game, through ScreenshotHarness._shoot().
## The water is the sim's: the QA game's day sets the season's clarity
## (WaterClarity) and 11b adds a QA bloom at the site (GameBridge.SpawnQaBloom).
##
## Checks (the roadmap's acceptance; ScreenshotHarness content failures):
## 1. in 11 the camera sits MIN_DEPTH_M-MAX_DEPTH_M under the surface,
##    inside the pen's net;
## 2. the surface's underside renders: in 11c (looking up) the water well
##    inside Snell's window (under WINDOW_INSIDE_DEG from the zenith) is at
##    least MIN_WINDOW_TIR_RATIO times as bright as the surface outside it
##    (TIR_BAND_DEG), which carries the water's own tint (green over red);
## 3. the fog follows the sim's visibility: 11b's (a bloom at the site) is
##    shorter and its fog denser than 11's;
## 4. caustics animate on the net (11d, a staged camera NET_SHOT_DISTANCE_M
##    off the net): with time stopped (so nothing else moves, whatever the
##    frame pacing, and the floating pen stays where the net's mask was
##    taken), moving the caustics' clock on by CAUSTIC_GAP_S changes the
##    clean net's pixels by MIN_CAUSTIC_CHANGE (mean |dL|), the fully fouled
##    net's by MIN_FOULED_CAUSTIC_CHANGE (its growth is lighter), and with the
##    caustics held off by no more than MAX_STILL_CHANGE;
## 5. fish are visible: in 11 the schools change MIN_FISH_FRACTION of the
##    frame (ScreenshotHarness._fish_fraction, as 08b).
## 6. the net-fouling uniform reads: fouling the site's nets fully changes
##    MIN_FOULED_SHARE of 11d's frame (the net fills it; time stopped);
##    11e is 11d with the nets fouled.
## A JSON report goes next to the frames (REPORT).

const REPORT := "11_underwater.json"
## id, hour, season (presentation calendar), weather (ScreenshotHarness
## WEATHER_*), tilt (deg, the underwater pitch offset), bloom severity.
const SHOTS := [
	{"id": "11_fjord_underwater", "hour": 12.0, "season": 0.5, "weather": 0, "tilt": 0.0, "bloom": 0.0},
	{"id": "11c_underwater_window", "hour": 12.0, "season": 0.5, "weather": 0, "tilt": 45.0, "bloom": 0.0},
	{"id": "11b_underwater_bloom", "hour": 12.0, "season": 0.5, "weather": 0, "tilt": 0.0, "bloom": 0.8},
]
const NET_SHOT := "11d_underwater_net"
## The caustics' frame pairs (with and without them) go here, under the shots.
const MOTION_DIR := "underwater_motion"
const FOULED_SHOT := "11e_underwater_net_fouled"
const NET_SHOT_DISTANCE_M := 2.6
const NET_SHOT_DEPTH_M := 4.0
const NET_SHOT_TILT_DEG := 8.0
const ZOOM_FARM := 2
const ZOOM_UNDERWATER := 3
const SETTLE_S := 2.5
const MIN_DEPTH_M := 3.0
const MAX_DEPTH_M := 8.0
const WINDOW_INSIDE_DEG := 40.0
const TIR_BAND_DEG := Vector2(55.0, 75.0)
const MIN_WINDOW_TIR_RATIO := 1.8
const MIN_FISH_FRACTION := 0.01
const CAUSTIC_GAP_S := 0.5
## Where on the caustics' clock the pair starts (s, inside the 9 s loop).
const CAUSTIC_CLOCK_S := 2.0
## Measured (2026-10-21): the clean net 0.0039 (its threads' albedo is 1.6 %:
## a faint shimmer, p99 4 levels of 255), the fouled net 0.0086, held off
## 0.0000.
const MIN_CAUSTIC_CHANGE := 0.002
const MIN_FOULED_CAUSTIC_CHANGE := 0.005
const MAX_STILL_CHANGE := 0.0005
## Fouling the nets fully changes this share of the net frame.
const MIN_FOULED_SHARE := 0.1
## A pixel belongs to a mask when hiding the thing moves a channel by MASK_STEP.
const MASK_STEP := 2.0 / 255.0
## Frames are sampled every STEP pixels.
const STEP := 4

var failures := PackedStringArray()
var report := {}


## Capture and check the underwater shots through `harness` (ScreenshotHarness).
func capture(harness, env: Node, game: Node) -> void:
	var cam: FjordCamera = env.get("camera") if env != null else null
	var pens := env.get_node_or_null("FishPenManager") as FishPenManager if env != null else null
	var director: EnvironmentDirector = env.get("director") if env != null else null
	var view := env.get_node_or_null("UnderwaterView") as UnderwaterView if env != null else null
	if cam == null or pens == null or director == null or view == null or pens.get_pen_ids().is_empty():
		failures.append("11_fjord_underwater: the fjord scene, its pens, the director or the underwater view are missing")
		return
	var vista := game.find_child("FjordVistaBridge", true, false)
	if vista != null:
		vista.set("sim_weather_enabled", false)
	var target := stocked_pen(pens)
	if target.is_empty():
		failures.append("11_fjord_underwater: no stocked pen to dive into")
		return
	var entries := {}
	for shot: Dictionary in SHOTS:
		var id := String(shot["id"])
		await harness._configure_env(float(shot["season"]), float(shot["hour"]), int(shot["weather"]), ZOOM_FARM)
		_set_bloom(harness, String(target["site_id"]), float(shot["bloom"]))
		await harness.get_tree().create_timer(0.6).timeout
		cam.auto_orbit_enabled = false
		cam.look_at_position(target["center"], ZOOM_UNDERWATER)
		cam.set_pitch_offset(float(shot["tilt"]))
		cam.call("_snap")
		await harness.get_tree().create_timer(SETTLE_S).timeout
		var img: Image = await harness._shoot(id, true)
		if img == null:
			failures.append("%s was not captured" % id)
			continue
		entries[id] = _entry(cam, director, view, target)
		if id == "11_fjord_underwater":
			_check_depth(entries[id])
			entries[id]["fish_fraction"] = snappedf(await harness._fish_fraction(), 0.0001)
			if float(entries[id]["fish_fraction"]) < MIN_FISH_FRACTION:
				failures.append("11_fjord_underwater: the fish change %.2f %% of the frame (min %.0f %%)" % [
					float(entries[id]["fish_fraction"]) * 100.0, MIN_FISH_FRACTION * 100.0])
		if id == "11c_underwater_window":
			var w := window_and_tir(img, cam)
			entries[id]["window"] = w
			if float(w["ratio"]) < MIN_WINDOW_TIR_RATIO or not bool(w["tir_tinted"]) or int(w["window_n"]) == 0 or int(w["tir_n"]) == 0:
				failures.append("11c_underwater_window: Snell's window reads %.2fx the surface outside it (min %.1fx), tinted %s (%d / %d samples)" % [
					float(w["ratio"]), MIN_WINDOW_TIR_RATIO, str(w["tir_tinted"]), int(w["window_n"]), int(w["tir_n"])])
		print("[UnderwaterShots] %s: %s" % [id, JSON.stringify(entries[id])])
	_set_bloom(harness, String(target["site_id"]), 0.0)
	_check_bloom(entries)
	await harness._configure_env(0.5, 12.0, 0, ZOOM_FARM)
	cam.look_at_position(target["center"], ZOOM_UNDERWATER)
	cam.set_pitch_offset(0.0)
	cam.call("_snap")
	await harness.get_tree().create_timer(1.0).timeout
	entries[NET_SHOT] = await _net_shots(harness, cam, pens, director, view, target)
	cam.set_pitch_offset(0.0)
	var farm := pens.get_site(String(target["site_id"]))
	report = {"pen": {"site_id": target["site_id"], "pen": target["pen"],
		"net_fouling": farm.get_net_fouling() if farm != null else -1.0}, "shots": entries, "failures": failures}
	var path := ProjectSettings.globalize_path("res://screenshots/game").path_join(REPORT)
	var file := FileAccess.open(path, FileAccess.WRITE)
	if file != null:
		file.store_string(JSON.stringify(report, "\t"))


func _entry(cam: FjordCamera, director: EnvironmentDirector, view: UnderwaterView, target: Dictionary) -> Dictionary:
	var composed := director.get_composed()
	var uw: Dictionary = composed.get("uw", {})
	var at := cam.global_position
	var centre: Vector3 = target["center"]
	return {
		"camera": _arr(at),
		"depth_m": snappedf(-at.y, 0.01),
		"from_pen_centre_m": snappedf(Vector2(at.x - centre.x, at.z - centre.z).length(), 0.01),
		"fog_density": snappedf(float(composed.get("fog_density", 0.0)), 0.0001),
		"exposure": snappedf(float(composed.get("exposure", 0.0)), 0.001),
		"visibility_m": snappedf(float(uw.get("visibility_m", 0.0)), 0.01),
		"site_water": view.water_now,
		"horizontal": _arr(uw.get("horizontal", Vector3.ZERO)),
		"sun_at_depth": _arr(uw.get("sun_at_depth", Vector3.ZERO)),
		"caustic_strength": snappedf(float(composed.get("caustic_strength", 0.0)), 0.001),
		"underside": bool(composed.get("underside", false)),
		"shafts": view.shafts != null and view.shafts.visible,
		"shaft_count": view.shafts.multimesh.visible_instance_count if view.shafts != null else 0,
		"snow": view.snow != null and view.snow.visible,
	}


func _check_depth(entry: Dictionary) -> void:
	var depth := float(entry["depth_m"])
	var from_centre := float(entry["from_pen_centre_m"])
	if depth < MIN_DEPTH_M or depth > MAX_DEPTH_M or from_centre >= UnderwaterView.NET_RADIUS_M:
		failures.append("11_fjord_underwater: the camera is %.1f m down, %.1f m from the pen's centre (want %.0f-%.0f m inside the %.1f m net)" % [
			depth, from_centre, MIN_DEPTH_M, MAX_DEPTH_M, UnderwaterView.NET_RADIUS_M])


func _check_bloom(entries: Dictionary) -> void:
	var clear: Dictionary = entries.get("11_fjord_underwater", {})
	var bloom: Dictionary = entries.get("11b_underwater_bloom", {})
	if clear.is_empty() or bloom.is_empty():
		failures.append("11b_underwater_bloom: the clear or the bloom shot is missing")
		return
	if float(bloom["visibility_m"]) >= float(clear["visibility_m"]) or float(bloom["fog_density"]) <= float(clear["fog_density"]):
		failures.append("11b_underwater_bloom: the bloom's water reads %.1f m (fog %.3f), the clear water's %.1f m (fog %.3f)" % [
			float(bloom["visibility_m"]), float(bloom["fog_density"]), float(clear["visibility_m"]), float(clear["fog_density"])])


## The surface seen from below in a frame: the mean luminance of the pixels
## whose view ray meets the surface well inside Snell's window (under
## WINDOW_INSIDE_DEG from the zenith) and outside it (TIR_BAND_DEG), their
## ratio, and whether the surface outside carries the water's tint (its
## green over its red). Pure (the tests drive it).
static func window_and_tir(img: Image, cam: Camera3D) -> Dictionary:
	var vp := cam.get_viewport().get_visible_rect().size
	var w_sum := 0.0
	var w_n := 0
	var t_sum := 0.0
	var t_n := 0
	var t_rgb := Vector3.ZERO
	for y in range(0, img.get_height(), STEP):
		for x in range(0, img.get_width(), STEP):
			var screen := Vector2(x, y) / Vector2(img.get_size()) * vp
			var ray := cam.project_ray_normal(screen)
			if ray.y <= 0.0:
				continue
			var from_zenith := rad_to_deg(acos(clampf(ray.y, -1.0, 1.0)))
			var c := img.get_pixel(x, y)
			if from_zenith < WINDOW_INSIDE_DEG:
				w_sum += c.get_luminance()
				w_n += 1
			elif from_zenith > TIR_BAND_DEG.x and from_zenith < TIR_BAND_DEG.y:
				t_sum += c.get_luminance()
				t_rgb += Vector3(c.r, c.g, c.b)
				t_n += 1
	var w_mean := w_sum / maxf(w_n, 1)
	var t_mean := t_sum / maxf(t_n, 1)
	t_rgb /= maxf(t_n, 1)
	return {"window_mean": snappedf(w_mean, 0.0001), "tir_mean": snappedf(t_mean, 0.0001),
		"ratio": snappedf(w_mean / maxf(t_mean, 1e-4), 0.01), "tir_rgb": _arr(t_rgb),
		"tir_tinted": t_rgb.y > t_rgb.x, "window_n": w_n, "tir_n": t_n}


## 11d and 11e: the camera NET_SHOT_DISTANCE_M off the net's wall on the far
## side from the sun, looking out with the sun behind it (staged: the
## camera's own process held): the wall facing the sun lies in the collar's
## and the walkway's shadow down to ~5 m at noon, this one takes the sun
## and its caustics. The caustics' motion on the net checked, then the
## site's nets fouled.
func _net_shots(harness, cam: FjordCamera, pens: FishPenManager, director: EnvironmentDirector,
		view: UnderwaterView, target: Dictionary) -> Dictionary:
	var centre: Vector3 = target["center"]
	var uw: Dictionary = director.get_composed().get("uw", {})
	var sun: Vector3 = uw.get("sun_dir", Vector3.UP)
	var out_dir := Vector3(-sun.x, 0.0, -sun.z)
	if out_dir.length() < 0.05:
		out_dir = Vector3(cam.global_position.x - centre.x, 0.0, cam.global_position.z - centre.z)
	out_dir = out_dir.normalized() if out_dir.length() > 0.001 else Vector3(1.0, 0.0, 0.0)
	var at := centre + out_dir * (UnderwaterView.NET_RADIUS_M - NET_SHOT_DISTANCE_M)
	at.y = -NET_SHOT_DEPTH_M
	var look := (out_dir * cos(deg_to_rad(NET_SHOT_TILT_DEG)) + Vector3.UP * sin(deg_to_rad(NET_SHOT_TILT_DEG))).normalized()
	cam.set_process(false)
	cam.global_transform = Transform3D(Basis.looking_at(look, Vector3.UP), at)
	await harness.get_tree().create_timer(SETTLE_S).timeout
	var entry := _entry(cam, director, view, target)
	var img: Image = await harness._shoot(NET_SHOT, true)
	var farm := pens.get_site(String(target["site_id"]))
	var cage := farm.get_cage(int(target["pen"])) if farm != null else null
	if img == null or cage == null:
		failures.append("%s was not captured" % NET_SHOT)
		cam.set_process(true)
		return entry
	var was := farm.get_net_fouling()
	await _measure_net(harness, cage, farm, pens, view, director, entry)
	# 11e: the same view with the site's nets fully fouled.
	farm.set_net_fouling(1.0)
	await harness.get_tree().create_timer(0.4).timeout
	if await harness._shoot(FOULED_SHOT, true) == null:
		failures.append("%s was not captured" % FOULED_SHOT)
	farm.set_net_fouling(was)
	cam.set_process(true)
	cam.call("_snap")
	print("[UnderwaterShots] %s: %s" % [NET_SHOT, JSON.stringify(entry)])
	return entry


## The net's measurements, all inside one freeze (time stopped: the floating
## pen stays where its mask is taken), with the fish, the shafts and the snow
## held off: the net's pixels (what hiding the pen's nets changes), the
## fouling's share of the frame, and the caustics' motion on the clean net,
## on it with the caustics held off and on the fully fouled net.
func _measure_net(harness, cage: PenCage, farm: FarmSite, pens: FishPenManager, view: UnderwaterView,
		director: EnvironmentDirector, entry: Dictionary) -> void:
	pens.set_fish_shown(false)
	view.stage_effects(false)
	var nets := cage.get_net_nodes()
	var was := farm.get_net_fouling()
	var scale := Engine.time_scale
	Engine.time_scale = 0.0
	var with_net := await _frozen_frame(harness)
	for n in nets:
		n.visible = false
	var without_net := await _frozen_frame(harness)
	for n in nets:
		n.visible = true
	farm.set_net_fouling(1.0)
	var fouled_net := await _frozen_frame(harness)
	var mask := mask_of(with_net, without_net, MASK_STEP)
	var fouled_caustics := await _caustic_change(harness, mask, MOTION_DIR.path_join(FOULED_SHOT), director)
	farm.set_net_fouling(was)
	var on := await _caustic_change(harness, mask, MOTION_DIR.path_join(NET_SHOT), director)
	director.stage_caustics(false)
	var off := await _caustic_change(harness, mask, MOTION_DIR.path_join(NET_SHOT + "_still"), director)
	director.stage_caustics(true)
	Engine.time_scale = scale
	view.stage_effects(true)
	pens.set_fish_shown(true)
	_check_fouling(with_net, fouled_net, was, entry)
	entry["net_mask_share"] = snappedf(float(mask.count(1)) / maxf(mask.size(), 1), 0.0001)
	entry["caustic_change_on"] = snappedf(on, 0.00001)
	entry["caustic_change_off"] = snappedf(off, 0.00001)
	entry["caustic_change_fouled"] = snappedf(fouled_caustics, 0.00001)
	if on < MIN_CAUSTIC_CHANGE or off > MAX_STILL_CHANGE or fouled_caustics < MIN_FOULED_CAUSTIC_CHANGE:
		failures.append("%s: %.1f s on the caustics' clock changes the net's pixels by %.4f (min %.3f), the fouled net's by %.4f (min %.3f), %.4f with them held off (max %.4f)" % [
			NET_SHOT, CAUSTIC_GAP_S, on, MIN_CAUSTIC_CHANGE, fouled_caustics, MIN_FOULED_CAUSTIC_CHANGE, off, MAX_STILL_CHANGE])


## The fouling's share of the net frame (clean against fully fouled, frozen).
func _check_fouling(with_net: Image, fouled_net: Image, was: float, entry: Dictionary) -> void:
	var fouled_mask := mask_of(with_net, fouled_net, MASK_STEP)
	var fouled_share := float(fouled_mask.count(1)) / maxf(fouled_mask.size(), 1)
	var everywhere := PackedByteArray()
	everywhere.resize(fouled_mask.size())
	everywhere.fill(1)
	entry["fouled_share"] = snappedf(fouled_share, 0.0001)
	entry["clean_rgb"] = _arr(masked_mean(with_net, everywhere))
	entry["fouled_rgb"] = _arr(masked_mean(fouled_net, everywhere))
	if was > 0.5 or fouled_share < MIN_FOULED_SHARE:
		failures.append("%s: fouling the nets (from %.2f) changes %.1f %% of the frame (min %.0f %%)" % [
			FOULED_SHOT, was, fouled_share * 100.0, MIN_FOULED_SHARE * 100.0])


## Mean |dL| (0-1) over the masked pixels between two frames whose caustics'
## clock is CAUSTIC_GAP_S apart (CAUSTIC_CLOCK_S, then on), time already
## stopped by the caller; the pair is kept as `<prefix>_t0.png` / `_t1.png`
## (the evidence of the motion, beside the shots).
func _caustic_change(harness, mask: PackedByteArray, prefix: String, director: EnvironmentDirector) -> float:
	director.stage_caustic_clock(CAUSTIC_CLOCK_S)
	var a: Image = await _frozen_frame(harness)
	director.stage_caustic_clock(CAUSTIC_CLOCK_S + CAUSTIC_GAP_S)
	var b: Image = await _frozen_frame(harness)
	director.stage_caustic_clock(-1.0)
	var dir := ProjectSettings.globalize_path("res://screenshots/game")
	DirAccess.make_dir_recursive_absolute(dir.path_join(prefix).get_base_dir())
	if a != null and b != null:
		a.save_png(dir.path_join(prefix + "_t0.png"))
		b.save_png(dir.path_join(prefix + "_t1.png"))
	return masked_change(a, b, mask)


static func _frozen_frame(harness) -> Image:
	for i in 3:
		await harness.get_tree().process_frame
	await RenderingServer.frame_post_draw
	return harness.get_viewport().get_texture().get_image()


## 1 where a channel differs by `step` or more (every STEP-th pixel).
static func mask_of(a: Image, b: Image, step: float) -> PackedByteArray:
	var mask := PackedByteArray()
	if a == null or b == null or a.get_size() != b.get_size():
		return mask
	for y in range(0, a.get_height(), STEP):
		for x in range(0, a.get_width(), STEP):
			var ca := a.get_pixel(x, y)
			var cb := b.get_pixel(x, y)
			mask.append(1 if maxf(absf(ca.r - cb.r), maxf(absf(ca.g - cb.g), absf(ca.b - cb.b))) >= step else 0)
	return mask


## Mean colour (linear 0-1 channels as stored) over the pixels of `mask`.
static func masked_mean(img: Image, mask: PackedByteArray) -> Vector3:
	if img == null or mask.is_empty():
		return Vector3.ZERO
	var sum := Vector3.ZERO
	var n := 0
	var k := 0
	for y in range(0, img.get_height(), STEP):
		for x in range(0, img.get_width(), STEP):
			if k < mask.size() and mask[k] == 1:
				var c := img.get_pixel(x, y)
				sum += Vector3(c.r, c.g, c.b)
				n += 1
			k += 1
	return sum / maxf(n, 1)


## Mean |dL| over the pixels of `mask` between two frames.
static func masked_change(a: Image, b: Image, mask: PackedByteArray) -> float:
	if a == null or b == null or a.get_size() != b.get_size() or mask.is_empty():
		return 0.0
	var sum := 0.0
	var n := 0
	var k := 0
	for y in range(0, a.get_height(), STEP):
		for x in range(0, a.get_width(), STEP):
			if k < mask.size() and mask[k] == 1:
				sum += absf(a.get_pixel(x, y).get_luminance() - b.get_pixel(x, y).get_luminance())
				n += 1
			k += 1
	return sum / maxf(n, 1)


static func _arr(v: Vector3) -> Array:
	return [snappedf(v.x, 0.0001), snappedf(v.y, 0.0001), snappedf(v.z, 0.0001)]


## The first pen whose school shows fish: {"site_id", "pen", "center"}, or {}.
static func stocked_pen(pens: FishPenManager) -> Dictionary:
	for site_id in pens.get_pen_ids():
		var farm := pens.get_site(site_id)
		if farm == null or farm.is_ghost():
			continue
		var schools: Array = pens._pens[site_id].get("fish_schools", [])
		for k in schools.size():
			if FishSchool.shown_fish(schools[k]) > 0:
				var at := farm.pen_rest_position(k)
				return {"site_id": site_id, "pen": k, "center": Vector3(at.x, 0.0, at.z)}
	return {}


## A QA bloom of `severity` at the site (0 clears every QA bloom), and the
## state refresh that hands the sites' water to the view.
static func _set_bloom(harness, site_id: String, severity: float) -> void:
	var gb: Node = harness.get_node_or_null("/root/GameBridge")
	var sim: Node = harness.get_node_or_null("/root/SimBridge")
	if gb == null:
		return
	if gb.has_method("ClearQaBlooms"):
		gb.ClearQaBlooms()
	if severity > 0.0 and gb.has_method("SpawnQaBloom"):
		gb.SpawnQaBloom(site_id, severity, 21)
	# The blooms emit GameStateUpdated; refresh now so the vista's sync hands
	# the sites' water to the view before the frame.
	if sim != null and sim.has_method("_refresh_state"):
		sim.call("_refresh_state")

