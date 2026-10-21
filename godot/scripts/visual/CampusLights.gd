class_name CampusLights
extends Node3D
## A land campus after dark (roadmap F2-23; farm-types spec §5.4: the lid
## work-lights and the hall windows light at every dusk and night, on the
## presentation clock, D20). The kit's own glow (facility_kit: the hall
## windows, the roof lights and the lids' work-light pools) lights the
## buildings and the tanks; this node draws the lamps themselves and the
## light they throw on the yard:
##
## - a camera-facing glow (campus_light_glow.gdshader, one MultiMesh for the
##   campus) at every lamp the kit carries (blender/land_designs.py): the four
##   work-lights on each tank's rail posts (small), the quay's four masts;
##   and at lamps the kit leaves to the layout: the harvest hall's wall lamps,
##   one on each pipe-bridge span, masts along the roads. A glow keeps a few
##   pixels on screen, so the lit campus reads from the land framing out to
##   the home view;
## - a few warm pools of light on the ground (OmniLight3D, no shadows) under
##   the quay's end masts and the hall's wall lamps: lit concrete under a
##   blue-hour sky (ref L2's mood). This node names each pool's energy (its
##   meta "energy"); EnvironmentDirector, the only writer of light energy,
##   switches and sets it (EnvironmentDirector.LIGHT_POOL_GROUP).
##
## A lamp stands once the part it hangs on is built (the campus's groups'
## build shares; a tank's once the engine has handed it over). The
## presenter sets the level and, once a frame, the eye's exposure the glows
## compensate (set_frame); a first-sight sequence lights them in a wave from
## the quay (set_wave).

const GLOW_SHADER := preload("res://shaders/campus_light_glow.gdshader")
## Heights over the pad (m) of the lamps the kit leaves to the layout: the
## hall's wall lamps, the pipe bridge's walkway (its deck stands 10 m over
## the pad), the road masts.
const WALL_LAMP_M := 8.0
const BRIDGE_LAMP_M := 11.0
const MAST_M := 11.0
## Spacing (m) of the road masts.
const ROAD_SPACING_M := 60.0
## The kit's own lamps (blender/land_designs.py, Blender x/y/z to Godot
## x/z/-y): a tank's four work-lights on its rail posts at 45, 135, 225 and
## 315 deg, rail_r = shell radius + 1.25 m, the heads deck + 1.2 m (the deck
## 0.25 m under the shell's top); the quay's masts at x = -50, -16.7, 16.7,
## 50 m on its landward edge (y 10.5 m), heads 10.2 m over the deck.
const TANK_RAIL_EXTRA_M := 1.25
const TANK_LAMP_OVER_TOP_M := 0.95
const QUAY_MASTS_X := [-50.0, -16.7, 16.7, 50.0]
const QUAY_MAST_LOCAL := Vector2(-10.5, 10.2)
## Per lamp kind: (brightness, size) scales of the shared glow.
const MAST := Color(1.0, 1.0, 0.0, 1.0)
const WALL := Color(0.85, 0.8, 0.0, 1.0)
const WALKWAY := Color(0.6, 0.6, 0.0, 1.0)
const TANK := Color(0.55, 0.55, 0.0, 1.0)
## The pools of light on the ground: how many at most, their reach (m),
## energy and colour (the mood palette's artificial light #FFB35C toward
## LED white).
const POOL_LIGHTS := 6
const POOL_RANGE_M := 40.0
const POOL_ENERGY := 2.4
const POOL_COLOR := Color("#FFC98A")
## Pools fade out from this camera distance (m) over the next POOL_FADE_M.
const POOL_FADE_FROM_M := 1100.0
const POOL_FADE_M := 300.0

## The glow material every campus shares (one set of uniforms a frame).
static var glow_material: ShaderMaterial = null

## Every lamp of the parts: {"at": Vector3, "group": String, "kind": Color, "pool": bool}.
var lamps: Array = []
## The tanks' work-lights (their tanks handed over): world positions.
var tank_lamps: Array[Vector3] = []
var glows: MultiMeshInstance3D = null
var pools: Array[OmniLight3D] = []
var _level := 0.0
var _built: Dictionary = {}
var _wave := Vector3(0.0, 0.0, 1e7)


static func shared_material() -> ShaderMaterial:
	if glow_material == null:
		glow_material = ShaderMaterial.new()
		glow_material.shader = GLOW_SHADER
	return glow_material


