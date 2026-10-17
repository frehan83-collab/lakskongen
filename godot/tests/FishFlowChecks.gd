extends RefCounted
## Roadmap G3-01 (salmon schools on the GPU) and the farm's lights after
## dark, checked in the running game. UiFlowTests runs these through
## run(self) and counts them with its own _check.
##
## 1. Every pen has a school riding with it: the solid salmon and its shoal
##    (the school seen through the surface), the same per-fish data in both.
## 2. Per-fish data is written into the MultiMesh custom data once: frames of
##    swimming, feeding and LOD swaps leave every buffer as it was.
## 3. 1,500-4,000 fish a pen by biomass (none in an empty pen), within 400 m;
##    from above, the solid pass draws only the jumpers.
## 4. The sim drives the uniforms: feeding raises the school (its mean depth
##    falls) and quickens it, low oxygen slows it, the clocks never jump; the
##    feed barge feeds a site's pens in turn, in daylight only.
## 5. The fish cost under 0.3 ms a frame at 12 pens.
## 6. The shoal sees the water's own optics (a bloom reaches it).
## 7. Night lights: a photocell night level, 1 / exposure into every light,
##    ring and world label; the vessels' lights (COLREGS arcs, the barge's
##    mast light and yellow marker in step with the corner lights) show only
##    at night.

const GAME_SCENE := "res://scenes/GameScene.tscn"
const FISH_COST_MS := 0.3
const COST_PENS := 12
## The bloom's green scatter the water shader mixes toward (water_shader
## .gdshader, FishSchool.water_optics: vec3(0.006, 0.024, 0.010)).
const BLOOM_SCATTER_G := 0.024

var t: Node = null


func run(runner: Node) -> void:
	t = runner
	var scene: Node = await _open_scene("po3_red_sone")
	var env: Node = scene.get_node("FjordEnvironment")
	var pens: FishPenManager = env.get_node("FishPenManager")
	await _schools_ride_every_pen(pens)
	await _fish_data_written_once(env, pens)
	await _fish_count_and_range(env, pens)
	_sim_drives_the_school()
	await _barge_feeds_in_turn_by_day(env, pens)
	_fish_cost_at_twelve_pens(scene)
	await _shoal_sees_the_water(env)
	await _night_lights(env, pens)
	SiteLights.apply(0.0, 1.0, t.get_tree())
	scene.queue_free()
	await _frames(2)


func _check(ok: bool, label: String) -> void:
	t._check(ok, label)


func _frames(n: int) -> void:
	for i in n:
		await t.get_tree().process_frame


func _open_scene(scenario: String) -> Node:
	var sim: Node = t._sim
	sim.start_qa_scenario(scenario, 91)
	sim.set_paused(true)
	var scene: Node = (load(GAME_SCENE) as PackedScene).instantiate()
	t.add_child(scene)
	await _frames(5)
	sim.set_paused(true)
	sim.state_updated.emit()
	await _frames(3)
	return scene


static func _stocked(pens: FishPenManager) -> Array:
	var out: Array = []
	for sid in pens.get_pen_ids():
		if pens._pens[sid].get("from_sim", false):
			out.append(sid)
	return out


static func _custom(buffer: PackedFloat32Array, i: int) -> Vector4:
	var o := i * 16 + 12
	return Vector4(buffer[o], buffer[o + 1], buffer[o + 2], buffer[o + 3])


