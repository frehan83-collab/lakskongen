# Market.gd
# Dynamic salmon price simulation
# - Spot price model with seasonality (Easter & Christmas highs, summer lows)
# - Weekly fluctuations with random noise
# - Size-based pricing (3-6kg premium)
# - Quality grading multipliers
# - Contract sales: lock price for X weeks at slight discount
# - Export markets adjustments using MarketData

extends Node
class_name Market

# Dependencies
@onready var MarketData = preload("res://game/scripts/MarketData.gd")

# Base price band (NOK/kg) -- used as baseline before seasonality and multipliers
var base_price_min: float = 50.0
var base_price_max: float = 140.0

# Random generator for weekly noise
var rng := RandomNumberGenerator.new()

# Active contracts: Array of Dictionaries {volume_kg, weeks_remaining, locked_price_per_kg}
var active_contracts: Array = []

func _init():
    rng.randomize()

# Get seasonal base price for given ISO week (1..52)
func get_seasonal_base_price(week: int) -> float:
    # Use MarketData seasonal curve (relative multiplier) and map to base range
    var curve = MarketData.get_seasonal_curve_for_market("EU")
    var idx = clamp(week, 1, 52) - 1
    var season_multiplier = curve[idx]

    # Map season multiplier (approx 0.6-1.5) into price band
    var avg_base = (base_price_min + base_price_max) / 2.0
    var span = (base_price_max - base_price_min) / 2.0
    # Apply multiplier around avg_base
    return clamp(avg_base * season_multiplier, base_price_min, base_price_max)

# Get weekly spot price (before quality/size/export adjustments)
func get_spot_price_for_week(week: int) -> float:
    var seasonal = get_seasonal_base_price(week)
    # Weekly noise +-5% with occasional higher volatility
    var noise = rng.randf_range(-0.05, 0.05)
    if rng.randf() < 0.05:
        # 5% chance of a larger swing (market event)
        noise += rng.randf_range(-0.12, 0.12)
    return max(0.0, seasonal * (1.0 + noise))

# Apply size multiplier based on MarketData.SIZE_PREMIERS
func apply_size_multiplier(price_per_kg: float, size_kg: float) -> float:
    for band in MarketData.SIZE_PREMIERS:
        if band.has("min") and band.has("max") and band.has("mult"):
            if size_kg >= band["min"] and size_kg <= band["max"]:
                return price_per_kg * band["mult"]
    return price_per_kg

# Apply quality multiplier
func apply_quality_multiplier(price_per_kg: float, quality: String) -> float:
    var q = quality.to_upper()
    if MarketData.QUALITY_MULTIPLIERS.has(q):
        return price_per_kg * MarketData.QUALITY_MULTIPLIERS[q]
    return price_per_kg

# Apply export market multiplier
func apply_market_multiplier(price_per_kg: float, market_name: String) -> float:
    var m = MarketData.get_market_by_name(market_name)
    if m.empty() or not m.has("price_multiplier"):
        return price_per_kg
    return price_per_kg * float(m.get("price_multiplier", 1.0))

# Calculate final price per kg for a sale (takes into account contracts)
# Params:
# - week: ISO week number (1..52)
# - size_kg: average size of fish being sold
# - quality: "SUPERIOR", "ORDINARY", "PRODUCTION"
# - export_market: market name ("EU","USA","Japan")
# - product_type: e.g. "HOG","FILLET"
func price_for_sale(week: int, size_kg: float, quality: String, export_market: String, product_type: String = "HOG") -> float:
    # First check if there are contracts available to fulfill this sale
    if _has_active_contract():
        # Use contract price; reduce contract volume accordingly externally (caller should call fulfill_contract)
        var contract_price = _peek_contract_price()
        # Apply product type processing margins
        var prod = MarketData.get_product_type(product_type)
        var proc_mult = prod.get("margin_multiplier", 1.0)
        var proc_cost = prod.get("processing_cost_per_kg", 0.0)
        return contract_price * proc_mult + proc_cost

    var spot = get_spot_price_for_week(week)
    var p = spot
    p = apply_size_multiplier(p, size_kg)
    p = apply_quality_multiplier(p, quality)
    p = apply_market_multiplier(p, export_market)

    # Apply product type margin/cost
    var prod = MarketData.get_product_type(product_type)
    if prod.size() > 0:
        var proc_mult = prod.get("margin_multiplier", 1.0)
        var proc_cost = prod.get("processing_cost_per_kg", 0.0)
        p = p * proc_mult + proc_cost

    return max(0.0, p)

# CONTRACTS: lock a price for X weeks at a slight discount to current spot (e.g., 2-6% discount)
func create_contract(weeks: int, volume_kg: float, locked_price_per_kg: float = -1.0, week_locked: int = 1) -> Dictionary:
    # If locked_price_per_kg not provided, determine from current spot (week_locked)
    var price = locked_price_per_kg
    if price < 0.0:
        price = get_spot_price_for_week(week_locked)
        # apply discount 2-6%
        var disc = rng.randf_range(0.02, 0.06)
        price *= (1.0 - disc)
    var c = {"volume_kg": volume_kg, "weeks_remaining": weeks, "locked_price_per_kg": price}
    active_contracts.append(c)
    return c

# Helper to see if any active contract exists
func _has_active_contract() -> bool:
    return active_contracts.size() > 0

# Peek the earliest contract price
func _peek_contract_price() -> float:
    if active_contracts.size() == 0:
        return -1.0
    return float(active_contracts[0].get("locked_price_per_kg", -1.0))

# Fulfill contract volume (call this when selling contracted volume)
# volume_kg: how much of sale fulfills contract
func fulfill_contract(volume_kg: float) -> float:
    # Return price per kg used from contract (or -1 if none)
    if active_contracts.size() == 0:
        return -1.0
    var c = active_contracts[0]
    if not c.has("volume_kg") or not c.has("locked_price_per_kg"):
        push_error("Contract missing required keys")
        active_contracts.remove_at(0)
        return -1.0
    var used = min(volume_kg, float(c["volume_kg"]))
    c["volume_kg"] = float(c["volume_kg"]) - used
    if c["volume_kg"] <= 0:
        active_contracts.remove_at(0)
    return float(c.get("locked_price_per_kg", -1.0))

# Advance week counters for contracts - call once per week tick
func advance_week():
    for i in range(active_contracts.size() - 1, -1, -1):
        if active_contracts[i].has("weeks_remaining"):
            active_contracts[i]["weeks_remaining"] -= 1
            if active_contracts[i]["weeks_remaining"] <= 0:
                active_contracts.remove_at(i)
        else:
            push_error("Contract missing 'weeks_remaining' key")
            active_contracts.remove_at(i)

# Utility: estimate split of production by export market using MarketData.EXPORT_MARKETS
func estimate_market_distribution(total_kg: float) -> Dictionary:
    var out = {}
    for m in MarketData.EXPORT_MARKETS:
        if m.has("name") and m.has("volume_share"):
            out[m["name"]] = total_kg * float(m["volume_share"])
        else:
            push_warning("Market entry missing 'name' or 'volume_share' - skipping")
    return out

# End of Market.gd
