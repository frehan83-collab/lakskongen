extends RefCounted
## F2-23 (farm-types spec §6.4, §7.2 acceptance 1-3; §5.4 night lighting):
## the first sight in the real game, headless. UiFlowTests runs these through
## run(self) and counts them with its own _check.
##
## 1. A land site the engine builds coming into operation is the company's
##    first of its variant: its sequence plays, at most 20 s, opening with a
##    3-5 s construction timelapse (the campus stands unbuilt at its start,
##    whole at its end); it holds the clock, the sim day and every stream
##    stand still; the lid work-lights come on as the camera passes; Esc
##    skips it and gives the camera back on the campus's own framing.
## 2. A second land site of the variant gets the toast only.
## 3. A unit that starts in operation (a commissioned or loaded unit) plays
##    nothing.
## 4. With reduced motion the offshore unit's first sight is a still: the
##    frame cross-fades for 2 s, the camera does not move; a click skips it.
## 5. Night lighting: at dusk the kits' lights, the lids' work-light pools
##    and the yard's lamps and pools are on; by day they are off.

const SCENARIO := "po3_red_sone"
const SEED := 91
const MAX_SECONDS := 20.0
const TIMELAPSE := Vector2(3.0, 5.0)
const CROSSFADE_S := 2.0

var t: Node = null


