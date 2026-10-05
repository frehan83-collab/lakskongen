@tool
extends Marker3D
## Fish pen location marker with metadata
## Stores pen identification, capacity, and position data for simulation

@export var pen_id: String = "":
	set(value):
		pen_id = value
		_update_gizmo()

@export var capacity: int = 200000:
	set(value):
		capacity = max(0, value)
		_update_gizmo()

@export var pen_radius: float = 15.0:
	set(value):
		pen_radius = max(1.0, value)
		_update_gizmo()

@export var show_debug_info: bool = true:
	set(value):
		show_debug_info = value
		_update_gizmo()

func _ready() -> void:
	if not Engine.is_editor_hint():
		# Register pen with game systems
		_register_pen()

func _register_pen() -> void:
	# Called at runtime to register this pen location with the game manager
	# Game systems can query pen locations via get_tree().get_nodes_in_group("fish_pens")
	add_to_group("fish_pens")
	
	print("Fish pen registered: ID=%s, Capacity=%d, Position=%v" % [pen_id, capacity, global_position])

func get_pen_data() -> Dictionary:
	"""Returns pen metadata as a dictionary for serialization/integration"""
	return {
		"id": pen_id,
		"capacity": capacity,
		"radius": pen_radius,
		"position": global_position,
		"world_position_2d": Vector2(global_position.x, global_position.z)
	}

func _update_gizmo() -> void:
	# Visual update for editor (gizmo plugin would handle actual drawing)
	queue_redraw() if has_method("queue_redraw") else update_gizmo()

# Debug visualization (optional, for editor/runtime debugging)
func _get_configuration_warnings() -> PackedStringArray:
	var warnings: PackedStringArray = []
	
	if pen_id.is_empty():
		warnings.append("Pen ID is not set. Each pen should have a unique identifier.")
	
	if capacity <= 0:
		warnings.append("Pen capacity should be greater than 0.")
	
	# Check for duplicate IDs among siblings
	if get_parent():
		for sibling in get_parent().get_children():
			if sibling != self and sibling is Marker3D and sibling.has_method("get_pen_data"):
				if sibling.get("pen_id") == pen_id and not pen_id.is_empty():
					warnings.append("Duplicate pen_id detected: '%s'" % pen_id)
					break
	
	return warnings
