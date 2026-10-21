class_name UnderwaterChecks
extends RefCounted
## G3-04 UI flow checks (run from UiFlowTests, headless): the underwater view.
## The optics (Snell's window, the sun's filter with depth, a bloom's fog),
## the sim's water reaching the view (GetSeaSiteDetails' visibility and a QA
## bloom's chlorophyll), the dive into the pen (3-8 m down inside the net),
## the director's composition under the surface and back (the fog, the
## surface's underside, the sky, the probe, the caustics' globals, the
## shafts, the snow and the lens), the fish's reach in the fog, the
## net-fouling uniform from the sim's net condition, the staging switches
## and the shots' measuring helpers. Pixel results (Snell's window against
## the TIR surface, the caustics' motion on the net, the fouled net's
## frame) are the screenshot harness's (UnderwaterShots, shot 11).

## The UiFlowTests node (untyped: its helpers are script methods).
var t

const HARNESS_SCRIPT := "res://scripts/tools/ScreenshotHarness.gd"
## A noon sun 50 degrees up (a Norwegian summer's).
const NOON_SUN := Vector3(0.0, 0.766, -0.643)


func run(tests: Node) -> void:
	t = tests
	_check_optics()
	_check_profile()
	_check_fouling_rule()
	_check_shot_helpers()
	_check_shaft_rules()
	t._check(t._sim.start_qa_scenario("po3_red_sone", 91), "underwater: seeded scenario starts")
	t._sim.set_paused(true)
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	t.add_child(scene)
	for i in 5:
		await t.get_tree().process_frame
	t._sim.set_paused(true)
	var env: FjordEnvironment = scene.get_node("FjordEnvironment")
	var vista: FjordVistaBridge = scene.get_node("FjordVistaBridge")
	var pens: FishPenManager = env.get_node("FishPenManager")
	env.camera.auto_orbit_enabled = false
	vista.sim_weather_enabled = false
	env.weather_system.transition_duration = 0.0
	env.set_weather(WeatherController.WeatherState.CLEAR)
	env.set_season(0.5)
	env.set_time(12.0)
	var site_id: String = pens.get_pen_ids()[0]
	await _check_site_water(env, pens, site_id)
	await _check_dive(env, pens, site_id)
	await _check_under_the_surface(env)
	await _check_fouling_wiring(pens, site_id)
	await _check_surfacing(env)
	# wave4b int (the gfx-polish-2 review's findings, handled on the merge).
	await _check_dive_path(env, pens, site_id)
	await _check_water_sky(env, pens, site_id)
	await _check_weather_under_water(env, pens, site_id)
	await _check_no_dive_over_land(env, pens, site_id)
	await _check_framing_leaves_the_water(env, pens, site_id)
	await _check_offshore_water(env, scene)
	_check_snow_fill(env)
	_bloom(site_id, 0.0)
	scene.queue_free()
	await t.get_tree().process_frame
	# The offshore check built a unit: the suite goes on with this check's own game.
	t._check(t._sim.start_qa_scenario("po3_red_sone", 91), "underwater: the seeded scenario starts again after the offshore check")
	t._sim.set_paused(true)


## Snell's law at the surface, the Fresnel share and the fog's visibility.
func _check_optics() -> void:
	var edge := UnderwaterOptics.refracted_sun(Vector3(1.0, 0.0, 0.0))
	var edge_deg := rad_to_deg(acos(edge.y))
	var overhead := UnderwaterOptics.refracted_sun(Vector3.UP)
	t._check(absf(edge_deg - UnderwaterOptics.SNELL_HALF_ANGLE_DEG) < 0.2 and overhead.is_equal_approx(Vector3.UP),
		"underwater: a sun on the horizon shines in at Snell's window's edge (%.2f deg from the zenith)" % edge_deg)
	var high := UnderwaterOptics.surface_transmission(Vector3.UP)
	var low := UnderwaterOptics.surface_transmission(Vector3(0.966, 0.259, 0.0))
	t._check(absf(high - (1.0 - UnderwaterOptics.WATER_F0)) < 1e-4 and low < high and UnderwaterOptics.surface_transmission(Vector3(1.0, -0.1, 0.0)) == 0.0,
		"underwater: the surface lets through 98 %% of a high sun, %.0f %% of one 15 deg up, none of a set sun" % (low * 100.0))
	var clear := UnderwaterOptics.fog_density(11.5)
	var murky := UnderwaterOptics.fog_density(2.35)
	t._check(murky > clear * 1.8 and is_equal_approx(clear, UnderwaterOptics.BLACK_TARGET / UnderwaterOptics.drawn_visibility(11.5))
		and UnderwaterOptics.drawn_visibility(0.0) >= UnderwaterOptics.READABLE_FLOOR_M,
		"underwater: a bloom's 2.35 m water draws a denser fog (%.3f) than 11.5 m water (%.3f); the view stays readable" % [murky, clear])


