class_name MooringGrid
extends Node3D

## Frame mooring of a salmon site (roadmap A2-01 site kit): a submerged
## rope frame of square cells with one pen at the centre of each pen cell,
## a grid buoy floating over every frame coupling, bridles from the coupling
## plates up to the pens' mooring collars, anchor lines running out and down
## from the outer couplings, and synchronised yellow lights (#FFC23A) on the
## four outer corners.
##
## The buoy and light meshes come from blender/build_site_kit.py (LOD0-2);
## everything is drawn through MultiMeshes, so a site costs a handful of
## draw calls however many cells it has. Local space: the grid is centred on
## this node, y = 0 is the waterline.
##
## The buoys, the lights and the top of every chain and bridle ride the drawn
## water in their shaders (site_float.gdshaderinc, SiteFloat.gd); the frame
## and the anchor lines lie still, deep under the waves.

## Frame cell size. A 160 m pen (53.6 m across the outer pipe) sits in a
## 90 m cell, ~18 m between the collar and the frame lines: Scottish site
## plans hold 160 m pens in a 90-110 m grid (Mowi, Loch Seaforth: a 90 m
## matrix; Loch Ainort and Inner Sound: 100 m) and a Norwegian application
## (Strandmo) asks for 100 x 100 m cells (roadmap A2-01: 70-100 m).
const CELL_M := 90.0
## Depth of the frame lines and coupling plates below the surface.
const FRAME_DEPTH := 6.0
## Anchor lines: horizontal reach from the outer couplings and the depth
## they run down to (the line fades out in the water long before).
const ANCHOR_REACH := 140.0
const ANCHOR_DEPTH := 55.0
## Where a bridle meets the pen: the mooring collars on the outer pipe.
const COLLAR_ATTACH_R := 26.95
const COLLAR_ATTACH_Y := 0.10
## Bridles meet the collar at the mooring collars either side of the corner.
const BRIDLE_SPREAD_DEG := 15.0
## Rope radii (frame/anchor lines ~80 mm, bridles and chains thinner).
const ROPE_R := 0.045
const BRIDLE_R := 0.035
const CHAIN_R := 0.03
## The buoy rides with its chain eye this far under the water.
const BUOY_CHAIN_Y := -0.95

## Buoy colours: D13 buoy yellow (the player's sites, owner references 1,
## 2 and 5), granite grey, and the classic orange (rival farms).
const BUOY_YELLOW := Color(0.910, 0.725, 0.165)  # #E8B92A
const BUOY_GREY := Color(0.420, 0.451, 0.471)    # #6B7378
const BUOY_ORANGE := Color(0.878, 0.400, 0.165)  # #E0662A
## How much of the water's heave each end of a line takes (first end,
## second end): a chain hangs from its buoy, a bridle runs up to the pen.
const ROPE_HEAVE := {
	"chain": Vector2(1.0, 0.0),
	"bridle": Vector2(0.0, 1.0),
	"frame": Vector2(0.0, 0.0),
	"anchor": Vector2(0.0, 0.0),
}
## Floating gear moves up to this far off its rest place (m): it is drawn
## where the GPU puts it, so its culling box grows by this much.
const FLOAT_CULL_MARGIN := 4.0
## Height of the corner light's lens above the waterline (build_site_kit.py).
const LAMP_Y := 1.72

## Camera distance (m) where each LOD hands over to the next.
const LOD_SWITCH := [350.0, 1200.0]
## Ropes are underwater lines: drawn only while they can be seen.
const ROPE_VISIBLE_M := 700.0

const BUOY_SCENES := [
	"res://assets/models/site_kit/mooring_buoy_LOD0.glb",
	"res://assets/models/site_kit/mooring_buoy_LOD1.glb",
	"res://assets/models/site_kit/mooring_buoy_LOD2.glb",
]
const LIGHT_SCENES := [
	"res://assets/models/site_kit/mooring_light_LOD0.glb",
	"res://assets/models/site_kit/mooring_light_LOD1.glb",
	"res://assets/models/site_kit/mooring_light_LOD2.glb",
]
const ROPE_MATERIAL := preload("res://assets/models/site_kit/mooring_rope_material.tres")
const GLOW_MATERIAL := preload("res://assets/models/site_kit/mooring_light_glow_material.tres")

static var _mesh_cache: Dictionary = {}

var cells_x: int = 1
var cells_z: int = 1
var buoy_positions: Array[Vector3] = []
var light_positions: Array[Vector3] = []
## Each rope: [from, to, radius, kind] in local space; kind is "chain",
## "frame", "bridle" or "anchor".
var ropes: Array = []

var _buoys: Array[MultiMeshInstance3D] = []
var _lights: Array[MultiMeshInstance3D] = []
var _ropes: MultiMeshInstance3D = null
var _glows: MultiMeshInstance3D = null


