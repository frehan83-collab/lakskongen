extends RefCounted
## Geo stages 2-3, the captain's integration close (docs/geo/STAGE23_PLAN.md
## §4 steps 4-5): what the windowed GEO-VIS review found off
## (docs/run/evidence/geo_vis_review.md), pinned in the running game.
## GeoRegionChecks runs these last; they have no _run() line of their own.
## GeoMapReviewChecks holds the national map's (the site marks keep apart,
## no plate anchor on a badge's numeral).
## 1. hav_1's 'Bygg her (hav)' (its hexagon's card at the region level) opens
##    the Hav family with the first offshore unit the company may start
##    chosen, hav_1 under it and its lens in the steps' view. Before, the
##    dialog opened with no unit: the lens stayed hidden, and choosing a unit
##    dropped hav_1.
## 2. A home area with no Havbruk til havs area of its own (PO4) borrows the
##    nearest (BALANCE §1ab): hav_1's lens says 'Nærmeste havområde' and the
##    area's own PO; a home area's own (PO3) says 'Havområde'.
## 3. The build dialog's entrance keeps its confirm inside the window on every
##    frame. On Sjø the places make the panel grow while it slides in; the
##    slide held the panel where the smaller panel was centred, so for its
##    0.28 s the confirm sat under the window's edge at 1920x1080, and U2-01's
##    auction test clicked past it (2 of 3 UI runs at the close).

## The offshore unit opens with the research, a first harvest and a year
## without an escape (BuildFlowChecks' setup; forste_sesong seed 7 has none).
const HAV_RESEARCH := "deep-sea-farming"
const ESCAPE_FREE_DAYS := 365
const HAV_SCENARIO := "forste_sesong"
const HAV_SEED := 7
## A game whose home area (PO4) has no Havbruk til havs area of its own.
const BORROWING_SCENARIO := "barekraft_mester"
## The home zone (PO3) whose Sjø step lists the area's places.
const SEA_ZONE := "fjord_west"
## A hang guard on the frames the entrance may take.
const ENTRANCE_FRAME_CAP := 600

var t: Node = null
var sim: Node = null


func run(runner: Node) -> void:
	t = runner
	sim = t._sim
	var started := Time.get_ticks_msec()
	await _hav_card_opens_hav_1_in_its_lens()
	await _a_borrowed_area_says_nearest()
	await _the_entrance_keeps_the_confirm_in_the_window()
	print("  geo GEO-VIS checks took %d ms" % (Time.get_ticks_msec() - started))


func _check(ok: bool, label: String) -> void:
	t._check(ok, label)


func _frames(n: int) -> void:
	for i in n:
		await t.get_tree().process_frame


## The Havbruk til havs candidate hav_1 is bound to ({} when none).
func _bound_row() -> Dictionary:
	for r: Dictionary in GeoBridgeAdapter.new(sim).offshore_candidates():
		if String(r.get("bound_plot_id", "")) == GeoOverlayLayer.HAV_PLOT:
			return r
	return {}


func _map_of(scene: Node) -> Control:
	var ui: Control = scene.get_node("UILayer/GameUI")
	ui.show_tab("map")
	await _frames(3)
	return ui.find_child("Panel_map", true, false)


func _hav_card_opens_hav_1_in_its_lens() -> void:
	_check(sim.start_qa_scenario(HAV_SCENARIO, HAV_SEED), "geo vis: %s starts (seed %d)" % [HAV_SCENARIO, HAV_SEED])
	sim.skip_tutorial()
	sim.set_paused(true)
	var researched: bool = sim.qa_open_farm_family(HAV_RESEARCH, "", true)
	sim.advance_days(ESCAPE_FREE_DAYS)
	sim.set_paused(true)
	var open := bool((sim.get_farm_type_starts().get("of1", {}) as Dictionary).get("startable", false))
	_check(researched and open, "geo vis: setup: a year into %s with the research, the offshore unit may start" % HAV_SCENARIO)
	var scene: Node = await t._spawn_scene_over_current_game()
	var map := await _map_of(scene)
	var bound := _bound_row()
	map.drill_into(int(bound.get("po", 0)))
	await _frames(2)
	var drill: GeoDrillStack = map.geo_drill()
	await t._click_control(drill.region_view().hexagon(String(bound.get("id", ""))))
	var card_offers_hav := drill.site_card().action() == "hav"
	await t._click_control(drill.site_card().action_button())
	# The dialog fits its steps, then brings the plot step into view.
	await _frames(5)
	var dialog: BuildSiteDialog = map.build_dialog()
	var lens := dialog.plot_lens()
	var view := (dialog.get_node("%StepScroll") as Control).get_global_rect()
	var hav_button := lens.plot_button(GeoOverlayLayer.HAV_PLOT)
	var in_view := hav_button != null and view.encloses((dialog.get_node("%PlotHeader") as Control).get_global_rect().grow(-1.0)) \
		and view.encloses(hav_button.get_global_rect().grow(-1.0))
	_check(card_offers_hav and dialog.visible and dialog.family() == "hav" and dialog.variant() == "of1"
			and dialog.plot_id() == GeoOverlayLayer.HAV_PLOT and lens.is_visible_in_tree()
			and hav_button != null and hav_button.button_pressed and in_view,
		"geo vis: hav_1's 'Bygg her (hav)' opens the Hav family on the first open unit (%s) with hav_1 (%s) chosen and in view (%s)" % [
			dialog.variant(), dialog.plot_id(), str(in_view)])
	dialog.close()
	scene.queue_free()
	await t.get_tree().process_frame


