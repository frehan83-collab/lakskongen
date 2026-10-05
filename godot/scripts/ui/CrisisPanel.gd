extends CanvasLayer
class_name CrisisPanel

# CrisisPanel - Dramatic Modal for Crisis Responses
# Design: Full-screen dark overlay with pulsing border and urgent typography
# Uses LakskongenTheme Nordlys palette: DANGER = #E54D4D, ACCENT = #E8825C

@onready var overlay: ColorRect = $Overlay
@onready var panel: PanelContainer = $PanelContainer
@onready var title_label: Label = $PanelContainer/VBoxContainer/Title
@onready var desc_label: RichTextLabel = $PanelContainer/VBoxContainer/Description
@onready var severity_indicator: ColorRect = $PanelContainer/VBoxContainer/SeverityIndicator
@onready var options_container: VBoxContainer = $PanelContainer/VBoxContainer/Options
@onready var timer_label: Label = $PanelContainer/VBoxContainer/Timer
@onready var advisor_hint: Label = $PanelContainer/VBoxContainer/AdvisorHint
@onready var animation_player: AnimationPlayer = $AnimationPlayer

var current_crisis_id: String = ""
var crisis_manager: Node = null
var _initialized: bool = false

func _ready():
    # Wait for autoloads
    if not Engine.has_singleton("CrisisResponseManager"):
        return
    crisis_manager = get_node("/root/CrisisResponseManager")
    crisis_manager.crisis_triggered.connect(_on_crisis_triggered)
    crisis_manager.crisis_resolved.connect(_on_crisis_resolved)
    crisis_manager.crisis_escalated.connect(_on_crisis_escalated)
    
    # Setup theme
    _setup_appearance()
    hide()
    _initialized = true

func _setup_appearance():
    # Dark semi-transparent overlay
    overlay.color = LakskongenTheme.PANEL
    overlay.color.a = 0.85
    
    # Panel styling
    var panel_style = StyleBoxFlat.new()
    panel_style.bg_color = LakskongenTheme.PANEL.darkened(0.1)
    panel_style.border_color = LakskongenTheme.DANGER
    panel_style.border_width_left = 3
    panel_style.border_width_right = 3
    panel_style.border_width_top = 3
    panel_style.border_width_bottom = 3
    panel_style.corner_radius_top_left = 16
    panel_style.corner_radius_top_right = 16
    panel_style.corner_radius_bottom_left = 16
    panel_style.corner_radius_bottom_right = 16
    panel_style.content_margin_left = 30
    panel_style.content_margin_right = 30
    panel_style.content_margin_top = 30
    panel_style.content_margin_bottom = 30
    panel.add_theme_stylebox_override("panel", panel_style)
    
    title_label.add_theme_font_size_override("font_size", LakskongenTheme.FONT_TITLE)
    title_label.add_theme_color_override("font_color", LakskongenTheme.DANGER)
    
    desc_label.bbcode_enabled = true
    desc_label.add_theme_font_size_override("font_size", LakskongenTheme.FONT_LG)
    desc_label.add_theme_color_override("default_color", LakskongenTheme.MUTED)
    
    timer_label.add_theme_font_size_override("font_size", LakskongenTheme.FONT_XL)
    timer_label.add_theme_color_override("font_color", LakskongenTheme.DANGER)
    
    advisor_hint.add_theme_font_size_override("font_size", LakskongenTheme.FONT_SM)
    advisor_hint.add_theme_color_override("font_color", LakskongenTheme.WARNING)
    
    # Create warning animation
    var animation = Animation.new()
    animation.length = 1.5
    animation.loop_mode = Animation.LOOP_LINEAR
    
    # Border pulse
    var border_track_idx = animation.add_track(Animation.TYPE_VALUE)
    animation.track_set_path(border_track_idx, "PanelContainer:theme_override_styles/panel:border_color")
    animation.track_insert_key(border_track_idx, 0.0, LakskongenTheme.DANGER)
    animation.track_insert_key(border_track_idx, 0.75, LakskongenTheme.DANGER.darkened(0.3))
    animation.track_insert_key(border_track_idx, 1.5, LakskongenTheme.DANGER)
    
    # Scale pulse
    var scale_track_idx = animation.add_track(Animation.TYPE_VALUE)
    animation.track_set_path(scale_track_idx, "PanelContainer:scale")
    animation.track_insert_key(scale_track_idx, 0.0, Vector2(1.0, 1.0))
    animation.track_insert_key(scale_track_idx, 0.75, Vector2(1.02, 1.02))
    animation.track_insert_key(scale_track_idx, 1.5, Vector2(1.0, 1.0))
    
    animation_player.add_animation("crisis_pulse", animation)

