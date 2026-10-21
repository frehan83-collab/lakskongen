class_name WaterGrid
extends Node3D
## The fjord's water surface (G1-05): a camera-snapped LOD grid out to the
## horizon, drawn with water_shader.gdshader.
##
## Level 0 is a 2 m grid 512 m across (256 quads), centred on the camera;
## every further level is a square ring twice as wide with twice the
## spacing (the same 256 quads across, minus the hole the finer level
## fills): 5 levels reach ±4.1 km at 32 m. Beyond that a flat skirt runs to
## the horizon (±32.8 km): out there a wave is far below a pixel, and a
## displaced grid would only shade sub-pixel triangles near the horizon.
## Each level is four meshes (quadrants at level 0, a pinwheel of strips in
## the rings and the skirt) so the half behind the camera is frustum-culled.
## The grid follows the camera in whole 2 m steps, so the finest lattice
## never swims. The vertex shader stitches each level's outer edge to the
## coarser ring (odd vertices take the average of their neighbours'
## displacement) and lays the last level's rim flat, so no cracks open.
##
## The material is EnvironmentDirector's to drive (waves, optics, bake);
## this node only owns the geometry and its placement.

## The water material (shared by every grid piece).
@export var material: ShaderMaterial
## The surface seen from below (G3-04, water/water_underside.gdshader): the
## pieces wear it while the camera is under the waves (set_underside).
@export var underside_material: ShaderMaterial
## Vertex spacing of the finest level (metres).
@export var base_spacing: float = 2.0
## Quads across each level (level 0 spans base_spacing * grid_quads).
@export var grid_quads: int = 256
## Displaced levels, level 0 included (5 levels reach 256 m * 2^4 = 4.1 km).
@export var levels: int = 5
## Half-extent of the flat skirt beyond the levels (m): the horizon.
@export var ocean_extent: float = 32768.0
## The camera the grid follows; the viewport's current camera when unset.
@export var follow_camera: Camera3D

## Vertical room for wave crests and troughs in each piece's cull box (m).
const CULL_HEIGHT_M := 12.0
## The water draws on render layer 20 only. The fjord's ReflectionProbe
## leaves that layer out of its reflection_mask: the water mirrors the sky's
## radiance, which follows the sky every frame, instead of a probe captured
## from one point (no box projection) whose box edge printed a seam across
## the open sea and whose capture went stale between day phases. The probe
## still renders the water into its capture and lights the solid geometry.
const RENDER_LAYER := 1 << 19

var _pieces: Array[MeshInstance3D] = []
var _underside := false


func _ready() -> void:
	_build()
	_follow()


## Draw the surface as seen from below (G3-04): the pieces wear
## underside_material instead of the water material (the director, while
## the camera is under the waves). Without an underside material: no change.
func set_underside(on: bool) -> void:
	var wanted := on and underside_material != null
	if wanted == _underside:
		return
	_underside = wanted
	for piece in _pieces:
		piece.material_override = underside_material if wanted else material


func is_underside() -> bool:
	return _underside


func _process(_delta: float) -> void:
	_follow()


## Half the width of a level in metres (level 0: 256 m).
func level_half_extent(level: int) -> float:
	return 0.5 * grid_quads * base_spacing * pow(2.0, level)


## Vertex spacing of a level in metres.
func level_spacing(level: int) -> float:
	return base_spacing * pow(2.0, level)


## Vertex spacing (m) of the grid at a world position: the level whose
## square (centred on the grid origin) contains it (INF on the flat skirt).
func spacing_at(world_pos: Vector3) -> float:
	var d := maxf(absf(world_pos.x - global_position.x), absf(world_pos.z - global_position.z))
	for level in levels:
		if d <= level_half_extent(level) + 1e-3:
			return level_spacing(level)
	return INF


## Half-extent (m) of the displaced levels; the skirt beyond is flat.
func displaced_extent() -> float:
	return level_half_extent(levels - 1)


## Every grid piece (tests read their meshes and transforms).
func get_pieces() -> Array[MeshInstance3D]:
	return _pieces


func _follow() -> void:
	var cam := follow_camera if follow_camera != null else (get_viewport().get_camera_3d() if is_inside_tree() else null)
	if cam == null:
		return
	var p := cam.global_position
	var step := base_spacing
	global_position = Vector3(floorf(p.x / step + 0.5) * step, global_position.y, floorf(p.z / step + 0.5) * step)


func _build() -> void:
	for piece in _pieces:
		piece.queue_free()
	_pieces.clear()
	var half := grid_quads / 2
	var quarter := grid_quads / 4
	# Level 0: a quadrant (half x half quads, unit spacing) turned four ways.
	var quadrant := PlaneMesh.new()
	quadrant.size = Vector2(half, half)
	quadrant.subdivide_width = half - 1
	quadrant.subdivide_depth = half - 1
	quadrant.center_offset = Vector3(half * 0.5, 0.0, half * 0.5)
	quadrant.custom_aabb = AABB(Vector3(0.0, -CULL_HEIGHT_M, 0.0), Vector3(half, 2.0 * CULL_HEIGHT_M, half))
	# Rings: a pinwheel strip covering x in [-half, quarter], z in [-half, -quarter]
	# (unit spacing); four turns tile the square ring between +-quarter and +-half.
	var strip := PlaneMesh.new()
	strip.size = Vector2(half + quarter, quarter)
	strip.subdivide_width = half + quarter - 1
	strip.subdivide_depth = quarter - 1
	strip.center_offset = Vector3(-half + (half + quarter) * 0.5, 0.0, -half + quarter * 0.5)
	strip.custom_aabb = AABB(Vector3(-half, -CULL_HEIGHT_M, -half), Vector3(half + quarter, 2.0 * CULL_HEIGHT_M, quarter))
	for level in levels:
		var spacing := level_spacing(level)
		for turn in 4:
			_add_piece("L%d_%d" % [level, turn], quadrant if level == 0 else strip, turn, spacing)
	# The skirt: the same pinwheel in metres, from the last level to the horizon.
	var inner := displaced_extent()
	var skirt := PlaneMesh.new()
	skirt.size = Vector2(ocean_extent + inner, ocean_extent - inner)
	skirt.center_offset = Vector3(-ocean_extent + (ocean_extent + inner) * 0.5, 0.0, -ocean_extent + (ocean_extent - inner) * 0.5)
	skirt.custom_aabb = AABB(Vector3(-ocean_extent, -CULL_HEIGHT_M, -ocean_extent), Vector3(ocean_extent + inner, 2.0 * CULL_HEIGHT_M, ocean_extent - inner))
	for turn in 4:
		_add_piece("Skirt_%d" % turn, skirt, turn, 1.0)
	for mat in [material, underside_material]:
		if mat != null:
			(mat as ShaderMaterial).set_shader_parameter("grid_half_units", float(grid_quads / 2))
			(mat as ShaderMaterial).set_shader_parameter("grid_base_spacing", base_spacing)
			(mat as ShaderMaterial).set_shader_parameter("displaced_extent", displaced_extent())


func _add_piece(piece_name: String, mesh: Mesh, turn: int, spacing: float) -> void:
	var piece := MeshInstance3D.new()
	piece.name = piece_name
	piece.mesh = mesh
	piece.material_override = underside_material if _underside else material
	piece.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	piece.layers = RENDER_LAYER
	piece.transform = Transform3D(Basis(Vector3.UP, turn * PI * 0.5).scaled(Vector3(spacing, spacing, spacing)), Vector3.ZERO)
	add_child(piece)
	_pieces.append(piece)
