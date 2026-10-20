extends RefCounted
## Roadmap G2-04 (multi-pen sites in a mooring grid) and the A2-01 site-kit
## completion, checked in the running game. UiFlowTests runs these through
## run(self) and counts them with its own _check.
##
## 1. The pens drawn for every site of every shipped scenario are the
##    engine's pens (GetSeaSiteDetails "pen_count", the scenario's "pens").
## 2. A site is a grid along the channel: one pen per 70-100 m cell, cells
##    CELL_M apart, deep water under every net, sites clear of each other.
## 3. One feed barge per stocked site, its white hoses from its hull to every
##    pen's port, clear of every collar, with yellow floats; the workboat
##    inside the grid.
## 4. Clicks reach each pen: a pen's click selects its site and that pen.
## 5. Past LOD0 a site draws its pens through one MultiMesh per part: each
##    pen in exactly one LOD by its own distance, the MultiMesh transforms
##    are the riding pens', and the draw sources do not grow with the pens.
## 6. The floating gear rides the drawn water: the shaders get the
##    director's very waves and the site's water; the shader's height (its
##    CPU twin) meets WaveField.height_at.
## 7. Per-pen data (S2-01b) drives each pen's ring; without it the site's.

const SCENARIOS_PATH := "res://data/scenarios.json"
const GAME_SCENE := "res://scenes/GameScene.tscn"

var t: Node = null


func run(runner: Node) -> void:
	t = runner
	await _pen_counts_match_every_scenario()
	await _site_grid_barge_and_hoses()
	await _clicks_reach_each_pen()
	await _floating_gear_rides_the_water()
	_pen_payload_drives_each_ring()
	await _ghost_site_and_variants()


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
	return scene


func _payload(site_id: String) -> Dictionary:
	for site in t._sim.get_all_pens():
		if str(site.get("id", "")) == site_id:
			return site
	return {}


## 1. Every scenario's sites: drawn pens == engine pens == the scenario's.
func _pen_counts_match_every_scenario() -> void:
	var data = JSON.parse_string(FileAccess.get_file_as_string(SCENARIOS_PATH))
	var scene: Node = await _open_scene("po3_red_sone")
	var pens: FishPenManager = scene.get_node("FjordEnvironment/FishPenManager")
	var all_ok := data is Array
	var sites_seen := 0
	var pens_seen := 0
	var report := []
	for scen in (data if data is Array else []):
		if not t._sim.start_qa_scenario(str(scen.get("id", "")), 91):
			continue
		t._sim.set_paused(true)
		t._sim.state_updated.emit()
		await _frames(1)
		for site in scen.get("starting_sites", []):
			var sid := str(site.get("id", ""))
			# A site has the pens of its locality layout (S2-01b); "pens" counts
			# the stocked populations it starts with.
			var layout = site.get("pen_layout", {})
			var want := int(layout.get("count", 0)) if layout is Dictionary else 0
			if want <= 0:
				want = int(site.get("pens", 1))
			var stocked_ok := int(site.get("pens", 1)) <= want
			var engine := int(_payload(sid).get("pen_count", -1))
			var drawn := pens.get_site_cages(sid).size()
			var mooring := pens.get_pen_mooring(sid)
			var cells := mooring.cells_x * mooring.cells_z if mooring != null else 0
			# Up to three pens lie in one row along the channel, more in two.
			var rows_ok := mooring != null and ((want <= 3 and mooring.cells_x == 1 and mooring.cells_z == want)
				or (want > 3 and mooring.cells_x == 2 and mooring.cells_z == ceili(want / 2.0)))
			var ok := engine == want and drawn == want and cells >= want and cells < want + 2 and rows_ok and stocked_ok
			all_ok = all_ok and ok
			sites_seen += 1
			pens_seen += drawn
			if not ok:
				report.append("%s: scenario %d, engine %d, drawn %d, cells %d" % [sid, want, engine, drawn, cells])
	_check(all_ok and sites_seen >= 15,
		"sites: every scenario site draws the engine's pens, in one row up to three, else two rows along the channel (%d sites, %d pens) %s"
			% [sites_seen, pens_seen, ", ".join(report)])
	scene.queue_free()
	await _frames(2)