## 1. A school per pen under its cage, two passes, one set of fish.
func _schools_ride_every_pen(pens: FishPenManager) -> void:
	var sites := _stocked(pens)
	var ok := not sites.is_empty()
	var schools := 0
	for sid in sites:
		var cages := pens.get_site_cages(sid)
		var list: Array = pens._pens[sid]["fish_schools"]
		ok = ok and list.size() == cages.size()
		for k in cages.size():
			var school: MultiMeshInstance3D = list[k]
			var shoal := FishSchool.shoal_of(school)
			ok = ok and school.get_parent() == cages[k] and shoal != null \
				and school.material_override == FishSchool.SCHOOL_MATERIAL \
				and shoal.material_override == FishSchool.SHOAL_MATERIAL \
				and school.multimesh.use_custom_data and shoal.multimesh.use_custom_data \
				and school.multimesh.instance_count == FishSchool.MAX_FISH \
				and shoal.multimesh.instance_count == FishSchool.MAX_FISH \
				and school.multimesh.buffer == shoal.multimesh.buffer \
				and school.cast_shadow == GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
			schools += 1
	_check(ok and schools >= 10,
		"fish: every pen's school rides with its collar, the solid salmon and its shoal share one set of fish (%d schools)" % schools)
	var school: MultiMeshInstance3D = pens._pens[sites[0]]["fish_schools"][0] if ok else null
	var buffer: PackedFloat32Array = school.multimesh.buffer if school else PackedFloat32Array()
	var identity := buffer.size() == FishSchool.MAX_FISH * 16
	var in_unit := identity
	var jumpers_on_top := identity
	for i in (FishSchool.MAX_FISH if identity else 0):
		var o := i * 16
		identity = identity and buffer[o] == 1.0 and buffer[o + 5] == 1.0 and buffer[o + 10] == 1.0 \
			and buffer[o + 3] == 0.0 and buffer[o + 7] == 0.0 and buffer[o + 11] == 0.0 and buffer[o + 1] == 0.0
		var c := _custom(buffer, i)
		in_unit = in_unit and c.x >= 0.0 and c.x < 1.0 and c.y >= 0.0 and c.y < 1.0 and c.z >= 0.0 and c.z < 1.0 \
			and c.w >= 0.0 and c.w <= 1.0
		if i < FishSchool.JUMPERS:
			jumpers_on_top = jumpers_on_top and c.z < 0.15
	_check(identity and in_unit and jumpers_on_top,
		"fish: instance transforms are the identity; each fish's angle, ring, depth rank and seed sit in its custom data (jumpers at the top)")


## 2. No per-frame writes of per-fish data.
func _fish_data_written_once(env: Node, pens: FishPenManager) -> void:
	var cam: FjordCamera = env.get_node("FjordCamera")
	var sites := _stocked(pens)
	var before: Dictionary = {}
	var all: Array = []
	for sid in sites:
		for school in pens._pens[sid]["fish_schools"]:
			all.append(school)
	for school in all:
		before[school] = [school.multimesh.buffer, FishSchool.shoal_of(school).multimesh.buffer]
	cam.look_at_position(pens.get_site(sites[0]).pen_rest_position(0), FjordCamera.ZoomLevel.FARM)
	pens._pens[sites[0]]["is_feeding"] = true
	await _frames(40)
	var clock0: Vector4 = all[0].get_instance_shader_parameter("school_clock")
	await _frames(10)
	var clock1: Vector4 = all[0].get_instance_shader_parameter("school_clock")
	var same := true
	for school in all:
		var b: Array = before[school]
		same = same and school.multimesh.buffer == b[0] and FishSchool.shoal_of(school).multimesh.buffer == b[1]
	_check(same and clock1.x > clock0.x and clock1.y > clock0.y,
		"fish: 50 frames of swimming leave every school's per-fish buffer untouched; only the pen's clocks move (angle %.4f -> %.4f)"
			% [clock0.x, clock1.x])
	var code := (load("res://scripts/environment/FishSchool.gd") as GDScript).source_code
	_check(not ("set_instance_transform" in code) and not ("set_instance_custom_data" in code)
		and code.count(".buffer = ") == 1,
		"fish: FishSchool writes the per-fish buffer in one place, when a school is made, and never a transform")


