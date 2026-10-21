extends RefCounted

## F2-20 (wave4b review): every land campus the engine can build stands clear,
## driven by the UI flow runner (UiFlowTests._test_facilities_render).
##
## For every land pad the engine lists (get_land_plots: its grow-out lots and
## post-smolt modules) and for a plot the terrain has no pad for (a migrated
## save's overflow plot, laid out on the presenter's sized frame), every lot
## count the engine allows is laid out in each template it can hold (all full
## HFS lots, all RAS halls, a pilot lot first; post-smolt modules with and
## without the river intake) with LandCampusBuilder.plan (no draws), and
## CampusLayoutCheck finds no clash: no two solids in one another, no road
## under a building, nothing off the pad. On every grow-out layout the pipe
## bridge runs between the intake basin and the harvest hall. The rows'
## tanks follow the land calibration's templates.

const CALIBRATION := "res://data/calibration/facilities_land.json"
## Template id -> the calibration's section (grow-out and pre-grow tanks).
const SECTIONS := {"hfs_lot": "hfs", "hfs_pilot_lot": "hfsPilot", "ras_hall": "ras", "post_smolt_module": "postSmolt"}
## A plot id the terrain carves no pad for (the overflow row's id prefix).
const OVERFLOW_PLOT := "overflow_land_9"

var t: Node = null
var _tanks := {}


func run(runner: Node, manager: FarmFacilityVisualManager) -> void:
	t = runner
	_read_templates()
	var plots: Array = t._sim.get_land_plots()
	var limits := {}
	for plot: Dictionary in plots:
		limits[str(plot["id"])] = Vector2i(int(plot.get("grow_out_lots", 0)), int(plot.get("post_smolt_modules", 0)))
	limits[OVERFLOW_PLOT] = Vector2i(4, 4)
	var bridge_problems: Array[String] = []
	var laid := 0
	for plot_id: String in limits:
		var lim: Vector2i = limits[plot_id]
		if lim.x > 0:
			var problems: Array[String] = []
			for n in range(1, lim.x + 1):
				for templates: Array in _grow_out_sets(n):
					var campus := _plan(manager, _row("hfs", plot_id, templates, false))
					laid += 1
					for c in CampusLayoutCheck.clashes(campus):
						problems.append("%s %s: %s" % [plot_id, ",".join(templates), c])
					var between := _bridge_between(campus)
					if between != "":
						bridge_problems.append("%s %d lots: %s" % [plot_id, n, between])
					campus.free()
			t._check(problems.is_empty(), "layout: every grow-out campus on %s (1-%d lots, HFS, RAS, pilot first) stands clear (%s)"
				% [plot_id, lim.x, "; ".join(problems.slice(0, 4))])
		if lim.y > 0:
			var problems: Array[String] = []
			for n in range(1, lim.y + 1):
				for river: bool in [false, true]:
					var modules: Array = []
					for k in n:
						modules.append("post_smolt_module")
					var campus := _plan(manager, _row("post_smolt", plot_id, modules, river))
					laid += 1
					for c in CampusLayoutCheck.clashes(campus):
						problems.append("%s %d modules%s: %s" % [plot_id, n, " + river intake" if river else "", c])
					campus.free()
			t._check(problems.is_empty(), "layout: every post-smolt site on %s (1-%d modules, with and without the river intake) stands clear (%s)"
				% [plot_id, lim.y, "; ".join(problems.slice(0, 4))])
	t._check(bridge_problems.is_empty() and laid > 0,
		"layout: on every grow-out campus the pipe bridge runs between the intake basin and the harvest hall (%d layouts; %s)"
		% [laid, "; ".join(bridge_problems.slice(0, 4))])
	# The check is not vacuous: the review's clash (the hall moved onto the bridge's line) is found.
	var probe := _plan(manager, _row("hfs", "pad_b", ["hfs_lot", "hfs_lot"], false))
	var hall: Array = probe.groups["harvest"]["pieces"][0]
	var bridge: Array = probe.groups["intake"]["pieces"].filter(func(p: Array) -> bool: return p[0] == "pipe_bridge_span")[2]
	var moved: Transform3D = hall[1]
	moved.origin = Vector3(moved.origin.x, moved.origin.y, (bridge[1] as Transform3D).origin.z) \
		if absf(probe.pad_frame().basis.z.z) > 0.5 else Vector3((bridge[1] as Transform3D).origin.x, moved.origin.y, moved.origin.z)
	hall[1] = moved
	var found := CampusLayoutCheck.clashes(probe)
	t._check(found.size() > 0 and Array(found).any(func(c: String) -> bool: return "harvest_hall" in c and "pipe_bridge_span" in c),
		"layout: a harvest hall moved onto the bridge's line is a clash (%s)" % "; ".join(Array(found).slice(0, 2)))
	probe.free()