## 2 and 3. Grid along the channel, cells, depth, spacing between sites,
## one barge per stocked site, its hoses, the workboat.
func _site_grid_barge_and_hoses() -> void:
	var scene: Node = await _open_scene("po3_red_sone")
	var env: Node = scene.get_node("FjordEnvironment")
	var pens: FishPenManager = env.get_node("FishPenManager")
	var terrain: FjordTerrain = env.get_node("Terrain")
	var barges: FeedBargeVisualManager = env.get_node("FeedBargeVisualManager")
	barges.tow_seconds = 0.05
	t._sim.state_updated.emit()
	await t.get_tree().create_timer(0.3).timeout
	var ids := pens.get_pen_ids()
	var along_ok := not ids.is_empty()
	var cells_ok := not ids.is_empty()
	var deep_ok := not ids.is_empty()
	var shallowest := INF
	var spans: Array = []
	for sid in ids:
		var farm := pens.get_site(sid)
		var at := pens.get_pen_position(sid)
		along_ok = along_ok and absf(wrapf(farm.global_rotation.y - pens.channel_yaw_at(at.z), -PI, PI)) < 1e-4
		var rest := farm.pen_local_positions()
		var dims := SiteLayout.grid_dims(farm.pen_count)
		var cells := SiteLayout.pen_cells(farm.pen_count)
		for k in rest.size():
			cells_ok = cells_ok and rest[k].distance_to(farm.grid.cell_center(cells[k].x, cells[k].y)) < 0.01
			for j in rest.size():
				var d: Vector3 = rest[j] - rest[k]
				if j != k and (absf(d.x) < 0.01 or absf(d.z) < 0.01):
					var steps := (absf(d.x) + absf(d.z)) / MooringGrid.CELL_M
					cells_ok = cells_ok and absf(steps - roundf(steps)) < 1e-4 and steps >= 0.99
			var p := farm.pen_rest_position(k)
			var depth := -terrain.get_height_at(p.x, p.z)
			shallowest = minf(shallowest, depth)
			deep_ok = deep_ok and depth > 40.0
		cells_ok = cells_ok and farm.grid.cells_x == dims.x and farm.grid.cells_z == dims.y
		var half := farm.half_extent()
		spans.append(Vector2(at.z - half.y, at.z + half.y))
	_check(along_ok, "sites: every site's grid lies along the fjord channel")
	_check(cells_ok, "sites: one pen at the centre of each of its cells, neighbours %.0f m apart" % MooringGrid.CELL_M)
	_check(deep_ok, "sites: every net hangs over deep water (shallowest %.0f m under a pen, net 38 m)" % shallowest)
	var apart := true
	spans.sort_custom(func(a: Vector2, b: Vector2) -> bool: return a.x < b.x)
	for i in range(1, spans.size()):
		apart = apart and spans[i].x - spans[i - 1].y >= SiteLayout.SITE_GAP_M - 1.0
	_check(apart and spans.size() == 2, "sites: neighbouring sites' grids keep at least %.0f m of water between them" % SiteLayout.SITE_GAP_M)

	# One barge per stocked site, hoses to every pen.
	var stocked: Array = []
	for site in t._sim.get_all_pens():
		if int(site.get("fish_count", 0)) > 0:
			stocked.append(str(site.get("id", "")))
	var barge_ids := barges.active_site_ids()
	barge_ids.sort()
	stocked.sort()
	var one_each := barge_ids == stocked and not stocked.is_empty()
	for sid in stocked:
		one_each = one_each and env.find_children("FeedBarge_%s" % sid, "", true, false).size() == 1
	_check(one_each, "feed barge: one barge per stocked site (%s)" % ", ".join(barge_ids))
	var hoses_ok := not stocked.is_empty()
	var clear := INF
	var report := []
	for sid in stocked:
		var farm := pens.get_site(sid)
		var barge := barges.barge_of(sid) as VesselModel
		var lines: Array[PackedVector2Array] = farm.hoses.hoses
		var ok := barge != null and lines.size() == farm.pen_count and farm.has_hoses()
		if ok:
			var b_local := farm.to_local(barge.global_position)
			var side := signf(b_local.x)
			var inner_x := b_local.x - side * barge.beam_m * 0.5
			var rest := farm.pen_local_positions()
			for k in lines.size():
				var line := lines[k]
				var start := line[0]
				var end := line[line.size() - 1]
				var cage := farm.get_cage(k)
				var port := farm.to_local(cage.to_global(Vector3(SiteLayout.PORT_R, 0.0, 0.0)))
				ok = ok and absf(start.x - inner_x) < 0.5 and absf(start.y - b_local.z) <= barge.length_m * 0.5
				# Square off the gate: the bundle leaves the hull and runs
				# straight in, never along the hull to its end.
				ok = ok and absf(b_local.z - SiteBerths.barge_gate_z(farm.pen_count)) < 1.0
				ok = ok and end.distance_to(Vector2(port.x, port.z)) < 0.6
				for p in line:
					for r in rest:
						clear = minf(clear, p.distance_to(Vector2(r.x, r.z)))
		hoses_ok = hoses_ok and ok
		var hose := farm.hoses.get_hose_instance()
		hoses_ok = hoses_ok and hose != null and hose.material_override == FeedHoses.HOSE_MATERIAL \
			and hose.cast_shadow == GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		var floats := farm.hoses.floats.size()
		hoses_ok = hoses_ok and floats >= 2
		report.append("%s %d hoses %.0f m, %d floats" % [sid, lines.size(), farm.hoses.total_length(), floats])
	_check(hoses_ok, "feed hoses: one white hose from the barge's side to each pen's port, with floats (%s)" % ", ".join(report))
	_check(clear >= 27.0, "feed hoses: every hose keeps off every collar (closest %.1f m from a pen centre, collar 26.8 m)" % clear)
	var float_colour := not stocked.is_empty()
	for sid in stocked:
		var mmis := pens.get_site(sid).hoses.get_float_instances()
		float_colour = float_colour and mmis.size() == 3
		for mmi in mmis:
			float_colour = float_colour and mmi.multimesh.use_colors \
				and mmi.multimesh.instance_count == pens.get_site(sid).hoses.floats.size() \
				and MultiMeshPack.color_of(mmi.multimesh, 0).is_equal_approx(FeedHoses.FLOAT_COLOR.srgb_to_linear())
	_check(float_colour, "feed hoses: the hose floats are buoy yellow (#E8B92A), at three LODs")
	var player_buoys := not ids.is_empty()
	for sid in ids:
		var grid := pens.get_pen_mooring(sid)
		for mmi in grid.get_buoy_instances():
			for k in grid.buoy_positions.size():
				player_buoys = player_buoys and MultiMeshPack.color_of(mmi.multimesh, k).is_equal_approx(MooringGrid.BUOY_YELLOW.srgb_to_linear())
	_check(player_buoys, "site kit: the player's grid buoys are buoy yellow (#E8B92A)")

	# The workboat lies inside its site's grid.
	var boats_in := true
	for sid in ids:
		var farm := pens.get_site(sid)
		var boat := pens.get_node_or_null("PenMarkers/%s_boat" % sid) as Node3D
		var local := farm.to_local(boat.global_position) if boat else Vector3.INF
		var half := farm.half_extent()
		boats_in = boats_in and boat != null and absf(local.x) < half.x and absf(local.z) < half.y
	_check(boats_in, "sites: each site's workboat lies moored inside its grid")

	# A barge sent home takes its hoses; a new one brings them back.
	var sid0: String = stocked[0] if not stocked.is_empty() else ""
	barges._start_return(sid0)
	var gone := not pens.get_site(sid0).has_hoses() if sid0 != "" else false
	await t.get_tree().create_timer(0.3).timeout
	t._sim.state_updated.emit()
	await t.get_tree().create_timer(0.4).timeout
	_check(gone and barges.barge_state(sid0) == "moored" and pens.get_site(sid0).has_hoses(),
		"feed hoses: the hoses leave with the barge and come back with the next one")

	# The engine gives a site more pens (S2-01b): it is laid out again and
	# its barge moors at the new, wider grid's berth with a hose per pen.
	var grown: Array = []
	for site in t._sim.get_all_pens():
		var copy: Dictionary = site.duplicate()
		if str(copy.get("id", "")) == sid0:
			copy["pen_count"] = 12
		grown.append(copy)
	pens.sync_pens_from_sim(grown)
	await _frames(2)
	var big := pens.get_site(sid0)
	var moored := barges.barge_of(sid0) as VesselModel
	var regap := -1.0
	if moored != null and big != null:
		regap = absf(big.to_local(moored.global_position).x) - moored.beam_m * 0.5 - big.half_extent().x
	_check(big != null and big.pen_count == 12 and barges.barge_state(sid0) == "moored" and absf(regap - FishPenManager.BARGE_GRID_GAP_M) < 1.0
		and big.hoses.hoses.size() == 12,
		"feed barge: a site laid out again (12 pens) has its barge moored at the new grid's berth (%.1f m off the frame) with 12 hoses" % regap)
	scene.queue_free()
	await _frames(2)