func _lens_area_row(lens: PlotLens) -> Array:
	var facts := lens.get_node("Lens/Facts")
	for i in range(0, facts.get_child_count() - 1, 2):
		var key := (facts.get_child(i) as Label).text
		if key == t.tr("GEO_PLOT_AREA") or key == t.tr("GEO_PLOT_AREA_NEAREST"):
			return [key, (facts.get_child(i + 1) as Label).text]
	return []


func _a_borrowed_area_says_nearest() -> void:
	for game: Array in [[BORROWING_SCENARIO, true], [HAV_SCENARIO, false]]:
		_check(sim.start_qa_scenario(str(game[0]), HAV_SEED), "geo vis: %s starts (seed %d)" % [str(game[0]), HAV_SEED])
		sim.set_paused(true)
		var scene: Node = await t._spawn_scene_over_current_game()
		var map := await _map_of(scene)
		var geo: Dictionary = sim.get_geo_plot_binding(GeoOverlayLayer.HAV_PLOT)
		var lens: PlotLens = (map.build_dialog() as BuildSiteDialog).plot_lens()
		lens.show_plots(sim.get_build_plots("hav"), GeoOverlayLayer.HAV_PLOT, "of1", "")
		var row := _lens_area_row(lens)
		var borrowed := int(geo.get("home_po", 0)) != int(geo.get("po", 0))
		var want := t.tr("GEO_PLOT_AREA_NEAREST") if bool(game[1]) else t.tr("GEO_PLOT_AREA")
		_check(bool(geo.get("bound", false)) and borrowed == bool(game[1]) and row.size() == 2 and row[0] == want
				and row[1] == t.tr("GEO_PLOT_AREA_VALUE") % [str(geo.get("area", "")), int(geo.get("po", 0))],
			"geo vis: %s (home PO%d, hav_1 in PO%d): the lens says '%s'" % [str(game[0]), int(geo.get("home_po", 0)),
				int(geo.get("po", 0)), ": ".join(row)])
		scene.queue_free()
		await t.get_tree().process_frame


func _the_entrance_keeps_the_confirm_in_the_window() -> void:
	_check(sim.start_qa_scenario(HAV_SCENARIO, HAV_SEED), "geo vis: %s starts (seed %d)" % [HAV_SCENARIO, HAV_SEED])
	sim.skip_tutorial()
	sim.set_paused(true)
	var scene: Node = await t._spawn_scene_over_current_game()
	var map := await _map_of(scene)
	var dialog: BuildSiteDialog = map.build_dialog()
	var panel := dialog.get_node("%Dialog") as Control
	var confirm := dialog.get_node("%ConfirmBtn") as Control
	var window := t.get_viewport().get_visible_rect()
	dialog.open_for_zone(SEA_ZONE)
	var opened_at := panel.get_combined_minimum_size().y
	# Every frame of the entrance (its fade ends with it), then two more.
	var below := -INF
	var frames := 0
	var settled := 0
	while settled < 2 and frames < ENTRANCE_FRAME_CAP:
		await t.get_tree().process_frame
		frames += 1
		below = maxf(below, confirm.get_global_rect().end.y - window.end.y)
		if panel.modulate.a >= 1.0:
			settled += 1
	_check(dialog.visible and dialog.family() == "sjo" and dialog.selected_candidate() != "" and settled == 2 and below <= 0.0,
		"geo vis: the Sjø build dialog's confirm stays inside the window as it enters (the panel %d -> %d px tall; %d frames; at worst it ends %+.0f px past the window's bottom)" % [
			int(opened_at), int(panel.get_combined_minimum_size().y), frames, below])
	dialog.close()
	scene.queue_free()
	await t.get_tree().process_frame