## Lay out a grid of cells_x by cells_z cells; pen_cells lists the cells
## that hold a pen (a pen sits at its cell centre). anchor_floor(x, z) can
## return the seabed height under a local point (negative below water) so
## anchor lines stop at the bottom; return NAN or leave it unset to use
## ANCHOR_DEPTH.
func build(nx: int, nz: int, pen_cells: Array, buoy_color: Color = BUOY_YELLOW,
		anchor_floor: Callable = Callable()) -> void:
	cells_x = maxi(nx, 1)
	cells_z = maxi(nz, 1)
	_clear()
	buoy_positions.clear()
	light_positions.clear()
	ropes.clear()
	for i in cells_x + 1:
		for j in cells_z + 1:
			var node := node_position(i, j)
			buoy_positions.append(node)
			ropes.append([Vector3(node.x, BUOY_CHAIN_Y, node.z), Vector3(node.x, -FRAME_DEPTH, node.z), CHAIN_R, "chain"])
			if i < cells_x:
				ropes.append([_plate(i, j), _plate(i + 1, j), ROPE_R, "frame"])
			if j < cells_z:
				ropes.append([_plate(i, j), _plate(i, j + 1), ROPE_R, "frame"])
			for dir in _outward(i, j):
				ropes.append([_plate(i, j), _anchor_end(node, dir, anchor_floor), ROPE_R, "anchor"])
	for corner in [Vector2i(0, 0), Vector2i(cells_x, 0), Vector2i(0, cells_z), Vector2i(cells_x, cells_z)]:
		light_positions.append(node_position(corner.x, corner.y))
	for cell in pen_cells:
		_add_bridles(cell as Vector2i)
	_buoys = _spawn_lods(BUOY_SCENES, "Buoy", buoy_positions, buoy_color.srgb_to_linear())
	_lights = _spawn_lods(LIGHT_SCENES, "CornerLight", light_positions, Color.WHITE)
	_ropes = _spawn_ropes()
	_glows = _spawn_glows()

## Surface position of the frame coupling at grid node (i, j).
func node_position(i: int, j: int) -> Vector3:
	return Vector3((float(i) - cells_x * 0.5) * CELL_M, 0.0, (float(j) - cells_z * 0.5) * CELL_M)

## Centre of cell (i, j): where its pen floats.
func cell_center(i: int, j: int) -> Vector3:
	return Vector3((float(i) + 0.5 - cells_x * 0.5) * CELL_M, 0.0, (float(j) + 0.5 - cells_z * 0.5) * CELL_M)

func ropes_of(kind: String) -> Array:
	return ropes.filter(func(r: Array) -> bool: return r[3] == kind)

func get_buoy_instances() -> Array[MultiMeshInstance3D]:
	return _buoys

func get_light_instances() -> Array[MultiMeshInstance3D]:
	return _lights

func get_rope_instance() -> MultiMeshInstance3D:
	return _ropes

func get_glow_instance() -> MultiMeshInstance3D:
	return _glows

## Half the grid's extent across (x) and along (z), metres.
func half_extent() -> Vector2:
	return Vector2(cells_x, cells_z) * CELL_M * 0.5

## Camera-facing flash halos over the corner lamps (they keep a minimum
## on-screen size, so the synchronised flashes read at every zoom).
func _spawn_glows() -> MultiMeshInstance3D:
	var quad := QuadMesh.new()
	quad.size = Vector2.ONE
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.mesh = quad
	var xforms: Array[Transform3D] = []
	for k in light_positions.size():
		xforms.append(Transform3D(Basis.IDENTITY, light_positions[k] + Vector3(0.0, LAMP_Y, 0.0)))
	MultiMeshPack.upload(mm, xforms)
	var mmi := MultiMeshInstance3D.new()
	mmi.name = "LightGlow"
	mmi.multimesh = mm
	mmi.material_override = GLOW_MATERIAL
	mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	# The halo grows past the 1 m quad on screen; keep it from being culled.
	mmi.extra_cull_margin = 40.0 + FLOAT_CULL_MARGIN
	add_child(mmi)
	return mmi

func _plate(i: int, j: int) -> Vector3:
	var p := node_position(i, j)
	return Vector3(p.x, -FRAME_DEPTH, p.z)

## Outward directions of an outer coupling (two at a corner, none inside).
func _outward(i: int, j: int) -> Array[Vector3]:
	var dirs: Array[Vector3] = []
	if i == 0:
		dirs.append(Vector3.LEFT)
	if i == cells_x:
		dirs.append(Vector3.RIGHT)
	if j == 0:
		dirs.append(Vector3.FORWARD)
	if j == cells_z:
		dirs.append(Vector3.BACK)
	return dirs

func _anchor_end(node: Vector3, dir: Vector3, anchor_floor: Callable) -> Vector3:
	var end := node + dir * ANCHOR_REACH
	var depth := -ANCHOR_DEPTH
	if anchor_floor.is_valid():
		# The callable looks the seabed up in world space.
		var world := to_global(end) if is_inside_tree() else end
		var floor_y = anchor_floor.call(world.x, world.z)
		if floor_y is float and not is_nan(floor_y):
			depth = clampf(floor_y, -ANCHOR_DEPTH * 3.0, -FRAME_DEPTH)
	return Vector3(end.x, depth, end.z)

