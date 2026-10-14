class_name SiteLayout
extends RefCounted
## The plan of a salmon site, as pure functions (roadmap G2-04): how many
## pens go in which cells of the frame mooring, how much of the fjord channel
## the site takes, and where the white feed hoses float from the feed barge's
## outlets to every pen. Everything is in the site's own frame: x across the
## channel, z along it, the grid centred on the origin, metres. FarmSite
## builds the 3D site from it; the UI-flow tests check it directly.
##
## A Norwegian site is a grid of round pens in a submerged rope frame,
## usually two rows along the fjord with the feed barge moored beside it and
## one hose from the barge to each pen's feed spreader (owner reference 1).
## Sites of up to three pens lie in one row.

## Pens in one row before the site takes a second row.
const MAX_SINGLE_ROW := 3
## The pen's hose port: where its own feed hose (build_pen.py, over the
## collar from the spreader) meets the site hose on the water, metres from
## the pen centre along the pen's +X.
const PORT_R := 30.0
## Hose path: resample step (m) of the smoothed centreline.
const HOSE_STEP_M := 2.0
## Hoses lie side by side this far apart in a bundle (m).
const BUNDLE_SPACING_M := 0.9
## Off the barge's side the hoses drop to the water within this run (m).
const OUTLET_DROP_M := 7.0
## Gentle slack: the floating hoses meander this much (m) over this length.
const MEANDER_AMPLITUDE_M := 0.7
const MEANDER_WAVELENGTH_M := 85.0
## Barge outlets spread over this fraction of the barge's length.
const OUTLET_SPREAD := 0.64
## Gap the channel keeps between neighbouring sites' grids (m) when it
## counts how many channel slots a site takes.
const SITE_GAP_M := 40.0
## Hose floats: one every this many metres along a hose, merged into one
## float where hoses run together.
const FLOAT_SPACING_M := 70.0
const FLOAT_MERGE_M := 7.0


## Frame cells across (x) and along (z) the channel for a site of n pens.
static func grid_dims(pen_count: int) -> Vector2i:
	var n := maxi(pen_count, 1)
	if n <= MAX_SINGLE_ROW:
		return Vector2i(1, n)
	return Vector2i(2, ceili(n / 2.0))


## The cells that hold a pen, pen 1 first: row by row along the channel,
## across the row first. An odd count leaves the last cell of the last row
## empty (real sites keep spare frame cells too).
static func pen_cells(pen_count: int) -> Array[Vector2i]:
	var dims := grid_dims(pen_count)
	var cells: Array[Vector2i] = []
	for k in maxi(pen_count, 1):
		cells.append(Vector2i(k % dims.x, k / dims.x))
	return cells


## Half-extent of the grid (x across, z along), metres.
static func half_extent(pen_count: int) -> Vector2:
	var dims := grid_dims(pen_count)
	return Vector2(dims.x, dims.y) * MooringGrid.CELL_M * 0.5


## Centre of a cell in the site frame (y = 0, the waterline).
static func cell_center(dims: Vector2i, cell: Vector2i) -> Vector3:
	return Vector3((float(cell.x) + 0.5 - dims.x * 0.5) * MooringGrid.CELL_M, 0.0,
		(float(cell.y) + 0.5 - dims.y * 0.5) * MooringGrid.CELL_M)


## Pen centres in the site frame, pen 1 first.
static func pen_positions(pen_count: int) -> Array[Vector3]:
	var dims := grid_dims(pen_count)
	var out: Array[Vector3] = []
	for cell in pen_cells(pen_count):
		out.append(cell_center(dims, cell))
	return out


## Channel slots (spaced `slot_spacing` metres) a site of n pens takes: its
## grid plus the gap to the next site.
static func slot_count(pen_count: int, slot_spacing: float) -> int:
	var length := half_extent(pen_count).y * 2.0 + SITE_GAP_M
	return maxi(1, ceili(length / maxf(slot_spacing, 1.0) - 1e-6))


## Which way each pen's hose port faces, as a unit x in the site frame
## (+1 or -1): a one-row site turns its ports to the barge side, a two-row
## site to the lane between the rows, where the hoses run (reference 1).
static func port_sides(pen_count: int, barge_side: float) -> Array[float]:
	var dims := grid_dims(pen_count)
	var out: Array[float] = []
	for p in pen_positions(pen_count):
		if dims.x == 1:
			out.append(signf(barge_side) if barge_side != 0.0 else 1.0)
		else:
			out.append(-signf(p.x))
	return out


