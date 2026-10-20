class_name GfxPolishChecks
extends RefCounted
## gfx-polish-1 UI flow checks (run from UiFlowTests, headless): the menu's
## hero view draws its clouds one pass finer; a Beaufort-0 calm is a mirror
## (no wind ripples); the pens damp the sea (the water draws no whitecap
## inside a collar: the director hands it every pen's collar while caps
## break, none in a calm); vessel chips keep their whole pill on screen and
## hide when their boat leaves it; the terrain chunks carry the slope and
## cavity with the wall ledges averaged out (UV2); the shoal's fish use the
## light salmon beyond 30 m; and the screenshot harness's single-shot and
## quality arguments parse. Pixel results (the clouds' steps, the walls'
## stripes, the moods, 18's mirror) are the screenshot harness's.

## The UiFlowTests node (untyped: its helpers are script methods).
var t

const HARNESS_SCRIPT := "res://scripts/tools/ScreenshotHarness.gd"
const BARGE_SCENE := "res://assets/models/feed_barge/feed_barge.tscn"


func run(tests: Node) -> void:
	t = tests
	_check_harness_arguments()
	_check_ripples_by_beaufort()
	await _check_menu_hero_sky()
	t._check(t._sim.start_qa_scenario("po3_red_sone", 91), "gfx polish: seeded scenario starts")
	t._sim.set_paused(true)
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	t.add_child(scene)
	for i in 5:
		await t.get_tree().process_frame
	t._sim.set_paused(true)
	var env: FjordEnvironment = scene.get_node("FjordEnvironment")
	var director: EnvironmentDirector = env.get_node("EnvironmentDirector")
	var weather: WeatherController = env.get_node("WeatherSystem")
	var cam: FjordCamera = env.get_node("FjordCamera")
	var terrain: FjordTerrain = env.get_node("Terrain")
	var vista: FjordVistaBridge = scene.get_node("FjordVistaBridge")
	var pens: FishPenManager = env.get_node("FishPenManager")
	var ui: Control = scene.get_node("UILayer/GameUI")
	cam.auto_orbit_enabled = false
	vista.sim_weather_enabled = false
	weather.transition_duration = 0.0
	ui.show_tab("fjord")
	for i in 600:
		if terrain.is_ready():
			break
		await t.get_tree().process_frame
	director.set_quality("high")
	_check_terrain_macro(terrain)
	await _check_pen_calm(env, director, cam, pens)
	await _check_shoal_lod(cam, pens)
	await _check_vessel_chips(env, cam, pens)
	_check_sun_keeps_its_light(env, director)
	scene.queue_free()
	await t.get_tree().process_frame


## The harness's `--only=` groups (unknown names dropped) and `--quality=`
## (a tier and knob overrides, the perf harness's variant syntax).
func _check_harness_arguments() -> void:
	var harness: GDScript = load(HARNESS_SCRIPT)
	var groups: PackedStringArray = harness.only_groups(PackedStringArray(["--only=menu,storm,nonsense", "--x"]))
	var none: PackedStringArray = harness.only_groups(PackedStringArray(["--quality=high"]))
	t._check(groups == PackedStringArray(["menu", "storm"]) and none.is_empty(),
		"harness: --only names its groups and drops unknown ones (%s)" % str(groups))
	var q: Dictionary = harness.quality_arg(PackedStringArray(["--only=menu", "--quality=high,cloud_pass=0,mirror=false,mirror_scale=0.5"]))
	t._check(String(q.get("tier", "")) == "high" and q.get("overrides", {}) == {"cloud_pass": 0, "mirror": false, "mirror_scale": 0.5}
		and (harness.quality_arg(PackedStringArray(["--only=menu"])) as Dictionary).is_empty(),
		"harness: --quality gives a tier and typed knob overrides (%s)" % str(q))


