class_name WeatherDramaChecks
extends RefCounted
## G3-05 UI flow checks: weather drama and the Nordic night (run from
## UiFlowTests, headless). The moods come from the sim's weather and
## calendar (each mood golden's sim context reads as its mood); a blend is
## applied to the sky, the fog, the water and the exposure; a front turns
## the mood over seconds; the fog stays Koschmieder's and a low deck lays
## mist on the mountains; the sim's frost smoke reaches the water; cloud
## shadows fall only under broken cloud with the sun up, cover the cloud's
## share and drift; the pens' lamps burn after dark in the lighting season,
## exposure-corrected; a storm raises Hs >= 2 m on the exposed coast (also
## measured on the drawn water, WaveField); the camera sways in a storm and
## holds still under reduced motion; the rain slants with the wind; and the
## measuring tools (CIEDE2000, pHash) agree with their references.

## The UiFlowTests node (untyped: its helpers are script methods).
var t


## `tests`: the UiFlowTests node (its _check, _sim and _settle_camera).
func run(tests: Node) -> void:
	t = tests
	_check_metrics()
	t._check(t._sim.start_qa_scenario("po3_red_sone", 91), "moods: seeded scenario starts")
	t._sim.set_paused(true)
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	t.add_child(scene)
	for i in 5:
		await t.get_tree().process_frame
	t._sim.set_paused(true)
	var env: FjordEnvironment = scene.get_node("FjordEnvironment")
	var director: EnvironmentDirector = env.get_node("EnvironmentDirector")
	var weather: WeatherController = env.get_node("WeatherSystem")
	var ts: TimeOfDay = env.get_node("TimeOfDay")
	var cam: FjordCamera = env.get_node("FjordCamera")
	var terrain: FjordTerrain = env.get_node("Terrain")
	var vista: FjordVistaBridge = scene.get_node("FjordVistaBridge")
	var gb: Node = t.get_node("/root/GameBridge")
	cam.auto_orbit_enabled = false
	# The world on screen (no panel over it): the sky lives and the clouds drift.
	(scene.get_node("UILayer") as CanvasLayer).visible = false
	vista.sim_weather_enabled = false
	ts.paused = true
	weather.transition_duration = 0.0
	for i in 600:
		if terrain.is_ready():
			break
		await t.get_tree().process_frame
	for i in 30:
		await t.get_tree().process_frame

	_check_moods_from_the_sim(gb, env, director)
	await _check_mood_blend_and_transition(gb, env, director, weather)
	_check_fog_and_mist(gb, env, director, weather)
	_check_frost_smoke(gb, env, director, weather)
	await _check_cloud_shadows(gb, env, director)
	await _check_pen_lamps(gb, env, director)
	await _check_storm_sea(gb, env, director, cam)
	await _check_storm_sway(gb, env, director, cam)
	_stage(gb, env, director, _golden("storm"))
	for i in 3:
		await t.get_tree().process_frame
	_check_rain_slant(weather)
	scene.queue_free()
	await t.get_tree().process_frame


static func _stage(gb: Node, env: FjordEnvironment, director: EnvironmentDirector, g: Dictionary) -> void:
	env.apply_sky_context(gb.SkyContextFor(String(g["state"]), float(g["wind"]), float(g["sea"]), int(g["day"]), float(g["lat"])))
	env.set_time(float(g["hour"]))
	director.compose(0.0, true)


static func _golden(mood: String) -> Dictionary:
	for g: Dictionary in MoodGoldens.GOLDENS:
		if g["mood"] == mood:
			return g
	return {}


func _check_moods_from_the_sim(gb: Node, env: FjordEnvironment, director: EnvironmentDirector) -> void:
	var wrong := []
	for g: Dictionary in MoodGoldens.GOLDENS:
		_stage(gb, env, director, g)
		var got := String(director.get_composed().get("mood", ""))
		var weights: Dictionary = director.get_composed().get("mood_weights", {})
		if got != g["mood"] or float(weights.get(g["mood"], 0.0)) < 0.9:
			wrong.append("%s->%s" % [g["mood"], got])
	t._check(wrong.is_empty() and MoodGoldens.GOLDENS.size() == EnvironmentMood.COUNT,
		"moods: each of the eight golden sim days reads as its own mood, weight >= 0.9 (wrong: %s)" % str(wrong))