## The water's light with depth and with a bloom (EnvironmentDirector.underwater_profile).
func _check_profile() -> void:
	var zenith := Vector3(0.05, 0.12, 0.3)
	var horizon := Color(0.5, 0.6, 0.7)
	var p2 := EnvironmentDirector.underwater_profile(NOON_SUN, 2.0, 3.0, Color.WHITE, zenith, horizon, 0.0)
	var p8 := EnvironmentDirector.underwater_profile(NOON_SUN, 8.0, 3.0, Color.WHITE, zenith, horizon, 0.0)
	var s2: Vector3 = p2["sun_at_depth"]
	var s8: Vector3 = p8["sun_at_depth"]
	t._check(UnderwaterOptics.luminance(s8) < UnderwaterOptics.luminance(s2) and s8.x / s8.y < s2.x / s2.y,
		"underwater: the sun dims with depth and the red goes first (r/g %.3f at 2 m, %.3f at 8 m)" % [s2.x / s2.y, s8.x / s8.y])
	var lw := UnderwaterOptics.luminance(p2["window"])
	var lh := UnderwaterOptics.luminance(p2["horizontal"])
	var ln := UnderwaterOptics.luminance(p2["nadir"])
	t._check(lw > lh and lh > ln and (p2["sun_dir"] as Vector3).y > NOON_SUN.y,
		"underwater: Snell's window outshines the water across (%.4f > %.4f) and the depths below it (%.4f); the sun stands steeper in the water" % [lw, lh, ln])
	var bloom := EnvironmentDirector.underwater_profile(NOON_SUN, 2.0, 3.0, Color.WHITE, zenith, horizon, 16.0)
	var hb: Vector3 = bloom["horizontal"]
	var hc: Vector3 = p2["horizontal"]
	t._check(hb.y / hb.z > hc.y / hc.z and UnderwaterOptics.luminance(bloom["sun_at_depth"]) < UnderwaterOptics.luminance(s2),
		"underwater: a bloom turns the water green (g/b %.2f against %.2f) and dims the sun" % [hb.y / hb.z, hc.y / hc.z])


## The nets' fouling from the sim's net condition (FarmSite.net_fouling_for).
func _check_fouling_rule() -> void:
	var clean := FarmSite.net_fouling_for({"net_integrity": 100.0})
	var half := FarmSite.net_fouling_for({"net_integrity": 80})
	var full := FarmSite.net_fouling_for({"net_integrity": FarmSite.FOULED_AT_INTEGRITY})
	var worn := FarmSite.net_fouling_for({"net_integrity": 25.0})
	var none := FarmSite.net_fouling_for({})
	var junk := FarmSite.net_fouling_for({"net_integrity": "?"})
	t._check(clean == 0.0 and is_equal_approx(half, 0.5) and full == 1.0 and worn == 1.0 and none == 0.0 and junk == 0.0,
		"underwater: the nets foul as the sim's net condition falls (100 %% clean, 80 %% half, 60 %% and below full; %.2f %.2f %.2f %.2f)" % [clean, half, full, worn])


## The shots' measuring helpers on made frames, and the harness's group.
func _check_shot_helpers() -> void:
	var a := Image.create(16, 16, false, Image.FORMAT_RGB8)
	a.fill(Color(0.2, 0.4, 0.4))
	var b := a.duplicate() as Image
	b.fill_rect(Rect2i(0, 0, 8, 16), Color(0.0, 0.1, 0.1))
	var mask := UnderwaterShots.mask_of(a, b, UnderwaterShots.MASK_STEP)
	var change := UnderwaterShots.masked_change(a, b, mask)
	var mean := UnderwaterShots.masked_mean(b, mask)
	t._check(mask.count(1) * 2 == mask.size() and change > 0.2 and mean.distance_to(Vector3(0.0, 0.1, 0.1)) < 0.01,
		"underwater: the shots' masks find the changed half (%d of %d), its change (%.3f) and its colour" % [mask.count(1), mask.size(), change])
	var harness: GDScript = load(HARNESS_SCRIPT)
	var groups: PackedStringArray = harness.only_groups(PackedStringArray(["--only=underwater"]))
	var ids := UnderwaterShots.SHOTS.map(func(s: Dictionary) -> String: return String(s["id"]))
	t._check(groups == PackedStringArray(["underwater"]) and "11_fjord_underwater" in ids and "11b_underwater_bloom" in ids
		and "11c_underwater_window" in ids and UnderwaterShots.NET_SHOT == "11d_underwater_net",
		"underwater: the harness's underwater group shoots 11, 11b, 11c and the net (%s)" % str(ids))


## The shafts: none with the sun low or behind a closed deck; each world cell's
## shaft the same every time and inside its cell.
func _check_shaft_rules() -> void:
	var noon := UnderwaterView.shaft_strength(45.0, 0.1)
	var low := UnderwaterView.shaft_strength(2.0, 0.1)
	var overcast := UnderwaterView.shaft_strength(45.0, 0.95)
	var a := UnderwaterView.shaft_in_cell(Vector2i(-29, -386))
	var again := UnderwaterView.shaft_in_cell(Vector2i(-29, -386))
	var cell := UnderwaterView.SHAFT_CELL_M
	var inside := float(a["x"]) >= -29.0 * cell and float(a["x"]) < -28.0 * cell and float(a["z"]) >= -386.0 * cell and float(a["z"]) < -385.0 * cell
	t._check(is_equal_approx(noon, 1.0) and low == 0.0 and overcast == 0.0 and a == again and inside,
		"underwater: shafts need a high sun and a broken sky (%.2f / %.2f / %.2f); a cell's shaft stays put" % [noon, low, overcast])


