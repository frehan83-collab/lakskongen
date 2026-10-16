class_name MoodGoldens
extends RefCounted
## The mood goldens (G3-05): eight frames from one camera, the FJORD view of
## the first farm, one per mood. Each is a real sim atmosphere: the sky
## context the sim derives for a weather regime, wind, sea temperature, day
## and latitude (GameBridge.SkyContextFor), at an hour of the presentation
## clock. Nothing is forced: the director weighs the mood from those inputs,
## and the run fails when it picks another one.
##
## Checks (roadmap G3-05 acceptance, ScreenshotHarness content failures):
## 1. the eight frames differ pairwise by pHash >= MIN_PHASH_DELTA, and the
##    sky and the water (within WATER_REACH_M of the camera) read within
##    EnvironmentMood.SPEC_DELTA_E (CIEDE2000) of EnvironmentMood.SPECS;
## 2. the pens' lamps light >= MIN_LAMP_FRACTION of the night frame (the
##    pixels the lamps change: the night frame again with the lamps off);
## 3. the clouds' shadows move: on broken-cloud Sommerdag the land changes
##    between two frames SHADOW_GAP_S apart, by at least MIN_SHADOW_MOTION
##    (mean |dL*|) and SHADOW_MOTION_RATIO times as much as without shadows;
## 4. a storm raises a sea of Hs >= MIN_STORM_HS on the exposed coast, at the
##    golden's wind and at the sim's weakest storm wind;
## plus the storm's outline: the mountains' skyline differs from the sky
## over it by >= MIN_SKYLINE_DL (mean dL*).
## A JSON report goes next to the frames (REPORT).

const SHOT_PREFIX := "20_mood_"
const REPORT := "20_moods.json"
## Sim day 0 is 1 January. The hour is the presentation clock's.
const GOLDENS := [
	{"mood": "sommerdag", "state": "Calm", "wind": 3.5, "sea": 12.5, "day": 176, "lat": 60.39, "hour": 13.0},
	{"mood": "midnattssol", "state": "Calm", "wind": 1.5, "sea": 9.0, "day": 172, "lat": 69.65, "hour": 0.5},
	{"mood": "graavaer", "state": "Moderate", "wind": 7.0, "sea": 9.0, "day": 290, "lat": 60.39, "hour": 12.0},
	{"mood": "storm", "state": "Storm", "wind": 16.0, "sea": 8.0, "day": 320, "lat": 60.39, "hour": 12.0},
	{"mood": "blaatime", "state": "Calm", "wind": 1.5, "sea": 5.0, "day": 9, "lat": 69.65, "hour": 12.0},
	{"mood": "nordlys", "state": "Calm", "wind": 1.5, "sea": 5.0, "day": 14, "lat": 69.65, "hour": 22.0},
	{"mood": "frostroyk", "state": "Calm", "wind": 1.0, "sea": 5.0, "day": 33, "lat": 67.28, "hour": 11.5},
	{"mood": "hoest", "state": "Calm", "wind": 3.0, "sea": 11.0, "day": 268, "lat": 61.0, "hour": 16.5},
]
## The sim's weakest storm wind (WeatherSystem: Storm blows 15 +- 2 m/s).
const WEAKEST_STORM_WIND := 13.0

const MIN_PHASH_DELTA := 9
const MIN_LAMP_FRACTION := 0.003
const MIN_SHADOW_MOTION := 0.5
const SHADOW_MOTION_RATIO := 3.0
const SHADOW_GAP_S := 12.0
const MIN_STORM_HS := 2.0
const MIN_SKYLINE_DL := 15.0
## Pixels sampled every STEP px; water counted within WATER_REACH_M of the
## camera (beyond it the water is the horizon's mirror); pens and their boats
## (within PEN_EXCLUDE_M of a pen) left out.
const STEP := 8
const WATER_REACH_M := 2000.0
const PEN_EXCLUDE_M := 70.0
## A lamp-lit pixel: lighter by this much (L*) than with the lamps off.
const LAMP_DL := 4.0
const SETTLE_S := 2.5

var failures := PackedStringArray()
var report := {}