## 3. Counts by biomass, drawn within 400 m.
func _fish_count_and_range(env: Node, pens: FishPenManager) -> void:
	var empty := FishSchool.fish_for(0, 0.0)
	var smolt := FishSchool.fish_for(12000, 1440.0)
	var half := FishSchool.fish_for(100000, 450000.0)
	var full := FishSchool.fish_for(200000, 1.2e6)
	_check(empty == 0 and smolt >= 1500 and smolt < 1520 and half > smolt and half < full and full == 4000,
		"fish: a pen shows 1,500-4,000 fish by its biomass and none when empty (%d, %d, %d, %d)" % [empty, smolt, half, full])
	var sites := _stocked(pens)
	var school: MultiMeshInstance3D = pens._pens[sites[0]]["fish_schools"][0]
	var shoal := FishSchool.shoal_of(school)
	var shown := FishSchool.shown_fish(school)
	var site: Dictionary = {}
	for s in t._sim.get_all_pens():
		if str(s.get("id", "")) == sites[0]:
			site = s
	var payload := FishSchool.payload(site, FishPenManager.pen_payload(site, 0, pens._pens[sites[0]]["pen_count"]))
	var expected := FishSchool.fish_for(int(payload["fish_count"]), float(payload["biomass_kg"]))
	_check(shown == expected and shown >= FishSchool.MIN_FISH and shown <= FishSchool.MAX_FISH,
		"fish: a stocked pen of the scenario shows %d fish for its %.0f kg" % [shown, float(payload["biomass_kg"])])
	pens.set_process(false)
	var cam := Camera3D.new()
	t.add_child(cam)
	cam.current = true
	var at := school.global_position
	cam.global_position = at + Vector3(0.0, 120.0, 250.0)
	FishSchool.animate(school, 0.016, cam)
	var near_ok := shoal.multimesh.visible_instance_count == shown \
		and school.multimesh.visible_instance_count == mini(FishSchool.JUMPERS, shown)
	cam.global_position = at + Vector3(0.0, 200.0, 460.0)
	FishSchool.animate(school, 0.016, cam)
	var far_ok := shoal.multimesh.visible_instance_count == 1 and school.multimesh.visible_instance_count == 0
	cam.global_position = at + Vector3(4.0, -5.0, 10.0)
	FishSchool.animate(school, 0.016, cam)
	var under_ok := school.multimesh.visible_instance_count == shown
	_check(near_ok and far_ok and under_ok and school.visibility_range_end == FishSchool.FISH_RANGE_M
		and absf(FishSchool.FISH_RANGE_M - 400.0) < 0.01,
		"fish: within 400 m the shoal draws every fish and the solid pass only the jumpers; past it only the pen's water; under the water the whole school")
	cam.queue_free()
	(env.get_node("FjordCamera") as Camera3D).current = true
	pens.set_process(true)
	await _frames(2)


static func _run(school: MultiMeshInstance3D, seconds: float, step: float = 0.05) -> void:
	var n := int(round(seconds / step))
	for i in n:
		FishSchool.animate(school, step, null)


static func _state(school: MultiMeshInstance3D) -> Vector4:
	return school.get_instance_shader_parameter("school_state")


static func _clock(school: MultiMeshInstance3D) -> Vector4:
	return school.get_instance_shader_parameter("school_clock")