## 4. A pen's click selects its site and that pen; a click between pens the
## nearest pen.
func _clicks_reach_each_pen() -> void:
	var scene: Node = await _open_scene("po3_red_sone")
	var pens: FishPenManager = scene.get_node("FjordEnvironment/FishPenManager")
	var vista: FjordVistaBridge = scene.get_node("FjordVistaBridge")
	var ui: Control = scene.get_node("UILayer/GameUI")
	await t._click_control(ui.find_child("Tab_fjord", true, false))
	var sid := "po3_nord"
	var farm := pens.get_site(sid)
	var last := farm.pen_count - 1
	var target := farm.pen_rest_position(last)
	await t._frame_pen(vista.vista_camera, target)
	var cam := vista.vista_camera
	await t._click_at(cam.unproject_position(target))
	var marker := pens.active_indicator
	_check(pens.selected_pen_id == sid and pens.selected_pen_index == last,
		"pen click: clicking pen %d selects site %s and that pen (%s #%d)" % [last + 1, sid, pens.selected_pen_id, pens.selected_pen_index])
	_check(marker.visible and Vector2(marker.global_position.x - target.x, marker.global_position.z - target.z).length() < 1.0,
		"pen click: the bright ring lies round the clicked pen")
	var others := pens.site_indicator.multimesh
	var quiet := pens.site_indicator.visible and others.instance_count == farm.pen_count \
		and MultiMeshPack.transform_of(others, last).basis.get_scale().length() < 1e-4
	for k in farm.pen_count - 1:
		var at := farm.pen_rest_position(k)
		var ring := MultiMeshPack.transform_of(others, k)
		quiet = quiet and Vector2(ring.origin.x - at.x, ring.origin.z - at.z).length() < 1.0 and ring.basis.get_scale().x > 0.99
	_check(quiet, "pen click: the site's other pens get the quiet ring")
	# Between the first two pens of a row: the click lands on the site and
	# picks the nearer pen.
	await t._click_control(ui.find_child("Tab_fjord", true, false))
	var a := farm.pen_rest_position(0)
	var b := farm.pen_rest_position(1)
	var near_a := a.lerp(b, 0.38)
	await t._frame_pen(cam, near_a)
	await t._click_at(cam.unproject_position(near_a))
	_check(pens.selected_pen_id == sid and pens.selected_pen_index == 0,
		"pen click: a click on the water between two pens picks the nearer one (#%d)" % (pens.selected_pen_index + 1))
	scene.queue_free()
	await _frames(2)