## The night level (0 by day .. 1) and the eye's exposure for every campus's
## glows (once a frame, from the presenter), and the fog that dims them.
static func set_frame(level: float, exposure: float, fog_density: float) -> void:
	var m := shared_material()
	m.set_shader_parameter("night", clampf(level, 0.0, 1.0))
	m.set_shader_parameter("exposure_comp", 1.0 / maxf(exposure, 1e-3))
	m.set_shader_parameter("fog_density", fog_density)


## Lay out the lamps of `campus` (its groups as planned) and build the glows
## and pools. Call once, after the campus's own build().
func build(campus: LandCampusBuilder) -> void:
	name = "CampusLights"
	add_to_group(EnvironmentDirector.LIGHT_POOL_GROUP)
	lamps.clear()
	var groups: Dictionary = campus.groups
	for group_name: String in groups:
		var group: Dictionary = groups[group_name]
		for piece: Array in group.get("pieces", []):
			_lamps_for_piece(campus, group_name, str(piece[0]), piece[1])
		if group_name == "roads":
			for rect: Array in group.get("rects", []):
				_lamps_along_road(campus, rect[1], rect[2])
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_colors = true
	var quad := QuadMesh.new()
	quad.size = Vector2.ONE
	mm.mesh = quad
	glows = MultiMeshInstance3D.new()
	glows.name = "LampGlows"
	glows.multimesh = mm
	glows.material_override = shared_material()
	glows.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	# Out of the planar mirror's pass: a land lamp is no light on the water there.
	glows.layers = PlanarReflection.NO_MIRROR_LAYER
	glows.extra_cull_margin = 64.0
	glows.visible = false
	add_child(glows)
	for lamp: Dictionary in lamps:
		if bool(lamp["pool"]) and pools.size() < POOL_LIGHTS:
			var light := OmniLight3D.new()
			light.name = "Pool%d" % pools.size()
			light.position = (lamp["at"] as Vector3) + Vector3(0.0, -1.0, 0.0)
			light.omni_range = POOL_RANGE_M
			light.omni_attenuation = 1.4
			light.light_color = POOL_COLOR
			light.light_specular = 0.25
			light.shadow_enabled = false
			light.distance_fade_enabled = true
			light.distance_fade_begin = POOL_FADE_FROM_M
			light.distance_fade_length = POOL_FADE_M
			light.visible = false
			light.set_meta("group", lamp["group"])
			light.set_meta("energy", 0.0)
			add_child(light)
			pools.append(light)


## The kit's own lamps on a piece (the quay's masts) and the lamps the
## layout adds (the harvest hall's wall lamps, a walkway lamp a bridge span).
func _lamps_for_piece(campus: LandCampusBuilder, group_name: String, asset: String, xf: Transform3D) -> void:
	var pad_y := campus.pad_frame().origin.y
	match asset:
		"quay":
			for k in QUAY_MASTS_X.size():
				var x: float = QUAY_MASTS_X[k]
				_add(xf * Vector3(x, QUAY_MAST_LOCAL.y, QUAY_MAST_LOCAL.x), group_name, MAST, k == 0 or k == QUAY_MASTS_X.size() - 1)
		"harvest_hall":
			var mesh := LandCampusBuilder.kit_mesh(asset, "LOD0")
			if mesh == null:
				return
			var box := xf * mesh.get_aabb()
			var c := box.get_center()
			var long_x := box.size.x >= box.size.z
			# Two lamps a metre off each long wall.
			for side: float in [-1.0, 1.0]:
				for t: float in [-0.25, 0.25]:
					var p := Vector3(c.x + (box.size.x * t if long_x else side * (box.size.x * 0.5 + 1.0)), pad_y + WALL_LAMP_M,
						c.z + (side * (box.size.z * 0.5 + 1.0) if long_x else box.size.z * t))
					_add(p, group_name, WALL, t > 0.0)
		"pipe_bridge_span":
			_add(Vector3(xf.origin.x, pad_y + BRIDGE_LAMP_M, xf.origin.z), group_name, WALKWAY, false)