func _on_crisis_triggered(crisis_data: Dictionary):
    if not _initialized:
        return
    
    current_crisis_id = crisis_data["id"]
    
    # Title with icon based on crisis type
    var crisis_type = crisis_data["type"]
    var emoji = _get_crisis_emoji(crisis_type)
    title_label.text = emoji + " " + crisis_data["title"]
    
    # Enhanced description with formatting
    desc_label.text = "[color=#" + LakskongenTheme.TEXT.to_html() + "]" + crisis_data["description"] + "[/color]"
    
    # Update severity indicator
    var severity = crisis_data.get("severity", 1)
    severity_indicator.color = _get_severity_color(severity)
    
    # Clear previous options
    for child in options_container.get_children():
        child.queue_free()
    
    # Create response buttons
    var responses = crisis_data["responses"]
    for i in range(responses.size()):
        var response = responses[i]
        var btn = _create_response_button(response, i)
        options_container.add_child(btn)
    
    # Show advisor hint based on crisis phase
    var phase = crisis_data.get("phase", "Warning")
    if phase == "Warning":
        advisor_hint.text = tr("[i]Advisor: You have 3 days to respond before escalation.[/i]")
    elif phase == "Escalation":
        advisor_hint.text = tr("[i]Advisor: Immediate action required! Crisis worsening.[/i]")
    elif phase == "FullCrisis":
        advisor_hint.text = tr("[i]Advisor: CATASTROPHIC — Damage control only.[/i]")
    
    # Show and animate
    animation_player.play("crisis_pulse")
    show()
    
    # Play warning sound (placeholder)
    # AudioStreamPlayer.play() would go here

func _on_response_selected(response_index: int):
    if not _initialized or current_crisis_id.is_empty():
        return
    
    crisis_manager.respond_to_crisis(current_crisis_id, response_index)
    
    # Quick fade out
    var tween = create_tween()
    tween.tween_property(panel, "modulate", Color(1, 1, 1, 0), 0.3)
    tween.tween_callback(hide)
    
    # Reset panel for next crisis
    panel.modulate = Color.WHITE

func _on_crisis_resolved(crisis_id: String, success: bool):
    if current_crisis_id == crisis_id:
        hide()
        animation_player.stop()
    
    # Show resolution toast (could be implemented separately)
    GD.print("Crisis %s resolved: success=%s" % [crisis_id, success])

func _on_crisis_escalated(crisis_id: String, new_severity: int):
    if current_crisis_id == crisis_id:
        # Update UI for escalated crisis
        var crisis = crisis_manager.get_crisis(crisis_id)
        if crisis:
            _on_crisis_triggered(crisis)
        
        # Add shake effect for escalation
        var original_pos = panel.position
        var tween = create_tween()
        for i in range(6):
            var offset = Vector2(randf_range(-10, 10), randf_range(-5, 5))
            tween.tween_property(panel, "position", original_pos + offset, 0.05)
        tween.tween_property(panel, "position", original_pos, 0.1)