func run(runner: Node) -> void:
	t = runner
	var sim: Node = t._sim
	FirstSightDirector.force_auto_play(true)
	MotionPrefs.force(false)
	_check(sim.start_qa_scenario(SCENARIO, SEED), "F2-23: seeded scenario starts")
	sim.set_paused(true)
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	t.add_child(scene)
	for i in 5:
		await t.get_tree().process_frame
	sim.set_paused(true)
	var env: Node = scene.get_node("FjordEnvironment")
	var cam: FjordCamera = env.get_node("FjordCamera")
	var manager: FarmFacilityVisualManager = env.get_node("FarmFacilityVisualManager")
	var director: FirstSightDirector = manager.first_sight
	var bridge := t.get_node("/root/GameBridge")
	cam.auto_orbit_enabled = false

	# 3. A commissioned unit starts in operation: no first sight.
	var placed: Dictionary = bridge.QaPlaceFarmTypeSet(0, 2, 0)
	sim.state_updated.emit()
	for i in 3:
		await t.get_tree().process_frame
	_check(bool(placed.get("success", false)) and director.played.is_empty() and not director.playing,
		"F2-23: a unit that starts in operation (commissioned at once) plays no first sight")

	# 1. The company's first HFS site, built by the engine, comes into operation.
	var started: Dictionary = bridge.QaStartLandBuild("pad_a", "hfs_lot")
	var land_id := str(started.get("unit_id", ""))
	_check(bool(started.get("success", false)), "F2-23: an HFS site is under construction on pad_a (%s)" % started.get("message", ""))
	bridge.QaStageConstruction(land_id, "completion", 0.95)
	sim.state_updated.emit()
	for i in 2:
		await t.get_tree().process_frame
	bridge.QaStageConstruction(land_id, "operational", 1.0)
	var day: int = sim.current_day
	var digest: String = sim.qa_state_digest()
	sim.state_updated.emit()
	for i in 3:
		await t.get_tree().process_frame
	_check(director.playing and director.unit_id == land_id and director.family == "land" and not director.reduced,
		"F2-23: the first HFS site in operation plays its first sight (%s)" % director.unit_id)
	_check(director.duration <= MAX_SECONDS + 1e-3, "F2-23: the sequence lasts %.1f s (<= 20 s)" % director.duration)
	var window: Array = director._plan.get("timelapse_seconds", [])
	var opens := window.size() == 2 and float(window[0]) == 0.0 and float(window[1]) - float(window[0]) >= TIMELAPSE.x and float(window[1]) - float(window[0]) <= TIMELAPSE.y
	_check(opens, "F2-23: it opens with a %s s construction timelapse (3-5 s from the start)" % str(window))
	_check(sim.is_clock_held(FirstSightDirector.CLOCK_HOLD), "F2-23: the sequence holds the clock")
	var ui: CanvasLayer = scene.get_node("UILayer")
	_check(not ui.visible and director.overlay != null, "F2-23: the game's UI steps aside for the sequence")
	director.manual = true
	var campus := manager.visual_of(land_id) as LandCampusBuilder
	director.seek(0.0)
	var early_whole := campus.whole_tanks()
	var early_blast := campus.blast_state()
	director.seek(float(window[1]) * 0.5 if opens else 2.0)
	var mid_whole := campus.whole_tanks()
	director.seek(float(window[1]) + 0.1 if opens else 5.0)
	var late_whole := campus.whole_tanks()
	var ready := 0
	for lot: Dictionary in manager.views[land_id].get("lots", []):
		for tank: Dictionary in lot.get("tanks", []):
			ready += 1 if bool(tank.get("ready", false)) else 0
	_check(early_whole == 0 and early_blast == 0.0 and mid_whole < ready and late_whole == ready and ready > 0,
		"F2-23: the timelapse builds the campus from the bare pad (%d tanks whole, blast %.2f; %d halfway) to the engine's %d handed-over tanks (%d)"
		% [early_whole, early_blast, mid_whole, ready, late_whole])
	var lights_at: Array = director._plan.get("lights_on_seconds", [])
	director.seek(float(lights_at[0]) - 1.0)
	manager._update_lights()
	var dark := Vector2i(campus.lights.lamps_in_wave(), campus.lights.lit_pools())
	director.seek(lerpf(float(lights_at[0]), float(lights_at[1]), 0.4))
	var half := campus.lights.lamps_in_wave()
	director.seek(float(lights_at[1]) + 0.5)
	manager._update_lights()
	var lit := Vector2i(campus.lights.lamps_in_wave(), campus.lights.lit_pools())
	_check(dark == Vector2i.ZERO and half > 0 and half < lit.x and lit.x == campus.lights.lit_lamps() and lit.y > 0,
		"F2-23: the campus lights come on in a wave from the quay as the camera passes (lamps %d -> %d -> %d of %d; pools %d -> %d)"
		% [dark.x, half, lit.x, campus.lights.lit_lamps(), dark.y, lit.y])
	# The clock held: a clock running at 30x (18 days in 0.6 s) does not move the sim day.
	director.manual = false
	sim.set_time_speed(30.0)
	sim.set_paused(false)
	await t.get_tree().create_timer(0.6).timeout
	_check(sim.current_day == day and director.time > 0.0, "F2-23: while it plays the sim day stands (day %d at 30x; %.2f s in)" % [sim.current_day, director.time])
	sim.set_paused(true)
	sim.set_time_speed(1.0)
	# Esc skips it, as a player presses it.
	await t._press_key(KEY_ESCAPE)
	await t.get_tree().process_frame
	var target := cam.get_focus_target(land_id)
	var focus := cam.get_focus_point()
	var at: Vector3 = target.get("pos", Vector3.INF)
	_check(not director.playing and not sim.is_clock_held(FirstSightDirector.CLOCK_HOLD) and ui.visible,
		"F2-23: Esc skips the sequence: the clock and the UI are given back")
	_check(Vector2(focus.x - at.x, focus.z - at.z).length() < 1.0 and is_equal_approx(cam.get_zoom_distance(), float(target.get("distance", -1.0))),
		"F2-23: the camera is left on the campus's own framing (%.0f m)" % cam.get_zoom_distance())
	_check(sim.current_day == day and sim.qa_state_digest() == digest,
		"F2-23: the sim day and every stream stand where they stood (day %d, the state digest unchanged)" % sim.current_day)

	# 2. A second HFS site: the toast only.
	var second: Dictionary = bridge.QaStartLandBuild("pad_b", "hfs_lot")
	var second_id := str(second.get("unit_id", ""))
	bridge.QaStageConstruction(second_id, "completion", 0.95)
	sim.state_updated.emit()
	await t.get_tree().process_frame
	bridge.QaStageConstruction(second_id, "operational", 1.0)
	sim.state_updated.emit()
	for i in 3:
		await t.get_tree().process_frame
	_check(not director.playing and director.played.size() == 1 and director.toasts.size() == 1,
		"F2-23: a second HFS site in operation gets a toast only (%s)" % ", ".join(director.toasts))

	# 4. Reduced motion: the offshore unit's first sight is a still.
	MotionPrefs.force(true)
	var contracted: Dictionary = bridge.QaContractOffshore("off_1", "of1")
	var off_id := str(contracted.get("unit_id", ""))
	bridge.QaStageConstruction(off_id, "commissioning", 0.9)
	sim.state_updated.emit()
	await t.get_tree().process_frame
	bridge.QaStageConstruction(off_id, "operational", 1.0)
	digest = sim.qa_state_digest()
	sim.state_updated.emit()
	for i in 3:
		await t.get_tree().process_frame
	_check(director.playing and director.unit_id == off_id and director.reduced and director.family == "hav",
		"F2-23: with reduced motion the offshore unit's first sight plays as a still")
	director.manual = true
	director.seek(0.0)
	var fade_start: float = director.overlay.still.modulate.a if director.overlay != null else -1.0
	director.seek(CROSSFADE_S)
	var fade_end: float = director.overlay.still.modulate.a if director.overlay != null else -1.0
	var pose_a := cam.global_transform
	director.seek(CROSSFADE_S + 2.0)
	var pose_b := cam.global_transform
	_check(is_equal_approx(fade_start, 1.0) and fade_end <= 0.001 and pose_a.is_equal_approx(pose_b) and director.duration <= 8.0,
		"F2-23: the still cross-fades in %.1f s (alpha %.2f -> %.2f), the camera holds, %.1f s in all" % [CROSSFADE_S, fade_start, fade_end, director.duration])
	director.manual = false
	# A click skips it.
	var view := t.get_viewport().get_visible_rect().size
	await t._click_at(Vector2(view.x * 0.75, view.y * 0.35))
	await t.get_tree().process_frame
	_check(not director.playing and not sim.is_clock_held(FirstSightDirector.CLOCK_HOLD), "F2-23: a click skips the still")
	_check(sim.qa_state_digest() == digest, "F2-23: the reduced sequence changes nothing in the engine either")

	# 5. Night lighting: by day off, at dusk on.
	var ts: TimeOfDay = env.get("time_system")
	ts.paused = true
	env.call("set_time", 12.0)
	manager._update_lights()
	var day_lamps := campus.lights.lit_lamps()
	var day_level := manager.light_level()
	var hour := director.hour_for_sun(ts.latitude_deg, ts.sim_day, -4.0)
	env.call("set_time", hour)
	manager._update_lights()
	var kit_night := float(FarmFacilityVisualManager.FACILITY_KIT.get_shader_parameter("night_light"))
	_check(day_level == 0.0 and day_lamps == 0, "F2-23: by day the campus's lights are off (level %.2f, %d lamps)" % [day_level, day_lamps])
	_check(manager.light_level() > 0.9 and kit_night > 0.5 and campus.lights.lit_lamps() > 0 and campus.lights.lit_pools() > 0,
		"F2-23: at dusk (%.1f h) the windows, lids and yard are lit (level %.2f, kit %.2f, %d lamps, %d pools)"
		% [hour, manager.light_level(), kit_night, campus.lights.lit_lamps(), campus.lights.lit_pools()])
	MotionPrefs.force(null)
	FirstSightDirector.force_auto_play(null)
	scene.queue_free()
	await t.get_tree().process_frame


func _check(condition: bool, label: String) -> void:
	t._check(condition, label)
