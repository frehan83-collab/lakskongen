extends RefCounted
## F2-21 (farm-types spec §4 "Camera" and "Wave driver", §7.2 acceptance
## 1-6): camera targets, bookmarks and the offshore sea, in the real game,
## headless. UiFlowTests runs these through run(self) and counts them with
## its own _check.
##
## 1. Every land pad and offshore slot is a focus target; the focus and the
##    camera reach each one (hav_1 lies 3.5 km beyond the world's focus
##    clamp), and the world clamp still holds away from them.
## 2. The land framing at -35 to -45 deg shows the whole campus (the real
##    camera, and the fit for every lot count pad_a and pad_b allow); the OF1
##    framing fills 25-45 % of the frame's width.
## 3. The horizon sits 10-25 % from the top at the FJORD and COAST defaults
##    and at the OF1 and Havfarm-class framings.
## 4. At a target Hs of 4 m the drawn open sea carries Σa² = Hs²/8 within
##    10 % across the wind and swell bands, the swell at 150-300 m; the
##    fjord gets none of it; the units ride it (WaveField = the shader's sum).
##    The presenter hands the director the engine's split of the targeted
##    unit's sea.
## 5. The unit's chip reads the engine's Hs within 0.05 m, with the status
##    glyph its limits give; at the home view it hides in clear weather and
##    shows in rain, snow and a storm (spec §5.5 rule 2).
## 6. G (the camera_bookmark action), pressed as a player does, cycles
##    Fjord -> Land -> Hav -> Fjord and names each; it leaves a text field's
##    letters and a covered world alone; none of it changes the engine.

const SCENARIO := "po3_red_sone"
const SEED := 91
## Steps of a large delta that settle the camera rig (k = 1 - e^-24 per step).
const SETTLE_STEPS := 8
const SETTLE_DELTA := 2.0
## A target's focus is reached within this many metres.
const REACH_M := 1.0
## Acceptance bands.
const OF1_FILL := Vector2(0.25, 0.45)
const HORIZON := Vector2(0.10, 0.25)
const LAND_PITCH := Vector2(-45.0, -35.0)
const TARGET_HS := 4.0
const TARGET_SWELL_HS := 2.5
const TARGET_SWELL_TP := 12.0
const SPECTRUM_TOLERANCE := 0.10
const SWELL_BAND := Vector2(150.0, 300.0)
const CHIP_HS_TOLERANCE := 0.05
## A point well away from the world and every target, and where the clamp puts it.
const FAR_POINTS := [Vector3(3000.0, 0.0, 0.0), Vector3(-3200.0, 0.0, 900.0), Vector3(0.0, 0.0, 3400.0)]

var t: Node = null


func run(runner: Node) -> void:
	t = runner
	var sim: Node = t._sim
	_check(sim.start_qa_scenario(SCENARIO, SEED), "F2-21: seeded scenario starts")
	sim.set_paused(true)
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	t.add_child(scene)
	for i in 5:
		await t.get_tree().process_frame
	sim.set_paused(true)
	var env: Node = scene.get_node("FjordEnvironment")
	var cam: FjordCamera = env.get_node("FjordCamera")
	var manager: FarmFacilityVisualManager = env.get_node("FarmFacilityVisualManager")
	var terrain: FjordTerrain = env.get_node("Terrain")
	var director: EnvironmentDirector = env.get("director")
	cam.auto_orbit_enabled = false
	for i in 3:
		await t.get_tree().process_frame

	_plot_targets(cam, terrain)
	var bridge := t.get_node("/root/GameBridge")
	var placed: Dictionary = bridge.QaPlaceFarmTypeSet(1, 2, 2)
	_check(bool(placed.get("success", false)), "F2-21: the QA set (an HFS lot, post-smolt, an OF1 and a Havfarm-class unit) is built (%s)" % placed.get("message", ""))
	# The open sea's first day: a forecast to read.
	sim.advance_days(1)
	sim.set_paused(true)
	sim.state_updated.emit()
	for i in 3:
		await t.get_tree().process_frame
	manager._refresh_seas()
	manager._update_sea_target()
	var land_id := str(placed.get("land_id", ""))
	var off_ids: Array = placed.get("offshore_ids", [])
	var off_id := str(off_ids[0]) if not off_ids.is_empty() else ""
	var havfarm_id := str(off_ids[1]) if off_ids.size() > 1 else ""
	var digest: String = sim.qa_state_digest()
	_framings(cam, manager, land_id, off_id, havfarm_id)
	_fits_every_campus(manager, terrain, sim)
	_spectrum(director, manager, off_id, sim)
	_chip(cam, manager, env, off_id, sim)
	await _bookmarks_by_input(scene, cam, manager, land_id, off_id)
	_check(sim.qa_state_digest() == digest, "F2-21: framing, bookmarks, the open sea and the chip leave the engine's state unchanged")
	scene.queue_free()
	await t.get_tree().process_frame