## Capture and check the goldens through `harness` (ScreenshotHarness:
## its _shoot() saves a frame). `env` is the game's FjordEnvironment.
func capture(harness, env: Node, game: Node) -> void:
	var gb: Node = harness.get_node_or_null("/root/GameBridge")
	var director: EnvironmentDirector = env.get("director") if env != null else null
	var cam: FjordCamera = env.get("camera") if env != null else null
	if gb == null or director == null or cam == null or not gb.has_method("SkyContextFor"):
		failures.append("mood goldens: the fjord scene or the sky bridge is missing")
		return
	var vista := game.find_child("FjordVistaBridge", true, false)
	if vista != null:
		vista.set("sim_weather_enabled", false)
	var ts: TimeOfDay = env.get("time_system")
	var weather: WeatherController = env.get("weather_system")
	ts.paused = true
	weather.transition_duration = 0.0
	# The frames are stills: no storm sway (StormSway is checked by the UI
	# flow tests, with reduced motion).
	StormSway.forced_reduced = 1
	var pens: FishPenManager = env.get_node_or_null("FishPenManager")
	var focus := Vector3.ZERO
	var exclude := []
	if pens != null and not pens.get_pen_ids().is_empty():
		focus = pens.get_pen_position(pens.get_pen_ids()[0])
		for id in pens.get_pen_ids():
			exclude.append(pens.get_pen_position(id))
	cam.auto_orbit_enabled = false
	cam.look_at_position(focus, FjordCamera.ZoomLevel.FJORD)
	cam.set_pitch_offset(0.0)
	cam.call("_snap")

	var regions := PackedByteArray()
	var images := {}
	var hashes := {}
	var entries := {}
	for g: Dictionary in GOLDENS:
		var mood := String(g["mood"])
		_stage(gb, env, director, g)
		await harness.get_tree().create_timer(SETTLE_S).timeout
		var img: Image = await harness._shoot(SHOT_PREFIX + mood, true)
		if img == null:
			failures.append("mood goldens: %s was not captured" % mood)
			continue
		if regions.is_empty():
			regions = MoodMetrics.classify(cam, img.get_size(), STEP, 0.0, WATER_REACH_M, exclude, PEN_EXCLUDE_M)
		images[mood] = img
		hashes[mood] = MoodMetrics.phash(img)
		var composed := director.get_composed()
		var entry := _measure(img, regions, mood)
		entry["composed_mood"] = composed.get("mood", "")
		entry["exposure"] = composed.get("exposure", 0.0)
		entry["sun_elevation"] = composed.get("sun_elevation", 0.0)
		var fog_light: Color = composed.get("fog_light_color", Color.BLACK)
		entry["fog_light"] = [fog_light.r, fog_light.g, fog_light.b]
		entry["visibility_m"] = composed.get("visibility_m", 0.0)
		entry["cloud_base_m"] = composed.get("cloud_base_m", 0.0)
		entry["mist"] = composed.get("mist", 0.0)
		entry["mist_banks"] = director.mist_banks.bank_count if director.mist_banks != null else 0
		entry["phash"] = MoodMetrics.phash_hex(hashes[mood])
		entries[mood] = entry
		if String(composed.get("mood", "")) != mood:
			failures.append("mood goldens: the sim's %s day reads as %s" % [mood, composed.get("mood", "?")])
		if mood == "nordlys":
			entry["lamp_fraction"] = await _lamp_fraction(harness, director, img)
			if float(entry["lamp_fraction"]) < MIN_LAMP_FRACTION:
				failures.append("mood goldens: the pen lamps light %.2f %% of the night frame (min %.1f %%)" % [
					float(entry["lamp_fraction"]) * 100.0, MIN_LAMP_FRACTION * 100.0])
		if mood == "sommerdag":
			var motion := await _shadow_motion(harness, director, regions)
			entry["shadow_motion"] = motion
			if motion.x < MIN_SHADOW_MOTION or motion.x < motion.y * SHADOW_MOTION_RATIO:
				failures.append("mood goldens: the cloud shadows move the land by %.2f dL* in %.0f s (%.2f without; min %.1f and %.0fx)" % [
					motion.x, SHADOW_GAP_S, motion.y, MIN_SHADOW_MOTION, SHADOW_MOTION_RATIO])
		if mood == "storm":
			var sea: Dictionary = director.sea_state
			entry["exposed_hs"] = float(sea.get("exposed_hs", 0.0))
			entry["skyline_dl"] = skyline_contrast(img, regions, STEP)
			if float(entry["skyline_dl"]) < MIN_SKYLINE_DL:
				failures.append("mood goldens: the storm's skyline stands %.1f dL* off the sky (min %.0f)" % [float(entry["skyline_dl"]), MIN_SKYLINE_DL])
			var weakest: Dictionary = gb.SkyContextFor("Storm", WEAKEST_STORM_WIND, 8.0, int(g["day"]), float(g["lat"]))
			weather.apply_sky_context(weakest)
			director.compose(0.0, true)
			entry["exposed_hs_weakest_storm"] = float(director.sea_state.get("exposed_hs", 0.0))
			for key in ["exposed_hs", "exposed_hs_weakest_storm"]:
				if float(entry[key]) < MIN_STORM_HS:
					failures.append("mood goldens: a storm's exposed sea is Hs %.2f m (%s; min %.1f)" % [float(entry[key]), key, MIN_STORM_HS])
		_check_spec(mood, entry)
		print("[MoodGoldens] %s: %s" % [mood, JSON.stringify(entry)])
	var pairs := []
	var names := hashes.keys()
	for i in names.size():
		for j in range(i + 1, names.size()):
			var d := MoodMetrics.hamming(hashes[names[i]], hashes[names[j]])
			pairs.append([d, names[i], names[j]])
			if d < MIN_PHASH_DELTA:
				failures.append("mood goldens: %s and %s differ by pHash %d (min %d)" % [names[i], names[j], d, MIN_PHASH_DELTA])
	pairs.sort_custom(func(a: Array, b: Array) -> bool: return a[0] < b[0])
	report = {"goldens": entries, "phash_pairs": pairs, "min_phash": pairs[0][0] if not pairs.is_empty() else -1,
		"failures": failures, "step": STEP, "water_reach_m": WATER_REACH_M}
	var path := ProjectSettings.globalize_path("res://screenshots/game").path_join(REPORT)
	var file := FileAccess.open(path, FileAccess.WRITE)
	if file != null:
		file.store_string(JSON.stringify(report, "\t"))
	StormSway.forced_reduced = -1


