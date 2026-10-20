class_name MistBanks
extends MultiMeshInstance3D
## Low cloud on the mountains (G3-05). Under a low deck (Gråvær, and scud in
## a storm) soft banks of cloud hang on the fjord's walls from the cloud
## base up and drift with the wind, as in the owner's overcast reference
## (ref 3: low cloud and mist on the mountains, their tops lost in it).
##
## The engine's height fog can only fade a slope evenly with its altitude
## (EnvironmentDirector's mist under a low deck), which reads as pale rock,
## not as cloud. The banks are cards stood upright along the walls' contour
## lines at a few altitudes, turned to face the camera about the vertical
## (mist_bank.gdshader): ragged noise wisps, flat-bottomed at the cloud
## base, faded where they meet the slope (the depth buffer) and near the
## camera. One draw call. EnvironmentDirector is the only caller: it hands
## them the cloud base, how much mist the mood shows, the deck's light and
## the wind every frame (update()).

const NODE_NAME := "MistBanks"
const SHADER := preload("res://shaders/mist_bank.gdshader")
## Contour altitudes the banks stand on (m) and the spacing along a wall.
const ALTITUDES := [170.0, 240.0, 320.0, 420.0, 540.0]
const SPACING_M := 260.0
## A bank's size (m) and the random spread of it (share).
const WIDTH_M := 560.0
const HEIGHT_M := 220.0
const SIZE_SPREAD := 0.3
## How far out over the slope a bank hangs from its contour (m), and how
## far it may stand above the contour (share of its height).
const STAND_OFF_M := 50.0
const LIFT := 0.3
## The contour search: steps out from the channel and how far it goes (m).
const SEARCH_STEP_M := 15.0
const SEARCH_MAX_M := 2200.0
## The wisps drift along the bank at this share of the wind speed.
const DRIFT_SHARE := 0.35
## Mist below this amount is not drawn.
const MIN_AMOUNT := 0.002

## Mist shown now (0-1), the cloud base (m) and the banks placed (tests).
var amount := 0.0
var cloud_base_m := 0.0
var bank_count := 0
var built := false
## Each bank's centre and the contour altitude it stands on, kept on the CPU
## (tests read them; a headless renderer keeps no MultiMesh data).
var bank_origins := PackedVector3Array()
var bank_altitudes := PackedFloat32Array()
var _drift := 0.0
var _material: ShaderMaterial


## The banks under `parent`, created on first use (added deferred: the
## director asks from its _ready, while the parent sets up its children).
static func ensure(parent: Node) -> MistBanks:
	if parent == null:
		return null
	var node := parent.get_node_or_null(NODE_NAME) as MistBanks
	if node == null:
		node = MistBanks.new()
		node.name = NODE_NAME
		parent.add_child.call_deferred(node)
	return node


func _init() -> void:
	_material = ShaderMaterial.new()
	_material.shader = SHADER
	material_override = _material
	cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	# Cloud, not a reflecting surface's subject: the half-resolution mirror
	# would pay for every card again (G2-03 NO_MIRROR_LAYER).
	layers = PlanarReflection.NO_MIRROR_LAYER
	# The cards turn about their own centres in the shader: bound them wide.
	extra_cull_margin = WIDTH_M
	visible = false


## Stand the banks on the walls of `terrain` (once it is built).
func build(terrain: FjordTerrain) -> void:
	if terrain == null or not terrain.is_ready():
		return
	var anchors := contour_anchors(terrain)
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_custom_data = true
	var quad := QuadMesh.new()
	quad.size = Vector2.ONE
	mm.mesh = quad
	mm.instance_count = anchors.size()
	bank_origins.resize(anchors.size())
	bank_altitudes.resize(anchors.size())
	var rng := RandomNumberGenerator.new()
	rng.seed = 7331
	for i in anchors.size():
		var a: Vector3 = anchors[i]
		var w := WIDTH_M * (1.0 + SIZE_SPREAD * rng.randf_range(-1.0, 1.0))
		var h := HEIGHT_M * (1.0 + SIZE_SPREAD * rng.randf_range(-1.0, 1.0))
		var origin := a + Vector3(0.0, h * (0.5 - 0.5 * LIFT) + h * LIFT * rng.randf(), 0.0)
		mm.set_instance_transform(i, Transform3D(Basis.from_scale(Vector3(w, h, 1.0)), origin))
		mm.set_instance_custom_data(i, Color(rng.randf(), a.y, 0.0, 0.0))
		bank_origins[i] = origin
		bank_altitudes[i] = a.y
	multimesh = mm
	bank_count = anchors.size()
	built = true


## Points on the walls' contours at ALTITUDES (pulled STAND_OFF_M out over
## the water), every SPACING_M along the fjord, on both sides.
static func contour_anchors(terrain: FjordTerrain) -> Array[Vector3]:
	var out: Array[Vector3] = []
	var ends := terrain.mouth_and_head()
	var z0 := minf(ends.x, ends.y)
	var z1 := maxf(ends.x, ends.y)
	var rng := RandomNumberGenerator.new()
	rng.seed = 4242
	var z := z0
	while z <= z1:
		var cx := terrain.channel_center_x(z)
		for side in [-1.0, 1.0]:
			for alt: float in ALTITUDES:
				var zz := z + rng.randf_range(-0.3, 0.3) * SPACING_M
				var d := SEARCH_STEP_M
				while d <= SEARCH_MAX_M:
					if terrain.get_height_at(cx + side * d, zz) >= alt:
						out.append(Vector3(cx + side * (d - STAND_OFF_M), alt, zz))
						break
					d += SEARCH_STEP_M
		z += SPACING_M
	return out


## Show `mist_amount` (0-1) of mist from `base_m` up, lit `light`
## (scene-linear), drifting with `wind_ms`.
func update(base_m: float, mist_amount: float, light: Color, wind_ms: float, delta: float) -> void:
	amount = clampf(mist_amount, 0.0, 1.0) if base_m > 0.0 else 0.0
	cloud_base_m = base_m
	visible = built and amount > MIN_AMOUNT
	if not visible:
		return
	_drift += wind_ms * DRIFT_SHARE * delta
	_material.set_shader_parameter("amount", amount)
	_material.set_shader_parameter("cloud_base", base_m)
	_material.set_shader_parameter("mist_light", Vector3(light.r, light.g, light.b))
	_material.set_shader_parameter("drift", _drift)