func _check(condition: bool, label: String) -> void:
	t._check(condition, label)


## Settle the rig on its targets (no tween: the framings were set with duration 0).
static func settle(cam: FjordCamera) -> void:
	for i in SETTLE_STEPS:
		cam._process(SETTLE_DELTA)


## Back to the home view at a zoom level, settled.
static func home(cam: FjordCamera, zoom: FjordCamera.ZoomLevel) -> void:
	cam.recentre()
	cam.set_zoom_level(zoom)
	cam.fly_to_position(cam.home_focus, zoom, 0.0)
	settle(cam)


## Where the horizon (the horizontal through the camera) crosses the frame,
## as a share of its height from the top.
static func horizon_row(cam: Camera3D) -> float:
	var forward := -cam.global_transform.basis.z
	forward.y = 0.0
	var far := cam.global_position + forward.normalized() * 100000.0
	return cam.unproject_position(far).y / cam.get_viewport().get_visible_rect().size.y


## Acceptance 1: every pad and slot is a target the focus and the camera reach.
func _plot_targets(cam: FjordCamera, terrain: FjordTerrain) -> void:
	var plots: Array = []
	for pad: Dictionary in terrain.land_pad_slots():
		plots.append(["plot:" + str(pad["id"]), pad["centre"]])
	for slot: Dictionary in terrain.offshore_slots():
		plots.append(["plot:" + str(slot["id"]), slot["position"]])
	_check(plots.size() >= 6, "F2-21: the terrain reserves %d plots (3 pads, 3 slots)" % plots.size())
	var reached: Array[String] = []
	var missed: Array[String] = []
	var beyond := 0
	for entry: Array in plots:
		var id: String = entry[0]
		if not cam.has_focus_target(id):
			missed.append(id + " (no target)")
			continue
		var target := cam.get_focus_target(id)
		cam.focus_target(id, 0.0)
		settle(cam)
		var focus := cam.get_focus_point()
		var at: Vector3 = entry[1]
		var off := Vector2(focus.x - at.x, focus.z - at.z).length()
		var stand := cam.global_position.distance_to(target["pos"])
		if off <= REACH_M and absf(stand - float(target["distance"])) <= 0.15 * float(target["distance"]):
			reached.append(id)
		else:
			missed.append("%s (focus off %.1f m, camera %.0f m for %.0f)" % [id, off, stand, float(target["distance"])])
		if absf(at.x) > 1700.0 or absf(at.z) > 1700.0:
			beyond += 1
	_check(missed.is_empty(), "F2-21: the focus and the camera reach every pad and slot (%d: %s; missed %s)"
		% [reached.size(), ", ".join(reached), ", ".join(missed)])
	_check(beyond >= 3, "F2-21: %d of the plots lie beyond the world's focus clamp and are reached" % beyond)
	var held := true
	for p: Vector3 in FAR_POINTS:
		var clamped := cam._clamp_to_world(p)
		var half := cam._world_half()
		held = held and absf(clamped.x) <= half.x + 0.01 and absf(clamped.z) <= half.y + 0.01
	var cam_far := cam._clamp_camera(Vector3(12000.0, 300.0, 0.0))
	held = held and absf(cam_far.x) <= cam._camera_half().x + 0.01
	_check(held, "F2-21: away from the targets the world's clamps still hold (focus and camera)")
	_check(cam.get_bookmark("land") == "plot:pad_a" and cam.get_bookmark("hav") == "plot:off_1",
		"F2-21: with no unit, Land is the hero pad and Hav the hero slot (%s, %s)" % [cam.get_bookmark("land"), cam.get_bookmark("hav")])