## 4. Feeding raises the school, low oxygen slows it, the clocks run on.
func _sim_drives_the_school() -> void:
	var holder := Node3D.new()
	t.add_child(holder)
	var school := FishSchool.create("ProbeSchool", "probe#0")
	holder.add_child(school)
	FishSchool.apply_pen(school, {"fish_count": 50000, "mean_weight_kg": 3.0, "oxygen_mgl": 9.0})
	FishSchool.set_feeding(school, false)
	_run(school, 12.0)
	var calm := _state(school)
	var calm_shoal: Vector4 = FishSchool.shoal_of(school).get_instance_shader_parameter("school_state")
	FishSchool.set_feeding(school, true)
	var steps_ok := true
	var last := _clock(school)
	var speed_max := FishSchool.CRUISE_SPEED_MS * FishSchool.FEED_SPEED / FishSchool.SCHOOL_OUTER_M * 0.05 * 1.01
	for i in 160:
		FishSchool.animate(school, 0.05, null)
		var c := _clock(school)
		steps_ok = steps_ok and c.x > last.x and c.x - last.x <= speed_max and c.z > last.z
		last = c
	var fed := _state(school)
	_check(calm.x >= 4.5 and fed.x < calm.x - 2.0 and fed.w > 0.95 and fed.z > calm.z * 1.3
		and calm_shoal == calm,
		"fish: feeding raises the school (mean depth %.1f -> %.1f m) and quickens it (speed %.2f -> %.2f), in both passes"
			% [calm.x, fed.x, calm.z, fed.z])
	_check(steps_ok, "fish: the school's clocks run on smoothly while feeding starts (no jump in the angle swum)")
	FishSchool.set_feeding(school, false)
	_run(school, FishSchool.SINK_S + 2.0)
	var sunk := _state(school)
	_check(absf(sunk.x - calm.x) < 0.05 and sunk.w < 0.01,
		"fish: after the feed the school sinks back (%.1f m)" % sunk.x)
	FishSchool.apply_pen(school, {"fish_count": 50000, "mean_weight_kg": 3.0, "oxygen_mgl": 3.0})
	_run(school, 15.0)
	var a0 := _clock(school).x
	_run(school, 5.0)
	var low_rate := _clock(school).x - a0
	var low := _state(school)
	FishSchool.apply_pen(school, {"fish_count": 50000, "mean_weight_kg": 3.0, "oxygen_mgl": 9.0})
	_run(school, 15.0)
	a0 = _clock(school).x
	_run(school, 5.0)
	var good_rate := _clock(school).x - a0
	var good := _state(school)
	_check(low.z < 0.45 and good.z > 0.95 and low_rate < 0.5 * good_rate,
		"fish: low oxygen slows the school (3 mg/L: speed %.2f, 9 mg/L: %.2f)" % [low.z, good.z])
	FishSchool.apply_pen(school, {"fish_count": 0, "mean_weight_kg": 0.0})
	FishSchool.set_feeding(school, true)
	_run(school, 6.0)
	var body: Vector4 = school.get_instance_shader_parameter("school_body")
	_check(FishSchool.shown_fish(school) == 0 and body.w == 0.0 and _state(school).w == 0.0,
		"fish: an empty pen shows no fish and nothing rises to the feed")
	FishSchool.apply_pen(school, {"fish_count": 200000, "mean_weight_kg": 5.0})
	var big: Vector4 = school.get_instance_shader_parameter("school_body")
	FishSchool.apply_pen(school, {"fish_count": 200000, "mean_weight_kg": 0.1})
	var small: Vector4 = school.get_instance_shader_parameter("school_body")
	_check(big.x > 0.7 and small.x < 0.25 and big.w == FishSchool.MAX_FISH,
		"fish: the fish are as long as the pen's mean weight makes them (%.2f m at 5 kg, %.2f m at 0.1 kg)" % [big.x, small.x])
	# Wave 3 ui-a: a day's growth too small to see writes nothing to the GPU
	# (+0.2 % weight: 0.4 mm of fork length); a visible one is written at once.
	FishSchool.apply_pen(school, {"fish_count": 200000, "mean_weight_kg": 5.0})
	var w0: int = FishSchool.body_writes
	FishSchool.apply_pen(school, {"fish_count": 200000, "mean_weight_kg": 5.01})
	var unseen: Vector4 = school.get_instance_shader_parameter("school_body")
	var w1: int = FishSchool.body_writes
	FishSchool.apply_pen(school, {"fish_count": 200000, "mean_weight_kg": 5.25})
	var seen: Vector4 = school.get_instance_shader_parameter("school_body")
	_check(w1 == w0 and is_equal_approx(unseen.x, FishSchool.length_for(5.0))
			and FishSchool.body_writes == w1 + 1 and is_equal_approx(seen.x, FishSchool.length_for(5.25)),
		"fish: a change too small to see writes no uniform, a visible one is written (%d, %d writes; %.4f -> %.4f m)" % [
			w1 - w0, FishSchool.body_writes - w1, unseen.x, seen.x])
	var steps := {}
	for kg in range(0, 900001, 3000):
		steps[FishSchool.fish_for(1000, float(kg))] = true
	_check(steps.size() == (FishSchool.MAX_FISH - FishSchool.MIN_FISH) / FishSchool.FISH_STEP + 1,
		"fish: the shown count moves in %d-fish steps (%d counts from empty to full)" % [FishSchool.FISH_STEP, steps.size()])
	holder.queue_free()


