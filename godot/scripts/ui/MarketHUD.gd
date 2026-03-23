
extends CanvasLayer

var simulation_core = null
var price_label: Label
var background_panel: ColorRect

func _ready():
    # Get SimulationCore
    simulation_core = get_node_or_null("/root/SimulationCore")
    if simulation_core == null:
        print("MarketHUD: SimulationCore not found at /root/SimulationCore")
        return

    # Create background panel
    background_panel = ColorRect.new()
    add_child(background_panel)

    # Set initial size and color for the background panel
    background_panel.set_anchors_preset(Control.PRESET_TOP_RIGHT)
    background_panel.set_offset_and_size(Vector2(-250, 0), Vector2(250, 70)) # Adjust size as needed
    background_panel.color = Color(0, 0, 0, 0.7) # Dark background with some transparency

    # Create price Label
    price_label = Label.new()
    background_panel.add_child(price_label)

    # Position the label within the background panel
    price_label.set_anchors_preset(Control.PRESET_FULL_RECT)
    price_label.set_align(HorizontalAlignment.ALIGN_RIGHT)
    price_label.set_valign(VerticalAlignment.ALIGN_CENTER)
    price_label.add_theme_font_size_override("font_size", 24) # Large text
    price_label.add_theme_constant_override("line_spacing", 0)
    price_label.set_modulate(Color.WHITE)
    price_label.set_text("SPOT PRICE: --.-- NOK/kg")


    # Connect to DayTicked signal
    simulation_core.DayTicked.connect(_on_day_ticked)

    # Initial update
    _on_day_ticked(0, "") # Call once to set initial price if available

func _on_day_ticked(_day: int, _log_summary: String):
    if simulation_core == null:
        return

    var current_price = simulation_core.Market.CurrentSpotPriceNoK
    var saturation_text = ""

    # Try to get MarketSaturation, ignore if not found (as per instruction)
    if simulation_core.Market.has_method("get_MarketSaturation"): # Check for getter or public property
        var saturation = simulation_core.Market.get_MarketSaturation() # Assuming a getter
        saturation_text = "\nSATURATION: %.2f" % saturation
    elif simulation_core.Market.has_method("get_MarketSaturation_no_getter"): # Just trying to simulate property access
        var saturation = simulation_core.Market.MarketSaturation
        saturation_text = "\nSATURATION: %.2f" % saturation

    price_label.set_text("SPOT PRICE: %.2f NOK/kg%s" % [current_price, saturation_text])

    # Update label color based on price
    if current_price < 60.0:
        price_label.set_modulate(Color.RED)
    elif current_price > 90.0:
        price_label.set_modulate(Color.GREEN)
    else:
        price_label.set_modulate(Color.WHITE)