## Acceptance 2 and 3 on the real camera.
func _framings(cam: FjordCamera, manager: FarmFacilityVisualManager, land_id: String, off_id: String, havfarm_id: String) -> void:
	var size := cam.get_viewport().get_visible_rect().size
	# The land framing: -35 to -45 deg, the whole campus in frame.
	var campus := manager.visual_of(land_id) as LandCampusBuilder
	_check(campus != null and cam.focus_target(land_id, 0.0), "F2-21: the land unit is a focus target")
	if campus != null:
		settle(cam)
		var fwd := -cam.global_transform.basis.z
		var pitch := rad_to_deg(asin(fwd.y))
		var corners := FarmFacilityVisualManager.box_corners(campus.blast_rect(), LandCampusBuilder.PAD_TOP_Y, 16.0)
		var inside := 0
		for p: Vector3 in corners:
			var s := cam.unproject_position(p)
			if not cam.is_position_behind(p) and s.x >= 0.0 and s.y >= 0.0 and s.x <= size.x and s.y <= size.y:
				inside += 1
		_check(pitch >= LAND_PITCH.x and pitch <= LAND_PITCH.y and inside == corners.size(),
			"F2-21: the land framing looks %.1f deg down from %.0f m and shows the whole campus (%d of %d corners in frame)"
			% [pitch, cam.get_current_distance(), inside, corners.size()])
	# The OF1 framing: 25-45 % of the frame's width, the horizon 10-25 % down.
	var unit := manager.visual_of(off_id) as OffshoreUnitVisual
	_check(unit != null and cam.focus_target(off_id, 0.0), "F2-21: the OF1 unit is a focus target")
	if unit != null:
		settle(cam)
		var lo := INF
		var hi := -INF
		var c := unit.unit.global_position
		for k in 48:
			var a := TAU * float(k) / 48.0
			for y: float in [0.0, unit.unit.air_draught_m]:
				var s := cam.unproject_position(c + Vector3(cos(a), 0.0, sin(a)) * unit.unit.circumradius_m + Vector3(0.0, y, 0.0))
				lo = minf(lo, s.x)
				hi = maxf(hi, s.x)
		var fill := (hi - lo) / size.x
		_check(fill >= OF1_FILL.x and fill <= OF1_FILL.y, "F2-21: the OF1 framing fills %.1f %% of the frame's width (25-45 %%)" % (fill * 100.0))
		var row := horizon_row(cam)
		_check(row >= HORIZON.x and row <= HORIZON.y, "F2-21: at the OF1 framing the horizon sits %.1f %% from the top" % (row * 100.0))
		# Looking in toward the mouth: the coast lies ahead, beyond the unit.
		var ahead := -cam.global_transform.basis.z
		_check(ahead.z > 0.5, "F2-21: the OF1 framing looks in toward the coast (heading z %.2f)" % ahead.z)
	if havfarm_id != "" and cam.focus_target(havfarm_id, 0.0):
		settle(cam)
		var row := horizon_row(cam)
		_check(row >= HORIZON.x and row <= HORIZON.y, "F2-21: at the Havfarm-class framing (%.0f m, %.0f deg) the horizon sits %.1f %% from the top"
			% [FarmFacilityVisualManager.HAVFARM_FRAMING.x, FarmFacilityVisualManager.HAVFARM_FRAMING.y, row * 100.0])
	else:
		_check(false, "F2-21: the Havfarm-class unit's plot is a focus target")
	for zoom in [FjordCamera.ZoomLevel.FJORD, FjordCamera.ZoomLevel.COAST]:
		home(cam, zoom)
		var row := horizon_row(cam)
		_check(row >= HORIZON.x and row <= HORIZON.y, "F2-21: at the %s default the horizon sits %.1f %% from the top"
			% ["FJORD" if zoom == FjordCamera.ZoomLevel.FJORD else "COAST", row * 100.0])


