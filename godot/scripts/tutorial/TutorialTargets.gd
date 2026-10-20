class_name TutorialTargets
extends RefCounted

## Where a first-season step points (roadmap U2-02). A step names a target
## (tutorial_steps.json "highlightTarget"); this resolves it to the control on
## screen now: the control itself when its screen is open, else the way there
## (the tab's rail button, the site's row in the pen list, the area's marker).
## Show, then do: the highlight always points at the next click.
##
## resolve() returns {"control": Control or null, "route": bool}; "route"
## is true when the control is the way to the target rather than the target.

## The game scene (GameScene root: UILayer/GameUI, UILayer/HUD).
var scene: Node = null
var _sim: Node = null


func _init(game_scene: Node) -> void:
	scene = game_scene
	_sim = game_scene.get_node_or_null("/root/SimBridge") if game_scene != null else null


func resolve(target: String) -> Dictionary:
	var ui := _ui()
	if ui == null or target.is_empty():
		return _none()
	if target.begins_with("tab:"):
		return _at(ui.tab_route(target.trim_prefix("tab:")), false)
	match target:
		"hud:inbox":
			return _at(_hud().inbox_button() if _hud() != null else null, false)
		"hud:clock":
			return _at(_hud().clock_controls() if _hud() != null else null, false)
		"pens:feed_plan":
			# The card while the player chooses; "Bruk på …" once there is a plan to apply.
			var pens := _open_panel(ui, "pens")
			if pens == null:
				return _route(ui, "pens")
			var card: FeedPlanCard = pens.feed_plan_card()
			if card != null and not card.apply_button().disabled:
				return _at(card.apply_button(), false)
			return _at(card, false)
		"pens:lice_order":
			var pens := _open_panel(ui, "pens")
			if pens == null:
				return _route(ui, "pens")
			var card: DriftsplanCard = pens.driftsplan_card()
			return _at(card.policy_check("delouse_on_count") if card != null else null, false)
		"pens:harvest":
			var planner := HarvestScreens.planner(scene.get_tree()) as HarvestPlanner
			if planner != null and planner.is_open():
				return _at(planner.order_button, false)
			return _site_control(ui, _first_site(), func(p: Control) -> Control: return p.harvest_button())
		"pens:harvest_report":
			var report := HarvestScreens.report(scene.get_tree())
			if report != null and report.is_open():
				return _none()   # open: closing it is the step
			return _site_control(ui, _first_site(), func(p: Control) -> Control: return p.harvest_report_button())
		"pens:stock":
			return _site_control(ui, _first_site(), func(p: Control) -> Control: return p.stock_button())
		"pens:stock_new_site":
			return _site_control(ui, _new_site(), func(p: Control) -> Control: return p.stock_button())
		"pens:new_site_lice_order":
			return _site_control(ui, _new_site(), func(p: Control) -> Control:
				var card: DriftsplanCard = p.driftsplan_card()
				return card.policy_check("delouse_on_count") if card != null else null)
		"research:start":
			var tree := _open_panel(ui, "research")
			if tree == null:
				return _route(ui, "research")
			return _none() if tree.is_confirming() else _at(tree.cheapest_available_node(), false)
		"finance:loan":
			var finance := _open_panel(ui, "finance")
			return _route(ui, "finance") if finance == null else _at(finance.loan_button(), false)
		"map:home_area":
			var map := _open_panel(ui, "map")
			return _route(ui, "map") if map == null else _at(map.region_marker(map.home_area()), false)
		"map:build":
			return _map_build(ui)
	return _none()


# ── Steps with a way through several screens ─────────────────────────────────

## On the pens tab with `site_id` open: what `pick` returns; with another
## site open: the site's row; elsewhere: the pens tab.
func _site_control(ui: Control, site_id: String, pick: Callable) -> Dictionary:
	var pens := _open_panel(ui, "pens")
	if pens == null:
		return _route(ui, "pens")
	if site_id.is_empty():
		return _none()
	if pens.get_selected_pen_id() != site_id:
		return _at(pens.site_row(site_id), true)
	return _at(pick.call(pens), false)


## The build: the dialog's confirm when it is open, the home zone's
## "Bygg lokalitet" on the home area, the home area's marker, the map tab.
func _map_build(ui: Control) -> Dictionary:
	var map := _open_panel(ui, "map")
	if map == null:
		return _route(ui, "map")
	var dialog: BuildSiteDialog = map.build_dialog()
	if dialog != null and dialog.visible:
		var result: Control = dialog.get_node_or_null("%ResultBox")
		if result != null and result.visible:
			return _none()
		return _at(dialog.get_node_or_null("%ConfirmBtn") as Control, false)
	var section: RegionExpansionSection = map.expansion_section()
	var home: int = map.home_area()
	if section != null and int(section.area().get("production_area", 0)) == home:
		var card := section.zone_card(_home_zone())
		if card != null:
			return _at(card.get_node_or_null("Body/Build/BuildBtn") as Control, false)
	return _at(map.region_marker(home), true)


# ── Helpers ───────────────────────────────────────────────────────────────────

func _ui() -> Control:
	return scene.get_node_or_null("UILayer/GameUI") as Control if scene != null else null


func _hud() -> Control:
	return scene.get_node_or_null("UILayer/HUD") as Control if scene != null else null


## The tab's panel when it is the open tab, else null.
func _open_panel(ui: Control, tab_id: String) -> Control:
	if ui.get_active_tab() != tab_id:
		return null
	var panel: Control = ui.panel_of(tab_id)
	return panel if panel != null and panel.is_visible_in_tree() else null


func _route(ui: Control, tab_id: String) -> Dictionary:
	return _at(ui.tab_route(tab_id), true)


func _sites() -> Array:
	return _sim.get_all_pens() if _sim != null else []


## The scenario's first site (the one the season starts with).
func _first_site() -> String:
	var sites := _sites()
	return str(sites[0].get("id", "")) if not sites.is_empty() else ""


## The site the player built (the newest), "" before it exists.
func _new_site() -> String:
	var sites := _sites()
	return str(sites[sites.size() - 1].get("id", "")) if sites.size() >= 2 else ""


func _home_zone() -> String:
	var sites := _sites()
	return str(sites[0].get("zone", "")) if not sites.is_empty() else ""


static func _at(control: Control, route: bool) -> Dictionary:
	if control == null or not is_instance_valid(control) or not control.is_visible_in_tree():
		return _none()
	return {"control": control, "route": route}


static func _none() -> Dictionary:
	return {"control": null, "route": false}
