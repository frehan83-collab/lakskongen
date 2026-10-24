extends SceneTree
## Weather drama and incident poses of the facilities (roadmap F2-24; farm-types
## spec §4, §5.4, §7.2 acceptance 1-4; not the sky's, G3-05's WeatherDramaChecks),
## checked on the real nodes, headless:
##
##   godot --headless --audio-driver Dummy --path godot -s res://tests/FacilityDramaChecks.gd
##
## 1. Spray appears only above Hs 3 m: the rule, then an OF1 unit's spray as
##    its sea changes (OffshoreUnitVisual.set_sea, the engine's forecast).
## 2. Nav-light phases are equal across units at night: the kit shader
##    flashes on the global TIME (pinned in tests/test_weather_drama.py).
## 3. A tilt shows a list of at most 3 degrees until resolved: the unit's
##    pose as the engine's open incident comes and goes (set_incident).
## 4. Vent steam only below -2 degC air: a planned campus's roof vents
##    (RoofSteam) as its halls stand and the air changes.
##
## Not part of the UI flow suite (its check count is frozen, SHIP_PLAN §3);
## tests/test_facility_drama.py runs it where Godot is installed.
## Prints FACILITY_DRAMA_RESULT checks=N failures=M and exits 1 on a failure.

var _checks := 0
var _failures: Array[String] = []


func _initialize() -> void:
	_run.call_deferred()


func _check(condition: bool, label: String) -> void:
	_checks += 1
	if condition:
		print("  ok   %s" % label)
	else:
		_failures.append(label)
		printerr("  FAIL %s" % label)


func _run() -> void:
	await process_frame
	_rules()
	_unit_spray_and_list()
	_roof_steam()
	print("FACILITY_DRAMA_RESULT checks=%d failures=%d" % [_checks, _failures.size()])
	for f in _failures:
		print("  failed: %s" % f)
	quit(1 if not _failures.is_empty() else 0)


# ── The rules ────────────────────────────────────────────────────────────────

func _rules() -> void:
	_check(FacilityDrama.spray_level(0.0) == 0.0 and FacilityDrama.spray_level(2.99) == 0.0 and FacilityDrama.spray_level(3.0) == 0.0,
		"rule: no spray at or under Hs 3 m")
	var l31 := FacilityDrama.spray_level(3.01)
	var l4 := FacilityDrama.spray_level(4.0)
	var l5 := FacilityDrama.spray_level(5.0)
	_check(l31 > 0.0 and l31 < l4 and l4 < l5 and l5 < 1.0,
		"rule: spray starts just over 3 m and grows with the sea (%.2f, %.2f, %.2f)" % [l31, l4, l5])
	_check(is_equal_approx(FacilityDrama.spray_level(6.0), 1.0) and is_equal_approx(FacilityDrama.spray_level(11.0), 1.0),
		"rule: spray is full from Hs 6 m")
	_check(FacilityDrama.list_target_deg("Tilt") > 0.0 and FacilityDrama.list_target_deg("Tilt") <= 3.0
			and FacilityDrama.list_target_deg("tilt") == FacilityDrama.list_target_deg("Tilt"),
		"rule: a tilt incident lists the unit by at most 3 degrees (%.1f)" % FacilityDrama.list_target_deg("Tilt"))
	_check(FacilityDrama.list_target_deg("") == 0.0 and FacilityDrama.list_target_deg("NetTear") == 0.0
			and FacilityDrama.list_target_deg("MooringFailure") == 0.0,
		"rule: no other incident, and none, lists the unit")
	_check(FacilityDrama.steam_level(15.0) == 0.0 and FacilityDrama.steam_level(-1.9) == 0.0 and FacilityDrama.steam_level(-2.0) == 0.0,
		"rule: no steam at or over -2 degC")
	var s21 := FacilityDrama.steam_level(-2.1)
	var s8 := FacilityDrama.steam_level(-8.0)
	_check(s21 > 0.0 and s21 < s8 and s8 < 1.0 and is_equal_approx(FacilityDrama.steam_level(-14.0), 1.0)
			and is_equal_approx(FacilityDrama.steam_level(-30.0), 1.0),
		"rule: steam starts just under -2 degC and thickens to full at -14 (%.2f, %.2f)" % [s21, s8])
	var pts := FacilityDrama.column_points(55.0)
	var on_ring := pts.size() == 12
	for p in pts:
		on_ring = on_ring and absf(Vector2(p.x, p.z).length() - 55.0) < 0.001 and is_equal_approx(p.y, FacilityDrama.SPRAY_EMIT_HEIGHT_M)
	_check(on_ring and pts[0].is_equal_approx(Vector3(55.0, 0.8, 0.0)) and pts[3].is_equal_approx(Vector3(0.0, 0.8, -55.0)),
		"rule: twelve column points at 55 m, vertex 0 on +X and vertex 3 on -Z (Blender +Y)")
	_check(FacilityDrama.vents_of("process_hall_bay").size() == 2 and FacilityDrama.vents_of("harvest_hall").size() == 5
			and FacilityDrama.vents_of("tank_growout").is_empty(),
		"rule: a hall bay has two roof vents, the harvest hall five, a tank none")