func _check_mood_blend_and_transition(gb: Node, env: FjordEnvironment, director: EnvironmentDirector, weather: WeatherController) -> void:
	_stage(gb, env, director, _golden("sommerdag"))
	var sum := 0.0
	for w in director.mood_weights:
		sum += w
	var sky := (director.world_environment.environment.sky.sky_material as ShaderMaterial)
	var water := director.water_material()
	var scatter: Vector3 = water.get_shader_parameter("deep_scatter")
	var blend_water: Color = director.mood["water"]
	t._check(absf(sum - 1.0) < 1e-4 and is_equal_approx(float(sky.get_shader_parameter("grade_amount")), float(director.mood["sky_grade"]))
		and scatter.is_equal_approx(Vector3(blend_water.r, blend_water.g, blend_water.b)),
		"moods: the weights sum to 1 and the blend reaches the sky's grade and the water's body")
	var sunny_blue := scatter.z / maxf(scatter.y, 1e-6)
	_stage(gb, env, director, _golden("graavaer"))
	var grey: Vector3 = water.get_shader_parameter("deep_scatter")
	t._check(sunny_blue > 3.0 and grey.z / maxf(grey.y, 1e-6) < 1.3 and grey.length() > scatter.length(),
		"moods: sunny water is deep blue, overcast water is grey (blue/green %.1f vs %.2f)" % [sunny_blue, grey.z / maxf(grey.y, 1e-6)])
	# A front rolls in: the storm's weight grows over seconds, never in a frame.
	_stage(gb, env, director, _golden("sommerdag"))
	var storm_ctx: Dictionary = gb.SkyContextFor("Storm", 16.0, 12.5, 176, 60.39)
	env.apply_sky_context(storm_ctx)
	director.set_process(false)
	director.compose(1.0 / 60.0)
	var first := director.mood_weights[EnvironmentMood.Id.STORM]
	for i in 600:
		director.compose(1.0 / 60.0)
	var later := director.mood_weights[EnvironmentMood.Id.STORM]
	director.set_process(true)
	t._check(first < 0.05 and later > 0.95,
		"moods: a storm front turns the mood over seconds (storm weight %.3f after a frame, %.2f after 10 s)" % [first, later])


func _check_fog_and_mist(gb: Node, env: FjordEnvironment, director: EnvironmentDirector, weather: WeatherController) -> void:
	var world := director.world_environment.environment
	_stage(gb, env, director, _golden("storm"))
	var storm_ok := absf(world.fog_density - 3.912 / weather.get_visibility_m()) < 1e-9
	var mist := world.fog_height_density < 0.0 and world.fog_height > 200.0
	_stage(gb, env, director, _golden("graavaer"))
	var grey_ok := absf(world.fog_density - 3.912 / weather.get_visibility_m()) < 1e-9
	var grey_mist := world.fog_height_density < 0.0
	t._check(storm_ok and grey_ok, "moods: the fog stays Koschmieder's under every mood (storm, grey)")
	t._check(mist and grey_mist, "moods: a low deck lays mist on the mountains (fog thickening above %.0f m)" % world.fog_height)
	# A sea-fog day keeps its layer on the water.
	var fog_ctx: Dictionary = gb.SkyContextFor("Calm", 1.0, 9.0, 172, 62.0)
	fog_ctx["sea_fog"] = 1.0
	env.apply_sky_context(fog_ctx)
	director.compose(0.0, true)
	t._check(world.fog_height_density > 0.0 and is_equal_approx(world.fog_height, EnvironmentDirector.SEA_FOG_TOP_M),
		"moods: a sea-fog layer stays on the water")
	# The storm's outline: the land's fog lighter than the deck hanging dark.
	_stage(gb, env, director, _golden("storm"))
	var sky := director.world_environment.environment.sky.sky_material as ShaderMaterial
	t._check(float(sky.get_shader_parameter("grade_deck_value")) < 0.6 and float(director.mood["fog_value"]) >= 1.0,
		"moods: a storm's deck hangs dark (%.2f) over land fog no darker than the horizon, so the mountains keep their outline" % float(sky.get_shader_parameter("grade_deck_value")))


