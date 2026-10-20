extends RefCounted
## A boat that sails a route along the fjord channel at sea level to its
## berth at a site (berth_route), shared by the wellboat and feed-barge
## managers. Load it with preload: it is not a global class.


## Route planning (berth_route): waypoints every ROUTE_STEP_M along the
## channel; a blocked lane is searched sideways in LANE_STEP_M steps out to
## LANE_SEARCH_M; KEEL_CLEARANCE_M of water stays under the keel and
## LANE_CLEARANCE_M between the hull and a site's mooring or berths; the
## last RUN_IN_EXTRA_M plus half the hull run straight in along the grid.
const ROUTE_STEP_M := 50.0
const LANE_STEP_M := 5.0
const LANE_SEARCH_M := 400.0
const KEEL_CLEARANCE_M := 2.0
const LANE_CLEARANCE_M := 4.0
const RUN_IN_EXTRA_M := 30.0


## Builds parent > Path3D > PathFollow3D > boat along `route`, the boat
## (already instantiated: its size planned the route) at the route's start.
## Returns {path, follow, boat}.
static func launch_boat(parent: Node, boat: Node3D, route: PackedVector3Array, boat_name: String) -> Dictionary:
	var curve := Curve3D.new()
	for point in route:
		curve.add_point(point)
	var path := Path3D.new()
	path.name = "%sPath" % boat_name
	path.curve = curve
	var follow := PathFollow3D.new()
	follow.name = "Follow"
	follow.rotation_mode = PathFollow3D.ROTATION_Y
	follow.loop = false  # a route has two ends: clamp at them, never wrap round
	path.add_child(follow)
	boat.name = boat_name
	follow.add_child(boat)
	parent.add_child(path)
	return {"path": path, "follow": follow, "boat": boat}


## Sea-level waypoints from `from_z` on the channel to a site berth
## (FishPenManager.get_berth): every waypoint lies over water deep enough
## for the boat's keel along its whole length and clear of every site's
## mooring grid and berths in `zones` (FishPenManager.site_zones, the
## boat's own berth left open), keeping as close as it can to the berth's
## lane beside the centreline; the last leg runs straight in along the
## site's grid axis, so the boat lies at its berth parallel to the grid.
static func berth_route(terrain: FjordTerrain, from_z: float, berth: Dictionary, zones: Array,
		boat: VesselModel) -> PackedVector3Array:
	var at: Vector3 = berth["position"]
	var axis: Vector3 = berth["axis"]
	var heading := axis if at.z >= from_z else -axis
	var half_len := boat.length_m * 0.5
	var half_beam := boat.beam_m * 0.5
	var depth := boat.get_draught() + KEEL_CLEARANCE_M
	var run_in := at - heading * (half_len + RUN_IN_EXTRA_M)
	var lane := at.x - terrain.channel_center_x(at.z)
	var points := PackedVector3Array()
	if (run_in.z - from_z) * signf(at.z - from_z) > 0.0:
		var count := maxi(1, ceili(absf(run_in.z - from_z) / ROUTE_STEP_M))
		for i in count:
			var z := lerpf(from_z, run_in.z, float(i) / float(count))
			var offset := _clear_lane(terrain, z, lane, half_len, half_beam, depth, zones)
			points.append(Vector3(terrain.channel_center_x(z) + offset, 0.0, z))
		points.append(Vector3(run_in.x, 0.0, run_in.z))
	else:
		# A berth closer to the start than the run-in: straight in from there.
		points.append(Vector3(terrain.channel_center_x(from_z) + lane, 0.0, from_z))
	points.append(Vector3(at.x, 0.0, at.z))
	return points


## The lateral offset from the centreline nearest `lane` where a hull at z
## floats clear (the lane itself, with a warning, when nothing near is).
static func _clear_lane(terrain: FjordTerrain, z: float, lane: float, half_len: float, half_beam: float,
		depth: float, zones: Array) -> float:
	# The centreline under the stern, amidships and the bow: it does not
	# depend on the lane, and every lookup is a call into the C# generator.
	var centres := Vector3(terrain.channel_center_x(z - half_len), terrain.channel_center_x(z),
		terrain.channel_center_x(z + half_len))
	for j in int(LANE_SEARCH_M / LANE_STEP_M) + 1:
		for side in [1.0, -1.0]:
			var offset: float = lane + float(side) * float(j) * LANE_STEP_M
			if _lane_is_clear(terrain, z, centres, offset, half_len, half_beam, depth, zones):
				return offset
	push_warning("[ChannelBoat] no clear lane within %.0f m at z %.0f: the route keeps lane %.0f m"
		% [LANE_SEARCH_M, z, lane])
	return lane


static func _lane_is_clear(terrain: FjordTerrain, z: float, centres: Vector3, offset: float, half_len: float,
		half_beam: float, depth: float, zones: Array) -> bool:
	var x := centres.y + offset
	for zone in zones:
		var dz := z - float(zone["z"])
		if absf(dz) > float(zone["half_z"]) + half_len + ROUTE_STEP_M:
			continue
		var shift := float(zone["x_per_z"]) * dz
		if x + half_beam + LANE_CLEARANCE_M > float(zone["lo"]) + shift \
				and x - half_beam - LANE_CLEARANCE_M < float(zone["hi"]) + shift:
			return false
	for k in 3:
		var zz := z + (float(k) - 1.0) * half_len
		var cx := centres[k] + offset
		for across in [-(half_beam + 1.0), 0.0, half_beam + 1.0]:
			if terrain.get_height_at(cx + float(across), zz) > -depth:
				return false
	return true


## Turns the boat to face back along the route for the trip home.
static func face_home(boat: Node3D) -> void:
	boat.rotation.y = PI


## Tints a copy of the boat's deck ShaderMaterial (its override, or the
## Blender vessels' imported surface material): every boat shares the
## scene's material. False when the scene has no tintable Deck. The vessel
## managers no longer call it: job state shows on a VesselChip, never in a
## boat's paint (roadmap A3-01).
static func tint_deck(boat: Node3D, colour: Color) -> bool:
	var deck := boat.get_node_or_null("Deck") as MeshInstance3D
	if deck == null:
		return false
	var source: Material = deck.material_override if deck.material_override else deck.get_active_material(0)
	if not (source is ShaderMaterial):
		return false
	var mat := source.duplicate() as ShaderMaterial
	mat.set_shader_parameter("albedo_color", colour)
	deck.material_override = mat
	return true