## Acceptance 2 for every campus the engine allows on the hero pads: the
## fitted distance shows all of it at -40 deg.
func _fits_every_campus(manager: FarmFacilityVisualManager, terrain: FjordTerrain, sim: Node) -> void:
	var bad: Array[String] = []
	var checked := 0
	var limits := {}
	for plot: Dictionary in sim.get_land_plots():
		limits[str(plot["id"])] = int(plot.get("grow_out_lots", 0))
	for pad: Dictionary in terrain.land_pad_slots():
		var lots := int(limits.get(str(pad["id"]), 0))
		for n in range(1, lots + 1):
			var row := {"unit_id": "fit", "family": "land", "variant": "hfs", "plot_id": pad["id"], "lots": []}
			for k in n:
				var tanks: Array = []
				for i in 14:
					tanks.append({"id": "t%d_%d" % [k, i], "number": i + 1, "role": "grow_out" if i < 12 else "pre_grow_out"})
				row["lots"].append({"lot_id": "lot%d" % k, "template": "hfs_lot", "tanks": tanks})
			var campus := LandCampusBuilder.new()
			campus.plan(row, pad)
			var focus := campus.focus_point()
			var corners := FarmFacilityVisualManager.box_corners(campus.blast_rect(), LandCampusBuilder.PAD_TOP_Y, 16.0)
			# The presenter's own framing of it: its yaw, its distance, the pitch the rig really takes.
			var framing := manager.land_framing(pad, focus, corners)
			var pitch := manager.solved_pitch(focus, framing.y, framing.x, -40.0)
			checked += 1
			if framing.x >= FarmFacilityVisualManager.FIT_MAX_M or pitch < LAND_PITCH.x or pitch > LAND_PITCH.y \
					or not FarmFacilityVisualManager.frames_all(corners, focus, framing.y, -40.0, framing.x, 16.0 / 9.0, 0.0):
				bad.append("%s %d lots (%.0f m, %.1f deg)" % [pad["id"], n, framing.x, pitch])
			campus.free()
	_check(checked >= 5 and bad.is_empty(), "F2-21: every grow-out campus the pads allow (%d) fits its land framing, the rig at -35 to -45 deg (%s)" % [checked, ", ".join(bad)])


