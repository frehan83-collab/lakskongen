
extends Control

signal region_selected(region_name: String)

# class_name LakskongenTheme # LakskongenTheme is already a global class_name, no need to preload

const LAND_DARK := Color(0.13, 0.20, 0.24)
const COAST_LIGHT := Color(0.75, 0.85, 0.90, 0.9)

var region_data = {
    "Rogaland": {"pos": Vector2(0.028, 0.829), "color_name": "green"},
    "Hordaland": {"pos": Vector2(0.014, 0.734), "color_name": "green"},
    "Sogn og Fjordane": {"pos": Vector2(0.003, 0.652), "color_name": "yellow"},
    "Møre og Romsdal": {"pos": Vector2(0.044, 0.592), "color_name": "yellow"},
    "Trøndelag": {"pos": Vector2(0.195, 0.527), "color_name": "green"},
    "Nordland": {"pos": Vector2(0.337, 0.264), "color_name": "red"},
    "Troms": {"pos": Vector2(0.5, 0.102), "color_name": "yellow"},
    "Finnmark": {"pos": Vector2(0.668, 0.033), "color_name": "red"},
}

var player_regions: Array = []
var font_sm: Font = ThemeDB.fallback_font
var text_color: Color = Color.WHITE

func _ready():
    custom_minimum_size = Vector2(250, 400)
    text_color = LakskongenTheme.TEXT

func _draw():
    var size = get_size()

    # 1. Background: Dark blue sea
    draw_rect(Rect2(Vector2.ZERO, size), Color(0.05, 0.1, 0.2), true)

    # 2. Real coastline (shared loader): land fill + crisp edge.
    NorwayCoastline.draw_landmass(self, size, LAND_DARK)
    for ring in NorwayCoastline.rings():
        var poly := PackedVector2Array()
        for p in ring:
            var a: Array = p
            poly.append(Vector2(float(a[0]) * size.x, float(a[1]) * size.y))
        if poly.size() < 2:
            continue
        if NorwayCoastline.is_open_ring(ring):
            draw_polyline(poly, COAST_LIGHT, 1.5)
        else:
            draw_polyline(poly + PackedVector2Array([poly[0]]), COAST_LIGHT, 1.5)

    # 3. Draw region points and names
    for region_name in region_data:
        var data = region_data[region_name]
        var scaled_pos = data.pos * size
        var color = get_traffic_light_color(data.color_name)
        
        # Draw region circle
        draw_circle(scaled_pos, 8.0, color)
        
        # Draw region name label
        if font_sm != null:
            var text_pos = scaled_pos + Vector2(12, -font_sm.get_height() / 2)
            draw_string(font_sm, text_pos, region_name, HORIZONTAL_ALIGNMENT_LEFT, -1, LakskongenTheme.FONT_SM, text_color)
        
        # 4. Draw larger ring for player's active regions
        if player_regions.has(region_name):
            draw_arc(scaled_pos, 12.0, 0, PI * 2, 32, Color.WHITE, 2.0)

func set_traffic_light(region: String, color: String):
    if region_data.has(region):
        region_data[region].color_name = color
        queue_redraw()

func set_player_regions(regions: Array):
    player_regions = regions
    queue_redraw()

func _gui_input(event):
    if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
        var mouse_pos = event.position
        var size = get_size()
        
        for region_name in region_data:
            var data = region_data[region_name]
            var scaled_pos = data.pos * size
            
            # Check if click is within region circle (8px radius)
            if mouse_pos.distance_to(scaled_pos) <= 8.0:
                emit_signal("region_selected", region_name)
                accept_event()
                return

func get_traffic_light_color(color_name: String) -> Color:
    match color_name:
        "green":
            return LakskongenTheme.SUCCESS
        "yellow":
            return LakskongenTheme.WARNING
        "red":
            return LakskongenTheme.DANGER
        _:
            return Color.WHITE
