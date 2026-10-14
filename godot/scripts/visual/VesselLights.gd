class_name VesselLights
extends RefCounted
## A vessel's lights after dark (shaders/vessel_light_glow.gdshader): one
## MultiMesh of camera-facing glows on the vessel, so they ride the water
## with it and cost one draw call, drawn only at night (SiteLights).
##
##   workboat, wellboat, RIB  a white masthead light over the wheelhouse (a
##                            wellboat, over 50 m, a second one forward, lower),
##                            red and green side lights on the wheelhouse
##                            sides, a white stern light
##   feed barge               an all-round white light on its mast, a yellow
##                            marker flashing with the site's corner lights,
##                            and white deck lamps on the control house roof
##
## Where the lights go is read off the vessel's own LOD0 meshes once per
## asset (build_vessels.py puts the mast, the wheelhouse windows and the
## deck there): the top of the superstructure is the masthead, the highest
## band of windows the wheelhouse, the after end of the deck the stern.

const LIGHT_MATERIAL := preload("res://assets/models/vessel_kit/vessel_light_material.tres")
const WHITE := Color(1.0, 0.96, 0.88)
const RED := Color(1.0, 0.16, 0.12)
const GREEN := Color(0.15, 1.0, 0.45)
## Synchronised yellow (#FFC23A) of the site's corner lights.
const YELLOW := Color(1.0, 0.761, 0.227)
## COLREGS arcs (rad): masthead 225 degrees forward, side lights 112.5 each
## from dead ahead, stern light 135 aft; all round.
const MASTHEAD_HALF := 1.9635
const SIDE_HALF := 0.98175
const STERN_HALF := 1.1781
const ALL_ROUND := PI
## The site's corner lights flash every 4 s (mooring_lamp_material).
const MARKER_PERIOD_S := 4.0
## Core screen brightness of each kind of light.
const NAV_BRIGHT := 2.0
const SIDE_BRIGHT := 2.4
const DECK_BRIGHT := 0.9
## A vessel this long carries a second masthead light forward.
const TWO_MASTHEADS_M := 50.0
const LIGHTS_NAME := "NavLights"
## Lights show this far (m).
const RANGE_M := 9000.0

static var _anchors: Dictionary = {}  # asset_id -> Dictionary


## Hang the lights on `vessel` (once; again replaces them).
static func attach(vessel: VesselModel) -> MultiMeshInstance3D:
	var old := vessel.get_node_or_null(LIGHTS_NAME)
	if old != null:
		old.free()
	var lights := lights_for(vessel)
	if lights.is_empty():
		return null
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_colors = true
	mm.use_custom_data = true
	mm.mesh = SiteLights.glow_mesh()
	mm.instance_count = lights.size()
	for i in lights.size():
		var light: Dictionary = lights[i]
		mm.set_instance_transform(i, Transform3D(Basis.IDENTITY, light["at"]))
		mm.set_instance_color(i, light["colour"])
		mm.set_instance_custom_data(i, Color(light["centre"], light["half"], light["period"], light["bright"]))
	var mmi := MultiMeshInstance3D.new()
	mmi.name = LIGHTS_NAME
	mmi.multimesh = mm
	mmi.material_override = LIGHT_MATERIAL
	mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	# The glows grow past their quads on screen.
	mmi.extra_cull_margin = 60.0
	mmi.visibility_range_end = RANGE_M
	mmi.add_to_group(SiteLights.NIGHT_ONLY_GROUP)
	mmi.visible = SiteLights.night_shown()
	vessel.add_child(mmi)
	return mmi


