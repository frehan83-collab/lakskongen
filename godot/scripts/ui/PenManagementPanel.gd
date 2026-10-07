
extends Control

signal delouse_requested(pen_id: int)
signal harvest_requested(pen_id: int)
signal sell_pen_requested(pen_id: int)
signal buy_pen_requested()

var _scroll_container: ScrollContainer
var _pens_vbox: VBoxContainer
var _overview_label: Label

func _ready():
    # Main VBoxContainer for the panel
    var main_vbox = VBoxContainer.new()
    main_vbox.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
    add_child(main_vbox)

    # Title
    var title_label = LakskongenTheme.create_label("MERDER & ANLEGG", LakskongenTheme.FONT_XL, LakskongenTheme.ACCENT)
    main_vbox.add_child(title_label)
    
    # Overview row
    _overview_label = LakskongenTheme.create_label("", LakskongenTheme.FONT_MD, LakskongenTheme.TEXT)
    main_vbox.add_child(_overview_label)

    # ScrollContainer for the list of pens
    _scroll_container = ScrollContainer.new()
    _scroll_container.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    _scroll_container.size_flags_vertical = Control.SIZE_EXPAND_FILL
    main_vbox.add_child(_scroll_container)

    _pens_vbox = VBoxContainer.new()
    _pens_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    _pens_vbox.add_theme_constant_override("separation", 15)
    _scroll_container.add_child(_pens_vbox)

    # Buy New Pen section
    var buy_pen_vbox = VBoxContainer.new()
    buy_pen_vbox.add_theme_constant_override("separation", 5)
    main_vbox.add_child(buy_pen_vbox)

    var buy_pen_button = LakskongenTheme.create_button("KJØP NY MERD", true, false)
    buy_pen_button.pressed.connect(_on_buy_pen_pressed.bind(buy_pen_button))
    buy_pen_vbox.add_child(buy_pen_button)

    var prices_label = LakskongenTheme.create_label("Priser: Tradisjonell 5M, Semi-Closed 15M, Fully Closed 40M, Offshore 200M", LakskongenTheme.FONT_SM, LakskongenTheme.TEXT)
    buy_pen_vbox.add_child(prices_label)


