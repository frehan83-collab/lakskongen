class_name FishSchool
extends RefCounted
## A pen's salmon school on the GPU (roadmap G3-01). FishPenManager owns one
## per pen, as a child of the pen (it rides the water with the collar).
##
## Two MultiMeshes carry the same fish. Every fish's own numbers (its angle
## round the pen, where it swims across the school's ring, its depth rank and
## a seed) are written into the MultiMesh custom data once, when the school
## is made; the instance transforms are the identity and are never written
## again. The swim, the turn of the whole school round the pen, jumps, rolls
## and splashes run in the vertex shaders (shaders/fish_school.gdshaderinc)
## from three per-pen instance uniforms this script sets: the clocks and the
## state (depth, speed, feeding) every frame, the body (size, count) when the
## sim changes it.
##
##   the school node  salmon_school.gdshader: the solid Blender salmon. The
##                    opaque water hides it from above, so from above it
##                    draws only the jumpers (in the air); under the water,
##                    the whole school.
##   "Shoal" child    fish_shoal.gdshader: the school seen through the
##                    surface, drawn after the water as a decal on it: the
##                    fish as dark shapes that fade with depth, splash rings,
##                    and the pen's water column (the school as a whole, the
##                    net and the lice skirt) as light added to the water.
##
## The sim drives the uniforms: fish shown 1,500-4,000 by the pen's biomass
## (none in an empty pen), fish length from the mean weight, feeding lifts
## the school towards the surface (a lower mean depth), crowds it round the
## spreader and quickens it, low oxygen slows it.

## Blender-built Atlantic salmon (blender/build_salmon.py, 0.9 m fork length,
## nose along -Z): LOD0 (~1.5k triangles) close up, LOD1 (~300) for the solid
## school, LOD2 (~40) for the school seen through the surface.
const SALMON_LOD_SCENES := [
	"res://assets/models/fish/salmon_LOD0.glb",
	"res://assets/models/fish/salmon_LOD1.glb",
	"res://assets/models/fish/salmon_LOD2.glb",
]
const SCHOOL_MATERIAL := preload("res://assets/models/fish/salmon_school_material.tres")
const SHOAL_MATERIAL := preload("res://assets/models/fish/fish_shoal_material.tres")
## Camera distance (m) where the solid school swaps LOD0 for LOD1, and where
## the shoal swaps LOD1 fish for LOD2.
const LOD_SWITCH := 20.0
const SHOAL_LOD_SWITCH := 90.0
## Fish are drawn within this camera distance (m); the pen's water column
## (a stocked pen's colour) within COLUMN_RANGE_M.
const FISH_RANGE_M := 400.0
const COLUMN_RANGE_M := 2200.0
const RANGE_HYSTERESIS_M := 10.0
## Kept for callers of the G2-04 school: the old far limit.
const VISIBLE_M := FISH_RANGE_M
## Fish shown per pen: MIN_FISH .. MAX_FISH by the pen's biomass (FULL_PEN_KG
## and more shows them all); an empty pen shows none.
const MIN_FISH := 1500
const MAX_FISH := 4000
## The shown count moves in steps of 1 % of that range (25 fish): a pen's
## daily growth no longer changes it every day (wave 3 ui-a).
const FISH_STEP := 25
const FULL_PEN_KG := 900000.0
## The first JUMPERS fish of a school leap now and then
## (fish_school.gdshaderinc jumper_count).
const JUMPERS := 6
const MESH_LENGTH_M := 0.9
## The school's ring (m from the pen centre, inside the 24.95 m net).
const SCHOOL_OUTER_M := 22.5
## Cruising speed (m/s) and tail beat (Hz); feeding quickens both.
const CRUISE_SPEED_MS := 0.7
const TAIL_HZ := 1.6
const FEED_SPEED := 1.6
## Depth (m, the band's middle) and half-band of a calm and a fed school.
const DEPTH_CALM_M := 5.0
const SPREAD_CALM_M := 3.2
const DEPTH_FED_M := 2.2
const SPREAD_FED_M := 1.7
## Seconds the school takes to rise to a feed, and to sink after it.
const RISE_S := 4.0
const SINK_S := 9.0
const SPEED_EASE_S := 3.0
## Oxygen (mg/L): at LOW_DO_MGL and below the school swims at LOW_DO_SPEED;
## at GOOD_DO_MGL and above at its full speed.
const LOW_DO_MGL := 4.0
const GOOD_DO_MGL := 7.0
const LOW_DO_SPEED := 0.35
## Jumps, rolls and splashes come this much more often while feeding.
const EVENTS_FED := 4.0
## The school's optical thickness seen from above: a stocked pen reads at
## THICKNESS_MIN, a full one at THICKNESS_MAX.
const THICKNESS_MIN := 3.0
const THICKNESS_MAX := 8.0
## How much the pen water's scatter rises in a stocked pen (fish_shoal
## pen_water_scatter): STOCK_MIN for any stock, 1 for a full pen.
const STOCK_MIN := 0.55
## Culling box of a school (the shoal's decal can reach past the net
## towards the camera).
const SCHOOL_BOX := AABB(Vector3(-27.0, -32.0, -27.0), Vector3(54.0, 36.0, 54.0))
const SHOAL_BOX := AABB(Vector3(-38.0, -32.0, -38.0), Vector3(76.0, 36.0, 76.0))
## Past this distance a school's uniforms are refreshed every FAR_FRAMES-th
## frame (only its water column shows there).
const FAR_FRAMES := 4
## The sim's day moves a pen's fish a little; the GPU hears of it once the
## change can show: 5 mm of fork length (about 1 % of a grown salmon, under
## a pixel beyond ~7 m at 1080p), 1 % of a full pen's water-column tint, a
## fish more or less, or the skirt (wave 3 ui-a: at 30x every day rewrote
## every pen's uniforms).
const BODY_LENGTH_STEP_M := 0.005
const BODY_STOCK_STEP := 0.01
## Writes of a school's body uniforms since launch (tests count them).
static var body_writes := 0

