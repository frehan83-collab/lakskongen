
extends CanvasLayer

signal contract_accepted(contract_data: Dictionary)
signal contract_rejected()
signal contract_negotiated(contract_data: Dictionary, new_price: float)

const LAYER_POPUP = 60 # As per requirement

# Placeholder for theme constants. In a real Godot project, these would be loaded from a custom theme resource.
# For now, we'll use basic colors and font sizes.
const FONT_XL_SIZE = 32
const FONT_LG_SIZE = 24
const FONT_MD_SIZE = 18
const FONT_SM_SIZE = 14

const COLOR_ACCENT = Color("0088cc")
const COLOR_TEXT = Color("cccccc")
const COLOR_WARNING = Color("ffcc00")
const COLOR_DANGER = Color("ff0000")
const COLOR_SUCCESS = Color("00cc00")
const COLOR_NEGATIVE = Color("ff5555")

var _contract_data: Dictionary
var _current_negotiated_price: float
var _spot_price_per_kg: float = 80.0 # Placeholder: This should be dynamic in a real game

var _panel: PanelContainer
var _title_label: Label
var _buyer_info_vbox: VBoxContainer
var _negotiation_vbox: VBoxContainer
var _slider_price: HSlider
var _slider_price_value_label: Label
var _acceptance_chance_label: Label
var _buttons_hbox: HBoxContainer

func _ready() -> void:
    layer = LAYER_POPUP
    visible = false

    # Main panel
    _panel = PanelContainer.new()
    _panel.custom_minimum_size = Vector2(500, 450)
    add_child(_panel)
    _panel.set_anchors_and_offsets_preset(Control.PRESET_CENTER)

    var main_vbox = VBoxContainer.new()
    main_vbox.add_theme_constant_override("separation", 15)
    _panel.add_child(main_vbox)

    # Title
    _title_label = Label.new()
    _title_label.text = tr("UI_CDIALOG_TITLE")
    LakskongenTheme.set_size(_title_label, FONT_XL_SIZE)
    LakskongenTheme.set_text_color(_title_label, COLOR_ACCENT)
    _title_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    main_vbox.add_child(_title_label)

    # Divider
    var hsep_top = HSeparator.new()
    main_vbox.add_child(hsep_top)

    # Buyer Info Section
    _buyer_info_vbox = VBoxContainer.new()
    main_vbox.add_child(_buyer_info_vbox)
    
    var buyer_label = Label.new()
    LakskongenTheme.set_size(buyer_label, FONT_LG_SIZE)
    LakskongenTheme.set_text_color(buyer_label, COLOR_TEXT)
    _buyer_info_vbox.add_child(buyer_label)

    # Negotiation Section
    _negotiation_vbox = VBoxContainer.new()
    _negotiation_vbox.add_theme_constant_override("separation", 5)
    main_vbox.add_child(_negotiation_vbox)
    
    var counter_price_label = Label.new()
    counter_price_label.text = tr("UI_CDIALOG_COUNTER_PRICE")
    LakskongenTheme.set_text_color(counter_price_label, COLOR_TEXT)
    _negotiation_vbox.add_child(counter_price_label)

    _slider_price = HSlider.new()
    _slider_price.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    _slider_price.step = 0.5
    _slider_price.value_changed.connect(_on_slider_price_value_changed)
    _negotiation_vbox.add_child(_slider_price)

    _slider_price_value_label = Label.new()
    _slider_price_value_label.text = tr("UI_CDIALOG_PRICE_PLACEHOLDER")
    _slider_price_value_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    LakskongenTheme.set_text_color(_slider_price_value_label, COLOR_TEXT)
    _negotiation_vbox.add_child(_slider_price_value_label)

    _acceptance_chance_label = Label.new()
    _acceptance_chance_label.text = "Sjanse for aksept: X%"
    _acceptance_chance_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    LakskongenTheme.set_text_color(_acceptance_chance_label, COLOR_TEXT)
    _negotiation_vbox.add_child(_acceptance_chance_label)
    
    # Push buttons to bottom
    main_vbox.add_child(Control.new()) # Spacer

    # Buttons
    _buttons_hbox = HBoxContainer.new()
    _buttons_hbox.alignment = BoxContainer.ALIGNMENT_CENTER
    _buttons_hbox.add_theme_constant_override("separation", 10)
    main_vbox.add_child(_buttons_hbox)

    var accept_button = Button.new()
    accept_button.text = tr("UI_CDIALOG_ACCEPT")
    accept_button.theme_type_variation = &"PrimaryButton"
    accept_button.pressed.connect(_on_accept_button_pressed)
    _buttons_hbox.add_child(accept_button)

    var negotiate_button = Button.new()
    negotiate_button.text = tr("UI_CDIALOG_NEGOTIATE")
    negotiate_button.pressed.connect(_on_negotiate_button_pressed)
    _buttons_hbox.add_child(negotiate_button)

    var decline_button = Button.new()
    decline_button.text = tr("UI_CDIALOG_DECLINE")
    decline_button.theme_type_variation = &"GhostButton"
    decline_button.pressed.connect(_on_decline_button_pressed)
    _buttons_hbox.add_child(decline_button)