## Beaufort 0 (under 0.5 m/s, "sea like a mirror"): no wind ripples, only
## the residual stir; force-1 ripples by 1.5 m/s; the breeze's curve
## unchanged from there.
func _check_ripples_by_beaufort() -> void:
	var glass := EnvironmentDirector.ripple_strength_for(0.0)
	var half := EnvironmentDirector.ripple_strength_for(0.5)
	var force1 := EnvironmentDirector.ripple_strength_for(1.5)
	var breeze := EnvironmentDirector.ripple_strength_for(6.0)
	var monotone := true
	var last := -1.0
	for i in 41:
		var r := EnvironmentDirector.ripple_strength_for(i * 0.5)
		monotone = monotone and r >= last - 1e-6
		last = r
	t._check(is_equal_approx(glass, EnvironmentDirector.RIPPLE_GLASS) and is_equal_approx(half, EnvironmentDirector.RIPPLE_GLASS)
		and glass <= 0.05 and is_equal_approx(force1, EnvironmentDirector.RIPPLE_MIN + 1.5 * EnvironmentDirector.RIPPLE_PER_MS)
		and is_equal_approx(breeze, EnvironmentDirector.RIPPLE_MIN + 6.0 * EnvironmentDirector.RIPPLE_PER_MS) and monotone,
		"water: a Beaufort-0 calm draws no wind ripples (%.3f at 0 and 0.5 m/s), force 1 by 1.5 m/s (%.3f), the breeze's curve unchanged (%.3f at 6 m/s)" % [
			glass, force1, breeze])


## The menu's hero view looks along the horizon: its clouds one pass finer
## than the tier's (High per pixel, Medium and Low in the half pass); the
## game keeps the tier's pass.
func _check_menu_hero_sky() -> void:
	var menu: Control = await t._spawn_main_menu()
	var backdrop: MenuBackdrop = menu.backdrop
	var director: EnvironmentDirector = backdrop.environment.director
	for i in 3:
		await t.get_tree().process_frame
	var passes := []
	for tier in ["low", "medium", "high", "ultra"]:
		director.set_quality(tier)
		var sky := (backdrop.environment.get_node("WorldEnvironment") as WorldEnvironment).environment.sky.sky_material as ShaderMaterial
		passes.append([director.cloud_pass_now(false), int(sky.get_shader_parameter("cloud_pass"))])
	director.set_quality("high")
	var game_like := EnvironmentDirector.new()
	game_like.quality.set_tier(RenderQuality.Tier.HIGH)
	var game_pass := game_like.cloud_pass_now(false)
	var under := director.cloud_pass_now(true)
	game_like.free()
	t._check(director.hero_sky and passes == [[1, 1], [1, 1], [0, 0], [0, 0]] and game_pass == 1 and under == 0,
		"menu: the hero view's clouds one pass finer than the tier's (low/medium/high/ultra %s), the game's High keeps the half pass (%d)" % [
			str(passes), game_pass])
	menu.queue_free()
	await t.get_tree().process_frame


## The terrain's chunks (heightfield and far field) carry CUSTOM0 (four
## floats a vertex): the slope and cavity of the ground without the wall
## ledges' terraces and the ledges' strength, which the shader's masks read
## where a terrace is small on screen. On the walls the slope differs from the
## vertex's (the terraces taken out) and the strength is up to the
## generator's 0.28; the far field carries no ledges.
func _check_terrain_macro(terrain: FjordTerrain) -> void:
	var chunks := terrain.get_chunk_meshes()
	var with_macro := 0
	var sane := true
	var differs := 0
	var strong := 0
	for chunk in chunks:
		var mesh := chunk.mesh as ArrayMesh
		if mesh == null or (mesh.surface_get_format(0) & Mesh.ARRAY_FORMAT_CUSTOM0) == 0:
			continue
		with_macro += 1
		var arrays := mesh.surface_get_arrays(0)
		var macro: PackedFloat32Array = arrays[Mesh.ARRAY_CUSTOM0]
		var normals: PackedVector3Array = arrays[Mesh.ARRAY_NORMAL]
		sane = sane and macro.size() == normals.size() * 4
		for i in range(0, normals.size(), 97):
			var slope := macro[i * 4]
			var cavity := macro[i * 4 + 1]
			var k := macro[i * 4 + 2]
			sane = sane and slope >= 0.0 and slope < 50.0 and cavity >= 0.0 and cavity <= 1.0 and k >= 0.0 and k <= 0.2801
			var n := normals[i]
			var own := Vector2(n.x, n.z).length() / maxf(n.y, 1e-3)
			if absf(own - slope) > 0.05:
				differs += 1
			if k > 0.2:
				strong += 1
	var far_ok := true
	if terrain.far_field != null:
		for child in terrain.far_field.get_children():
			if not (child is MeshInstance3D):
				continue
			var far_mesh := (child as MeshInstance3D).mesh as ArrayMesh
			if far_mesh.surface_get_format(0) & Mesh.ARRAY_FORMAT_CUSTOM0 == 0:
				far_ok = false
				continue
			var far_macro: PackedFloat32Array = far_mesh.surface_get_arrays(0)[Mesh.ARRAY_CUSTOM0]
			for i in range(2, far_macro.size(), 4 * 211):
				far_ok = far_ok and far_macro[i] == 0.0
	t._check(not chunks.is_empty() and with_macro == chunks.size() and sane and differs > 0 and strong > 0 and far_ok,
		"terrain: every chunk (%d) and the far field carry the ground without its ledges in CUSTOM0, the walls' slope taken out of its terraces (%d samples differ) and the ledges' strength (%d samples over 0.2)" % [
			chunks.size(), differs, strong])