static var _salmon_meshes: Dictionary = {}  # lod -> Mesh
static var _shoal_meshes: Dictionary = {}  # lod -> ArrayMesh


static func salmon_mesh(lod: int = 1) -> Mesh:
	lod = clampi(lod, 0, SALMON_LOD_SCENES.size() - 1)
	if _salmon_meshes.has(lod):
		return _salmon_meshes[lod]
	var mesh: Mesh = null
	var packed: PackedScene = load(SALMON_LOD_SCENES[lod]) as PackedScene
	if packed != null:
		var inst: Node = packed.instantiate()
		var mi := inst.find_child("Salmon", true, false) as MeshInstance3D
		if mi and mi.mesh:
			mesh = mi.mesh
		inst.free()
	if mesh == null:
		var body := BoxMesh.new()
		body.size = Vector3(0.12, 0.22, MESH_LENGTH_M)
		var mat := StandardMaterial3D.new()
		mat.albedo_color = Color(0.5, 0.58, 0.65, 1.0)
		mat.roughness = 0.6
		body.material = mat
		mesh = body
	_salmon_meshes[lod] = mesh
	return mesh


## The shoal's mesh: the water column quad (UV2.x = 2), the salmon of `lod`
## (UV2.x = 0) and a splash quad (UV2.x = 1), in that draw order.
static func shoal_mesh(lod: int = 2) -> ArrayMesh:
	if _shoal_meshes.has(lod):
		return _shoal_meshes[lod]
	var fish := salmon_mesh(lod).surface_get_arrays(0)
	var verts := PackedVector3Array()
	var normals := PackedVector3Array()
	var uvs := PackedVector2Array()
	var uv2s := PackedVector2Array()
	var indices := PackedInt32Array()
	_add_quad(verts, normals, uvs, uv2s, indices, 2.0)
	var base := verts.size()
	var fv: PackedVector3Array = fish[Mesh.ARRAY_VERTEX]
	var fn: PackedVector3Array = fish[Mesh.ARRAY_NORMAL]
	var fu: PackedVector2Array = fish[Mesh.ARRAY_TEX_UV]
	for i in fv.size():
		verts.append(fv[i])
		normals.append(fn[i] if i < fn.size() else Vector3.UP)
		uvs.append(fu[i] if i < fu.size() else Vector2.ZERO)
		uv2s.append(Vector2.ZERO)
	var fi = fish[Mesh.ARRAY_INDEX]
	if fi is PackedInt32Array and not (fi as PackedInt32Array).is_empty():
		for idx in fi:
			indices.append(base + idx)
	else:
		for i in fv.size():
			indices.append(base + i)
	_add_quad(verts, normals, uvs, uv2s, indices, 1.0)
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = verts
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_TEX_UV2] = uv2s
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	mesh.surface_set_material(0, SHOAL_MATERIAL)
	_shoal_meshes[lod] = mesh
	return mesh


