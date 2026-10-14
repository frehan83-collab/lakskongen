
extends Control

class_name RegulationPanel

@onready var _simulation_core = get_node_or_null("/root/SimulationCore")

# Assume LakskongenTheme is an autoload or globally available class
# class_name LakskongenTheme is for the theme itself, not this panel

var _traffic_light_sections: Array[HBoxContainer] = []
var _esg_stats_sections: Array[HBoxContainer] = []
var _esg_certification_label: Label
var _esg_progress_label: Label
var _esg_overall_score_label: Label

func _init():
    custom_minimum_size = Vector2(800, 600) # Example size
    size_flags_horizontal = Control.SIZE_EXPAND_FILL
    size_flags_vertical = Control.SIZE_EXPAND_FILL

    var main_hbox = HBoxContainer.new()
    add_child(main_hbox)
    main_hbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    main_hbox.size_flags_vertical = Control.SIZE_EXPAND_FILL

    # Left Side: Traffic Light System
    var traffic_light_vbox = VBoxContainer.new()
    main_hbox.add_child(traffic_light_vbox)
    traffic_light_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    traffic_light_vbox.size_flags_vertical = Control.SIZE_EXPAND_FILL
    traffic_light_vbox.add_child(LakskongenTheme.create_section_header("🚦 TRAFFIC LIGHT SYSTEM"))
    traffic_light_vbox.add_child(LakskongenTheme.create_divider(LakskongenTheme.PANEL))

    # Placeholder for 13 production areas
    for i in range(13):
        var area_hbox = HBoxContainer.new()
        area_hbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)
        area_hbox.alignment = BoxContainer.ALIGNMENT_CENTER
        _traffic_light_sections.append(area_hbox) # Store to update later
        traffic_light_vbox.add_child(area_hbox)
        traffic_light_vbox.add_child(LakskongenTheme.create_divider(LakskongenTheme.MUTED))

    var explanation_label = LakskongenTheme.create_label("Grønn = +6% vekst | Gul = Status quo | Rød = -6% kutt", LakskongenTheme.FONT_SM, LakskongenTheme.MUTED)
    explanation_label.set_anchors_and_offsets_preset(Control.PRESET_CENTER_BOTTOM)
    traffic_light_vbox.add_child(explanation_label)
    explanation_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    explanation_label.set_h_size_flags(Control.SIZE_SHRINK_CENTER)


    # Right Side: ESG Dashboard
    var esg_vbox = VBoxContainer.new()
    main_hbox.add_child(esg_vbox)
    esg_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    esg_vbox.size_flags_vertical = Control.SIZE_EXPAND_FILL
    esg_vbox.add_child(LakskongenTheme.create_section_header("🌍 ESG & SUSTAINABILITY"))
    esg_vbox.add_child(LakskongenTheme.create_divider(LakskongenTheme.PANEL))

    _esg_certification_label = LakskongenTheme.create_label("[CurrentCertification]", LakskongenTheme.FONT_LG, LakskongenTheme.MUTED)
    esg_vbox.add_child(_esg_certification_label)
    _esg_progress_label = LakskongenTheme.create_label("Progress: [X]%", LakskongenTheme.FONT_MD, LakskongenTheme.WARNING)
    esg_vbox.add_child(_esg_progress_label)
    _esg_progress_label.visible = false # Hide initially

    esg_vbox.add_child(LakskongenTheme.create_divider(LakskongenTheme.PANEL))

    # Stat rows
    _esg_stats_sections.append(LakskongenTheme.create_stat("🌿 Carbon Footprint:", "0.0 kg CO2e/kg", LakskongenTheme.TEXT))
    _esg_stats_sections.append(LakskongenTheme.create_stat("💊 Antibiotic Use:", "0.0 mg/kg", LakskongenTheme.TEXT))
    _esg_stats_sections.append(LakskongenTheme.create_stat("🐟 Fish Welfare:", "0/100", LakskongenTheme.TEXT))
    _esg_stats_sections.append(LakskongenTheme.create_stat("🌾 Feed Sustainability:", "0/100", LakskongenTheme.TEXT))
    _esg_stats_sections.append(LakskongenTheme.create_stat("🏃 Escape Risk:", "0%", LakskongenTheme.TEXT))

    for stat_row in _esg_stats_sections:
        esg_vbox.add_child(stat_row)

    esg_vbox.add_child(LakskongenTheme.create_divider(LakskongenTheme.PANEL))

    var overall_score_vbox = VBoxContainer.new()
    overall_score_vbox.add_child(LakskongenTheme.create_label("Overall ESG Score:", LakskongenTheme.FONT_LG, LakskongenTheme.TEXT))
    _esg_overall_score_label = LakskongenTheme.create_label("0", LakskongenTheme.FONT_XXL, LakskongenTheme.MUTED)
    _esg_overall_score_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    overall_score_vbox.add_child(_esg_overall_score_label)
    esg_vbox.add_child(overall_score_vbox)

func _ready():
    if _simulation_core:
        _simulation_core.DayTicked.connect(_refresh)
    _refresh()