func show_contract(contract_data: Dictionary) -> void:
    _contract_data = contract_data
    visible = true

    # Clear previous buyer info stats
    for child in _buyer_info_vbox.get_children():
        if child != _buyer_info_vbox.get_child(0): # Keep the buyer_name label
            child.queue_free()

    # Update Buyer Info
    var buyer_name_label = _buyer_info_vbox.get_child(0) as Label
    buyer_name_label.text = "Kjøper: %s" % _contract_data.buyer_name
    
    create_stat("Volum:", str(_contract_data.volume_tons) + " tonn", COLOR_TEXT)
    
    var price_color = COLOR_TEXT
    if _contract_data.price_per_kg > _spot_price_per_kg * 1.05: # Offer significantly above spot price
        price_color = COLOR_SUCCESS
    elif _contract_data.price_per_kg < _spot_price_per_kg * 0.95: # Offer significantly below spot price
        price_color = COLOR_NEGATIVE
    create_stat("Tilbudt pris:", "%.2f NOK/kg" % _contract_data.price_per_kg, price_color)

    create_stat("Varighet:", str(_contract_data.duration_weeks) + " uker", COLOR_TEXT)
    create_stat("Kvalitetskrav:", _contract_data.quality_min, COLOR_WARNING)
    create_stat("Bøter ved manglende levering:", "%.2f NOK/kg" % _contract_data.penalty_per_kg, COLOR_DANGER)

    # Update Negotiation Slider
    _slider_price.min_value = _contract_data.price_per_kg * 0.8
    _slider_price.max_value = _contract_data.price_per_kg * 1.3
    _slider_price.value = _contract_data.price_per_kg # Default to offered price
    _current_negotiated_price = _slider_price.value
    _on_slider_price_value_changed(_slider_price.value) # Update label and chance

func create_stat(title: String, value: String, color: Color) -> void:
    var hbox = HBoxContainer.new()
    hbox.add_theme_constant_override("separation", 10)
    
    var title_label = Label.new()
    title_label.text = title
    LakskongenTheme.set_size(title_label, FONT_MD_SIZE)
    LakskongenTheme.set_text_color(title_label, COLOR_TEXT)
    hbox.add_child(title_label)

    var value_label = Label.new()
    value_label.text = value
    LakskongenTheme.set_size(value_label, FONT_MD_SIZE)
    LakskongenTheme.set_text_color(value_label, color)
    value_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
    value_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    hbox.add_child(value_label)
    
    _buyer_info_vbox.add_child(hbox)

func _on_slider_price_value_changed(value: float) -> void:
    _current_negotiated_price = value
    _slider_price_value_label.text = "%.2f NOK/kg" % _current_negotiated_price
    update_acceptance_chance_label()

func update_acceptance_chance_label() -> void:
    var offered_price = _contract_data.price_per_kg
    var din_pris = _current_negotiated_price
    
    # Formel: aksept_sjanse = max(5, 100 - (din_pris - tilbudt_pris) / tilbudt_pris * 200)
    var acceptance_chance = max(5.0, 100.0 - (din_pris - offered_price) / offered_price * 200.0)
    
    _acceptance_chance_label.text = "Sjanse for aksept: %d%%" % int(acceptance_chance)
    
    if acceptance_chance < 20:
        LakskongenTheme.set_text_color(_acceptance_chance_label, COLOR_DANGER)
    elif acceptance_chance < 50:
        LakskongenTheme.set_text_color(_acceptance_chance_label, COLOR_WARNING)
    else:
        LakskongenTheme.set_text_color(_acceptance_chance_label, COLOR_SUCCESS)


func _on_accept_button_pressed() -> void:
    contract_accepted.emit(_contract_data)
    _hide_dialog()

func _on_negotiate_button_pressed() -> void:
    var offered_price = _contract_data.price_per_kg
    var din_pris = _current_negotiated_price
    var acceptance_chance = max(5.0, 100.0 - (din_pris - offered_price) / offered_price * 200.0)
    
    if randf() * 100.0 < acceptance_chance:
        contract_negotiated.emit(_contract_data, _current_negotiated_price)
        _hide_dialog()
    else:
        contract_rejected.emit()
        _hide_dialog()

func _on_decline_button_pressed() -> void:
    contract_rejected.emit()
    _hide_dialog()

func _hide_dialog() -> void:
    visible = false

func _input(event: InputEvent) -> void:
    if event.is_action_pressed("ui_cancel"):
        get_viewport().set_input_as_handled()
        _on_close_requested()

func _on_close_requested() -> void:
    hide()


