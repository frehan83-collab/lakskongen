class_name MapLabelPlacer
extends RefCounted
## Deterministic label placement for the national map's area plates (geo
## stages 2-3 plan, Track C; absorbs U3-03's no-collision acceptance).
##
## Each label has an anchor (its area's label point on screen) and a size.
## Labels are placed in the order given. For each, the 8 compass offsets are
## tried in the order of the label's preferred direction (closest angle
## first), on rings of growing distance; the first rect that lies inside the
## bounds and touches no obstacle, no soft obstacle (the site clusters, the
## candidates, the player's farms: things a plate should not hide) and no
## label placed before wins. A label placed away from its anchor gets a
## leader line. When no candidate is free, the one that covers the least
## soft area without touching a hard obstacle is taken; when every candidate
## touches a hard one, the least overlap is taken and the result says so
## (ok = false), so a caller can fall back to smaller labels.
##
## Same inputs, same output: no randomness, no hash order.

## Unit compass offsets, east first, counter-clockwise in screen space.
const DIRECTIONS: Array[Vector2] = [
	Vector2(1, 0), Vector2(1, -1), Vector2(0, -1), Vector2(-1, -1),
	Vector2(-1, 0), Vector2(-1, 1), Vector2(0, 1), Vector2(1, 1),
]
## Rings: the gap between anchor and label, in px, tried in order.
const RINGS: Array[float] = [6.0, 18.0, 34.0, 54.0, 78.0]
## A label further than this from its anchor draws a leader line.
const LEADER_MIN_PX := 10.0
## The clear radius around every anchor dot a label must keep (the dot's
## rect in MapPanel.region_label_rects is this half-size).
const ANCHOR_CLEAR_PX := 6.0
## Any hard collision outweighs every soft one.
const HARD_WEIGHT := 1.0e7


## Place labels. anchors[i] and sizes[i] per label; prefer[i] is the unit
## direction the label would rather lie in (Vector2.ZERO: no preference,
## east first). Returns {rects: Array[Rect2], leaders: Array[bool], ok: bool,
## overlaps: int (labels touching a hard obstacle), soft_px: float (soft
## area covered)}.
static func place(anchors: PackedVector2Array, sizes: PackedVector2Array, prefer: PackedVector2Array,
		bounds: Rect2, obstacles: Array[Rect2], soft: Array[Rect2] = []) -> Dictionary:
	var placed: Array[Rect2] = []
	var leaders: Array[bool] = []
	var overlaps := 0
	var soft_px := 0.0
	for i in anchors.size():
		var best := Rect2()
		var best_cost := INF
		var found := false
		for ring in RINGS:
			for d in _ordered(prefer[i] if i < prefer.size() else Vector2.ZERO):
				var r := _candidate(anchors[i], sizes[i], d, ring)
				var cost := _cost(r, bounds, obstacles, placed, anchors, i) * HARD_WEIGHT + _soft_cost(r, soft)
				if cost == 0.0:
					best = r
					best_cost = 0.0
					found = true
					break
				if cost < best_cost:
					best_cost = cost
					best = r
			if found:
				break
		if best_cost >= HARD_WEIGHT:
			overlaps += 1
		else:
			soft_px += best_cost
		placed.append(best)
		leaders.append(_gap(best, anchors[i]) > LEADER_MIN_PX)
	return {"rects": placed, "leaders": leaders, "ok": overlaps == 0, "overlaps": overlaps, "soft_px": soft_px}