## In a storm the water breaks no whitecap inside a pen: the director hands
## it every pen's collar (PenSpray.collars: nearest the camera first, the
## cage's collar radius); in a calm it sends none.
func _check_pen_calm(env: FjordEnvironment, director: EnvironmentDirector, cam: FjordCamera, pens: FishPenManager) -> void:
	var spray: PenSpray = env.get_node("PenSpray")
	var mat := director.water_material()
	var focus: Vector3 = pens.get_pen_position(pens.get_pen_ids()[0])
	cam.look_at_position(focus, FjordCamera.ZoomLevel.FJORD)
	await t._settle_camera(cam)
	env.apply_sky_context({"weather_state": "Storm", "wind_ms": 16.0, "cloud_cover": 1.0, "visibility_km": 2.0})
	await t.get_tree().create_timer(PenSpray.REFRESH_S + 0.3).timeout
	director.compose(1.0 / 60.0)
	var cages: Array[PenCage] = []
	for id in pens.get_pen_ids():
		for cage in pens.get_site_cages(id):
			if (cage as PenCage).is_visible_in_tree():
				cages.append(cage)
	var sent: PackedVector4Array = mat.get_shader_parameter("pen_calm")
	var count := int(mat.get_shader_parameter("pen_calm_count"))
	var eye := cam.global_position
	var matched := 0
	var sorted := true
	var last_d := -1.0
	for i in count:
		var e := sent[i]
		var d := Vector2(e.x - eye.x, e.y - eye.z).length()
		sorted = sorted and d >= last_d - 0.01
		last_d = d
		for cage in cages:
			if Vector2(cage.global_position.x, cage.global_position.z).distance_to(Vector2(e.x, e.y)) < 0.01 \
					and is_equal_approx(e.z, cage.get_collar_radius()):
				matched += 1
				break
	var whitecaps := float(director.sea_state.get("whitecaps", 0.0))
	t._check(whitecaps > EnvironmentDirector.WHITECAP_MIN_SHARE and count == mini(cages.size(), PenSpray.MAX_CALM_PENS)
		and matched == count and sorted and sent.size() == PenSpray.MAX_CALM_PENS,
		"storm: the water knows every pen's collar while caps break (%d of %d pens, nearest first, %.1f %% caps)" % [
			count, cages.size(), whitecaps * 100.0])
	env.apply_sky_context({"weather_state": "Calm", "wind_ms": 1.5, "cloud_cover": 0.15, "visibility_km": 50.0})
	director.compose(1.0 / 60.0)
	t._check(int(mat.get_shader_parameter("pen_calm_count")) == 0 and int(director.get_composed().get("pen_calm_count", -1)) == 0,
		"calm: no collars sent while no cap breaks")