## 5 and 6. Pen LODs through the site's MultiMeshes; the floating gear.
func _floating_gear_rides_the_water() -> void:
	var scene: Node = await _open_scene("po3_red_sone")
	var env: Node = scene.get_node("FjordEnvironment")
	var pens: FishPenManager = env.get_node("FishPenManager")
	var terrain: FjordTerrain = env.get_node("Terrain")
	var director: EnvironmentDirector = env.get_node("EnvironmentDirector")
	var cam: FjordCamera = env.get_node("FjordCamera")
	cam.auto_orbit_enabled = false
	for i in 600:
		if terrain.is_ready():
			break
		await t.get_tree().process_frame
	var farm := pens.get_site("po3_nord")
	var small := pens.get_site("po3_sor")
	for site in [farm, small]:
		site.set_process(false)  # the probe positions drive the LODs here

	# Close beside pen 1, out past the end of the grid: pen 1 is LOD0, the
	# far ones LOD1, each by its own distance.
	var p0 := farm.pen_rest_position(0)
	var away := (p0 - farm.pen_rest_position(farm.pen_count - 1)).normalized()
	var probe := p0 + away * 150.0 + Vector3(0.0, 90.0, 0.0)
	farm.update_lods(probe)
	var mixed := farm.pens_in_lod(FarmSite.Lod.NEAR) > 0 and farm.pens_in_lod(FarmSite.Lod.MID) > 0
	_check(mixed and _one_lod_each(farm, probe),
		"pen LODs: every pen is drawn by exactly one LOD, chosen by its own distance (%s)" % str(farm.lod_state))
	_check(farm.lod_state[0] == FarmSite.Lod.NEAR and farm.cages[0].is_lod0_visible(), "pen LODs: the pen nearest the camera shows its own LOD0")
	# Hysteresis at the LOD0/LOD1 switch.
	var edge: float = PenCage.LOD_SWITCH[0] + PenCage.LOD_HYSTERESIS * 0.5
	_check(FarmSite.next_lod(FarmSite.Lod.NEAR, edge) == FarmSite.Lod.NEAR and FarmSite.next_lod(FarmSite.Lod.MID, edge) == FarmSite.Lod.MID
		and FarmSite.next_lod(FarmSite.Lod.MID, PenCage.LOD_SWITCH[1] + PenCage.LOD_HYSTERESIS * 0.5) == FarmSite.Lod.MID
		and FarmSite.next_lod(FarmSite.Lod.FAR, PenCage.LOD_SWITCH[1] - PenCage.LOD_HYSTERESIS * 0.5) == FarmSite.Lod.FAR
		and FarmSite.next_lod(FarmSite.Lod.NEAR, 2000.0) == FarmSite.Lod.FAR,
		"pen LODs: the hand-overs hold within +-%.0f m of %s" % [PenCage.LOD_HYSTERESIS, str(PenCage.LOD_SWITCH)])
	# Mid range: all LOD1, through the MultiMeshes, at the pens' own poses.
	var mid := farm.global_position + Vector3(0.0, 420.0, 300.0)
	farm.update_lods(mid)
	_check(farm.pens_in_lod(FarmSite.Lod.MID) == farm.pen_count and _one_lod_each(farm, mid),
		"pen LODs: from %.0f m every pen is LOD1, drawn by the site's MultiMeshes" % mid.distance_to(farm.global_position))
	var parts: Dictionary = farm.get_lod_multimeshes(FarmSite.Lod.MID)
	var poses := not parts.is_empty()
	var shadowless := not parts.is_empty()
	for part in parts:
		var mmi := parts[part] as MultiMeshInstance3D
		shadowless = shadowless and mmi.cast_shadow == GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		for k in farm.pen_count:
			poses = poses and MultiMeshPack.transform_of(mmi.multimesh, k).is_equal_approx(farm.cages[k].transform)
	for level in [FarmSite.Lod.FAR]:
		for part in farm.get_lod_multimeshes(level):
			shadowless = shadowless and (farm.get_lod_multimeshes(level)[part] as MultiMeshInstance3D).cast_shadow == GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_check(poses, "pen LODs: each LOD1 instance is its riding pen's transform (%d parts)" % parts.size())
	_check(shadowless, "pen LODs: no LOD1 or LOD2 MultiMesh casts a shadow")
	# Far: the draw sources do not grow with the pens.
	var far := farm.global_position + Vector3(0.0, 1500.0, 1500.0)
	farm.update_lods(far)
	small.update_lods(far)
	var big_sources := _pen_draw_sources(farm)
	var small_sources := _pen_draw_sources(small)
	var parts_far := farm.get_lod_multimeshes(FarmSite.Lod.FAR).size()
	_check(farm.pens_in_lod(FarmSite.Lod.FAR) == farm.pen_count and small.pens_in_lod(FarmSite.Lod.FAR) == small.pen_count
		and big_sources == _shown_parts(farm, FarmSite.Lod.FAR) and small_sources == _shown_parts(small, FarmSite.Lod.FAR)
		and big_sources <= parts_far and small_sources <= parts_far,
		"pen LODs: far away a %d-pen and a %d-pen site each draw their pens from one MultiMesh per shown part (%d and %d of %d, not one per pen and part)"
			% [farm.pen_count, small.pen_count, big_sources, small_sources, parts_far])

	# The floating gear: the director's very waves and bake, from the farm
	# camera (the fine grid near it draws the short waves too).
	cam.look_at_position(farm.global_position, FjordCamera.ZoomLevel.FARM)
	await t._settle_camera(cam)
	var weather: WeatherController = env.get_node("WeatherSystem")
	weather.transition_duration = 0.0
	env.apply_sky_context({"weather_state": "Severe", "wind_ms": 25.0, "cloud_cover": 1.0, "visibility_km": 2.0})
	director.compose(1.0 / 60.0)
	var field := director.wave_field
	SiteFloat.apply(field)
	var uniforms_ok := field != null and field.wave_k.size() == SiteFloat.WAVES
	for mat in SiteFloat.MATERIALS:
		var m := mat as ShaderMaterial
		uniforms_ok = uniforms_ok and is_equal_approx(float(m.get_shader_parameter("float_on")), 1.0) \
			and m.get_shader_parameter("float_wave_k") == field.wave_k \
			and m.get_shader_parameter("float_wave_amp") == field.wave_amp \
			and m.get_shader_parameter("float_grid_origin") == field.grid_origin() \
			and is_equal_approx(float(m.get_shader_parameter("float_grid_base_spacing")), field.grid_base_spacing)
	_check(uniforms_ok, "floating gear: every float shader gets the director's waves and the water grid (%d materials)" % SiteFloat.MATERIALS.size())
	var terrain_bake := terrain.water_bake_texture
	var bake_ok := terrain_bake != null and SiteFloat.bake_texture == terrain_bake and field.has_bake > 0.0
	for mat in SiteFloat.MATERIALS:
		var m := mat as ShaderMaterial
		bake_ok = bake_ok and m.get_shader_parameter("float_bake_tex") == terrain_bake \
			and is_equal_approx(float(m.get_shader_parameter("float_has_bake")), 1.0) \
			and m.get_shader_parameter("float_bake_origin") == field.bake_origin \
			and is_equal_approx(float(m.get_shader_parameter("float_bake_spacing")), field.bake_spacing) \
			and is_equal_approx(float(m.get_shader_parameter("float_bake_texels")), field.bake_texels)
	_check(bake_ok, "floating gear: every float shader samples the water's own bake (depth and exposure per point)")
	var floats_on_gear := true
	for node in farm.find_children("*", "GeometryInstance3D", true, false):
		var g := node as GeometryInstance3D
		var mesh: Mesh = null
		if g is MultiMeshInstance3D and (g as MultiMeshInstance3D).multimesh != null:
			mesh = (g as MultiMeshInstance3D).multimesh.mesh
		elif g is MeshInstance3D:
			mesh = (g as MeshInstance3D).mesh
		var gear := g.get_parent() == farm.grid or g.get_parent() == farm.hoses
		if not gear or mesh == null:
			continue
		var floats := g.material_override != null and SiteFloat.MATERIALS.has(g.material_override)
		for i in mesh.get_surface_count():
			floats = floats or SiteFloat.MATERIALS.has(mesh.surface_get_material(i))
		floats_on_gear = floats_on_gear and floats
	_check(floats_on_gear, "floating gear: every buoy, light, line, hose and float of the site draws with a floating material")
	var ropes := farm.grid.get_rope_instance().multimesh
	var weights_ok := ropes.use_custom_data and ropes.instance_count == farm.grid.ropes.size()
	for k in farm.grid.ropes.size():
		var want: Vector2 = MooringGrid.ROPE_HEAVE[farm.grid.ropes[k][3]]
		var got := MultiMeshPack.custom_of(ropes, k)
		weights_ok = weights_ok and is_equal_approx(got.r, want.x) and is_equal_approx(got.g, want.y)
	_check(weights_ok and MooringGrid.ROPE_HEAVE["chain"] == Vector2(1, 0) and MooringGrid.ROPE_HEAVE["bridle"] == Vector2(0, 1),
		"floating gear: chains hang from their buoys and bridles rise to the pens; frame and anchor lines lie still")
	var worst := 0.0
	var tallest := 0.0
	for i in 64:
		var a := float(i) * 2.399963
		var r := 260.0 * sqrt((float(i) + 0.5) / 64.0)
		var x := farm.global_position.x + cos(a) * r
		var z := farm.global_position.z + sin(a) * r
		var drawn := field.height_at(x, z)
		tallest = maxf(tallest, absf(drawn))
		worst = maxf(worst, absf(SiteFloat.reference_height(field, x, z) - drawn))
	_check(tallest > 0.2 and worst <= 0.01,
		"floating gear: in a 25 m/s storm the shader's water meets the drawn water within %.3f m over the site (waves to %.2f m)" % [worst, tallest])
	scene.queue_free()
	await _frames(2)