## The template sets of an n-lot grow-out site: all full HFS lots, all RAS
## halls, and a pilot lot first (the rest full lots).
static func _grow_out_sets(n: int) -> Array:
	var hfs: Array = []
	var ras: Array = []
	var pilot: Array = ["hfs_pilot_lot"]
	for k in n:
		hfs.append("hfs_lot")
		ras.append("ras_hall")
		if k > 0:
			pilot.append("hfs_lot")
	return [hfs, ras, pilot]


func _read_templates() -> void:
	var data = JSON.parse_string(FileAccess.get_file_as_string(CALIBRATION))
	for tpl: String in SECTIONS:
		var sec: Dictionary = data.get(SECTIONS[tpl], {}) if data is Dictionary else {}
		_tanks[tpl] = Vector2i(int(sec.get("growOutTanks", 0)), int(sec.get("preGrowTanks", 0)))


## A get_facility_visuals row for a built unit of `templates` on `plot_id`.
func _row(variant: String, plot_id: String, templates: Array, river: bool) -> Dictionary:
	var unit := "layout_check"
	var built := {"status": "none", "phase_key": "operational", "phase_index": 6, "phase_count": 6, "phase_progress": 1.0,
		"progress": 1.0, "suspended": false, "waiting_reason": ""}
	var lots: Array = []
	var number := 1
	for k in templates.size():
		var tpl: String = templates[k]
		var counts: Vector2i = _tanks[tpl]
		var tanks: Array = []
		for i in counts.x + counts.y:
			var role := ("post_smolt" if tpl == "post_smolt_module" else "grow_out") if i < counts.x else "pre_grow_out"
			tanks.append({"id": "%s/t%02d" % [unit, number], "number": number, "role": role, "ready": true,
				"stocked": false, "fish_count": 0})
			number += 1
		lots.append({"lot_id": "%s/lot%d" % [unit, k + 1], "template": tpl, "tanks": tanks, "build": built, "expansion": {}})
	return {"unit_id": unit, "family": "land", "variant": variant, "plot_id": plot_id, "plot_x": 0.0, "plot_z": 0.0,
		"lots": lots, "build": built, "has_river_intake": river, "feeding": false}


## The campus laid out on the pad the presenter would give the row.
func _plan(manager: FarmFacilityVisualManager, row: Dictionary) -> LandCampusBuilder:
	var campus := LandCampusBuilder.new()
	campus.plan(row, manager._pad_for(row))
	return campus


## "" when the bridge's footprint lies between the intake basin's and the
## harvest hall's along the quay (v), else what is wrong.
static func _bridge_between(campus: LandCampusBuilder) -> String:
	var spans: Array[Rect2] = []
	var basin := Rect2()
	var hall := Rect2()
	for g: String in campus.groups:
		for p in campus.groups[g]["pieces"]:
			var r := CampusLayoutCheck.footprint(campus, str(p[0]), p[1])
			match str(p[0]):
				"pipe_bridge_span": spans.append(r)
				"intake_basin": basin = r
				"harvest_hall": hall = r
	if spans.is_empty() or not basin.has_area() or not hall.has_area():
		return "no bridge, basin or hall"
	for s in spans:
		if s.position.y < basin.end.y or s.end.y > hall.position.y:
			return "span v %.1f..%.1f, basin to %.1f, hall from %.1f" % [s.position.y, s.end.y, basin.end.y, hall.position.y]
	return ""