## The sim's water per site (GetSeaSiteDetails), a QA bloom's, and the view's.
func _check_site_water(env: FjordEnvironment, pens: FishPenManager, site_id: String) -> void:
	var row := _site_row(site_id)
	var clear_vis := float(row.get("water_visibility_m", -1.0))
	t._check(clear_vis > 4.0 and clear_vis < 18.0 and float(row.get("bloom_chlorophyll_ug_l", -1.0)) == 0.0,
		"underwater: the bridge reports the site's water (%.2f m, no bloom)" % clear_vis)
	_bloom(site_id, 0.8)
	var bloom_row := _site_row(site_id)
	var bloom_vis := float(bloom_row.get("water_visibility_m", -1.0))
	var chl := float(bloom_row.get("bloom_chlorophyll_ug_l", 0.0))
	t._check(bloom_vis > 0.0 and bloom_vis < clear_vis * 0.5 and absf(chl - 16.0) < 0.01,
		"underwater: a QA bloom at the site clouds its water (%.2f m, %.1f ug/L chlorophyll)" % [bloom_vis, chl])
	for i in 3:
		await t.get_tree().process_frame
	var at := pens.get_pen_position(site_id)
	var water := env.underwater_view.water_at(at)
	t._check(String(water["site_id"]) == site_id and is_equal_approx(float(water["visibility_m"]), bloom_vis)
		and is_equal_approx(float(water["chlorophyll_ug_l"]), chl),
		"underwater: the view takes the site's water from the sim (%s: %.2f m)" % [String(water["site_id"]), float(water["visibility_m"])])
	_bloom(site_id, 0.0)
	for i in 3:
		await t.get_tree().process_frame
	var back := env.underwater_view.water_at(at)
	t._check(is_equal_approx(float(back["visibility_m"]), clear_vis) and float(back["chlorophyll_ug_l"]) == 0.0,
		"underwater: the bloom cleared, the site's water is clear again (%.2f m)" % float(back["visibility_m"]))


## The dive: the pen nearest the focus, 3-8 m down inside its net, looking
## across it; the tilt stays within its limit.
func _check_dive(env: FjordEnvironment, pens: FishPenManager, site_id: String) -> void:
	var cam := env.camera
	var view := env.underwater_view
	var farm := pens.get_site(site_id)
	var pen_at := farm.pen_rest_position(0)
	var anchor := view.dive_anchor(pen_at + Vector3(3.0, 0.0, -2.0))
	var far := view.dive_anchor(pen_at + Vector3(5000.0, 0.0, 0.0))
	t._check(String(anchor.get("site_id", "")) == site_id and int(anchor.get("pen", -1)) == 0 and far.is_empty(),
		"underwater: the dive takes the pen nearest the focus, none in open water")
	cam.look_at_position(pen_at, FjordCamera.ZoomLevel.UNDERWATER)
	await t._settle_camera(cam)
	var centre: Vector3 = anchor["center"]
	var depth := -cam.global_position.y
	var from_centre := Vector2(cam.global_position.x - centre.x, cam.global_position.z - centre.z).length()
	t._check(cam.is_underwater_view() and depth >= FjordCamera.UNDERWATER_MIN_DEPTH and depth <= FjordCamera.UNDERWATER_MAX_DEPTH
		and from_centre < UnderwaterView.NET_RADIUS_M,
		"underwater: the camera hangs %.1f m down, %.1f m from the pen's centre (inside the %.2f m net)" % [depth, from_centre, UnderwaterView.NET_RADIUS_M])
	var forward := -cam.global_transform.basis.z
	var to_centre := Vector3(centre.x - cam.global_position.x, 0.0, centre.z - cam.global_position.z).normalized()
	var across := rad_to_deg(Vector3(forward.x, 0.0, forward.z).normalized().angle_to(to_centre))
	t._check(absf(across - FjordCamera.UNDERWATER_TURN_DEG) < 3.0 and forward.y > 0.0,
		"underwater: it looks across the pen, %.0f deg off its centre and up toward the surface" % across)
	cam.set_pitch_offset(90.0)
	await _settle_view(cam)
	var up := rad_to_deg(asin(clampf((-cam.global_transform.basis.z).y, -1.0, 1.0)))
	t._check(up <= FjordCamera.UNDERWATER_PITCH_LIMIT_DEG + 0.5 and up > FjordCamera.UNDERWATER_PITCH_LIMIT_DEG - 2.0,
		"underwater: tilting up stops at the limit (%.1f deg)" % up)
	cam.set_pitch_offset(0.0)
	await _settle_view(cam)


## Waits until the camera stops turning (a tilt moves no position, so
## UiFlowTests._settle_camera returns at once).
func _settle_view(cam: Camera3D) -> void:
	var last := Vector3.INF
	for i in 1200:
		await t.get_tree().process_frame
		var forward := -cam.global_transform.basis.z
		if last != Vector3.INF and forward.angle_to(last) < 1e-5:
			return
		last = forward