## True when each pen is in exactly one LOD: LOD0 shown iff NEAR, its
## LOD1 / LOD2 MultiMesh instances live iff MID / FAR, the state the one
## its distance asks for.
func _one_lod_each(farm: FarmSite, probe: Vector3) -> bool:
	var ok := true
	for k in farm.pen_count:
		var state := farm.lod_state[k]
		var d := probe.distance_to(farm.cages[k].global_position)
		var near: float = PenCage.LOD_SWITCH[0]
		var far: float = PenCage.LOD_SWITCH[1]
		var h := PenCage.LOD_HYSTERESIS
		var expect_ok := (state == FarmSite.Lod.NEAR and d < near + h) or (state == FarmSite.Lod.MID and d > near - h and d < far + h) \
			or (state == FarmSite.Lod.FAR and d > far - h)
		ok = ok and expect_ok and farm.cages[k].is_lod0_visible() == (state == FarmSite.Lod.NEAR)
		for level in [FarmSite.Lod.MID, FarmSite.Lod.FAR]:
			var parts: Dictionary = farm.get_lod_multimeshes(level)
			for part in parts:
				var mmi := parts[part] as MultiMeshInstance3D
				if not farm._part_shown(part):
					continue
				var live := mmi.visible and MultiMeshPack.transform_of(mmi.multimesh, k).basis.get_scale().length() > 1e-4
				ok = ok and live == (state == level)
	return ok