func _refresh():
    if not _simulation_core:
        return

    # Refresh Traffic Light System
    for i in range(13):
        var area_data = _simulation_core.TrafficLight.Areas[i] # Assuming Areas is a C# List/Array
        var status = area_data.Status # Assuming Status is an enum or string (Green, Yellow, Red)
        var name = area_data.Name
        var lice_percentage = area_data.LicePercentage

        var color_rect = ColorRect.new()
        color_rect.custom_minimum_size = Vector2(20, 20)
        color_rect.set_h_size_flags(Control.SIZE_SHRINK_BEGIN)
        match status:
            "Green":
                color_rect.color = LakskongenTheme.SUCCESS
            "Yellow":
                color_rect.color = LakskongenTheme.WARNING
            "Red":
                color_rect.color = LakskongenTheme.DANGER
            _:
                color_rect.color = LakskongenTheme.MUTED # Default or error color

        var name_label = LakskongenTheme.create_label("PO%d — %s" % [i+1, name], LakskongenTheme.FONT_SM, LakskongenTheme.TEXT)
        name_label.set_h_size_flags(Control.SIZE_EXPAND_FILL)

        var status_label = LakskongenTheme.create_label(status, LakskongenTheme.FONT_SM, color_rect.color)
        status_label.set_h_size_flags(Control.SIZE_SHRINK_CENTER)

        var lice_label = LakskongenTheme.create_label("Lice: %.1f%%" % lice_percentage, LakskongenTheme.FONT_SM, LakskongenTheme.MUTED)
        lice_label.set_h_size_flags(Control.SIZE_SHRINK_END)

        # Clear existing children and add new ones
        for child in _traffic_light_sections[i].get_children():
            child.queue_free()
        _traffic_light_sections[i].add_child(color_rect)
        _traffic_light_sections[i].add_child(name_label)
        _traffic_light_sections[i].add_child(status_label)
        _traffic_light_sections[i].add_child(lice_label)

    # Refresh ESG Dashboard
    var esg_tracker = _simulation_core.ESGTracker
    var current_certification = esg_tracker.CurrentCertification # None, InProgress, ASC
    var esg_progress = esg_tracker.Progress # e.g., 0.75 for 75%
    var carbon_footprint = esg_tracker.CarbonFootprint
    var antibiotic_use = esg_tracker.AntibioticUse
    var fish_welfare = esg_tracker.FishWelfare
    var feed_sustainability = esg_tracker.FeedSustainability
    var escape_risk = esg_tracker.EscapeRisk
    var overall_esg_score = esg_tracker.OverallESGScore

    var cert_color = LakskongenTheme.MUTED
    match current_certification:
        "InProgress":
            cert_color = LakskongenTheme.WARNING
            _esg_progress_label.visible = true
            _esg_progress_label.text = "Progress: %.0f%%" % (esg_progress * 100)
        "ASC":
            cert_color = LakskongenTheme.SUCCESS
            _esg_progress_label.visible = false
        _: # None
            cert_color = LakskongenTheme.MUTED
            _esg_progress_label.visible = false

    _esg_certification_label.text = current_certification
    LakskongenTheme.set_text_color(_esg_certification_label, cert_color)

    # Update stat rows
    # Carbon Footprint
    var cf_color = LakskongenTheme.TEXT
    if carbon_footprint < 3.5:
        cf_color = LakskongenTheme.SUCCESS
    elif carbon_footprint > 5.0:
        cf_color = LakskongenTheme.DANGER
    _esg_stats_sections[0].get_child(1).text = "%.1f kg CO2e/kg" % carbon_footprint
    LakskongenTheme.set_text_color(_esg_stats_sections[0].get_child(1), cf_color)

    # Antibiotic Use
    var au_color = LakskongenTheme.TEXT
    if antibiotic_use < 0.1:
        au_color = LakskongenTheme.SUCCESS
    elif antibiotic_use > 1.0:
        au_color = LakskongenTheme.DANGER
    _esg_stats_sections[1].get_child(1).text = "%.1f mg/kg" % antibiotic_use
    LakskongenTheme.set_text_color(_esg_stats_sections[1].get_child(1), au_color)

    # Fish Welfare
    var fw_color = LakskongenTheme.TEXT
    if fish_welfare > 70:
        fw_color = LakskongenTheme.SUCCESS
    elif fish_welfare < 40:
        fw_color = LakskongenTheme.DANGER
    _esg_stats_sections[2].get_child(1).text = "%d/100" % fish_welfare
    LakskongenTheme.set_text_color(_esg_stats_sections[2].get_child(1), fw_color)

    # Feed Sustainability
    # Assuming Feed Sustainability has similar color coding as others
    var fs_color = LakskongenTheme.TEXT
    if feed_sustainability > 70: # Example threshold
        fs_color = LakskongenTheme.SUCCESS
    elif feed_sustainability < 40: # Example threshold
        fs_color = LakskongenTheme.DANGER
    _esg_stats_sections[3].get_child(1).text = "%d/100" % feed_sustainability
    LakskongenTheme.set_text_color(_esg_stats_sections[3].get_child(1), fs_color)

    # Escape Risk
    var er_color = LakskongenTheme.TEXT
    if escape_risk < 5.0:
        er_color = LakskongenTheme.SUCCESS
    elif escape_risk > 20.0:
        er_color = LakskongenTheme.DANGER
    _esg_stats_sections[4].get_child(1).text = "%.0f%%" % escape_risk
    LakskongenTheme.set_text_color(_esg_stats_sections[4].get_child(1), er_color)

    # Overall ESG Score
    var overall_esg_color = LakskongenTheme.MUTED
    if overall_esg_score > 70:
        overall_esg_color = LakskongenTheme.SUCCESS
    elif overall_esg_score >= 40:
        overall_esg_color = LakskongenTheme.WARNING
    else:
        overall_esg_color = LakskongenTheme.DANGER
    _esg_overall_score_label.text = str(overall_esg_score)
    LakskongenTheme.set_text_color(_esg_overall_score_label, overall_esg_color)

func _input(event: InputEvent) -> void:
    # Only while shown: hidden, this used to swallow every ESC.
    if is_visible_in_tree() and event.is_action_pressed("ui_cancel"):
        get_viewport().set_input_as_handled()
        _on_close_requested()

func _on_close_requested() -> void:
    hide()