## Place free-standing labels (city names): beside the anchor in one of the
## 8 directions, at `gap` px; the first spot clear of every obstacle and
## soft obstacle wins, else the first clear of the obstacles alone; a label
## that fits nowhere is left out (an empty Rect2) instead of overlapping.
static func place_optional(anchors: PackedVector2Array, sizes: PackedVector2Array, gap: float,
		bounds: Rect2, obstacles: Array[Rect2], soft: Array[Rect2] = []) -> Array[Rect2]:
	var placed: Array[Rect2] = []
	var taken: Array[Rect2] = obstacles.duplicate()
	var order: Array[Vector2] = [Vector2(1, 0), Vector2(-1, 0), Vector2(0, -1), Vector2(0, 1),
		Vector2(1, -1), Vector2(-1, -1), Vector2(1, 1), Vector2(-1, 1)]
	for i in anchors.size():
		var got := Rect2()
		var fallback := Rect2()
		for d in order:
			var r := _candidate(anchors[i], sizes[i], d, gap)
			if not bounds.encloses(r) or _hits(r, taken):
				continue
			if not _hits(r, soft):
				got = r
				break
			if fallback.size == Vector2.ZERO:
				fallback = r
		if got.size == Vector2.ZERO:
			got = fallback
		placed.append(got)
		if got.size != Vector2.ZERO:
			taken.append(got)
	return placed


## The rect of a label of `size` beside `anchor` in direction `d`, `gap` px
## from it: centred on the axis it leaves along, flush on the diagonals.
static func _candidate(anchor: Vector2, size: Vector2, d: Vector2, gap: float) -> Rect2:
	var pos := anchor - size * 0.5
	if d.x > 0.0:
		pos.x = anchor.x + gap
	elif d.x < 0.0:
		pos.x = anchor.x - gap - size.x
	if d.y > 0.0:
		pos.y = anchor.y + gap * (0.5 if d.x != 0.0 else 1.0)
	elif d.y < 0.0:
		pos.y = anchor.y - gap * (0.5 if d.x != 0.0 else 1.0) - size.y
	return Rect2(pos.round(), size)


## The 8 directions sorted by angle to `prefer` (stable: index order on ties).
static func _ordered(prefer: Vector2) -> Array[Vector2]:
	var out: Array[Vector2] = DIRECTIONS.duplicate()
	if prefer == Vector2.ZERO:
		return out
	var p := prefer.normalized()
	var idx := range(out.size())
	idx.sort_custom(func(a: int, b: int) -> bool:
		var da := p.angle_to(DIRECTIONS[a].normalized())
		var db := p.angle_to(DIRECTIONS[b].normalized())
		return absf(da) < absf(db) - 0.0001 or (absf(absf(da) - absf(db)) <= 0.0001 and a < b))
	var sorted: Array[Vector2] = []
	for i: int in idx:
		sorted.append(DIRECTIONS[i])
	return sorted


## 0 for a free spot; else how badly it collides (area outside the bounds,
## plus overlap with obstacles, labels placed before and other anchors).
static func _cost(r: Rect2, bounds: Rect2, obstacles: Array[Rect2], placed: Array[Rect2],
		anchors: PackedVector2Array, self_index: int) -> float:
	var cost := r.get_area() - bounds.intersection(r).get_area() + 1.0 if not bounds.encloses(r) else 0.0
	for o in obstacles:
		if o.intersects(r):
			cost += o.intersection(r).get_area() + 1.0
	for o in placed:
		if o.intersects(r):
			cost += o.intersection(r).get_area() + 1.0
	# No plate hides another area's anchor dot (ANCHOR_CLEAR_PX around it).
	for j in anchors.size():
		if j != self_index and r.grow(ANCHOR_CLEAR_PX).has_point(anchors[j]):
			cost += 50.0
	return cost


## The soft area a rect covers (1 px² at least per soft obstacle touched).
static func _soft_cost(r: Rect2, soft: Array[Rect2]) -> float:
	var cost := 0.0
	for o in soft:
		if o.intersects(r):
			cost += o.intersection(r).get_area() + 1.0
	return cost


static func _hits(r: Rect2, rects: Array[Rect2]) -> bool:
	for o in rects:
		if o.intersects(r):
			return true
	return false


## Distance from an anchor to the nearest point of a rect.
static func _gap(r: Rect2, anchor: Vector2) -> float:
	var nearest := Vector2(clampf(anchor.x, r.position.x, r.end.x), clampf(anchor.y, r.position.y, r.end.y))
	return nearest.distance_to(anchor)