## Parts a site shows at a LOD (its variants: bird net, skirt, band).
func _shown_parts(farm: FarmSite, level: int) -> int:
	var n := 0
	for part in farm.get_lod_multimeshes(level):
		n += 1 if farm._part_shown(part) else 0
	return n


## Visible pen-part geometries of a site (its LOD0 meshes still rendered,
## plus its visible LOD MultiMeshes).
func _pen_draw_sources(farm: FarmSite) -> int:
	var n := 0
	for cage in farm.cages:
		if cage.is_lod0_visible():
			for mesh in cage.get_lod_meshes(0):
				n += 1 if mesh.visible else 0
	for level in [FarmSite.Lod.MID, FarmSite.Lod.FAR]:
		var parts: Dictionary = farm.get_lod_multimeshes(level)
		for part in parts:
			n += 1 if (parts[part] as MultiMeshInstance3D).visible else 0
	return n


## 7. Per-pen payloads (S2-01b) drive each ring; the site's otherwise.
func _pen_payload_drives_each_ring() -> void:
	var site := {"id": "probe", "fish_count": 40000, "lice_per_fish": 0.1, "mean_weight_kg": 2.0}
	var shared := FishPenManager.pen_payload(site, 2, 4)
	_check(is_equal_approx(float(shared["fish_count"]), 10000.0) and is_equal_approx(float(shared["lice_per_fish"]), 0.1),
		"pen payload: without per-pen data each pen gets the site's lice and its share of the fish")
	var lice := [0.05, 0.3, 0.6, 0.05]
	var per_pen := site.duplicate()
	per_pen["pens"] = lice.map(func(l: float) -> Dictionary: return {"fish_count": 9000, "lice_per_fish": l})
	var states := []
	for k in 4:
		states.append(FishPenManager.health_state_for(FishPenManager.pen_payload(per_pen, k, 4)))
	_check(states == [PenCage.Health.OK, PenCage.Health.WARN, PenCage.Health.CRIT, PenCage.Health.OK],
		"pen payload: per-pen data gives each pen its own ring (%s)" % str(states))