static func _add_quad(verts: PackedVector3Array, normals: PackedVector3Array, uvs: PackedVector2Array,
		uv2s: PackedVector2Array, indices: PackedInt32Array, part: float) -> void:
	var base := verts.size()
	for corner in [Vector2(0, 0), Vector2(1, 0), Vector2(1, 1), Vector2(0, 1)]:
		verts.append(Vector3(corner.x - 0.5, 0.0, corner.y - 0.5))
		normals.append(Vector3.UP)
		uvs.append(corner)
		uv2s.append(Vector2(part, 0.0))
	indices.append_array(PackedInt32Array([base, base + 1, base + 2, base, base + 2, base + 3]))


## The per-fish data (identity transforms, custom data), the same for both
## of a school's MultiMeshes: angle round the pen (turns), across the ring,
## depth rank (low-discrepancy sequences, so any first N fish fill the pen
## evenly) and a seed. The jumpers swim at the top of the school.
static func fish_buffer(key: String) -> PackedFloat32Array:
	var rng := RandomNumberGenerator.new()
	rng.seed = hash(key)
	var offsets := Vector3(rng.randf(), rng.randf(), rng.randf())
	var buffer := PackedFloat32Array()
	buffer.resize(MAX_FISH * 16)
	for i in MAX_FISH:
		var o := i * 16
		buffer[o] = 1.0
		buffer[o + 5] = 1.0
		buffer[o + 10] = 1.0
		buffer[o + 12] = fposmod(offsets.x + float(i) * 0.6180339887, 1.0)
		buffer[o + 13] = fposmod(offsets.y + float(i) * 0.7548776662, 1.0)
		buffer[o + 14] = 0.02 * float(i) if i < JUMPERS else fposmod(offsets.z + float(i) * 0.5698402910, 1.0)
		buffer[o + 15] = rng.randf()
	return buffer


static func _multimesh(mesh: Mesh, buffer: PackedFloat32Array) -> MultiMesh:
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_custom_data = true
	mm.mesh = mesh
	mm.instance_count = MAX_FISH
	mm.buffer = buffer
	mm.visible_instance_count = 0
	return mm


## A new school (no fish shown yet). `key` seeds its fish (a pen id).
static func create(school_name: String, key: String = "") -> MultiMeshInstance3D:
	var buffer := fish_buffer(key if key != "" else school_name)
	var school := MultiMeshInstance3D.new()
	school.name = school_name
	school.multimesh = _multimesh(salmon_mesh(1), buffer)
	school.material_override = SCHOOL_MATERIAL
	school.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	school.custom_aabb = SCHOOL_BOX
	school.visibility_range_end = FISH_RANGE_M
	school.visibility_range_end_margin = RANGE_HYSTERESIS_M
	var shoal := MultiMeshInstance3D.new()
	shoal.name = "Shoal"
	shoal.multimesh = _multimesh(shoal_mesh(2), buffer)
	shoal.material_override = SHOAL_MATERIAL
	shoal.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	shoal.custom_aabb = SHOAL_BOX
	shoal.visibility_range_end = COLUMN_RANGE_M
	school.add_child(shoal)
	var dir := 1.0 if absi(hash(key + "/dir")) % 2 == 0 else -1.0
	school.set_meta("school", {
		"count": 0, "length_m": 0.2, "thickness": 0.0, "stock": 0.0, "skirt": false, "dir": dir,
		"feeding": false, "speed_target": 1.0,
		"feed": 0.0, "speed": 1.0, "angle": 0.0, "tail": 0.0, "events": 0.0,
		"near": false, "frame": absi(hash(key)) % FAR_FRAMES,
	})
	_write_body(school)
	_write_motion(school)
	return school


