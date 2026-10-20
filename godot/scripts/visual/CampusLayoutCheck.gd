class_name CampusLayoutCheck
extends RefCounted
## Where a land campus's pieces stand in one another (roadmap F2-20; the
## wave4b review found the pipe bridge running through the harvest hall on
## pad_b, a pad the stage never rendered). The UI-flow checks run it over
## every pad and lot count the engine allows; the capture records it per shot.
##
## Every solid's footprint is its LOD0 model box turned onto the pad's frame
## (u toward the quay, v along it). A clash is two solids overlapping by more
## than TOL_M both ways, a road or apron under a solid, or a solid or road
## off the pad. Joins are not clashes: a hall bay beside the next bay of its
## group, a bridge span beside the next, the bridge landing on the quay and
## its legs standing on a road; the quay, the bridge and the intake basin
## reach out over the quay face (+u).

## Overlap (m) two footprints may share both ways before they clash.
const TOL_M := 0.5
## Kit pieces that join their neighbours of the same group.
const JOINED := ["process_hall_bay", "pipe_bridge_span"]
## Pieces that stand out over the quay face: the quay, the bridge landing on
## it and the intake basin's arc in the water.
const OVER_THE_QUAY := ["quay", "pipe_bridge_span", "intake_basin"]


## The clashes of `campus` (planned or built); empty for a sound layout.
static func clashes(campus: LandCampusBuilder) -> PackedStringArray:
	var out := PackedStringArray()
	var solids := _solids(campus)
	var flats := _flats(campus)
	for i in solids.size():
		for j in range(i + 1, solids.size()):
			if not _is_join(solids[i], solids[j]) and _overlaps(solids[i]["rect"], solids[j]["rect"]):
				out.append("%s in %s" % [solids[i]["label"], solids[j]["label"]])
	for f: Dictionary in flats:
		for s: Dictionary in solids:
			if s["asset"] != "pipe_bridge_span" and _overlaps(f["rect"], s["rect"]):
				out.append("%s under %s" % [f["label"], s["label"]])
	var h := campus.half_extents()
	var pad_rect := Rect2(-h, h * 2.0).grow(TOL_M)
	for s: Dictionary in solids:
		var r: Rect2 = s["rect"]
		if s["asset"] in OVER_THE_QUAY:
			# Out over the quay face is where they belong; along it they stay on the pad.
			if r.position.x < -h.x - TOL_M or r.position.y < -h.y - TOL_M or r.end.y > h.y + TOL_M:
				out.append("%s off the pad" % s["label"])
		elif not pad_rect.encloses(r):
			out.append("%s off the pad" % s["label"])
	for f: Dictionary in flats:
		if not pad_rect.encloses(f["rect"]):
			out.append("%s off the pad" % f["label"])
	return out


static func _solids(campus: LandCampusBuilder) -> Array:
	var out: Array = []
	for g: String in campus.groups:
		for p in campus.groups[g]["pieces"]:
			out.append({"label": "%s/%s" % [g, p[0]], "asset": str(p[0]), "group": g,
				"rect": footprint(campus, str(p[0]), p[1])})
	for i in campus.tanks.size():
		var t: Dictionary = campus.tanks[i]
		out.append({"label": "tank %d (%s)" % [i + 1, t["lot_id"]], "asset": str(t["asset"]), "group": "tanks",
			"rect": footprint(campus, str(t["asset"]), t["xform"])})
	return out


static func _flats(campus: LandCampusBuilder) -> Array:
	var out: Array = []
	for g: String in campus.groups:
		for r in campus.groups[g]["rects"]:
			var lo: Vector2 = r[1]
			var hi: Vector2 = r[2]
			out.append({"label": "%s/flat" % g, "rect": Rect2(lo, hi - lo)})
	return out


## A piece's LOD0 model box turned onto the pad: its (u, v) footprint.
static func footprint(campus: LandCampusBuilder, asset: String, xform: Transform3D) -> Rect2:
	var mesh := LandCampusBuilder.kit_mesh(asset, LandCampusBuilder.LODS[0])
	if mesh == null:
		return Rect2()
	var box := mesh.get_aabb()
	var to_pad := campus.pad_frame().affine_inverse() * xform
	var lo := Vector2(INF, INF)
	var hi := Vector2(-INF, -INF)
	for k in 8:
		var p := to_pad * box.get_endpoint(k)
		lo = Vector2(minf(lo.x, p.x), minf(lo.y, p.z))
		hi = Vector2(maxf(hi.x, p.x), maxf(hi.y, p.z))
	return Rect2(lo, hi - lo)


static func _overlaps(a: Rect2, b: Rect2) -> bool:
	var i := a.intersection(b)
	return i.size.x > TOL_M and i.size.y > TOL_M


static func _is_join(a: Dictionary, b: Dictionary) -> bool:
	var first := str(a["asset"])
	var second := str(b["asset"])
	if first == second and first in JOINED and a["group"] == b["group"]:
		return true
	return (first == "pipe_bridge_span" and second == "quay") or (first == "quay" and second == "pipe_bridge_span")