## 4. The barge feeds a site's pens in turn, in daylight only.
func _barge_feeds_in_turn_by_day(env: Node, pens: FishPenManager) -> void:
	var sid: String = _stocked(pens)[0]
	var n: int = pens._pens[sid]["pen_count"]
	pens._pens[sid]["is_feeding"] = true
	var seen: Array = []
	for k in n:
		pens._feed_clock = (float(k) + 0.5) * FishPenManager.FEED_SLOT_S
		seen.append(pens.feeding_pen(sid))
	pens._pens[sid]["is_feeding"] = false
	var off := pens.feeding_pen(sid)
	var turn_ok := off == -1
	for k in n:
		turn_ok = turn_ok and seen[k] == k
	_check(turn_ok, "fish: the barge feeds the site's %d pens in turn, %.0f s each, and none while the sim does not feed" % [n, FishPenManager.FEED_SLOT_S])
	pens._pens[sid]["is_feeding"] = true
	pens._feed_clock = 0.0
	var env3d: FjordEnvironment = env as FjordEnvironment
	env3d.set_time(12.0)
	await _frames(4)
	# By day exactly the pen whose turn it is is fed.
	var fed_now := pens.feeding_pen(sid)
	var fed_count := 0
	for k in n:
		if bool(FishSchool._state(pens._pens[sid]["fish_schools"][k]).get("feeding", false)):
			fed_count += 1
	var day_fed: bool = fed_count == 1 and fed_now >= 0 \
		and bool(FishSchool._state(pens._pens[sid]["fish_schools"][fed_now]).get("feeding", false))
	env3d.set_time(23.5)
	env3d.set_season(0.04)
	await _frames(4)
	var night_fed := false
	for school in pens._pens[sid]["fish_schools"]:
		night_fed = night_fed or bool(FishSchool._state(school).get("feeding", false))
	_check(day_fed and not night_fed, "fish: by day the barge feeds the one pen whose turn it is, and none in the dark")
	env3d.set_season(0.5)
	env3d.set_time(12.0)
	await _frames(2)


## 5. 12 pens' schools cost under 0.3 ms a frame.
func _fish_cost_at_twelve_pens(scene: Node) -> void:
	var holder := Node3D.new()
	scene.add_child(holder)
	var cam := Camera3D.new()
	holder.add_child(cam)
	cam.global_position = Vector3(0.0, 80.0, 160.0)
	var schools: Array = []
	for k in COST_PENS:
		var school := FishSchool.create("CostSchool%d" % k, "cost#%d" % k)
		holder.add_child(school)
		school.position = Vector3((k % 4) * 60.0 - 90.0, 0.0, (k / 4) * 60.0 - 60.0)
		FishSchool.apply_pen(school, {"fish_count": 150000, "mean_weight_kg": 3.0, "oxygen_mgl": 8.0})
		schools.append(school)
	var times: Array = []
	for frame in 80:
		var t0 := Time.get_ticks_usec()
		for k in schools.size():
			FishSchool.set_feeding(schools[k], k == frame / 20)
			FishSchool.animate(schools[k], 0.016, cam)
		times.append(float(Time.get_ticks_usec() - t0) / 1000.0)
	times = times.slice(10)
	times.sort()
	var median: float = times[times.size() / 2]
	_check(median < FISH_COST_MS, "fish: %d pens' schools cost %.3f ms a frame (median; budget %.1f ms)" % [COST_PENS, median, FISH_COST_MS])
	holder.queue_free()