## Acceptance 4: the spectrum the director draws for a target Hs of 4 m, and
## the presenter's own target.
func _spectrum(director: EnvironmentDirector, manager: FarmFacilityVisualManager, off_id: String, sim: Node) -> void:
	var forecast: Dictionary = sim.get_sea_state_forecast(off_id)
	var today: Dictionary = forecast.get("today", {})
	var target: Dictionary = director.open_sea.target
	_check(str(target.get("plot_id", "")) == str(forecast.get("plot_id", "-")) and is_equal_approx(float(target.get("hs", -1.0)), float(today.get("hs", -2.0))),
		"F2-21: the open sea draws the targeted unit's plot sea (%s, Hs %.2f m = the engine's %.2f m)"
		% [target.get("plot_id", ""), float(target.get("hs", -1.0)), float(today.get("hs", -2.0))])
	var parts := sqrt(pow(float(today.get("wind_sea_hs", 0.0)), 2.0) + pow(float(today.get("swell_total_hs", 0.0)), 2.0))
	_check(absf(parts - float(today.get("hs", 0.0))) < 1e-3, "F2-21: the bridge's split of today's sea adds up in energy (%.3f vs %.3f m)" % [parts, float(today.get("hs", 0.0))])
	manager.set_process(false)
	var wind_hs := sqrt(TARGET_HS * TARGET_HS - TARGET_SWELL_HS * TARGET_SWELL_HS)
	director.set_target_sea({"hs": TARGET_HS, "wind_sea_hs": wind_hs, "swell_total_hs": TARGET_SWELL_HS,
		"swell_tp": TARGET_SWELL_TP, "plot_id": "test"})
	director.compose(0.0, true)
	var total := 0.0
	var swell := 0.0
	var swell_sheltered := 0.0
	var outside: Array[String] = []
	for comp: Dictionary in director.drawn_components():
		var e := float(comp["exposed"])
		total += e * e
		if str(comp["kind"]) == "swell":
			swell += e * e
			swell_sheltered += absf(float(comp["sheltered"]))
			var l := float(comp["wavelength"])
			if l < SWELL_BAND.x or l > SWELL_BAND.y:
				outside.append("%.0f m" % l)
	var want := TARGET_HS * TARGET_HS / 8.0
	_check(absf(total - want) <= SPECTRUM_TOLERANCE * want, "F2-21: at Hs 4 m the drawn bands carry Σa² %.3f m² (Hs²/8 = %.3f, ±10 %%)" % [total, want])
	_check(outside.is_empty() and absf(swell - TARGET_SWELL_HS * TARGET_SWELL_HS / 8.0) <= 0.01 * want,
		"F2-21: the swell's energy (%.3f m²) sits at 150-300 m (%s)" % [swell, ", ".join(outside) if not outside.is_empty() else "all in band"])
	_check(swell_sheltered == 0.0, "F2-21: the sheltered fjord gets none of the swell")
	var shader_swell = director.water_material().get_shader_parameter("swell_amp")
	_check(shader_swell is PackedVector4Array and (shader_swell as PackedVector4Array).size() == 4
		and director.wave_field.swell_amp == director.open_sea.swell_amp(),
		"F2-21: the shader's swell uniforms and the CPU wave field get the director's swell")
	# The units ride the swell: the CPU field's sum at an open-sea point is the
	# shader's wind sum plus its swell terms, computed here from the uniforms.
	var field := director.wave_field
	var probe := Vector3(-450.0, 0.0, -2750.0)
	var with_swell := field.displacement(probe.x, probe.z)
	var keep_k := field.swell_k
	var keep_a := field.swell_amp
	field.set_swell(PackedVector4Array(), PackedVector4Array())
	var without := field.displacement(probe.x, probe.z)
	field.set_swell(keep_k, keep_a)
	var reach := field.grid_reach(probe.x, probe.z)
	var expected := 0.0
	for i in keep_k.size():
		var row: Vector4 = keep_a[i]
		var a := row.y * smoothstep(WaveField.WAVE_MIN_SPACINGS * reach.x, WaveField.WAVE_FULL_SPACINGS * reach.x, row.w)
		expected += a * sin(keep_k[i].x * probe.x + keep_k[i].y * probe.z - row.z)
	expected *= reach.y
	var added := with_swell.y - without.y
	_check(reach.y > 0.0 and absf(expected) > 1e-3 and absf(added - expected) < 1e-3,
		"F2-21: the units ride the swell: the CPU field adds the shader's swell term (%.3f m, expected %.3f m)" % [added, expected])
	director.set_target_sea({})
	director.compose(0.0, true)
	_check(director.open_sea.swell_hs() < 1e-6, "F2-21: with no target the open coast draws no swell")
	manager.set_process(true)
	manager._update_sea_target()


