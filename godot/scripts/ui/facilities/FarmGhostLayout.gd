class_name FarmGhostLayout
extends RefCounted

## What a build ghosts onto a plot (roadmap F2-31, docs/FARM_TYPES_SPEC.md
## §6.2 step 3): the shapes of the unit the player is about to order, taken
## from the campus layout the 3D presenter builds (LandCampusLayout: the
## F2-18 gray box and the F2-25 land kit read the same placements), so the
## lens and the 3D ghost show what will stand there. Pure presentation: no
## engine call, no state.
##
## shapes_for() returns {"shapes": [{kind, pos (plot metres from the plot's
## centre), size, world (Vector3), height}], "route": the pipe route to the
## intake (plot metres), "extent": the area to frame (m)}.

## A catenary mooring spreads over about this many water depths from the
## unit (illustrative; the lens draws the footprint ring at it).
const MOORING_DEPTHS := 3.0


static func shapes_for(plot_id: String, variant_id: String, option: String, engine_xz: Vector2, water_depth_m: float = 150.0) -> Dictionary:
	if plot_id == "":
		return {"shapes": [], "route": PackedVector2Array(), "extent": Vector2.ZERO}
	if variant_id in ["of1", "havfarm"]:
		return _sea(variant_id, engine_xz, water_depth_m)
	return _land(plot_id, variant_id, option)


static func _land(plot_id: String, variant_id: String, option: String) -> Dictionary:
	if not LandCampusLayout.PLOTS.has(plot_id):
		return {"shapes": [], "route": PackedVector2Array(), "extent": Vector2.ZERO}
	var centre: Vector3 = LandCampusLayout.plot_centre(plot_id)
	var placements: Array = LandCampusLayout.plot_placements(plot_id)
	var picked: Array = []
	match variant_id:
		"post_smolt":
			picked = _first(placements, -1, "tank_pregrow", 2) + _first(placements, -1, "process_hall_bay", 1)
		"ras":
			picked = _first(placements, 0, "process_hall_bay", LandCampusLayout.HALL_BAYS_PER_LOT)
		_:
			if option == "hfs_lot":
				picked = placements.filter(func(p: Dictionary) -> bool: return int(p["lot"]) == 0)
			else:
				picked = _first(placements, 0, "tank_growout", 4) + _first(placements, 0, "tank_pregrow", 1) \
					+ _first(placements, 0, "process_hall_bay", 2)
	var shapes: Array = []
	var sum := Vector2.ZERO
	for p in picked:
		var pos: Vector3 = p["pos"]
		var size: Vector3 = p["size"]
		var local := Vector2(pos.x - centre.x, pos.z - centre.z)
		sum += local
		var kind := "tank" if str(p["asset"]).begins_with("tank_") else "box"
		shapes.append({"kind": kind, "pos": local, "size": Vector2(size.x, size.z),
			"world": pos, "height": size.y})
	var route := PackedVector2Array()
	for p in placements:
		if str(p["asset"]) in ["intake_basin", "river_intake"] and not shapes.is_empty():
			var pos: Vector3 = p["pos"]
			route.append(sum / shapes.size())
			route.append(Vector2(pos.x - centre.x, pos.z - centre.z))
			break
	var plot: Dictionary = LandCampusLayout.PLOTS[plot_id]
	return {"shapes": shapes, "route": route, "extent": plot.get("size", Vector2(400, 400))}


static func _sea(variant_id: String, xz: Vector2, depth: float) -> Dictionary:
	var shapes: Array = []
	var world := Vector3(xz.x, 0.0, xz.y)
	var reach := 0.0
	if variant_id == "havfarm":
		var hull: Vector3 = LandCampusLayout.HAVFARM_SIZE
		shapes.append({"kind": "hull", "pos": Vector2.ZERO, "size": Vector2(hull.x, hull.z), "world": world, "height": hull.y})
		reach = hull.x * 0.5
	else:
		var d := 2.0 * LandCampusLayout.OF1_RADIUS
		shapes.append({"kind": "ring", "pos": Vector2.ZERO, "size": Vector2(d, d), "world": world, "height": LandCampusLayout.OF1_FREEBOARD})
		reach = LandCampusLayout.OF1_RADIUS
	var mooring := 2.0 * (reach + MOORING_DEPTHS * depth)
	shapes.append({"kind": "mooring", "pos": Vector2.ZERO, "size": Vector2(mooring, mooring), "world": world, "height": 0.0})
	return {"shapes": shapes, "route": PackedVector2Array(), "extent": Vector2(mooring, mooring) * 1.15}


static func _first(placements: Array, lot: int, asset: String, n: int) -> Array:
	var out: Array = []
	for p in placements:
		if int(p["lot"]) == lot and str(p["asset"]) == asset:
			out.append(p)
			if out.size() >= n:
				break
	return out
