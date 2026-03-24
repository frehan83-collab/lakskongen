
extends CanvasLayer

var tooltip_panel: PanelContainer
var tooltip_label: RichTextLabel
var is_visible: bool = false

func _ready():
    # Opprett PanelContainer
    tooltip_panel = PanelContainer.new()
    tooltip_panel.set_anchors_and_offsets_preset(Control.PRESET_TOP_LEFT)
    tooltip_panel.set_custom_minimum_size(Vector2(0, 0)) # For å tillate innhold å bestemme størrelse
    tooltip_panel.set_max_size(Vector2(300, 0)) # Maks bredde

    # Lag en StyleBoxFlat for bakgrunn og border
    var stylebox_panel = StyleBoxFlat.new()
    stylebox_panel.bg_color = Color(0.1, 0.1, 0.1, 0.95) # Placeholder for LakskongenTheme.PANEL med alpha 0.95
    stylebox_panel.border_width_left = 1
    stylebox_panel.border_width_top = 1
    stylebox_panel.border_width_right = 1
    stylebox_panel.border_width_bottom = 1
    stylebox_panel.border_color = Color(0.8, 0.6, 0.2, 1.0) # Placeholder for LakskongenTheme.ACCENT
    stylebox_panel.set_corner_radius_all(4) # Slightly rounded corners
    tooltip_panel.add_theme_stylebox_override("panel", stylebox_panel)

    # Opprett RichTextLabel inne i panelet
    tooltip_label = RichTextLabel.new()
    tooltip_label.set_use_bbcode(true)
    tooltip_label.set_fit_content(true)
    tooltip_label.set_autowrap_mode(TextServer.AUTOWRAP_WORD_SMART)
    tooltip_label.set_structured_text_bidi_override(TextServer.STRUCTURED_TEXT_URI) # For å hindre at BBCode brytes

    # Placeholder for LakskongenTheme.FONT_SM og LakskongenTheme.TEXT
    var theme_font = FontVariation.new() # Using FontVariation for modern Godot
    # Assuming a default system font for now, replace with actual theme font later
    # Example: theme_font.base_font = preload("res://themes/LakskongenTheme.tres").get_font("body_small")
    # For now, just set a default font size
    tooltip_label.add_theme_font_size_override("normal_font_size", 14)
    tooltip_label.add_theme_color_override("default_color", Color(0.9, 0.9, 0.9, 1.0)) # Placeholder for LakskongenTheme.TEXT

    tooltip_panel.add_child(tooltip_label)
    add_child(tooltip_panel)

    # Start usynlig
    tooltip_panel.visible = false
    is_visible = false

func show_tooltip(text: String, bbcode: bool = false):
    tooltip_label.clear()
    if bbcode:
        tooltip_label.parse_bbcode(text)
    else:
        tooltip_label.text = text
    
    tooltip_panel.visible = true
    is_visible = true
    _update_tooltip_position()

func hide_tooltip():
    tooltip_panel.visible = false
    is_visible = false

func _process(_delta):
    if is_visible:
        _update_tooltip_position()

func _update_tooltip_position():
    var mouse_pos = get_viewport().get_mouse_position()
    var offset = Vector2(15, 15)
    var tooltip_size = tooltip_panel.get_size()
    var viewport_size = get_viewport().get_visible_rect().size

    var new_pos = mouse_pos + offset

    # Sørg for at tooltip ikke går utenfor skjermen (flytt til venstre/opp om nødvendig)
    if new_pos.x + tooltip_size.x > viewport_size.x:
        new_pos.x = mouse_pos.x - tooltip_size.x - offset.x # Flytt til venstre
    if new_pos.y + tooltip_size.y > viewport_size.y:
        new_pos.y = mouse_pos.y - tooltip_size.y - offset.y # Flytt opp

    tooltip_panel.position = new_pos

static func format_stat(label: String, value: String, unit: String = "") -> String:
    return "[b]%s:[/b] %s %s" % [label, value, unit]

static func format_fish_info(weight_kg: float, health: float, lice: float) -> String:
    var health_color = ""
    if health > 80.0:
        health_color = "green"
    elif health >= 50.0:
        health_color = "yellow"
    else:
        health_color = "red"
    
    var info_text = ""
    info_text += format_stat("Vekt", "%.2f" % weight_kg, "kg") + "\n"
    info_text += "[color=%s]%s:[/color] %.1f%%\n" % [health_color, "Helse", health]
    info_text += format_stat("Lus", "%.1f" % lice, "")
    return info_text
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