static func _stage(gb: Node, env: Node, director: EnvironmentDirector, g: Dictionary) -> void:
	var ctx: Dictionary = gb.SkyContextFor(String(g["state"]), float(g["wind"]), float(g["sea"]), int(g["day"]), float(g["lat"]))
	env.call("apply_sky_context", ctx)
	env.call("set_time", float(g["hour"]))
	director.stage_cloud_drift(Vector2.ZERO)
	director.compose(0.0, true)


func _measure(img: Image, regions: PackedByteArray, mood: String) -> Dictionary:
	var spec: Dictionary = EnvironmentMood.SPECS[mood]
	var sky_skip := Callable()
	var water_skip := Callable()
	if mood == "nordlys":
		sky_skip = MoodMetrics.is_aurora
		water_skip = MoodMetrics.is_aurora
	var out := {}
	if spec.has("smoke"):
		water_skip = func(c: Color) -> bool: return MoodMetrics.srgb_to_lab(c).x >= EnvironmentMood.SMOKE_SPLIT_L
		var smoke_skip := func(c: Color) -> bool: return MoodMetrics.srgb_to_lab(c).x < EnvironmentMood.SMOKE_SPLIT_L
		var smoke := MoodMetrics.region_lab(img, regions, STEP, MoodMetrics.Region.WATER, smoke_skip)
		out["smoke_lab"] = [smoke.x, smoke.y, smoke.z]
		out["smoke_share_of_frame"] = smoke.w
		out["smoke_de"] = MoodMetrics.delta_e_to_spec(Vector3(smoke.x, smoke.y, smoke.z), spec["smoke"])
	var sky := MoodMetrics.region_lab(img, regions, STEP, MoodMetrics.Region.SKY, sky_skip)
	var water := MoodMetrics.region_lab(img, regions, STEP, MoodMetrics.Region.WATER, water_skip)
	out["sky_lab"] = [sky.x, sky.y, sky.z]
	out["water_lab"] = [water.x, water.y, water.z]
	out["sky_de"] = MoodMetrics.delta_e_to_spec(Vector3(sky.x, sky.y, sky.z), spec["sky"])
	out["water_de"] = MoodMetrics.delta_e_to_spec(Vector3(water.x, water.y, water.z), spec["water"])
	out["sky_hex"] = MoodMetrics.lab_to_srgb(Vector3(sky.x, sky.y, sky.z)).to_html(false)
	out["water_hex"] = MoodMetrics.lab_to_srgb(Vector3(water.x, water.y, water.z)).to_html(false)
	return out