func _check_frost_smoke(gb: Node, env: FjordEnvironment, director: EnvironmentDirector, weather: WeatherController) -> void:
	var g := _golden("frostroyk")
	var ctx: Dictionary = gb.SkyContextFor(String(g["state"]), float(g["wind"]), float(g["sea"]), int(g["day"]), float(g["lat"]))
	_stage(gb, env, director, g)
	var water := director.water_material()
	t._check(float(ctx.get("frost_smoke", 0.0)) > 0.5 and is_equal_approx(weather.get_frost_smoke(), float(ctx["frost_smoke"]))
		and float(water.get_shader_parameter("frost_smoke")) > 0.5,
		"moods: the sim's frost smoke (%.2f on a still Nordland winter day) reaches the water" % float(ctx.get("frost_smoke", 0.0)))
	var windy: Dictionary = gb.SkyContextFor("Moderate", 8.0, 5.0, int(g["day"]), float(g["lat"]))
	env.apply_sky_context(windy)
	director.compose(0.0, true)
	t._check(float(windy.get("frost_smoke", 1.0)) == 0.0 and float(water.get_shader_parameter("frost_smoke")) == 0.0,
		"moods: a breeze sweeps the frost smoke away")
	t._check(is_equal_approx(EnvironmentMood.frost_smoke_shown(1.0, 2.0), 1.0) and EnvironmentMood.frost_smoke_shown(1.0, 20.0) == 0.0,
		"moods: frost smoke lifts once the sun is high enough to warm the air")


func _check_cloud_shadows(gb: Node, env: FjordEnvironment, director: EnvironmentDirector) -> void:
	var shadows: CloudShadows = director.cloud_shadows
	var water := director.water_material()
	_stage(gb, env, director, _golden("sommerdag"))
	var cover := float(director.get_composed()["cloud_cover"])
	var share := _map_share(shadows)
	t._check(shadows.visible and shadows.strength > 0.9 and float(water.get_shader_parameter("cloud_shadow_strength")) > 0.9
		and water.get_shader_parameter("cloud_shadow_map") == shadows.get_map(),
		"moods: broken cloud under a high sun casts shadows on the land and the water (strength %.2f)" % shadows.strength)
	t._check(absf(share - cover) < 0.05, "moods: the shadows cover the cloud's share of the ground (%.2f for cover %.2f)" % [share, cover])
	var before := shadows.map_origin
	director.set_process(false)
	for i in 30:
		director.compose(1.0 / 30.0)
	director.set_process(true)
	var moved := shadows.map_origin.distance_to(before)
	var drift := Vector2(director.get_composed()["cloud_shadow_drift"]).length()
	t._check(moved > 5.0, "moods: the shadows drift with the clouds (%.0f m in a second)" % moved)
	_stage(gb, env, director, _golden("graavaer"))
	var closed := not shadows.visible and float(water.get_shader_parameter("cloud_shadow_strength")) == 0.0
	_stage(gb, env, director, _golden("nordlys"))
	var night := not shadows.visible
	t._check(closed and night and drift >= 0.0, "moods: no cloud shadows under a closed deck or at night")


## Share of the shadow map's texels in shadow.
static func _map_share(shadows: CloudShadows) -> float:
	var img := shadows.map_image
	if img == null:
		return -1.0
	var hits := 0
	var total := 0
	for y in range(0, img.get_height(), 3):
		for x in range(0, img.get_width(), 3):
			total += 1
			if img.get_pixel(x, y).a > 0.5:
				hits += 1
	return float(hits) / float(maxi(total, 1))