## The lights of `vessel`: [{at (local), colour, centre, half, period, bright}].
static func lights_for(vessel: VesselModel) -> Array:
	var a := anchors(vessel)
	if a.is_empty():
		return []
	var out: Array = []
	var top: Vector3 = a["mast_top"]
	if vessel.asset_id.begins_with("feed_barge"):
		out.append(_light(top + Vector3(0.0, 0.25, 0.0), WHITE, 0.0, ALL_ROUND, 0.0, NAV_BRIGHT))
		out.append(_light(top + Vector3(0.0, -0.9, 0.0), YELLOW, 0.0, ALL_ROUND, MARKER_PERIOD_S, NAV_BRIGHT))
		var roof: AABB = a["roof"]
		for corner in [Vector2(0, 0), Vector2(1, 0), Vector2(0, 1), Vector2(1, 1)]:
			var at := roof.position + Vector3(roof.size.x * corner.x, roof.size.y + 0.4, roof.size.z * corner.y)
			out.append(_light(at, WHITE, 0.0, ALL_ROUND, 0.0, DECK_BRIGHT))
		return out
	if vessel.asset_id == "rib":
		out.append(_light(top + Vector3(0.0, 0.2, 0.0), WHITE, 0.0, ALL_ROUND, 0.0, NAV_BRIGHT))
		return out
	out.append(_light(top + Vector3(0.0, 0.2, 0.0), WHITE, 0.0, MASTHEAD_HALF, 0.0, NAV_BRIGHT))
	if vessel.length_m > TWO_MASTHEADS_M and a.has("fore_top"):
		out.append(_light(a["fore_top"] + Vector3(0.0, 0.2, 0.0), WHITE, 0.0, MASTHEAD_HALF, 0.0, NAV_BRIGHT))
	var bridge: AABB = a["bridge"]
	var side_y := bridge.position.y + bridge.size.y * 0.5
	var side_z := bridge.position.z + 0.4
	out.append(_light(Vector3(bridge.end.x + 0.15, side_y, side_z), GREEN, SIDE_HALF, SIDE_HALF, 0.0, SIDE_BRIGHT))
	out.append(_light(Vector3(bridge.position.x - 0.15, side_y, side_z), RED, -SIDE_HALF, SIDE_HALF, 0.0, SIDE_BRIGHT))
	out.append(_light(a["stern"], WHITE, PI, STERN_HALF, 0.0, NAV_BRIGHT))
	return out


static func _light(at: Vector3, colour: Color, centre: float, half: float, period: float, bright: float) -> Dictionary:
	return {"at": at, "colour": colour, "centre": centre, "half": half, "period": period, "bright": bright}


## Where a vessel's lights go, read off its LOD0 meshes (cached per asset):
## mast_top, fore_top (a foremast, if one stands well forward), bridge (AABB
## of the highest band of windows), roof (AABB of the top of the
## superstructure), stern (the after end of the deck, a metre up).
static func anchors(vessel: VesselModel) -> Dictionary:
	if _anchors.has(vessel.asset_id):
		return _anchors[vessel.asset_id]
	var sup := _part_points(vessel, "Superstructure")
	var windows := _part_points(vessel, "Windows")
	var deck := _part_points(vessel, "Deck")
	if sup.is_empty():
		return {}
	var top := sup[0]
	for p in sup:
		if p.y > top.y:
			top = p
	var out := {"mast_top": top}
	# A foremast: the highest superstructure point in the forward fifth.
	var bow_z := -0.5 * vessel.length_m
	var fore := Vector3(0.0, -INF, 0.0)
	for p in sup:
		if p.z < bow_z + 0.2 * vessel.length_m and p.y > fore.y and absf(p.x) < 0.5:
			fore = p
	if fore.y > top.y - 12.0 and fore.y > 4.0 and fore.z < top.z - 10.0:
		out["fore_top"] = fore
	var band := windows if not windows.is_empty() else sup
	var high := -INF
	for p in band:
		high = maxf(high, p.y)
	out["bridge"] = _box(band.filter(func(p: Vector3) -> bool: return p.y > high - 1.6))
	var roof_y := -INF
	for p in sup:
		if absf(p.x) > 1.6:
			roof_y = maxf(roof_y, p.y)
	out["roof"] = _box(sup.filter(func(p: Vector3) -> bool: return p.y > roof_y - 0.3 and absf(p.x) > 1.6))
	var aft := deck if not deck.is_empty() else sup
	var stern_z := -INF
	for p in aft:
		stern_z = maxf(stern_z, p.z)
	var stern_y := -INF
	for p in aft:
		if p.z > stern_z - 2.5:
			stern_y = maxf(stern_y, p.y)
	out["stern"] = Vector3(0.0, stern_y + 1.0, stern_z - 0.4)
	_anchors[vessel.asset_id] = out
	return out


static func _part_points(vessel: VesselModel, part: String) -> Array[Vector3]:
	var points: Array[Vector3] = []
	for mesh in vessel.get_lod_meshes(0):
		if mesh.name != part or mesh.mesh == null:
			continue
		for s in mesh.mesh.get_surface_count():
			var verts: PackedVector3Array = mesh.mesh.surface_get_arrays(s)[Mesh.ARRAY_VERTEX]
			for v in verts:
				points.append(mesh.transform * v)
	return points


static func _box(points: Array) -> AABB:
	if points.is_empty():
		return AABB()
	var box := AABB(points[0], Vector3.ZERO)
	for p in points:
		box = box.expand(p)
	return box
