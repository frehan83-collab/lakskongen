class_name FishAgeVariant
extends Node

## Handles age-class variations for salmon: young (smolt), adult, mature
## Applies scale, color, and material variations

enum AgeClass { YOUNG, ADULT, MATURE }

@export var age_class: AgeClass = AgeClass.ADULT
@export var size_multiplier: float = 1.0

var _body: MeshInstance3D
var _fins: Array[MeshInstance3D]
var _materials: Dictionary

func _ready() -> void:
	_body = get_node_or_null("Body") as MeshInstance3D
	_fins = []
	for child in get_children():
		if child is MeshInstance3D and child != _body:
			_fins.append(child)
	
	_materials = {
		AgeClass.YOUNG: preload("res://assets/models/fish/salmon_young_material.tres"),
		AgeClass.ADULT: preload("res://assets/models/fish/salmon_adult_material.tres"),
		AgeClass.MATURE: preload("res://assets/models/fish/salmon_mature_material.tres"),
	}
	
	apply_age_class(age_class)

func apply_age_class(p_age_class: AgeClass) -> void:
	age_class = p_age_class
	
	var scale_map = {
		AgeClass.YOUNG: 0.4,
		AgeClass.ADULT: 1.0,
		AgeClass.MATURE: 1.6,
	}
	
	size_multiplier = scale_map[age_class]
	
	if _body:
		_body.scale = Vector3.ONE * size_multiplier
	
	for fin in _fins:
		fin.scale *= size_multiplier
	
	var mat = _materials[age_class]
	if mat and _body:
		_body.material_override = mat
	for fin in _fins:
		if mat:
			fin.material_override = mat

func get_random_age_class() -> AgeClass:
	var rand = randf()
	if rand < 0.3:
		return AgeClass.YOUNG
	elif rand < 0.85:
		return AgeClass.ADULT
	else:
		return AgeClass.MATURE