## 6. The shoal sees the water's optics, bloom and all.
func _shoal_sees_the_water(env: Node) -> void:
	var director: EnvironmentDirector = env.get_node("EnvironmentDirector")
	director.set_water_state(8.0, 0.0)
	await _frames(3)
	var clear := _vec3_param("deep_scatter")
	var expect := FishSchool.water_optics(director.water_material())
	var sigma := _vec3_param("absorption")
	director.set_water_state(8.0, 0.8)
	await _frames(3)
	var bloom := _vec3_param("deep_scatter")
	var expect_bloom := FishSchool.water_optics(director.water_material())
	# G3-05 moods set the water's body (deep_scatter) per mood, so the green a
	# bloom adds is measured against the way to the bloom's own green (the
	# water shader's mix toward 0.024 by 0.85 x algae: 68 % at 0.8), which
	# holds under every mood; on the pre-mood body it is the old 1.5x and more.
	var toward := (bloom.y - clear.y) / maxf(BLOOM_SCATTER_G - clear.y, 1e-6)
	_check(clear.is_equal_approx(expect[1]) and sigma.is_equal_approx(expect[0]) and bloom.is_equal_approx(expect_bloom[1])
		and bloom.y > clear.y and toward >= 0.65,
		"fish: the shoal sees through the water's own optics, clear and in a bloom, and a bloom greens it (scatter g %.4f -> %.4f, %.0f %% of the way to the bloom's green)"
			% [clear.y, bloom.y, toward * 100.0])
	director.set_water_state(8.0, 0.0)
	await _frames(2)


## 7. The farm after dark.
func _night_lights(env: Node, pens: FishPenManager) -> void:
	_check(SiteLights.night_for(30.0, 0.0) == 0.0 and SiteLights.night_for(-10.0, 0.0) == 1.0
		and SiteLights.night_for(3.0, 1.0) > SiteLights.night_for(3.0, 0.0),
		"night: the lights switch on at dusk, earlier under a heavy sky")
	var env3d := env as FjordEnvironment
	var director: EnvironmentDirector = env.get_node("EnvironmentDirector")
	env3d.set_season(0.5)
	env3d.set_time(12.0)
	await _frames(4)
	var lamp: ShaderMaterial = SiteLights.LIGHT_MATERIALS[0]
	var vessels := _vessels(env)
	var day_dark := float(lamp.get_shader_parameter("night")) == 0.0
	for v in vessels:
		var lights := v.get_node_or_null(VesselLights.LIGHTS_NAME) as MultiMeshInstance3D
		day_dark = day_dark and lights != null and not lights.visible
	env3d.set_season(0.04)
	env3d.set_time(23.5)
	await _frames(4)
	var exposure := float(director.get_composed().get("exposure", 1.0))
	var night_on := float(lamp.get_shader_parameter("night")) > 0.99 and exposure > 2.0
	for mat in SiteLights.LIGHT_MATERIALS + SiteLights.SIGNAL_MATERIALS:
		night_on = night_on and absf(float(mat.get_shader_parameter("exposure_comp")) * exposure - 1.0) < 0.03
	for v in vessels:
		night_on = night_on and (v.get_node(VesselLights.LIGHTS_NAME) as Node3D).visible
	_check(day_dark and night_on and vessels.size() >= 4,
		"night: lamps, windows and %d vessels' lights are dark by day and on at night, every light and ring at 1 / exposure (%.1fx)"
			% [vessels.size(), exposure])
	var label := pens.get_site_label(_stocked(pens)[0])
	var base: Color = label.get_meta(SiteLights.BASE_COLOR)
	var lin := label.modulate.srgb_to_linear()
	var base_lin := base.srgb_to_linear()
	_check(label.is_in_group(SiteLights.WORLD_TEXT_GROUP) and absf(lin.r * exposure / base_lin.r - 1.0) < 0.05
		and absf(lin.g * exposure / base_lin.g - 1.0) < 0.05,
		"night: a site's name keeps its colour on screen at the night exposure (no bloom)")
	_check_vessel_lights(vessels)
	env3d.set_season(0.5)
	env3d.set_time(12.0)
	await _frames(3)