## The school's shoal (the through-the-surface pass).
static func shoal_of(school: MultiMeshInstance3D) -> MultiMeshInstance3D:
	return school.get_node_or_null("Shoal") as MultiMeshInstance3D if school != null else null


## Fish the school shows (0 for an empty pen).
static func shown_fish(school: MultiMeshInstance3D) -> int:
	return int(_state(school).get("count", 0))


static func _state(school: MultiMeshInstance3D) -> Dictionary:
	if school == null or not school.has_meta("school"):
		return {}
	return school.get_meta("school")


## How many fish a pen of `fish_count` fish and `biomass_kg` shows.
static func fish_for(fish_count: int, biomass_kg: float) -> int:
	if fish_count <= 0:
		return 0
	var extra := float(MAX_FISH - MIN_FISH) * clampf(biomass_kg / FULL_PEN_KG, 0.0, 1.0)
	return MIN_FISH + int(round(extra / FISH_STEP)) * FISH_STEP


## Fork length (m) of a fish of `mean_weight_kg` (L(cm) = (100 W_g / 1.1)^(1/3)).
static func length_for(mean_weight_kg: float) -> float:
	if mean_weight_kg <= 0.0:
		return 0.2
	return clampf(pow(100.0 * mean_weight_kg * 1000.0 / 1.1, 1.0 / 3.0) / 100.0, 0.15, 1.0)


## Speed factor at `oxygen_mgl` (NaN or missing: full speed).
static func speed_for_oxygen(oxygen_mgl: float) -> float:
	if is_nan(oxygen_mgl):
		return 1.0
	return lerpf(LOW_DO_SPEED, 1.0, clampf((oxygen_mgl - LOW_DO_MGL) / (GOOD_DO_MGL - LOW_DO_MGL), 0.0, 1.0))


## What a pen's school needs from the sim (FishPenManager.pen_payload and
## its site): the pen's fish, weight and so its biomass, the site's oxygen
## and lice skirt.
static func payload(site: Dictionary, pen: Dictionary) -> Dictionary:
	var count := _num(pen, "fish_count", 0.0)
	var weight := _num(pen, "mean_weight_kg", _num(site, "mean_weight_kg", 0.0))
	return {
		"fish_count": count,
		"mean_weight_kg": weight,
		"biomass_kg": count * weight,
		"oxygen_mgl": _num(pen, "oxygen_mgl", _num(site, "oxygen_mgl", NAN)),
		"has_skirt": bool(site.get("has_skirt", false)),
	}


## The pen as the sim reports it: fish_count, mean_weight_kg, oxygen_mgl and
## the site's has_skirt.
static func apply_pen(school: MultiMeshInstance3D, pen: Dictionary) -> void:
	var st := _state(school)
	if st.is_empty():
		return
	var count := int(_num(pen, "fish_count", 0.0))
	var weight := _num(pen, "mean_weight_kg", 0.0)
	var biomass := _num(pen, "biomass_kg", float(count) * weight)
	st["count"] = fish_for(count, biomass)
	st["length_m"] = length_for(weight)
	var full := clampf(biomass / FULL_PEN_KG, 0.0, 1.0)
	st["thickness"] = lerpf(THICKNESS_MIN, THICKNESS_MAX, full) if count > 0 else 0.0
	st["stock"] = lerpf(STOCK_MIN, 1.0, full) if count > 0 else 0.0
	st["speed_target"] = speed_for_oxygen(_num(pen, "oxygen_mgl", NAN))
	st["skirt"] = bool(pen.get("has_skirt", false))
	if body_changed(st):
		_write_body(school)


