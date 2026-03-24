extends Node3D

## FjordTerrain3D — Integrates gd-retroterrain for Norwegian fjord landscape.
## Creates a terrain with water for the salmon farm view.

@onready var terrain: MeshInstance3D = $Terrain
@onready var water: Node3D = $WaterPlane
@onready var camera: Camera3D = $RTSCamera

func _ready() -> void:
	# Configure terrain for fjord look
	if terrain:
		terrain.size = Vector2i(64, 64)
		terrain.smooth = true
		terrain.grid = false
		if terrain.noise:
			terrain.noise.seed = 42  # Consistent seed for fjord
			terrain.noise.frequency = 0.02  # Gentler hills
		terrain.should_update = true
	
	# Position water at fjord level
	if water:
		water.position.y = -1.0
	
	print("FjordTerrain3D: ready")
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
