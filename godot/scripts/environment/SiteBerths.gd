class_name SiteBerths
extends RefCounted
## Where a site's boats lie and what water it keeps clear (roadmap G2-04,
## A3-01), as pure functions over the terrain and a site's record
## (FishPenManager._pens entries: position, grid_yaw, barge_side, pen_count).
##
## A site's frame mooring is laid along the channel (its local +Z follows the
## fjord's centreline at the site). The feed barge lies on one side of the
## grid, square off its middle, its hoses to every pen; a wellboat comes
## alongside on the other side; both lie outside the grid buoys. The
## wellboat takes the side facing the fairway (the open water a 116 m hull
## needs; on the centreline, the deeper side), the barge the other.

## Metres from the grid's outer frame line to the barge's inner hull side:
## the hoses leave the hull, drop to the water and float in at the grid.
const BARGE_GRID_GAP_M := 16.0
## ... and to a wellboat's inner hull side: 3.5 m outside the grid buoys.
const WELLBOAT_GRID_GAP_M := 3.5
## What a site keeps clear for passing boats, beyond the grid: the widest
## wellboat (23 m beam) on its side, the large barge (13 m) with its tender
## RIB (3 m and a 0.5 m gap) on the other, over at least this half-length
## along the grid axis (a 116 m hull alongside, plus room to turn in).
const WELLBOAT_RESERVE_BEAM_M := 23.0
const BARGE_RESERVE_BEAM_M := 16.5
const SITE_RESERVE_HALF_LENGTH_M := 80.0
const SITE_RESERVE_END_M := 12.0
## A boat heading for a berth sees that side of its site clear from this far
## inside its own inner hull side, more than ChannelBoat's 4 m lane
## clearance, so the berth's own lane stays open all the way in.
const OPEN_BERTH_SLACK_M := 5.0
## Water a berth side needs under its footprint (m: the 116 m carrier's
## 8.5 m draught plus ChannelBoat's 2 m keel clearance, and a margin), and
## the footprint's along-axis reach either side of the site centre (m).
const BERTH_MIN_DEPTH_M := 12.0
const BERTH_SAMPLE_REACH_M := 60.0
## The channel's heading at a site is the centreline's slope over +-this (m).
const CHANNEL_HEADING_SPAN_M := 20.0


## Heading (yaw about +Y) of the fjord channel at world z (0 without terrain).
static func channel_yaw(terrain: FjordTerrain, z: float) -> float:
	if terrain == null:
		return 0.0
	var dx := terrain.channel_center_x(z + CHANNEL_HEADING_SPAN_M) - terrain.channel_center_x(z - CHANNEL_HEADING_SPAN_M)
	return atan2(dx, 2.0 * CHANNEL_HEADING_SPAN_M)


## The world-x side (+1 or -1) a site's feed barge moors on; its wellboat
## berth is the other side.
static func choose_barge_side(terrain: FjordTerrain, position: Vector3, grid_yaw: float, half_w: float) -> float:
	if terrain == null:
		return 1.0
	var to_fairway := terrain.channel_center_x(position.z) - position.x
	var wellboat_side := signf(to_fairway)
	if absf(to_fairway) < 1.0:
		var plus := berth_depth(terrain, position, grid_yaw, 1.0, half_w)
		wellboat_side = 1.0 if plus >= berth_depth(terrain, position, grid_yaw, -1.0, half_w) else -1.0
	var barge_side := -wellboat_side
	if berth_depth(terrain, position, grid_yaw, barge_side, half_w) < BERTH_MIN_DEPTH_M \
			and berth_depth(terrain, position, grid_yaw, wellboat_side, half_w) >= BERTH_MIN_DEPTH_M:
		barge_side = wellboat_side
	return barge_side


## The shallowest water (m) under a berth side's footprint.
static func berth_depth(terrain: FjordTerrain, position: Vector3, grid_yaw: float, side: float, half_w: float) -> float:
	var across := Vector3(cos(grid_yaw), 0.0, -sin(grid_yaw)) * side
	var along := Vector3(sin(grid_yaw), 0.0, cos(grid_yaw))
	var shallowest := INF
	for reach in [half_w + WELLBOAT_GRID_GAP_M, half_w + BARGE_GRID_GAP_M + BARGE_RESERVE_BEAM_M * 0.5,
			half_w + WELLBOAT_GRID_GAP_M + WELLBOAT_RESERVE_BEAM_M]:
		for t in [-1.0, 0.0, 1.0]:
			var q: Vector3 = position + across * float(reach) + along * (BERTH_SAMPLE_REACH_M * float(t))
			shallowest = minf(shallowest, -terrain.get_height_at(q.x, q.z))
	return shallowest


## Where a vessel of `beam_m` lies at a site: role "barge" (square off the
## grid's middle, so its hoses run straight in to the lane between the rows)
## or "wellboat" (alongside, the other side, level with the site centre in
## world z). {position: the world point a channel route ends at; axis: the
## grid's +Z, the way the vessel lies; side: +1 or -1 in world x}.
static func berth(site: Dictionary, role: String, beam_m: float) -> Dictionary:
	var yaw := float(site.get("grid_yaw", 0.0))
	var barge_side := float(site.get("barge_side", 1.0))
	var half_w := SiteLayout.half_extent(int(site.get("pen_count", 1))).x
	var side := barge_side if role == "barge" else -barge_side
	var hull_side := half_w + (BARGE_GRID_GAP_M if role == "barge" else WELLBOAT_GRID_GAP_M)
	var at: Vector3 = site["position"]
	var axis := Vector3(sin(yaw), 0.0, cos(yaw))
	if role == "barge":
		var across := Vector3(cos(yaw), 0.0, -sin(yaw))
		return {"position": at + across * side * (hull_side + beam_m * 0.5), "axis": axis, "side": side}
	# The perpendicular distance to the hull's centreline, laid off along x.
	var reach := (hull_side + beam_m * 0.5) / cos(yaw)
	return {"position": Vector3(at.x + side * reach, 0.0, at.z), "axis": axis, "side": side}


## The water a site keeps clear for passing boats: its mooring grid and both
## berths (the wellboat's for the widest hull), as a band of world x at the
## site's z that shifts with the grid's heading along z; `open_role` leaves
## that berth open (a boat heading for it). {z, x_per_z, lo, hi, half_z}
static func zone(site: Dictionary, open_role: String = "") -> Dictionary:
	var at: Vector3 = site["position"]
	var yaw := float(site.get("grid_yaw", 0.0))
	var barge_side := float(site.get("barge_side", 1.0))
	var half := SiteLayout.half_extent(int(site.get("pen_count", 1)))
	var barge_reach := half.x + BARGE_GRID_GAP_M + BARGE_RESERVE_BEAM_M
	var wellboat_reach := half.x + WELLBOAT_GRID_GAP_M + WELLBOAT_RESERVE_BEAM_M
	if open_role == "barge":
		barge_reach = half.x + BARGE_GRID_GAP_M - OPEN_BERTH_SLACK_M
	if open_role == "wellboat":
		wellboat_reach = half.x + WELLBOAT_GRID_GAP_M - OPEN_BERTH_SLACK_M
	var plus := (barge_reach if barge_side > 0.0 else wellboat_reach) / cos(yaw)
	var minus := (wellboat_reach if barge_side > 0.0 else barge_reach) / cos(yaw)
	return {
		"z": at.z,
		"x_per_z": tan(yaw),
		"lo": at.x - minus,
		"hi": at.x + plus,
		"half_z": maxf(SITE_RESERVE_HALF_LENGTH_M, half.y + SITE_RESERVE_END_M),
	}