## The pen's yaw (rotation about +Y) that turns its +X hose port to `side`.
static func port_yaw(side: float) -> float:
	return 0.0 if side > 0.0 else PI


## Where the barge's hoses leave its grid-facing side: one outlet per pen,
## spread along the hull, sorted along the channel (site frame, y = 0).
## barge_at: the barge centre; barge_side: the side of the grid it lies on.
static func barge_outlets(pen_count: int, barge_at: Vector2, barge_side: float, barge_length: float,
		barge_beam: float) -> PackedVector2Array:
	var n := maxi(pen_count, 1)
	var out := PackedVector2Array()
	var x := barge_at.x - signf(barge_side) * barge_beam * 0.5
	for k in n:
		var t := 0.5 if n == 1 else float(k) / float(n - 1)
		out.append(Vector2(x, barge_at.y + (t - 0.5) * OUTLET_SPREAD * barge_length))
	return out


## The interior frame line (z) where a two-row site's hose bundle enters the
## lane between the rows: the one nearest the barge.
static func entry_z(pen_count: int, barge_z: float) -> float:
	var dims := grid_dims(pen_count)
	var half_l := dims.y * MooringGrid.CELL_M * 0.5
	if dims.y < 2:
		return 0.0
	var best := 0.0
	var best_d := INF
	for j in range(1, dims.y):
		var z := float(j) * MooringGrid.CELL_M - half_l
		if absf(z - barge_z) < best_d:
			best_d = absf(z - barge_z)
			best = z
	return best


## The key points of every hose, pen 1 first (site frame, x and z): from a
## barge outlet, off the hull, to the pen's port. A one-row site's hoses run
## straight out to their pens; a two-row site's bundle crosses the near row
## between two pens, runs along the lane between the rows and branches to
## each pen's port there.
static func hose_key_points(pen_count: int, barge_at: Vector2, barge_side: float, barge_length: float,
		barge_beam: float) -> Array[PackedVector2Array]:
	var dims := grid_dims(pen_count)
	var pens := pen_positions(pen_count)
	var sides := port_sides(pen_count, barge_side)
	var s := signf(barge_side) if barge_side != 0.0 else 1.0
	var half_w := dims.x * MooringGrid.CELL_M * 0.5
	var outlets := barge_outlets(pen_count, barge_at, s, barge_length, barge_beam)
	# Outlets in the order of the pens' z, so the hoses fan out uncrossed.
	var order: Array[int] = []
	for k in pens.size():
		order.append(k)
	order.sort_custom(func(a: int, b: int) -> bool:
		return pens[a].z < pens[b].z or (pens[a].z == pens[b].z and pens[a].x * s > pens[b].x * s))
	var outlet_of: Array[int] = []
	outlet_of.resize(pens.size())
	for rank in order.size():
		outlet_of[order[rank]] = rank
	var routes: Array[PackedVector2Array] = []
	var z_gate := entry_z(pen_count, barge_at.y)
	# Lane offsets: hoses heading up and down the lane form two bundles.
	var lane_rank := _lane_ranks(pens, z_gate)
	for k in pens.size():
		var pen := Vector2(pens[k].x, pens[k].z)
		var port := pen + Vector2(sides[k] * PORT_R, 0.0)
		var o := outlets[outlet_of[k]]
		var pts := PackedVector2Array([o, o - Vector2(s * OUTLET_DROP_M, 0.0)])
		if dims.x == 1:
			pts.append(Vector2(port.x + s * 10.0, lerpf(o.y, port.y, 0.7)))
			pts.append(port + Vector2(s * 4.0, 0.0))
			pts.append(port)
		else:
			var spread := (float(outlet_of[k]) - (pens.size() - 1) * 0.5) * BUNDLE_SPACING_M
			var lane_x := float(lane_rank[k]) * BUNDLE_SPACING_M
			pts.append(Vector2(s * (half_w + 6.0), z_gate + spread))
			pts.append(Vector2(s * (half_w - MooringGrid.CELL_M * 0.5), z_gate + spread * 0.8))
			pts.append(Vector2(lane_x + s * 4.0, z_gate + spread * 0.5))
			var dz := pen.y - z_gate
			var lead := minf(28.0, absf(dz) * 0.45)
			if absf(dz) > 1.0:
				pts.append(Vector2(lane_x, pen.y - signf(dz) * lead))
			pts.append(Vector2(lerpf(lane_x, port.x, 0.6), pen.y - signf(dz) * 4.0))
			pts.append(port)
		routes.append(pts)
	return routes


