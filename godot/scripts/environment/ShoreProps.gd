class_name FjordShoreProps
extends Node3D
## Naust boathouses and a quay on the fjord shore (G2-02).
##
## Placed by ShorePropPlacer (sim) on the terrain worker thread: sheltered,
## gentle, dry shore near the water, each building turned to face it, the
## woods cleared around them. Meshes: blender/build_shore_props.py. The naust
## walls take a red ochre or a tar-black paint per building.

const SCENES := {
	0: "res://assets/models/shore/naust_LOD0.glb",
	1: "res://assets/models/shore/shore_quay_LOD0.glb",
}
## Paints for the naust walls (sRGB): red ochre, tar black.
const NAUST_PAINT := [Color("#8E3B2A"), Color("#2E2620")]
const WALL_MATERIAL := "LK_NaustWall"
## Beyond this a building is a few pixels; draw nothing (m).
const VISIBILITY_END_M := 3200.0

## Buildings placed: {kind, position, yaw, variant} as the terrain build gave them.
var placed: Array = []

var _paints: Array[Material] = []


## Stand the buildings up (Array of {kind, position, yaw, variant}).
func build(props: Array) -> void:
	for child in get_children():
		child.queue_free()
	placed = []
	for prop: Dictionary in props:
		var path: String = SCENES.get(int(prop["kind"]), "")
		var scene := load(path) as PackedScene if path != "" and ResourceLoader.exists(path) else null
		if scene == null:
			push_warning("[ShoreProps] no model for kind %s" % str(prop["kind"]))
			continue
		var node := scene.instantiate() as Node3D
		node.name = "%s_%d" % ["Naust" if int(prop["kind"]) == 0 else "Quay", placed.size()]
		node.position = prop["position"]
		node.rotation = Vector3(0.0, float(prop["yaw"]), 0.0)
		add_child(node)
		_finish_meshes(node, int(prop["kind"]), int(prop.get("variant", 0)))
		placed.append(prop)


func _finish_meshes(root: Node, kind: int, variant: int) -> void:
	var stack: Array[Node] = [root]
	while not stack.is_empty():
		var node: Node = stack.pop_back()
		stack.append_array(node.get_children())
		var mesh_instance := node as MeshInstance3D
		if mesh_instance == null or mesh_instance.mesh == null:
			continue
		mesh_instance.visibility_range_end = VISIBILITY_END_M
		mesh_instance.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
		if kind != 0:
			continue
		for surface in mesh_instance.mesh.get_surface_count():
			var material := mesh_instance.mesh.surface_get_material(surface)
			if material != null and material.resource_name == WALL_MATERIAL:
				mesh_instance.set_surface_override_material(surface, _paint(variant, material))


func _paint(variant: int, base: Material) -> Material:
	while _paints.size() < NAUST_PAINT.size():
		var painted := base.duplicate() as BaseMaterial3D
		if painted == null:
			return base
		painted.albedo_color = NAUST_PAINT[_paints.size()]
		_paints.append(painted)
	return _paints[clampi(variant, 0, _paints.size() - 1)]
