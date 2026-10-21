class_name GeoClusterer
extends RefCounted
## Screen-grid clustering of the national map's sites (geo stages 2-3 plan,
## Track C). At national scale one screen pixel is 2-3.5 km, so the 258 sites
## of PO3 would sit on top of each other: points are binned into square cells
## of `cell_px` screen pixels, and clusters whose centres end up closer than
## `min_gap_px` are merged, largest first.
##
## Deterministic: cells are visited in (row, column) order, members keep their
## input order, and merges break ties by the cluster's first member. O(n) for
## the binning, O(k²) for the merge over the k occupied cells (k is a few
## hundred at most). Run on a layout change only, never on a tick.
##
## A cluster: {pos: Vector2 (members' mean), count: int, by_class:
## PackedInt32Array (GeoBridgeAdapter.CLASS_COUNT), owners: {owner index:
## count} (owner >= 0 only), members: PackedInt32Array (input indices)}.


static func cluster(points: PackedVector2Array, classes: PackedByteArray, owners: PackedInt32Array,
		cell_px: float, min_gap_px: float) -> Array:
	var cells := {}
	for i in points.size():
		var key := Vector2i(floori(points[i].x / cell_px), floori(points[i].y / cell_px))
		var members: PackedInt32Array = cells.get(key, PackedInt32Array())
		members.append(i)
		cells[key] = members
	var keys := cells.keys()
	keys.sort_custom(func(a: Vector2i, b: Vector2i) -> bool: return a.y < b.y or (a.y == b.y and a.x < b.x))
	var out: Array = []
	for key: Vector2i in keys:
		out.append(_make(cells[key], points, classes, owners))
	return _merge(out, points, classes, owners, min_gap_px)


## Merge clusters whose drawn marks would overlap (GEO-VIS: on a 1280x720
## window the badges grow to keep their numerals readable, and a fixed gap
## left them stacked over each other and the city names). `gap_of(a, b)` is
## the least distance between the centres of clusters of `a` and `b` sites
## that keeps either from covering the other; two clusters closer than that
## become one, biggest first (on a tie, the one whose first member comes
## first), round after round until none are, since a merged cluster moves and
## grows. Deterministic; members keep their order and the output is sorted
## as cluster() sorts it.
static func declutter(clusters: Array, points: PackedVector2Array, classes: PackedByteArray,
		owners: PackedInt32Array, gap_of: Callable) -> Array:
	var out := clusters.duplicate()
	for _round in MAX_DECLUTTER_ROUNDS:
		var order := range(out.size())
		order.sort_custom(func(a: int, b: int) -> bool:
			var ca: int = out[a]["count"]
			var cb: int = out[b]["count"]
			return ca > cb or (ca == cb and _first(out[a]) < _first(out[b])))
		var kept: Array = []
		var merged := false
		for idx: int in order:
			var c: Dictionary = out[idx]
			var host := -1
			for j in kept.size():
				var gap := float(gap_of.call(int(kept[j]["count"]), int(c["count"])))
				if (kept[j]["pos"] as Vector2).distance_to(c["pos"]) < gap:
					host = j
					break
			if host < 0:
				kept.append(c)
				continue
			var members: PackedInt32Array = kept[host]["members"].duplicate()
			members.append_array(c["members"])
			kept[host] = _make(members, points, classes, owners)
			merged = true
		out = kept
		if not merged:
			break
	out.sort_custom(func(a: Dictionary, b: Dictionary) -> bool: return _first(a) < _first(b))
	return out


## Rounds of declutter() at most (it settles in a few: every round that
## merges leaves fewer clusters).
const MAX_DECLUTTER_ROUNDS := 32


## Totals over clusters: PackedInt32Array of CLASS_COUNT counts.
static func totals(clusters: Array) -> PackedInt32Array:
	var out := PackedInt32Array()
	out.resize(GeoBridgeAdapter.CLASS_COUNT)
	for c: Dictionary in clusters:
		var by: PackedInt32Array = c["by_class"]
		for k in by.size():
			out[k] += by[k]
	return out


## The class most members have (the lowest class index on a tie).
static func dominant_class(c: Dictionary) -> int:
	var by: PackedInt32Array = c["by_class"]
	var best := 0
	for k in by.size():
		if by[k] > by[best]:
			best = k
	return best


static func _make(members: PackedInt32Array, points: PackedVector2Array, classes: PackedByteArray,
		owners: PackedInt32Array) -> Dictionary:
	var sum := Vector2.ZERO
	var by := PackedInt32Array()
	by.resize(GeoBridgeAdapter.CLASS_COUNT)
	var own := {}
	for i in members:
		sum += points[i]
		by[mini(classes[i], GeoBridgeAdapter.CLASS_COUNT - 1)] += 1
		var o := owners[i] if i < owners.size() else -1
		if o >= 0:
			own[o] = int(own.get(o, 0)) + 1
	return {"pos": sum / float(members.size()), "count": members.size(), "by_class": by, "owners": own,
		"members": members}


## Merge clusters closer than min_gap, in one pass: biggest first (on a tie,
## the one whose first member comes first), each absorbed by the first kept
## cluster within min_gap of it.
static func _merge(clusters: Array, points: PackedVector2Array, classes: PackedByteArray,
		owners: PackedInt32Array, min_gap: float) -> Array:
	var order := range(clusters.size())
	order.sort_custom(func(a: int, b: int) -> bool:
		var ca: int = clusters[a]["count"]
		var cb: int = clusters[b]["count"]
		return ca > cb or (ca == cb and clusters[a]["members"][0] < clusters[b]["members"][0]))
	var kept: Array = []
	for idx: int in order:
		var c: Dictionary = clusters[idx]
		var host := -1
		for j in kept.size():
			if (kept[j]["pos"] as Vector2).distance_to(c["pos"]) < min_gap:
				host = j
				break
		if host < 0:
			kept.append(c)
			continue
		var members: PackedInt32Array = kept[host]["members"].duplicate()
		members.append_array(c["members"])
		kept[host] = _make(members, points, classes, owners)
	# Stable output order: by the first member's input index.
	kept.sort_custom(func(a: Dictionary, b: Dictionary) -> bool: return _first(a) < _first(b))
	return kept


static func _first(c: Dictionary) -> int:
	var m: PackedInt32Array = c["members"]
	var lo := m[0]
	for i in m:
		lo = mini(lo, i)
	return lo