func update_pens(pens: Array):
    # Clear existing pens
    for child in _pens_vbox.get_children():
        child.queue_free()

    var total_pens = pens.size()
    var total_mab = 0.0
    var total_biomass = 0.0

    if pens.is_empty():
        var empty_lbl := LakskongenTheme.create_label(
            "🐟 Ingen merder ennå — kjøp din første merd nedenfor for å starte produksjonen.",
            LakskongenTheme.FONT_SM, LakskongenTheme.TEXT_SECONDARY)
        empty_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
        _pens_vbox.add_child(empty_lbl)

    for pen_data in pens:
        total_mab += pen_data["mab_limit_tons"]
        total_biomass += pen_data["biomass_tons"]

        var pen_panel = LakskongenTheme.create_styled_panel()
        pen_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
        _pens_vbox.add_child(pen_panel)

        var pen_vbox = VBoxContainer.new()
        pen_vbox.add_theme_constant_override("separation", 8)
        pen_panel.add_child(pen_vbox)

        # --- Row 1: Type, ID, Name, Region ---
        var row1_hbox = HBoxContainer.new()
        row1_hbox.add_theme_constant_override("separation", 10)
        pen_vbox.add_child(row1_hbox)

        var type_icon = ""
        match pen_data["type"]:
            "traditional": type_icon = "🏗️"
            "semi_closed": type_icon = "🛡️"
            "fully_closed": type_icon = "🔒"
            "offshore": type_icon = "🌊"

        row1_hbox.add_child(LakskongenTheme.create_label(type_icon, LakskongenTheme.FONT_MD, LakskongenTheme.TEXT))
        var pen_info_label = LakskongenTheme.create_label("Merd #%d (%s) - %s" % [pen_data["id"], pen_data["type"].capitalize(), pen_data["region"]], LakskongenTheme.FONT_MD, LakskongenTheme.TEXT)
        row1_hbox.add_child(pen_info_label)

        # --- Row 2: Stats ---
        var stats_grid = GridContainer.new()
        stats_grid.columns = 2
        stats_grid.add_theme_constant_override("h_separation", 20)
        stats_grid.add_theme_constant_override("v_separation", 5)
        pen_vbox.add_child(stats_grid)

        # Fish count
        var fish_capacity_pct = float(pen_data["fish_count"]) / pen_data["max_fish"]
        var fish_color = LakskongenTheme.SUCCESS if fish_capacity_pct < 0.8 else (LakskongenTheme.WARNING if fish_capacity_pct < 0.95 else LakskongenTheme.DANGER)
        stats_grid.add_child(LakskongenTheme.create_stat("Fisk", "%s / %s" % [LakskongenTheme.format_number(pen_data["fish_count"]), LakskongenTheme.format_number(pen_data["max_fish"])], fish_color))

        # Biomass
        stats_grid.add_child(LakskongenTheme.create_stat("Biomasse", "%.0f / %.0f tonn" % [pen_data["biomass_tons"], pen_data["mab_limit_tons"]], LakskongenTheme.TEXT))

        # Lice level
        var lice_color = LakskongenTheme.SUCCESS if pen_data["lice_level"] < 0.2 else (LakskongenTheme.WARNING if pen_data["lice_level"] < 0.5 else LakskongenTheme.DANGER)
        stats_grid.add_child(LakskongenTheme.create_stat("Lus", "%.2f" % pen_data["lice_level"], lice_color))

        # Temperature
        stats_grid.add_child(LakskongenTheme.create_stat("Temp", "%.1f°C" % pen_data["temperature_c"], LakskongenTheme.TEXT))

        # Health
        var health_color = LakskongenTheme.SUCCESS if pen_data["health_pct"] > 80.0 else (LakskongenTheme.WARNING if pen_data["health_pct"] > 50.0 else LakskongenTheme.DANGER)
        stats_grid.add_child(LakskongenTheme.create_stat("Helse", "%.0f%%" % pen_data["health_pct"], health_color))
        
        # --- Row 3: Actions ---
        var actions_hbox = HBoxContainer.new()
        actions_hbox.alignment = HBoxContainer.ALIGNMENT_END
        actions_hbox.add_theme_constant_override("separation", 10)
        pen_vbox.add_child(actions_hbox)

        var delouse_button = LakskongenTheme.create_button("AVLUSE", false, true)
        delouse_button.pressed.connect(_on_pen_action_button.bind("delouse", pen_data["id"], delouse_button))
        actions_hbox.add_child(delouse_button)

        var harvest_button = LakskongenTheme.create_button("HØSTE", true, false)
        harvest_button.pressed.connect(_on_pen_action_button.bind("harvest", pen_data["id"], harvest_button))
        actions_hbox.add_child(harvest_button)

        var sell_button = LakskongenTheme.create_button("SELG MERD", false, true)
        sell_button.pressed.connect(_on_pen_action_button.bind("sell", pen_data["id"], sell_button))
        actions_hbox.add_child(sell_button)

    # Update overview label
    var used_capacity_pct = 0.0
    if total_mab > 0:
        used_capacity_pct = (total_biomass / total_mab) * 100
    
    _overview_label.text = "Totalt antall merder: %s | Total MAB: %s tonn | Brukt kapasitet: %s%%" % [
        LakskongenTheme.format_number(total_pens),
        LakskongenTheme.format_number(total_mab, 0),
        LakskongenTheme.format_number(used_capacity_pct, 1)
    ]

# ── Juice-only feedback (signals preserved; no sim changes) ─────────────────

func _on_buy_pen_pressed(button: Button) -> void:
    buy_pen_requested.emit()
    if is_instance_valid(button):
        JuiceEffects.button_pop(button)
    _toast("🏗️ Ny merd bestilt — lykke til!", "SUCCESS")

func _on_pen_action_button(action: String, pen_id: int, button: Button) -> void:
    match action:
        "delouse":
            delouse_requested.emit(pen_id)
            _toast("🧪 Avlusing bestilt for merd %d" % pen_id, "INFO")
        "harvest":
            harvest_requested.emit(pen_id)
            _toast("📦 Høsting startet i merd %d 🎉" % pen_id, "SUCCESS")
        "sell":
            sell_pen_requested.emit(pen_id)
            _toast("💰 Salg av merd %d sendt" % pen_id, "INFO")
    if is_instance_valid(button):
        JuiceEffects.button_pop(button)

func _toast(message: String, level: String = "INFO") -> void:
    var mgr := get_node_or_null("/root/ToastNotification")
    if mgr and mgr.has_method("show_toast"):
        mgr.show_toast(message, level, 4.0)

func _input(event: InputEvent) -> void:
    if event.is_action_pressed("ui_cancel"):
        get_viewport().set_input_as_handled()
        _on_close_requested()

func _on_close_requested() -> void:
    hide()