## Acceptance 5 and spec §5.5 rule 2: the chip.
func _chip(cam: FjordCamera, manager: FarmFacilityVisualManager, env: Node, off_id: String, sim: Node) -> void:
	var unit := manager.visual_of(off_id) as OffshoreUnitVisual
	if unit == null:
		_check(false, "F2-21: the OF1 unit is drawn for its chip")
		return
	# A chip made before its first text (the sea chip is made hidden and waits
	# for its unit's first forecast) reads size zero: gfx-polish-1's slide that
	# keeps a chip on screen reads it every frame (the wave4b merge's SCRIPT
	# ERROR, VesselChip.get_size_px).
	var blank := VesselChip.new()
	_check(blank.get_size_px() == Vector2.ZERO, "F2-21: a chip with no text yet reads size zero for the on-screen slide (%s)" % str(blank.get_size_px()))
	blank.free()
	var forecast: Dictionary = sim.get_sea_state_forecast(off_id)
	unit.set_sea(forecast)
	cam.focus_target(off_id, 0.0)
	settle(cam)
	unit._refresh_chip(unit._camera_distance())
	var text := unit.chip.get_text()
	var m := RegEx.create_from_string("Hs (\\d+)[,.](\\d) m").search(text)
	var shown := float("%s.%s" % [m.get_string(1), m.get_string(2)]) if m != null else -1.0
	var engine_hs := float((forecast.get("today", {}) as Dictionary).get("hs", -9.0))
	_check(unit.chip.visible and absf(shown - engine_hs) <= CHIP_HS_TOLERANCE,
		"F2-21: at the OF1 framing the chip reads the engine's Hs (%s; engine %.2f m)" % [text, engine_hs])
	_check(int(unit.chip.get_shape()) == unit.sea_status() and unit.chip.get_accent() == OffshoreUnitVisual.STATUS_COLORS[unit.sea_status()],
		"F2-21: the chip's glyph is its status, colour and shape (%d)" % unit.sea_status())
	# The home view: hidden in clear weather, shown in rain, snow and a storm.
	home(cam, FjordCamera.ZoomLevel.FJORD)
	var weather: WeatherController = env.get("weather_system")
	var duration := weather.transition_duration
	weather.transition_duration = 0.0
	var shown_in := {}
	for state in [WeatherController.WeatherState.CLEAR, WeatherController.WeatherState.RAIN,
			WeatherController.WeatherState.SNOW, WeatherController.WeatherState.STORM]:
		weather.set_weather(state)
		(env.get("director") as EnvironmentDirector).compose(0.0, true)
		unit._refresh_chip(unit._camera_distance())
		shown_in[state] = unit.chip.visible
	weather.transition_duration = duration
	weather.set_weather(WeatherController.WeatherState.CLEAR)
	_check(not shown_in[WeatherController.WeatherState.CLEAR], "F2-21: at the home view in clear weather the unit reads by itself (no chip)")
	_check(shown_in[WeatherController.WeatherState.RAIN] and shown_in[WeatherController.WeatherState.SNOW] and shown_in[WeatherController.WeatherState.STORM],
		"F2-21: at the home view the chip shows in rain, snow and a storm (%s)" % str(shown_in.values()))
	for c: Color in OffshoreUnitVisual.STATUS_COLORS:
		_check(_delta_e(c, OffshoreUnitVisual.HULL_YELLOW) >= 20.0, "F2-21: the chip colour %s stands >= 20 delta E off the hull yellow (%.1f)" % [c.to_html(false), _delta_e(c, OffshoreUnitVisual.HULL_YELLOW)])


