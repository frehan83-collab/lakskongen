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
##
## Vessels float (roadmap G2-03): every frame ride_waves() heaves, pitches
## and rolls the hull onto the plane that best fits the drawn water under
## its waterplane (WaveField.hull). Owners keep x, z and yaw: a boat on a
## PathFollow3D is moved along its route and turned as before. A vessel
## carried by another (the barge's tender RIB) floats on its own water but
## stays tied to its carrier's calm-water pose.

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
## Ride the drawn water (G2-03). Off: the vessel keeps the pose it is given.
@export var rides_waves: bool = true

const LOD_NAMES := ["", "LOD1", "LOD2"]
const LOD_HYSTERESIS := 6.0
## Parts that never cast a shadow (glass, small orange gear); LOD1 also
## drops the small machinery and rubber, and LOD2 casts none at all.
const NO_SHADOW_PARTS := ["Windows", "Safety"]
const NO_SHADOW_PARTS_LOD1 := ["Machinery", "Rubber"]
## The part names build_vessels.py writes.
const PART_NAMES := ["Hull", "Deck", "Superstructure", "Windows", "Machinery", "Rubber", "Crane", "Safety"]
## Loaded draught (m) of each asset: the depth a route must keep under the
## keel (route planning, ChannelBoat). Others: 6 % of the length.
const DRAUGHT_M := {
	"rib": 0.6, "workboat": 2.2, "feed_barge": 3.0, "feed_barge_large": 4.0,
	"wellboat": 7.0, "wellboat_large": 8.5,
}
## Past lod_switch.y the vessel is LOD2 and its motion is under a pixel: it
## rides every FAR_RIDE_FRAMES-th frame instead of every frame.
const FAR_RIDE_FRAMES := 8

## A carried vessel's pose on its carrier, captured the first time it is
## needed (after the owner has placed it).
var _rest_local := Transform3D.IDENTITY
var _has_rest_local := false
var _ride_slot := 0

func _ready() -> void:
	_configure_lods()
	_ride_slot = int(get_instance_id() % FAR_RIDE_FRAMES)
	# Navigation, mast and deck lights after dark (VesselLights, SiteLights).
	VesselLights.attach(self)

func _process(_delta: float) -> void:
	if WaveField.active == null or not rides_waves:
		return
	# A carried vessel rides on its carrier's schedule, so it never keeps a
	# stale pose on a carrier that has moved.
	var carrier := _carrier()
	var gauge: VesselModel = self if carrier == null else carrier
	var near := gauge.lod_switch.y
	var cam := get_viewport().get_camera_3d()
	if cam != null and cam.global_position.distance_squared_to(gauge.global_position) > near * near \
			and (Engine.get_process_frames() + gauge._ride_slot) % FAR_RIDE_FRAMES != 0:
		return
	ride_waves()

## Water depth (m) this vessel needs under it.
func get_draught() -> float:
	return float(DRAUGHT_M.get(asset_id, 0.06 * length_m))

## The calm-water pose now: where the owner put the vessel (its x, z and
## yaw, or for a carried vessel its place on its carrier's calm-water pose),
## at the waterline, level. Computed on demand, so it never depends on which
## of a carrier and its passenger rode first.
func rest_transform() -> Transform3D:
	var parent := get_parent() as Node3D
	var carrier := _carrier()
	if carrier != null:
		if not _has_rest_local:
			_rest_local = Transform3D(Basis(Vector3.UP, rotation.y), Vector3(position.x, 0.0, position.z))
			_has_rest_local = true
		return carrier.rest_transform() * _rest_local
	var frame: Transform3D = parent.global_transform if parent != null else Transform3D.IDENTITY
	return frame * Transform3D(Basis(Vector3.UP, rotation.y), Vector3(position.x, 0.0, position.z))

## Float on the drawn water: heave to the mean surface under the waterplane
## (length_m x beam_m), pitch and roll to its mean slopes. A hull shorter
## than a wave rides it; a wave shorter than the hull runs under it.
func ride_waves() -> void:
	var field := WaveField.active
	if field == null or not rides_waves:
		return
	var parent := get_parent() as Node3D
	var frame: Transform3D = parent.global_transform if parent != null else Transform3D.IDENTITY
	var rest := rest_transform()
	var along := Vector2(rest.basis.z.x, rest.basis.z.z).normalized()
	var across := Vector2(rest.basis.x.x, rest.basis.x.z).normalized()
	var plane := field.hull(rest.origin.x, rest.origin.z, along, across, length_m * 0.5, beam_m * 0.5)
	# Rx(pitch) lifts local +Z by -tan(pitch); Rz(roll) lifts local +X by tan(roll).
	var pitch := -atan(plane.y)
	var roll := atan(plane.z)
	if _carrier() != null:
		var pose := Transform3D(rest.basis * Basis.from_euler(Vector3(pitch, 0.0, roll)),
			Vector3(rest.origin.x, plane.x, rest.origin.z))
		transform = frame.affine_inverse() * pose
	else:
		position.y = plane.x - frame.origin.y
		rotation = Vector3(pitch, rotation.y, roll)

## The floating vessel carrying this one (null for a vessel on its own).
func _carrier() -> VesselModel:
	var parent := get_parent()
	if parent is VesselModel and (parent as VesselModel).rides_waves:
		return parent as VesselModel
	return null

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
