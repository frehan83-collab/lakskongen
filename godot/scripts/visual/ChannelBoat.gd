extends RefCounted
## A boat that sails a route along the fjord channel at sea level
## (FjordTerrain.channel_route), shared by the wellboat and feed-barge
## managers. Load it with preload: it is not a global class.


## Builds parent > Path3D > PathFollow3D > boat along `route`, the boat at
## the route's start. Returns {path, follow, boat}.
static func launch(parent: Node, scene: PackedScene, route: PackedVector3Array, boat_name: String) -> Dictionary:
	var curve := Curve3D.new()
	for point in route:
		curve.add_point(point)
	var path := Path3D.new()
	path.name = "%sPath" % boat_name
	path.curve = curve
	var follow := PathFollow3D.new()
	follow.name = "Follow"
	follow.rotation_mode = PathFollow3D.ROTATION_Y
	follow.loop = false  # a route has two ends: clamp at them, never wrap round
	path.add_child(follow)
	var boat := scene.instantiate() as Node3D
	boat.name = boat_name
	follow.add_child(boat)
	parent.add_child(path)
	return {"path": path, "follow": follow, "boat": boat}


## Turns the boat to face back along the route for the trip home.
static func face_home(boat: Node3D) -> void:
	boat.rotation.y = PI


## Tints a copy of the boat's deck ShaderMaterial: every boat shares the
## scene's material. False when the scene has no tintable Deck.
static func tint_deck(boat: Node3D, colour: Color) -> bool:
	var deck := boat.get_node_or_null("Deck") as MeshInstance3D
	if deck == null or not (deck.material_override is ShaderMaterial):
		return false
	var mat := deck.material_override.duplicate() as ShaderMaterial
	mat.set_shader_parameter("albedo_color", colour)
	deck.material_override = mat
	return true
