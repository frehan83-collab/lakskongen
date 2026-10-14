class_name PenCage
extends Node3D

## A floating salmon pen: the AKVA Polarcirkel 500-class hero asset built by
## blender/build_pen.py (160 m twin HDPE collar, brackets every 2.67 m,
## uprights and handrail, walkway, bird net on a central mast, 30 m net wall
## with a cone bottom, sinker tube, feed hose to a central spreader, and an
## optional lice skirt).
##
## The scene inherits pen_cage_LOD0.glb, so the LOD0 parts are direct
## children under the names other code looks up (Collar, Net, Walkway,
## Handrail, FeedPipe, Band, BirdNet, BirdNetPoles, SinkerTube, LiceSkirt).
## LOD1 and LOD2 are the same parts, simpler, under the LOD1/LOD2 nodes;
## visibility ranges switch between them. HealthRing is the signal on the
## water and ClickArea is what owners connect clicks to.
##
## Per-pen state goes through instance shader parameters, so every pen and
## every rival farm shares the same materials (roadmap G1-07):
##   set_health_state() / set_health_color()  the ring on the water
##   set_owner_band()                         rival colour band
##   set_net_opacity()                        quieter rival nets
##   set_lice_skirt()                         the skirt variant
##   set_bird_net()                           tent on a centre mast, or green
##                                            net on blue rim poles (ref. 5)
##
## In a multi-pen site (FarmSite, roadmap G2-04) the site draws every pen's
## LOD1 and LOD2 through one MultiMesh per part (set_site_drawn()): the pen
## keeps its LOD0 meshes, its health ring and its click area.
##
## The pen floats (roadmap G2-03): every frame ride_waves() sets the node's
## height and tilt from the drawn water (WaveField), so pens, nets and the
## health ring rise and fall with the sea. Its x, z and yaw stay its owner's.

@export_group("Pen Dimensions")
@export var collar_radius: float = 25.5  # 160 m inner pipe circumference / (2*PI)
@export var collar_thickness: float = 1.6  # twin 500 mm pipes at 1.1 m centres
@export var net_depth: float = 38.0  # 30 m wall + 8 m cone
@export var walkway_width: float = 1.205
@export var handrail_height: float = 0.95
@export var feedpipe_radius: float = 0.055

@export_group("Materials")
@export var collar_material: Material
@export var net_material: Material
@export var walkway_material: Material
@export var handrail_material: Material
@export var feedpipe_material: Material

@export_group("Waves")
## Ride the drawn water (G2-03). Off: the pen keeps its rest height.
@export var rides_waves: bool = true

@export_group("Health Indicator")
@export var health_color: Color = Color("#9FB4BB", 0.35)

## Site health as the ring shows it. UNKNOWN covers an empty or fallow site.
enum Health { UNKNOWN, OK, WARN, CRIT }

## World signal colours: the D13 "Kyst & Stal" Ink signal tokens (ok, warn,
## crit, secondary), with alpha as the ring's opacity. Trouble is louder.
const HEALTH_COLORS := {
	Health.UNKNOWN: Color(0.624, 0.706, 0.733, 0.35),  # #9FB4BB
	Health.OK: Color(0.498, 0.839, 0.608, 0.32),       # #7FD69B, calm while healthy
	Health.WARN: Color(0.949, 0.718, 0.020, 0.85),     # #F2B705
	Health.CRIT: Color(1.0, 0.541, 0.502, 0.95),       # #FF8A80
}
## Halo strength per state: calm while healthy or unknown, full glow when
## something needs the player (owner brief: alarms rare and meaningful).
const HEALTH_GLOW := {
	Health.UNKNOWN: 0.3,
	Health.OK: 0.3,
	Health.WARN: 1.0,
	Health.CRIT: 1.0,
}
const HEALTH_PULSE_HZ := {
	Health.UNKNOWN: 0.0,
	Health.OK: 0.0,
	Health.WARN: 0.5,
	Health.CRIT: 1.2,
}
## Ink surface (#0B2A3A) behind world text, for label outlines.
const INK := Color(0.043, 0.165, 0.227, 0.92)
## Label text sits this high above the waterline (above the bird-net mast).
const LABEL_HEIGHT := 11.0

