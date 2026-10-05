
extends Control

var factory_option_button: OptionButton
var quality_option_button: OptionButton
var quantity_spinbox: SpinBox
var process_legally_button: Button
var smuggle_button: Button
var status_label: RichTextLabel

func _ready():
    # Set up the main container
    var main_vbox = VBoxContainer.new()
    add_child(main_vbox)
    main_vbox.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
    main_vbox.add_constant_override("separation", 10)

    # Factory Selection
    var factory_label = Label.new()
    factory_label.text = tr("Velg Fabrikk:")
    main_vbox.add_child(factory_label)

    factory_option_button = OptionButton.new()
    factory_option_button.add_item("Norge")
    factory_option_button.add_item("Polen")
    factory_option_button.connect("item_selected", Callable(self, "_on_factory_selected"))
    main_vbox.add_child(factory_option_button)

    # Fish Quality Selection
    var quality_label = Label.new()
    quality_label.text = tr("Velg Fiske-kvalitet:")
    main_vbox.add_child(quality_label)

    quality_option_button = OptionButton.new()
    quality_option_button.add_item("Superior")
    quality_option_button.add_item("Ordinary")
    quality_option_button.add_item("Production")
    quality_option_button.connect("item_selected", Callable(self, "_on_quality_selected"))
    main_vbox.add_child(quality_option_button)

    # Quantity Input
    var quantity_label = Label.new()
    quantity_label.text = tr("Mengde (Kg):")
    main_vbox.add_child(quantity_label)

    quantity_spinbox = SpinBox.new()
    quantity_spinbox.min_value = 0
    quantity_spinbox.max_value = 100000 # Example max value
    quantity_spinbox.value = 1000
    quantity_spinbox.step = 100
    main_vbox.add_child(quantity_spinbox)

    # Buttons Container
    var button_hbox = HBoxContainer.new()
    main_vbox.add_child(button_hbox)
    button_hbox.add_constant_override("separation", 10)

    # Process Legally Button
    process_legally_button = Button.new()
    process_legally_button.text = tr("Process Legally")
    process_legally_button.add_theme_color_override("font_color", Color("00ff00")) # Green color
    process_legally_button.connect("pressed", Callable(self, "_on_process_legally_pressed"))
    button_hbox.add_child(process_legally_button)

    # Smuggle Button
    smuggle_button = Button.new()
    smuggle_button.text = "Smuggle to Poland (15% Risk)"
    smuggle_button.add_theme_color_override("font_color", Color("ff0000")) # Red color
    smuggle_button.connect("pressed", Callable(self, "_on_smuggle_pressed"))
    button_hbox.add_child(smuggle_button)

    # Status/Log Area
    status_label = RichTextLabel.new()
    status_label.set_v_size_flags(Control.SIZE_EXPAND_FILL)
    status_label.set_h_size_flags(Control.SIZE_EXPAND_FILL)
    status_label.fit_content = true
    main_vbox.add_child(status_label)

    # Initial UI update
    _update_button_state()

func _on_factory_selected(index: int):
    _update_button_state()

func _on_quality_selected(index: int):
    _update_button_state()

func _update_button_state():
    var selected_factory = factory_option_button.get_item_text(factory_option_button.selected)
    var selected_quality = quality_option_button.get_item_text(quality_option_button.selected)

    var is_production_fish = (selected_quality == "Production")
    var is_poland_factory = (selected_factory == "Polen")

    process_legally_button.disabled = (is_production_fish and is_poland_factory)
    smuggle_button.visible = (is_production_fish and is_poland_factory)
    smuggle_button.disabled = not smuggle_button.visible # Disable if not visible

func _on_process_legally_pressed():
    var quantity = quantity_spinbox.value
    var factory = factory_option_button.get_item_text(factory_option_button.selected)
    var quality = quality_option_button.get_item_text(quality_option_button.selected)

    # Simulate legal processing
    var tariff = 0.0
    if factory == "Polen":
        tariff = 0.05 # Example 5% tariff for Poland
    status_label.append_text(tr("[color=green]Success! %s Kg %s fish processed legally at %s. %s%% Tariff applied.[/color]
") % [quantity, quality, factory, (tariff * 100)])

func _on_smuggle_pressed():
    var quantity = quantity_spinbox.value
    var smuggle_risk = randf()

    if smuggle_risk < 0.15: # 15% chance of getting caught
        var fine = 5000000 # Example fine
        status_label.append_text(tr("[color=red]CAUGHT! %s Kg fish smuggling attempt FAILED! %s NOK fine![/color]
") % [quantity, fine])
    else:
        status_label.append_text(tr("[color=green]Smuggling successful! %s Kg fish smuggled to Poland without incident.[/color]
") % [quantity])

func _input(event: InputEvent) -> void:
    if event.is_action_pressed("ui_cancel"):
        get_viewport().set_input_as_handled()
        _on_close_requested()

func _on_close_requested() -> void:
    hide()

