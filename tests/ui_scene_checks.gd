# Simple non-interactive checks for Dashboard and MarketPanel scripts
# These are intended to run inside Godot's editor/command line (tool scripts).

tool
extends Node

func _ready():
    var ok = true
    # Try loading classes
    var dash = null
    var market = null
    var err = ""
    if Engine.has_singleton("GameManager"):
        # not required for checks
        pass
    # instantiate by class_name if available
    var DashClass = preload("res://game/ui/Dashboard.gd")
    var MarketClass = preload("res://game/ui/MarketPanel.gd")
    dash = DashClass.new()
    market = MarketClass.new()
    add_child(dash)
    add_child(market)
    # Call update functions with sample data
    var state = {
        "cash": 12345,
        "revenue": 1000,
        "profit": 200,
        "total_biomass": 500.0,
        "mab_limit": 1000.0,
        "avg_weight_kg": 1.2,
        "mortality_pct": 0.05,
        "fish_alive": 400,
        "sea_temp_c": 8.5,
        "oxygen_mg_l": 7.8,
        "lice_per_fish": 0.2,
        "active_diseases": [],
        "spot_price_nok_per_kg": 12.34,
        "time_speed": 1
    }
    dash.update_from_state(state)
    var history = [10.0,11.5,12.0,12.3]
    market.update_market(12.3, history, ["Contract A", "Contract B"])
    print("UI scene checks completed: Dashboard and MarketPanel instantiated and updated.")
    # Exit if running as a standalone test
    if OS.has_feature("standalone"):
        get_tree().quit()