func _check_pen_lamps(gb: Node, env: FjordEnvironment, director: EnvironmentDirector) -> void:
	var lamps: PenLamps = director.pen_lamps
	var water := director.water_material()
	_stage(gb, env, director, _golden("nordlys"))
	for i in 70:
		await t.get_tree().process_frame
	director.compose(0.0, true)
	var expected := 0
	for cage in env.find_children("*", "", true, false):
		if cage is PenCage and PenLamps._holds_fish(cage as PenCage):
			expected += 1
	var lit := 0
	for light in lamps.lights:
		if light.visible and light.light_energy > 0.0:
			lit += 1
	var exposure := float(director.get_composed()["exposure"])
	var glow: Vector3 = water.get_shader_parameter("lamp_light")
	t._check(expected > 0 and lit == mini(expected, PenLamps.MAX_LAMPS) and int(water.get_shader_parameter("lamp_count")) == lit,
		"moods: on a winter night every stocked pen's lamp burns (%d of %d) and glows through the water" % [lit, expected])
	t._check(absf(glow.length() * exposure - Vector3(EnvironmentDirector.LAMP_GLOW_ON_SCREEN, 0, 0).length() * _filtered_norm()) < 0.05
		and exposure > 4.0,
		"moods: the lamps' glow keeps its screen brightness at the night's exposure (x%.1f)" % exposure)
	var lamp_y := lamps.lights[0].global_position.y if lit > 0 else 0.0
	t._check(lit > 0 and absf(lamp_y - (lamps.cages[0].global_position.y - PenLamps.LAMP_DEPTH_M)) < 0.01,
		"moods: the lamps hang %.0f m down in the pens" % PenLamps.LAMP_DEPTH_M)
	_stage(gb, env, director, _golden("sommerdag"))
	var day_dark := int(water.get_shader_parameter("lamp_count")) == 0 and lamps.lights.all(func(l: OmniLight3D) -> bool: return not l.visible)
	_stage(gb, env, director, _golden("midnattssol"))
	var summer_dark := int(water.get_shader_parameter("lamp_count")) == 0
	var summer_night: Dictionary = gb.SkyContextFor("Calm", 1.5, 11.0, 200, 60.39)
	env.apply_sky_context(summer_night)
	env.set_time(1.0)
	director.compose(0.0, true)
	var out_of_season := int(water.get_shader_parameter("lamp_count")) == 0
	t._check(day_dark and summer_dark and out_of_season, "moods: the lamps stay dark by day and outside the lighting season")


## Length of the lamp colour filtered by the water above it (as the director computes it).
static func _filtered_norm() -> float:
	var a := EnvironmentDirector.WATER_ABSORPTION
	var d := PenLamps.LAMP_DEPTH_M
	var f := EnvironmentDirector.LAMP_WHITE * Vector3(exp(-a.x * d), exp(-a.y * d), exp(-a.z * d))
	return (f / maxf(f.x, maxf(f.y, f.z))).length()


func _check_storm_sea(gb: Node, env: FjordEnvironment, director: EnvironmentDirector, cam: FjordCamera) -> void:
	var lowest := 99.0
	for wind in [13.0, 15.0, 17.0, 25.0]:
		env.apply_sky_context(gb.SkyContextFor("Storm" if wind < 20.0 else "Severe", wind, 8.0, 320, 60.39))
		director.compose(0.0, true)
		lowest = minf(lowest, float(director.sea_state["exposed_hs"]))
	t._check(lowest >= 2.0, "moods: every storm the sim blows (13-25 m/s) raises Hs >= 2 m on the exposed coast (lowest %.2f m)" % lowest)
	# The drawn water itself (WaveField, the CPU mirror of the shader's
	# displacement): on the open coast (exposure 1, deep) at the grid's finest
	# spacing, the components the grid draws carry Hs = sqrt(8 sum a^2).
	env.apply_sky_context(gb.SkyContextFor("Storm", 13.0, 8.0, 320, 60.39))
	director.compose(0.0, true)
	var field := director.wave_field
	var sum := 0.0
	for i in field.wave_amp.size():
		var a := field.drawn_amplitude(i, WaveField.OPEN_OCEAN, field.grid_base_spacing)
		sum += a * a
	var drawn_hs := sqrt(8.0 * sum)
	t._check(drawn_hs >= 2.0 and drawn_hs >= 0.95 * float(director.sea_state["exposed_hs"]),
		"moods: the drawn sea on the open coast in the weakest storm is Hs %.2f m (of %.2f m)" % [drawn_hs, float(director.sea_state["exposed_hs"])])
	await t.get_tree().process_frame


