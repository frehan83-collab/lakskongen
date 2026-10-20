class_name FarmGhost
extends Node3D

## The build dialog's ghost in the 3D world (roadmap F2-31,
## docs/FARM_TYPES_SPEC.md §6.2 step 3): a translucent copy of the unit the
## player is about to order, standing on the chosen plot (the lot's tanks and
## hall bays and the pipe route to the intake on land; the unit's ring or
## hull and its mooring footprint at sea), from FarmGhostLayout, the same
## shapes the dialog's lens draws. Presentation only: it reads no engine
## state and changes none. One ghost lives under the fjord environment; the
## dialog finds it through the "farm_ghost" group.

const GROUP := &"farm_ghost"

var _material: StandardMaterial3D = null
var _ring_material: StandardMaterial3D = null
## The plot and variant the ghost shows ("" when hidden).
var plot_id := ""
var variant_id := ""


func _ready() -> void:
	add_to_group(GROUP)
	name = "FarmGhost"


## The ghost under the running scene's fjord environment, made on first use;
## null when no 3D world is up (menus, tests without a game scene).
static func ensure(tree: SceneTree) -> FarmGhost:
	if tree == null:
		return null
	var existing := tree.get_first_node_in_group(GROUP)
	if existing is FarmGhost:
		return existing as FarmGhost
	var env := tree.root.find_child("FjordEnvironment", true, false) as Node3D
	if env == null:
		return null
	var ghost := FarmGhost.new()
	env.add_child(ghost)
	return ghost


## Stand the ghost of `variant` (with `option`, the HFS size) on `plot`
## (engine x/z and water depth from the dialog's plot dictionary).
func show_ghost(plot: Dictionary, variant: String, option: String) -> void:
	clear_ghost()
	plot_id = str(plot.get("id", ""))
	variant_id = variant
	var layout := FarmGhostLayout.shapes_for(plot_id, variant, option,
		Vector2(float(plot.get("x", 0.0)), float(plot.get("z", 0.0))), float(plot.get("water_depth_m", 0.0)))
	for shape in layout.get("shapes", []):
		var node := _mesh_for(shape as Dictionary)
		if node != null:
			add_child(node)
	visible = get_child_count() > 0


## Take the ghost away.
func clear_ghost() -> void:
	for child in get_children():
		remove_child(child)
		child.queue_free()
	plot_id = ""
	variant_id = ""
	visible = false


func _mat(ring: bool) -> StandardMaterial3D:
	if ring and _ring_material != null:
		return _ring_material
	if not ring and _material != null:
		return _material
	var m := StandardMaterial3D.new()
	m.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	m.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	m.cull_mode = BaseMaterial3D.CULL_DISABLED
	m.no_depth_test = false
	# Cool translucent white for the build, the equipment orange for the ring
	# and routes (saturation only on equipment, the art direction's rule).
	m.albedo_color = Color(0.86, 0.93, 0.97, 0.38) if not ring else Color(0.95, 0.53, 0.42, 0.55)
	if ring:
		_ring_material = m
	else:
		_material = m
	return m


func _mesh_for(shape: Dictionary) -> MeshInstance3D:
	var world: Vector3 = shape.get("world", Vector3.ZERO)
	var size: Vector2 = shape.get("size", Vector2.ZERO)
	var height := float(shape.get("height", 0.0))
	var mi := MeshInstance3D.new()
	mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	match str(shape.get("kind", "")):
		"tank":
			var c := CylinderMesh.new()
			c.top_radius = size.x * 0.5
			c.bottom_radius = size.x * 0.5
			c.height = maxf(1.0, height)
			mi.mesh = c
			mi.position = world + Vector3(0.0, c.height * 0.5, 0.0)
			mi.material_override = _mat(false)
		"box", "hull":
			var b := BoxMesh.new()
			b.size = Vector3(size.x, maxf(1.0, height), size.y)
			mi.mesh = b
			mi.position = world + Vector3(0.0, b.size.y * 0.5, 0.0)
			mi.material_override = _mat(str(shape.get("kind", "")) == "hull")
		"ring", "mooring":
			var t := TorusMesh.new()
			var r := size.x * 0.5
			var tube := 3.0 if str(shape.get("kind", "")) == "ring" else 1.0
			t.inner_radius = maxf(0.5, r - tube)
			t.outer_radius = r + tube
			mi.mesh = t
			mi.position = world + Vector3(0.0, 0.5, 0.0)
			mi.material_override = _mat(true)
		_:
			return null
	return mi