## The shoal's fish (drawn through the surface) use the light salmon beyond
## SHOAL_LOD_SWITCH: at FARM zoom only a pen closer than that would get the
## heavy one.
func _check_shoal_lod(cam: FjordCamera, pens: FishPenManager) -> void:
	var focus: Vector3 = pens.get_pen_position(pens.get_pen_ids()[0])
	cam.look_at_position(focus, FjordCamera.ZoomLevel.FARM)
	await t._settle_camera(cam)
	for i in 5:
		await t.get_tree().process_frame
	var light := FishSchool.shoal_mesh(2)
	var heavy := FishSchool.shoal_mesh(1)
	var right := 0
	var schools := 0
	for id in pens.get_pen_ids():
		for cage in pens.get_site_cages(id):
			for child in (cage as Node).get_children():
				if not (child is MultiMeshInstance3D and String(child.name).begins_with("FishSchool")):
					continue
				var shoal := FishSchool.shoal_of(child as MultiMeshInstance3D)
				if shoal == null or shoal.multimesh.visible_instance_count <= 1:
					continue
				schools += 1
				var d := cam.global_position.distance_to((child as Node3D).global_position)
				if shoal.multimesh.mesh == (heavy if d < FishSchool.SHOAL_LOD_SWITCH else light):
					right += 1
	t._check(FishSchool.SHOAL_LOD_SWITCH <= 35.0 and schools > 0 and right == schools,
		"fish: the shoal's fish use the light salmon beyond %.0f m (%d of %d schools in range)" % [FishSchool.SHOAL_LOD_SWITCH, right, schools])


## A vessel chip keeps its whole pill EDGE_MARGIN_PX inside the screen while
## its boat is on screen (08b cut a barge's chip at the left edge), and hides
## once the boat has left it.
func _check_vessel_chips(env: FjordEnvironment, cam: FjordCamera, pens: FishPenManager) -> void:
	var view: Vector2 = (t.get_viewport() as Viewport).get_visible_rect().size
	var size := Vector2(220.0, VesselChip.HEIGHT_PX)
	var centred := VesselChip.edge_shift_for(view * 0.5, size, cam.fov, view)
	var k := VesselChip.PIXEL_SIZE * view.y / (2.0 * tan(deg_to_rad(cam.fov) * 0.5))
	var left := VesselChip.edge_shift_for(Vector2(6.0, view.y * 0.5), size, cam.fov, view)
	var right := VesselChip.edge_shift_for(Vector2(view.x - 6.0, view.y * 0.5), size, cam.fov, view)
	var top := VesselChip.edge_shift_for(Vector2(view.x * 0.5, 4.0), size, cam.fov, view)
	var left_edge := 6.0 + left.x * k - size.x * k * 0.5
	var right_edge := view.x - 6.0 + right.x * k + size.x * k * 0.5
	var top_edge := 4.0 - (VesselChip.LIFT_PX + top.y) * k - size.y * k * 0.5
	t._check(centred == Vector2.ZERO and absf(left_edge - VesselChip.EDGE_MARGIN_PX) < 0.5
		and absf(view.x - right_edge - VesselChip.EDGE_MARGIN_PX) < 0.5 and absf(top_edge - VesselChip.EDGE_MARGIN_PX) < 0.5,
		"chips: a pill at the left, right or top edge slides in to %.0f px from it (left %.1f, right %.1f, top %.1f px), a centred one stays" % [
			VesselChip.EDGE_MARGIN_PX, left_edge, view.x - right_edge, top_edge])
	# A live chip on the real camera: anchored at the left edge it slides in;
	# anchored off screen it hides; back on screen it shows again.
	var focus: Vector3 = pens.get_pen_position(pens.get_pen_ids()[0])
	cam.look_at_position(focus, FjordCamera.ZoomLevel.FARM)
	await t._settle_camera(cam)
	var chip := VesselChip.new()
	env.add_child(chip)
	chip.set_chip("Fôrflåte · fôrer", Color("#E8B92A"))
	var depth := 120.0
	chip.global_position = cam.project_position(Vector2(10.0, view.y * 0.5), depth)
	for i in 3:
		await t.get_tree().process_frame
	var at := cam.unproject_position(chip.global_position)
	var kk := VesselChip.PIXEL_SIZE * view.y / (2.0 * tan(deg_to_rad(cam.fov) * 0.5))
	var pill := chip.get_size_px()
	var pill_left := at.x + chip.edge_shift.x * kk - pill.x * kk * 0.5
	var shown_left := chip.on_screen and pill_left >= VesselChip.EDGE_MARGIN_PX - 0.6
	chip.global_position = cam.project_position(Vector2(-200.0, view.y * 0.5), depth)
	for i in 3:
		await t.get_tree().process_frame
	var hidden_off := not chip.on_screen and not (chip.get_node("Pill") as Node3D).visible and not (chip.get_node("Text") as Node3D).visible
	chip.global_position = cam.project_position(view * 0.5, depth)
	for i in 3:
		await t.get_tree().process_frame
	var back := chip.on_screen and (chip.get_node("Pill") as Node3D).visible and chip.edge_shift == Vector2.ZERO
	t._check(shown_left and hidden_off and back,
		"chips: a barge's chip at the screen's left edge shows its whole pill (left %.1f px), hides with its boat off screen and returns centred" % pill_left)
	chip.queue_free()
	# A chip riding a 37 m barge (08b): while the barge's bow still shows,
	# its centre (the anchor) a little past the left edge, the chip stays,
	# slid in; once the whole barge has left the view, it hides.
	var barge := (load(BARGE_SCENE) as PackedScene).instantiate() as Node3D
	# Held where the test puts it (no heave to the water level).
	barge.set("rides_waves", false)
	env.add_child(barge)
	var ride := VesselChip.new()
	barge.add_child(ride)
	ride.set_chip("Fôrflåte · fôrer", Color("#E8B92A"))
	var half_px := VesselChip.boat_reach_px(cam, view, cam.project_position(view * 0.5, depth), ride.vessel_half_length_m())
	barge.global_position = cam.project_position(Vector2(-0.5 * half_px, view.y * 0.5), depth)
	ride.position = Vector3.ZERO
	for i in 3:
		await t.get_tree().process_frame
	var bow_left := cam.unproject_position(ride.global_position).x + ride.edge_shift.x * kk - ride.get_size_px().x * kk * 0.5
	var stays := ride.on_screen and (ride.get_node("Pill") as Node3D).visible and bow_left >= VesselChip.EDGE_MARGIN_PX - 0.6
	barge.global_position = cam.project_position(Vector2(-1.5 * half_px - 20.0, view.y * 0.5), depth)
	for i in 3:
		await t.get_tree().process_frame
	var gone := not ride.on_screen and not (ride.get_node("Pill") as Node3D).visible
	t._check(is_equal_approx(ride.vessel_half_length_m(), 18.5) and half_px > 20.0 and stays and gone,
		"chips: a barge whose bow still shows keeps its chip slid in (anchor %.0f px past the edge, its half length %.0f px; pill left %.1f px), and loses it once the whole barge is out" % [
			0.5 * half_px, half_px, bow_left])
	barge.queue_free()