func _check_storm_sway(gb: Node, env: FjordEnvironment, director: EnvironmentDirector, cam: FjordCamera) -> void:
	var sway := cam.get_node_or_null(StormSway.NODE_NAME) as StormSway
	t._check(sway != null, "moods: the camera carries a storm sway")
	if sway == null:
		return
	StormSway.forced_reduced = 0
	sway.refresh_prefs()
	_stage(gb, env, director, _golden("storm"))
	for i in 240:
		await t.get_tree().process_frame
	var swaying := sway.level > 0.2 and sway.applied.length() > 0.0 and sway.peak_offset > 0.0
	var view_h := 2.0 * cam.get_current_distance() * tan(deg_to_rad(cam.fov * 0.5))
	t._check(swaying and sway.peak_offset <= view_h * StormSway.MAX_SCREEN_FRACTION * 1.01,
		"moods: the camera sways in a storm (level %.2f), at most %.2f %% of the view" % [sway.level, StormSway.MAX_SCREEN_FRACTION * 100.0])
	StormSway.forced_reduced = 1
	sway.refresh_prefs()
	for i in 3:
		await t.get_tree().process_frame
	t._check(sway.level == 0.0 and sway.applied == Vector2.ZERO and is_zero_approx(cam.h_offset) and is_zero_approx(cam.v_offset),
		"moods: reduced motion holds the view still in a storm")
	StormSway.forced_reduced = 0
	sway.refresh_prefs()
	_stage(gb, env, director, _golden("sommerdag"))
	for i in 240:
		await t.get_tree().process_frame
	t._check(sway.level < 0.01, "moods: no sway in a summer breeze (level %.3f)" % sway.level)
	StormSway.forced_reduced = -1
	sway.refresh_prefs()


func _check_rain_slant(weather: WeatherController) -> void:
	var process := weather.rain_particles.process_material as ParticleProcessMaterial
	var wind := weather.get_wind()
	var aim := WeatherController.rain_direction(wind)
	var lean := rad_to_deg(atan2(Vector2(aim.x, aim.z).length(), -aim.y))
	t._check(process.particle_flag_align_y and process.direction.distance_to(aim) < 2.0 * WeatherController.RAIN_AIM_STEP and wind.length() > 10.0 and lean > 30.0,
		"moods: storm rain slants with the wind (%.0f deg off vertical) and each streak lies along its drop's path" % lean)
	var cam_pos := Vector3(10.0, 50.0, -20.0)
	var emitter := weather.rain_emitter_position(cam_pos)
	t._check(Vector2(emitter.x - cam_pos.x, emitter.z - cam_pos.z).dot(wind) < 0.0,
		"moods: the rain's emitter sits upwind of the camera, so the slanting rain still covers the view")


func _check_metrics() -> void:
	# Sharma, Wu and Dalal (2005), test pairs 1, 7 and 17.
	var pairs := [
		[Vector3(50.0, 2.6772, -79.7751), Vector3(50.0, 0.0, -82.7485), 2.0425],
		[Vector3(50.0, 0.0, 0.0), Vector3(50.0, -1.0, 2.0), 2.3669],
		[Vector3(22.7233, 20.0904, -46.694), Vector3(23.0331, 14.973, -42.5619), 2.0373],
	]
	var worst := 0.0
	for p in pairs:
		worst = maxf(worst, absf(MoodMetrics.delta_e2000(p[0], p[1]) - float(p[2])))
	t._check(worst < 1e-3, "moods: CIEDE2000 matches the published test pairs (worst %.5f)" % worst)
	var white := MoodMetrics.srgb_to_lab(Color(1, 1, 1))
	var back := MoodMetrics.lab_to_srgb(MoodMetrics.srgb_to_lab(Color("#0E3F7A")))
	t._check(absf(white.x - 100.0) < 0.01 and absf(white.y) < 0.01 and back.is_equal_approx(Color("#0E3F7A")),
		"moods: CIELAB round-trips (white L* %.2f)" % white.x)
	var a := Image.create(64, 64, false, Image.FORMAT_RGB8)
	var b := Image.create(64, 64, false, Image.FORMAT_RGB8)
	for y in 64:
		for x in 64:
			a.set_pixel(x, y, Color(x / 64.0, y / 64.0, 0.5))
			b.set_pixel(x, y, Color(1.0 - x / 64.0, y / 64.0, 0.5))
	t._check(MoodMetrics.hamming(MoodMetrics.phash(a), MoodMetrics.phash(a)) == 0 and MoodMetrics.hamming(MoodMetrics.phash(a), MoodMetrics.phash(b)) >= 9,
		"moods: pHash is 0 apart for one image and far apart for its mirror")
	t._check(MoodMetrics.delta_e_to_spec(MoodMetrics.srgb_to_lab(Color("#12407A")), ["#0B3F7A", "#12407A"]) < 0.01,
		"moods: a range spec measures to its nearest point")
