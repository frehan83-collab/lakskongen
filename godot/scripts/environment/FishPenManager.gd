class_name FishPenManager
extends Node3D

## Manages fish pen markers in the 3D fjord environment.
## Places pens along the fjord channel and handles visual indicators.

# === Configuration ===
@export_group("Pen Placement")
@export var max_pens: int = 12
@export var pen_spacing: float = 150.0  # Meters between pens
@export var min_depth: float = 15.0      # Minimum water depth for pen placement
@export var max_depth: float = 100.0     # Maximum water depth

# === Internal State ===
var _pens: Dictionary = {}  # pen_id -> {position: Vector3, active: bool, node: Node3D}
var _terrain_ref: FjordTerrain = null
var _recommended_locations: Array[Vector3] = []

# === Node References ===
@onready var markers_parent: Node3D = $PenMarkers
@onready var visualization: MeshInstance3D = $FishPenVisualization
@onready var active_indicator: MeshInstance3D = $ActivePenIndicator

# === Signals ===
signal pen_added(pen_id: String, position: Vector3)
signal pen_removed(pen_id: String)
signal pen_clicked(pen_id: String, position: Vector3)

func _ready() -> void:
    # Hide editor visualization
    if visualization:
        visualization.visible = false
    if active_indicator:
        active_indicator.visible = false
    
    print("[FishPenManager] Ready for pen placement")


func setup_pens(terrain: FjordTerrain) -> void:
    """Initialize pen manager with terrain reference."""
    _terrain_ref = terrain
    _generate_recommended_locations()
    
    # Place default pens at recommended locations
    for i in range(min(max_pens, _recommended_locations.size())):
        var pen_id = "pen_%d" % (i + 1)
        var pos = _recommended_locations[i]
        _add_pen_internal(pen_id, pos, false)
    
    print("[FishPenManager] Setup complete — %d pens ready" % _pens.size())


func _generate_recommended_locations() -> void:
    """Generate recommended pen locations along the fjord channel."""
    _recommended_locations.clear()
    
    if not _terrain_ref:
        # Fallback: place pens in a line through center
        for i in range(max_pens):
            var z = (i - max_pens / 2.0) * pen_spacing
            _recommended_locations.append(Vector3(0, -5, z))
        return
    
    # Generate locations based on fjord shape
    var fjord_length = _terrain_ref.terrain_size.y
    var start_z = -fjord_length * 0.4
    
    for i in range(max_pens):
        # Create pens along the winding fjord centerline
        var progress = float(i) / float(max_pens - 1)
        var z = start_z + progress * fjord_length * 0.8
        
        # Add some variation to x position (follow fjord bends)
        var fjord_center_x = sin(progress * 4.0) * 50.0
        
        # Try different x positions to find deep water
        var best_pos = Vector3(fjord_center_x, 0, z)
        var best_depth = -999.0
        
        for offset in [-100, -50, 0, 50, 100]:
            var test_x = fjord_center_x + offset
            var height = _terrain_ref.get_height_at(test_x, z)
            var depth = -height  # Negative height = underwater
            
            if depth > min_depth and depth < max_depth and depth > best_depth:
                best_depth = depth
                best_pos = Vector3(test_x, -depth * 0.3, z)  # Slightly submerged
        
        _recommended_locations.append(best_pos)
    
    print("[FishPenManager] Generated %d recommended locations" % _recommended_locations.size())


func _add_pen_internal(pen_id: String, position: Vector3, active: bool) -> void:
    """Internal method to add a pen marker."""
    
    # Create marker node
    var marker = Marker3D.new()
    marker.name = pen_id
    marker.position = position
    
    # Add visual representation (floating ring)
    var ring = MeshInstance3D.new()
    ring.name = "Ring"
    ring.mesh = _create_ring_mesh()
    ring.position.y = 0.5
    marker.add_child(ring)
    
    # Add label sprite
    var label = Label3D.new()
    label.name = "Label"
    label.text = pen_id.replace("pen_", "Pen ")
    label.position.y = 3.0
    label.font_size = 64
    label.modulate = Color(1, 1, 1, 0.9)
    marker.add_child(label)
    
    # Add interaction area
    var area = Area3D.new()
    area.name = "ClickArea"
    var collision = CollisionShape3D.new()
    var sphere_shape = SphereShape3D.new()
    sphere_shape.radius = 15.0
    collision.shape = sphere_shape
    area.add_child(collision)
    area.input_event.connect(_on_pen_input_event.bind(pen_id))
    marker.add_child(area)
    
    # Add to scene
    markers_parent.add_child(marker)
    
    # Store pen data
    _pens[pen_id] = {
        "position": position,
        "active": active,
        "node": marker,
        "depth": -_terrain_ref.get_height_at(position.x, position.z) if _terrain_ref else 30.0
    }