## Under the surface the director composes the site's water: its fog, the
## surface's underside, the sky's gradient, no probe, the caustics' globals,
## the shafts, the snow and the lens; a bloom's water is denser and greener;
## the fish show within the fog's reach; the staging switches hold.
func _check_under_the_surface(env: FjordEnvironment) -> void:
	var director := env.director
	var view := env.underwater_view
	var world_env := director.world_environment.environment
	for i in 6:
		await t.get_tree().process_frame
	director.compose(0.0, true)
	var c := director.get_composed()
	var uw: Dictionary = c.get("uw", {})
	var vis := float(view.water_now["visibility_m"])
	t._check(not uw.is_empty() and is_equal_approx(world_env.fog_density, UnderwaterOptics.fog_density(vis)) and world_env.fog_sky_affect == 0.0,
		"underwater: the fog is the site's water (%.2f m: %.4f /m) and leaves the sky's gradient alone" % [vis, world_env.fog_density])
	var sky := world_env.sky.sky_material as ShaderMaterial
	t._check(director.water_grid.is_underside() and float(sky.get_shader_parameter("underwater")) == 1.0
		and not director.reflection_probe.visible and director.uw_globals_on and float(c.get("caustic_strength", 0.0)) > 0.5,
		"underwater: the surface draws its underside, the sky its gradient, the probe rests, the caustics run (%.2f)" % float(c.get("caustic_strength", 0.0)))
	t._check(view.shafts.visible and view.shafts.multimesh.visible_instance_count > 10 and view.snow.visible and view.snow.emitting
		and view.lens_rect.visible,
		"underwater: shafts (%d), snow and the lens's vignette under a noon sun" % view.shafts.multimesh.visible_instance_count)
	var reach := minf(FishSchool.UNDERWATER_REACH_OPTICAL / world_env.fog_density + FishSchool.SCHOOL_OUTER_M, FishSchool.FISH_RANGE_M)
	t._check(absf(FishSchool.underwater_reach_m - reach) < 0.5 and FishSchool.underwater_reach_m < 60.0,
		"underwater: the fish show within the fog's reach (%.1f m)" % FishSchool.underwater_reach_m)
	var clear_color := world_env.fog_light_color
	var clear_density := world_env.fog_density
	view.set_process(false)
	director.set_underwater_water(2.35, 16.0)
	director.compose(0.0, true)
	var bloom_color := world_env.fog_light_color
	t._check(world_env.fog_density > clear_density * 1.8 and bloom_color.g / maxf(bloom_color.b, 1e-3) > clear_color.g / maxf(clear_color.b, 1e-3),
		"underwater: a bloom's water is denser (%.3f /m) and greener than the clear water" % world_env.fog_density)
	director.set_underwater_water(vis, 0.0)
	view.set_process(true)
	await _check_staging(env)
	env.set_time(1.0)
	director.compose(0.0, true)
	for i in 3:
		await t.get_tree().process_frame
	t._check(float(director.get_composed().get("caustic_strength", 1.0)) == 0.0 and not view.shafts.visible,
		"underwater: at night no caustics and no shafts")
	env.set_time(12.0)
	director.compose(0.0, true)


## The harness's staging: the caustics held off, the shafts and snow held off.
func _check_staging(env: FjordEnvironment) -> void:
	var director := env.director
	var view := env.underwater_view
	director.stage_caustics(false)
	view.stage_effects(false)
	director.stage_caustic_clock(3.25)
	director.compose(0.0, true)
	for i in 3:
		await t.get_tree().process_frame
	var held := float(director.get_composed().get("caustic_strength", 1.0)) == 0.0 and not view.shafts.visible and not view.snow.visible \
		and is_equal_approx(float(director.get_composed().get("caustic_time", 0.0)), 3.25)
	director.stage_caustic_clock(-1.0)
	director.stage_caustics(true)
	view.stage_effects(true)
	director.compose(0.0, true)
	for i in 3:
		await t.get_tree().process_frame
	t._check(held and float(director.get_composed().get("caustic_strength", 0.0)) > 0.5 and view.shafts.visible and view.snow.visible,
		"underwater: the staging holds the caustics (and their clock), the shafts and the snow off and gives them back")


## The net-fouling uniform on every net of the site's pens and their LODs.
func _check_fouling_wiring(pens: FishPenManager, site_id: String) -> void:
	var farm := pens.get_site(site_id)
	farm.set_net_fouling(1.0)
	var nets: Array = []
	for k in farm.pen_count:
		nets.append_array(farm.get_cage(k).get_net_nodes())
	for level in farm._lod_parts:
		for part in farm._lod_parts[level]:
			if part in PenCage.NET_PARTS:
				nets.append(farm._lod_parts[level][part])
	var fouled := nets.filter(func(n: GeometryInstance3D) -> bool: return float(n.get_instance_shader_parameter("net_fouling")) == 1.0)
	t._check(nets.size() > farm.pen_count and fouled.size() == nets.size() and farm.get_cage(0).get_net_fouling() == 1.0,
		"underwater: fouling a site reaches its pens' nets and their LOD meshes (%d of %d)" % [fouled.size(), nets.size()])
	farm.apply_site_kit({"has_skirt": false, "net_integrity": 100.0})
	var clean := nets.filter(func(n: GeometryInstance3D) -> bool: return float(n.get_instance_shader_parameter("net_fouling")) == 0.0)
	t._check(farm.get_net_fouling() == 0.0 and clean.size() == nets.size(),
		"underwater: a clean net condition from the sim takes the fouling off every net")
	# The sim's daily sync: forty days of repair (0.5 % a day) touch the nets
	# in fouling steps, not every day (a write a day cost the 30x UI side 1 ms).
	var writes0 := FarmSite.fouling_writes
	for day in 40:
		farm.apply_site_kit({"has_skirt": false, "net_integrity": 80.0 + 0.5 * day})
	var writes := FarmSite.fouling_writes - writes0
	t._check(writes <= 12 and farm.get_net_fouling() <= FarmSite.FOULING_STEP,
		"underwater: forty days of net repair write the nets %d times (fouling %.2f)" % [writes, farm.get_net_fouling()])