## Masts along a road (a rect in the pad's own frame, u and v), on its edge.
func _lamps_along_road(campus: LandCampusBuilder, lo: Vector2, hi: Vector2) -> void:
	var long_u := (hi.x - lo.x) >= (hi.y - lo.y)
	var length := (hi.x - lo.x) if long_u else (hi.y - lo.y)
	var n := maxi(1, int(floor(length / ROAD_SPACING_M)))
	for k in n:
		var t := (float(k) + 0.5) / float(n)
		var u := lerpf(lo.x, hi.x, t) if long_u else lo.x
		var v := lo.y if long_u else lerpf(lo.y, hi.y, t)
		_add(campus.world_xform(u, v, MAST_M, 0.0).origin, "roads", MAST, false)


func _add(at: Vector3, group_name: String, kind: Color, pool: bool) -> void:
	lamps.append({"at": at, "group": group_name, "kind": kind, "pool": pool})


## The four work-lights of every handed-over tank (its xform, its asset).
static func tank_lamp_positions(tanks: Array, whole: PackedFloat32Array) -> Array[Vector3]:
	var out: Array[Vector3] = []
	for i in tanks.size():
		if i >= whole.size() or whole[i] < 1.0:
			continue
		var big := str(tanks[i]["asset"]) == "tank_growout"
		var r := (LandCampusBuilder.GROWOUT_D if big else LandCampusBuilder.PREGROW_D) * 0.5 + TANK_RAIL_EXTRA_M
		var h := (LandCampusBuilder.GROWOUT_H if big else LandCampusBuilder.PREGROW_H) + TANK_LAMP_OVER_TOP_M
		var xf: Transform3D = tanks[i]["xform"]
		for k in 4:
			var a := TAU * (float(k) + 0.125) / 4.0
			out.append(xf * Vector3(cos(a) * r, h, -sin(a) * r))
	return out


## Show the lamps whose parts are built (`built`: group name -> true) and the
## work-lights of the tanks handed over.
func set_built(built: Dictionary, tanks: Array, progress: PackedFloat32Array) -> void:
	_built = built
	tank_lamps = tank_lamp_positions(tanks, progress)
	var xforms: Array[Transform3D] = []
	var colors: Array = []
	for lamp: Dictionary in lamps:
		if _is_built(str(lamp["group"])):
			xforms.append(Transform3D(Basis.IDENTITY, lamp["at"]))
			colors.append(lamp["kind"])
	for at in tank_lamps:
		xforms.append(Transform3D(Basis.IDENTITY, at))
		colors.append(TANK)
	MultiMeshPack.upload(glows.multimesh, xforms, colors)
	_apply()


func _is_built(group: String) -> bool:
	return _built.has(group)


## The campus's night level (0 by day .. 1): the glows and the pools.
func set_level(level: float) -> void:
	_level = level
	_apply()


## A first-sight sequence's wave: lamps within `radius` m of `origin` (world
## x/z) are on.
func set_wave(origin: Vector2, radius: float, width: float) -> void:
	_wave = Vector3(origin.x, origin.y, radius)
	var w := width
	for light in pools:
		var d := Vector2(light.position.x, light.position.z).distance_to(origin) if is_inside_tree() else 0.0
		light.set_meta("wave", 1.0 - smoothstep(radius - w, radius, d))
	_apply()


## The glows' visibility, and each pool's energy as named for the director
## (meta "energy": 0 off).
func _apply() -> void:
	if glows != null:
		glows.visible = _level > 0.0 and glows.multimesh.instance_count > 0
	for light in pools:
		var on := _level > 0.0 and _is_built(str(light.get_meta("group", "")))
		var wave := float(light.get_meta("wave", 1.0))
		light.set_meta("energy", POOL_ENERGY * _level * wave if on and wave > 0.0 else 0.0)


## Lamps drawn now (glows standing, lit).
func lit_lamps() -> int:
	return glows.multimesh.instance_count if glows != null and glows.visible else 0


## Lamps a first-sight sequence's wave has switched on (all of them outside a sequence).
func lamps_in_wave() -> int:
	var n := 0
	var origin := Vector2(_wave.x, _wave.y)
	for lamp: Dictionary in lamps:
		var at: Vector3 = lamp["at"]
		if _is_built(str(lamp["group"])) and Vector2(at.x, at.z).distance_to(origin) < _wave.z:
			n += 1
	for at in tank_lamps:
		if Vector2(at.x, at.z).distance_to(origin) < _wave.z:
			n += 1
	return n


## Pools of light on now (their named energy above 0).
func lit_pools() -> int:
	var n := 0
	for light in pools:
		if float(light.get_meta("energy", 0.0)) > 0.0:
			n += 1
	return n