# ── An offshore unit: spray and list ─────────────────────────────────────────

func _sea(hs: float) -> Dictionary:
	return {"ok": true, "today": {"hs": hs, "tp": 10.0}, "limits": {}, "days": []}


func _unit_spray_and_list() -> void:
	var unit := OffshoreUnitVisual.new()
	root.add_child(unit)
	unit.build({"unit_id": "wd_unit", "family": "offshore", "variant": "of1", "plot_x": 0.0, "plot_z": 0.0, "operational": true}, 0.0)
	unit.set_process(false)
	unit.spray.set_process(false)
	var emitter := unit.spray.emitter
	_check(unit.spray.get_parent() == unit.unit and not emitter.emitting,
		"spray: the unit's spray rides its model and is quiet before it has a sea")
	var mat := emitter.process_material as ParticleProcessMaterial
	_check(mat.emission_shape == ParticleProcessMaterial.EMISSION_SHAPE_POINTS and mat.emission_point_count == 12
			and emitter.layers == PlanarReflection.NO_MIRROR_LAYER and emitter.draw_pass_1 != null,
		"spray: one emitter with a point at each of the 12 columns, out of the planar mirror")
	var rows := {}
	for hs: float in [2.0, 2.9, 3.0, 3.1, 4.8, 7.0]:
		unit.set_sea(_sea(hs))
		rows[hs] = [emitter.emitting, unit.spray.level]
	_check(not rows[2.0][0] and not rows[2.9][0] and not rows[3.0][0],
		"spray: none at Hs 2, 2.9 and 3.0 m")
	_check(rows[3.1][0] and rows[4.8][0] and rows[7.0][0] and rows[3.1][1] < rows[4.8][1] and is_equal_approx(rows[7.0][1], 1.0),
		"spray: from 3.1 m, growing to full at 7 m (levels %.2f, %.2f, %.2f)" % [rows[3.1][1], rows[4.8][1], rows[7.0][1]])
	unit.set_sea(_sea(2.0))
	_check(not emitter.emitting, "spray: it stops when the sea falls back under 3 m")
	unit.set_sea({"ok": false})
	unit.set_sea(_sea(5.0))
	unit.set_sea({"ok": false})
	_check(not emitter.emitting, "spray: a forecast the engine could not make asks for none")
	unit.set_wind(Vector2(10.0, 0.0))
	_check(mat.gravity.x > 0.0 and mat.gravity.y < 0.0, "spray: the wind carries the drops downwind")

	# The list.
	_check(is_zero_approx(unit.list_deg), "list: a unit with no incident stands upright")
	unit.set_incident("Tilt")
	var worst := 0.0
	var first := 0.0
	for step in 12:
		unit._process(0.5)
		if step == 0:
			first = unit.list_deg
		worst = maxf(worst, unit.list_deg)
	_check(first > 0.0 and first < unit.list_deg, "list: a tilt eases the unit over (%.2f deg after 0.5 s, %.2f after 6 s)" % [first, unit.list_deg])
	_check(is_equal_approx(unit.list_deg, FacilityDrama.LIST_DEG) and worst <= 3.0,
		"list: it stands at %.1f deg while the tilt is open, never past 3 (worst %.2f)" % [unit.list_deg, worst])
	_check(absf(unit.unit.rotation.z - deg_to_rad(unit.list_deg)) < 1e-5, "list: the model carries it as its roll (%.2f deg)" % rad_to_deg(unit.unit.rotation.z))
	unit.set_incident("NetTear")
	for step in 12:
		unit._process(0.5)
	_check(is_zero_approx(unit.list_deg), "list: another incident (a net tear) does not hold it over")
	unit.set_incident("Tilt")
	for step in 12:
		unit._process(0.5)
	unit.set_incident("")
	var mid := 0.0
	for step in 12:
		unit._process(0.5)
		if step == 0:
			mid = unit.list_deg
	_check(mid > 0.0 and mid < FacilityDrama.LIST_DEG and is_zero_approx(unit.list_deg),
		"list: once the incident is resolved the unit rights itself (%.2f deg after 0.5 s, %.2f after 6 s)" % [mid, unit.list_deg])
	unit.queue_free()


