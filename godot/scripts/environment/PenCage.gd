class_name PenCage
extends Node3D

## A floating salmon pen (AKVA Polarcirkel 500-class circular HDPE pen).
## Components: Collar, Net, Walkway, Handrail, FeedPipe (the names other code
## looks up), an owner colour Band, the HealthRing signal on the water and
## the ClickArea owners connect to.
## Scale: 160 m circumference (radius ~25.5 m), net 40 m deep.
##
## Per-pen state goes through instance shader parameters, so every pen and
## every rival farm shares the same materials (roadmap G1-07):
##   set_health_state() / set_health_color()  the ring on the water
##   set_owner_band()                         rival colour band
##   set_net_opacity()                        quieter rival nets

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
@export var health_color: Color = Color("#9FB4BB", 0.35)

## Site health as the ring shows it. UNKNOWN covers an empty or fallow site.
enum Health { UNKNOWN, OK, WARN, CRIT }

## World signal colours: the D13 "Kyst & Stal" Ink signal tokens (ok, warn,
## crit, secondary), with alpha as the ring's opacity. Trouble is louder.
const HEALTH_COLORS := {
	Health.UNKNOWN: Color(0.624, 0.706, 0.733, 0.35),  # #9FB4BB
	Health.OK: Color(0.498, 0.839, 0.608, 0.55),       # #7FD69B
	Health.WARN: Color(0.949, 0.718, 0.020, 0.85),     # #F2B705
	Health.CRIT: Color(1.0, 0.541, 0.502, 0.95),       # #FF8A80
}
const HEALTH_PULSE_HZ := {
	Health.UNKNOWN: 0.0,
	Health.OK: 0.0,
	Health.WARN: 0.5,
	Health.CRIT: 1.2,
}
## Ink surface (#0B2A3A) behind world text, for label outlines.
const INK := Color(0.043, 0.165, 0.227, 0.92)
## Label text sits this high above the waterline.
const LABEL_HEIGHT := 9.0

var health_state: int = Health.UNKNOWN

var _collar_node: MeshInstance3D
var _net_node: MeshInstance3D
var _walkway_node: MeshInstance3D
var _handrail_node: MeshInstance3D
var _feedpipe_node: MeshInstance3D
var _band_node: MeshInstance3D
var _health_ring: MeshInstance3D
var _click_area: Area3D
var _band_color: Color = Color(0, 0, 0, 0)
var _net_opacity: float = 1.0

func _ready() -> void:
	_collar_node = get_node_or_null("Collar") as MeshInstance3D
	_net_node = get_node_or_null("Net") as MeshInstance3D
	_walkway_node = get_node_or_null("Walkway") as MeshInstance3D
	_handrail_node = get_node_or_null("Handrail") as MeshInstance3D
	_feedpipe_node = get_node_or_null("FeedPipe") as MeshInstance3D
	_band_node = get_node_or_null("Band") as MeshInstance3D
	_health_ring = get_node_or_null("HealthRing") as MeshInstance3D
	# Owners wire clicks: they connect ClickArea.input_event (FishPenManager, VisualAdapter3D).
	_click_area = get_node_or_null("ClickArea") as Area3D
	_apply_health()
	_apply_band()
	_apply_net_opacity()

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

## Paint the owner's colour band (rival farms). Alpha 0 hides it.
func set_owner_band(color: Color) -> void:
	_band_color = color
	_apply_band()

func get_owner_band() -> Color:
	return _band_color

func _apply_band() -> void:
	if _band_node == null:
		return
	_band_node.visible = _band_color.a > 0.0
	_band_node.set_instance_shader_parameter("band_color", _band_color)

## Net visibility, 0..1 (rival pens are drawn quieter than the player's).
func set_net_opacity(value: float) -> void:
	_net_opacity = clampf(value, 0.0, 1.0)
	_apply_net_opacity()

func get_net_opacity() -> float:
	return _net_opacity

func _apply_net_opacity() -> void:
	for net in get_net_nodes():
		net.set_instance_shader_parameter("net_opacity", _net_opacity)

## Every mesh drawn with the net shader.
func get_net_nodes() -> Array[MeshInstance3D]:
	var nets: Array[MeshInstance3D] = []
	if _net_node:
		nets.append(_net_node)
	return nets

func get_click_area() -> Area3D:
	return _click_area

func get_collar_radius() -> float:
	return collar_radius

func get_net_depth() -> float:
	return net_depth

## Readable world label: billboarded, constant on-screen size, light text on
## an Ink outline so it reads over water, rock and sky alike.
static func style_label(label: Label3D, text_color: Color) -> void:
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