## Up through the surface: the day's look comes back.
func _check_surfacing(env: FjordEnvironment) -> void:
	var cam := env.camera
	var director := env.director
	var view := env.underwater_view
	cam.set_zoom_level(FjordCamera.ZoomLevel.FJORD)
	await t._settle_camera(cam)
	for i in 40:
		await t.get_tree().process_frame
	director.compose(0.0, true)
	var sky := director.world_environment.environment.sky.sky_material as ShaderMaterial
	t._check(cam.global_position.y > 0.0 and director.get_composed().get("uw", {}).is_empty() and not director.water_grid.is_underside()
		and float(sky.get_shader_parameter("underwater")) == 0.0 and director.reflection_probe.visible and not director.uw_globals_on,
		"underwater: surfacing gives back the surface, the sky, the probe and switches the water's globals off")
	t._check(not view.shafts.visible and not view.snow.visible and not view.lens_rect.visible,
		"underwater: above the surface no shafts, no snow and no lens")


func _site_row(site_id: String) -> Dictionary:
	var gb: Node = t.get_node_or_null("/root/GameBridge")
	if gb == null:
		return {}
	for row in gb.GetSeaSiteDetails():
		if String(row.get("id", "")) == site_id:
			return row
	return {}


## A QA bloom of `severity` at the site (0 clears every QA bloom), refreshed
## into the sim's state the vista syncs from.
func _bloom(site_id: String, severity: float) -> void:
	var gb: Node = t.get_node_or_null("/root/GameBridge")
	if gb == null:
		return
	gb.ClearQaBlooms()
	if severity > 0.0:
		gb.SpawnQaBloom(site_id, severity, 21)
	t._sim.call("_refresh_state")


# ── wave4b int: the gfx-polish-2 review's findings ────────────────────────────

## The pen the checks dive into: its anchor and its cage.
func _pen_of(view: UnderwaterView, pens: FishPenManager, site_id: String) -> Dictionary:
	var farm := pens.get_site(site_id)
	var anchor := view.dive_anchor(farm.pen_rest_position(0))
	return {"centre": anchor["center"] as Vector3, "cage": farm.get_cage(int(anchor["pen"]))}


## A land pad's centre on its top (FjordTerrain.land_pad_slots).
func _pad_point(env: FjordEnvironment) -> Vector3:
	var pad: Dictionary = env.terrain.land_pad_slots()[0]
	var c: Vector3 = pad["centre"]
	return Vector3(c.x, LandCampusBuilder.PAD_TOP_Y, c.z)


## Steps the camera and the view at 60 Hz toward `zoom` at `centre` (the
## process of both held) until the camera has settled under the water (the
## dive) or is high over it (the way up): the camera, the pen's bird net and
## the lens's veil at every step.
func _step_path(env: FjordEnvironment, centre: Vector3, cage: PenCage, zoom: FjordCamera.ZoomLevel) -> Array:
	var cam := env.camera
	var view := env.underwater_view
	var dt := 1.0 / 60.0
	if zoom == FjordCamera.ZoomLevel.UNDERWATER:
		cam.look_at_position(centre, zoom)
	else:
		cam.set_zoom_level(zoom)
	var samples := []
	for i in 600:
		cam._process(dt)
		view._process(dt)
		var at := cam.global_position
		samples.append({"at": at, "fade": cage.get_bird_net_fade(), "veil": float(view.get("_veil"))})
		await t.get_tree().process_frame
		if zoom == FjordCamera.ZoomLevel.UNDERWATER and i > 30 and at.y < -FjordCamera.UNDERWATER_DEPTH + 0.05:
			break
		if zoom != FjordCamera.ZoomLevel.UNDERWATER and at.y > 60.0:
			break
	return samples