## A placeholder (ghost) site fades its pens, hides nets, gear and hoses and
## comes back whole; the variants (bird net, skirt, band) reach the pens and
## the site's LOD MultiMeshes alike.
func _ghost_site_and_variants() -> void:
	var holder := Node3D.new()
	t.add_child(holder)
	var farm := FarmSite.new()
	holder.add_child(farm)
	farm.build("ghost_probe", 4, {"bird_net": PenCage.BirdNet.RIM})
	farm.attach_hoses(farm.to_global(Vector3(-130.0, 0.0, 0.0)), 37.0, 11.0)
	farm.set_ghost(true)
	var faded := farm.is_ghost() and not farm.grid.visible and not farm.hoses.visible
	for cage in farm.cages:
		for mesh in cage.get_lod_meshes(0):
			faded = faded and mesh.material_override != null and mesh.cast_shadow == GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		for net in cage.get_net_nodes():
			faded = faded and not net.visible
	for level in [FarmSite.Lod.MID, FarmSite.Lod.FAR]:
		for part in farm.get_lod_multimeshes(level):
			faded = faded and (farm.get_lod_multimeshes(level)[part] as MultiMeshInstance3D).material_override != null
	farm.set_ghost(false)
	var whole := not farm.is_ghost() and farm.grid.visible and farm.hoses.visible
	for cage in farm.cages:
		for mesh in cage.get_lod_meshes(0):
			whole = whole and mesh.material_override == null and mesh.cast_shadow == PenCage.shadow_for(String(mesh.name), 0)
			whole = whole and mesh.visible == cage.part_shown(String(mesh.name))
	_check(faded and whole, "sites: a placeholder site fades and hides its gear, and comes back whole")
	farm.set_lice_skirt(true)
	farm.set_owner_band(Color(0.2, 0.4, 0.8))
	var variants := farm.get_bird_net() == PenCage.BirdNet.RIM
	for cage in farm.cages:
		variants = variants and cage.has_lice_skirt() and cage.get_bird_net() == PenCage.BirdNet.RIM
		variants = variants and cage.part_shown("BirdNetRim") and not cage.part_shown("BirdNet") and cage.part_shown("Band")
	for level in [FarmSite.Lod.MID, FarmSite.Lod.FAR]:
		var parts: Dictionary = farm.get_lod_multimeshes(level)
		var band := parts["Band"] as MultiMeshInstance3D
		variants = variants and farm._part_shown("LiceSkirt") and not farm._part_shown("BirdNetPoles")
		variants = variants and band.get_instance_shader_parameter("band_color") == Color(0.2, 0.4, 0.8)
	_check(variants, "sites: the bird-net style, skirt and band reach every pen and the site's LOD MultiMeshes")
	holder.queue_free()
	await _frames(1)