func _check_spec(mood: String, entry: Dictionary) -> void:
	for region in ["sky", "water"]:
		var de := float(entry["%s_de" % region])
		if de > EnvironmentMood.SPEC_DELTA_E:
			failures.append("mood goldens: %s %s reads #%s, delta E %.1f from %s (max %.0f)" % [
				mood, region, entry["%s_hex" % region], de, str(EnvironmentMood.SPECS[mood][region]), EnvironmentMood.SPEC_DELTA_E])


## Share of the frame the pens' lamps light: the night frame against the
## same frame with the lamps off.
func _lamp_fraction(harness, director: EnvironmentDirector, lit: Image) -> float:
	if director.pen_lamps == null:
		return 0.0
	director.pen_lamps.enabled = false
	await harness.get_tree().create_timer(0.3).timeout
	var dark: Image = await harness._grab_frame()
	director.pen_lamps.enabled = true
	if dark == null:
		return 0.0
	var hits := 0
	var total := 0
	for y in range(0, lit.get_height(), 2):
		for x in range(0, lit.get_width(), 2):
			total += 1
			if MoodMetrics.srgb_to_lab(lit.get_pixel(x, y)).x - MoodMetrics.srgb_to_lab(dark.get_pixel(x, y)).x >= LAMP_DL:
				hits += 1
	return float(hits) / float(maxi(total, 1))


## Mean |dL*| on the land between two frames SHADOW_GAP_S apart, with the
## cloud shadows (x) and without them (y).
func _shadow_motion(harness, director: EnvironmentDirector, regions: PackedByteArray) -> Vector2:
	var out := Vector2.ZERO
	var toasts := harness.get_node_or_null("/root/ToastNotification") as CanvasLayer
	var toasts_shown := toasts != null and toasts.visible
	if toasts != null:
		toasts.visible = false
	for pass_i in 2:
		if director.cloud_shadows != null:
			director.cloud_shadows.enabled = pass_i == 0
		director.stage_cloud_drift(Vector2.ZERO)
		await harness.get_tree().create_timer(0.3).timeout
		var a: Image = await harness._grab_frame()
		await harness.get_tree().create_timer(SHADOW_GAP_S).timeout
		var b: Image = await harness._grab_frame()
		if pass_i == 0 and a != null and b != null:
			# The pair the motion is measured on, for the evidence.
			var dir := ProjectSettings.globalize_path("res://screenshots/game")
			a.save_png(dir.path_join(SHOT_PREFIX + "sommerdag_shadows_t0.png"))
			b.save_png(dir.path_join(SHOT_PREFIX + "sommerdag_shadows_t1.png"))
		var sum := 0.0
		var count := 0
		var cols := a.get_width() / STEP if a != null else 1
		for k in regions.size():
			if regions[k] != MoodMetrics.Region.LAND or a == null or b == null:
				continue
			var px := (k % cols) * STEP + STEP / 2
			var py := (k / cols) * STEP + STEP / 2
			sum += absf(MoodMetrics.srgb_to_lab(a.get_pixel(px, py)).x - MoodMetrics.srgb_to_lab(b.get_pixel(px, py)).x)
			count += 1
		out[pass_i] = sum / maxf(count, 1)
	if director.cloud_shadows != null:
		director.cloud_shadows.enabled = true
	if toasts != null:
		toasts.visible = toasts_shown
	return out


## Mean L* of the sky just over the skyline minus the land just under it.
static func skyline_contrast(img: Image, regions: PackedByteArray, step: int) -> float:
	var cols := img.get_width() / step
	var rows := regions.size() / cols
	var sum := 0.0
	var count := 0
	for x in cols:
		for y in range(2, rows - 3):
			if regions[(y - 1) * cols + x] == MoodMetrics.Region.SKY and regions[y * cols + x] == MoodMetrics.Region.LAND:
				var sky := 0.0
				var n_sky := 0
				for yy in range(maxi(y - 3, 0), y):
					sky += MoodMetrics.srgb_to_lab(img.get_pixel(x * step + step / 2, yy * step + step / 2)).x
					n_sky += 1
				var land := 0.0
				var n_land := 0
				for yy in range(y + 1, y + 4):
					if regions[yy * cols + x] == MoodMetrics.Region.LAND:
						land += MoodMetrics.srgb_to_lab(img.get_pixel(x * step + step / 2, yy * step + step / 2)).x
						n_land += 1
				if n_land > 0 and n_sky > 0:
					sum += absf(sky / n_sky - land / n_land)
					count += 1
				break
	return sum / count if count > 0 else 0.0