## Camera distance (m) where each LOD hands over to the next. LOD0 keeps the
## brackets and uprights while they are a few pixels wide; LOD2 (< 2k
## triangles) takes over once the whole pen is ~100 px across.
const LOD_SWITCH := [260.0, 850.0]
const LOD_HYSTERESIS := 8.0
const LOD_NAMES := ["", "LOD1", "LOD2"]
## Parts drawn with the net shader (translucent, no shadows).
const NET_PARTS := ["Net", "BirdNet", "BirdNetRim"]
## Parts that cast no shadow: the see-through nets and what hangs under the
## water (a shadow there is never seen, but every cascade would draw it).
## Past LOD0 nothing casts a shadow (roadmap P0-04: the pen is a few pixels
## wide there and every cascade would draw it again).
const NO_SHADOW_PARTS := ["Net", "BirdNet", "BirdNetRim", "SinkerTube", "LiceSkirt"]
## Bird-net styles: a tent from a centre mast (owner references 2 and 4) or
## a green net on blue rim poles (reference 5), or none.
enum BirdNet { TENT, RIM, NONE }
## The parts each bird-net style shows.
const BIRD_NET_PARTS := {
	BirdNet.TENT: ["BirdNet", "BirdNetPoles"],
	BirdNet.RIM: ["BirdNetRim", "BirdNetRimPoles"],
	BirdNet.NONE: [],
}

## The circle the collar floats on: the walkway centreline between the twin
## pipes (blender/build_pen.py WALK_MID = R_IN + PIPE_SPACING / 2).
const RING_RADIUS := 26.015
## Past this camera distance (m) the pen is LOD2, its motion is under a
## pixel and the grid there draws only long waves: it rides every
## FAR_RIDE_FRAMES-th frame instead of every frame.
const NEAR_RIDE_M := 850.0
const FAR_RIDE_FRAMES := 8

var health_state: int = Health.UNKNOWN

var _collar_node: MeshInstance3D
var _net_node: MeshInstance3D
var _walkway_node: MeshInstance3D
var _handrail_node: MeshInstance3D
var _feedpipe_node: MeshInstance3D
var _health_ring: MeshInstance3D
var _click_area: Area3D
var _band_color: Color = Color(0, 0, 0, 0)
var _net_opacity: float = 1.0
var _lice_skirt: bool = false
var _bird_net: int = BirdNet.TENT
## True once a FarmSite draws this pen's LOD1 and LOD2 (set_site_drawn).
var _site_drawn := false
var _lod0_shown := true
## Render layers of the LOD0 meshes while shown.
const LOD0_LAYERS := 1
## Bake sample under the pen, keyed by (x, z, WaveField.bake_version).
var _bake_key := Vector3(INF, INF, -1.0)
var _bake_sample := Color()
var _ride_slot := 0
## Counts ride_waves() calls: a FarmSite re-uploads its MultiMesh poses
## only when one of its pens has moved.
var ride_count := 0

func _ready() -> void:
	_collar_node = get_node_or_null("Collar") as MeshInstance3D
	_net_node = get_node_or_null("Net") as MeshInstance3D
	_walkway_node = get_node_or_null("Walkway") as MeshInstance3D
	_handrail_node = get_node_or_null("Handrail") as MeshInstance3D
	_feedpipe_node = get_node_or_null("FeedPipe") as MeshInstance3D
	_health_ring = get_node_or_null("HealthRing") as MeshInstance3D
	# Owners wire clicks: they connect ClickArea.input_event (FishPenManager).
	_click_area = get_node_or_null("ClickArea") as Area3D
	_configure_lods()
	_apply_health()
	_apply_band()
	_apply_net_opacity()
	_apply_lice_skirt()
	_apply_bird_net()
	_ride_slot = int(get_instance_id() % FAR_RIDE_FRAMES)


func _process(_delta: float) -> void:
	if WaveField.active == null or not rides_waves:
		return
	var cam := get_viewport().get_camera_3d()
	if cam != null and cam.global_position.distance_squared_to(global_position) > NEAR_RIDE_M * NEAR_RIDE_M \
			and (Engine.get_process_frames() + _ride_slot) % FAR_RIDE_FRAMES != 0:
		return
	ride_waves()