## Per pen, the hose's place across the lane: hoses heading up the lane
## (z above the gate) lie on one side of its centre, those heading down on
## the other, nearest pens innermost.
static func _lane_ranks(pens: Array[Vector3], z_gate: float) -> Array[float]:
	var ranks: Array[float] = []
	ranks.resize(pens.size())
	for dir in [-1.0, 1.0]:
		var group: Array[int] = []
		for k in pens.size():
			var dz := pens[k].z - z_gate
			if (dz >= 0.0) == (dir > 0.0):
				group.append(k)
		group.sort_custom(func(a: int, b: int) -> bool: return absf(pens[a].z - z_gate) < absf(pens[b].z - z_gate))
		for i in group.size():
			ranks[group[i]] = dir * (0.6 + float(i))
	return ranks


## A smooth hose centreline through key points (Catmull-Rom), resampled
## every HOSE_STEP_M, with a gentle deterministic meander that fades out at
## both ends (floating hoses are never taut). `variant` varies the meander.
static func smooth_hose(keys: PackedVector2Array, variant: int) -> PackedVector2Array:
	var dense := PackedVector2Array()
	if keys.size() < 2:
		return keys
	for i in keys.size() - 1:
		var p0 := keys[maxi(i - 1, 0)]
		var p1 := keys[i]
		var p2 := keys[i + 1]
		var p3 := keys[mini(i + 2, keys.size() - 1)]
		var steps := maxi(2, ceili(p1.distance_to(p2) / 1.0))
		for s in steps:
			var t := float(s) / float(steps)
			dense.append(_catmull(p0, p1, p2, p3, t))
	dense.append(keys[keys.size() - 1])
	var total := 0.0
	var lengths := PackedFloat32Array([0.0])
	for i in range(1, dense.size()):
		total += dense[i].distance_to(dense[i - 1])
		lengths.append(total)
	var n := maxi(2, ceili(total / HOSE_STEP_M) + 1)
	var out := PackedVector2Array()
	var j := 1
	var phase := float(absi(variant) % 997) * 0.37
	for i in n:
		var target := total * float(i) / float(n - 1)
		while j < dense.size() - 1 and lengths[j] < target:
			j += 1
		var seg := lengths[j] - lengths[j - 1]
		var t := 0.0 if seg <= 0.0 else (target - lengths[j - 1]) / seg
		var p := dense[j - 1].lerp(dense[j], t)
		var dir := (dense[j] - dense[j - 1]).normalized()
		var taper := smoothstep(0.0, 18.0, target) * smoothstep(0.0, 18.0, total - target)
		var wiggle := MEANDER_AMPLITUDE_M * taper * sin(TAU * target / MEANDER_WAVELENGTH_M + phase)
		out.append(p + Vector2(-dir.y, dir.x) * wiggle)
	return out


static func _catmull(p0: Vector2, p1: Vector2, p2: Vector2, p3: Vector2, t: float) -> Vector2:
	var t2 := t * t
	var t3 := t2 * t
	return 0.5 * ((2.0 * p1) + (-p0 + p2) * t + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2
		+ (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3)


## Yellow hose floats: one every FLOAT_SPACING_M along each hose (from
## FLOAT_SPACING_M / 2 off the barge), merged where hoses run together.
static func hose_floats(hoses: Array[PackedVector2Array]) -> PackedVector2Array:
	var floats := PackedVector2Array()
	for hose in hoses:
		var run := 0.0
		var next := FLOAT_SPACING_M * 0.5
		for i in range(1, hose.size()):
			run += hose[i].distance_to(hose[i - 1])
			if run >= next and i < hose.size() - 6:
				next += FLOAT_SPACING_M
				var merged := false
				for f in floats:
					if f.distance_to(hose[i]) < FLOAT_MERGE_M:
						merged = true
						break
				if not merged:
					floats.append(hose[i])
	return floats