## Whether the school's shape differs visibly from what its shaders have
## (BODY_LENGTH_STEP_M, BODY_STOCK_STEP; any change of fish or skirt).
static func body_changed(st: Dictionary) -> bool:
	var w: Array = st.get("written", [])
	if w.size() != 4:
		return true
	return int(w[0]) != int(st["count"]) or bool(w[3]) != bool(st["skirt"]) \
		or absf(float(w[1]) - float(st["length_m"])) >= BODY_LENGTH_STEP_M \
		or absf(float(w[2]) - float(st["stock"])) >= BODY_STOCK_STEP


## Kept for the G2-04 callers: fish_count fish of mean_weight_kg.
static func apply_density(school: MultiMeshInstance3D, fish_count: int, mean_weight_kg: float) -> void:
	apply_pen(school, {"fish_count": fish_count, "mean_weight_kg": mean_weight_kg})


## Whether the barge is feeding this pen now (FishPenManager decides).
static func set_feeding(school: MultiMeshInstance3D, on: bool) -> void:
	var st := _state(school)
	if not st.is_empty():
		st["feeding"] = on


static func _num(d: Dictionary, key: String, fallback: float) -> float:
	var v = d.get(key)
	return float(v) if (v is float or v is int) else fallback


## Close schools get the detailed salmon, far ones the light mesh (only the
## mesh changes, the per-fish data stays).
static func update_lod(school: MultiMeshInstance3D, cam: Camera3D) -> void:
	if cam == null or school == null or school.multimesh == null:
		return
	var dist := cam.global_position.distance_to(school.global_position)
	var mesh := salmon_mesh(0 if dist < LOD_SWITCH else 1)
	if school.multimesh.mesh != mesh:
		school.multimesh.mesh = mesh
	var shoal := shoal_of(school)
	if shoal != null:
		var shoal_mesh_now := shoal_mesh(1 if dist < SHOAL_LOD_SWITCH else 2)
		if shoal.multimesh.mesh != shoal_mesh_now:
			shoal.multimesh.mesh = shoal_mesh_now


## One frame of the school: ease its state towards the sim's, run its
## clocks, show as many fish as the camera can see and hand the shaders the
## pen's uniforms. No per-fish data is written.
static func animate(school: MultiMeshInstance3D, delta: float, cam: Camera3D) -> void:
	var st := _state(school)
	if st.is_empty() or not school.is_inside_tree():
		return
	_ease(st, delta)
	if not school.is_visible_in_tree():
		return
	var dist := cam.global_position.distance_to(school.global_position) if cam != null else 0.0
	var near: bool = dist < FISH_RANGE_M + (RANGE_HYSTERESIS_M if st["near"] else -RANGE_HYSTERESIS_M)
	st["near"] = near
	var underwater := cam != null and cam.global_position.y < 0.0
	var count: int = st["count"]
	_set_visible(school.multimesh, (count if underwater else mini(JUMPERS, count)) if near else 0)
	var shoal := shoal_of(school)
	if shoal != null:
		_set_visible(shoal.multimesh, maxi(count, 1) if near else 1)
	if dist > COLUMN_RANGE_M:
		return
	st["frame"] = int(st["frame"]) + 1
	if not near and int(st["frame"]) % FAR_FRAMES != 0:
		return
	update_lod(school, cam)
	_write_motion(school)


static func _set_visible(mm: MultiMesh, n: int) -> void:
	if mm != null and mm.visible_instance_count != n:
		mm.visible_instance_count = n


## The state follows the sim and the barge: the school rises to a feed in
## RISE_S and sinks after it in SINK_S; its speed eases in SPEED_EASE_S;
## the clocks advance at the current pace.
static func _ease(st: Dictionary, delta: float) -> void:
	var feed_target := 1.0 if bool(st["feeding"]) and int(st["count"]) > 0 else 0.0
	var feed: float = st["feed"]
	var ease_s := RISE_S if feed_target > feed else SINK_S
	feed = move_toward(feed, feed_target, delta / ease_s)
	st["feed"] = feed
	var speed_target: float = float(st["speed_target"]) * lerpf(1.0, FEED_SPEED, feed)
	var speed: float = st["speed"]
	speed += (speed_target - speed) * (1.0 - exp(-delta / SPEED_EASE_S))
	st["speed"] = speed
	st["angle"] = float(st["angle"]) + delta * CRUISE_SPEED_MS * speed / SCHOOL_OUTER_M
	st["tail"] = float(st["tail"]) + delta * TAU * TAIL_HZ * (0.6 + 0.4 * speed)
	st["events"] = float(st["events"]) + delta * lerpf(1.0, EVENTS_FED, feed)


