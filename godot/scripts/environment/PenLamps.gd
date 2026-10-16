class_name PenLamps
extends Node3D
## The pens' submerged lamps (G3-05). Salmon farms hang lamps a few metres
## down in their pens through the dark half of the year (continuous light
## holds back early maturation); at night a lit farm is a row of glowing
## rings on a black fjord.
##
## This node finds the pens that hold fish (a pen whose school, FishSchool,
## shows the sim's fish: every pen of a G2-04 site is drawn, stocked or not;
## a pen with no school counts when its net shows, a planned pen has none),
## keeps one OmniLight LAMP_DEPTH_M under each of
## the nearest MAX_LAMPS to the camera (they light the net and the fish,
## seen underwater) and lists them for the water, which draws the glow the
## lamps send up through the surface (water_mood.gdshaderinc: the water
## itself is opaque). EnvironmentDirector decides when they burn and how
## bright (it is the only writer of light energy) through apply().

## The water shader's lamp slots (water_mood.gdshaderinc MAX_LAMPS).
const MAX_LAMPS := 24
const LAMP_DEPTH_M := 5.0
const LIGHT_RANGE_M := 24.0
## Pens are looked for again this often (s): sites are built and laid out.
const RESCAN_S := 1.0
## The lighting season (day of year): from early October to the end of April.
const SEASON_FROM_DOY := 275
const SEASON_TO_DOY := 120
## Lamps burn from dusk on the site's photocell (SiteLights.night_for: sun
## under 3 degrees, earlier under a heavy sky), as the site's other lights do.

@export var pen_manager: Node
## Off keeps every lamp dark whatever the hour (the harness's A/B of the
## night shot measures what the lamps add).
@export var enabled := true

## The lit pens' cages, nearest first (tests read them).
var cages: Array[PenCage] = []
var lights: Array[OmniLight3D] = []
var _scan_age := RESCAN_S


func _ready() -> void:
	for i in MAX_LAMPS:
		var light := OmniLight3D.new()
		light.name = "Lamp%d" % i
		light.omni_range = LIGHT_RANGE_M
		light.omni_attenuation = 1.4
		light.shadow_enabled = false
		light.light_specular = 0.3
		light.visible = false
		add_child(light)
		lights.append(light)


## How dark it is for the lamps' photocell (0 day, 1 night): the site's one
## photocell rule, so the lamps, the corner lights and the boats' lights
## switch together.
static func night_for(sun_elevation_deg: float, cloud_cover: float) -> float:
	return SiteLights.night_for(sun_elevation_deg, cloud_cover)


## 1 in the lighting season (October to April), 0 in summer, with a few
## weeks' ramp at each end.
static func season_for(day_of_year: int) -> float:
	var autumn := smoothstep(float(SEASON_FROM_DOY) - 15.0, float(SEASON_FROM_DOY), float(day_of_year))
	var spring := 1.0 - smoothstep(float(SEASON_TO_DOY), float(SEASON_TO_DOY) + 15.0, float(day_of_year))
	return maxf(autumn, spring)


## The lamps for the water: (x, z, the collar's radius, 1) per lit pen,
## nearest the camera first, at most MAX_LAMPS.
func lamp_entries() -> PackedVector4Array:
	var out := PackedVector4Array()
	for cage in cages:
		if is_instance_valid(cage):
			var p := cage.global_position
			out.append(Vector4(p.x, p.z, cage.get_collar_radius(), 1.0))
	return out


func _process(delta: float) -> void:
	_scan_age += delta
	if _scan_age >= RESCAN_S:
		_scan_age = 0.0
		_scan()
	for i in lights.size():
		var cage: PenCage = cages[i] if i < cages.size() else null
		if cage != null and is_instance_valid(cage):
			lights[i].global_position = cage.global_position + Vector3(0.0, -LAMP_DEPTH_M, 0.0)


## Find the stocked pens, nearest the camera first.
func _scan() -> void:
	var root: Node = pen_manager
	if root == null and get_parent() != null:
		# FishPenManager is the environment's child in the game scene.
		root = get_parent().get_node_or_null("FishPenManager")
		pen_manager = root
	if root == null:
		cages = []
		return
	var found: Array[PenCage] = []
	if root.has_method("get_pen_ids") and root.has_method("get_site_cages"):
		# FishPenManager: every pen of every site, without walking the tree.
		for id in root.call("get_pen_ids"):
			for cage in root.call("get_site_cages", id):
				if _holds_fish(cage as PenCage):
					found.append(cage as PenCage)
	else:
		_collect(root, found)
	var cam := get_viewport().get_camera_3d() if is_inside_tree() else null
	if cam != null:
		var eye := cam.global_position
		found.sort_custom(func(a: PenCage, b: PenCage) -> bool:
			return a.global_position.distance_squared_to(eye) < b.global_position.distance_squared_to(eye))
	if found.size() > MAX_LAMPS:
		found.resize(MAX_LAMPS)
	cages = found


static func _collect(node: Node, out: Array[PenCage]) -> void:
	for child in node.get_children():
		if child is PenCage:
			if _holds_fish(child as PenCage):
				out.append(child as PenCage)
		elif child.get_child_count() > 0:
			_collect(child, out)


## A pen holds fish when its school shows the sim's fish (G3-01; an empty
## pen of a site shows none, a ghost site's schools none); a pen with no
## school (a staged one) when it shows its net (a planned pen has none).
static func _holds_fish(cage: PenCage) -> bool:
	if cage == null or not cage.is_visible_in_tree():
		return false
	for child in cage.get_children():
		if child is MultiMeshInstance3D and String(child.name).begins_with("FishSchool"):
			return FishSchool.shown_fish(child as MultiMeshInstance3D) > 0
	for net in cage.get_net_nodes():
		if net.visible:
			return true
	return false