## Finding 3 and 4 (the dive's path): stepped at 60 Hz, below DIVE_HOVER_M
## the camera stays inside the pen's net; it crosses the surface straight
## down and straight up; the pen's bird net is gone wherever the camera is
## inside the pen under its top; on the way up the veil covers the last
## metres under the surface (the water material's own underside), and the
## bird net is whole again once the camera is out.
func _check_dive_path(env: FjordEnvironment, pens: FishPenManager, site_id: String) -> void:
	var cam := env.camera
	var view := env.underwater_view
	var pen := _pen_of(view, pens, site_id)
	var centre: Vector3 = pen["centre"]
	var cage: PenCage = pen["cage"]
	cam.look_at_position(centre, FjordCamera.ZoomLevel.FARM)
	await t._settle_camera(cam)
	cam.set_process(false)
	view.set_process(false)
	var down := await _step_path(env, centre, cage, FjordCamera.ZoomLevel.UNDERWATER)
	for i in 20:
		cam._process(1.0 / 60.0)
		view._process(1.0 / 60.0)
		await t.get_tree().process_frame
	var up := await _step_path(env, centre, cage, FjordCamera.ZoomLevel.FARM)
	cam.set_process(true)
	view.set_process(true)
	var outside := 0
	var gauzy := 0
	for s: Dictionary in down:
		var at: Vector3 = s["at"]
		var r := Vector2(at.x - centre.x, at.z - centre.z).length()
		if at.y < FjordCamera.DIVE_HOVER_M - 0.5 and r >= UnderwaterView.NET_RADIUS_M:
			outside += 1
		if r < UnderwaterView.NET_RADIUS_M and at.y < UnderwaterView.BIRD_NET_FADE_M.x and float(s["fade"]) > 0.0:
			gauzy += 1
	var bare := 0
	for s: Dictionary in up:
		var at: Vector3 = s["at"]
		var depth := -at.y
		if depth > 0.0 and depth < EnvironmentDirector.UNDERSIDE_FROM_M and float(s["veil"]) < 0.99:
			bare += 1
		var r := Vector2(at.x - centre.x, at.z - centre.z).length()
		if r < UnderwaterView.NET_RADIUS_M and at.y < UnderwaterView.BIRD_NET_FADE_M.x and float(s["fade"]) > 0.0:
			gauzy += 1
	var drift := maxf(_crossing_drift(down), _crossing_drift(up))
	t._check(down.size() > 20 and up.size() > 5 and outside == 0 and drift < MAX_CROSSING_SLOPE and gauzy == 0 and bare == 0
		and float(up[up.size() - 1]["fade"]) == 1.0,
		"underwater: the dive stays inside the net under %.0f m (%d outside), crosses the surface straight (%.3f m across a metre down), the bird net gone inside it (%d frames left), the veil over the last metres up (%d bare)" % [
			FjordCamera.DIVE_HOVER_M, outside, drift, gauzy, bare])


## The dive crosses the surface within this slope of the vertical (across
## per metre down: 0.25 is 14 degrees; it comes down within
## FjordCamera.DIVE_ENTRY_M of its place).
const MAX_CROSSING_SLOPE := 0.25


## How far the camera moved across per metre it moved up or down between
## the two steps either side of the surface (INF without a crossing).
static func _crossing_drift(samples: Array) -> float:
	for i in range(1, samples.size()):
		var a: Vector3 = samples[i - 1]["at"]
		var b: Vector3 = samples[i]["at"]
		if (a.y >= 0.0) != (b.y >= 0.0):
			return Vector2(b.x - a.x, b.z - a.z).length() / maxf(absf(b.y - a.y), 1e-3)
	return INF


## Findings 1 and 2 (the water's sky): under the surface the sky runs
## REALTIME (its radiance map, which the fog, the ambient and the
## reflections read, is the water's on the first frame) and the scene's mode
## comes back above; the sky draws the director's linear function with the
## measured blur factor, steepened positive everywhere (nothing clips); the
## surface seen from below fades into that same function at the fog's
## attenuation; the fog takes its colour from it.
func _check_water_sky(env: FjordEnvironment, pens: FishPenManager, site_id: String) -> void:
	var cam := env.camera
	var director := env.director
	var world_env := director.world_environment.environment
	var centre: Vector3 = _pen_of(env.underwater_view, pens, site_id)["centre"]
	var above_mode := world_env.sky.process_mode
	cam.look_at_position(centre, FjordCamera.ZoomLevel.UNDERWATER)
	await t._settle_camera(cam)
	for i in 3:
		await t.get_tree().process_frame
	var under_mode := world_env.sky.process_mode
	# The suite's UI may cover the world, which holds the sky still: a
	# staging compose writes it (as the checks above).
	director.compose(0.0, true)
	var sky := world_env.sky.sky_material as ShaderMaterial
	var side := director.water_grid.underside_material
	var uw: Dictionary = director.get_composed().get("uw", {})
	var fn: Dictionary = uw.get("sky", {})
	var sky_fn: bool = not fn.is_empty() and sky.get_shader_parameter("uw_horizontal") == fn["horizontal"] \
		and sky.get_shader_parameter("uw_vertical") == fn["vertical"] and sky.get_shader_parameter("uw_sun_slope") == fn["sun_slope"] \
		and is_equal_approx(float(sky.get_shader_parameter("uw_blur_slope")), EnvironmentDirector.UW_BLUR_SLOPE)
	var side_fn: bool = not fn.is_empty() and side.get_shader_parameter("underside_horizontal") == fn["horizontal"] \
		and side.get_shader_parameter("underside_vertical") == fn["vertical"] and side.get_shader_parameter("underside_sun_slope") == fn["sun_slope"] \
		and side.get_shader_parameter("underside_glow_dir") == sky.get_shader_parameter("uw_sun_dir")
	var side_fog: Variant = side.get_shader_parameter("underside_fog")
	var fog_fn: bool = side_fog != null and is_equal_approx(float(side_fog), world_env.fog_density) \
		and world_env.fog_aerial_perspective == 1.0 and world_env.fog_sky_affect == 0.0
	var floor := _steepened_floor(fn, sky.get_shader_parameter("uw_sun_dir"))
	var same := sky_fn and side_fn and fog_fn
	var floor_ok := floor >= EnvironmentDirector.UW_SKY_FLOOR - 1e-4
	cam.set_zoom_level(FjordCamera.ZoomLevel.FARM)
	await t._settle_camera(cam)
	for i in 3:
		await t.get_tree().process_frame
	t._check(under_mode == Sky.PROCESS_MODE_REALTIME and above_mode != Sky.PROCESS_MODE_REALTIME
		and world_env.sky.process_mode == above_mode and same and floor_ok,
		"underwater: the sky runs REALTIME under the surface (mode %d, %d above) and draws the director's linear water light (%s), steepened positive (floor %.3f); the surface from below (%s) and the fog (%s) share it" % [
			under_mode, world_env.sky.process_mode, sky_fn, floor, side_fn, fog_fn])