## Acceptance 6: G as the player presses it.
func _bookmarks_by_input(scene: Node, cam: FjordCamera, manager: FarmFacilityVisualManager, land_id: String, off_id: String) -> void:
	var ui: Control = scene.get_node("UILayer/GameUI")
	var hint: Node = scene.get_node("UILayer/CameraHint")
	ui.show_tab("fjord")
	await t.get_tree().process_frame
	t.get_viewport().gui_release_focus()
	cam.recentre()
	_check(cam.get_bookmark("land") == land_id and cam.get_bookmark("hav") == off_id,
		"F2-21: with units, Land and Hav are the company's own (%s, %s)" % [cam.get_bookmark("land"), cam.get_bookmark("hav")])
	var seen: Array[String] = []
	var wanted := {"land": land_id, "hav": off_id, "fjord": ""}
	for slot: String in ["land", "hav", "fjord"]:
		await t._press_key(KEY_G, 103)
		# The camera flies there: wait out its tween, then read where it went.
		for i in 600:
			if cam._fly_tween == null or not cam._fly_tween.is_running():
				break
			await t.get_tree().process_frame
		var target_id: String = wanted[slot]
		var focus := cam.get_focus_point()
		var at: Vector3 = cam.home_focus if target_id == "" else (cam.get_focus_target(target_id)["pos"] as Vector3)
		var caption: String = hint.call("bookmark_caption")
		var place := manager.target_name(target_id) if target_id != "" else ""
		var ok := cam.current_bookmark() == slot and Vector2(focus.x - at.x, focus.z - at.z).length() <= REACH_M \
			and caption != "" and (place == "" or caption.contains(place))
		if slot == "fjord":
			# The home view at the FJORD default, whatever framing G left.
			ok = ok and is_equal_approx(cam.get_zoom_distance(), float(FjordCamera.ZOOM_DISTANCE[FjordCamera.ZoomLevel.FJORD]))
		seen.append("%s:%s" % [cam.current_bookmark(), caption])
		_check(ok, "F2-21: G takes the camera to %s (%s; caption '%s')" % [slot, target_id if target_id != "" else "home", caption])
		if slot == "land":
			# The caption stands where the player sees it: above the camera hint
			# at the bottom, clear of the HUD's top bars (drawn over the hint's layer).
			var chip := hint.get_node("%BookmarkChip") as Control
			var view := t.get_viewport().get_visible_rect()
			var r := chip.get_global_rect()
			_check(chip.is_visible_in_tree() and view.encloses(r) and r.position.y > view.size.y * 0.5,
				"F2-21: the caption shows on screen above the camera hint, clear of the top bars (%s in %s)" % [str(r), str(view.size)])
	# A text field keeps its letters; a covered world takes no camera key.
	var field := LineEdit.new()
	scene.get_node("UILayer").add_child(field)
	field.grab_focus()
	var before := cam.current_bookmark()
	await t._press_key(KEY_G, 103)
	_check(cam.current_bookmark() == before and field.text.to_lower().contains("g"), "F2-21: G typed into a text field stays text ('%s')" % field.text)
	field.queue_free()
	t.get_viewport().gui_release_focus()
	ui.show_tab("dashboard")
	await t.get_tree().process_frame
	await t._press_key(KEY_G, 103)
	_check(cam.current_bookmark() == before, "F2-21: G behind an opaque panel leaves the camera alone")
	ui.show_tab("fjord")
	await t.get_tree().process_frame


## CIE76 delta E of two sRGB colours.
static func _delta_e(a: Color, b: Color) -> float:
	return _lab(a).distance_to(_lab(b))


static func _lab(c: Color) -> Vector3:
	var l := c.srgb_to_linear()
	var x := (0.4124 * l.r + 0.3576 * l.g + 0.1805 * l.b) / 0.95047
	var y := 0.2126 * l.r + 0.7152 * l.g + 0.0722 * l.b
	var z := (0.0193 * l.r + 0.1192 * l.g + 0.9505 * l.b) / 1.08883
	var fx := _f(x)
	var fy := _f(y)
	var fz := _f(z)
	return Vector3(116.0 * fy - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz))


static func _f(v: float) -> float:
	return pow(v, 1.0 / 3.0) if v > 0.008856 else 7.787 * v + 16.0 / 116.0