## The mean depth (m) and half-band of the school now.
static func depth_now(school: MultiMeshInstance3D) -> Vector2:
	var feed := float(_state(school).get("feed", 0.0))
	return Vector2(lerpf(DEPTH_CALM_M, DEPTH_FED_M, feed), lerpf(SPREAD_CALM_M, SPREAD_FED_M, feed))


static func _write_motion(school: MultiMeshInstance3D) -> void:
	var st := _state(school)
	var depth := depth_now(school)
	var clock := Vector4(st["angle"], st["tail"], st["events"], st["thickness"])
	var state := Vector4(depth.x, depth.y, st["speed"], st["feed"])
	for node in [school, shoal_of(school)]:
		if node != null:
			node.set_instance_shader_parameter("school_clock", clock)
			node.set_instance_shader_parameter("school_state", state)


static func _write_body(school: MultiMeshInstance3D) -> void:
	var st := _state(school)
	st["written"] = [st["count"], st["length_m"], st["stock"], st["skirt"]]
	body_writes += 1
	var body := Vector4(st["length_m"], st["dir"], SCHOOL_OUTER_M, st["count"])
	school.set_instance_shader_parameter("school_body", body)
	var shoal := shoal_of(school)
	if shoal != null:
		shoal.set_instance_shader_parameter("school_body", body)
		shoal.set_instance_shader_parameter("school_pen", Vector4(1.0 if st["skirt"] else 0.0, st["stock"], 0.0, 0.0))
	_write_motion(school)


## The water's optics as water_shader.gdshader composes them (absorption and
## deep scatter, warmed and bloomed by the director's uniforms) and the
## distance fog, into the shoal material (once a frame, shared by every
## school).
static func sync(water: ShaderMaterial, fog_density: float) -> void:
	var optics := water_optics(water)
	SHOAL_MATERIAL.set_shader_parameter("absorption", optics[0])
	SHOAL_MATERIAL.set_shader_parameter("deep_scatter", optics[1])
	SHOAL_MATERIAL.set_shader_parameter("fog_density", fog_density)


## [absorption, deep scatter] of the water material now (the shader's
## defaults without one).
static func water_optics(water: ShaderMaterial) -> Array:
	var sigma := Vector3(0.40, 0.09, 0.14)
	var scatter := Vector3(0.0022, 0.0085, 0.0105)
	var algae := 0.0
	var turbidity := 0.0
	var warm := 0.2
	if water != null:
		sigma = _vec3(water.get_shader_parameter("absorption"), sigma)
		scatter = _vec3(water.get_shader_parameter("deep_scatter"), scatter)
		algae = _float(water.get_shader_parameter("algae_density"), algae)
		turbidity = _float(water.get_shader_parameter("turbidity"), turbidity)
		warm = _float(water.get_shader_parameter("temperature_factor"), warm)
	scatter = scatter.lerp(scatter * Vector3(0.9, 1.15, 0.85), warm * 0.5)
	sigma += Vector3(0.25, 0.04, 0.45) * algae + Vector3(0.15, 0.2, 0.3) * turbidity
	scatter = scatter.lerp(Vector3(0.006, 0.024, 0.010), algae * 0.85)
	scatter = scatter.lerp(Vector3(0.016, 0.017, 0.011), turbidity * 0.7)
	return [sigma, scatter]


static func _vec3(v, fallback: Vector3) -> Vector3:
	if v is Vector3:
		return v
	if v is Color:
		return Vector3(v.r, v.g, v.b)
	return fallback


static func _float(v, fallback: float) -> float:
	return float(v) if (v is float or v is int) else fallback