# ── A land campus: roof steam ────────────────────────────────────────────────

func _row(templates: Array) -> Dictionary:
	var built := {"status": "none", "phase_key": "operational", "phase_index": 6, "phase_count": 6, "phase_progress": 1.0,
		"progress": 1.0, "suspended": false, "waiting_reason": ""}
	var lots: Array = []
	for k in templates.size():
		lots.append({"lot_id": "wd/lot%d" % (k + 1), "template": templates[k], "tanks": [], "build": built, "expansion": {}})
	return {"unit_id": "wd_land", "family": "land", "variant": "hfs", "plot_id": "pad_a", "plot_x": 0.0, "plot_z": 0.0,
		"lots": lots, "build": built, "has_river_intake": false, "feeding": false}


func _count_pieces(campus: LandCampusBuilder, asset: String, group: String = "") -> int:
	var n := 0
	for g: String in campus.groups:
		if group != "" and g != group:
			continue
		for piece: Array in (campus.groups[g] as Dictionary).get("pieces", []):
			n += int(str(piece[0]) == asset)
	return n


func _roof_steam() -> void:
	var campus := LandCampusBuilder.new()
	campus.plan(_row(["hfs_lot", "hfs_pilot_lot", "ras_hall"]), {})
	var steam := RoofSteam.new()
	root.add_child(steam)
	steam.build(campus)
	var bays := _count_pieces(campus, "process_hall_bay")
	var halls := _count_pieces(campus, "harvest_hall")
	_check(bays == 6 + 2 + 18 and halls == 1 and steam.sources.size() == 2 * bays + 5 * halls,
		"steam: a campus of a full lot, a pilot lot and a RAS hall has %d vents (%d bays x2, %d harvest hall x5)" % [steam.sources.size(), bays, halls])
	steam.set_weather(-10.0, Vector2(2.0, 0.0))
	_check(not steam.emitter.emitting and steam.steaming_vents() == 0,
		"steam: nothing steams before a hall stands, however cold")
	steam.set_built({"harvest": true})
	_check(steam.active.size() == 5 and steam.emitter.emitting and steam.steaming_vents() == 5,
		"steam: the harvest hall's five vents steam once it stands, in -10 degC")
	steam.set_built({"harvest": true, "lot0": true})
	_check(steam.active.size() == 5 + 12 and steam.steaming_vents() == 17,
		"steam: a full lot's six bays add twelve (%d vents)" % steam.steaming_vents())
	steam.set_built(campus_built(campus))
	_check(steam.active.size() == steam.sources.size() and steam.emitter.amount == steam.sources.size() * RoofSteam.PER_VENT,
		"steam: every hall standing, every vent steams (%d, %d particles)" % [steam.active.size(), steam.emitter.amount])
	var warm := {}
	for air: float in [15.0, 0.0, -1.9, -2.0, -2.1, -8.0, -14.0]:
		steam.set_weather(air, Vector2(2.0, 0.0))
		warm[air] = [steam.emitter.emitting, steam.level]
	_check(not warm[15.0][0] and not warm[0.0][0] and not warm[-1.9][0] and not warm[-2.0][0],
		"steam: none at 15, 0, -1.9 and -2.0 degC")
	_check(warm[-2.1][0] and warm[-8.0][0] and warm[-14.0][0] and warm[-2.1][1] < warm[-8.0][1] and is_equal_approx(warm[-14.0][1], 1.0),
		"steam: from -2.1 degC, thickening to full at -14 (levels %.2f, %.2f, %.2f)" % [warm[-2.1][1], warm[-8.0][1], warm[-14.0][1]])
	var roof_up := true
	var pad_y := campus.pad_frame().origin.y
	for p in steam.active:
		roof_up = roof_up and p.y > pad_y + 10.0 and p.y < pad_y + 17.0
	_check(roof_up, "steam: every vent stands on a roof, 10-17 m over the pad")
	steam.set_weather(-8.0, Vector2(4.0, 0.0))
	var mat := steam.emitter.process_material as ParticleProcessMaterial
	_check(mat.gravity.y > 0.0 and mat.gravity.x > 0.0, "steam: it rises and leans downwind")
	campus.free()
	steam.queue_free()


## Every group of the campus standing whole (what LandCampusBuilder.built_groups returns once built).
func campus_built(campus: LandCampusBuilder) -> Dictionary:
	var out := {}
	for g: String in campus.groups:
		out[g] = true
	return out
