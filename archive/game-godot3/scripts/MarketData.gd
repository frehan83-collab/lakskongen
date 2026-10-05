# MarketData.gd
# Static data resource for export markets, product types, and seasonal demand curves.
# Provides structured data used by Market.gd and Finance.gd.

extends Node
class_name MarketData

# Export market definitions: name, volume_share (0-1), price_multiplier, preferences
# volume_share is the fraction of total production typically sold to that market.
const EXPORT_MARKETS = [
    {
        "name": "EU",
        "volume_share": 0.65,
        "price_multiplier": 1.0,  # baseline
        "preferences": {"size_preference": "medium", "quality_focus": "ordinary_to_superior"}
    },
    {
        "name": "USA",
        "volume_share": 0.08,
        "price_multiplier": 1.15, # premium
        "preferences": {"size_preference": "larger", "quality_focus": "superior"}
    },
    {
        "name": "Japan",
        "volume_share": 0.05,
        "price_multiplier": 1.35, # ultra-premium
        "preferences": {"size_preference": "smaller", "quality_focus": "superior"}
    }
]

# Product types with processing requirements and typical margins
# margins expressed as fraction of raw HOG value (e.g. fillet may add +40% relative value)
const PRODUCT_TYPES = {
    "HOG": {
        "display_name": "Head-on gutted (HOG)",
        "processing_required": false,
        "margin_multiplier": 1.0
    },
    "FILLET": {
        "display_name": "Fillet",
        "processing_required": true,
        "processing_cost_per_kg": 10.0, # NOK/kg - indicative
        "margin_multiplier": 1.45
    },
    "SMOKED": {
        "display_name": "Smoked fillet",
        "processing_required": true,
        "processing_cost_per_kg": 25.0,
        "margin_multiplier": 1.75
    }
}

# Quality grade multipliers (applied to spot/base price)
const QUALITY_MULTIPLIERS = {
    "SUPERIOR": 1.0,
    "ORDINARY": 0.8,
    "PRODUCTION": 0.5
}

# Size premium table (kg -> multiplier). Define ranges and multipliers.
# Typical premium for 3-6kg fish.
const SIZE_PREMIERS = [
    {"min": 0.0, "max": 2.9, "mult": 0.9},
    {"min": 3.0, "max": 6.0, "mult": 1.12},
    {"min": 6.01, "max": 999.0, "mult": 1.0}
]

# Seasonality: we provide a small helper to compute demand multipliers per week (1..52)
# This keeps the data centralized here but generated at runtime for flexibility.

static func get_seasonal_curve_for_market(market_name: String) -> Array:
    # Returns an array[52] of multipliers (float) for each ISO week index (1..52)
    var curve = []
    # Base sinusoidal seasonality: peak in late winter (Easter/Christmas windows), trough in summer
    for week in range(1, 53):
        var w = float(week) / 52.0 * TAU  # TAU = 2*pi in Godot
        # base seasonal shape: cos curve so peaks at week=0
        var base = 0.1 * cos(w - PI*0.15) + 1.0
        # Add sharper spikes for Easter and Christmas depending on market
        var easter_boost = _easter_spike(week)
        var christmas_boost = _christmas_spike(week)
        var market_pref = 1.0
        if market_name == "Japan":
            market_pref = 1.05
        elif market_name == "USA":
            market_pref = 1.03
        curve.append(clamp(base + easter_boost + christmas_boost, 0.6, 1.5) * market_pref)
    return curve

static func _easter_spike(week: int) -> float:
    # Rough Easter around week 13-17; use gaussian-like bump
    var center = 15
    var width = 3.5
    var dist = (week - center) / width
    return 0.12 * exp(-dist * dist)

static func _christmas_spike(week: int) -> float:
    # Christmas at week ~51-52 and week 1 (wrap). Create two bumps.
    var c1 = 51
    var c2 = 1
    var width = 2.5
    var dist1 = (week - c1) / width
    var dist2 = (week - c2) / width
    return 0.18 * exp(-dist1 * dist1) + 0.08 * exp(-dist2 * dist2)

# Convenience lookups
static func get_market_by_name(name: String) -> Dictionary:
    for m in EXPORT_MARKETS:
        if m["name"] == name:
            return m
    return {}

static func get_product_type(key: String) -> Dictionary:
    if PRODUCT_TYPES.has(key):
        return PRODUCT_TYPES[key]
    return {}

# End of MarketData.gd