## The lowest share of its mean the radiance pass's steepened function
## reaches over the sphere, the worst channel (EnvironmentDirector.underwater_sky).
static func _steepened_floor(fn: Dictionary, sun: Vector3) -> float:
	if fn.is_empty():
		return -1.0
	var mean: Vector3 = fn["horizontal"]
	var up: Vector3 = fn["vertical"]
	var toward: Vector3 = fn["sun_slope"]
	var worst := INF
	var n := 400
	for i in n:
		var y := 1.0 - 2.0 * (float(i) + 0.5) / float(n)
		var ring := sqrt(maxf(1.0 - y * y, 0.0))
		var phi := float(i) * 2.39996323
		var d := Vector3(cos(phi) * ring, y, sin(phi) * ring)
		var f := mean + (up * d.y + toward * d.dot(sun)) / EnvironmentDirector.UW_BLUR_SLOPE
		for c in 3:
			if mean[c] > 0.0:
				worst = minf(worst, f[c] / mean[c])
	return worst


## Finding 5: no rain or snow round a camera under the surface; back in the
## air they fall again.
func _check_weather_under_water(env: FjordEnvironment, pens: FishPenManager, site_id: String) -> void:
	var cam := env.camera
	var ws := env.weather_system
	var centre: Vector3 = _pen_of(env.underwater_view, pens, site_id)["centre"]
	var blend := ws.transition_duration
	ws.transition_duration = 0.0
	env.set_weather(WeatherController.WeatherState.STORM)
	cam.look_at_position(centre, FjordCamera.ZoomLevel.UNDERWATER)
	await t._settle_camera(cam)
	for i in 3:
		await t.get_tree().process_frame
	var under := ws.submerged and not ws.rain_particles.visible and not ws.rain_particles.emitting \
		and not ws.snow_particles.visible and not ws.snow_particles.emitting
	cam.set_zoom_level(FjordCamera.ZoomLevel.FARM)
	await t._settle_camera(cam)
	for i in 3:
		await t.get_tree().process_frame
	var back := not ws.submerged and ws.rain_particles.visible and ws.rain_particles.emitting
	env.set_weather(WeatherController.WeatherState.CLEAR)
	for i in 2:
		await t.get_tree().process_frame
	ws.transition_duration = blend
	t._check(under and back, "underwater: a storm's rain and snow stop and hide under the surface and fall again above it")


## Finding 6a: over land (a land pad, the land campus's) the view does not
## dive: the wheel past its last notch and the API both stop at FARM; a pen
## it dives into.
func _check_no_dive_over_land(env: FjordEnvironment, pens: FishPenManager, site_id: String) -> void:
	var cam := env.camera
	var pad := _pad_point(env)
	var centre: Vector3 = _pen_of(env.underwater_view, pens, site_id)["centre"]
	cam.look_at_position(pad, FjordCamera.ZoomLevel.FARM)
	await t._settle_camera(cam)
	var middle := cam.get_viewport().get_visible_rect().size * 0.5
	for i in 30:
		cam._zoom_by(1.0 / FjordCamera.ZOOM_STEP, middle)
	cam.look_at_position(pad, FjordCamera.ZoomLevel.UNDERWATER)
	await t._settle_camera(cam)
	var at := cam.global_position
	var over := at.y - env.terrain.get_height_at(at.x, at.z)
	t._check(not cam.is_underwater_view() and cam.current_zoom == FjordCamera.ZoomLevel.FARM and not cam.can_dive_at(pad)
		and over > 1.0 and cam.can_dive_at(centre),
		"underwater: over a land pad the view does not dive (the camera %.0f m over the ground); into a pen it does" % over)