func _create_ring_mesh() -> Mesh:
    """Create a torus mesh for pen visualization."""
    var torus = TorusMesh.new()
    torus.inner_radius = 12.0
    torus.outer_radius = 14.0
    
    var material = StandardMaterial3D.new()
    material.albedo_color = Color(0.9, 0.6, 0.2, 0.6)
    material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
    material.cull_mode = BaseMaterial3D.CULL_DISABLED
    torus.material = material
    
    return torus


func _on_pen_input_event(camera: Node, event: InputEvent, position: Vector3, normal: Vector3, shape_idx: int, pen_id: String) -> void:
    """Handle click events on pen markers."""
    if event is InputEventMouseButton:
        if event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
            pen_clicked.emit(pen_id, _pens[pen_id].position)
            _highlight_pen(pen_id)


func _highlight_pen(pen_id: String) -> void:
    """Visually highlight the selected pen."""
    # Reset all pens
    for id in _pens:
        var pen_data = _pens[id]
        var ring = pen_data.node.get_node("Ring")
        if ring:
            ring.mesh.material.albedo_color = Color(0.9, 0.6, 0.2, 0.6)
    
    # Highlight selected
    if pen_id in _pens:
        var selected = _pens[pen_id]
        var ring = selected.node.get_node("Ring")
        if ring:
            ring.mesh.material.albedo_color = Color(0.2, 0.9, 0.3, 0.8)


# === Public API ===

func add_pen(pen_id: String, position: Vector3) -> bool:
    """Add a new pen at the specified position."""
    if _pens.size() >= max_pens:
        push_warning("[FishPenManager] Maximum pens (%d) reached" % max_pens)
        return false
    
    if pen_id in _pens:
        push_warning("[FishPenManager] Pen %s already exists" % pen_id)
        return false
    
    _add_pen_internal(pen_id, position, true)
    pen_added.emit(pen_id, position)
    
    print("[FishPenManager] Added pen %s at %s" % [pen_id, position])
    return true


func remove_pen(pen_id: String) -> bool:
    """Remove a pen from the scene."""
    if not pen_id in _pens:
        return false
    
    var pen_data = _pens[pen_id]
    pen_data.node.queue_free()
    _pens.erase(pen_id)
    
    pen_removed.emit(pen_id)
    print("[FishPenManager] Removed pen %s" % pen_id)
    return true


func get_pen_position(pen_id: String) -> Vector3:
    """Get the world position of a pen."""
    if pen_id in _pens:
        return _pens[pen_id].position
    return Vector3.ZERO


func get_active_pen_count() -> int:
    """Get number of active pens."""
    var count = 0
    for pen_data in _pens.values():
        if pen_data.active:
            count += 1
    return count


func get_pen_ids() -> Array[String]:
    """Get list of all pen IDs."""
    var ids: Array[String] = []
    for pen_id in _pens.keys():
        ids.append(pen_id)
    return ids


func get_recommended_locations() -> Array[Vector3]:
    """Get list of recommended pen locations."""
    return _recommended_locations.duplicate()


func get_available_slots() -> int:
    """Get number of available pen slots."""
    return max_pens - _pens.size()


func refresh_pen_positions() -> void:
    """Update pen positions after terrain regeneration."""
    if not _terrain_ref:
        return
    
    for pen_id in _pens:
        var pen_data = _pens[pen_id]
        var pos = pen_data.position
        var new_height = _terrain_ref.get_height_at(pos.x, pos.z)
        
        # Keep pen at appropriate depth
        if new_height > -5.0:  # Too shallow
            pen_data.node.position.y = -10.0
        else:
            pen_data.node.position.y = new_height * 0.3  # Float at ~30% depth
        
        pen_data.position = pen_data.node.position
        pen_data.depth = -new_height


func show_pen_visualization(pen_id: String = "") -> void:
    """Show visual indicator for pen placement preview."""
    if visualization:
        visualization.visible = true
        if pen_id and pen_id in _pens:
            visualization.position = _pens[pen_id].position


func hide_pen_visualization() -> void:
    """Hide pen placement preview."""
    if visualization:
        visualization.visible = false


func get_pen_info(pen_id: String) -> Dictionary:
    """Get detailed information about a pen."""
    if not pen_id in _pens:
        return {}
    
    var pen_data = _pens[pen_id]
    return {
        "id": pen_id,
        "position": pen_data.position,
        "depth": pen_data.depth,
        "active": pen_data.active
    }