## Float on the drawn water: the collar settles on the plane that best fits
## the surface under its ring (WaveField.ring: heave and a gentle tilt), so
## the pen rides the swell and lets chop shorter than itself run under it,
## as a 160 m HDPE ring does. Moves only this node's height and tilt.
func ride_waves() -> void:
	var field := WaveField.active
	if field == null or not rides_waves:
		return
	var parent := get_parent() as Node3D
	var frame: Transform3D = parent.global_transform if parent != null else Transform3D.IDENTITY
	var rest := frame * Vector3(position.x, 0.0, position.z)
	if rest.x != _bake_key.x or rest.z != _bake_key.y or float(field.bake_version) != _bake_key.z:
		_bake_key = Vector3(rest.x, rest.z, float(field.bake_version))
		_bake_sample = field.bake_at(rest.x, rest.z)
	var plane := field.ring(rest.x, rest.z, RING_RADIUS, _bake_sample)
	# The world slope in the pen's own axes (its yaw inside its owner's frame).
	var axes := frame.basis * Basis(Vector3.UP, rotation.y)
	var slope_x := plane.y * axes.x.x + plane.z * axes.x.z
	var slope_z := plane.y * axes.z.x + plane.z * axes.z.z
	position.y = plane.x - frame.origin.y
	rotation = Vector3(-atan(slope_z), rotation.y, atan(slope_x))
	ride_count += 1

## The meshes of one LOD level (0 = the direct children).
func get_lod_meshes(level: int) -> Array[MeshInstance3D]:
	var meshes: Array[MeshInstance3D] = []
	var holder: Node = self if level == 0 else get_node_or_null(LOD_NAMES[level])
	if holder == null:
		return meshes
	for child in holder.get_children():
		if child is MeshInstance3D and child != _health_ring and child.name != "HealthRing":
			meshes.append(child as MeshInstance3D)
	return meshes

## Every mesh part of one name across the LODs (e.g. "Net", "Band").
func get_parts(part: String) -> Array[MeshInstance3D]:
	var out: Array[MeshInstance3D] = []
	for level in LOD_NAMES.size():
		for mesh in get_lod_meshes(level):
			if mesh.name == part:
				out.append(mesh)
	return out

func _configure_lods() -> void:
	for level in LOD_NAMES.size():
		var begin: float = 0.0 if level == 0 else LOD_SWITCH[level - 1]
		var end: float = LOD_SWITCH[level] if level < LOD_SWITCH.size() else 0.0
		for mesh in get_lod_meshes(level):
			mesh.visibility_range_begin = begin
			mesh.visibility_range_end = end
			mesh.visibility_range_begin_margin = LOD_HYSTERESIS if begin > 0.0 else 0.0
			mesh.visibility_range_end_margin = LOD_HYSTERESIS if end > 0.0 else 0.0
			mesh.visibility_range_fade_mode = GeometryInstance3D.VISIBILITY_RANGE_FADE_DISABLED
			mesh.cast_shadow = shadow_for(String(mesh.name), level)

## Whether a part of a LOD casts a shadow: LOD0's solid parts only.
static func shadow_for(part: String, level: int) -> GeometryInstance3D.ShadowCastingSetting:
	if level >= 1 or part in NO_SHADOW_PARTS:
		return GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	return GeometryInstance3D.SHADOW_CASTING_SETTING_ON

## The ring colour for a health state.
static func health_color_of(state: int) -> Color:
	return HEALTH_COLORS.get(state, HEALTH_COLORS[Health.UNKNOWN])

## Show the site's health on the ring (colour, opacity and pulse).
func set_health_state(state: int) -> void:
	health_state = state
	health_color = health_color_of(state)
	_apply_health()

## Legacy entry point: a raw colour, no pulse.
func set_health_color(color: Color) -> void:
	health_state = Health.UNKNOWN
	for state in HEALTH_COLORS:
		if HEALTH_COLORS[state] == color:
			health_state = state
	health_color = color
	_apply_health()

func get_health_color() -> Color:
	return health_color

func show_health_ring(on: bool) -> void:
	if _health_ring:
		_health_ring.visible = on

func get_health_ring() -> MeshInstance3D:
	return _health_ring

func _apply_health() -> void:
	if _health_ring == null:
		return
	_health_ring.set_instance_shader_parameter("ring_color", health_color)
	_health_ring.set_instance_shader_parameter("pulse_hz", float(HEALTH_PULSE_HZ.get(health_state, 0.0)))
	_health_ring.set_instance_shader_parameter("glow", float(HEALTH_GLOW.get(health_state, 1.0)))

## Paint the owner's colour band (rival farms). Alpha 0 hides it.
func set_owner_band(color: Color) -> void:
	_band_color = color
	_apply_band()

func get_owner_band() -> Color:
	return _band_color

func get_band_nodes() -> Array[MeshInstance3D]:
	return get_parts("Band")