## The sun light carries the luminance the director gives it (its energy,
## the transmittance's against the white balance) at any elevation: its
## colour, sRGB-encoded as Light3D colours are, sets only the hue. A
## 3-degree midnight sun lost about three quarters of its light to a
## peak-normalised linear tint read as sRGB, and a 10-degree one about half.
func _check_sun_keeps_its_light(env: FjordEnvironment, director: EnvironmentDirector) -> void:
	var sun := env.get_node("Sun") as DirectionalLight3D
	var rows := []
	var kept := true
	for staging in [[69.65, 172.0, 0.5], [61.0, 268.0, 16.5], [60.39, 176.0, 13.0]]:
		env.set_latitude(staging[0])
		env.set_season(staging[1] / 365.0)
		env.set_time(staging[2])
		director.compose(0.0, true)
		var composed := director.get_composed()
		var energy := float(composed.get("sun_energy", 0.0))
		var lum := EnvironmentDirector.sun_color_luminance(sun.light_color)
		var light := lum * sun.light_energy
		kept = kept and energy > 0.0 and absf(light - energy) <= 0.002 * maxf(energy, 1.0)
		rows.append("%.1f deg: energy %.3f, light %.3f" % [float(composed.get("sun_elevation", 0.0)), energy, light])
	t._check(kept, "sun: the light keeps the luminance its energy gives at every elevation (%s)" % ", ".join(rows))
