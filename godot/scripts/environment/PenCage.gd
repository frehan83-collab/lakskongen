class_name PenCage
extends Node3D

## Low-poly salmon pen cage model
## Components: Collar (12-segment torus), Net (alpha-cutout plane), Walkway, Handrail, Feed Pipe
## Scale: ~160m circumference (radius ~25.5m) × 40m depth

@export_group("Pen Dimensions")
@export var collar_radius: float = 25.5  # ~160m circumference / (2*PI)
@export var collar_thickness: float = 2.0
@export var net_depth: float = 40.0
@export var walkway_width: float = 1.5
@export var handrail_height: float = 1.1
@export var feedpipe_radius: float = 0.3

@export_group("Materials")
@export var collar_material: Material
@export var net_material: Material
@export var walkway_material: Material
@export var handrail_material: Material
@export var feedpipe_material: Material

@export_group("Health Indicator")
@export var health_color: Color = Color(0.25, 0.75, 0.35, 0.85)

var _collar_node: MeshInstance3D
var _net_node: MeshInstance3D
var _walkway_node: MeshInstance3D
var _handrail_node: MeshInstance3D
var _feedpipe_node: MeshInstance3D
var _click_area: Area3D

func _ready() -> void:
	_collar_node = get_node_or_null("Collar") as MeshInstance3D
	_net_node = get_node_or_null("Net") as MeshInstance3D
	_walkway_node = get_node_or_null("Walkway") as MeshInstance3D
	_handrail_node = get_node_or_null("Handrail") as MeshInstance3D
	_feedpipe_node = get_node_or_null("FeedPipe") as MeshInstance3D
	_click_area = get_node_or_null("ClickArea") as Area3D
	
	if _click_area:
		_click_area.input_event.connect(_on_input_event.bind())
	
	_update_materials()

func _on_input_event(camera: Node, event: InputEvent, position: Vector3, normal: Vector3, shape_idx: int) -> void:
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
			# Intentionally untyped: referencing the FishPenManager class here
			# creates a circular GDScript dependency (FishPenManager preloads
			# pen_cage.tscn -> PenCage.gd), which prevents the scene from
			# loading. Emitting by name keeps the edge dynamic.
			var manager := get_parent()
			if manager and manager.has_signal("pen_clicked"):
				manager.emit_signal("pen_clicked", name, global_position)

func set_health_color(color: Color) -> void:
	health_color = color
	_update_materials()

func _update_materials() -> void:
	if _collar_node and _collar_node.mesh and _collar_node.mesh.surface_get_material(0) is StandardMaterial3D:
		(_collar_node.mesh.surface_get_material(0) as StandardMaterial3D).albedo_color = health_color

func get_click_area() -> Area3D:
	return _click_area

func get_collar_radius() -> float:
	return collar_radius

func get_net_depth() -> float:
	return net_depth