func _apply_band() -> void:
	for band in get_band_nodes():
		band.visible = _band_color.a > 0.0
		band.set_instance_shader_parameter("band_color", _band_color)

## Net visibility, 0..1 (rival pens are drawn quieter than the player's).
func set_net_opacity(value: float) -> void:
	_net_opacity = clampf(value, 0.0, 1.0)
	_apply_net_opacity()

func get_net_opacity() -> float:
	return _net_opacity

func _apply_net_opacity() -> void:
	for net in get_net_nodes():
		net.set_instance_shader_parameter("net_opacity", _net_opacity)

## Every mesh drawn with the net shader (pen net and bird net, all LODs).
func get_net_nodes() -> Array[MeshInstance3D]:
	var nets: Array[MeshInstance3D] = []
	for part in NET_PARTS:
		nets.append_array(get_parts(part))
	return nets

## The lice-skirt variant: a tarpaulin round the top 10 m of the net
## (GameBridge sends it per site as "has_skirt").
func set_lice_skirt(on: bool) -> void:
	_lice_skirt = on
	_apply_lice_skirt()

func has_lice_skirt() -> bool:
	return _lice_skirt

func _apply_lice_skirt() -> void:
	for skirt in get_parts("LiceSkirt"):
		skirt.visible = _lice_skirt

## Which bird net the pen carries (BirdNet.TENT, RIM or NONE).
func set_bird_net(style: int) -> void:
	_bird_net = style if BIRD_NET_PARTS.has(style) else BirdNet.TENT
	_apply_bird_net()

func get_bird_net() -> int:
	return _bird_net

func _apply_bird_net() -> void:
	for style in BIRD_NET_PARTS:
		for part in BIRD_NET_PARTS[style]:
			for mesh in get_parts(part):
				mesh.visible = style == _bird_net

## Whether a part shows under the pen's current variants (skirt, bird net,
## owner band): what a FarmSite's MultiMesh of that part shows.
func part_shown(part: String) -> bool:
	if part == "LiceSkirt":
		return _lice_skirt
	if part == "Band":
		return _band_color.a > 0.0
	for style in BIRD_NET_PARTS:
		if part in BIRD_NET_PARTS[style]:
			return style == _bird_net
	return true

## A FarmSite draws this pen's LOD1 and LOD2 through its MultiMeshes: the
## pen's own LOD1/LOD2 meshes are hidden for good and its LOD0 meshes are
## shown and hidden by the site (set_lod0_visible), not by distance.
func set_site_drawn(on: bool) -> void:
	_site_drawn = on
	for level in [1, 2]:
		var holder := get_node_or_null(LOD_NAMES[level]) as Node3D
		if holder != null:
			holder.visible = not on
	for mesh in get_lod_meshes(0):
		mesh.visibility_range_end = 0.0 if on else LOD_SWITCH[0]
		mesh.visibility_range_end_margin = 0.0 if on else LOD_HYSTERESIS

func is_site_drawn() -> bool:
	return _site_drawn

## Show or hide the LOD0 meshes (a FarmSite's LOD hand-over). It clears
## their render layers rather than `visible`, which the variants own.
func set_lod0_visible(on: bool) -> void:
	_lod0_shown = on
	for mesh in get_lod_meshes(0):
		mesh.layers = LOD0_LAYERS if on else 0

func is_lod0_visible() -> bool:
	return _lod0_shown


func get_click_area() -> Area3D:
	return _click_area

func get_collar_radius() -> float:
	return collar_radius

func get_net_depth() -> float:
	return net_depth

## Readable world label: billboarded, constant on-screen size, light text on
## an Ink outline so it reads over water, rock and sky alike. Constant-size
## labels of far farms pile up on the horizon, so they fade out past
## `max_distance` metres.
static func style_label(label: Label3D, text_color: Color, max_distance: float = 1600.0) -> void:
	label.visibility_range_end = max_distance
	label.visibility_range_end_margin = max_distance * 0.1
	label.visibility_range_fade_mode = GeometryInstance3D.VISIBILITY_RANGE_FADE_SELF
	label.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	label.fixed_size = true
	label.pixel_size = 0.0005
	label.font_size = 40
	label.outline_size = 12
	label.modulate = text_color
	label.outline_modulate = INK
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.vertical_alignment = VERTICAL_ALIGNMENT_BOTTOM
	label.render_priority = 2
	label.outline_render_priority = 1
	label.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
