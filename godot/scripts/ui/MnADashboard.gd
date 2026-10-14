
extends Control

var simulation_core = null
var competitors_vbox = null

func _ready():
    name = "MnADashboard"
    %c
    # Setup the main UI container
    var main_vbox = VBoxContainer.new()
    add_child(main_vbox)
    main_vbox.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
    main_vbox.add_constant_override("separation", 10)

    # Title Label
    var title_label = Label.new()
    title_label.text = tr("M&A Dashboard - Competitors")
    LakskongenTheme.set_size(title_label, 24)
    title_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    main_vbox.add_child(title_label)

    # Competitors List Container
    competitors_vbox = VBoxContainer.new()
    competitors_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    competitors_vbox.add_constant_override("separation", 5)
    main_vbox.add_child(competitors_vbox)

    # Get SimulationCore reference
    simulation_core = get_node_or_null("/root/SimulationCore")
    if simulation_core == null:
        print("ERROR: SimulationCore not found at /root/SimulationCore")
        return
    %c
    _refresh_competitors_list()

func _refresh_competitors_list():
    if competitors_vbox == null:
        print("ERROR: competitors_vbox is null in _refresh_competitors_list")
        return

    # Clear existing entries
    for child in competitors_vbox.get_children():
        child.queue_free()

    if simulation_core == null:
        print("SimulationCore is null, cannot refresh competitors.")
        return
    
    # Assume SimulationCore.Competitors is accessible and iterable (e.g., a Godot.Collections.Array of AiCompany objects)
    # In a real C#/GDScript interop scenario, you might need to call a method on SimulationCore to get the competitors
    # For now, let's assume it's directly accessible and returns a C# array/list that GDScript can iterate.
    var competitors = simulation_core.get_competitors_data() # Assuming a method to get data

    if competitors:
        for competitor in competitors:
            var competitor_row = HBoxContainer.new()
            competitor_row.size_flags_horizontal = Control.SIZE_EXPAND_FILL
            competitors_vbox.add_child(competitor_row)

            # Company Name
            var name_label = Label.new()
            name_label.text = competitor.CompanyName
            name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
            name_label.clip_text = true
            competitor_row.add_child(name_label)

            # Balance
            var balance_label = Label.new()
            balance_label.text = "Balance: %s NOK" % [str(round(competitor.BalanceNoK))]
            balance_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
            balance_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
            competitor_row.add_child(balance_label)

            # Owned Pens
            var pens_label = Label.new()
            pens_label.text = "Pens: %s" % [str(competitor.OwnedPens)]
            pens_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
            pens_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
            competitor_row.add_child(pens_label)

            # Bankrupt status and Acquire Button
            var acquire_button = Button.new()
            acquire_button.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
            acquire_button.focus_mode = Control.FOCUS_NONE

            if competitor.IsBankrupt:
                LakskongenTheme.set_text_color(name_label, Color.RED)
                LakskongenTheme.set_text_color(balance_label, Color.RED)
                LakskongenTheme.set_text_color(pens_label, Color.RED)

                acquire_button.text = "ACQUIRE FOR %s NOK" % [str(round(competitor.AssetValueNoK))]
                acquire_button.disabled = false
                # Connect signal with arguments
                acquire_button.pressed.connect(_on_acquire_pressed.bind(competitor.CompanyName, competitor.AssetValueNoK))
            else:
                acquire_button.text = tr("NOT FOR SALE")
                acquire_button.disabled = true
            
            competitor_row.add_child(acquire_button)
    else:
        var no_competitors_label = Label.new()
        no_competitors_label.text = tr("No competitors found.")
        no_competitors_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
        competitors_vbox.add_child(no_competitors_label)

func _on_acquire_pressed(company_name, price):
    print("Attempting to acquire %s for %s NOK" % [company_name, str(price)])
    # In a real scenario, you would call a C# method on SimulationCore here
    # e.g., simulation_core.acquire_company(company_name)
    _refresh_competitors_list()

func _input(event: InputEvent) -> void:
    if event.is_action_pressed("ui_cancel"):
        get_viewport().set_input_as_handled()
        _on_close_requested()

func _on_close_requested() -> void:
    hide()

