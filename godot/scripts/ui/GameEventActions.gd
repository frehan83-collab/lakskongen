class_name GameEventActions
extends RefCounted

## Performs a game event's counter verb (roadmap U2-06): every verb opens the
## place it lives, with the site the event is about already selected, so the
## player sees the preview and the cost there before committing. Nothing here
## spends money or changes the simulation.
##
## A company-wide event (lice, nets) has no site of its own: the verb picks
## the site that needs it most (most lice, most worn nets) when it runs.

## Verb -> the tab it opens. Site verbs open the pen view of their site.
const TAB_VERBS := {
	"check_water": "telemetry",
	"review_feeding": "pens",
	"raise_capital": "finance",
	"open_sustainability": "sustainability",
}
const SITE_VERBS := ["delouse", "freshwater_bath", "inspect_nets"]


## Runs the event's verb inside a running game scene (its root). Returns true
## when it opened something.
static func perform(event: Dictionary, scene: Node) -> bool:
	if scene == null:
		return false
	var verb := str(event.get("counter_verb", "none"))
	var ui := scene.get_node_or_null("%GameUI")
	var vista := scene.get_node_or_null("%FjordVistaBridge")
	if TAB_VERBS.has(verb):
		if ui == null or not ui.has_method("show_tab"):
			return false
		ui.show_tab(TAB_VERBS[verb])
		return true
	var sim := scene.get_node_or_null("/root/SimBridge")
	var site := target_site(event, sim)
	if SITE_VERBS.has(verb):
		if vista != null and site != "" and vista.has_method("show_pen_detail"):
			return bool(vista.show_pen_detail(site))
		if ui != null and ui.has_method("show_tab"):
			ui.show_tab("pens")
			return true
		return false
	if verb == "inspect_site":
		if ui != null and ui.has_method("show_tab"):
			ui.show_tab("fjord")
		if vista != null and site != "" and vista.has_method("select_pen_from_ui"):
			vista.select_pen_from_ui(site)
			vista.focus_selected_pen()
		return ui != null
	return false


## The site a verb acts on: the event's own, else the site that needs it most.
static func target_site(event: Dictionary, sim: Node) -> String:
	var own := str(event.get("site_id", ""))
	if own != "" or sim == null or not sim.has_method("get_all_pens"):
		return own
	var verb := str(event.get("counter_verb", ""))
	var best := ""
	var best_score := -INF
	for pen in sim.get_all_pens():
		var id := str(pen.get("id", ""))
		if id == "":
			continue
		# Most lice for delousing, most worn nets for an inspection, else the
		# site with the most fish.
		var score := float(pen.get("fish_count", 0))
		if verb == "delouse":
			score = float(pen.get("lice_per_fish", 0.0))
		elif verb == "inspect_nets":
			score = -float(pen.get("net_integrity", 100.0))
		if score > best_score:
			best_score = score
			best = id
	return best
