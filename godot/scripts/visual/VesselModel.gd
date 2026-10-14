class_name VesselModel
extends Node3D

## A vessel built by blender/build_vessels.py (roadmap A3-01): the 15 x 10 m
## workboat, the 37 x 11.2 m and 64.8 x 13 m feed barges, the 77 x 17.8 m
## and 116 x 23 m wellboats and the 8.5 x 3 m work RIB.
##
## The scene inherits <asset>_LOD0.glb, so the LOD0 parts are direct
## children: Hull, Deck, Superstructure, Windows, Machinery, Rubber, Crane,
## Safety (one material and one draw call each). LOD1 and LOD2 are the same
## parts, simpler, under the LOD1/LOD2 nodes; visibility ranges switch
## between them. The origin is the waterline amidships and the bow points
## along -Z, the way a PathFollow3D faces.
##
## Every vessel shares the vessel_kit materials: job state is never painted
## on a hull (A3-01 accept 2), it shows on a VesselChip above the boat.

## Which build_vessels.py asset this scene shows.
@export var asset_id: String = "wellboat"
## Moulded hull length and beam (the asset's measured dimensions).
@export var length_m: float = 76.96
@export var beam_m: float = 17.8
## Height of the highest part above the waterline (mast top).
@export var air_draught_m: float = 20.6
## Camera distance (m) where LOD0 hands over to LOD1, and LOD1 to LOD2.
@export var lod_switch: Vector2 = Vector2(260.0, 900.0)
## Past this camera distance (m) the vessel is not drawn at all (0: always).
@export var max_distance: float = 0.0

const LOD_NAMES := ["", "LOD1", "LOD2"]
const LOD_HYSTERESIS := 6.0
## Parts that never cast a shadow (glass, small orange gear); LOD1 also
## drops the small machinery and rubber, and LOD2 casts none at all.
const NO_SHADOW_PARTS := ["Windows", "Safety"]
const NO_SHADOW_PARTS_LOD1 := ["Machinery", "Rubber"]
## The part names build_vessels.py writes.
const PART_NAMES := ["Hull", "Deck", "Superstructure", "Windows", "Machinery", "Rubber", "Crane", "Safety"]

func _ready() -> void:
	_configure_lods()

## The meshes of one LOD level (0 = the direct children).
func get_lod_meshes(level: int) -> Array[MeshInstance3D]:
	var meshes: Array[MeshInstance3D] = []
	var holder: Node = self if level == 0 else get_node_or_null(LOD_NAMES[level])
	if holder == null:
		return meshes
	for child in holder.get_children():
		if child is MeshInstance3D and String(child.name) in PART_NAMES:
			meshes.append(child as MeshInstance3D)
	return meshes

## Every mesh of one part across the LODs (e.g. "Hull").
func get_parts(part: String) -> Array[MeshInstance3D]:
	var out: Array[MeshInstance3D] = []
	for level in LOD_NAMES.size():
		for mesh in get_lod_meshes(level):
			if mesh.name == part:
				out.append(mesh)
	return out

## The vessel's footprint in its own space (length along Z, beam along X).
func get_footprint() -> Vector2:
	return Vector2(beam_m, length_m)

## Where a chip or label sits above the vessel, in local space.
func get_chip_anchor() -> Vector3:
	return Vector3(0.0, air_draught_m + 4.0, 0.0)

func _configure_lods() -> void:
	for level in LOD_NAMES.size():
		var begin: float = 0.0 if level == 0 else lod_switch[level - 1]
		var end: float = lod_switch[level] if level < 2 else max_distance
		for mesh in get_lod_meshes(level):
			mesh.visibility_range_begin = begin
			mesh.visibility_range_end = end
			mesh.visibility_range_begin_margin = LOD_HYSTERESIS if begin > 0.0 else 0.0
			mesh.visibility_range_end_margin = LOD_HYSTERESIS if end > 0.0 else 0.0
			mesh.visibility_range_fade_mode = GeometryInstance3D.VISIBILITY_RANGE_FADE_DISABLED
			var part := String(mesh.name)
			if part in NO_SHADOW_PARTS or level == 2 or (level == 1 and part in NO_SHADOW_PARTS_LOD1):
				mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