## Two bridles from each corner plate of a pen cell to the pen's mooring
## collars either side of the corner direction.
func _add_bridles(cell: Vector2i) -> void:
	var center := cell_center(cell.x, cell.y)
	for corner in [Vector2i(0, 0), Vector2i(1, 0), Vector2i(0, 1), Vector2i(1, 1)]:
		var plate := _plate(cell.x + corner.x, cell.y + corner.y)
		var to_corner := Vector2(plate.x - center.x, plate.z - center.z).angle()
		for side in [-1.0, 1.0]:
			var a: float = to_corner + deg_to_rad(BRIDLE_SPREAD_DEG) * side
			var attach := center + Vector3(cos(a) * COLLAR_ATTACH_R, COLLAR_ATTACH_Y, sin(a) * COLLAR_ATTACH_R)
			ropes.append([plate, attach, BRIDLE_R, "bridle"])

func _clear() -> void:
	for child in get_children():
		child.queue_free()
	_buoys.clear()
	_lights.clear()
	_ropes = null
	_glows = null

static func _mesh_from(path: String, node_name: String) -> Mesh:
	if _mesh_cache.has(path):
		return _mesh_cache[path]
	var mesh: Mesh = null
	var packed := load(path) as PackedScene
	if packed != null:
		var inst := packed.instantiate()
		var mi := inst.find_child(node_name, true, false) as MeshInstance3D
		if mi != null:
			mesh = mi.mesh
		inst.free()
	_mesh_cache[path] = mesh
	return mesh

func _spawn_lods(paths: Array, node_name: String, points: Array[Vector3], tint: Color) -> Array[MultiMeshInstance3D]:
	var out: Array[MultiMeshInstance3D] = []
	for level in paths.size():
		var mesh := _mesh_from(paths[level], node_name)
		if mesh == null:
			push_warning("[MooringGrid] missing %s mesh in %s" % [node_name, paths[level]])
			continue
		var mm := MultiMesh.new()
		mm.transform_format = MultiMesh.TRANSFORM_3D
		mm.use_colors = true
		mm.mesh = mesh
		var xforms: Array[Transform3D] = []
		var tints: Array = []
		for k in points.size():
			# A small fixed yaw per buoy so the lifting eyes do not all line up.
			var yaw := float(k * 37 % 180) * PI / 180.0
			xforms.append(Transform3D(Basis(Vector3.UP, yaw), points[k]))
			tints.append(tint)
		MultiMeshPack.upload(mm, xforms, tints)
		var mmi := MultiMeshInstance3D.new()
		mmi.name = "%s_LOD%d" % [node_name, level]
		mmi.multimesh = mm
		mmi.visibility_range_begin = 0.0 if level == 0 else LOD_SWITCH[level - 1]
		mmi.visibility_range_end = LOD_SWITCH[level] if level < LOD_SWITCH.size() else 0.0
		mmi.extra_cull_margin = FLOAT_CULL_MARGIN
		if level > 0:
			mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		add_child(mmi)
		out.append(mmi)
	return out

func _spawn_ropes() -> MultiMeshInstance3D:
	var cyl := CylinderMesh.new()
	cyl.top_radius = 1.0
	cyl.bottom_radius = 1.0
	cyl.height = 1.0
	cyl.radial_segments = 6
	cyl.rings = 1
	cyl.cap_top = false
	cyl.cap_bottom = false
	cyl.material = ROPE_MATERIAL
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_custom_data = true
	mm.mesh = cyl
	var xforms: Array[Transform3D] = []
	var heaves: Array = []
	for k in ropes.size():
		var heave: Vector2 = ROPE_HEAVE.get(ropes[k][3], Vector2.ZERO)
		heaves.append(Color(heave.x, heave.y, 0.0, 0.0))
		var a: Vector3 = ropes[k][0]
		var b: Vector3 = ropes[k][1]
		var r: float = ropes[k][2]
		var axis := b - a
		var length := axis.length()
		var y := axis / maxf(length, 0.001)
		var x := y.cross(Vector3.UP if absf(y.y) < 0.99 else Vector3.RIGHT).normalized()
		var z := x.cross(y)
		var basis := Basis(x * r, y * length, z * r)
		xforms.append(Transform3D(basis, (a + b) * 0.5))
	MultiMeshPack.upload(mm, xforms, [], heaves)
	var mmi := MultiMeshInstance3D.new()
	mmi.name = "Ropes"
	mmi.multimesh = mm
	mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	mmi.extra_cull_margin = FLOAT_CULL_MARGIN
	mmi.visibility_range_end = ROPE_VISIBLE_M
	add_child(mmi)
	return mmi