func _check_vessel_lights(vessels: Array) -> void:
	var boat_ok := false
	var barge_ok := false
	var marker_period := float((SiteLights.LIGHT_MATERIALS[0] as ShaderMaterial).get_shader_parameter("period_s"))
	for v in vessels:
		var lights := VesselLights.lights_for(v)
		var a := VesselLights.anchors(v)
		if v.asset_id == "workboat":
			var bridge: AABB = a["bridge"]
			var kinds := {}
			var ok := lights.size() == 4
			for l in lights:
				var at: Vector3 = l["at"]
				var c: Color = l["colour"]
				if c == VesselLights.GREEN:
					ok = ok and at.x > 0.5 and float(l["centre"]) > 0.0 and absf(float(l["half"]) - deg_to_rad(56.25)) < 1e-3
					kinds["green"] = true
				elif c == VesselLights.RED:
					ok = ok and at.x < -0.5 and float(l["centre"]) < 0.0
					kinds["red"] = true
				elif float(l["centre"]) == PI:
					ok = ok and at.z > 0.3 * v.length_m and absf(float(l["half"]) - deg_to_rad(67.5)) < 1e-3
					kinds["stern"] = true
				else:
					ok = ok and at.y >= bridge.end.y and absf(float(l["half"]) - deg_to_rad(112.5)) < 1e-3
					kinds["masthead"] = true
			boat_ok = ok and kinds.size() == 4
		elif v.asset_id.begins_with("feed_barge"):
			var white_top := false
			var marker := false
			var deck := 0
			for l in lights:
				var c: Color = l["colour"]
				if c == VesselLights.YELLOW:
					marker = float(l["period"]) == marker_period and float(l["half"]) >= 3.1
				elif float(l["bright"]) == VesselLights.DECK_BRIGHT:
					deck += 1
				elif c == VesselLights.WHITE:
					white_top = float(l["half"]) >= 3.1 and (l["at"] as Vector3).y >= (a["mast_top"] as Vector3).y
			barge_ok = barge_ok or (white_top and marker and deck == 4)
	_check(boat_ok, "night: the workboat shows a masthead light over its wheelhouse, green to starboard, red to port and a stern light, each in its COLREGS arc")
	_check(barge_ok and is_equal_approx(marker_period, VesselLights.MARKER_PERIOD_S)
		and VesselLights.YELLOW.is_equal_approx(Color("#FFC23A")),
		"night: the feed barge shows an all-round mast light, deck lamps and a yellow #FFC23A marker flashing with the corner lights")


static func _vessels(root: Node) -> Array:
	var out: Array = []
	var stack: Array = [root]
	while not stack.is_empty():
		var n: Node = stack.pop_back()
		if n is VesselModel:
			out.append(n)
		stack.append_array(n.get_children())
	return out


## A vec3 uniform of the shoal material (-1s when it was never set).
static func _vec3_param(name: String) -> Vector3:
	var v = FishSchool.SHOAL_MATERIAL.get_shader_parameter(name)
	return v if v is Vector3 else Vector3(-1.0, -1.0, -1.0)