func _create_response_button(response_data: Dictionary, index: int) -> Button:
    var btn = Button.new()
    btn.text = response_data["label"]
    btn.tooltip_text = _format_response_tooltip(response_data)
    btn.custom_minimum_size = Vector2(500, 50)
    btn.add_theme_font_size_override("font_size", LakskongenTheme.FONT_LG)
    
    # Style based on effectiveness
    var effectiveness = response_data.get("effectiveness", 50)
    var btn_color = _get_effectiveness_color(effectiveness)
    
    var normal_style = StyleBoxFlat.new()
    normal_style.bg_color = btn_color
    normal_style.corner_radius_top_left = 8
    normal_style.corner_radius_top_right = 8
    normal_style.corner_radius_bottom_left = 8
    normal_style.corner_radius_bottom_right = 8
    normal_style.content_margin_left = 20
    normal_style.content_margin_right = 20
    normal_style.content_margin_top = 12
    normal_style.content_margin_bottom = 12
    normal_style.border_width_left = 2
    normal_style.border_width_right = 2
    normal_style.border_width_top = 2
    normal_style.border_width_bottom = 2
    normal_style.border_color = btn_color.darkened(0.3)
    
    btn.add_theme_stylebox_override("normal", normal_style)
    
    var hover_style = normal_style.duplicate()
    hover_style.bg_color = btn_color.lightened(0.15)
    btn.add_theme_stylebox_override("hover", hover_style)
    
    var pressed_style = normal_style.duplicate()
    pressed_style.bg_color = btn_color.darkened(0.15)
    btn.add_theme_stylebox_override("pressed", pressed_style)
    
    btn.add_theme_color_override("font_color", LakskongenTheme.TEXT)
    btn.add_theme_color_override("font_hover_color", LakskongenTheme.TEXT)
    btn.add_theme_color_override("font_pressed_color", LakskongenTheme.MUTED)
    
    btn.pressed.connect(_on_response_selected.bind(index))
    return btn

func _format_response_tooltip(response_data: Dictionary) -> String:
    var lines = []
    lines.append(response_data["description"])
    lines.append("")
    lines.append("Cost: " + LakskongenTheme.format_currency(response_data.get("cost", 0), false))
    lines.append("Effectiveness: " + str(response_data.get("effectiveness", 50)) + "%")
    lines.append("Time: " + str(response_data.get("time", 3)) + " days")
    return "\n".join(lines)

func _get_crisis_emoji(crisis_type: String) -> String:
    match crisis_type:
        "DiseaseOutbreak":
            return "🦠"
        "LiceEpidemic":
            return "🪱"
        "AlgalBloom":
            return "🌿"
        "JellyfishSwarm":
            return "🪼"
        "HurricaneStorm":
            return "🌀"
        "MassEscape":
            return "🏃‍♂️"
        "OxygenCrash":
            return "💨"
        _:
            return "⚠️"

func _get_severity_color(severity: int) -> Color:
    match severity:
        1, 2:
            return LakskongenTheme.WARNING
        3:
            return LakskongenTheme.DANGER
        4, 5:
            return Color("#8B0000")  # Dark red
        _:
            return LakskongenTheme.MUTED

func _get_effectiveness_color(effectiveness: int) -> Color:
    if effectiveness >= 80:
        return LakskongenTheme.KELP_LIGHT
    elif effectiveness >= 60:
        return LakskongenTheme.TEAL
    elif effectiveness >= 40:
        return LakskongenTheme.WARNING
    else:
        return LakskongenTheme.DANGER.darkened(0.2)

func _process(delta):
    if not _initialized or not visible or current_crisis_id.is_empty():
        return
    
    if crisis_manager.has_method("get_crisis_time_remaining"):
        var time_left = crisis_manager.get_crisis_time_remaining(current_crisis_id)
        
        if time_left <= 0:
            timer_label.text = tr("TIME EXPIRED")
        elif time_left == 1:
            timer_label.text = tr("FINAL DAY TO ACT")
            timer_label.add_theme_color_override("font_color", LakskongenTheme.DANGER)
        elif time_left <= 3:
            timer_label.text = "URGENT: %d DAYS REMAINING" % time_left
            timer_label.add_theme_color_override("font_color", LakskongenTheme.DANGER)
        else:
            timer_label.text = "ACT IN %d DAYS" % time_left
            timer_label.add_theme_color_override("font_color", LakskongenTheme.WARNING)
    else:
        timer_label.text = tr("TIME LIMITED")

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		_on_close_requested()

func _on_close_requested() -> void:
	hide()