## Finding 6b: a framing or a hold asked for under water takes the camera
## out of the water before it goes (a land campus's card, a bookmark).
func _check_framing_leaves_the_water(env: FjordEnvironment, pens: FishPenManager, site_id: String) -> void:
	var cam := env.camera
	var pad := _pad_point(env)
	var centre: Vector3 = _pen_of(env.underwater_view, pens, site_id)["centre"]
	cam.look_at_position(centre, FjordCamera.ZoomLevel.UNDERWATER)
	await t._settle_camera(cam)
	var dived := cam.is_underwater_view() and cam.is_below_surface()
	cam.frame_position(pad, 220.0, -18.0)
	await t._settle_camera(cam)
	var at := cam.global_position
	var framed := not cam.is_underwater_view() and cam.current_zoom == FjordCamera.ZoomLevel.FARM and not cam.is_below_surface() \
		and at.y > env.terrain.get_height_at(at.x, at.z) + 10.0
	cam.look_at_position(centre, FjordCamera.ZoomLevel.UNDERWATER)
	await t._settle_camera(cam)
	cam.hold_view(pad, cam.home_yaw(pad), 300.0)
	await t._settle_camera(cam)
	at = cam.global_position
	var held := not cam.is_underwater_view() and not cam.is_below_surface() and at.y > env.terrain.get_height_at(at.x, at.z) + 10.0
	cam.recentre()
	await t._settle_camera(cam)
	t._check(dived and framed and held,
		"underwater: a framing or a hold from under the water takes the camera out (dived %s, framed %s, held %s)" % [dived, framed, held])


## Finding 9 and 4: an offshore unit's water is the sim's (its row in
## GetAllSeaSiteDetails), not the fallback, and there, with the open sea's
## swell drawn, the surface seen from below carries every wave uniform the
## water does, the swell's too.
func _check_offshore_water(env: FjordEnvironment, scene: Node) -> void:
	var cam := env.camera
	var view := env.underwater_view
	var director := env.director
	var bridge: Node = t.get_node("/root/GameBridge")
	var placed: Dictionary = bridge.QaPlaceFarmTypeSet(0, 0, 1)
	t._sim.advance_days(1)
	t._sim.set_paused(true)
	t._sim.state_updated.emit()
	for i in 3:
		await t.get_tree().process_frame
	var unit_id := str(placed.get("offshore_id", ""))
	var row := {}
	for r in bridge.GetAllSeaSiteDetails():
		if str(r.get("id", "")) == unit_id:
			row = r
	var slot := Vector3.INF
	for s: Dictionary in env.terrain.offshore_slots():
		if str(s.get("id", "")) == str(row.get("plot_id", "")):
			slot = s["position"]
	var manager := scene.get_node_or_null("FjordEnvironment/FarmFacilityVisualManager")
	var dived := false
	if slot.is_finite():
		cam.look_at_position(slot, FjordCamera.ZoomLevel.UNDERWATER)
		await t._settle_camera(cam)
		if manager != null:
			manager._refresh_seas()
			manager._update_sea_target()
		for i in 90:
			await t.get_tree().process_frame
		dived = cam.is_underwater_view() and cam.is_below_surface()
	var w := view.water_now
	var water := director.water_material()
	var side := director.water_grid.underside_material
	# Every uniform the two shaders share, from the shaders themselves (not
	# the director's list of what it copies: the review's mutant emptied it).
	var shared := shared_uniforms(water.shader, side.shader)
	var differ: Array[String] = []
	for key in shared:
		if side.get_shader_parameter(key) != water.get_shader_parameter(key):
			differ.append(key)
	var copied := director.water_grid.is_underside() and shared.size() >= 14 and differ.is_empty()
	var swell := float(water.get_shader_parameter("swell_peak"))
	cam.set_zoom_level(FjordCamera.ZoomLevel.FARM)
	await t._settle_camera(cam)
	t._check(bool(placed.get("success", false)) and not row.is_empty() and dived and String(w["site_id"]) == unit_id
		and is_equal_approx(float(w["visibility_m"]), float(row.get("water_visibility_m", -1.0))),
		"underwater: at an offshore unit the view takes the unit's water from the sim (%s: %.2f m)" % [String(w["site_id"]), float(w["visibility_m"])])
	t._check(copied and swell > 0.0,
		"underwater: under the open sea's swell (peak %.2f m) the surface from below carries every uniform it shares with the water (%d; differ: %s)" % [
			swell, shared.size(), ", ".join(differ)])


## The uniforms two shaders both declare (names), samplers included.
static func shared_uniforms(a: Shader, b: Shader) -> Array[String]:
	var names := {}
	for u: Dictionary in a.get_shader_uniform_list():
		names[String(u["name"])] = true
	var out: Array[String] = []
	for u: Dictionary in b.get_shader_uniform_list():
		if names.has(String(u["name"])):
			out.append(String(u["name"]))
	return out



## Found by the captain's measurement: a dive fills the snow's box at once
## with its preprocess, which at 30 simulation steps a second held the
## crossing's frame 100 ms; at UnderwaterView.SNOW_FIXED_FPS, interpolated,
## the whole lifetime is at most 120 steps (35 ms measured).
func _check_snow_fill(env: FjordEnvironment) -> void:
	var snow := env.underwater_view.snow
	var steps := snow.preprocess * float(snow.fixed_fps)
	t._check(snow.interpolate and steps <= 120.0 and snow.preprocess >= snow.lifetime,
		"underwater: a dive fills the snow's box at once in %.0f simulation steps (interpolated)" % steps)
