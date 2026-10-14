class_name WaterGrid
extends Node3D
## The fjord's water surface (G1-05): a camera-snapped LOD grid out to the
## horizon, drawn with water_shader.gdshader.
##
## Level 0 is a 2 m grid 512 m across (256 quads), centred on the camera;
## every further level is a square ring twice as wide with twice the
## spacing (the same 256 quads across, minus the hole the finer level
## fills), out to 8 levels = ±32.8 km. Each level is four meshes (quadrants
## at level 0, a pinwheel of strips in the rings) so the half behind the
## camera is frustum-culled. The grid follows the camera in whole 2 m steps,
## so the finest lattice never swims. The vertex shader stitches each
## level's outer edge to the coarser ring (odd vertices take the average of
## their neighbours' displacement), so no cracks open between levels.
##
## The material is EnvironmentDirector's to drive (waves, optics, bake);
## this node only owns the geometry and its placement.

## The water material (shared by every grid piece).
@export var material: ShaderMaterial
## Vertex spacing of the finest level (metres).
@export var base_spacing: float = 2.0
## Quads across each level (level 0 spans base_spacing * grid_quads).
@export var grid_quads: int = 256
## Number of levels, level 0 included (8 levels reach 256 m * 2^7 = 32.8 km).
@export var levels: int = 8
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


func _ready() -> void:
	_build()
	_follow()


func _process(_delta: float) -> void:
	_follow()


## Half the width of a level in metres (level 0: 256 m).
func level_half_extent(level: int) -> float:
	return 0.5 * grid_quads * base_spacing * pow(2.0, level)


## Vertex spacing of a level in metres.
func level_spacing(level: int) -> float:
	return base_spacing * pow(2.0, level)


## Vertex spacing (m) of the grid at a world position: the level whose
## square (centred on the grid origin) contains it.
func spacing_at(world_pos: Vector3) -> float:
	var d := maxf(absf(world_pos.x - global_position.x), absf(world_pos.z - global_position.z))
	for level in levels:
		if d <= level_half_extent(level) + 1e-3:
			return level_spacing(level)
	return INF


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
			var piece := MeshInstance3D.new()
			piece.name = "L%d_%d" % [level, turn]
			piece.mesh = quadrant if level == 0 else strip
			piece.material_override = material
			piece.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
			piece.layers = RENDER_LAYER
			piece.transform = Transform3D(Basis(Vector3.UP, turn * PI * 0.5).scaled(Vector3(spacing, spacing, spacing)), Vector3.ZERO)
			add_child(piece)
			_pieces.append(piece)
